using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Explicit, single-slot persistence. This class never loads or saves on a lifecycle event.
public static class BaselineSaveStore
{
    public const string DefaultKeyPrefix = "LakeDomain.Baseline.Save";
    private const int EnvelopeVersion = 1;
    private const int MaxPayloadLength = 16384;
    private const int MaxEnvelopeLength = 32768;
    private const string PrimarySuffix = ".primary";
    private const string BackupSuffix = ".backup";

    [Serializable]
    private sealed class Envelope
    {
        public int formatVersion;
        public string payload;
        public string checksum;
    }

    [Serializable]
    private sealed class EnvelopeHeader { public int formatVersion; }

    [Serializable]
    private sealed class DataHeader { public int version; public int mapVersion; }

    private enum SlotState { Missing, Valid, Corrupt, Incompatible }

    private sealed class Slot
    {
        public SlotState state;
        public string raw;
        public BaselineSaveData data;
        public string message;
    }

    private sealed class PendingRecovery
    {
        public Slot primary;
        public Slot backup;
    }

    // Session-only safety boundary. An unconfirmed cache must not become a Continue
    // source or the recovery copy for a later save. This does not change stored formats.
    private static readonly Dictionary<string, PendingRecovery> pendingRecoveries =
        new Dictionary<string, PendingRecovery>(StringComparer.Ordinal);

    public static bool TryLoad(out BaselineSaveData data, out string message)
    {
        return TryLoad(DefaultKeyPrefix, out data, out message);
    }

    // Presence is independent of readability: replacing an incompatible/corrupt slot
    // still needs the same explicit user choice as replacing a loadable save.
    public static bool HasAnyData() { return HasAnyData(DefaultKeyPrefix); }

    public static bool HasAnyData(string keyPrefix)
    {
        CheckPrefix(keyPrefix);
        return pendingRecoveries.ContainsKey(keyPrefix) ||
            PlayerPrefs.HasKey(keyPrefix + PrimarySuffix) || PlayerPrefs.HasKey(keyPrefix + BackupSuffix);
    }

    public static bool TryLoad(string keyPrefix, out BaselineSaveData data, out string message)
    {
        data = null;
        try
        {
            CheckPrefix(keyPrefix);
            if (pendingRecoveries.ContainsKey(keyPrefix))
            {
                message = "Cannot continue: the previous save's session cache could not be restored. " +
                    "Retry saving or explicitly start a new journey. Persistent storage could not be confirmed.";
                return false;
            }
            Slot primary = ReadSlot(keyPrefix + PrimarySuffix);
            if (primary.state == SlotState.Incompatible)
            {
                message = "Cannot load this save: " + primary.message + " Existing data has been kept.";
                return false;
            }
            if (primary.state == SlotState.Valid)
            {
                data = primary.data;
                message = "Saved progress loaded." + primary.message;
                return true;
            }

            Slot backup = ReadSlot(keyPrefix + BackupSuffix);
            if (backup.state == SlotState.Valid)
            {
                data = backup.data;
                message = "Recovered progress from the backup; the main save is " +
                    (primary.state == SlotState.Missing ? "missing." : "unreadable.") + backup.message;
                return true;
            }
            message = backup.state == SlotState.Incompatible
                ? "Cannot load the backup: " + backup.message + " Existing data has been kept."
                : primary.state == SlotState.Missing && backup.state == SlotState.Missing
                    ? "No saved progress was found."
                    : "Saved progress could not be read, and no valid backup is available. " +
                        primary.message + " " + backup.message;
            return false;
        }
        catch (Exception exception)
        {
            message = "Could not read saved progress: " + exception.Message;
            return false;
        }
    }

    public static bool Save(BaselineSaveData data, out string message)
    {
        return Save(DefaultKeyPrefix, data, out message);
    }

    public static bool Save(string keyPrefix, BaselineSaveData data, out string message)
    {
        string primaryKey = null, backupKey = null;
        Slot primary = null, backup = null;
        bool changingSlots = false;
        try
        {
            CheckPrefix(keyPrefix);
            if (!TryPrepare(data, out BaselineSaveData snapshot, out message)) return false;
            string adjustment = message;
            string payload = JsonUtility.ToJson(snapshot);
            string candidate = JsonUtility.ToJson(new Envelope
            {
                formatVersion = EnvelopeVersion,
                payload = payload,
                checksum = Checksum(payload)
            });
            // Verify the exact serialized candidate before changing either stored value.
            if (Decode(candidate).state != SlotState.Valid)
            {
                message = "The save could not be validated. Existing data has been kept.";
                return false;
            }

            primaryKey = keyPrefix + PrimarySuffix;
            backupKey = keyPrefix + BackupSuffix;
            primary = ReadSlot(primaryKey);
            backup = ReadSlot(backupKey);
            if (primary.state == SlotState.Incompatible || backup.state == SlotState.Incompatible)
            {
                message = "Cannot replace an incompatible save: " +
                    (primary.state == SlotState.Incompatible ? primary.message : backup.message) +
                    " Existing data has been kept.";
                return false;
            }

            if (pendingRecoveries.TryGetValue(keyPrefix, out PendingRecovery pending))
            {
                // Future copies above still refuse replacement. Otherwise first restore
                // the held pre-failure raw values, so an uncertain candidate cannot rotate
                // into backup merely because the caller retries saving.
                string restoration = RestoreCachedSlots(primaryKey, pending.primary, backupKey, pending.backup, out bool restored);
                if (!restored)
                {
                    message = "Could not retry saving." + restoration + " Continue remains blocked for this slot.";
                    return false;
                }
                primary = pending.primary;
                backup = pending.backup;
                pendingRecoveries.Remove(keyPrefix);
            }

            // Preserve a known-valid recovery copy before replacing the primary. With
            // no readable prior copy, confirm the first primary before creating its backup.
            bool hasRecovery = primary.state == SlotState.Valid || backup.state == SlotState.Valid;
            string recovery = primary.state == SlotState.Valid ? primary.raw :
                backup.state == SlotState.Valid ? backup.raw : null;
            changingSlots = true;
            if (hasRecovery)
            {
                PlayerPrefs.SetString(backupKey, recovery);
                PlayerPrefs.Save();
            }
            PlayerPrefs.SetString(primaryKey, candidate);
            PlayerPrefs.Save();
            if (PlayerPrefs.GetString(primaryKey, "") != candidate)
                throw new InvalidOperationException("The written primary did not match the save candidate.");
            if (!hasRecovery)
            {
                // The primary flush returned and its cached candidate was confirmed.
                // A failed duplicate must not make callers roll back that accepted save.
                try
                {
                    PlayerPrefs.SetString(backupKey, candidate);
                    PlayerPrefs.Save();
                    if (PlayerPrefs.GetString(backupKey, "") != candidate)
                        throw new InvalidOperationException("The first backup did not match the save candidate.");
                }
                catch (Exception exception)
                {
                    string failure = RestoreCachedSlot(backupKey, backup);
                    message = "Progress saved." + adjustment + " The first backup could not be confirmed: " + exception.Message +
                        (failure == null ? " The previous backup value was restored in the session cache." :
                            " Backup cache restoration could not be confirmed: " + failure) +
                        " Persistent backup storage could not be confirmed.";
                    return true;
                }
            }
            message = "Progress saved." + adjustment;
            return true;
        }
        catch (Exception exception)
        {
            message = "The save could not be confirmed: " + exception.Message;
            if (changingSlots)
            {
                message += RestoreCachedSlots(primaryKey, primary, backupKey, backup, out bool restored);
                if (!restored)
                {
                    pendingRecoveries[keyPrefix] = new PendingRecovery { primary = primary, backup = backup };
                    message += " Continue is blocked for this slot until its cache is restored by a successful retry or the slot is explicitly cleared.";
                }
            }
            else message += " Existing data has been kept.";
            return false;
        }
    }

    // Only an explicit caller request removes data. Failures propagate to the caller.
    public static void Clear() { Clear(DefaultKeyPrefix); }

    public static void Clear(string keyPrefix)
    {
        CheckPrefix(keyPrefix);
        string primaryKey = keyPrefix + PrimarySuffix, backupKey = keyPrefix + BackupSuffix;
        Slot primary = ReadSlot(primaryKey), backup = ReadSlot(backupKey);
        try
        {
            PlayerPrefs.DeleteKey(primaryKey);
            PlayerPrefs.DeleteKey(backupKey);
            PlayerPrefs.Save();
            pendingRecoveries.Remove(keyPrefix);
        }
        catch (Exception exception)
        {
            string restoration = RestoreCachedSlots(primaryKey, primary, backupKey, backup, out bool restored);
            if (!restored && !pendingRecoveries.ContainsKey(keyPrefix))
                pendingRecoveries[keyPrefix] = new PendingRecovery { primary = primary, backup = backup };
            throw new InvalidOperationException("Saved progress could not be cleared: " + exception.Message + restoration +
                (pendingRecoveries.ContainsKey(keyPrefix) ? " Continue remains blocked for this slot." : ""), exception);
        }
    }

    // Restore only this slot's cached values. A failed flush can have an unknown durable
    // outcome; another flush here cannot guarantee disk/IndexedDB rollback and may fail too.
    private static string RestoreCachedSlots(string primaryKey, Slot primary, string backupKey, Slot backup, out bool restored)
    {
        string primaryFailure = RestoreCachedSlot(primaryKey, primary);
        string backupFailure = RestoreCachedSlot(backupKey, backup);
        restored = primaryFailure == null && backupFailure == null;
        return (restored
            ? " Previous slot values were restored in the session cache."
            : " Session cache restoration could not be fully confirmed." +
                (primaryFailure == null ? "" : " Primary: " + primaryFailure) +
                (backupFailure == null ? "" : " Backup: " + backupFailure)) +
            " Persistent storage could not be confirmed.";
    }

    private static string RestoreCachedSlot(string key, Slot previous)
    {
        try
        {
            if (previous.state == SlotState.Missing)
            {
                PlayerPrefs.DeleteKey(key);
                if (PlayerPrefs.HasKey(key)) return "The created key could not be removed.";
            }
            else
            {
                PlayerPrefs.SetString(key, previous.raw);
                if (!PlayerPrefs.HasKey(key) || PlayerPrefs.GetString(key, "") != previous.raw)
                    return "The previous raw value could not be restored.";
            }
            return null;
        }
        catch (Exception exception) { return exception.Message; }
    }

    private static void CheckPrefix(string keyPrefix)
    {
        if (string.IsNullOrWhiteSpace(keyPrefix))
            throw new ArgumentException("A nonempty save-key prefix is required.", nameof(keyPrefix));
    }

    private static Slot ReadSlot(string key)
    {
        return PlayerPrefs.HasKey(key)
            ? Decode(PlayerPrefs.GetString(key, ""))
            : new Slot { state = SlotState.Missing, message = "Save copy is missing." };
    }

    private static Slot Decode(string raw)
    {
        var result = new Slot { state = SlotState.Corrupt, raw = raw, message = "Save copy is invalid." };
        if (string.IsNullOrEmpty(raw)) return result;
        if (raw.Length > MaxEnvelopeLength)
        {
            result.state = SlotState.Incompatible;
            result.message = "Save size exceeds the supported limit.";
            return result;
        }
        try
        {
            // Read version-only headers first, so future field shapes cannot make a
            // newer save look like ordinary corruption and permit its replacement.
            EnvelopeHeader envelopeHeader = JsonUtility.FromJson<EnvelopeHeader>(raw);
            if (envelopeHeader == null) return result;
            if (envelopeHeader.formatVersion != 0 && envelopeHeader.formatVersion != EnvelopeVersion)
            {
                result.state = SlotState.Incompatible;
                result.message = "Storage format " + envelopeHeader.formatVersion + " is unsupported.";
                return result;
            }
            Envelope envelope = JsonUtility.FromJson<Envelope>(raw);
            if (envelope == null || envelope.formatVersion != EnvelopeVersion || string.IsNullOrEmpty(envelope.payload)) return result;
            if (envelope.payload.Length > MaxPayloadLength)
            {
                result.state = SlotState.Incompatible;
                result.message = "Save payload exceeds the supported limit.";
                return result;
            }
            DataHeader dataHeader = JsonUtility.FromJson<DataHeader>(envelope.payload);
            if (dataHeader == null) return result;
            if (dataHeader.version < 1 || dataHeader.version > BaselineSaveData.CurrentVersion)
            {
                result.state = SlotState.Incompatible;
                result.message = "Save version " + dataHeader.version + " is unsupported.";
                return result;
            }
            if (dataHeader.version >= 4 && dataHeader.mapVersion > OverworldV2Layout.CurrentMapVersion)
            {
                result.state = SlotState.Incompatible;
                result.message = "Map version " + dataHeader.mapVersion + " is unsupported.";
                return result;
            }
            if (!string.Equals(envelope.checksum, Checksum(envelope.payload), StringComparison.Ordinal))
            {
                result.message = "Save checksum does not match.";
                return result;
            }
            // Sentinels prevent missing required fields from becoming valid constructor
            // defaults, independent of JsonUtility's allocation behavior.
            var decoded = new BaselineSaveData
            {
                version = 0, mapVersion = -1, accepted = null, visited = null, completed = null, opened = null,
                trialRewardClaimed = null, taskRewardClaimed = null, tracked = -2, runeProgress = -1,
                taskRewardGrants = null,
                recallBeaconsUsed = -1,
                unequippedMask = -1,
                safePosition = new Vector3(float.NaN, float.NaN, float.NaN), safeYaw = float.NaN
            };
            JsonUtility.FromJsonOverwrite(envelope.payload, decoded);
            // v1 had claim flags but no reward receipts. Keep those flags: never invent
            // an item or reopen an old claim while upgrading the in-memory snapshot.
            bool migrated = decoded.version < BaselineSaveData.CurrentVersion;
            if (migrated)
            {
                if (decoded.version == 1) decoded.taskRewardGrants = new BaselineTaskReward[3];
                // These fields already existed in v3; never refund spent beacons
                // or re-equip removed items while moving that save to schema v4.
                if (decoded.version <= 2)
                {
                    decoded.recallBeaconsUsed = 0;
                    decoded.unequippedMask = 0;
                }
                decoded.mapVersion = 1;
                decoded.northBridgeOpen = false;
                decoded.openingCompleted = false;
                decoded.version = BaselineSaveData.CurrentVersion;
            }
            if (!TryPrepare(decoded, out result.data, out result.message)) return result;
            if (migrated) result.message += " Previous save version upgraded; existing progress retained.";
            result.state = SlotState.Valid;
            return result;
        }
        catch (Exception exception)
        {
            result.message = "Save copy is unreadable: " + exception.Message;
            return result;
        }
    }

    private static bool TryPrepare(BaselineSaveData source, out BaselineSaveData snapshot, out string message)
    {
        snapshot = null;
        message = "Save data is missing or has an invalid shape.";
        if (source == null) return false;
        if (source.version != BaselineSaveData.CurrentVersion)
        {
            message = "Save version " + source.version + " is unsupported.";
            return false;
        }
        if (source.mapVersion < 1 || source.mapVersion > OverworldV2Layout.CurrentMapVersion)
        {
            message = "Map version " + source.mapVersion + " is unsupported.";
            return false;
        }
        if (!HasLength(source.accepted, 3) || !HasLength(source.visited, 3) ||
            !HasLength(source.completed, 3) || !HasLength(source.opened, 3) ||
            !HasLength(source.trialRewardClaimed, 2) || !HasLength(source.taskRewardClaimed, 3) ||
            source.taskRewardGrants == null || source.taskRewardGrants.Length != 3) return false;
        if ((source.trialBDifficultyBonus != 0 && source.trialBDifficultyBonus != 10 && source.trialBDifficultyBonus != 20 && source.trialBDifficultyBonus != 30) || (!source.trialBCompleted && source.trialBDifficultyBonus != 0))
        { message = "Save data contains invalid Side B difficulty reward."; return false; }
        var grants = new BaselineTaskReward[3];
        for (int i = 0; i < grants.Length; i++)
        {
            var grant = source.taskRewardGrants[i];
            if (grant == null || grant.IsEmpty) continue; // Normalize Unity's inline null representation, including v1 claims.
            if (!grant.IsConfigured || !source.taskRewardClaimed[i] || !source.completed[i])
            {
                message = "Save data contains an invalid task reward receipt.";
                return false;
            }
            grants[i] = grant.Snapshot();
        }
        if (source.recallBeaconsUsed < 0 || source.recallBeaconsUsed > source.RecallBeaconsGranted ||
            source.unequippedMask < 0 || source.unequippedMask > 63)
        {
            message = "Save data contains an invalid consumable balance.";
            return false;
        }
        if (source.tracked < -1 || source.tracked > 2 || source.runeProgress < 0 || source.runeProgress > 3 ||
            source.puzzleSolved != (source.runeProgress == 3))
        {
            message = "Save data contains invalid task tracking or rune progress.";
            return false;
        }
        Vector3 position = source.safePosition;
        if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) || !Finite(source.safeYaw))
        {
            message = "Save data contains a non-finite player position or direction.";
            return false;
        }

        // Side-world coordinates and implausible heights must not become overworld spawns.
        // Terrain/collision suitability is still checked by the world when restoring play.
        float safeExtent = source.mapVersion >= 3 ? 109f : 84f;
        bool resetPosition = Mathf.Abs(position.x) > safeExtent || Mathf.Abs(position.z) > safeExtent ||
            position.y < -10f || position.y > 50f;
        snapshot = new BaselineSaveData
        {
            mapVersion = source.mapVersion,
            northBridgeOpen = source.northBridgeOpen,
            openingCompleted = source.openingCompleted,
            accepted = (bool[])source.accepted.Clone(),
            visited = (bool[])source.visited.Clone(),
            completed = (bool[])source.completed.Clone(),
            opened = (bool[])source.opened.Clone(),
            tracked = source.tracked,
            runeProgress = source.runeProgress,
            puzzleSolved = source.puzzleSolved,
            introCompleted = source.introCompleted,
            hasSword = source.hasSword,
            bossDefeated = source.bossDefeated,
            trialACompleted = source.trialACompleted,
            trialBCompleted = source.trialBCompleted,
            eclipsePresentedStage = Mathf.Clamp(source.eclipsePresentedStage, 0, 2),
            trialBDifficultyBonus = source.trialBDifficultyBonus,
            worldSurveyComplete = source.worldSurveyComplete,
            worldInscriptionRead = source.worldInscriptionRead,
            worldRelayComplete = source.worldRelayComplete,
            trialRewardClaimed = (bool[])source.trialRewardClaimed.Clone(),
            taskRewardClaimed = (bool[])source.taskRewardClaimed.Clone(),
            taskRewardGrants = grants,
            recallBeaconsUsed = source.recallBeaconsUsed,
            unequippedMask = source.unequippedMask,
            safePositionWasReset = resetPosition,
            safePosition = resetPosition ? BaselineSaveData.DefaultSafePosition : position,
            safeYaw = resetPosition ? 0f : Mathf.Repeat(source.safeYaw, 360f)
        };
        message = resetPosition ? " The saved position was reset to the overworld start." : "";
        return true;
    }

    private static bool HasLength(bool[] values, int length) { return values != null && values.Length == length; }
    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

    // Deterministic FNV-1a over UTF-16 code units detects accidental damage; this is not encryption.
    private static string Checksum(string value)
    {
        uint hash = 2166136261;
        unchecked { foreach (char character in value) hash = (hash ^ character) * 16777619; }
        return hash.ToString("X8", CultureInfo.InvariantCulture);
    }
}
