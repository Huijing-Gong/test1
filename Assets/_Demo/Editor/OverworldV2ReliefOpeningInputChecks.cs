using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Prepared only: import into Assets/_Demo/Editor when no Play validation is running.
// Real Look/Move/Approach conditions start the tour. No lesson reflection, relocation,
// StartPractice/Skip/Camera.Play/Complete API or dialogue interaction bypass is used.
[InitializeOnLoad]
public static class OverworldV2ReliefOpeningInputChecks
{
    const string PrepareState = "QA.OverworldV2Play.", State = "QA.OverworldV2ReliefOpeningInput.";
    const string Report = "ToolReports/MapV2Relief/opening-input-checks.txt", Images = "Docs/MapV2/Relief/OpeningInputGameView/";
    static readonly FieldInfo OrbitYaw = typeof(ThirdPersonCamera).GetField("yaw", BindingFlags.Instance | BindingFlags.NonPublic);
    static BaselineFlow flow;
    static BaselineWorld world;
    static OverworldV2Layout layout;
    static BaselineCampTutorial tutorial;
    static PlayerMovement player;
    static Health health;
    static ThirdPersonCamera orbit;
    static Transform guide;
    static MonoBehaviour host;
    static Coroutine coroutine;
    static Keyboard keyboard, oldKeyboard;
    static Mouse mouse, oldMouse;
    static KeyboardState desiredInput;
    static bool oldKeyboardEnabled, oldMouseEnabled, oldBackground, oldCursorVisible, finished;
    static InputSettings.BackgroundBehavior oldBehavior;
    static CursorLockMode oldCursorLock;
    static float oldTimeScale, sensitivity;
    static double deadline;
    static int failures, mode, lastInputFrame;
    static string phase, qaPrefix, realPrefix, primaryHash, backupHash;
    public static bool Running { get; private set; }
    public static string Status => Running ? phase + " " + Pose() : SessionState.GetString(State + "status", "Not run");

    static OverworldV2ReliefOpeningInputChecks()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode && Running) Stop(false); };
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (Running) Fail("Assembly reload interrupted opening input check."); };
    }
    public static void Begin()
    {
        qaPrefix = SessionState.GetString(PrepareState + "prefix", ""); realPrefix = SessionState.GetString(PrepareState + "realPrefix", "");
        if (!EditorApplication.isPlaying || Running || OverworldV2PlayChecks.Running || OverworldV2TutorialChecks.Running ||
            OverworldV2TraversalChecks.Running || OverworldV2CameraChecks.Running || OverworldV2WaystationChecks.Running || OverworldV2ReliefChecks.Running ||
            OverworldV2ReliefTowerChecks.Running || OverworldV2ReliefInteractionChecks.Running ||
            !SessionState.GetBool(PrepareState + "prepared", false) || SceneManager.sceneCount != 1 ||
            SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene ||
            !qaPrefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) || string.IsNullOrEmpty(realPrefix) || qaPrefix == realPrefix)
            throw new InvalidOperationException("Requires exclusive Play in the isolated scene made by OverworldV2PlayChecks.Prepare.");
        flow = Object.FindFirstObjectByType<BaselineFlow>(); world = flow ? flow.GetComponent<BaselineWorld>() : null;
        layout = flow ? flow.GetComponent<OverworldV2Layout>() : null; tutorial = flow ? flow.Tutorial : null;
        player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null; health = player ? player.GetComponent<Health>() : null;
        orbit = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null; host = SideWorldTravel.Instance;
        guide = null;
        foreach (var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None))
            if (world && item.gameObject.scene == world.gameObject.scene && item.action == BaselineInteraction.ActionKind.AcceptTask && item.index == 0) guide = item.transform;
        if (!flow || flow.saveKeyPrefix != qaPrefix || !world || !layout || !tutorial || !player || !health || !orbit || OrbitYaw == null || !guide || !host || !host.isActiveAndEnabled)
            throw new InvalidOperationException("Wait for scene Start; existing V2 player, guide, tutorial, camera and persistent host required.");
        sensitivity = new SerializedObject(orbit).FindProperty("mouseSensitivity").floatValue;
        if (sensitivity <= 0) throw new InvalidOperationException("Existing mouse sensitivity must be positive.");
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); Directory.CreateDirectory(Images);
        File.WriteAllText(Report, "METHOD: existing Prepare QA scene/save; NewGame API is the sole journey setup. Real Enter closes teaching cards, mouse delta completes Look, and W walks from the authored spawn through Move/Approach to the guide. " +
            "The automatic 4m/0.4s guide trigger must call the camera itself. Real Esc skips only the tour and opens the Talk card; real Enter then releases the card; real Tab subsequently skips the whole introduction. " +
            "No controlled player relocation, lesson reflection, StartPractice/Skip/Camera.Play/Complete API, artificial distance or direct grant. Actual Game View screenshots. Not human play or full tutorial completion.\n");
        primaryHash = SlotHash(realPrefix + ".primary"); backupHash = SlotHash(realPrefix + ".backup");
        oldKeyboard = Keyboard.current; oldMouse = Mouse.current; oldKeyboardEnabled = oldKeyboard != null && oldKeyboard.enabled; oldMouseEnabled = oldMouse != null && oldMouse.enabled;
        oldBehavior = InputSystem.settings.backgroundBehavior; oldBackground = Application.runInBackground; oldTimeScale = Time.timeScale;
        oldCursorLock = Cursor.lockState; oldCursorVisible = Cursor.visible;
        failures = mode = 0; lastInputFrame = -1; desiredInput = new KeyboardState(); finished = false; phase = "initializing";
        Running = true; deadline = EditorApplication.timeSinceStartup + 120; SessionState.SetBool(State + "completed", false);
        try
        {
            keyboard = InputSystem.AddDevice<Keyboard>("V2ReliefOpeningQAKeyboard"); mouse = InputSystem.AddDevice<Mouse>("V2ReliefOpeningQAMouse");
            if (oldKeyboardEnabled) InputSystem.DisableDevice(oldKeyboard); if (oldMouseEnabled) InputSystem.DisableDevice(oldMouse);
            keyboard.MakeCurrent(); mouse.MakeCurrent(); InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; Application.runInBackground = true;
            EditorApplication.ExecuteMenuItem("Window/General/Game"); EditorApplication.update += Tick; coroutine = host.StartCoroutine(Guarded());
        }
        catch (Exception exception) { Fail(exception.ToString()); }
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
            if (current is IEnumerator nested) { stack.Push(nested); continue; } yield return current;
        }
        if (Running) Stop();
    }
    static IEnumerator Run()
    {
        phase = "authored new-game arrival"; flow.NewGame(); yield return new WaitForSecondsRealtime(.5f);
        Check(tutorial.Active && tutorial.CardOpen && tutorial.CurrentLesson == BaselineCampTutorial.Lesson.Look && !layout.BridgeOpen && !GameSession.Instance.HasSword,
            "NewGame starts original Look card and closed bridge without sword");
        Vector3 arrival = player.transform.position; int hpBefore = health.CurrentHealth; float fovBefore = Camera.main.fieldOfView;
        Check(world.NavigationSegment(arrival, layout.CampPosition), "Whole existing birth-to-hub walking segment passes preflight; actual movement follows separately");
        Check(Mathf.Abs(arrival.x) < .5f && Mathf.Abs(layout.CampPosition.x) < .5f && guide.position.z > layout.CampPosition.z,
            "Known main road remains northward through C2 toward the original guide");
        yield return Press(Key.Enter);
        Check(tutorial.Active && !tutorial.CardOpen && tutorial.CurrentLesson == BaselineCampTutorial.Lesson.Look && !player.IsGameplayInputLocked && Time.timeScale > 0,
            "Real Enter releases the Look card for actual camera input");
        phase = "real mouse look"; mode = 1; double until = EditorApplication.timeSinceStartup + 8;
        while (tutorial.CurrentLesson == BaselineCampTutorial.Lesson.Look && EditorApplication.timeSinceStartup < until) yield return null;
        mode = 0; Neutral();
        Check(tutorial.CurrentLesson == BaselineCampTutorial.Lesson.Move && tutorial.CardOpen,
            "Actual mouse rotation satisfied original Look threshold and opened Move card");
        yield return Press(Key.Enter);
        Check(tutorial.CurrentLesson == BaselineCampTutorial.Lesson.Move && !tutorial.CardOpen && !player.IsGameplayInputLocked,
            "Real Enter releases Move card");
        phase = "align via actual mouse"; mode = 2; until = EditorApplication.timeSinceStartup + 8;
        while (Mathf.Abs(Mathf.DeltaAngle((float)OrbitYaw.GetValue(orbit), 0)) > 1.5f && EditorApplication.timeSinceStartup < until) yield return null;
        Check(Mathf.Abs(Mathf.DeltaAngle((float)OrbitYaw.GetValue(orbit), 0)) <= 1.5f, "Actual mouse delta restores northward road heading; no orbit reset used");
        phase = "real W uphill to guide"; mode = 3; until = EditorApplication.timeSinceStartup + 40;
        bool reachedApproach = false, swam = false, climbed = false, mantled = false;
        while (!tutorial.TourPlaying && EditorApplication.timeSinceStartup < until)
        {
            reachedApproach |= tutorial.CurrentLesson == BaselineCampTutorial.Lesson.Approach;
            swam |= player.SwimmingActive; climbed |= player.ClimbingActive; mantled |= player.MantlingActive;
            if (Vector3.Distance(player.transform.position, guide.position) < 3.7f) { mode = 0; Neutral(); }
            if (player.transform.position.z > guide.position.z + 1) throw new InvalidOperationException("Passed guide without automatic tour; no teleport/retrigger substitute.");
            yield return null;
        }
        mode = 0; Neutral();
        Check(reachedApproach && tutorial.TourPlaying && tutorial.CameraTour && tutorial.CameraTour.IsPlaying,
            "Real W completed original 4m Move criterion and NPC approach automatically started camera after dwell");
        Check(!swam && !climbed && !mantled && health.CurrentHealth == hpBefore && Planar(arrival, player.transform.position) > 25,
            "Actual spawn-to-guide uphill used ordinary walking, travelled over25m and retained HP");
        Check(player.IsGameplayInputLocked && orbit.CinematicControlled && orbit.IsLookInputLocked && Time.timeScale == 0,
            "Automatic tour owns the existing camera and pauses/locks gameplay");
        Check(!flow.Progress.openingCompleted && !layout.BridgeOpen && !GameSession.Instance.HasSword && All(world.accepted, false) && All(world.opened, false),
            "Automatic scenic trigger did not grant sword/tasks/chests or open bridge");
        var tour = tutorial.CameraTour;
        until = EditorApplication.timeSinceStartup + 5;
        while (tour.IsPlaying && tour.Elapsed < 2.0f && EditorApplication.timeSinceStartup < until) yield return null;
        Check(tour.IsPlaying && tour.Elapsed >= 2 && tour.Elapsed < 12, "Automatic sequence visibly advances before real skip input");
        yield return Capture("automatic-tour-game-view.png");
        phase = "real Esc skip view"; yield return Press(Key.Escape);
        Check(!tutorial.TourPlaying && !orbit.CinematicControlled && tutorial.Active && tutorial.CardOpen && tutorial.CurrentLesson == BaselineCampTutorial.Lesson.Talk && !flow.MenuOpen,
            "Real Esc skips only the scenic view, reaches Talk card and does not open journey menu");
        Check(player.IsGameplayInputLocked && Time.timeScale == 0 && Mathf.Abs(Camera.main.fieldOfView - fovBefore) < .01f,
            "Camera FOV restores; remaining pause/input lock correctly belongs to the original Talk card");
        Check(!layout.BridgeOpen && !flow.Progress.openingCompleted, "Skipping only the view does not skip introduction or open bridge");
        yield return Capture("after-esc-talk-card-game-view.png");
        phase = "real Enter return to gameplay"; yield return Press(Key.Enter); yield return new WaitForSecondsRealtime(.7f);
        Check(tutorial.Active && !tutorial.CardOpen && !tutorial.TourPlaying && tutorial.CurrentLesson == BaselineCampTutorial.Lesson.Talk &&
            !player.IsGameplayInputLocked && !orbit.IsLookInputLocked && !orbit.CinematicControlled && Time.timeScale == 1 && !flow.MenuOpen,
            "Real Enter dismisses Talk card; time and player/camera input restore without replaying tour beside guide");
        yield return Capture("after-tour-control-game-view.png");
        phase = "real Tab skip introduction"; yield return Press(Key.Tab);
        Check(!tutorial.Active && !tutorial.CardOpen && !tutorial.TourPlaying && layout.BridgeOpen && flow.Progress.openingCompleted && flow.Progress.northBridgeOpen,
            "Real Tab from Talk phase skips whole introduction and opens original north bridge");
        Check(!player.IsGameplayInputLocked && !orbit.IsLookInputLocked && !orbit.CinematicControlled && Time.timeScale == 1 && !flow.MenuOpen &&
            !GameSession.Instance.HasSword && All(world.accepted, false) && All(world.opened, false),
            "Whole-introduction skip retains normal control and leaves optional sword/tasks/chests unclaimed");
        Check(BaselineSaveStore.TryLoad(qaPrefix, out var saved, out var message) && saved.openingCompleted && saved.northBridgeOpen && !saved.hasSword,
            "Read-only save check sees the actual Tab transition in disposable slot: " + message);
        CheckRealSave(); finished = true;
        Record("COMPLETE automatic NPC camera trigger via real mouse/W plus Esc view skip, Enter card release, then Tab introduction skip. Tab during an active camera, human orbit comfort and full action tutorial remain outside this bounded run.");
    }
    static IEnumerator Press(Key key)
    {
        mode = 0; Neutral(); yield return new WaitForSecondsRealtime(.1f);
        Record("INJECT " + key + " frame=" + Time.frameCount); desiredInput = new KeyboardState(key); QueueInput();
        yield return new WaitForSecondsRealtime(.16f); Neutral(); yield return new WaitForSecondsRealtime(.25f);
    }
    static void Neutral() { desiredInput = new KeyboardState(); QueueInput(); }
    static void QueueInput()
    {
        if (keyboard == null || !keyboard.added || mouse == null || !mouse.added) return;
        if (!keyboard.enabled) InputSystem.EnableDevice(keyboard); if (!mouse.enabled) InputSystem.EnableDevice(mouse);
        if (Keyboard.current != keyboard) keyboard.MakeCurrent(); if (Mouse.current != mouse) mouse.MakeCurrent();
        float dx = 0; var state = desiredInput;
        if (mode > 0)
        {
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            if (mode == 1) dx = 12;
            else
            {
                float yaw = (float)OrbitYaw.GetValue(orbit); dx = Mathf.Clamp(Mathf.DeltaAngle(yaw, 0) * .55f, -2.5f, 2.5f) / sensitivity;
                if (mode == 3 && Mathf.Abs(Mathf.DeltaAngle(orbit.transform.eulerAngles.y, 0)) < 15 && !tutorial.ModalOpen) state = new KeyboardState(Key.W);
            }
        }
        InputSystem.QueueStateEvent(keyboard, state); InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(dx, 0) });
    }
    static void Tick()
    {
        if (!Running) return;
        try
        {
            if (!EditorApplication.isPlaying || !flow || flow.saveKeyPrefix != qaPrefix || SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene)
                throw new InvalidOperationException("Play/scene/host/save-prefix changed.");
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Opening-input suite exceeded120seconds.");
            EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            if (Time.frameCount != lastInputFrame) { lastInputFrame = Time.frameCount; QueueInput(); }
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }
    static IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame(); var image = ScreenCapture.CaptureScreenshotAsTexture(); if (!image) throw new InvalidOperationException("Game View capture unavailable.");
        try { File.WriteAllBytes(Images + name, image.EncodeToPNG()); Record("GAME_VIEW " + Images + name + " " + Pose()); } finally { Object.Destroy(image); }
    }
    static bool All(bool[] values, bool expected) { foreach (bool value in values) if (value != expected) return false; return true; }
    static void Check(bool ok, string message) { Record((ok ? "PASS " : "FAIL ") + message + " | " + Pose()); if (!ok) throw new InvalidOperationException(message); }
    static void Fail(string message) { failures++; finished = false; try { Record("FAIL " + message + " | " + Pose()); } finally { Stop(); } }
    public static void Stop(bool exitPlay = true)
    {
        if (!Running) return; Running = false; mode = 0; EditorApplication.update -= Tick;
        var cleanupErrors = new List<string>(); Action<Action> restore = action => { try { action(); } catch (Exception e) { cleanupErrors.Add(e.Message); } };
        restore(() => { if (host && coroutine != null) host.StopCoroutine(coroutine); coroutine = null; });
        // Cleanup only: abort a still-active tour on failure before restoring temporary input state.
        restore(() => { if (tutorial && tutorial.CameraTour && tutorial.CameraTour.IsPlaying) tutorial.CameraTour.Abort(); });
        restore(() => { if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); keyboard = null; });
        restore(() => { if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); mouse = null; });
        restore(() => { if (oldKeyboard != null && oldKeyboard.added) { if (oldKeyboardEnabled) InputSystem.EnableDevice(oldKeyboard); else InputSystem.DisableDevice(oldKeyboard); oldKeyboard.MakeCurrent(); } });
        restore(() => { if (oldMouse != null && oldMouse.added) { if (oldMouseEnabled) InputSystem.EnableDevice(oldMouse); else InputSystem.DisableDevice(oldMouse); oldMouse.MakeCurrent(); } });
        restore(() => InputSystem.settings.backgroundBehavior = oldBehavior); restore(() => Application.runInBackground = oldBackground); restore(() => Time.timeScale = oldTimeScale);
        restore(() => { Cursor.lockState = oldCursorLock; Cursor.visible = oldCursorVisible; }); restore(CheckRealSave);
        bool ok = finished && failures == 0 && cleanupErrors.Count == 0; SessionState.SetBool(State + "completed", ok);
        SessionState.SetString(State + "status", ok ? "PASS automatic tour and real skip inputs; restored temporary state" : "FAIL/INCOMPLETE; inspect opening-input-checks.txt");
        try { Record("RESTORED input/background/time/cursor before Play exit; original Prepare owns main scene/real-save audit and disposable slot removal. completed=" + ok + " errors=" + string.Join("; ", cleanupErrors)); }
        finally { if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying = false; }
    }
    static void CheckRealSave()
    {
        if (SlotHash(realPrefix + ".primary") != primaryHash || SlotHash(realPrefix + ".backup") != backupHash) throw new InvalidOperationException("Real save primary/backup changed during isolated opening check.");
        Record("PASS real save primary/backup hashes unchanged");
    }
    static string SlotHash(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(PlayerPrefs.GetString(key))));
    }
    static void Record(string text) => File.AppendAllText(Report, DateTime.UtcNow.ToString("O") + " " + text + "\n");
    static float Planar(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    static string Pose() => player ? "pos=" + player.transform.position + " lesson=" + (tutorial ? tutorial.CurrentLesson.ToString() : "none") +
        " card=" + (tutorial && tutorial.CardOpen) + " tour=" + (tutorial && tutorial.TourPlaying) + " locked=" + player.IsGameplayInputLocked + " timeScale=" + Time.timeScale : "no player";
}
