using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Controlled API fixtures only. Prepare owns the copied scene and disposable save slot.
[InitializeOnLoad]
public static class SideGameBIntegrationChecks
{
    const string PrepareState = "QA.OverworldV2Play.", State = "QA.SideGameBIntegration.";
    const string Report = "ToolReports/SideGameBQuality/integration-checks.txt";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static BaselineFlow flow;
    static BaselineWorld world;
    static PlayerMovement player;
    static ThirdPersonCamera camera;
    static SideWorldTravel travel;
    static BaselineCampTutorial tutorial;
    static double deadline, resumeAt;
    static int resumeFrame;
    static bool oldBackground, oldCollision;
    static string snapshot, realPrimary, realBackup;
    static Vector3 returnPosition;
    static int lesson, bCompletionEvents;
    public static bool Running { get; private set; }
    public static string Status => SessionState.GetString(State + "status", "Not run");

    static SideGameBIntegrationChecks()
    {
        EditorApplication.playModeStateChanged += ModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (Running) Fail("Assembly reload interrupted QA."); };
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Restoration(); };
    }

    public static void Begin()
    {
        string prefix = SessionState.GetString(PrepareState + "prefix", "");
        if (!EditorApplication.isPlaying || Running || OverworldV2PlayChecks.Running ||
            OverworldV2TutorialChecks.Running || OverworldV2CameraChecks.Running ||
            OverworldV2TraversalChecks.Running || OverworldV2WaystationChecks.Running ||
            !SessionState.GetBool(PrepareState + "prepared", false) || SceneManager.sceneCount != 1 ||
            SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene ||
            !prefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) ||
            prefix == SessionState.GetString(PrepareState + "realPrefix", ""))
            throw new InvalidOperationException("Requires exclusive prepared V2 validation Play session; call Prepare in Edit mode first.");
        flow = Object.FindFirstObjectByType<BaselineFlow>(); world = flow ? flow.GetComponent<BaselineWorld>() : null;
        player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null;
        camera = Object.FindFirstObjectByType<ThirdPersonCamera>(); tutorial = flow ? flow.Tutorial : null;
        travel = SideWorldTravel.Instance;
        if (!flow || flow.saveKeyPrefix != prefix || !world || !player || !camera || !tutorial || !travel || travel.IsAway || travel.Busy)
            throw new InvalidOperationException("Missing initialized host or wrong save prefix; no gameplay calls made.");
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, "METHOD: isolated copied-scene API integration. No human playthrough, input or full B combat claim. B death uses TakeDamage; victory invokes the real trigger with a controlled unlocked door/player collider.\n");
        string real = SessionState.GetString(PrepareState + "realPrefix", "");
        realPrimary = Slot(real + ".primary"); realBackup = Slot(real + ".backup");
        SessionState.SetString(State + "realPrimary", realPrimary); SessionState.SetString(State + "realBackup", realBackup);
        SessionState.SetString(State + "realPrefix", real); SessionState.SetString(State + "prefix", prefix);
        SessionState.SetBool(State + "restore", true); SessionState.SetBool(State + "complete", false);
        oldBackground = Application.runInBackground; oldCollision = Physics.GetIgnoreLayerCollision(6, 7);
        Application.runInBackground = true; Running = true;
        bCompletionEvents = 0; travel.TrialBCompletionRecorded += BCompleted;
        deadline = EditorApplication.timeSinceStartup + 240; resumeAt = 0; resumeFrame = Time.frameCount;
        steps.Clear(); steps.Push(Run()); SessionState.SetString(State + "status", "Running bounded API checks");
        EditorApplication.update += Tick;
    }

    static IEnumerator Run()
    {
        flow.NewGame(); yield return .3f;
        Check(tutorial.Active && tutorial.CardOpen && !flow.Progress.openingCompleted, "Fresh unfinished tutorial");
        tutorial.StartPractice(); yield return .15f;
        world.Interact(Item(BaselineInteraction.ActionKind.AcceptTask, 0));
        world.Track(0);
        world.Interact(Item(BaselineInteraction.ActionKind.Chest, 0));
        if (flow.rewards && flow.rewards.IsOpen) flow.rewards.Dismiss();
        yield return .3f;
        Check(!flow.BlockWorldInput && tutorial.Active && !tutorial.CardOpen, "Fixture retains nonmodal unfinished tutorial");
        Check(flow.SaveNow(), "Fixture saved only to isolated QA slot");
        snapshot = HostState(); returnPosition = player.transform.position; lesson = (int)tutorial.CurrentLesson;

        travel.Enter(false); yield return Away(false);
        Check(!travel.TrialACompleted, "Fresh A incomplete");
        travel.ReturnToMain(); yield return Home(); CheckHost("A early return");
        Check(!flow.Progress.trialACompleted, "A early return grants no completion");
        travel.Enter(false); yield return Away(false);
        var battle = Object.FindFirstObjectByType<BattleManager>(); Check(battle, "A manager exists");
        battle.PlayerWon(); yield return .15f;
        Check(travel.TrialACompleted, "Actual A victory API records completion");
        travel.ReturnToMain(); yield return Home(); CheckHost("A victory return");

        travel.Enter(true); yield return Away(true);
        yield return CaptureGameView("editor-side-b.png");
        Check(!travel.TrialBCompleted, "Fresh B incomplete");
        travel.ReturnToMain(); yield return Home(); CheckHost("B early return");
        Check(!flow.Progress.trialBCompleted, "B early return grants no completion");
        Check(!flow.ClaimTrialReward(1) && !flow.Progress.trialRewardClaimed[1], "Unfinished B cannot claim its reward");
        travel.Enter(true); yield return Away(true);
        var health = Object.FindFirstObjectByType<PlayerHealth>(); Check(health, "B health exists");
        health.TakeDamage(int.MaxValue); yield return .1f;
        Check(health.IsDead && !travel.TrialBCompleted, "Controlled B death grants no completion");
        int previousScene = SceneManager.GetActiveScene().handle;
        var hud = Object.FindFirstObjectByType<PlayerHealthHud>(); Check(hud, "B retry HUD exists");
        hud.RestartGame();
        yield return Until(() => !travel.Busy && SceneManager.GetActiveScene().handle != previousScene &&
            SceneManager.GetActiveScene().path == SideWorldTravel.SideGameBPath, "B retry reload", 25);
        yield return .3f;
        PrepareBControls();
        health = Object.FindFirstObjectByType<PlayerHealth>();
        Check(health && !health.IsDead && travel.IsAway && !travel.TrialBCompleted, "B retry restores living player and retains suspended host");
        var portal = Object.FindFirstObjectByType<VictoryPortalTrigger2D>();
        var fighter = Object.FindFirstObjectByType<PlayerController2D>();
        Check(portal && fighter, "B victory trigger and player exist");
        var collider = fighter.GetComponentInChildren<Collider2D>(); Check(collider, "B player collider exists");
        var trigger = typeof(VictoryPortalTrigger2D).GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance);
        Check(trigger != null, "Real B trigger entry exists");
        var blocker = portal.transform.parent ? portal.transform.parent.GetComponent<Collider2D>() : null;
        if (blocker)
        {
            blocker.enabled = true; trigger.Invoke(portal, new object[] { collider });
            Check(!portal.HasWon && !travel.TrialBCompleted, "Closed B door rejects victory");
            blocker.enabled = false;
        }
        else Record("NOTE no parent door blocker; closed-door negative check unavailable");
        trigger.Invoke(portal, new object[] { collider }); yield return .1f;
        Check(portal.HasWon && travel.TrialBCompleted && bCompletionEvents == 1, "Real B trigger records exactly one controlled victory event");
        trigger.Invoke(portal, new object[] { collider });
        Check(bCompletionEvents == 1, "Repeated victory trigger does not duplicate completion event");
        portal.ReturnToMainWorld(); yield return Home(); CheckHost("B victory return");
        yield return CaptureGameView("editor-return-host.png");
        Check(flow.Progress.trialACompleted && flow.Progress.trialBCompleted, "Both completion flags persist in host");
        Check(BaselineSaveStore.TryLoad(flow.saveKeyPrefix, out var saved, out string message) &&
            saved.trialACompleted && saved.trialBCompleted && !saved.openingCompleted,
            "QA save contains both wins and unfinished tutorial: " + message);
        Check(flow.ClaimTrialReward(1), "Completed B can claim its reward once");
        if (flow.rewards && flow.rewards.IsOpen) flow.rewards.Dismiss();
        yield return .3f;
        Check(BaselineSaveStore.TryLoad(flow.saveKeyPrefix, out var rewarded, out string rewardMessage) &&
            rewarded.trialRewardClaimed[1] && rewarded.trialBCompleted,
            "B claimed reward persisted to QA save: " + rewardMessage);
        Check(!flow.ClaimTrialReward(1), "Repeated B reward claim rejected after presentation closes");
        // Current-schema compatibility fixture represents old entrance-gift ownership.
        // This is not a historical raw-save version migration test.
        var legacyGift = JsonUtility.FromJson<BaselineSaveData>(JsonUtility.ToJson(rewarded));
        legacyGift.trialBCompleted = false;
        var loadout = player.GetComponent<BaselineLoadout>();
        int priorBonus = loadout ? loadout.DamageBonus : 0;
        Check(BaselineSaveStore.Save(flow.saveKeyPrefix, legacyGift, out string fixtureMessage),
            "Saved isolated legacy-gift ownership fixture: " + fixtureMessage);
        Check(flow.ContinueGame(), "Loaded isolated legacy-gift ownership fixture"); yield return .3f;
        Check(flow.Progress.trialRewardClaimed[1] && !flow.Progress.trialBCompleted &&
            (!loadout || loadout.DamageBonus == priorBonus), "Owned entrance gift survives load without deduction or fabricated B win");
        Check(!flow.ClaimTrialReward(1), "Previously owned entrance gift cannot be granted again");
        CheckReal();
        SessionState.SetBool(State + "complete", true); Record("PASS API suite complete; awaiting Edit-mode restoration");
    }

    static IEnumerator Away(bool b)
    {
        string path = b ? SideWorldTravel.SideGameBPath : SideWorldTravel.BattlePath;
        yield return Until(() => travel.IsAway && !travel.Busy && SceneManager.GetActiveScene().path == path, "Enter " + path, 25);
        yield return .3f;
        if (b) PrepareBControls();
        Check(!flow.gameObject.activeInHierarchy && SceneManager.GetSceneByPath(OverworldV2PlayChecks.ValidationScene).isLoaded,
            "Host remains loaded and suspended");
    }
    static IEnumerator Home()
    {
        yield return Until(() => !travel.IsAway && !travel.Busy && SceneManager.GetActiveScene().path == OverworldV2PlayChecks.ValidationScene, "Return host", 25);
        yield return .2f;
    }
    static IEnumerator CaptureGameView(string filename)
    {
        string path = Path.Combine("Docs/SideGameBQuality", filename);
        DateTime requestedAt = DateTime.UtcNow;
        bool requested = false;
        try
        {
            Directory.CreateDirectory("Docs/SideGameBQuality");
            // Captures the rendered Game View, including its UI. No Scene View or extra camera.
            ScreenCapture.CaptureScreenshot(path);
            requested = true;
        }
        catch (Exception exception) { Record("NOTE Game View capture unavailable: " + exception.Message); }
        yield return .4f;
        if (requested)
        {
            try
            {
                var file = new FileInfo(path);
                bool fresh = file.Exists && file.Length > 0 && file.LastWriteTimeUtc >= requestedAt;
                Record(fresh ? "GAME_VIEW fresh screenshot output: " + path : "NOTE fresh screenshot not verified yet: " + path);
            }
            catch (Exception exception) { Record("NOTE screenshot file check unavailable: " + exception.Message); }
        }
    }
    static void PrepareBControls()
    {
        // Scene Awake/Start must have run first. These are controlled menu API calls,
        // not UI-click evidence; CloseTutorial can open a second paused hero selector.
        var controls = Object.FindFirstObjectByType<GameControlsTutorial>();
        if (controls && controls.isActiveAndEnabled)
        {
            var close = typeof(GameControlsTutorial).GetMethod("CloseTutorial", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(close != null, "B tutorial close entry exists"); close.Invoke(controls, null);
            Record("API fixture closed B controls tutorial");
        }
        var selector = Object.FindFirstObjectByType<CharacterSelectionUI>();
        if (selector && Time.timeScale <= 0)
        {
            var choose = typeof(CharacterSelectionUI).GetMethod("ApplyChoice", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(choose != null, "B hero selection entry exists");
            choose.Invoke(selector, new object[] { CharacterSelectionUI.HeroChoice.Princess });
            Record("API fixture chose Princess; not a balance/design decision");
        }
        Check(Time.timeScale > 0, "B tutorial and hero selection release pause before gameplay fixture");
    }
    static IEnumerator Until(Func<bool> condition, string label, double seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (!condition()) { if (EditorApplication.timeSinceStartup > end) throw new TimeoutException(label); yield return .05f; }
    }
    static void CheckHost(string label)
    {
        Check(HostState() == snapshot, label + " preserves tasks, boxes, tracking, gear and tutorial flags");
        Check(tutorial.Active && !tutorial.CardOpen && (int)tutorial.CurrentLesson == lesson, label + " resumes unfinished lesson");
        Check(player.isActiveAndEnabled && !player.IsGameplayInputLocked && !camera.IsLookInputLocked && Mathf.Approximately(Time.timeScale, 1), label + " restores player/camera input and time");
        Check(Vector3.Distance(player.transform.position, returnPosition) < 1.5f, label + " retains return position");
        Check(SceneManager.sceneCount == 1, label + " unloads side scene"); CheckReal();
    }
    static string HostState()
    {
        var data = new BaselineSaveData(); world.CopyProgressTo(data);
        data.openingCompleted = flow.Progress.openingCompleted; data.northBridgeOpen = flow.Progress.northBridgeOpen;
        data.trialRewardClaimed = (bool[])flow.Progress.trialRewardClaimed.Clone();
        data.taskRewardClaimed = (bool[])flow.Progress.taskRewardClaimed.Clone(); data.unequippedMask = flow.Progress.unequippedMask;
        data.hasSword = GameSession.Instance.HasSword;
        return JsonUtility.ToJson(data);
    }
    static BaselineInteraction Item(BaselineInteraction.ActionKind kind, int index)
    {
        foreach (var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None))
            if (item.action == kind && item.index == index) return item;
        throw new InvalidOperationException("Missing fixture item " + kind + " " + index);
    }
    static string Slot(string key) => PlayerPrefs.HasKey(key) ? "present:" + PlayerPrefs.GetString(key) : "absent";
    static void BCompleted() => bCompletionEvents++;
    static void CheckReal()
    {
        string real = SessionState.GetString(State + "realPrefix", "");
        Check(Slot(real + ".primary") == realPrimary && Slot(real + ".backup") == realBackup, "Real save slots unchanged");
    }
    static void Tick()
    {
        if (!Running) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("240-second suite deadline");
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play interrupted");
            if (EditorApplication.timeSinceStartup < resumeAt || Time.frameCount < resumeFrame) return;
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                resumeAt = EditorApplication.timeSinceStartup + (step.Current is float delay ? delay : .05);
                resumeFrame = Time.frameCount + 1; return;
            }
            Stop();
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }
    static void Check(bool value, string text) { Record((value ? "PASS " : "FAIL ") + text); if (!value) throw new InvalidOperationException(text); }
    static void Record(string text) => File.AppendAllText(Report, DateTime.UtcNow.ToString("O") + " " + text + "\n");
    static void Fail(string text) { SessionState.SetBool(State + "complete", false); Record("FAIL " + text); Stop(); }
    public static void Stop(bool exitPlay = true)
    {
        if (!Running) return; Running = false; EditorApplication.update -= Tick; steps.Clear();
        if (travel) travel.TrialBCompletionRecorded -= BCompleted;
        Application.runInBackground = oldBackground; Physics.IgnoreLayerCollision(6, 7, oldCollision);
        SessionState.SetString(State + "status", SessionState.GetBool(State + "complete", false) ? "Awaiting restoration" : "INCOMPLETE/FAIL; see report");
        // Do not restore the real prefix: Flow can autosave during teardown.
        if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
    static void ModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && Running) { Record("INCOMPLETE Play stopped externally"); Stop(false); }
        if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Restoration;
    }
    public static void Restoration()
    {
        if (!SessionState.GetBool(State + "restore", false)) return;
        string prefix = SessionState.GetString(State + "prefix", ""), real = SessionState.GetString(State + "realPrefix", "");
        var scene = SceneManager.GetActiveScene();
        bool ok = !EditorApplication.isPlaying && !SessionState.GetBool(PrepareState + "prepared", false) &&
            scene.path == "Assets/_Demo/Scenes/OverworldBaseline.unity" && !scene.isDirty && SceneManager.sceneCount == 1 &&
            !PlayerPrefs.HasKey(prefix + ".primary") && !PlayerPrefs.HasKey(prefix + ".backup") &&
            Slot(real + ".primary") == SessionState.GetString(State + "realPrimary", "") &&
            Slot(real + ".backup") == SessionState.GetString(State + "realBackup", "");
        Record((ok ? "PASS " : "FAIL ") + "Prepare restoration: clean original scene, disposable slots cleared, real slots unchanged. Main-file hash evidence: ToolReports/MapV2Migration/runtime-checks.txt");
        SessionState.SetBool(State + "restore", false);
        SessionState.SetString(State + "status", ok && SessionState.GetBool(State + "complete", false) ? "PASS bounded API checks and restoration" : "INCOMPLETE/FAIL; inspect report");
    }
}
