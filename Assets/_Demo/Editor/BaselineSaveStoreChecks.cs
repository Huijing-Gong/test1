using System;
using UnityEditor;
using UnityEngine;

// Uses only a unique temporary prefix. Never clears or writes the player's actual slot.
public static class BaselineSaveStoreChecks
{
    [MenuItem("Tools/Baseline/Check Save Storage")]
    public static void Run()
    {
        string prefix = BaselineSaveStore.DefaultKeyPrefix + ".Checks." + Guid.NewGuid().ToString("N");
        string primaryKey = prefix + ".primary";
        string backupKey = prefix + ".backup";
        try
        {
            var first = new BaselineSaveData
            {
                mapVersion = OverworldV2Layout.CurrentMapVersion,
                northBridgeOpen = true,
                openingCompleted = true,
                introCompleted = true,
                hasSword = true,
                bossDefeated = true,
                trialACompleted = true,
                trialBCompleted = true,
                tracked = 1,
                runeProgress = 3,
                puzzleSolved = true,
                safePosition = new Vector3(-20f, 2f, 30f),
                safeYaw = 270f
            };
            first.accepted[1] = true;
            first.visited[0] = true;
            first.completed[2] = true;
            first.opened[0] = true;
            first.trialRewardClaimed[1] = true;
            first.taskRewardClaimed[2] = true;
            first.taskRewardGrants[2] = new BaselineTaskReward
            {
                itemId = "qa.receipt", title = "QA reward", description = "Receipt persistence check", quantity = 2
            };
            Require(BaselineSaveStore.Save(prefix, first, out string message), message);
            Require(BaselineSaveStore.TryLoad(prefix, out BaselineSaveData loaded, out message), message);
            Require(JsonUtility.ToJson(first) == JsonUtility.ToJson(loaded), "Roundtrip did not preserve every field.");

            var second = new BaselineSaveData { safePosition = new Vector3(10f, 2f, -20f) };
            Require(BaselineSaveStore.Save(prefix, second, out message), message);
            string validPrimary = PlayerPrefs.GetString(primaryKey);
            string validBackup = PlayerPrefs.GetString(backupKey);
            PlayerPrefs.SetString(primaryKey, "{invalid json");
            Require(BaselineSaveStore.TryLoad(prefix, out loaded, out message), message);
            Require(JsonUtility.ToJson(loaded) == JsonUtility.ToJson(first), "Corrupt primary did not recover previous save.");
            Require(message.IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0, "Recovery was not reported.");
            Require(BaselineSaveStore.Save(prefix, second, out message), message);
            Require(PlayerPrefs.GetString(backupKey) == validBackup, "Corrupt primary replaced the good backup.");

            second.opened = new bool[2];
            Require(!BaselineSaveStore.Save(prefix, second, out message), "Invalid array length was accepted.");
            Require(PlayerPrefs.GetString(primaryKey) == validPrimary, "Rejected data changed the primary.");
            second.opened = new bool[3];
            second.safePosition = new Vector3(float.NaN, 2f, 0f);
            Require(!BaselineSaveStore.Save(prefix, second, out message), "NaN position was accepted.");
            Require(PlayerPrefs.GetString(primaryKey) == validPrimary, "NaN rejection changed the primary.");

            second.safePosition = new Vector3(1000f, 2f, 0f);
            Require(BaselineSaveStore.Save(prefix, second, out message), message);
            Require(BaselineSaveStore.TryLoad(prefix, out loaded, out message), message);
            Require(loaded.safePosition == BaselineSaveData.DefaultSafePosition, "Side-world position did not use the safe start.");

            // Change the payload version without updating its checksum: compatibility must
            // be recognized before corruption recovery can roll a newer save back.
            string future = validPrimary.Replace("\\\"version\\\":" + BaselineSaveData.CurrentVersion, "\\\"version\\\":999");
            Require(future != validPrimary, "Could not construct the future-version fixture.");
            PlayerPrefs.SetString(primaryKey, future);
            Require(!BaselineSaveStore.TryLoad(prefix, out loaded, out message), "An unknown version was loaded.");
            Require(message.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0, "Unknown version was not explained.");
            Require(!BaselineSaveStore.Save(prefix, new BaselineSaveData(), out message), "An unknown version was overwritten.");
            Require(PlayerPrefs.GetString(primaryKey) == future, "The future-version save was modified.");

            PlayerPrefs.SetString(primaryKey, validPrimary);
            PlayerPrefs.SetString(backupKey, future);
            Require(!BaselineSaveStore.Save(prefix, new BaselineSaveData(), out message), "An unknown backup version was overwritten.");
            Require(PlayerPrefs.GetString(backupKey) == future, "The future-version backup was modified.");

            string futureFormat = validPrimary.Replace("\"formatVersion\":1", "\"formatVersion\":999");
            Require(futureFormat != validPrimary, "Could not construct the future-format fixture.");
            PlayerPrefs.SetString(primaryKey, futureFormat);
            PlayerPrefs.SetString(backupKey, validBackup);
            Require(!BaselineSaveStore.TryLoad(prefix, out loaded, out message), "An unknown storage format was loaded.");
            Require(!BaselineSaveStore.Save(prefix, new BaselineSaveData(), out message), "An unknown storage format was overwritten.");
            Require(PlayerPrefs.GetString(primaryKey) == futureFormat, "The future-format save was modified.");
            BaselineSaveStore.Clear(prefix);
            string legacy = System.IO.File.ReadAllText("Assets/_Demo/Editor/BaselineSaveV1Fixture.json");
            PlayerPrefs.SetString(primaryKey, legacy);
            Require(BaselineSaveStore.TryLoad(prefix, out loaded, out message), message);
            Require(loaded.version == BaselineSaveData.CurrentVersion && loaded.completed[0] &&
                !loaded.taskRewardClaimed[0] && loaded.taskRewardClaimed[2] && loaded.taskRewardGrants[2] == null,
                "v1 migration changed task/claim identities or invented a reward.");
            Require(PlayerPrefs.GetString(primaryKey) == legacy, "Reading the v1 fixture rewrote stored data.");
            Require(BaselineSaveStore.Save(prefix, loaded, out message), message);
            Require(PlayerPrefs.GetString(backupKey) == legacy, "Upgrading did not retain the original v1 backup.");
            Require(BaselineSaveStore.TryLoad(prefix, out loaded, out message), message);
            Require(loaded.completed[0] && loaded.taskRewardClaimed[2], "Upgraded save did not preserve legacy progress.");

            loaded.taskRewardClaimed[0] = true;
            loaded.taskRewardGrants[0] = new BaselineTaskReward { itemId = "qa.snapshot", title = "Original", quantity = 3 };
            Require(BaselineSaveStore.Save(prefix, loaded, out message), message);
            string receiptSave = PlayerPrefs.GetString(primaryKey);
            loaded.taskRewardGrants[0].title = "Changed after save";
            Require(BaselineSaveStore.TryLoad(prefix, out var receiptLoaded, out message), message);
            Require(receiptLoaded.taskRewardGrants[0].title == "Original" && receiptLoaded.taskRewardGrants[0].quantity == 3,
                "Saved receipt changed with its source definition.");
            loaded.taskRewardGrants[0].quantity = 0;
            Require(!BaselineSaveStore.Save(prefix, loaded, out message), "Zero quantity receipt was accepted.");
            Require(PlayerPrefs.GetString(primaryKey) == receiptSave, "Rejected receipt changed saved data.");
            loaded.taskRewardGrants = null;
            Require(!BaselineSaveStore.Save(prefix, loaded, out message), "Missing v2 receipt array was accepted.");

            var supplies = new BaselineSaveData();
            supplies.completed[0] = supplies.taskRewardClaimed[0] = true;
            supplies.taskRewardGrants[0] = new BaselineTaskReward { itemId = BaselineSaveData.RecallItemId, title = "Recall beacon", quantity = 2 };
            supplies.recallBeaconsUsed = 1;
            supplies.unequippedMask = 16;
            Require(BaselineSaveStore.Save(prefix, supplies, out message), message);
            Require(BaselineSaveStore.TryLoad(prefix, out loaded, out message) && loaded.RecallBeaconsRemaining == 1 && loaded.unequippedMask == 16,
                "Used consumable was replenished on reload.");
            string supplySave = PlayerPrefs.GetString(primaryKey);
            supplies.recallBeaconsUsed = 3;
            Require(!BaselineSaveStore.Save(prefix, supplies, out message), "Overspent consumables were accepted.");
            supplies.recallBeaconsUsed = -1;
            Require(!BaselineSaveStore.Save(prefix, supplies, out message), "Negative consumption was accepted.");
            Require(PlayerPrefs.GetString(primaryKey) == supplySave, "Invalid consumption changed saved data.");

            // Model a v2 envelope with a real receipt but no v3 consumption field.
            supplies.recallBeaconsUsed = 0;
            string payload = JsonUtility.ToJson(supplies).Replace("\"version\":" + BaselineSaveData.CurrentVersion, "\"version\":2")
                .Replace(",\"recallBeaconsUsed\":0", "").Replace(",\"unequippedMask\":16", "")
                .Replace(",\"mapVersion\":1", "").Replace(",\"northBridgeOpen\":false", "").Replace(",\"openingCompleted\":false", "");
            uint checksum = 2166136261;
            unchecked { foreach (char c in payload) checksum = (checksum ^ c) * 16777619; }
            string v2 = JsonUtility.ToJson(new LegacyEnvelope { formatVersion = 1, payload = payload, checksum = checksum.ToString("X8") });
            BaselineSaveStore.Clear(prefix); PlayerPrefs.SetString(primaryKey, v2);
            Require(BaselineSaveStore.TryLoad(prefix, out loaded, out message) && loaded.version == BaselineSaveData.CurrentVersion &&
                loaded.RecallBeaconsRemaining == 2 && loaded.taskRewardClaimed[0] && loaded.unequippedMask == 0, "v2 migration lost its receipt or claim.");
            Require(PlayerPrefs.GetString(primaryKey) == v2, "Reading v2 rewrote original data.");
            CheckMapMigration(prefix, first);
            Debug.Log("Baseline save checks passed: backup, validation, safe position, future protection, v1/v2/v3 migration, V2 map policy, receipts and consumable spending.");
        }
        finally
        {
            BaselineSaveStore.Clear(prefix);
        }
    }

    static void CheckMapMigration(string prefix, BaselineSaveData template)
    {
        string primaryKey = prefix + ".primary", backupKey = prefix + ".backup";
        var source = JsonUtility.FromJson<BaselineSaveData>(JsonUtility.ToJson(template));
        source.completed[0] = source.taskRewardClaimed[0] = true;
        source.taskRewardGrants[0] = new BaselineTaskReward
        { itemId = BaselineSaveData.RecallItemId, title = "Legacy recall", quantity = 3 };
        source.recallBeaconsUsed = 2;
        source.unequippedMask = 19;
        source.mapVersion = 1; source.northBridgeOpen = source.openingCompleted = false;
        string payload = JsonUtility.ToJson(source)
            .Replace("\"version\":" + BaselineSaveData.CurrentVersion, "\"version\":3")
            .Replace(",\"mapVersion\":1", "")
            .Replace(",\"northBridgeOpen\":false", "")
            .Replace(",\"openingCompleted\":false", "");
        Require(payload.Contains("\"version\":3") && !payload.Contains("mapVersion"), "v3 fixture was not actually legacy-shaped.");
        uint checksum = 2166136261;
        unchecked { foreach (char c in payload) checksum = (checksum ^ c) * 16777619; }
        string legacy = JsonUtility.ToJson(new LegacyEnvelope { formatVersion = 1, payload = payload, checksum = checksum.ToString("X8") });
        BaselineSaveStore.Clear(prefix); PlayerPrefs.SetString(primaryKey, legacy);
        Require(BaselineSaveStore.TryLoad(prefix, out var loaded, out string message), message);
        Require(JsonUtility.ToJson(loaded) == JsonUtility.ToJson(source), "v3 schema upgrade changed identities, receipts, spending or equipment.");
        Require(PlayerPrefs.GetString(primaryKey) == legacy && !PlayerPrefs.HasKey(backupKey), "Read-only v3 upgrade wrote a slot.");

        var camp = new Vector3(2f, 3.7f, -70f);
        Require(PrepareMap(loaded, camp, true), "Old map did not report a migration.");
        source.mapVersion = OverworldV2Layout.CurrentMapVersion;
        source.openingCompleted = source.northBridgeOpen = true;
        source.safePosition = camp; source.safeYaw = 0;
        Require(JsonUtility.ToJson(loaded) == JsonUtility.ToJson(source), "Map migration changed fields beyond the approved landing/tutorial/bridge fields.");
        Require(!PrepareMap(loaded, camp, true), "A second Continue was not idempotent.");
        Require(BaselineSaveStore.Save(prefix, loaded, out message), message);
        Require(PlayerPrefs.GetString(backupKey) == legacy, "Migrating V2 did not retain the untouched v3 backup.");
        Require(BaselineSaveStore.TryLoad(prefix, out loaded, out message), message);
        Require(JsonUtility.ToJson(loaded) == JsonUtility.ToJson(source), "V2 migration did not survive saving and reloading.");

        var arrival = new Vector3(0f, 2.5f, -75f);
        var fresh = new BaselineSaveData();
        Require(PrepareMap(fresh, arrival, false), "New journey did not adopt the V2 map.");
        Require(fresh.mapVersion == OverworldV2Layout.CurrentMapVersion && fresh.safePosition == arrival && fresh.safePosition != camp &&
            !fresh.northBridgeOpen && !fresh.openingCompleted, "New journey must start at V2 arrival with its bridge closed.");
        fresh.safePosition = new Vector3(-40f, 2f, 8f); fresh.safeYaw = 123f;
        Require(PrepareMap(fresh, camp, true), "Continue of an unfinished introduction did not mark it skipped.");
        Require(fresh.northBridgeOpen && fresh.openingCompleted && fresh.safePosition == new Vector3(-40f, 2f, 8f) &&
            fresh.safeYaw == 123f, "Continue moved a current-map landing or failed to open the bridge.");

        // A damaged but finite map-2 landing must use the scene's camp, even
        // when the store's historical normalization point happens to have ground.
        fresh.safePosition = new Vector3(1000f, 2f, 0f);
        string damagedPayload = JsonUtility.ToJson(fresh);
        checksum = 2166136261;
        unchecked { foreach (char c in damagedPayload) checksum = (checksum ^ c) * 16777619; }
        string damaged = JsonUtility.ToJson(new LegacyEnvelope { formatVersion = 1, payload = damagedPayload, checksum = checksum.ToString("X8") });
        string valid = PlayerPrefs.GetString(primaryKey);
        PlayerPrefs.SetString(primaryKey, damaged);
        Require(BaselineSaveStore.TryLoad(prefix, out loaded, out message), message);
        Require(PrepareMap(loaded, camp, true) && loaded.safePosition == camp,
            "Current-map invalid landing did not resolve to the V2 camp.");
        PlayerPrefs.SetString(primaryKey, valid);
        string futureMap = valid.Replace("\\\"mapVersion\\\":" + OverworldV2Layout.CurrentMapVersion, "\\\"mapVersion\\\":999");
        Require(futureMap != valid, "Could not construct a future map fixture.");
        PlayerPrefs.SetString(primaryKey, futureMap);
        Require(!BaselineSaveStore.TryLoad(prefix, out loaded, out message), "A future map loaded via backup fallback.");
        Require(!BaselineSaveStore.Save(prefix, new BaselineSaveData(), out message), "A future map was overwritten.");
        Require(PlayerPrefs.GetString(primaryKey) == futureMap, "Future map protection changed the stored slot.");
    }

    static bool PrepareMap(BaselineSaveData data, Vector3 camp, bool continuing)
    {
        var method = typeof(BaselineFlow).GetMethod("PrepareMapProgress", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Require(method != null, "Missing pure map-migration policy.");
        return (bool)method.Invoke(null, new object[] { data, camp, continuing });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Baseline save check failed: " + message);
    }
    [Serializable]
    private sealed class LegacyEnvelope { public int formatVersion; public string payload; public string checksum; }
}
