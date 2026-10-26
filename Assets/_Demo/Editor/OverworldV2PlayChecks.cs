using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit API integration checks in a saved scene copy. Never enters Play itself.
// No input injection, physical traversal, side-world or water-input claims.
[InitializeOnLoad]
public static class OverworldV2PlayChecks
{
    public const string ValidationScene = "Assets/_Demo/Scenes/OverworldV2Validation.unity";
    const string MainScene = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    const string Report = "ToolReports/MapV2Migration/";
    const string State = "QA.OverworldV2Play.";
    static IEnumerator routine;
    static double resumeAt, deadline;
    static int resumeFrame;
    static BaselineFlow flow;
    static BaselineWorld world;
    static BaselineCampTutorial tutorial;
    static OverworldV2Layout layout;
    static PlayerMovement player;
    static DialogueUI dialogue;
    public static bool Running => routine != null;
    public static string Status => SessionState.GetString(State + "result", "Not prepared");

    static OverworldV2PlayChecks()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }

    public static void Prepare()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Running || SessionState.GetBool(State + "prepared", false))
            throw new InvalidOperationException("Requires Edit mode and no existing prepared V2 check.");
        var original = SceneManager.GetActiveScene();
        if (original.path != MainScene || original.isDirty || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Save the main scene first; do not discard dirty/additive work.");
        var originalFlow = SceneFlow(original);
        if (!originalFlow || !originalFlow.GetComponent<OverworldV2Layout>())
            throw new InvalidOperationException("The clean main scene must contain the migrated V2 host.");
        string qaPrefix = "QA.OverworldV2." + Guid.NewGuid().ToString("N");
        string realPrefix = originalFlow.saveKeyPrefix;
        if (string.IsNullOrWhiteSpace(realPrefix) || qaPrefix == realPrefix)
            throw new InvalidOperationException("Cannot establish an isolated save prefix.");
        Directory.CreateDirectory(Report);
        File.WriteAllText(Report + "runtime-checks.txt", "Method: copied scene; public gameplay APIs and controlled position restoration. No E/WASD, swimming or side-world traversal tests.\n");
        SessionState.SetString(State + "prefix", qaPrefix);
        SessionState.SetString(State + "realPrefix", realPrefix);
        SessionState.SetString(State + "primaryHash", SlotHash(realPrefix + ".primary"));
        SessionState.SetString(State + "backupHash", SlotHash(realPrefix + ".backup"));
        SessionState.SetString(State + "mainHash", FileHash(MainScene));
        SessionState.SetString(State + "startOverride", EditorSceneManager.playModeStartScene ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "");
        SessionState.SetFloat(State + "timeScale", Time.timeScale);
        SessionState.SetInt(State + "cursorLock", (int)Cursor.lockState);
        SessionState.SetBool(State + "cursorVisible", Cursor.visible);
        SessionState.SetBool(State + "runInBackground", Application.runInBackground);
        SessionState.SetBool(State + "finished", false);
        SessionState.SetBool(State + "failed", false);
        Scene copy = default;
        try
        {
            if (!EditorSceneManager.SaveScene(original, ValidationScene, true))
                throw new InvalidOperationException("Could not save the validation copy.");
            if (original.path != MainScene || original.isDirty)
                throw new InvalidOperationException("Save-as-copy unexpectedly changed the main scene state.");
            copy = EditorSceneManager.OpenScene(ValidationScene, OpenSceneMode.Additive);
            var copyFlow = SceneFlow(copy);
            if (!copyFlow) throw new InvalidOperationException("The copied scene has no unique Flow.");
            copyFlow.saveKeyPrefix = qaPrefix;
            EditorUtility.SetDirty(copyFlow);
            EditorSceneManager.MarkSceneDirty(copy);
            if (!EditorSceneManager.SaveScene(copy)) throw new InvalidOperationException("Could not save QA prefix in the copied scene.");
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            if (copy.IsValid() && copy.isLoaded) EditorSceneManager.CloseScene(copy, true);
        }
        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ValidationScene);
        if (!sceneAsset) throw new InvalidOperationException("Validation scene was not imported.");
        CheckRealSlots("after Prepare");
        Check(FileHash(MainScene) == SessionState.GetString(State + "mainHash", ""), "Prepare preserved the main scene file");
        EditorSceneManager.playModeStartScene = sceneAsset;
        SessionState.SetBool(State + "prepared", true);
        SessionState.SetString(State + "result", "Prepared QA scene; enter Play then call Begin");
        Record("PREPARED " + ValidationScene + " with isolated prefix " + qaPrefix);
    }

    public static void Begin()
    {
        if (!EditorApplication.isPlaying || Running || !SessionState.GetBool(State + "prepared", false) ||
            SceneManager.GetActiveScene().path != ValidationScene || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Begin requires only the prepared validation scene in Play.");
        flow = SceneFlow(SceneManager.GetActiveScene());
        if (!flow || flow.Started || flow.saveKeyPrefix != QAPrefix())
            throw new InvalidOperationException("Unexpected Flow/session prefix; no gameplay calls were made.");
        world = flow.GetComponent<BaselineWorld>(); tutorial = flow.Tutorial;
        layout = flow.GetComponent<OverworldV2Layout>();
        player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null;
        dialogue = Object.FindFirstObjectByType<DialogueUI>();
        if (!world || !layout || !tutorial || !player || !dialogue || !GameSession.Instance)
            throw new InvalidOperationException("Wait for Start, or repair the incomplete copied host.");
        CheckRealSlots("before Begin");
        Application.runInBackground = true;
        SessionState.SetString(State + "result", "Running copied-scene API checks");
        deadline = EditorApplication.timeSinceStartup + 150;
        resumeAt = 0; resumeFrame = Time.frameCount;
        routine = Run(); EditorApplication.update += Tick;
    }

    static IEnumerator Run()
    {
        flow.NewGame(); yield return .3f;
        Check(flow.Started && tutorial.Active && tutorial.CardOpen, "Fresh journey starts its existing tutorial/card");
        Check(PlanarDistance(player.transform.position, layout.ArrivalPosition) < .8f &&
            Mathf.Abs(player.transform.position.y - layout.ArrivalPosition.y) < 1.5f,
            "Fresh player is at Arrival, not Camp: " + player.transform.position);
        Check(!layout.BridgeOpen && !flow.Progress.northBridgeOpen && !flow.Progress.openingCompleted,
            "Fresh V2 journey keeps the north bridge closed");
        Check(!GameSession.Instance.HasSword && !world.accepted[0] && world.Tracked == -1,
            "Fresh tutorial did not grant sword, accept a task or select tracking");
        tutorial.Cancel(); yield return .2f;
        Check(!tutorial.Active && !layout.BridgeOpen && !flow.Progress.openingCompleted,
            "Cancel alone does not complete the introduction or open the bridge");
        tutorial.Begin(); yield return .2f;
        tutorial.Skip(); yield return .3f;
        Check(!tutorial.Active && layout.BridgeOpen && flow.Progress.northBridgeOpen && flow.Progress.openingCompleted,
            "Explicit Skip completes the gate policy and opens the north bridge");
        Check(Time.timeScale > 0 && !player.IsGameplayInputLocked, "Skip releases the tutorial pause/input lock");
        Check(BaselineSaveStore.TryLoad(QAPrefix(), out var skipped, out string message) && skipped.northBridgeOpen && skipped.openingCompleted,
            "Skip gate state was saved to the QA slot: " + message);

        // Calls the actual scene NPC completion event; this is not an E-key test.
        var guide = Item(BaselineInteraction.ActionKind.AcceptTask, 0);
        guide.GetComponent<NPCDialogue>().NotifyDialogueCompleted(dialogue); yield return .2f;
        Check(GameSession.Instance.HasSword && GameSession.Instance.IntroDialogueCompleted && world.accepted[0],
            "Guide completion event retains its real sword/intro/task-0 listeners");
        for (int i = 1; i < 3; i++) world.Interact(Item(BaselineInteraction.ActionKind.AcceptTask, i));
        Check(All(world.accepted) && None(world.completed) && world.Tracked == -1,
            "All three existing task identities accept independently without automatic tracking/completion");
        world.Track(1); yield return .15f;
        Check(world.Tracked == 1, "Public manual Track selects task index 1");
        Check(flow.SaveNow() && flow.ContinueGame(), "Tracked-task save and Continue APIs succeeded"); yield return .3f;
        Check(world.Tracked == 1 && All(world.accepted) && !tutorial.Active && layout.BridgeOpen,
            "Continue restores selected tracking and skips replaying the tutorial");

        world.Interact(Item(BaselineInteraction.ActionKind.Chest, 2));
        Check(!world.opened[2] && world.RuneProgress == 0, "Puzzle chest rejects collection before its rune sequence");
        for (int i = 0; i < 2; i++)
        {
            world.Interact(Item(BaselineInteraction.ActionKind.Chest, i)); yield return .2f;
            Check(world.opened[i] && None(world.completed), "Hidden chest " + i + " opens independently of task completion");
            if (flow.rewards && flow.rewards.IsOpen) flow.rewards.Dismiss(); yield return .2f;
        }
        world.Interact(Item(BaselineInteraction.ActionKind.Rune, 0));
        Check(world.RuneProgress == 0 && !world.PuzzleSolved, "Wrong first symbol does not unlock the puzzle");
        int[] order = { 2, 0, 1 };
        for (int i = 0; i < order.Length; i++)
        {
            world.Interact(Item(BaselineInteraction.ActionKind.Rune, order[i]));
            Check(world.RuneProgress == i + 1, "SUN-WAVE-LEAF retains index " + order[i] + " at step " + (i + 1));
        }
        Check(world.PuzzleSolved, "Existing three-symbol sequence unlocks its seal");
        world.Interact(Item(BaselineInteraction.ActionKind.Chest, 2)); yield return .2f;
        Check(All(world.opened), "Exactly the three existing chest indices can be collected");
        if (flow.rewards && flow.rewards.IsOpen) flow.rewards.Dismiss(); yield return .3f;
        for (int i = 0; i < 3; i++)
        {
            world.Interact(Item(BaselineInteraction.ActionKind.Objective, i));
            Check(world.visited[i] && world.completed[i], "Existing objective " + i + " completes its accepted optional task");
        }
        Check(world.Tracked == -1, "Completing the tracked task clears tracking by the existing rule");
        Check(flow.SaveNow(), "Completed task/chest/puzzle state saved");
        Check(BaselineSaveStore.TryLoad(QAPrefix(), out var beforeReload, out message), "QA persistence is readable: " + message);
        string fullExpected = JsonUtility.ToJson(beforeReload);
        Check(flow.ContinueGame(), "Full-progress Continue API succeeded"); yield return .3f;
        Check(JsonUtility.ToJson(flow.Progress) == fullExpected && All(world.completed) && All(world.opened) && world.PuzzleSolved &&
            GameSession.Instance.HasSword && !tutorial.Active, "Full saved progress survives Continue unchanged");

        var legacy = LegacyProgress();
        string rawLegacy = LegacyEnvelope(legacy);
        // Only this run's QA primary is replaced. Flow keeps its QA prefix until Play exits.
        PlayerPrefs.SetString(QAPrefix() + ".primary", rawLegacy); PlayerPrefs.Save();
        Check(flow.ContinueGame(), "Legacy v3 fixture loads through the real Continue path"); yield return .3f;
        legacy.mapVersion = OverworldV2Layout.CurrentMapVersion;
        legacy.northBridgeOpen = legacy.openingCompleted = true;
        legacy.safePosition = layout.CampPosition; legacy.safeYaw = 0;
        Check(JsonUtility.ToJson(flow.Progress) == JsonUtility.ToJson(legacy),
            "Legacy Continue changes only map/tutorial/bridge/landing fields; receipts, consumption, equipment and identities survive");
        Check(flow.Progress.recallBeaconsUsed == 1 && flow.Progress.RecallBeaconsRemaining == 2 && flow.Progress.unequippedMask == 19,
            "Legacy consumable spend and removed equipment were not reset");
        Check(PlanarDistance(player.transform.position, layout.CampPosition) < .8f &&
            Mathf.Abs(player.transform.position.y - layout.CampPosition.y) < 1.5f && layout.BridgeOpen && !tutorial.Active,
            "Old-map Continue arrives safely at Camp with bridge open and no tutorial replay");
        Check(PlayerPrefs.GetString(QAPrefix() + ".backup", "") == rawLegacy,
            "First V2 migration save keeps the untouched v3 envelope as its recovery copy");
        CheckRealSlots("after API checks");
        SessionState.SetBool(State + "finished", true);
        SessionState.SetString(State + "result", "API checks completed; awaiting Edit-mode restoration/hash checks");
        Record("PASS API suite completed. No continuous movement, E-key interaction, water traversal, side-world or camera-playtest claim.");
    }

    static BaselineSaveData LegacyProgress()
    {
        var data = new BaselineSaveData
        {
            introCompleted = true, hasSword = true, bossDefeated = false,
            trialACompleted = true, trialBCompleted = true, tracked = 1,
            runeProgress = 1, puzzleSolved = false, recallBeaconsUsed = 1, unequippedMask = 19,
            safePosition = new Vector3(-18, 2, -35), safeYaw = 225
        };
        data.accepted[0] = data.accepted[1] = data.accepted[2] = true;
        data.visited[0] = data.completed[0] = true;
        data.opened[0] = data.opened[1] = true;
        data.trialRewardClaimed[0] = data.trialRewardClaimed[1] = true;
        data.taskRewardClaimed[0] = true;
        data.taskRewardGrants[0] = new BaselineTaskReward
        { itemId = BaselineSaveData.RecallItemId, title = "QA legacy beacon", description = "Temporary test receipt", quantity = 3 };
        return data;
    }
    [Serializable] sealed class Envelope { public int formatVersion; public string payload; public string checksum; }
    static string LegacyEnvelope(BaselineSaveData data)
    {
        string payload = JsonUtility.ToJson(data).Replace("\"version\":" + BaselineSaveData.CurrentVersion, "\"version\":3")
            .Replace(",\"mapVersion\":1", "").Replace(",\"northBridgeOpen\":false", "").Replace(",\"openingCompleted\":false", "");
        uint checksum = 2166136261;
        unchecked { foreach (char c in payload) checksum = (checksum ^ c) * 16777619; }
        return JsonUtility.ToJson(new Envelope { formatVersion = 1, payload = payload, checksum = checksum.ToString("X8", CultureInfo.InvariantCulture) });
    }

    static BaselineInteraction Item(BaselineInteraction.ActionKind action, int index)
    {
        BaselineInteraction result = null;
        foreach (var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (item.gameObject.scene == world.gameObject.scene && item.action == action && item.index == index)
            {
                if (result) throw new InvalidOperationException("Duplicate interaction " + action + ":" + index);
                result = item;
            }
        if (!result || !result.isActiveAndEnabled) throw new InvalidOperationException("Missing/inactive interaction " + action + ":" + index);
        return result;
    }
    static BaselineFlow SceneFlow(Scene scene)
    {
        BaselineFlow found = null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<BaselineFlow>(true))
            {
                if (found) throw new InvalidOperationException("Scene contains multiple Flow hosts.");
                found = candidate;
            }
        return found;
    }
    static bool All(bool[] flags) { foreach (bool value in flags) if (!value) return false; return true; }
    static bool None(bool[] flags) { foreach (bool value in flags) if (value) return false; return true; }
    static float PlanarDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    static string QAPrefix()
    {
        string prefix = SessionState.GetString(State + "prefix", "");
        if (!prefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) || prefix == SessionState.GetString(State + "realPrefix", ""))
            throw new InvalidOperationException("Missing/unsafe QA prefix.");
        return prefix;
    }
    static void Check(bool condition, string message)
    {
        Record((condition ? "PASS " : "FAIL ") + message);
        if (!condition)
        {
            SessionState.SetBool(State + "failed", true);
            throw new InvalidOperationException(message);
        }
    }
    static void Record(string message)
    {
        Directory.CreateDirectory(Report);
        File.AppendAllText(Report + "runtime-checks.txt", DateTime.UtcNow.ToString("O") + " " + message + "\n");
    }
    static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static string SlotHash(string key) => PlayerPrefs.HasKey(key) ? Hash(Encoding.UTF8.GetBytes(PlayerPrefs.GetString(key, ""))) : "missing";
    static string FileHash(string path) => Hash(File.ReadAllBytes(path));
    static void CheckRealSlots(string stage)
    {
        string prefix = SessionState.GetString(State + "realPrefix", "");
        string primary = SlotHash(prefix + ".primary"), backup = SlotHash(prefix + ".backup");
        Record("HASH " + stage + " primary=" + primary + " backup=" + backup);
        Check(primary == SessionState.GetString(State + "primaryHash", "") &&
            backup == SessionState.GetString(State + "backupHash", ""), "Real save primary/backup hashes unchanged " + stage);
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || !flow) { Stop(); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (flow.saveKeyPrefix != QAPrefix()) throw new InvalidOperationException("QA prefix changed during the run; abort.");
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Bounded API check exceeded 150 seconds.");
            if (EditorApplication.timeSinceStartup < resumeAt || Time.frameCount < resumeFrame) return;
            if (!routine.MoveNext()) { Stop(); return; }
            resumeAt = EditorApplication.timeSinceStartup + (routine.Current is float delay ? delay : .05f);
            resumeFrame = Time.frameCount + 2;
        }
        catch (Exception exception)
        {
            SessionState.SetBool(State + "failed", true);
            Record("FAIL exception " + exception);
            Stop();
        }
    }
    public static void Stop(bool exitPlay = true)
    {
        EditorApplication.update -= Tick; routine = null;
        if (!SessionState.GetBool(State + "finished", false))
        {
            SessionState.SetBool(State + "failed", true);
            SessionState.SetString(State + "result", "Incomplete/failed; see runtime-checks.txt");
            Record("INCOMPLETE stopped before suite completion");
        }
        // Never restore the real prefix here: Flow saves again during Play teardown.
        if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
    public static void CancelPrepared()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        RestoreEditor();
    }
    static void BeforeReload()
    {
        if (!Running) return;
        Record("INCOMPLETE interrupted by assembly reload"); Stop();
    }
    static void OnModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && Running) Stop(false);
        if (state == PlayModeStateChange.EnteredEditMode) RestoreEditor();
    }
    static void RestoreEditor()
    {
        if (!SessionState.GetBool(State + "prepared", false)) return;
        EditorApplication.update -= Tick; routine = null;
        string previous = SessionState.GetString(State + "startOverride", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        Time.timeScale = SessionState.GetFloat(State + "timeScale", 1);
        Cursor.lockState = (CursorLockMode)SessionState.GetInt(State + "cursorLock", 0);
        Cursor.visible = SessionState.GetBool(State + "cursorVisible", true);
        Application.runInBackground = SessionState.GetBool(State + "runInBackground", false);
        try
        {
            CheckRealSlots("after Play teardown");
            Check(FileHash(MainScene) == SessionState.GetString(State + "mainHash", ""), "Main scene file unchanged by Play validation");
            var active = SceneManager.GetActiveScene();
            Check(active.path == MainScene && !active.isDirty && SceneManager.sceneCount == 1,
                "Editor returned to one Clean main scene");
            BaselineSaveStore.Clear(QAPrefix());
            CheckRealSlots("after QA cleanup");
            bool complete = SessionState.GetBool(State + "finished", false) && !SessionState.GetBool(State + "failed", false);
            SessionState.SetString(State + "result", complete ? "PASS API integration and restoration checks" : "INCOMPLETE/FAIL; Editor restored");
            Record("RESTORED Edit mode; time=" + Time.timeScale + "; original Play override restored; no input devices were changed; QA slot cleared.");
        }
        catch (Exception exception)
        {
            SessionState.SetBool(State + "failed", true);
            SessionState.SetString(State + "result", "FAIL restoration audit; " + exception.Message);
            Record("FAIL restoration " + exception);
            Debug.LogException(exception);
        }
        finally { SessionState.SetBool(State + "prepared", false); }
    }
}
