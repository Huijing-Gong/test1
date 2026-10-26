using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Isolated lesson fixtures plus actual controller/input/resource checks, never a full human playthrough.
[InitializeOnLoad]
public static class OverworldV2TutorialChecks
{
    const string PrepareState = "QA.OverworldV2Play.", State = "QA.OverworldV2Tutorial.";
    const string Report = "ToolReports/MapV2Tutorial/runtime-checks.txt", Images = "Docs/MapV2/Tutorial/";
    static BaselineFlow flow;
    static BaselineWorld world;
    static OverworldV2Layout layout;
    static BaselineCampTutorial tutorial;
    static PlayerMovement player;
    static PlayerVitals vitals;
    static Health health;
    static ThirdPersonCamera orbit;
    static Keyboard keyboard, oldKeyboard;
    static Mouse mouse, oldMouse;
    static bool oldKeyboardEnabled, oldMouseEnabled, oldBackground, oldSideLayerCollision, finished;
    static InputSettings.BackgroundBehavior oldBehavior;
    static Coroutine coroutine;
    static MonoBehaviour coroutineHost;
    static double deadline;
    static string phase;
    static KeyboardState desiredKeyboardState;
    static string desiredKeys = "released";
    static readonly List<string> samples = new List<string>();
    public static bool Running { get; private set; }
    public static string Status => Running ? phase + " " + Pose() : SessionState.GetString(State + "status", "Not run");
    static OverworldV2TutorialChecks()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (Running) Fail("Assembly reload interrupted checks."); };
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(State + "awaitRestore", false)) Restoration(); };
    }
    public static void Begin()
    {
        string prefix = SessionState.GetString(PrepareState + "prefix", "");
        if (!EditorApplication.isPlaying || Running || OverworldV2PlayChecks.Running || OverworldV2CameraChecks.Running ||
            OverworldV2TraversalChecks.Running || OverworldV2WaystationChecks.Running ||
            !SessionState.GetBool(PrepareState + "prepared", false) || SceneManager.sceneCount != 1 ||
            SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene ||
            !prefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) || prefix == SessionState.GetString(PrepareState + "realPrefix", ""))
            throw new InvalidOperationException("Requires exclusive Play in the isolated scene produced by OverworldV2PlayChecks.Prepare.");
        flow = Object.FindFirstObjectByType<BaselineFlow>(); world = flow ? flow.GetComponent<BaselineWorld>() : null;
        layout = flow ? flow.GetComponent<OverworldV2Layout>() : null; tutorial = flow ? flow.Tutorial : null;
        player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null;
        vitals = player ? player.GetComponent<PlayerVitals>() : null; health = player ? player.GetComponent<Health>() : null;
        orbit = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (!flow || flow.saveKeyPrefix != prefix || !world || !layout || !layout.TutorialPracticeReady || !tutorial || !player || !vitals || !health || !orbit)
            throw new InvalidOperationException("Missing authored practice route, initialized host, player, or isolated save prefix.");
        coroutineHost = SideWorldTravel.Instance;
        if (!coroutineHost || !coroutineHost.isActiveAndEnabled)
            throw new InvalidOperationException("Persistent SideWorldTravel is required to keep the QA coroutine alive while the host scene is suspended.");
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); Directory.CreateDirectory(Images);
        File.WriteAllText(Report, "METHOD: copied QA scene + disposable original-Prepare save slot. API/Reflection prepares lesson and controlled start pose; W/Space/R then run actual PlayerMovement, physics, Health and Vitals. No injected damage for the real drop/heal case, no simulated jump/climb flags. Separate negative tests use explicit resource/teleport fixtures. Not human WASD or full opening route. Screenshots are actual Game View.\n");
        oldKeyboard = Keyboard.current; oldMouse = Mouse.current;
        oldKeyboardEnabled = oldKeyboard != null && oldKeyboard.enabled; oldMouseEnabled = oldMouse != null && oldMouse.enabled;
        oldBehavior = InputSystem.settings.backgroundBehavior; oldBackground = Application.runInBackground;
        oldSideLayerCollision = Physics.GetIgnoreLayerCollision(6, 7);
        keyboard = InputSystem.AddDevice<Keyboard>("V2TutorialQAKeyboard"); mouse = InputSystem.AddDevice<Mouse>("V2TutorialQAMouse");
        if (oldKeyboardEnabled) InputSystem.DisableDevice(oldKeyboard); if (oldMouseEnabled) InputSystem.DisableDevice(oldMouse);
        keyboard.MakeCurrent(); mouse.MakeCurrent(); InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; Application.runInBackground = true;
        SessionState.SetString(State + "prefix", prefix); SessionState.SetBool(State + "awaitRestore", true); SessionState.SetBool(State + "completed", false);
        SessionState.SetString(State + "status", "Running bounded tutorial physics checks");
        desiredKeyboardState = new KeyboardState(); desiredKeys = "released";
        samples.Clear(); samples.Add("phase,frame,time,x,y,z,hp,stamina,grounded,swim,climb,mantle,lesson,card,inputLocked,lookLocked,playerEnabled,simulationActive,timeScale,currentKeyboardId,qaKeyboardId,qaEnabled,w,space,spacePressed,r,rPressed,focus,desiredKeys");
        Record("INPUT_SETUP updateMode=" + InputSystem.settings.updateMode + " " + InputState());
        finished = false; Running = true; deadline = EditorApplication.timeSinceStartup + 240;
        EditorApplication.ExecuteMenuItem("Window/General/Game"); EditorApplication.update += Tick;
        coroutine = coroutineHost.StartCoroutine(Guarded());
    }
    static IEnumerator Guarded()
    {
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (Running && stack.Count > 0)
        {
            object current = null; bool advanced;
            try { advanced = stack.Peek().MoveNext(); if (advanced) current = stack.Peek().Current; }
            catch (Exception exception) { Fail(exception.ToString()); yield break; }
            if (!advanced) { stack.Pop(); continue; }
            if (current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return current;
        }
        if (Running) Stop();
    }
    static IEnumerator Run()
    {
        phase = "fresh"; flow.NewGame(); yield return new WaitForSecondsRealtime(.3f);
        Check(tutorial.Active && tutorial.CardOpen && !layout.BridgeOpen, "Fresh opening starts paused with bridge closed");
        tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.25f);
        GameSession.Instance.RestoreProgress(true, true, false); // Fixture only: original conversation/sword is covered separately.
        phase = "jump"; yield return Place(layout.JumpPracticePosition, Heading(layout.JumpPracticePosition, layout.DropPlatformPosition));
        yield return Lesson("Jump"); yield return Capture("jump-card-game-view.png"); tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.25f);
        float startY = player.transform.position.y, highest = startY; bool airborne = false; double until = EditorApplication.timeSinceStartup + 7;
        Keys(Key.W, Key.Space); yield return new WaitForSecondsRealtime(.2f); Keys(Key.W);
        while (EditorApplication.timeSinceStartup < until && !Flag("JumpPracticed"))
        {
            highest = Mathf.Max(highest, player.transform.position.y); airborne |= !player.IsStableGrounded;
            CheckNoAirCard(); if (highest > startY + .5f) Keys(); yield return null;
        }
        Keys(); Check(airborne && highest > startY + .45f && Flag("JumpPracticed"), "Injected W+Space produces genuine rise/landing and JumpPracticed; peak=" + highest);
        Check(player.IsStableGrounded, "Next jump lesson card waits until stable landing");
        phase = "walk practice ramp"; if (tutorial.CardOpen) tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.25f);
        var rampFoot = new Vector3(-18.2f, layout.DropPlatformPosition.y - 3.82f, -26);
        yield return Place(rampFoot, Heading(rampFoot, layout.DropPlatformPosition));
        int rampHealth = health.CurrentHealth; bool rampClimbed = false, rampMantled = false;
        until = EditorApplication.timeSinceStartup + 10; Keys(Key.W);
        while (EditorApplication.timeSinceStartup < until && Planar(player.transform.position, layout.DropPlatformPosition) >= .5f)
        {
            rampClimbed |= player.ClimbingActive; rampMantled |= player.MantlingActive;
            CheckNoAirCard(); yield return null;
        }
        Keys(); yield return new WaitForSecondsRealtime(.35f);
        Check(Planar(player.transform.position, layout.DropPlatformPosition) < .6f &&
            Mathf.Abs(player.transform.position.y - layout.DropPlatformPosition.y) < .65f && player.IsStableGrounded &&
            health.CurrentHealth == rampHealth && !rampClimbed && !rampMantled,
            "Actual W walks the full authored ramp to top without climbing/mantling or damage; climbed=" + rampClimbed + " mantled=" + rampMantled);
        phase = "drop";
        yield return Place(layout.DropPlatformPosition, Heading(layout.DropPlatformPosition, layout.DropLandingPosition));
        yield return Lesson("Drop"); tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.25f);
        int beforeHP = health.CurrentHealth; float topY = player.transform.position.y; bool fell = false, recordedDamage = false; until = EditorApplication.timeSinceStartup + 9;
        Keys(Key.W);
        while (EditorApplication.timeSinceStartup < until && !(tutorial.CardOpen &&
            (tutorial.CurrentLesson.ToString() == "Heal" || tutorial.CurrentLesson.ToString() == "Track")))
        {
            fell |= !player.IsStableGrounded && player.transform.position.y < topY - .5f;
            CheckNoAirCard();
            if (!recordedDamage && health.CurrentHealth < beforeHP)
            {
                recordedDamage = true;
                Record("REAL_FALL beforeHP=" + beforeHP + " afterHP=" + health.CurrentHealth + " landingDistance=" +
                    Planar(player.transform.position, layout.DropLandingPosition) + "; keep actual W held until open landing/card. " + Pose());
            }
            yield return null;
        }
        Keys(); Check(fell && health.CurrentHealth < beforeHP && !health.IsDead && Flag("FallPracticed"), "Actual walking drop deals real fall damage and sets FallPracticed; beforeHP=" + beforeHP + " topY=" + topY);
        Check(player.IsStableGrounded && tutorial.CardOpen &&
            (tutorial.CurrentLesson.ToString() == "Heal" || tutorial.CurrentLesson.ToString() == "Track") &&
            Planar(player.transform.position, layout.DropLandingPosition) <= 1.1f,
            "Recovery card waits for actual landing plus continued W exit to open landing within1.1m; distance=" + Planar(player.transform.position, layout.DropLandingPosition));
        yield return Capture("fall-heal-card-game-view.png");
        phase = "field aid"; if (tutorial.CardOpen) tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.3f);
        int beforeAid = vitals.AidCharges; beforeHP = health.CurrentHealth; yield return Press(Key.R);
        Check(health.CurrentHealth > beforeHP && vitals.AidCharges == beforeAid - 1 && Flag("FieldAidPracticed"), "Injected R heals actual fall damage, consumes one field aid, and advances teaching");
        phase = "death after field aid"; if (tutorial.CardOpen) tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.3f);
        Check(tutorial.CurrentLesson.ToString() == "Track" && !layout.BridgeOpen, "Completed aid lesson is active and nonmodal at Track before death fixture");
        Record("FIXTURE API lethal environment damage for death-path regression, separate from the preceding real walking drop.");
        Check(vitals.ApplyEnvironmentDamage(health.MaxHealth), "API lethal environment damage accepted by actual Vitals/Health");
        until = EditorApplication.timeSinceStartup + 20;
        while (EditorApplication.timeSinceStartup < until && (health.IsDead || !player.IsStableGrounded || Planar(player.transform.position, layout.CampPosition) > .75f)) yield return null;
        yield return new WaitForSecondsRealtime(.3f);
        Check(!health.IsDead && player.IsStableGrounded && Planar(player.transform.position, layout.CampPosition) < .75f &&
            tutorial.Active && tutorial.CurrentLesson.ToString() == "Track" && Flag("JumpPracticed") && Flag("FallPracticed") && Flag("FieldAidPracticed") &&
            !layout.BridgeOpen && !flow.Progress.openingCompleted && !player.IsGameplayInputLocked && Time.timeScale == 1,
            "Actual death handler returns to C2 while preserving completed action flags, Track lesson and closed bridge");
        phase = "swim"; if (tutorial.CardOpen) tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.25f);
        world.accepted[1] = true; world.Track(1); // Explicit API fixture; not a claim of J/menu input.
        yield return Place(layout.IntroShorePosition, Heading(layout.IntroShorePosition, layout.IslandRestPosition));
        yield return Lesson("Swim"); tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.25f);
        float swimming = 0, minimum = vitals.Stamina; until = EditorApplication.timeSinceStartup + 25; bool captured = false;
        Keys(Key.W);
        while (EditorApplication.timeSinceStartup < until && Planar(player.transform.position, layout.IslandRestPosition) > .55f && !tutorial.CardOpen)
        {
            if (player.SwimmingActive) swimming += Time.deltaTime; minimum = Mathf.Min(minimum, vitals.Stamina);
            CheckNoAirCard(); if (!captured && swimming > 2) { captured = true; yield return Capture("swimming-game-view.png"); }
            yield return null;
        }
        Keys(); yield return new WaitForSecondsRealtime(.4f);
        Check(swimming > .35f && player.IsStableGrounded && !player.SwimmingActive && Planar(player.transform.position, layout.IslandRestPosition) < 6,
            "Real injected W swimming reaches dry island landing; swimSeconds=" + swimming + " minStamina=" + minimum);
        phase = "rest"; Check(tutorial.CurrentLesson.ToString() == "RecoverStamina", "Swim transitions to the dry-ground stamina lesson");
        yield return Capture("stamina-rest-card-game-view.png"); tutorial.StartPractice(); until = EditorApplication.timeSinceStartup + 8;
        while (EditorApplication.timeSinceStartup < until && !Flag("StaminaRestPracticed")) yield return null;
        Check(Flag("StaminaRestPracticed") && player.IsStableGrounded && vitals.Stamina >= 79, "Real dry-ground recovery reaches safe stamina before climbing");
        phase = "climb"; if (tutorial.CardOpen) tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.25f);
        yield return Place(layout.ClimbBasePosition, Heading(layout.ClimbBasePosition, layout.ClimbTopPosition));
        yield return Lesson("Climb"); tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.25f);
        bool climbed = false, mantled = false; until = EditorApplication.timeSinceStartup + 18; captured = false; Keys(Key.W);
        while (EditorApplication.timeSinceStartup < until && !Flag("ClimbPracticed"))
        {
            climbed |= player.ClimbingActive; mantled |= player.MantlingActive; CheckNoAirCard();
            if (!captured && player.MantlingActive) { captured = true; yield return Capture("mantling-game-view.png"); }
            yield return null;
        }
        Keys(); Check(climbed && mantled && Flag("ClimbPracticed") && player.IsStableGrounded,
            "Actual W against authored short wall climbs/mantles and lands; climbed=" + climbed + " mantled=" + mantled);
        yield return Capture("climb-landing-game-view.png"); Check(!layout.BridgeOpen && !flow.Progress.openingCompleted, "Practicing abilities alone does not open the bridge or complete the objective");

        phase = "side A preserves action progress";
        SideWorldEntrance sideA = null;
        foreach (var entrance in Object.FindObjectsByType<SideWorldEntrance>(FindObjectsSortMode.None))
            if (entrance.gameObject.scene == world.gameObject.scene && !entrance.isSideWorldB) { sideA = entrance; break; }
        Check(sideA, "Original native A entrance remains available for controlled travel regression");
        var sideAApproach = Vector3.zero; bool foundSideAApproach = false;
        foreach (var offset in new[] { Vector3.right * 4, Vector3.forward * 4, Vector3.back * 4, Vector3.left * 4,
            new Vector3(3, 0, 3), new Vector3(3, 0, -3), new Vector3(-3, 0, 3), new Vector3(-3, 0, -3) })
        {
            var candidate = sideA.transform.position + offset;
            bool clear = world.NavigationGround(candidate, out var ground);
            Record("A_APPROACH candidate=" + candidate + " allowedStandingGround=" + clear + " floor=" + ground);
            if (!clear) continue;
            sideAApproach = new Vector3(ground.x, ground.y - .1f, ground.z); foundSideAApproach = true; break;
        }
        Check(foundSideAApproach, "One of eight bounded native A approach candidates has safe standing ground: " + sideAApproach);
        yield return Place(sideAApproach, Heading(sideAApproach, sideA.transform.position));
        var returnPose = player.transform.position; var travel = SideWorldTravel.Instance;
        Check(tutorial.Active && !tutorial.ModalOpen && tutorial.CurrentLesson.ToString() == "ReachIsland" && world.Tracked == 1,
            "Unfinished ReachIsland and original manual tracking are present before native A");
        Keys(); Record("FIXTURE native A Enter(false)/ReturnToMain APIs from controlled safe entrance pose; no A combat win or entrance E claim.");
        travel.Enter(false); until = EditorApplication.timeSinceStartup + 20;
        while (travel.Busy && EditorApplication.timeSinceStartup < until) yield return null;
        Check(travel.IsAway && !travel.IsWebPreview && !travel.Busy && SceneManager.GetSceneByPath(SideWorldTravel.BattlePath).isLoaded,
            "Original native A scene actually loads within20s");
        travel.ReturnToMain(); until = EditorApplication.timeSinceStartup + 20;
        while ((travel.Busy || travel.IsAway) && EditorApplication.timeSinceStartup < until) yield return null;
        yield return new WaitForSecondsRealtime(.5f);
        Check(!travel.IsAway && !travel.Busy && world.gameObject.activeInHierarchy && player.IsStableGrounded &&
            Vector3.Distance(player.transform.position, returnPose) < .75f && tutorial.Active && !tutorial.ModalOpen &&
            tutorial.CurrentLesson.ToString() == "ReachIsland" && world.Tracked == 1 && Flag("JumpPracticed") && Flag("FallPracticed") &&
            Flag("FieldAidPracticed") && Flag("StaminaRestPracticed") && Flag("ClimbPracticed") && !layout.BridgeOpen && !flow.Progress.openingCompleted,
            "Native A return preserves safe pose, all completed action flags, ReachIsland, tracked task and closed bridge");

        phase = "original inscription completion";
        BaselineInteraction inscription = null;
        foreach (var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None))
            if (item.gameObject.scene == world.gameObject.scene && item.action == BaselineInteraction.ActionKind.Objective && item.index == 1)
            { inscription = item; break; }
        Check(inscription && tutorial.CurrentLesson.ToString() == "ReachIsland", "Actual climb transitions to original island objective, whose identity remains available");
        Vector3 approach = Vector3.zero; bool foundApproach = false;
        foreach (var offset in new[] { Vector3.right * 2, Vector3.left * 2, Vector3.back * 2, Vector3.forward * 2 })
            if (world.NavigationGround(inscription.transform.position + offset, out var ground))
            { approach = new Vector3(ground.x, ground.y - .1f, ground.z); foundApproach = true; break; }
        Check(foundApproach, "Original inscription has a safe controlled interaction approach");
        yield return Place(approach, Heading(approach, inscription.transform.position));
        Record("FIXTURE controlled inscription approach then original world.Interact API; this does not claim physical E dialogue input.");
        world.Interact(inscription); yield return new WaitForSecondsRealtime(.4f);
        Check(world.visited[1] && world.completed[1] && !tutorial.Active && layout.BridgeOpen && flow.Progress.openingCompleted,
            "Original inscription API after genuine practice/swim/climb completes teaching and opens north bridge");
        Check(BaselineSaveStore.TryLoad(flow.saveKeyPrefix, out var finishedSave, out var savedMessage) && finishedSave.openingCompleted && finishedSave.northBridgeOpen,
            "Completed opening and bridge state are durable in disposable QA slot: " + savedMessage);

        phase = "negative teleport"; flow.NewGame(); yield return new WaitForSecondsRealtime(.25f); tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.2f);
        yield return Lesson("Jump"); tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.2f);
        yield return Place(layout.JumpPracticePosition + Vector3.up * 2, 0, false); yield return new WaitForSecondsRealtime(.7f);
        Check(!Flag("JumpPracticed"), "Relocation followed by falling does not count as a Space jump");
        yield return Lesson("Drop"); tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.2f);
        yield return Place(layout.DropPlatformPosition, 0); yield return Place(layout.DropLandingPosition, 0);
        Check(!Flag("FallPracticed"), "Instant relocation from top to bottom does not count as actual fall damage");
        yield return Lesson("Climb"); tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.2f);
        yield return Place(layout.ClimbTopPosition, 0); Check(!Flag("ClimbPracticed"), "Standing on climb top after relocation does not count as climbing");
        phase = "heal recovery guidance"; vitals.Refill();
        yield return Lesson("Heal"); tutorial.StartPractice(); yield return new WaitForSecondsRealtime(.3f); yield return Press(Key.R);
        string guidance = Instruction();
        Check(!tutorial.ModalOpen && !player.IsGameplayInputLocked && Time.timeScale == 1 && !Flag("FieldAidPracticed") && guidance.Contains("practice ledge"),
            "Full health preserves aid/control and explains how to retry the real practice drop"); Record("GUIDANCE " + guidance);
        SetProperty(vitals, "AidCharges", 0); yield return Press(Key.R); guidance = Instruction();
        Check(!tutorial.ModalOpen && !player.IsGameplayInputLocked && Time.timeScale == 1 && !Flag("FieldAidPracticed") && guidance.Contains("camp") && guidance.Contains("refill"),
            "No aid keeps control free, does not fake completion, and points back to camp for refill"); Record("GUIDANCE " + guidance);
        tutorial.Cancel(); yield return new WaitForSecondsRealtime(.2f);
        Check(!tutorial.Active && !layout.BridgeOpen && !flow.Progress.openingCompleted && Time.timeScale == 1, "Cancel preserves unfinished bridge/progress and releases tutorial controls");
        tutorial.Begin(); yield return new WaitForSecondsRealtime(.2f); yield return Press(Key.Tab);
        Check(!tutorial.Active && layout.BridgeOpen && flow.Progress.openingCompleted && !player.IsGameplayInputLocked && Time.timeScale == 1,
            "Actual injected Tab skips the whole new sequence, opens bridge and restores controls");
        Record("PENDING full human opening route, every NPC/J interaction, comfort and camera assessment, interruptions in stages beyond tested death-at-Track/A-at-ReachIsland, browser B and A victory. Starting lessons/controlled poses are API fixtures, not claimed continuous traversal.");
        finished = true; SessionState.SetBool(State + "completed", true); Record("COMPLETE bounded physical tutorial and negative checks.");
    }
    static IEnumerator Lesson(string name)
    {
        phase += " [" + name + "]";
        var value = (BaselineCampTutorial.Lesson)Enum.Parse(typeof(BaselineCampTutorial.Lesson), name);
        typeof(BaselineCampTutorial).GetMethod("ShowLesson", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tutorial, new object[] { value });
        Record("FIXTURE ShowLesson(" + name + "); actual actions follow."); yield return new WaitForSecondsRealtime(.15f);
    }
    static IEnumerator Place(Vector3 requested, float yaw, bool grounded = true)
    {
        Keys(); var position = requested;
        if (grounded) { Check(world.NavigationGround(requested, out var ground), "Controlled start has valid floor " + requested); position.y = ground.y - .1f; }
        player.Relocate(position, Quaternion.Euler(0, yaw, 0)); Physics.SyncTransforms(); orbit.ResetOrbit(yaw, 14);
        yield return new WaitForSecondsRealtime(.5f);
        if (grounded) Check(player.IsStableGrounded && !player.SwimmingActive && !player.ClimbingActive, "Controlled start settled on dry ground");
    }
    static IEnumerator Press(Key key) { Keys(key); yield return new WaitForSecondsRealtime(.16f); Keys(); yield return new WaitForSecondsRealtime(.35f); }
    static void Keys(params Key[] keys)
    {
        string names = keys.Length == 0 ? "released" : string.Join("+", keys);
        bool changed = names != desiredKeys;
        desiredKeys = names; desiredKeyboardState = new KeyboardState(keys);
        QueueHeldInput();
        if (changed) Record("INJECT desired=" + desiredKeys + " " + InputState());
    }
    static void QueueHeldInput()
    {
        // Requeue the desired state in each Editor tick, as the validated traversal driver does.
        // Unchanged states remain held; only Keys(released)->Keys(key) creates a real press edge.
        // Do not call InputSystem.Update manually or synthesize gameplay flags.
        if (keyboard == null || !keyboard.added) return;
        if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
        if (Keyboard.current != keyboard) keyboard.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, desiredKeyboardState);
        if (mouse != null && mouse.added)
        {
            if (!mouse.enabled) InputSystem.EnableDevice(mouse);
            if (Mouse.current != mouse) mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState());
        }
    }
    static bool Flag(string property) => (bool)(typeof(BaselineCampTutorial).GetProperty(property)?.GetValue(tutorial) ?? throw new MissingMemberException(property));
    static string Instruction() => typeof(BaselineCampTutorial).GetProperty("Instruction", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(tutorial) as string ?? "";
    static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
    static float Planar(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    static float Heading(Vector3 a, Vector3 b) => Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
    static void CheckNoAirCard() { if (!player.IsStableGrounded && tutorial.CardOpen) throw new InvalidOperationException("Card paused an airborne movement: " + Pose()); }
    static IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame(); var texture = ScreenCapture.CaptureScreenshotAsTexture();
        if (!texture) throw new InvalidOperationException("Game View capture unavailable");
        try { File.WriteAllBytes(Images + name, texture.EncodeToPNG()); Record("GAME_VIEW " + Images + name + " " + Pose()); }
        finally { Object.Destroy(texture); }
    }
    static string Pose() => player ? "pos=" + player.transform.position + " grounded=" + player.IsStableGrounded + " swim=" + player.SwimmingActive + " climb=" + player.ClimbingActive + " mantle=" + player.MantlingActive + " HP=" + (health ? health.CurrentHealth : -1) + " stamina=" + (vitals ? vitals.Stamina : -1) + " lesson=" + (tutorial ? tutorial.CurrentLesson.ToString() : "missing") + " card=" + (tutorial && tutorial.CardOpen) + " " + InputState() : "no player";
    static string InputState() => "frame=" + Time.frameCount + " time=" + Time.time + " timeScale=" + Time.timeScale +
        " inputLocked=" + (player && player.IsGameplayInputLocked) + " lookLocked=" + (orbit && orbit.IsLookInputLocked) +
        " playerEnabled=" + (player && player.isActiveAndEnabled) + " simulationActive=" + (vitals && vitals.SimulationActive) +
        " keyboardCurrent=" + (Keyboard.current != null ? Keyboard.current.name + "#" + Keyboard.current.deviceId : "none") +
        " qaKeyboard=" + (keyboard != null ? keyboard.name + "#" + keyboard.deviceId : "none") +
        " qaEnabled=" + (keyboard != null && keyboard.enabled) + " w=" + (keyboard != null && keyboard.wKey.isPressed) +
        " space=" + (keyboard != null && keyboard.spaceKey.isPressed) + " spaceEdge=" + (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) +
        " r=" + (keyboard != null && keyboard.rKey.isPressed) + " rEdge=" + (keyboard != null && keyboard.rKey.wasPressedThisFrame) +
        " desired=" + desiredKeys + " focused=" + Application.isFocused;
    static void Tick()
    {
        if (!Running) return; EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        if (!EditorApplication.isPlaying || !flow || flow.saveKeyPrefix != SessionState.GetString(State + "prefix", "")) { Fail("Play/host/QA prefix changed"); return; }
        if (EditorApplication.timeSinceStartup > deadline) { Fail("Bounded tutorial suite exceeded 240 seconds"); return; }
        QueueHeldInput();
        var p = player.transform.position; samples.Add(string.Join(",", phase, Time.frameCount, Time.time, p.x, p.y, p.z, health.CurrentHealth, vitals.Stamina, player.IsStableGrounded, player.SwimmingActive, player.ClimbingActive, player.MantlingActive, tutorial.CurrentLesson, tutorial.CardOpen,
            player.IsGameplayInputLocked, orbit.IsLookInputLocked, player.isActiveAndEnabled, vitals.SimulationActive, Time.timeScale,
            Keyboard.current != null ? Keyboard.current.deviceId : -1, keyboard != null ? keyboard.deviceId : -1,
            keyboard != null && keyboard.enabled, keyboard != null && keyboard.wKey.isPressed, keyboard != null && keyboard.spaceKey.isPressed,
            keyboard != null && keyboard.spaceKey.wasPressedThisFrame, keyboard != null && keyboard.rKey.isPressed,
            keyboard != null && keyboard.rKey.wasPressedThisFrame, Application.isFocused, desiredKeys));
    }
    static void Check(bool ok, string message) { Record((ok ? "PASS " : "FAIL ") + message + " | " + Pose()); if (!ok) throw new InvalidOperationException(message); }
    static void Record(string text) => File.AppendAllText(Report, DateTime.UtcNow.ToString("O") + " " + text + "\n");
    static void Fail(string text) { finished = false; SessionState.SetBool(State + "completed", false); Record("FAIL " + text + " | " + Pose()); Stop(); }
    public static void Stop(bool exitPlay = true)
    {
        if (!Running) return; Running = false; EditorApplication.update -= Tick;
        File.WriteAllLines("ToolReports/MapV2Tutorial/runtime-samples.csv", samples);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (oldKeyboard != null && oldKeyboard.added) { if (oldKeyboardEnabled) InputSystem.EnableDevice(oldKeyboard); oldKeyboard.MakeCurrent(); }
        if (oldMouse != null && oldMouse.added) { if (oldMouseEnabled) InputSystem.EnableDevice(oldMouse); oldMouse.MakeCurrent(); }
        InputSystem.settings.backgroundBehavior = oldBehavior; Application.runInBackground = oldBackground;
        Physics.IgnoreLayerCollision(6, 7, oldSideLayerCollision);
        Record("RESTORED original keyboard/mouse/background before Play teardown; disposable prefix retained through teardown");
        SessionState.SetString(State + "status", finished ? "Checks completed; awaiting restoration" : "INCOMPLETE/FAIL; inspect report");
        if (coroutineHost && coroutine != null) coroutineHost.StopCoroutine(coroutine); coroutine = null;
        if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
    static void OnModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode && Running) Stop(false);
        if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(State + "awaitRestore", false)) EditorApplication.delayCall += Restoration;
    }
    static void Restoration()
    {
        if (!SessionState.GetBool(State + "awaitRestore", false)) return;
        string prefix = SessionState.GetString(State + "prefix", ""); var scene = SceneManager.GetActiveScene();
        bool ok = !EditorApplication.isPlaying && scene.path == "Assets/_Demo/Scenes/OverworldBaseline.unity" && !scene.isDirty && SceneManager.sceneCount == 1 &&
            !SessionState.GetBool(PrepareState + "prepared", false) && !PlayerPrefs.HasKey(prefix + ".primary") && !PlayerPrefs.HasKey(prefix + ".backup");
        Record((ok ? "PASS " : "FAIL ") + "Original Prepare harness restored main scene/QA slot; scene=" + scene.path + " clean=" + !scene.isDirty + " time=" + Time.timeScale + ". Real save and main scene hash evidence: ToolReports/MapV2Migration/runtime-checks.txt");
        SessionState.SetBool(State + "awaitRestore", false); SessionState.SetString(State + "status", ok && SessionState.GetBool(State + "completed", false) ? "PASS tutorial checks/restoration; visual review pending" : "INCOMPLETE/FAIL; inspect reports");
    }
}
