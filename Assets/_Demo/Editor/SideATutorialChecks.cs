using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Bounded, real-Play regression. Lessons are completed with actual InputSystem
/// keyboard events, not by changing tutorial progress or invoking attack methods.
/// The sole relocation fixture places a fighter inside the original KO trigger.
/// No authored scene, prefab, or gameplay save is written by these checks.
/// </summary>
[InitializeOnLoad]
public static class SideATutorialChecks
{
    const string State = "QA.SideATutorial.";
    const string ScenePath = "Assets/Scenes/BattleScene.unity";
    static string Output => SessionState.GetString(State + "output", Path.Combine(Path.GetTempPath(), "sidea-tutorial-checks"));
    static string Report => Path.Combine(Output, "results.txt");
    static int SelectedStage => SessionState.GetInt(State + "stage", 1);
    static bool ReducedMotion => SessionState.GetBool(State + "reducedMotion", false);
    static readonly Stack<IEnumerator> Routine = new Stack<IEnumerator>();
    static Keyboard keyboard, originalKeyboard;
    static Mouse mouse, originalMouse;
    static bool originalKeyboardEnabled, originalMouseEnabled, oldBackground;
    static InputSettings.BackgroundBehavior oldBackgroundBehavior;
    static InputSettings.EditorInputBehaviorInPlayMode oldEditorInputBehavior;
    static KeyboardState desiredInput;
    static BattleJMove observedJRoute;
    static BattleSpecialKind observedKRoute;
    static int desiredRouteVersion, observedRouteVersion, observedRouteFrame;
    static string observedRouteKeys;
    static ushort desiredMouseButtons;
    static Vector2? desiredMousePosition;
    static bool oldReducedMotion;
    static bool comboCollisionModified, oldComboLayerCollision;
    static bool checkingUiClick, uiClickLeaked;
    static bool running, stopping, success;
    static int lastFrame, passed, highestGhostCount;
    static double deadline;
    static string phase;
    static BattleManager manager;
    static BattleTutorial tutorial;
    static BattlePlayerController player;
    static GameObject enemy;
    static BattleSpecialMoves specials;
    static SceneSetup[] originalScenes;

    static SideATutorialChecks()
    {
        EditorApplication.playModeStateChanged += Changed;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }

    /// <summary>Compilation/import sanity; usable with -executeMethod and -quit.</summary>
    public static void Compile()
    {
        foreach (string name in new[] { "BattleTutorial", "BattleTutorialHud", "BattleTutorialHints", "BattleTrialMenu", "BattleQuickGuidePreview", "SideATutorialChecks" })
        {
            string prefix = name == "SideATutorialChecks" ? "Assets/_Demo/Editor/" : "Assets/Scripts/";
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(prefix + name + ".cs");
            if (!script || script.GetClass() == null) throw new InvalidOperationException("Script did not import: " + name);
        }
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) throw new InvalidOperationException("BattleScene is missing.");
        Debug.Log("SIDE_A_TUTORIAL: PASS script imports and existing BattleScene. Unity " + Application.unityVersion +
            ". Native requested version is recorded by ProjectSettings/ProjectVersion.txt; a different patch is not exact-version validation.");
    }

    public static void RunAutomation()
        => Launch(1, false, "full", true);
    public static void RunSunlitAutomation() => Launch(0, false, "full", true);
    public static void RunEclipseAutomation() => Launch(2, false, "full", true);
    public static void RunReducedMotionAutomation() => Launch(1, true, "full", true);
    public static void RunBoundariesAutomation() => Launch(1, false, "boundaries", true);
    public static void RunSunlitBoundariesAutomation() => Launch(0, false, "boundaries", true);
    public static void RunChargeBoundaryAutomation() => Launch(1, false, "charge-boundary", true);
    public static void RunEscMenuAutomation() => Launch(1, false, "esc-menu", true);
    public static void RunQuickGuideAutomation() => Launch(1, false, "quick-guide", true);
    public static void RunMovingComboAutomation() => Launch(1, false, "moving-combo", true);

    [MenuItem("Tools/Side A/Validate Interactive Tutorial (Real Play)")]
    public static void Run() => Launch(1, false, "full", false);
    [MenuItem("Tools/Side A/Validate Tutorial Boundaries (Real Play)")]
    public static void RunBoundaries() => Launch(1, false, "boundaries", false);

    static void Launch(int stage, bool reducedMotion, string suite, bool quit)
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before running Side A tutorial checks.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save your scene first; validation never discards scene edits.");
        Compile();
        SessionState.SetInt(State + "stage", stage);
        SessionState.SetBool(State + "reducedMotion", reducedMotion);
        SessionState.SetString(State + "suite", suite);
        SessionState.SetBool(State + "quit", quit);
        SessionState.SetString(State + "output", Path.Combine(Path.GetTempPath(), "sidea-tutorial-checks",
            (stage == 0 ? "sunlit" : stage == 1 ? "twilight" : "eclipse") + "-" + (reducedMotion ? "reduced-motion" : "normal-motion") +
            "-" + suite + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        originalScenes = EditorSceneManager.GetSceneManagerSetup();
        var savedScenes = new SavedScene[originalScenes.Length];
        for (int i = 0; i < originalScenes.Length; i++)
            savedScenes[i] = new SavedScene { path = originalScenes[i].path,
                isLoaded = originalScenes[i].isLoaded, isActive = originalScenes[i].isActive };
        SessionState.SetString(State + "sceneSetup", JsonUtility.ToJson(new SceneSnapshot { scenes = savedScenes }));
        Directory.CreateDirectory(Output);
        string method = suite == "moving-combo"
            ? "Method: existing standalone BattleScene and actual InputSystem events. Public SkipStep/ReviewStep create repeatable Combo-lesson fixtures, " +
                "not fabricated successful combat. Only an explicitly labelled left-facing/body-distance fixture relocates real bodies before a test. " +
                "No direct damage, LastAttackLanded, hit serial or private progress writes. Actual neutral, A-held, D-held and mixed-input J chains " +
                "must each land all four real hits. W/S and K arbitration, per-punch facing commitment and whiff gates are checked.\n"
            : suite == "quick-guide"
            ? "Method: empty unsaved no-BaselineFlow host; actual public Enter(false) loads Side A additively. " +
                "Real pointer callbacks and InputSystem keys navigate all five cards. The isolated real-avatar preview is sampled with timeScale=0; " +
                "pose, rendered pixels, progress and key highlights must change while actual fighter roots, damage and stocks stay fixed. " +
                "Screenshots are actual Unity Game View; this is a visual demonstration, not recorded gameplay. " +
                "Public RestartBattle is an explicitly labelled retry fixture; no user save, reward flow or progress writes are used.\n"
            : suite == "esc-menu"
            ? "Method: empty unsaved host fixture, no BaselineFlow or gameplay save. Actual public Enter(false) additively loads the existing BattleScene; " +
                "actual UI pointer clicks and InputSystem events validate removed toolbar rectangles, ESC/help/back/continue, return button and global B. " +
                "Host suspension, cursor/time/collision restoration are checked; this is NOT real main-world reward/save acceptance.\n"
            : "Method: actual input events complete movement, double jump, LEFT mouse contact, J combo, K short beam, RIGHT mouse full beam and recovery. " +
                "Public menu actions perform retry/skip/start; only a KO fixture repositions a body into the original trigger. " +
                "No private progress writes or fabricated attack/beam events. " +
                "Standalone BattleScene has no main-world host: real host reward persistence and human comfort are not covered.\n";
        File.WriteAllText(Report, "SIDE A INTERACTIVE TUTORIAL: real Play regression\nUnity=" + Application.unityVersion +
            "\nStage=" + stage + " ReducedMotion=" + reducedMotion + " Suite=" + suite + "\nOutput=" + Output +
            "\n" + method);
        SessionState.SetBool(State + "pending", true);
        SessionState.SetBool(State + "success", false);
        if (suite == "esc-menu" || suite == "quick-guide") EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        else EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.EnterPlaymode();
    }

    [Serializable] class SavedScene { public string path; public bool isLoaded, isActive; }
    [Serializable] class SceneSnapshot { public SavedScene[] scenes; }

    static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(State + "pending", false))
        {
            try { Begin(); }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }
        if (change == PlayModeStateChange.ExitingPlayMode && running && !stopping)
            Finish(false, "Play was interrupted before validation completed.");
        if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(State + "pending", false)) return;
        bool ok = SessionState.GetBool(State + "success", false);
        SessionState.SetBool(State + "pending", false);
        bool quit = SessionState.GetBool(State + "quit", false);
        SessionState.SetBool(State + "quit", false);
        if (!quit)
        {
            var snapshot = JsonUtility.FromJson<SceneSnapshot>(SessionState.GetString(State + "sceneSetup", "{}"));
            if (snapshot != null && snapshot.scenes != null && snapshot.scenes.Length > 0)
            {
                var setup = new SceneSetup[snapshot.scenes.Length];
                for (int i = 0; i < setup.Length; i++) setup[i] = new SceneSetup {
                    path = snapshot.scenes[i].path, isLoaded = snapshot.scenes[i].isLoaded, isActive = snapshot.scenes[i].isActive };
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }
        if (quit) EditorApplication.Exit(ok ? 0 : 1);
    }

    static void BeforeReload()
    {
        if (running) Finish(false, "An assembly reload interrupted active tutorial checks.");
    }

    static void Begin()
    {
        running = true; stopping = success = false; passed = highestGhostCount = 0;
        lastFrame = -1; phase = "scene initialization";
        deadline = EditorApplication.timeSinceStartup + 180;
        originalKeyboard = Keyboard.current; originalMouse = Mouse.current;
        originalKeyboardEnabled = originalKeyboard != null && originalKeyboard.enabled;
        originalMouseEnabled = originalMouse != null && originalMouse.enabled;
        oldBackground = Application.runInBackground;
        oldBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        oldEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        keyboard = InputSystem.AddDevice<Keyboard>("SideATutorialQAKeyboard");
        mouse = InputSystem.AddDevice<Mouse>("SideATutorialQAMouse");
        if (originalKeyboardEnabled) InputSystem.DisableDevice(originalKeyboard);
        if (originalMouseEnabled) InputSystem.DisableDevice(originalMouse);
        keyboard.MakeCurrent(); mouse.MakeCurrent();
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Application.runInBackground = true;
        desiredInput = new KeyboardState();
        desiredRouteVersion = 0; observedRouteVersion = -1; observedRouteFrame = -1;
        observedRouteKeys = "not sampled";
        desiredMouseButtons = 0;
        desiredMousePosition = null;
        checkingUiClick = uiClickLeaked = false;
        comboCollisionModified = false;
        oldReducedMotion = BattlePresentationSettings.ReducedMotion;
        BattlePresentationSettings.ReducedMotion = ReducedMotion;
        // Editor updates may consume queued virtual-keyboard events before the
        // game update. Inject immediately before the actual Dynamic/Fixed input
        // update instead, and explicitly route virtual devices to Game View.
        InputSystem.onBeforeUpdate += BeforeInput;
        InputSystem.onAfterUpdate += AfterInput;
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        Record("OUTPUT " + Output);
        string suite = SessionState.GetString(State + "suite", "full");
        Routine.Clear(); Routine.Push(suite == "moving-combo" ? MovingComboTests() : suite == "quick-guide" ? QuickGuideTests() : suite == "esc-menu" ? EscMenuTests() :
            suite == "boundaries" ? BoundaryTests() : suite == "charge-boundary" ? ChargeBoundaryTests() : Tests());
        EditorApplication.update += Tick;
    }

    static IEnumerator Tests()
    {
        yield return Delay(.6f);
        Rebind();
        Check(manager && manager.trialMenu && manager.SelectionOpen && manager.IsPaused && !manager.IsBattleActive,
            "Original trial selection starts paused, before any tutorial or duel.");
        Check(!SideWorldTravel.Instance, "Standalone QA scene has no main-world reward host.");
        manager.trialMenu.SelectTrial(SelectedStage);
        Check(manager.trialMenu.SelectedTrial == SelectedStage, "Requested arena selected through original public selection API.");
        manager.BeginTutorialPractice();
        yield return Wait(() => manager.IsTutorialActive && tutorial.CurrentStepIndex == 0, 6, "practice entry");
        yield return Delay(.4f);
        Check(!manager.SelectionOpen && manager.IsBattleActive && !manager.IsPaused && tutorial.StepCount == 7,
            "Interactive practice enters all seven lessons without normal countdown/result flow.");
        Check(manager.CurrentTrialIndex == SelectedStage && manager.BattleDuration == 0 && player.enabled &&
            !enemy.GetComponent<EnemyMovement>().enabled && !enemy.GetComponent<EnemyAttack>().enabled,
            "Practice retains selected stage; native player input is on, enemy AI and formal timer are off.");
        Capture("01-practice-move.png");

        phase = "pause and help isolation";
        yield return Press(Key.H);
        Check(manager.HelpOpen && manager.IsPaused && Time.timeScale == 0, "Actual H opens existing controls and pauses practice.");
        float age = tutorial.StepAge, battleTime = manager.BattleDuration;
        int step = tutorial.CurrentStepIndex;
        Vector3 pausedPosition = player.transform.position;
        SetKeys(Key.D); yield return Delay(.55f); SetKeys();
        Check(tutorial.CurrentStepIndex == step && Mathf.Abs(tutorial.StepAge - age) < .01f &&
            Mathf.Abs(manager.BattleDuration - battleTime) < .01f && Vector3.Distance(pausedPosition, player.transform.position) < .05f,
            "Help pause blocks lesson time/progression, movement and formal timer.");
        Capture("02-existing-help-paused.png");
        yield return Delay(.2f);
        manager.trialMenu.OpenMoveCards();
        yield return Delay(.3f);
        bool allCards = true;
        for (int card = 1; card <= 6; card++) allCards &= GameObject.Find("Move " + card) != null;
        Check(allCards && manager.HelpOpen && manager.IsPaused,
            "All six move reference cards open inside the existing help pause.");
        Capture("02b-real-move-cards.png");
        yield return Delay(.2f);
        manager.trialMenu.OpenControls();
        yield return Press(Key.H);
        Check(!manager.HelpOpen && !manager.IsPaused && Time.timeScale > 0, "Actual H closes help and restores practice.");
        yield return Press(Key.Escape);
        Check(manager.IsPaused && manager.trialMenu.CurrentView == BattleTrialMenu.View.Pause,
            "Actual Escape uses existing pause menu inside training.");
        age = tutorial.StepAge;
        yield return Delay(.4f);
        Check(Mathf.Abs(tutorial.StepAge - age) < .01f, "Manual pause also freezes the lesson clock.");
        manager.ResumeTrialPause();
        Check(!manager.IsPaused && tutorial.IsRunning, "Existing Continue action resumes the same lesson.");

        phase = "retry and harmless player fall";
        manager.RestartBattle();
        yield return Delay(.15f);
        Check(tutorial.CurrentStepIndex == 0 && player.GetComponent<BattleStockLives>().Remaining == 2 && !manager.IsGameOver,
            "Retry during training resets only current lesson, not a formal duel.");
        PlaceInOriginalKo(player.gameObject);
        yield return Wait(() => player.GetComponent<Collider>().bounds.min.y >
            manager.GetComponent<BattleArenaStages>().mainGround.GetComponent<Collider>().bounds.max.y - .15f,
            4, "original KO trigger returns training player to main deck");
        yield return Delay(.25f);
        Check(tutorial.IsRunning && !manager.IsGameOver && player.GetComponent<BattleStockLives>().Remaining == 2 &&
            manager.trialMenu.CurrentView != BattleTrialMenu.View.Result && manager.BattleDuration == 0,
            "Real KO route resets training fall without stock loss, result or formal time.");

        phase = "real movement";
        Vector3 movedFrom = player.transform.position;
        float maximumMoveDistance = 0;
        double movementDeadline = EditorApplication.timeSinceStartup + 12;
        while (tutorial.CurrentStepIndex == 0 && EditorApplication.timeSinceStartup < movementDeadline)
        {
            maximumMoveDistance = Mathf.Max(maximumMoveDistance, Mathf.Abs(player.transform.position.x - movedFrom.x));
            float dx = tutorial.TargetWorldPosition.Value.x - player.transform.position.x;
            if (Mathf.Abs(dx) > .22f) SetKeys(dx > 0 ? Key.D : Key.A); else SetKeys();
            yield return null;
        }
        SetKeys();
        Check(tutorial.CurrentStepIndex == 1 && maximumMoveDistance > 1.5f,
            "Real A/D reaches authored marker and completes Move; no relocation or lesson setter.");

        phase = "real double jump";
        yield return Delay(.35f);
        yield return Press(Key.Space, .06f, .12f);
        yield return Press(Key.Space, .06f, .12f);
        yield return Wait(() => tutorial.CurrentStepIndex == 2, 8, "real ground and air jumps advance Jump");
        Check(player.JumpSerial >= 2 && player.AirJumpSerial >= 1,
            "Actual native ground jump and air jump counters changed.");

        phase = "real first contact";
        yield return Delay(.35f);
        yield return PressMouse(MouseButton.Left, .06f, .18f);
        yield return Wait(() => tutorial.CurrentStepIndex == 3, 7, "real LEFT mouse contact advances Attack");
        Check(tutorial.CurrentLesson == BattleTutorial.Lesson.Combo,
            "Real primary LEFT mouse attack contact advances to optional four-hit combo.");

        phase = "real four-hit combo";
        yield return Delay(.35f);
        yield return Press(Key.J, .055f, .095f);
        yield return Wait(() => tutorial.ComboHits == 1, 3, "one real confirmed punch");
        yield return Wait(() => tutorial.ComboHits == 0, 4, "expired native combo window clears displayed progress");
        Check(tutorial.CurrentStepIndex == 3 && tutorial.ComboHits == 0,
            "An expired real combo clears tutorial progress instead of preserving stale hits.");
        tutorial.RetryStep(); yield return Delay(.3f);
        double comboDeadline = EditorApplication.timeSinceStartup + 10;
        while (tutorial.CurrentStepIndex == 3 && EditorApplication.timeSinceStartup < comboDeadline)
            yield return Press(Key.J, .055f, .095f);
        SetKeys();
        Check(tutorial.CurrentStepIndex == 4, "Four real buffered J presses/contacts complete optional combo lesson.");
        Capture("03-energy-lesson.png");

        phase = "real short then fully charged beam";
        yield return Delay(.45f);
        yield return PausedChargeBoundary(false);
        phase = "real short then fully charged beam";
        yield return Delay(.3f);
        yield return Press(Key.K, .07f, .25f);
        yield return Wait(() => tutorial.ShortBeamComplete, 6, "short K beam genuinely contacts dummy");
        yield return Wait(() => !specials.IsBusy, 4, "short beam recovers");
        yield return Delay(.15f);
        SetKeys(); desiredMouseButtons = (ushort)(1 << (int)MouseButton.Right);
        yield return Wait(() => tutorial.ChargedBeamComplete || tutorial.CurrentStepIndex > 4, 6, "full RIGHT mouse beam genuinely contacts dummy");
        desiredMouseButtons = 0;
        yield return Wait(() => tutorial.CurrentStepIndex == 5, 5, "both native beam releases advance Energy");
        Check(!manager.IsGameOver && manager.BattleDuration == 0,
            "Real backup K short beam and primary RIGHT mouse full beam contact stay in isolated untimed training.");

        phase = "real rising recovery";
        yield return Delay(.2f);
        Capture("04-recovery-lesson.png");
        Bounds recoveryDeck = manager.GetComponent<BattleArenaStages>().mainGround.GetComponent<Collider>().bounds;
        SetKeys(Key.A);
        yield return Wait(() => player.GetComponent<Collider>().bounds.center.x < recoveryDeck.min.x - .35f &&
            player.GetComponent<Collider>().bounds.min.y < recoveryDeck.max.y + .55f,
            4, "actual A walks beyond original left edge");
        SetKeys();
        yield return Press(new[] { Key.W, Key.K }, .10f, .05f);
        double recoverDeadline = EditorApplication.timeSinceStartup + 12;
        bool usedRise = specials.Kind == BattleSpecialKind.Rise;
        while (tutorial.CurrentStepIndex == 5 && EditorApplication.timeSinceStartup < recoverDeadline)
        {
            usedRise |= specials.Kind == BattleSpecialKind.Rise;
            float dx = tutorial.TargetWorldPosition.Value.x - player.transform.position.x;
            if (Mathf.Abs(dx) > .2f) SetKeys(dx > 0 ? Key.D : Key.A); else SetKeys();
            yield return null;
        }
        SetKeys();
        Check(usedRise && tutorial.CurrentStepIndex == 6,
            "Actual W+K rises and A/D returns onto a real arena platform, completing recovery lesson.");

        phase = "skip/retry and stock demonstration isolation";
        yield return Delay(.3f);
        PlaceInOriginalKo(enemy);
        yield return Wait(() => tutorial.TutorialRingOuts == 1, 4, "first real practice-rival fall");
        Check(enemy.GetComponent<BattleStockLives>().Remaining == 1 &&
            enemy.GetComponent<EnemyDamage>().damagePercent == 0 && !manager.IsGameOver,
            "First demo ring-out shows one rival life and resets damage, without a result.");
        yield return Delay(.5f);
        PlaceInOriginalKo(enemy);
        yield return Wait(() => tutorial.TutorialRingOuts == 2, 4, "second real practice-rival fall");
        Check(enemy.GetComponent<BattleStockLives>().Remaining == 0 &&
            player.GetComponent<BattleStockLives>().Remaining == 2 && !manager.IsGameOver &&
            manager.trialMenu.CurrentView != BattleTrialMenu.View.Result,
            "Second demo ring-out shows zero rival lives while player retains two; no formal result.");
        yield return Wait(() => tutorial.IsComplete, 4, "stock demonstration completes all seven lessons");
        int lastIndex = tutorial.CurrentStepIndex;
        tutorial.RetryStep();
        yield return Delay(.3f);
        Check(tutorial.CurrentStepIndex == lastIndex && !tutorial.IsComplete &&
            enemy.GetComponent<BattleStockLives>().Remaining == 2,
            "Retry from completed practice resets just stock demonstration and restores its two demo lives.");
        bool rivalHudReset = false;
        var teamHud = manager.GetComponent<BattleTeamHud>();
        foreach (var widget in teamHud.Widgets)
            if (widget.Binding.source == enemy.GetComponent<EnemyDamage>())
                rivalHudReset = widget.DisplayedLives == 2 && widget.Damage.text != "OUT";
        Check(rivalHudReset, "Retry also restores the visible rival HUD from OUT to its real 0% damage.");
        tutorial.SkipStep();
        yield return Delay(.2f);
        Check(tutorial.IsComplete && tutorial.IsRunning && !manager.IsGameOver,
            "Skip is available even on the final lesson and opens completion without a match result.");
        Capture("05-complete.png");
        // CaptureScreenshot is deferred to the next render. Do not unload the
        // lesson immediately or this image would show the new intro instead.
        yield return Delay(.3f);

        phase = "fresh formal duel boundary";
        int oldManagerId = manager.GetInstanceID();
        tutorial.StartDuel();
        yield return Wait(() =>
        {
            var candidate = Object.FindFirstObjectByType<BattleManager>();
            return candidate && candidate.GetInstanceID() != oldManagerId && candidate.Tutorial;
        }, 12, "formal battle scene reload");
        Rebind();
        Check(!manager.IsTutorialActive && !manager.IsGameOver && manager.CurrentTrialIndex == SelectedStage &&
            player.GetComponent<BattleStockLives>().Remaining == 2 && enemy.GetComponent<BattleStockLives>().Remaining == 2 &&
            player.GetComponent<PlayerDamage>().damagePercent == 0 && enemy.GetComponent<EnemyDamage>().damagePercent == 0,
            "Formal reload retains selected stage and resets both stocks, percentages and tutorial state.");
        yield return Wait(() => manager.IsBattleActive && !manager.IsPaused, 14, "formal countdown and original landing sequence");
        Check(enemy.GetComponent<EnemyMovement>().enabled && enemy.GetComponent<EnemyAttack>().enabled && player.enabled,
            "Formal countdown restores native opponent AI and player control.");
        yield return Delay(.3f);
        Check(manager.BattleDuration > 0 && !manager.IsTutorialActive,
            "Only the new formal duel advances the real battle timer.");
        if (SelectedStage == 2)
        {
            Capture("06-eclipse-formal-duel.png");
            yield return Delay(.3f);
        }
        Check(BattlePresentationSettings.ReducedMotion ? highestGhostCount == 0 : highestGhostCount > 0,
            BattlePresentationSettings.ReducedMotion ? "Reduced-motion preference suppresses afterimage snapshots as intended." :
            "Actual air jump/recovery rendered native afterimage snapshots during practice.");
        oldManagerId = manager.GetInstanceID();
        manager.BeginTutorialPractice();
        yield return Wait(() =>
        {
            var candidate = Object.FindFirstObjectByType<BattleManager>();
            return candidate && candidate.GetInstanceID() != oldManagerId && candidate.IsTutorialActive;
        }, 12, "replay practice from active formal match reloads safely");
        Rebind();
        Check(manager.CurrentTrialIndex == SelectedStage && manager.BattleDuration == 0 && tutorial.CurrentStepIndex == 0 &&
            !enemy.GetComponent<EnemyMovement>().enabled && !manager.IsGameOver,
            "Practice replay from active match preserves stage and re-enters isolated untimed Move.");
        Record("COMPLETE: " + passed + " assertions. Highest active native afterimages=" + highestGhostCount +
            ". Human readability, mouse ergonomics and main-world persistent reward integration still require manual checks.");
    }

    // Separate smoke: the original full-input suite above remains independent.
    // It exercises public buttons and actual pointer hit testing, not only APIs.
    static IEnumerator BoundaryTests()
    {
        phase = "boundary selection entry";
        yield return Delay(.6f); Rebind();
        manager.trialMenu.SelectTrial(SelectedStage);
        yield return ClickUiButton(manager.trialMenu.PracticeButton);
        yield return Wait(() => manager.IsTutorialActive && tutorial.TargetWorldPosition.HasValue, 5, "practice button entry");
        Check(manager.CurrentTrialIndex == SelectedStage && tutorial.CurrentStepIndex == 0 && manager.BattleDuration == 0,
            "Real selection practice button enters requested stage with no formal time.");

        phase = "immediate start without any lesson";
        int id = manager.GetInstanceID();
        yield return ClickUiButton(FindTutorialButton("Start normal trial"));
        yield return WaitForReload(id, false);
        AssertFreshFormal("Starting immediately from Move");
        yield return Wait(() => manager.IsBattleActive, 14, "immediate-start native countdown");
        Check(!manager.IsPaused && Mathf.Abs(Time.timeScale - 1) < .001f,
            "Immediate skip-to-duel restores normal time scale and active countdown outcome.");

        phase = "each-step retry and skip";
        yield return ReplayPractice();
        for (int index = 0; index < tutorial.StepCount; index++)
        {
            Check(tutorial.CurrentStepIndex == index && !tutorial.IsComplete,
                "Reached actual lesson " + index + " through original skip transition.");
            yield return ClickUiButton(FindTutorialButton("Retry lesson"));
            Check(tutorial.CurrentStepIndex == index && !tutorial.IsComplete && tutorial.StepAge < .5f &&
                !manager.IsGameOver && player.GetComponent<BattleStockLives>().Remaining == 2,
                "Actual RETRY button resets only lesson " + index + " without attacks or life loss.");
            if (index == 4) yield return PausedChargeBoundary();
            yield return ClickUiButton(FindTutorialButton("Skip objective"));
        }
        Check(tutorial.IsComplete && tutorial.IsRunning && !manager.IsGameOver && manager.BattleDuration == 0,
            "All seven real SKIP STEP buttons reach a non-result completion summary.");
        Capture("boundary-completion-before-retry.png"); yield return Delay(.2f);

        phase = "completion retry is an actual working button";
        var retry = FindTutorialButton("Retry lesson");
        Check(retry.IsInteractable(), "Completion page RETRY is enabled, not merely supported by an invisible API.");
        yield return ClickUiButton(retry);
        Check(!tutorial.IsComplete && tutorial.CurrentStepIndex == 6 && enemy.GetComponent<BattleStockLives>().Remaining == 2,
            "Completion RETRY button really reopens final lesson and restores demo stocks.");
        yield return ClickUiButton(FindTutorialButton("Skip objective"));
        Check(tutorial.IsComplete, "Final-step skip returns to completion after actual completion-page retry.");

        phase = "review a previous lesson with real movement";
        yield return ClickUiButton(FindTutorialButton("Review lessons"));
        yield return ClickUiButton(FindTutorialButton("Review lesson 1"));
        yield return Delay(.2f);
        Check(tutorial.IsReviewing && !tutorial.IsComplete && tutorial.CurrentStepIndex == 0,
            "Completed practice can revisit Move without erasing earlier lesson outcomes.");
        bool lastWasSkipped = tutorial.WasSkipped(6);
        double moveEnd = EditorApplication.timeSinceStartup + 9;
        while (!tutorial.IsComplete && EditorApplication.timeSinceStartup < moveEnd)
        {
            if (tutorial.TargetWorldPosition.HasValue)
            {
                float dx = tutorial.TargetWorldPosition.Value.x - player.transform.position.x;
                if (Mathf.Abs(dx) > .22f) SetKeys(dx > 0 ? Key.D : Key.A); else SetKeys();
            }
            yield return null;
        }
        SetKeys();
        Check(tutorial.IsComplete && tutorial.HasPassed(0) && tutorial.WasSkipped(6) == lastWasSkipped,
            "Real movement passes reviewed Move and returns to summary, retaining other skipped outcomes.");
        Capture("boundary-review-move-passed.png"); yield return Delay(.2f);

        phase = "skip to formal from arbitrary lesson";
        yield return ClickUiButton(FindTutorialButton("Review lessons"));
        yield return ClickUiButton(FindTutorialButton("Review lesson 5"));
        yield return Delay(.2f);
        Check(tutorial.IsReviewing && tutorial.CurrentStepIndex == 4 && !tutorial.IsComplete,
            "Energy can be reopened directly from completed practice.");
        id = manager.GetInstanceID();
        yield return ClickUiButton(FindTutorialButton("Start normal trial"));
        yield return WaitForReload(id, false);
        AssertFreshFormal("Starting from reviewed Energy");
        yield return Wait(() => manager.IsBattleActive, 14, "review-start native countdown");
        Check(!manager.IsPaused && Mathf.Abs(Time.timeScale - 1) < .001f,
            "Arbitrary-lesson start leaves no slow-motion or pause state behind.");
        Record("COMPLETE BOUNDARIES: " + passed + " assertions. Real UI raycasts, every-step retry/skip, pause-release charge, " +
            "completion retry, reviewed movement and two formal boundaries. No authored gameplay data changed.");
    }

    static IEnumerator EscMenuTests()
    {
        phase = "empty no-save host fixture";
        yield return Delay(.3f);
        Check(!Object.FindFirstObjectByType<BaselineFlow>() && !SideWorldTravel.Instance && !Object.FindFirstObjectByType<BattleManager>(),
            "Empty unsaved host has no BaselineFlow, gameplay save owner, travel singleton or preloaded BattleScene.");
        Check(Application.CanStreamedLevelBeLoaded(SideWorldTravel.BattlePath),
            "Existing BuildSettings permit real additive BattleScene loading without editing build settings.");
        Scene host = SceneManager.GetActiveScene();
        var marker = new GameObject("QA host marker - no gameplay save");
        bool hostCollision = Physics.GetIgnoreLayerCollision(6, 7);
        Time.timeScale = .75f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
        CursorLockMode hostCursorLock = Cursor.lockState;
        bool hostCursorVisible = Cursor.visible;
        var travel = new GameObject("QA real additive SideWorldTravel").AddComponent<SideWorldTravel>();
        travel.Enter(false);
        yield return Wait(() => travel.IsAway && !travel.Busy && SceneManager.GetSceneByPath(SideWorldTravel.BattlePath).isLoaded,
            20, "public Enter(false) loads real BattleScene additively");
        yield return Delay(.4f); Rebind();
        Check(host.isLoaded && !marker.activeSelf && travel.HostFlow == null && Physics.GetIgnoreLayerCollision(6, 7),
            "Real travel suspends empty host marker and applies Side A collision policy; no save/reward flow is accessed.");
        Check(manager.trialMenu.IsQuickGuideOpen, "Additive entry shows the five-card quick overview before trial selection.");
        yield return ClickUiButton(FindMenuButton("Skip quick guide"));
        Check(manager.SelectionOpen && manager.IsPaused && Time.timeScale == 0,
            "Additive Side A starts in its real paused trial selection.");
        manager.trialMenu.SelectTrial(SelectedStage);
        yield return ClickUiButton(manager.trialMenu.PracticeButton);
        yield return Wait(() => manager.IsTutorialActive && tutorial.TargetWorldPosition.HasValue && !manager.IsPaused,
            5, "real practice button in additive arena");
        yield return Delay(.25f);

        phase = "removed toolbar rectangles have real combat input";
        // These are the old, deleted IMGUI rectangles, not guessed HUD points.
        // The two centers must now behave like any unobstructed battlefield.
        Rect[] oldToolbar = { new Rect(8, 60, 130, 27), new Rect(144, 60, 130, 27) };
        for (int i = 0; i < oldToolbar.Length; i++)
        {
            float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
            Vector2 center = oldToolbar[i].center;
            desiredMousePosition = new Vector2(center.x * scale, Screen.height - center.y * scale);
            SetKeys(); desiredMouseButtons = 0; yield return Delay(.18f);
            UiDiagnostic(null, "DELETED RECTANGLE CENTER " + i);
            Record("OLD RECTANGLE DIAGNOSTIC screen=" + Screen.width + "x" + Screen.height + " scale=" + scale +
                " TimeScale=" + Time.timeScale + " CanStartActions=" + player.GetComponent<BattleCombatInput>().CanStartActions);
            // Responsive HUD scale can place the current visible ESC button
            // over an old rectangle's center. That is legitimate UI protection,
            // not a deleted-toolbar dead zone: pick a verified clear point.
            desiredMousePosition = ClearPointInsideOldToolbar(oldToolbar[i]);
            yield return Delay(.1f);
            UiDiagnostic(null, "VERIFIED CLEAR DELETED RECTANGLE " + i);
            var attack = player.GetComponent<BattleAttack>();
            desiredMouseButtons = (ushort)(1 << (int)MouseButton.Left);
            yield return Wait(() => attack.IsJAttack, 2, "LEFT works in deleted toolbar rectangle " + i);
            Check(attack.IsMovementLocked && !manager.IsPaused && travel.IsAway,
                "Old toolbar rectangle " + i + " accepts actual LEFT punch without hidden button/return interception.");
            desiredMouseButtons = 0; attack.CancelForInputFocusLoss(); specials.Cancel();
            yield return Delay(.2f);
            desiredMouseButtons = (ushort)(1 << (int)MouseButton.Right);
            yield return Wait(() => specials.IsCharging && specials.Charge01 > .1f, 3,
                "RIGHT charge works in deleted toolbar rectangle " + i);
            Check(specials.Kind == BattleSpecialKind.Charge && !manager.IsPaused && travel.IsAway,
                "Old toolbar rectangle " + i + " accepts real held RIGHT energy charge; no invisible combat dead zone.");
            desiredMouseButtons = 0; specials.Cancel(); attack.CancelForInputFocusLoss();
            yield return Delay(.2f);
        }
        desiredMousePosition = null;
        Capture("esc-01-gameplay-without-duplicate-toolbar.png"); yield return Delay(.25f);

        phase = "real ESC help back continue ownership";
        yield return Press(Key.Escape);
        Check(manager.IsPaused && !manager.HelpOpen && manager.trialMenu.CurrentView == BattleTrialMenu.View.Pause && Time.timeScale == 0,
            "Actual ESC opens one paused menu in additive practice.");
        Check(MenuButtonText("Resume").Contains("[ESC]") && MenuButtonText("Help").Contains("[H]") &&
            MenuButtonText("Return").Contains("[B]"), "Pause actions show CONTINUE [ESC], HELP [H] and MAIN WORLD [B].");
        Capture("esc-02-pause-menu.png"); yield return Delay(.25f);
        float pausedAge = tutorial.StepAge;
        yield return ClickUiButton(FindMenuButton("Help"));
        Check(manager.HelpOpen && manager.IsPaused && manager.trialMenu.CurrentView == BattleTrialMenu.View.Controls && Time.timeScale == 0,
            "Actual HELP pointer click opens controls without resuming or firing an attack.");
        Capture("esc-03-help-from-pause.png"); yield return Delay(.2f);
        yield return ClickUiButton(FindMenuButton("Close"));
        Check(!manager.HelpOpen && manager.IsPaused && manager.trialMenu.CurrentView == BattleTrialMenu.View.Pause && Time.timeScale == 0,
            "Actual BACK pointer click returns from help to Pause, not gameplay.");
        yield return Press(Key.H);
        Check(manager.HelpOpen && manager.IsPaused && Time.timeScale == 0, "H from Pause opens help while keeping time frozen.");
        yield return Press(Key.Escape);
        Check(!manager.HelpOpen && manager.IsPaused && manager.trialMenu.CurrentView == BattleTrialMenu.View.Pause && Time.timeScale == 0 &&
            Mathf.Abs(tutorial.StepAge - pausedAge) < .01f,
            "ESC from help returns to Pause and neither help path advances the lesson clock.");
        yield return ClickUiButton(FindMenuButton("Resume"));
        Check(!manager.IsPaused && !manager.HelpOpen && manager.trialMenu.CurrentView == BattleTrialMenu.View.None &&
            Mathf.Abs(Time.timeScale - 1f) < .001f && !player.GetComponent<BattleAttack>().IsMovementLocked && !specials.IsBusy,
            "Actual CONTINUE pointer click restores normal practice with no menu-click attack leakage.");

        phase = "real main world button restores no-save host";
        yield return Press(Key.Escape);
        yield return ClickUiButton(FindMenuButton("Return"));
        yield return Wait(() => !travel.IsAway && !travel.Busy && marker.activeSelf, 12, "pause MAIN WORLD returns additive empty host");
        Check(SceneManager.GetActiveScene() == host && !SceneManager.GetSceneByPath(SideWorldTravel.BattlePath).isLoaded &&
            Mathf.Abs(Time.timeScale - .75f) < .001f && Cursor.lockState == hostCursorLock && Cursor.visible == hostCursorVisible &&
            Physics.GetIgnoreLayerCollision(6, 7) == hostCollision && !travel.TrialACompleted && travel.HostFlow == null,
            "MAIN WORLD unloads only Side A, reactivates host marker, and restores host time/cursor/layer collision without rewards.");

        phase = "second additive visit and global B return";
        travel.Enter(false);
        yield return Wait(() => travel.IsAway && !travel.Busy && SceneManager.GetSceneByPath(SideWorldTravel.BattlePath).isLoaded,
            15, "second real additive entry");
        yield return Delay(.4f); Rebind();
        Check(manager.trialMenu.IsQuickGuideOpen, "A new host visit shows the quick overview again, not only the first session.");
        yield return ClickUiButton(FindMenuButton("Skip quick guide"));
        manager.trialMenu.SelectTrial(SelectedStage);
        yield return ClickUiButton(manager.trialMenu.PracticeButton);
        yield return Wait(() => manager.IsTutorialActive && !manager.IsPaused, 5, "second actual practice entry");
        yield return Press(Key.B);
        yield return Wait(() => !travel.IsAway && !travel.Busy && marker.activeSelf, 12, "global B returns from active practice");
        Check(SceneManager.GetActiveScene() == host && !SceneManager.GetSceneByPath(SideWorldTravel.BattlePath).isLoaded &&
            Mathf.Abs(Time.timeScale - .75f) < .001f && Cursor.visible == hostCursorVisible &&
            Cursor.lockState == hostCursorLock && Physics.GetIgnoreLayerCollision(6, 7) == hostCollision && !travel.TrialACompleted,
            "Global B remains functional on a second visit and restores the same host state without a formal result/reward.");
        Record("COMPLETE ESC MENU: " + passed + " assertions. Real additive loading, pointer callbacks, keyboard and attack/charge states. " +
            "Empty QA host only: no real main-world gameplay save, reward persistence or human comfort acceptance is claimed.");
    }

    static IEnumerator MovingComboTests()
    {
        phase = "moving-combo public lesson fixture";
        yield return Delay(.6f); Rebind();
        Check(!SideWorldTravel.Instance && manager.SelectionOpen,
            "Moving-combo smoke uses the existing standalone arena with no user save or reward host.");
        oldComboLayerCollision = Physics.GetIgnoreLayerCollision(6, 7);
        Physics.IgnoreLayerCollision(6, 7, true);
        comboCollisionModified = true;
        Record("FIXTURE apply the same IgnoreLayerCollision(6,7,true) policy as real SideWorldTravel.Enter(false). " +
            "Moving combos therefore cannot pass merely because standalone player/enemy body collisions pin the target. Finish restores the prior policy.");
        manager.trialMenu.SelectTrial(SelectedStage);
        manager.BeginTutorialPractice();
        yield return Wait(() => manager.IsTutorialActive && tutorial.TargetWorldPosition.HasValue, 5, "real practice setup");
        yield return Delay(.2f);
        while (!tutorial.IsComplete) tutorial.SkipStep();
        Record("FIXTURE public SkipStep opens the completed lesson summary; public ReviewStep(3) will reset each real Combo pair. " +
            "These setup skips are not successful combat, and no private progress/damage/landed state is assigned.");
        yield return ConfirmedMovingChain("held D right-facing", 1, false, true);
        yield return ConfirmedMovingChain("held A left-facing", -1, false, true);
        yield return ConfirmedMovingChain("directions released neutral regression", 1, false, false);
        yield return ConfirmedMovingChain("alternating D held and released", 1, true, true);

        phase = "per-punch commitment and next-punch steering";
        yield return PrepareComboReview(false);
        var attack = player.GetComponent<BattleAttack>();
        SetKeys(Key.D); yield return Delay(.04f); SetKeys(Key.D, Key.J);
        yield return Wait(() => attack.IsJAttack && attack.CurrentJStep == 0, 2, "real D+J begins committed first punch");
        SetKeys(Key.A); yield return Delay(.04f);
        Check(attack.IsJAttack && attack.CurrentJStep == 0 && attack.CommittedDirection > 0 && attack.attackPoint.localPosition.x > 0,
            "Pressing opposite A during the first D+J punch does not flip its committed hitbox or visual facing.");
        SetKeys(Key.D);
        yield return Wait(() => attack.IsJAttack && attack.CurrentJStep == 0 && attack.LastAttackLanded, 3, "committed first punch still genuinely lands");
        SetKeys(Key.A, Key.J);
        yield return Wait(() => attack.IsJAttack && attack.CurrentJStep == 1, 3, "actual A+J buffers second punch");
        SetKeys();
        Check(attack.CurrentJMove == BattleJMove.Neutral && attack.CommittedDirection < 0 && attack.attackPoint.localPosition.x < 0,
            "The next confirmed combo punch samples the new A direction at Begin without replacing the four-hit chain.");
        attack.CancelForInputFocusLoss(); tutorial.SkipStep();

        phase = "real whiff cannot force a combo continuation";
        yield return PrepareComboReview(false);
        Vector3 distant = enemy.transform.position + Vector3.right * 6;
        PlaceComboBody(enemy, distant);
        Record("FIXTURE enemy body moved six world units away before input, to test an honest miss; no contact flags or damage assigned.");
        float damageBefore = enemy.GetComponent<EnemyDamage>().damagePercent;
        int serialBefore = enemy.GetComponent<EnemyDamage>().PresentationSerial;
        int presentationBefore = player.GetComponent<BattleJPresentation>().HitCount;
        SetKeys(Key.D, Key.J);
        yield return Wait(() => attack.IsJAttack && attack.CurrentJStep == 0, 2, "real distant D+J starts punch one");
        SetKeys(Key.D); yield return Delay(.035f);
        SetKeys(Key.D, Key.J); yield return Delay(.055f); SetKeys(Key.D);
        yield return Wait(() => attack.LastAttackResolved && !attack.LastAttackLanded, 3, "genuine missed punch resolves");
        Check(!attack.LastAttackConnected && enemy.GetComponent<EnemyDamage>().damagePercent == damageBefore &&
            enemy.GetComponent<EnemyDamage>().PresentationSerial == serialBefore && player.GetComponent<BattleJPresentation>().HitCount == presentationBefore,
            "The distant real whiff changes no enemy damage/contact serial or successful-hit feedback.");
        yield return Wait(() => !attack.IsMovementLocked, 3, "whiff pays native recovery");
        Check(attack.CurrentJStep < 0 && tutorial.ComboHits == 0,
            "Buffered D+J cannot hard-continue a missed punch: native recovery resets the chain instead of advancing it.");
        SetKeys(); attack.CancelForInputFocusLoss(); tutorial.SkipStep();

        phase = "real keyboard routing and unchanged K families";
        yield return PrepareComboReview(false);
        Key[][] keySets = { new Key[0], new[] { Key.A }, new[] { Key.D }, new[] { Key.A, Key.D },
            new[] { Key.W }, new[] { Key.S }, new[] { Key.W, Key.D }, new[] { Key.S, Key.A },
            new[] { Key.W, Key.S, Key.D }, new[] { Key.W, Key.S, Key.A, Key.D } };
        BattleJMove[] expectedJ = { BattleJMove.Neutral, BattleJMove.Neutral, BattleJMove.Neutral, BattleJMove.Neutral,
            BattleJMove.Upper, BattleJMove.Low, BattleJMove.Upper, BattleJMove.Low, BattleJMove.Neutral, BattleJMove.Neutral };
        BattleSpecialKind[] expectedK = { BattleSpecialKind.Charge, BattleSpecialKind.Dash, BattleSpecialKind.Dash, BattleSpecialKind.Charge,
            BattleSpecialKind.Rise, BattleSpecialKind.Counter, BattleSpecialKind.Rise, BattleSpecialKind.Counter, BattleSpecialKind.Dash, BattleSpecialKind.Charge };
        for (int i = 0; i < keySets.Length; i++)
        {
            SetKeys(keySets[i]);
            int request = desiredRouteVersion;
            yield return Wait(() => observedRouteVersion >= request, 2, "new Dynamic input routing sample for key case " + i);
            Check(observedJRoute == expectedJ[i] && observedKRoute == expectedK[i],
                "Actual Dynamic keyboard routing case " + i + ": J=" + observedJRoute + "; K=" + observedKRoute +
                "; frame=" + observedRouteFrame + "; observed=" + observedRouteKeys + " (vertical conflicts cancel only their axis).");
        }
        SetKeys();
        foreach (Key direction in new[] { Key.W, Key.S })
        {
            tutorial.RetryStep(); yield return Delay(.12f);
            SetKeys(direction, Key.J);
            yield return Wait(() => attack.IsJAttack, 2, "actual directional J startup " + direction);
            SetKeys();
            var expected = direction == Key.W ? BattleJMove.Upper : BattleJMove.Low;
            Check(attack.CurrentJMove == expected && attack.CurrentJStep < 0,
                "Actual " + direction + "+J still starts its original standalone " + expected + ", not a neutral chain.");
            attack.CancelForInputFocusLoss();
        }
        Record("COMPLETE MOVING COMBO: " + passed + " assertions. Four real four-hit chains, left body-placement fixture, " +
            "per-punch facing commitment, honest whiff gate and W/S/K arbitration. Blocked-hit continuation and human feel require separate checks.");
    }

    static IEnumerator PrepareComboReview(bool left)
    {
        SetKeys(); desiredMouseButtons = 0;
        if (!tutorial.IsComplete) tutorial.SkipStep();
        tutorial.ReviewStep(3);
        yield return Delay(.16f);
        Check(tutorial.IsReviewing && tutorial.CurrentLesson == BattleTutorial.Lesson.Combo && !manager.IsPaused,
            "Public Combo review fixture resets the real pair without assigning damage or successful hits.");
        if (left)
        {
            float center = manager.GetComponent<BattleArenaStages>().mainGround.GetComponent<Collider>().bounds.center.x;
            Vector3 p = player.transform.position, e = enemy.transform.position;
            p.x = center * 2 - p.x; e.x = center * 2 - e.x;
            PlaceComboBody(player.gameObject, p); PlaceComboBody(enemy, e);
            player.FaceLaunchDirection(-1);
            var enemyAnimator = enemy.GetComponentInChildren<Animator>();
            if (enemyAnimator) enemyAnimator.transform.rotation = Quaternion.Euler(0, 90, 0);
            Physics.SyncTransforms();
            Record("FIXTURE left-facing pair mirrored about deck center before input, using body/transform positions and public facing only.");
        }
    }

    static void PlaceComboBody(GameObject fighter, Vector3 position)
    {
        var body = fighter.GetComponent<Rigidbody>();
        fighter.transform.position = position;
        if (body) { body.position = position; body.linearVelocity = body.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }

    static IEnumerator ConfirmedMovingChain(string label, int facing, bool alternate, bool directionHeld)
    {
        phase = "real four-hit chain: " + label;
        yield return PrepareComboReview(facing < 0);
        var attack = player.GetComponent<BattleAttack>();
        var damage = enemy.GetComponent<EnemyDamage>();
        var presentation = player.GetComponent<BattleJPresentation>();
        int serialBefore = damage.PresentationSerial, feedbackBefore = presentation.HitCount;
        float damageBefore = damage.damagePercent;
        for (int hit = 0; hit < 4; hit++)
        {
            bool held = directionHeld && (!alternate || hit % 2 == 1);
            Key[] move = held ? new[] { facing > 0 ? Key.D : Key.A } : new Key[0];
            SetKeys(move); yield return Delay(.04f);
            SetKeys(held ? new[] { move[0], Key.J } : new[] { Key.J });
            int index = hit;
            yield return Wait(() => attack.IsJAttack && attack.CurrentJStep == index, 3, label + " starts native hit " + (hit + 1));
            SetKeys(move);
            Record(MovingComboDiagnostic(label + " BEGIN " + (hit + 1), attack));
            var expected = BattleJMoveSet.Get(BattleJMove.Neutral, hit);
            Check(attack.CurrentJMove == BattleJMove.Neutral && Mathf.Sign(attack.CommittedDirection) == facing &&
                attack.JData.Damage == expected.Damage && attack.JData.Startup == expected.Startup && attack.JData.Recovery == expected.Recovery,
                label + " punch " + (hit + 1) + " uses the original neutral timing/damage and intended committed facing.");
            bool recordedActive = false, recordedMiss = false;
            yield return Wait(() =>
            {
                if (attack.IsJAttack && attack.CurrentJStep == index)
                {
                    if (!recordedActive && attack.JElapsed >= attack.JData.Startup)
                    {
                        recordedActive = true;
                        Record(MovingComboDiagnostic(label + " FIRST ACTIVE " + (index + 1), attack));
                    }
                    if (!recordedMiss && attack.LastAttackResolved && !attack.LastAttackLanded)
                    {
                        recordedMiss = true;
                        Record(MovingComboDiagnostic(label + " RESOLVED MISS " + (index + 1), attack));
                        Capture("moving-combo-diagnostic-miss-" + index + ".png");
                    }
                }
                return attack.IsJAttack && attack.CurrentJStep == index && attack.LastAttackLanded &&
                    damage.PresentationSerial > serialBefore + index;
            }, 4, label + " lands genuine hit " + (hit + 1));
            Record(MovingComboDiagnostic(label + " LANDED " + (hit + 1), attack));
            Check(presentation.HitCount == feedbackBefore + hit + 1 && damage.damagePercent > damageBefore,
                label + " punch " + (hit + 1) + " genuinely damages the opponent and emits successful J feedback exactly once.");
            if (hit == 3)
            {
                Check(attack.JData.Damage == 8 && attack.JData.Knockback == .90f && attack.JData.Recovery == .70f && presentation.ActiveContacts > 0,
                    label + " fourth hit retains heavy 8-damage/.90-knockback/.70-recovery data and an actual visible contact burst.");
                Capture("moving-combo-" + (facing < 0 ? "left" : alternate ? "mixed" : directionHeld ? "right" : "neutral") + "-heavy-fourth.png");
            }
        }
        SetKeys();
        yield return Wait(() => tutorial.IsComplete, 4, label + " real four contacts complete reviewed lesson");
        Check(damage.PresentationSerial == serialBefore + 4 && presentation.HitCount == feedbackBefore + 4,
            label + " completes exactly four real contacts, without fabricated landed/progress state.");
        yield return Delay(.1f);
    }

    static string MovingComboDiagnostic(string label, BattleAttack attack)
    {
        var pb = player.GetComponent<Rigidbody>(); var eb = enemy.GetComponent<Rigidbody>();
        var pc = player.GetComponent<Collider>(); var ec = enemy.GetComponent<Collider>();
        Vector3 p = player.transform.position, e = enemy.transform.position;
        float direction = attack.CommittedDirection;
        Vector3 center = p + new Vector3(attack.JData.Center.x * direction, attack.JData.Center.y, 0);
        var hits = Physics.OverlapBox(center, attack.JData.HalfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        bool enemyOverlaps = false;
        foreach (var hit in hits) if (hit.GetComponentInParent<EnemyDamage>() == enemy.GetComponent<EnemyDamage>()) enemyOverlaps = true;
        return "COMBO DIAGNOSTIC " + label + " direction=" + direction + " elapsed=" + attack.JElapsed.ToString("F4") +
            " timescale=" + Time.timeScale + " P.transform=" + p.ToString("F4") + " E.transform=" + e.ToString("F4") +
            " P.body=" + pb.position.ToString("F4") + " E.body=" + eb.position.ToString("F4") +
            " P.velocity=" + pb.linearVelocity.ToString("F4") + " E.velocity=" + eb.linearVelocity.ToString("F4") +
            " signedGap.transform=" + ((e.x - p.x) * direction).ToString("F4") +
            " signedGap.body=" + ((eb.position.x - pb.position.x) * direction).ToString("F4") +
            " P.bounds=" + pc.bounds + " E.bounds=" + ec.bounds +
            " J.center=" + center.ToString("F4") + " J.half=" + attack.JData.HalfExtents.ToString("F4") +
            " enemyOverlaps=" + enemyOverlaps + " observedKeys=" + observedRouteKeys;
    }

    // Visual-only overview regression. The clone uses the authored character
    // meshes/animation clips but never runs an alternate combat simulation.
    static IEnumerator QuickGuideTests()
    {
        phase = "quick guide empty no-save host fixture";
        yield return Delay(.3f);
        Check(!Object.FindFirstObjectByType<BaselineFlow>() && !SideWorldTravel.Instance && !Object.FindFirstObjectByType<BattleManager>(),
            "Quick-guide QA host is empty and unsaved, without a gameplay save/reward owner.");
        Check(Application.CanStreamedLevelBeLoaded(SideWorldTravel.BattlePath),
            "Existing build settings support the actual additive Side A entry.");
        Scene host = SceneManager.GetActiveScene();
        var marker = new GameObject("QA quick-guide empty host marker - no save");
        Time.timeScale = .75f;
        var travel = new GameObject("QA quick-guide real additive travel").AddComponent<SideWorldTravel>();
        travel.Enter(false);
        yield return Wait(() => travel.IsAway && !travel.Busy && SceneManager.GetSceneByPath(SideWorldTravel.BattlePath).isLoaded,
            20, "real additive host-to-Side-A entry");
        yield return Delay(.4f); Rebind();
        Check(host.isLoaded && !marker.activeSelf && travel.HostFlow == null && manager.trialMenu.IsQuickGuideOpen &&
            manager.trialMenu.QuickGuidePage == 0 && manager.IsPaused && !manager.IsBattleActive && Time.timeScale == 0,
            "Real host entry automatically opens card 1 before selection, with the world paused and no duel started.");
        Check(!FindMenuButton("Previous quick lesson").interactable,
            "Previous is visibly disabled on the first quick-guide card.");
        Vector3 playerStart = player.transform.position, enemyStart = enemy.transform.position;
        float playerDamage = player.GetComponent<BattleDamageReceiver>().damagePercent;
        float enemyDamage = enemy.GetComponent<BattleDamageReceiver>().damagePercent;
        int playerLives = player.GetComponent<BattleStockLives>().Remaining;
        int enemyLives = enemy.GetComponent<BattleStockLives>().Remaining;

        for (int page = 0; page < 5; page++)
        {
            phase = "real looping quick-guide card " + (page + 1);
            Check(manager.trialMenu.IsQuickGuideOpen && manager.trialMenu.QuickGuidePage == page,
                "Requested five-card overview order is intact at card " + (page + 1) + ".");
            yield return SampleQuickGuidePage(page);
            Check(Vector3.Distance(player.transform.position, playerStart) < .001f &&
                Vector3.Distance(enemy.transform.position, enemyStart) < .001f &&
                player.GetComponent<BattleDamageReceiver>().damagePercent == playerDamage &&
                enemy.GetComponent<BattleDamageReceiver>().damagePercent == enemyDamage &&
                player.GetComponent<BattleStockLives>().Remaining == playerLives &&
                enemy.GetComponent<BattleStockLives>().Remaining == enemyLives &&
                manager.BattleDuration == 0 && manager.IsPaused && Time.timeScale == 0,
                "Card " + (page + 1) + " animates without moving, damaging or spending stocks on the actual frozen fighters.");
            if (page == 0) yield return ClickUiButton(FindMenuButton("Next quick lesson"));
            else if (page == 1) yield return Press(Key.RightArrow);
            else if (page == 2)
            {
                yield return ClickUiButton(FindMenuButton("Previous quick lesson"));
                Check(manager.trialMenu.IsQuickGuideOpen && manager.trialMenu.QuickGuidePage == 1,
                    "Actual previous pointer callback returns exactly one card.");
                yield return Press(Key.RightArrow);
                Check(manager.trialMenu.QuickGuidePage == 2 && manager.IsPaused,
                    "Actual Right Arrow restores card 3 without unpausing.");
                yield return Press(Key.Enter);
            }
            else if (page == 3) yield return ClickUiButton(FindMenuButton("Next quick lesson"));
        }
        SetKeys(Key.W, Key.D, Key.Space, Key.J, Key.K); yield return Delay(.2f); SetKeys();
        yield return Delay(.2f);
        Check(manager.trialMenu.IsQuickGuideOpen && manager.trialMenu.QuickGuidePage == 4 &&
            Vector3.Distance(player.transform.position, playerStart) < .001f &&
            !player.GetComponent<BattleAttack>().IsMovementLocked && !specials.IsBusy,
            "Real WASD/Space/J/K input over the overview cannot leak into the paused combatants.");
        Check(MenuButtonText("Next quick lesson").Contains("TRIAL SELECTION"),
            "The last card explicitly says it continues to trial selection, not directly into combat.");
        yield return Press(Key.Enter);
        Check(!manager.trialMenu.IsQuickGuideOpen && manager.SelectionOpen &&
            manager.trialMenu.CurrentView == BattleTrialMenu.View.Selection && manager.IsPaused &&
            !manager.IsBattleActive && !manager.HelpOpen && Time.timeScale == 0,
            "Actual final Enter closes only the overview: the same input does not also start a duel.");
        Capture("quick-06-finished-at-trial-selection.png"); yield return Delay(.25f);

        phase = "selection help rewatch preserves return stack";
        yield return ClickUiButton(FindMenuButton("Help"));
        yield return ClickUiButton(FindMenuButton("Quick guide"));
        yield return Wait(() => manager.trialMenu.IsQuickGuideOpen && manager.trialMenu.QuickGuidePreview.Ready,
            6, "rewatch overview from selection controls");
        Check(manager.HelpOpen && manager.IsPaused && manager.trialMenu.QuickGuidePage == 0,
            "Controls can reopen the concise visual guide at its first card without starting combat.");
        yield return Press(Key.Escape);
        Check(manager.HelpOpen && manager.IsPaused && manager.trialMenu.CurrentView == BattleTrialMenu.View.Controls,
            "Escape from a rewatched guide returns to Controls and retains help pause ownership.");
        yield return ClickUiButton(FindMenuButton("Close"));
        Check(manager.SelectionOpen && !manager.HelpOpen && manager.trialMenu.CurrentView == BattleTrialMenu.View.Selection && Time.timeScale == 0,
            "Back from selection Controls returns to selection instead of losing the original return stack.");

        phase = "practice help rewatch preserves pause stack";
        manager.trialMenu.SelectTrial(SelectedStage);
        yield return ClickUiButton(manager.trialMenu.PracticeButton);
        yield return Wait(() => manager.IsTutorialActive && !manager.IsPaused, 5, "normal practice remains available after quick overview");
        yield return Press(Key.Escape);
        yield return ClickUiButton(FindMenuButton("Help"));
        float lessonAge = tutorial.StepAge;
        yield return ClickUiButton(FindMenuButton("Quick guide"));
        yield return Wait(() => manager.trialMenu.IsQuickGuideOpen && manager.trialMenu.QuickGuidePreview.Ready,
            6, "rewatch overview from practice pause controls");
        yield return Delay(.4f);
        yield return ClickUiButton(FindMenuButton("Skip quick guide"));
        Check(manager.HelpOpen && manager.IsPaused && manager.trialMenu.CurrentView == BattleTrialMenu.View.Controls &&
            Mathf.Abs(tutorial.StepAge - lessonAge) < .01f && Time.timeScale == 0,
            "Skip from a manually reopened guide returns to frozen Controls, not selection or live gameplay.");
        yield return ClickUiButton(FindMenuButton("Close"));
        Check(!manager.HelpOpen && manager.IsPaused && manager.trialMenu.CurrentView == BattleTrialMenu.View.Pause,
            "Help Back still returns to the original Pause after the nested visual guide.");
        yield return ClickUiButton(FindMenuButton("Resume"));
        Check(manager.IsTutorialActive && !manager.IsPaused && manager.trialMenu.CurrentView == BattleTrialMenu.View.None && Time.timeScale == 1,
            "Continue restores active practice after the nested guide without attack leakage.");

        phase = "actual practice-to-duel reload and explicit retry fixture";
        int previousManager = manager.GetInstanceID();
        yield return ClickUiButton(FindTutorialButton("Start normal trial"));
        yield return Wait(() =>
        {
            var next = Object.FindFirstObjectByType<BattleManager>();
            return next && next.GetInstanceID() != previousManager && next.IsBattleActive;
        }, 15, "normal start-duel reload completes intro");
        Rebind();
        Check(!manager.trialMenu.IsQuickGuideOpen && !manager.SelectionOpen && !manager.IsTutorialActive &&
            manager.CurrentTrialIndex == SelectedStage && player.GetComponent<BattleStockLives>().Remaining == 2 &&
            enemy.GetComponent<BattleStockLives>().Remaining == 2,
            "Practice-to-duel reload does not repeat the entry guide and keeps the selected stage with fresh stocks.");
        previousManager = manager.GetInstanceID();
        Record("FIXTURE public RestartBattle() requests the real additive retry; no result or save is fabricated.");
        manager.RestartBattle();
        yield return Wait(() =>
        {
            var next = Object.FindFirstObjectByType<BattleManager>();
            return next && next.GetInstanceID() != previousManager && next.IsBattleActive;
        }, 15, "real additive RetryBattle reload");
        Rebind();
        Check(!manager.trialMenu.IsQuickGuideOpen && !manager.SelectionOpen && !manager.IsPaused &&
            manager.CurrentTrialIndex == SelectedStage && !travel.Busy,
            "A same-visit formal retry starts normally and does not repeat the entry overview.");
        yield return Press(Key.Escape);
        yield return ClickUiButton(FindMenuButton("Return"));
        yield return Wait(() => !travel.IsAway && !travel.Busy && marker.activeSelf, 12, "return from formal retry to empty host");
        Check(SceneManager.GetActiveScene() == host && Mathf.Abs(Time.timeScale - .75f) < .001f && travel.HostFlow == null,
            "Returning restores the original no-save host and its pre-entry time scale.");

        phase = "new visit reopens and Escape skips one level only";
        travel.Enter(false);
        yield return Wait(() => travel.IsAway && !travel.Busy && SceneManager.GetSceneByPath(SideWorldTravel.BattlePath).isLoaded,
            15, "second real host-to-Side-A visit");
        yield return Delay(.35f); Rebind();
        Check(manager.trialMenu.IsQuickGuideOpen && manager.trialMenu.QuickGuidePage == 0 && manager.IsPaused,
            "A later real host visit presents the overview again at card 1.");
        yield return Press(Key.Escape);
        Check(travel.IsAway && manager.SelectionOpen && manager.trialMenu.CurrentView == BattleTrialMenu.View.Selection &&
            manager.IsPaused && !manager.IsBattleActive && !manager.HelpOpen,
            "First Escape skips only the entry guide: it does not also exit Side A or open help.");
        yield return Press(Key.Escape);
        yield return Wait(() => !travel.IsAway && !travel.Busy && marker.activeSelf, 12, "second Escape returns from selection to host");
        Check(SceneManager.GetActiveScene() == host && Mathf.Abs(Time.timeScale - .75f) < .001f && !travel.TrialACompleted,
            "Second Escape follows the existing selection return, with no completion/reward side effect.");
        Record("COMPLETE QUICK GUIDE: " + passed + " assertions. Five real-avatar looping cards, frozen real fighters, actual pointer/key " +
            "navigation, nested help/pause ownership, two real host visits and additive retry. No authored scene, prefab or gameplay save changed.");
    }

    static IEnumerator SampleQuickGuidePage(int page)
    {
        var menu = manager.trialMenu;
        yield return Wait(() => menu.QuickGuidePreview && menu.QuickGuidePreview.Ready, 8, "real-avatar preview ready on page " + (page + 1));
        var preview = menu.QuickGuidePreview;
        Check(preview.Texture && preview.Texture.IsCreated() && string.IsNullOrEmpty(preview.Error),
            "Card " + (page + 1) + " has an actual allocated RenderTexture and no preview error.");
        var avatars = preview.GetComponentsInChildren<Animator>(true);
        Animator avatar = null;
        foreach (var candidate in avatars) if (candidate && candidate.isHuman) { avatar = candidate; break; }
        Check(avatar && avatar.avatar && avatar.avatar.isValid,
            "Card " + (page + 1) + " demonstration contains an authored humanoid avatar, not a placeholder silhouette.");
        float firstProgress = preview.Progress;
        var firstKeys = preview.Keys;
        int firstPose = QuickGuidePoseFingerprint(avatar), firstPixels = QuickGuidePixelFingerprint(preview.Texture);
        bool progressChanged = false, keysChanged = false, poseChanged = false, pixelsChanged = false;
        BattleQuickGuidePreview.DemoKeys observed = BattleQuickGuidePreview.DemoKeys.None;
        float[] cycleSeconds = { 6.4f, 4.6f, 8.6f, 5.4f, 7.8f };
        double started = EditorApplication.timeSinceStartup;
        bool capturedA = false, capturedB = false;
        while (EditorApplication.timeSinceStartup - started < cycleSeconds[page])
        {
            yield return Delay(.045f);
            progressChanged |= Mathf.Abs(preview.Progress - firstProgress) > .025f;
            keysChanged |= preview.Keys != firstKeys;
            observed |= preview.Keys;
            poseChanged |= QuickGuidePoseFingerprint(avatar) != firstPose;
            if (!capturedA && EditorApplication.timeSinceStartup - started > .6)
            {
                capturedA = true;
                Capture("quick-0" + (page + 1) + "-phase-a.png");
                pixelsChanged |= QuickGuidePixelFingerprint(preview.Texture) != firstPixels;
            }
            if (!capturedB && EditorApplication.timeSinceStartup - started > 1.5)
            {
                capturedB = true;
                Capture("quick-0" + (page + 1) + "-phase-b.png");
                pixelsChanged |= QuickGuidePixelFingerprint(preview.Texture) != firstPixels;
            }
            if (preview.Progress < 0 || preview.Progress > 1.001f || Time.timeScale != 0)
                throw new InvalidOperationException("Preview progress/time ownership invalid on page " + page);
        }
        Check(progressChanged && keysChanged && poseChanged && pixelsChanged && !string.IsNullOrEmpty(preview.ActionLabel),
            "Card " + (page + 1) + " actually changes progress, pressed keys, humanoid bone pose and rendered pixels using unscaled time.");
        var flags = BattleQuickGuidePreview.DemoKeys.None;
        if (page == 0) flags = BattleQuickGuidePreview.DemoKeys.A | BattleQuickGuidePreview.DemoKeys.D | BattleQuickGuidePreview.DemoKeys.Space;
        else if (page == 1) flags = BattleQuickGuidePreview.DemoKeys.J;
        else if (page == 2) flags = BattleQuickGuidePreview.DemoKeys.W | BattleQuickGuidePreview.DemoKeys.S | BattleQuickGuidePreview.DemoKeys.J;
        else if (page == 3) flags = BattleQuickGuidePreview.DemoKeys.K;
        else flags = BattleQuickGuidePreview.DemoKeys.W | BattleQuickGuidePreview.DemoKeys.S | BattleQuickGuidePreview.DemoKeys.K;
        Check((observed & flags) == flags,
            "Card " + (page + 1) + " visually demonstrates its required actual key families; observed=" + observed + ".");
    }

    static int QuickGuidePoseFingerprint(Animator avatar)
    {
        unchecked
        {
            int hash = 17;
            foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.Chest })
            {
                var joint = avatar.GetBoneTransform(bone);
                if (!joint) continue;
                Quaternion q = joint.localRotation; Vector3 p = joint.localPosition;
                hash = hash * 31 + Mathf.RoundToInt(q.x * 10000);
                hash = hash * 31 + Mathf.RoundToInt(q.y * 10000);
                hash = hash * 31 + Mathf.RoundToInt(q.z * 10000);
                hash = hash * 31 + Mathf.RoundToInt(q.w * 10000);
                hash = hash * 31 + Mathf.RoundToInt(p.x * 10000);
                hash = hash * 31 + Mathf.RoundToInt(p.y * 10000);
                hash = hash * 31 + Mathf.RoundToInt(p.z * 10000);
            }
            return hash;
        }
    }

    static int QuickGuidePixelFingerprint(RenderTexture source)
    {
        var previous = RenderTexture.active;
        var tiny = RenderTexture.GetTemporary(64, 36, 0, RenderTextureFormat.ARGB32);
        var image = new Texture2D(64, 36, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(source, tiny); RenderTexture.active = tiny;
            image.ReadPixels(new Rect(0, 0, 64, 36), 0, 0); image.Apply();
            unchecked
            {
                int hash = 17;
                foreach (var pixel in image.GetPixels32())
                {
                    hash = hash * 31 + pixel.r; hash = hash * 31 + pixel.g; hash = hash * 31 + pixel.b;
                }
                return hash;
            }
        }
        finally
        {
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(tiny); Object.Destroy(image);
        }
    }

    static Button FindMenuButton(string name)
    {
        foreach (var button in manager.trialMenu.GetComponentsInChildren<Button>(true))
            if (button.name == name && button.gameObject.activeInHierarchy) return button;
        throw new InvalidOperationException("Active trial menu button missing: " + name);
    }
    static string MenuButtonText(string name)
    {
        var label = FindMenuButton(name).GetComponentInChildren<TMPro.TMP_Text>();
        return label ? label.text : "";
    }

    static Vector2 ClearPointInsideOldToolbar(Rect rectangle)
    {
        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
        float[] fractions = { .5f, .85f, .15f, .95f, .05f };
        foreach (float x in fractions)
            foreach (float y in fractions)
            {
                Vector2 point = new Vector2((rectangle.x + rectangle.width * x) * scale,
                    Screen.height - (rectangle.y + rectangle.height * y) * scale);
                var hits = new List<RaycastResult>();
                var system = EventSystem.current;
                if (system) system.RaycastAll(new PointerEventData(system) { position = point }, hits);
                bool occupied = false;
                foreach (var hit in hits)
                {
                    var selectable = hit.gameObject.GetComponentInParent<Selectable>();
                    if (selectable && selectable.IsActive() && selectable.IsInteractable()) { occupied = true; break; }
                }
                if (!occupied)
                {
                    Record("Verified old toolbar test point=" + point + " inside deleted rect=" + rectangle + " has no active Selectable raycast.");
                    return point;
                }
            }
        throw new InvalidOperationException("Current visible HUD covers the entire old toolbar rectangle; cannot assert deleted-area mouse input at this viewport.");
    }

    static IEnumerator ChargeBoundaryTests()
    {
        phase = "isolated pause-charge fixture";
        yield return Delay(.6f); Rebind();
        manager.trialMenu.SelectTrial(SelectedStage);
        manager.BeginTutorialPractice();
        yield return Wait(() => manager.IsTutorialActive && tutorial.TargetWorldPosition.HasValue, 5, "public practice fixture entry");
        yield return Delay(.2f);
        while (tutorial.CurrentStepIndex < 4) tutorial.SkipStep();
        Record("FIXTURE public SkipStep advanced to Energy; this smoke does not claim full lesson completion or UI click coverage.");
        yield return Delay(.25f);
        Check(tutorial.CurrentStepIndex == 4 && manager.IsTutorialActive, "Public API fixture reaches real Energy lesson.");
        yield return PausedChargeBoundary(false);
        Record("COMPLETE CHARGE BOUNDARY: real held RIGHT mouse plus H pause/release/resume, no fabricated charge progress.");
    }

    static IEnumerator PausedChargeBoundary(bool retryThroughUi = true)
    {
        phase = "paused charged mouse release";
        SetKeys(); desiredMousePosition = null;
        desiredMouseButtons = (ushort)(1 << (int)MouseButton.Right);
        yield return Wait(() => specials.IsCharging && specials.Charge01 >= .2f, 4, "real right-mouse charge before pause");
        yield return Press(Key.H);
        Check(manager.HelpOpen && manager.IsPaused && Time.timeScale == 0,
            "H pauses an actual owned right-mouse charge.");
        float damageBefore = enemy.GetComponent<EnemyDamage>().damagePercent;
        int contactBefore = enemy.GetComponent<EnemyDamage>().PresentationSerial;
        desiredMouseButtons = 0;
        yield return Delay(.3f);
        Check(enemy.GetComponent<EnemyDamage>().damagePercent == damageBefore && !tutorial.ShortBeamComplete && !tutorial.ChargedBeamComplete,
            "Releasing charge during pause neither hits the rival nor completes the lesson while hidden.");
        yield return Press(Key.H);
        yield return Delay(.8f);
        Check(!manager.IsPaused && !specials.IsBusy && enemy.GetComponent<EnemyDamage>().PresentationSerial == contactBefore &&
            !tutorial.ShortBeamComplete && !tutorial.ChargedBeamComplete && Mathf.Abs(Time.timeScale - 1) < .001f,
            "Resume after paused release creates no surprise beam/contact/progress and restores normal time.");
        if (retryThroughUi) yield return ClickUiButton(FindTutorialButton("Retry lesson"));
        else tutorial.RetryStep();
        phase = "each-step retry and skip";
    }

    static IEnumerator ReplayPractice()
    {
        int id = manager.GetInstanceID(); manager.BeginTutorialPractice();
        yield return WaitForReload(id, true); yield return Delay(.3f);
    }
    static IEnumerator WaitForReload(int previousId, bool training)
    {
        yield return Wait(() =>
        {
            var found = Object.FindFirstObjectByType<BattleManager>();
            return found && found.GetInstanceID() != previousId && found.Tutorial &&
                (!training || found.IsTutorialActive);
        }, 12, training ? "practice replay reload" : "formal duel reload");
        Rebind();
    }
    static void AssertFreshFormal(string label)
    {
        Check(!manager.IsTutorialActive && !manager.IsGameOver && manager.CurrentTrialIndex == SelectedStage &&
            player.GetComponent<BattleStockLives>().Remaining == 2 && enemy.GetComponent<BattleStockLives>().Remaining == 2 &&
            player.GetComponent<PlayerDamage>().damagePercent == 0 && enemy.GetComponent<EnemyDamage>().damagePercent == 0,
            label + " retains arena and gives two clean stocks and 0% to both fighters.");
    }
    static Button FindTutorialButton(string name)
    {
        foreach (var button in manager.GetComponent<BattleTutorialHud>().GetComponentsInChildren<Button>(true))
            if (button.name == name && button.gameObject.activeInHierarchy) return button;
        throw new InvalidOperationException("Tutorial button not found: " + name);
    }
    static IEnumerator ClickUiButton(Button button)
    {
        if (!button || !button.IsActive() || !button.IsInteractable())
            throw new InvalidOperationException("UI button absent, inactive or disabled: " + (button ? button.name : "none"));
        Canvas.ForceUpdateCanvases();
        var rect = button.GetComponent<RectTransform>();
        yield return Wait(() =>
        {
            Canvas.ForceUpdateCanvases();
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            return point.x > 0 && point.y > 0 && point.x < Screen.width && point.y < Screen.height;
        }, 4, "button layout settles inside actual screen: " + button.name);
        desiredMousePosition = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        SetKeys(); desiredMouseButtons = 0;
        yield return Delay(.2f);
        // Selection detail entrances may move while the first pointer hover is
        // travelling. Read the actual current rect again before mouse-down.
        Canvas.ForceUpdateCanvases();
        desiredMousePosition = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        yield return Delay(.1f);
        string name = button.name;
        bool callbackObserved = false;
        UnityEngine.Events.UnityAction observe = () => callbackObserved = true;
        button.onClick.AddListener(observe);
        UiDiagnostic(button, "BEFORE " + name);
        checkingUiClick = true; uiClickLeaked = false;
        desiredMouseButtons = (ushort)(1 << (int)MouseButton.Left);
        yield return Delay(.1f);
        UiDiagnostic(button, "DOWN " + name);
        desiredMouseButtons = 0; yield return Delay(.2f);
        checkingUiClick = false; desiredMousePosition = null;
        if (button) button.onClick.RemoveListener(observe);
        Check(callbackObserved, "Real pointer press/release invoked actual Button callback: " + name);
        Check(!uiClickLeaked, "Actual UI click on " + name + " never leaks into J/K gameplay input.");
    }
    static void UiDiagnostic(Button button, string label)
    {
        var system = EventSystem.current;
        var module = system ? system.currentInputModule as InputSystemUIInputModule : null;
        Vector2 pointer = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        var hits = new List<RaycastResult>();
        if (system) system.RaycastAll(new PointerEventData(system) { position = pointer }, hits);
        var names = new List<string>();
        for (int i = 0; i < Mathf.Min(5, hits.Count); i++)
        {
            var hitButton = hits[i].gameObject.GetComponentInParent<Button>();
            names.Add(hits[i].gameObject.name + "[button=" + (hitButton ? hitButton.name : "none") + "]");
        }
        string action = module ? "point=" + (module.point != null && module.point.action != null ?
            module.point.action.ReadValue<Vector2>().ToString() + "/enabled=" + module.point.action.enabled + "/controls=" + module.point.action.controls.Count : "none") +
            " left=" + (module.leftClick != null && module.leftClick.action != null ?
            module.leftClick.action.ReadValue<float>() + "/enabled=" + module.leftClick.action.enabled + "/controls=" + module.leftClick.action.controls.Count : "none") : "no current InputSystemUIInputModule";
        Record("UI DIAGNOSTIC " + label + " desired=" + desiredMousePosition + " actual=" + pointer +
            " buttonLive=" + (button && button.IsActive() && button.IsInteractable()) +
            " EventSystem=" + (system ? system.name : "none") + " " + action + " hits=" + string.Join(" | ", names));
    }

    static void Rebind()
    {
        manager = Object.FindFirstObjectByType<BattleManager>();
        if (!manager) throw new InvalidOperationException("BattleManager missing from original scene.");
        tutorial = manager.Tutorial;
        player = Object.FindFirstObjectByType<BattlePlayerController>();
        enemy = GameObject.FindGameObjectWithTag("Enemy");
        if (!tutorial || !player || !enemy) throw new InvalidOperationException("Original fighters/tutorial not initialized.");
        specials = player.GetComponent<BattleSpecialMoves>();
    }

    static void PlaceInOriginalKo(GameObject fighter)
    {
        KOZone zone = null;
        foreach (var candidate in Object.FindObjectsByType<KOZone>(FindObjectsSortMode.None))
            if (candidate.GetComponent<Collider>() && candidate.GetComponent<Collider>().isTrigger &&
                (!zone || candidate.transform.position.y < zone.transform.position.y)) zone = candidate;
        if (!zone) throw new InvalidOperationException("No original KO trigger.");
        var body = fighter.GetComponent<Rigidbody>();
        body.linearVelocity = Vector3.zero;
        body.position = zone.GetComponent<Collider>().bounds.center;
        fighter.transform.position = body.position;
        Physics.SyncTransforms();
        Record("FIXTURE original KO trigger placement only: " + fighter.name + " -> " + body.position);
    }

    static IEnumerator Press(Key key, float hold = .09f, float release = .16f) => Press(new[] { key }, hold, release);
    static IEnumerator Press(Key[] keys, float hold, float release)
    {
        SetKeys(); yield return Delay(.06f);
        SetKeys(keys); yield return Delay(hold);
        SetKeys(); yield return Delay(release);
    }
    static IEnumerator PressMouse(MouseButton button, float hold, float release)
    {
        SetKeys(); desiredMouseButtons = 0; yield return Delay(.06f);
        desiredMouseButtons = (ushort)(1 << (int)button); yield return Delay(hold);
        desiredMouseButtons = 0; yield return Delay(release);
    }
    static void SetKeys(params Key[] keys)
    {
        desiredInput = new KeyboardState(keys);
        desiredRouteVersion++;
    }
    static void BeforeInput()
    {
        if (!running || (InputState.currentUpdateType != InputUpdateType.Dynamic && InputState.currentUpdateType != InputUpdateType.Fixed)) return;
        QueueInput();
    }
    static void AfterInput()
    {
        if (!running || keyboard == null || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        // EditorApplication.update can expose the Editor input snapshot rather
        // than the gameplay snapshot. Observe public routing here, immediately
        // after the real Dynamic keyboard event has been applied.
        observedJRoute = BattleAttack.ReadJMove(keyboard);
        observedKRoute = BattleSpecialMoves.ReadKind(keyboard);
        observedRouteVersion = desiredRouteVersion;
        observedRouteFrame = Time.frameCount;
        observedRouteKeys = "W=" + keyboard.wKey.isPressed + ",A=" + keyboard.aKey.isPressed +
            ",S=" + keyboard.sKey.isPressed + ",D=" + keyboard.dKey.isPressed;
        if (keyboard.hKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
            Record("INPUT EDGE H=" + keyboard.hKey.wasPressedThisFrame + " Escape=" + keyboard.escapeKey.wasPressedThisFrame +
                " currentQA=" + (Keyboard.current == keyboard) + " enabled=" + keyboard.enabled + " focus=" + Application.isFocused +
                " mode=" + InputSystem.settings.updateMode + " frame=" + Time.frameCount);
        if (SessionState.GetString(State + "suite", "") == "esc-menu" && mouse != null &&
            (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
            Record("MOUSE DYNAMIC EDGE left=" + mouse.leftButton.wasPressedThisFrame + " right=" + mouse.rightButton.wasPressedThisFrame +
                " actual=" + mouse.position.ReadValue() + " QA=" + (Mouse.current == mouse) + " enabled=" + mouse.enabled +
                " priorPlayable=" + (player && player.GetComponent<BattleCombatInput>().CanStartActions) +
                " paused=" + (manager && manager.IsPaused) + " scale=" + Time.timeScale + " frame=" + Time.frameCount);
    }
    static void QueueInput()
    {
        if (keyboard != null && keyboard.added)
        {
            if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, desiredInput);
        }
        if (mouse != null && mouse.added)
        {
            if (!mouse.enabled) InputSystem.EnableDevice(mouse);
            // Neutral battlefield location, not a practice/menu button. Keep
            // held mouse state across every update so charge ownership is real.
            mouse.MakeCurrent(); InputSystem.QueueStateEvent(mouse, new MouseState {
                position = desiredMousePosition ?? new Vector2(Screen.width * .42f, Screen.height * .5f), buttons = desiredMouseButtons });
        }
    }
    static IEnumerator Delay(float seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < end) yield return null;
    }
    static IEnumerator Wait(Func<bool> condition, float seconds, string label)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > end) throw new TimeoutException(label + "; " + Pose());
            yield return null;
        }
    }

    static void Tick()
    {
        if (!running || stopping) return;
        try
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play ended unexpectedly.");
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Suite exceeded 180 seconds.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (player && player.GetComponent<BattleAfterimages>())
                highestGhostCount = Mathf.Max(highestGhostCount, player.GetComponent<BattleAfterimages>().ActiveCount);
            if (checkingUiClick && player)
            {
                var input = player.GetComponent<BattleCombatInput>();
                if (input && (input.NormalPressed || input.SpecialPressed)) uiClickLeaked = true;
                if (player.GetComponent<BattleAttack>().IsMovementLocked || player.GetComponent<BattleSpecialMoves>().IsBusy)
                    uiClickLeaked = true;
            }
            while (Routine.Count > 0)
            {
                var current = Routine.Peek();
                if (!current.MoveNext()) { Routine.Pop(); continue; }
                if (current.Current is IEnumerator nested) { Routine.Push(nested); continue; }
                return;
            }
            Finish(true, "All bounded tutorial checks passed.");
        }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }

    static void Capture(string name)
    {
        // Captured by Unity, never image generation. The queued screen capture
        // may be unavailable under -nographics; gameplay assertions remain valid.
        if (Application.isBatchMode && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        string path = Path.Combine(Output, name);
        ScreenCapture.CaptureScreenshot(path);
        Record("QUEUED REAL GAME VIEW " + path);
    }
    static string Pose() => "phase=" + phase + " frame=" + Time.frameCount + " step=" +
        (tutorial ? tutorial.CurrentStepIndex.ToString() : "none") + " position=" + (player ? player.transform.position.ToString("F2") : "none");
    static void Check(bool condition, string message)
    {
        Record((condition ? "PASS " : "FAIL ") + message + " | " + Pose());
        if (!condition) throw new InvalidOperationException(message);
        passed++;
    }
    static void Record(string message)
    {
        Directory.CreateDirectory(Output);
        File.AppendAllText(Report, message + "\n");
        Debug.Log("SIDE_A_TUTORIAL: " + message);
    }
    static void Finish(bool ok, string message)
    {
        if (stopping) return;
        stopping = true; success = ok; running = false;
        EditorApplication.update -= Tick;
        InputSystem.onBeforeUpdate -= BeforeInput;
        InputSystem.onAfterUpdate -= AfterInput;
        Routine.Clear();
        try
        {
            if (comboCollisionModified)
            {
                Physics.IgnoreLayerCollision(6, 7, oldComboLayerCollision);
                comboCollisionModified = false;
            }
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (originalKeyboard != null && originalKeyboard.added)
            {
                if (originalKeyboardEnabled) InputSystem.EnableDevice(originalKeyboard); else InputSystem.DisableDevice(originalKeyboard);
                originalKeyboard.MakeCurrent();
            }
            if (originalMouse != null && originalMouse.added)
            {
                if (originalMouseEnabled) InputSystem.EnableDevice(originalMouse); else InputSystem.DisableDevice(originalMouse);
                originalMouse.MakeCurrent();
            }
            InputSystem.settings.backgroundBehavior = oldBackgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInputBehavior;
            Application.runInBackground = oldBackground;
            BattlePresentationSettings.ReducedMotion = oldReducedMotion;
        }
        catch (Exception exception) { success = false; message += "\nInput cleanup failed: " + exception; }
        SessionState.SetBool(State + "success", success);
        Record((success ? "RESULT PASS " : "RESULT FAIL ") + message);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        else if (SessionState.GetBool(State + "quit", false)) EditorApplication.Exit(success ? 0 : 1);
    }
}
