using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Bounded physical walking evidence in the existing disposable QA scene.
// Controlled starts and camera steering are fixtures; movement itself uses W and the real controller.
[InitializeOnLoad]
public static class OverworldV2ReliefChecks
{
    const string PrepareState = "QA.OverworldV2Play.", State = "QA.OverworldV2Relief.";
    const string Report = "ToolReports/MapV2Relief/", Images = "Docs/MapV2/Relief/";
    static BaselineFlow flow;
    static BaselineWorld world;
    static OverworldV2Layout layout;
    static BaselineCampTutorial tutorial;
    static PlayerMovement player;
    static Health health;
    static ThirdPersonCamera orbit;
    static MonoBehaviour host;
    static Coroutine coroutine;
    static Keyboard keyboard, oldKeyboard;
    static Mouse mouse, oldMouse;
    static bool oldKeyboardEnabled, oldMouseEnabled, oldBackground, oldCursorVisible;
    static InputSettings.BackgroundBehavior oldBehavior;
    static CursorLockMode oldCursorLock;
    static float oldTimeScale, cameraPivotHeight, steeringYaw;
    static bool forward, finished;
    static double deadline, lastSteer, nextSample;
    static int steerFrame, failures;
    static string phase, qaPrefix, realPrefix, primaryHash, backupHash;
    static readonly List<string> samples = new List<string>();
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public static bool Running { get; private set; }
    public static string Status => Running ? phase + " " + Pose() : SessionState.GetString(State + "status", "Not run");

    static OverworldV2ReliefChecks()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (Running) Fail("Assembly reload interrupted walking checks."); };
    }

    public static void Begin()
    {
        qaPrefix = SessionState.GetString(PrepareState + "prefix", "");
        realPrefix = SessionState.GetString(PrepareState + "realPrefix", "");
        if (!EditorApplication.isPlaying || Running || OverworldV2PlayChecks.Running || OverworldV2TutorialChecks.Running ||
            OverworldV2TraversalChecks.Running || OverworldV2CameraChecks.Running || OverworldV2WaystationChecks.Running ||
            !SessionState.GetBool(PrepareState + "prepared", false) || SceneManager.sceneCount != 1 ||
            SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene ||
            !qaPrefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) || string.IsNullOrEmpty(realPrefix) || qaPrefix == realPrefix)
            throw new InvalidOperationException("Requires exclusive Play in the isolated scene made by OverworldV2PlayChecks.Prepare.");
        flow = Object.FindFirstObjectByType<BaselineFlow>();
        world = flow ? flow.GetComponent<BaselineWorld>() : null;
        layout = flow ? flow.GetComponent<OverworldV2Layout>() : null;
        tutorial = flow ? flow.Tutorial : null;
        player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null;
        health = player ? player.GetComponent<Health>() : null;
        orbit = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        host = SideWorldTravel.Instance;
        if (!flow || flow.saveKeyPrefix != qaPrefix || !world || !layout || !layout.TutorialPracticeReady ||
            !tutorial || !player || !health || !orbit || !host || !host.isActiveAndEnabled)
            throw new InvalidOperationException("Wait for scene Start; a complete V2 host, practice route, real player and persistent coroutine host are required.");

        Directory.CreateDirectory(Report); Directory.CreateDirectory(Images);
        File.WriteAllText(Report + "walking-checks.txt",
            "METHOD: copied scene and disposable Prepare save prefix; NewGame then tutorial.Cancel public APIs. " +
            "Separate route cases use controlled start placements. Actual W input drives PlayerMovement continuously along each route; " +
            "no per-frame relocation, jump, climb or synthetic damage. Ground queries and full-segment checks guide the fixture, " +
            "not a human WASD playthrough. Screenshots use actual Game View, with controlled orbit heading. " +
            "Camera minima exclude the first two frames after an orbit reset; manual camera feel remains untested.\n");
        primaryHash = SlotHash(realPrefix + ".primary"); backupHash = SlotHash(realPrefix + ".backup");
        oldKeyboard = Keyboard.current; oldMouse = Mouse.current;
        oldKeyboardEnabled = oldKeyboard != null && oldKeyboard.enabled;
        oldMouseEnabled = oldMouse != null && oldMouse.enabled;
        oldBehavior = InputSystem.settings.backgroundBehavior; oldBackground = Application.runInBackground;
        oldTimeScale = Time.timeScale; oldCursorLock = Cursor.lockState; oldCursorVisible = Cursor.visible;
        cameraPivotHeight = new SerializedObject(orbit).FindProperty("targetHeight").floatValue;
        samples.Clear(); samples.Add("phase,frame,time,x,y,z,hp,grounded,swimming,climbing,mantling,cameraPivotDistance,w");
        finished = false; failures = 0; forward = false; phase = "initializing";
        SessionState.SetString(State + "status", "Running bounded relief walking checks");
        SessionState.SetBool(State + "completed", false);
        Running = true; deadline = EditorApplication.timeSinceStartup + 180; nextSample = 0;
        try
        {
            keyboard = InputSystem.AddDevice<Keyboard>("V2ReliefQAKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("V2ReliefQAMouse");
            if (oldKeyboardEnabled) InputSystem.DisableDevice(oldKeyboard);
            if (oldMouseEnabled) InputSystem.DisableDevice(oldMouse);
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true;
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.update += Tick;
            coroutine = host.StartCoroutine(Guarded());
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
            if (current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return current;
        }
        if (Running) Stop();
    }

    static IEnumerator Run()
    {
        phase = "fresh journey"; flow.NewGame(); yield return new WaitForSecondsRealtime(.3f);
        tutorial.Cancel(); yield return new WaitForSecondsRealtime(.25f);
        Check(!tutorial.Active && !layout.BridgeOpen && !flow.Progress.openingCompleted && !player.IsGameplayInputLocked && Time.timeScale > 0,
            "Cancel releases teaching without completing introduction or opening bridge");
        world.RefreshNavigation();

        yield return Place(layout.ArrivalPosition, Heading(layout.ArrivalPosition, layout.CampPosition));
        yield return Capture("spawn-game-view.png");
        yield return Walk("spawn to hub uphill", layout.CampPosition, "hub-game-view.png", true);
        yield return Walk("hub to spawn descent", layout.ArrivalPosition, null, false);

        phase = "tutorial approach fixture";
        yield return Place(layout.CampPosition, Heading(layout.CampPosition, layout.JumpPracticePosition));
        yield return Walk("hub to jump anchor", layout.JumpPracticePosition, "practice-approach-game-view.png", false);
        // Platform-relative seed follows the authored practice lift, including this relief pass's +3 units.
        var rampFoot = new Vector3(-18.2f, layout.DropPlatformPosition.y - 3.82f, -26);
        phase = "independent practice ramp fixture";
        Record("CONTROLLED_FIXTURE jump anchor to ramp foot is not a walking test: the existing jump-teaching stone intentionally lies between them. " +
            "Actual Space jump is covered by the separate TutorialChecks suite. Place at the ramp foot, then test continuous W ascent only.");
        yield return Place(rampFoot, Heading(rampFoot, layout.DropPlatformPosition));
        yield return Walk("practice ramp to deck", layout.DropPlatformPosition, "practice-deck-game-view.png", false);

        phase = "central road fixture";
        yield return Place(layout.CampPosition, 32);
        var central = new[] { new Vector3(13, layout.CampPosition.y, -24), new Vector3(10, layout.CampPosition.y, -3),
            new Vector3(26, layout.EastRegionPosition.y, 15), new Vector3(38, layout.EastRegionPosition.y, 31),
            new Vector3(52, layout.EastRegionPosition.y + 10, 40) };
        for (int i = 0; i < central.Length; i++)
            yield return Walk("central road " + (i + 1), central[i], i == 1 ? "central-game-view.png" : i == 4 ? "east-camp-game-view.png" : null, false);

        Check(!layout.BridgeOpen && !flow.Progress.openingCompleted && !tutorial.Active,
            "Walking alone retained closed bridge and incomplete introduction");
        CheckRealSave();
        finished = true; SessionState.SetBool(State + "completed", true);
        Record("COMPLETE actual injected-W routes. No human camera/navigation interaction or full tutorial gameplay claim.");
    }

    static Vector3 Ground(Vector3 requested)
    {
        if (!world.NavigationGround(requested, out var ground))
            throw new InvalidOperationException("No safe walking ground at authored point " + requested + "; no alternate endpoint silently substituted.");
        return ground;
    }

    static IEnumerator Place(Vector3 requested, float yaw)
    {
        forward = false; QueueInput(); var ground = Ground(requested);
        player.Relocate(ground - Vector3.up * .1f, Quaternion.Euler(0, yaw, 0));
        Physics.SyncTransforms(); Steer(yaw);
        Record("CONTROLLED_START requested=" + requested + " resolved=" + ground);
        yield return new WaitForSecondsRealtime(.5f);
        Check(player.IsStableGrounded && !player.SwimmingActive && !player.ClimbingActive && !health.IsDead,
            "Controlled route start settled on dry ground");
    }

    static List<Vector3> Route(Vector3 target)
    {
        var from = Ground(player.transform.position); var path = new List<Vector3>();
        if (world.NavigationSegment(from, target, path))
        {
            Record("ROUTE complete direct segment checked including endpoints; samples=" + path.Count);
            return path;
        }
        path.Clear(); var graph = world.WalkingRoute(player.transform.position, target);
        if (graph.Length == 0) throw new InvalidOperationException("No connected walking route: " + world.NavigationStatus);
        path.AddRange(graph);
        // WalkingRoute ends beside an interaction. This physical fixture must also reach its actual authored endpoint.
        if (!world.NavigationSegment(path[path.Count - 1], target, path))
            throw new InvalidOperationException("Graph approach exists but final segment to authored target is blocked.");
        Record("ROUTE existing WalkingRoute plus checked final connection; samples=" + path.Count);
        return path;
    }

    static IEnumerator Walk(string label, Vector3 requested, string screenshot, bool captureUphill)
    {
        phase = label; forward = false;
        var target = Ground(requested); var path = Route(target);
        Check(path.Count > 0, label + " has a checked route");
        int beforeHP = health.CurrentHealth, index = 0; bool climbed = false, mantled = false, swam = false, captured = false;
        float initialY = player.transform.position.y, highestY = initialY, lowestY = initialY, minimumCamera = float.PositiveInfinity;
        float tightCameraSeconds = 0, longestTightCamera = 0, tightRun = 0;
        double localDeadline = Math.Min(deadline, EditorApplication.timeSinceStartup + Math.Max(15, PathLength(path) / 2 + 7));
        double progressAt = EditorApplication.timeSinceStartup;
        double nextLookAhead = 0;
        Vector3 progressPosition = player.transform.position;
        while (Planar(player.transform.position, target) > .42f)
        {
            if (EditorApplication.timeSinceStartup >= localDeadline)
                throw new TimeoutException(label + " did not reach target; planar remaining=" + Planar(player.transform.position, target));
            if (health.IsDead || player.IsGameplayInputLocked || Time.timeScale <= 0)
                throw new InvalidOperationException(label + " controller died, became input-locked or paused.");
            while (index < path.Count - 1 && Planar(player.transform.position, path[index]) < .65f) index++;
            // Bounded look-ahead is accepted only after the whole shortcut is checked. This avoids cutting route corners.
            if (EditorApplication.timeSinceStartup >= nextLookAhead)
            {
                nextLookAhead = EditorApplication.timeSinceStartup + .35;
                int furthest = index;
                while (furthest + 1 < path.Count && furthest < index + 7 && Planar(player.transform.position, path[furthest + 1]) <= 4.5f) furthest++;
                // At most two full shortcut checks per interval; no per-frame graph search.
                if (furthest > index && world.NavigationSegment(player.transform.position, path[furthest])) index = furthest;
                else if (furthest > index + 1 && world.NavigationSegment(player.transform.position, path[index + 1])) index++;
            }
            Vector3 aimPoint = path[index]; float yaw = Heading(player.transform.position, aimPoint);
            if (!forward || (EditorApplication.timeSinceStartup - lastSteer > .35 && Mathf.Abs(Mathf.DeltaAngle(steeringYaw, yaw)) > 5)) Steer(yaw);
            forward = true; QueueInput();
            climbed |= player.ClimbingActive; mantled |= player.MantlingActive; swam |= player.SwimmingActive;
            highestY = Mathf.Max(highestY, player.transform.position.y); lowestY = Mathf.Min(lowestY, player.transform.position.y);
            if (Time.frameCount > steerFrame + 2)
            {
                float distance = CameraDistance(); minimumCamera = Mathf.Min(minimumCamera, distance);
                if (distance < 2) { tightCameraSeconds += Time.unscaledDeltaTime; tightRun += Time.unscaledDeltaTime; }
                else tightRun = 0;
                longestTightCamera = Mathf.Max(longestTightCamera, tightRun);
            }
            if (EditorApplication.timeSinceStartup - progressAt > 3)
            {
                if (Planar(progressPosition, player.transform.position) < .3f)
                    throw new InvalidOperationException(label + " stalled for three seconds at " + player.transform.position);
                progressAt = EditorApplication.timeSinceStartup; progressPosition = player.transform.position;
            }
            float remaining = Planar(player.transform.position, target);
            if (captureUphill && !captured && remaining < 18 && remaining > 7)
            {
                captured = true; forward = false; QueueInput(); yield return new WaitForSecondsRealtime(.3f);
                yield return Capture("uphill-game-view.png");
            }
            yield return null;
        }
        forward = false; QueueInput(); yield return new WaitForSecondsRealtime(.35f);
        Record("METRICS " + label + " target=" + target + " actual=" + player.transform.position + " HP=" + beforeHP + "->" + health.CurrentHealth +
            " heightNet=" + F(player.transform.position.y - initialY) + " maximumRise=" + F(highestY - initialY) +
            " heightRange=" + F(highestY - lowestY) + " minCameraPivotDistance=" + F(minimumCamera) +
            " cameraBelow2mSeconds=" + F(tightCameraSeconds) + " longestCameraBelow2m=" + F(longestTightCamera));
        Check(Planar(player.transform.position, target) < .65f && Mathf.Abs(player.transform.position.y - target.y) < .7f && player.IsStableGrounded,
            label + " reached and settled at the actual target");
        Check(health.CurrentHealth == beforeHP && !climbed && !mantled && !swam,
            label + " used ordinary walking with unchanged HP, no climbing/mantling/swimming");
        Check(longestTightCamera < 2, label + " did not sustain measured camera distance below2m for two seconds (manual orbit remains untested)");
        if (screenshot != null) yield return Capture(screenshot);
    }

    static void Steer(float yaw)
    {
        steeringYaw = yaw; orbit.ResetOrbit(yaw, 14); lastSteer = EditorApplication.timeSinceStartup; steerFrame = Time.frameCount;
    }
    static void QueueInput()
    {
        if (keyboard == null || !keyboard.added) return;
        if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
        if (Keyboard.current != keyboard) keyboard.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, forward ? new KeyboardState(Key.W) : new KeyboardState());
        if (mouse == null || !mouse.added) return;
        if (!mouse.enabled) InputSystem.EnableDevice(mouse);
        if (Mouse.current != mouse) mouse.MakeCurrent();
        InputSystem.QueueStateEvent(mouse, new MouseState());
    }
    static IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame(); var image = ScreenCapture.CaptureScreenshotAsTexture();
        if (!image) throw new InvalidOperationException("Game View capture unavailable.");
        try { File.WriteAllBytes(Images + name, image.EncodeToPNG()); Record("GAME_VIEW " + Images + name + " " + Pose() + " camera=" + orbit.transform.position + " euler=" + orbit.transform.eulerAngles); }
        finally { Object.Destroy(image); }
    }
    static void Tick()
    {
        if (!Running) return;
        try
        {
            if (!EditorApplication.isPlaying || !flow || flow.saveKeyPrefix != qaPrefix || SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene)
                throw new InvalidOperationException("Play/host/isolated scene/save prefix changed.");
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Bounded relief suite exceeded180seconds.");
            EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); QueueInput();
            if (EditorApplication.timeSinceStartup < nextSample) return;
            nextSample = EditorApplication.timeSinceStartup + .1;
            var p = player.transform.position;
            samples.Add(string.Join(",", phase, Time.frameCount, F(Time.time), F(p.x), F(p.y), F(p.z), health.CurrentHealth,
                player.IsStableGrounded, player.SwimmingActive, player.ClimbingActive, player.MantlingActive, F(CameraDistance()), forward));
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }
    static void Check(bool ok, string message)
    {
        Record((ok ? "PASS " : "FAIL ") + message + " | " + Pose());
        if (!ok) throw new InvalidOperationException(message);
    }
    static void Record(string text) => File.AppendAllText(Report + "walking-checks.txt", DateTime.UtcNow.ToString("O") + " " + text + "\n");
    static void Fail(string message)
    {
        failures++; finished = false; SessionState.SetBool(State + "completed", false);
        try { Record("FAIL " + message + " | " + Pose()); }
        finally { Stop(); }
    }
    public static void Stop(bool exitPlay = true)
    {
        if (!Running) return;
        Running = false; forward = false; EditorApplication.update -= Tick;
        // Restoration precedes report writes, so an unavailable report directory cannot strand input devices.
        var cleanupErrors = new List<string>();
        Action<Action> restore = action => { try { action(); } catch (Exception e) { cleanupErrors.Add(e.Message); } };
        restore(() => { if (host && coroutine != null) host.StopCoroutine(coroutine); coroutine = null; });
        restore(() => { if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); keyboard = null; });
        restore(() => { if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); mouse = null; });
        restore(() => { if (oldKeyboard != null && oldKeyboard.added) { if (oldKeyboardEnabled) InputSystem.EnableDevice(oldKeyboard); else InputSystem.DisableDevice(oldKeyboard); oldKeyboard.MakeCurrent(); } });
        restore(() => { if (oldMouse != null && oldMouse.added) { if (oldMouseEnabled) InputSystem.EnableDevice(oldMouse); else InputSystem.DisableDevice(oldMouse); oldMouse.MakeCurrent(); } });
        restore(() => InputSystem.settings.backgroundBehavior = oldBehavior);
        restore(() => Application.runInBackground = oldBackground);
        restore(() => Time.timeScale = oldTimeScale);
        restore(() => { Cursor.lockState = oldCursorLock; Cursor.visible = oldCursorVisible; });
        restore(CheckRealSave);
        bool ok = finished && failures == 0 && cleanupErrors.Count == 0;
        SessionState.SetBool(State + "completed", ok);
        SessionState.SetString(State + "status", ok ? "PASS walking checks; inputs restored; Prepare teardown audit pending" : "INCOMPLETE/FAIL; restored inputs; inspect walking-checks.txt");
        try
        {
            File.WriteAllLines(Report + "walking-samples.csv", samples);
            Record("RESTORED keyboard/mouse/background/time/cursor before Play teardown. QA prefix retained for teardown; original Prepare owns slot removal and main-scene/hash restoration. " +
                "completed=" + ok + " cleanupErrors=" + string.Join("; ", cleanupErrors));
        }
        finally { if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying = false; }
    }
    static void OnModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && Running) Stop(false);
    }
    static void CheckRealSave()
    {
        if (SlotHash(realPrefix + ".primary") != primaryHash || SlotHash(realPrefix + ".backup") != backupHash)
            throw new InvalidOperationException("Original save primary/backup changed during the isolated relief test.");
        Record("PASS real save primary/backup hashes unchanged");
    }
    static string SlotHash(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(PlayerPrefs.GetString(key))));
    }
    static float PathLength(List<Vector3> path) { float length = 0; for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]); return length; }
    static float Planar(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    static float Heading(Vector3 a, Vector3 b) => Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
    static float CameraDistance() => Vector3.Distance(player.transform.position + Vector3.up * cameraPivotHeight, orbit.transform.position);
    static string F(float number) => number.ToString("0.###", Invariant);
    static string Pose() => player ? "pos=" + player.transform.position + " HP=" + (health ? health.CurrentHealth : -1) +
        " grounded=" + player.IsStableGrounded + " swim=" + player.SwimmingActive + " climb=" + player.ClimbingActive + " mantle=" + player.MantlingActive : "no player";
}
