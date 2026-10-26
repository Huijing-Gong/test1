using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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

// Bounded check of the CURRENT main-scene copy, not the earlier visual-study prefab.
// Reuses the existing tower centreline/driver and the Prepare disposable-save contract.
[InitializeOnLoad]
public static class OverworldV2ReliefTowerChecks
{
    const string PrepareState = "QA.OverworldV2Play.", State = "QA.OverworldV2ReliefTower.";
    const string Report = "ToolReports/MapV2Relief/", Images = "Docs/MapV2/Relief/TowerGameView/";
    static readonly FieldInfo OrbitYaw = typeof(ThirdPersonCamera).GetField("yaw", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static BaselineFlow flow;
    static BaselineWorld world;
    static OverworldV2Layout layout;
    static PlayerMovement player;
    static Health health;
    static ThirdPersonCamera orbit;
    static MonoBehaviour host;
    static Coroutine coroutine;
    static Keyboard keyboard, oldKeyboard;
    static Mouse mouse, oldMouse;
    static bool oldKeyboardEnabled, oldMouseEnabled, oldBackground, oldCursorVisible, finished;
    static InputSettings.BackgroundBehavior oldBehavior;
    static CursorLockMode oldCursorLock;
    static float oldTimeScale, pivotHeight, mouseSensitivity;
    static double deadline, routeDeadline, stallDeadline, nextSample;
    static int lastFrame, failures, segment;
    static string phase, qaPrefix, realPrefix, primaryHash, backupHash;
    static List<Vector3> path;
    static float[] distances;
    static float progress, lastProgress, maxDeviation, maxHeightError, minCamera, longestTightCamera, tightRun;
    static bool climbed, mantled, swam;
    static readonly List<string> samples = new List<string>();
    public static bool Running { get; private set; }
    public static string Status => Running ? phase + " " + Pose() : SessionState.GetString(State + "status", "Not run");

    static OverworldV2ReliefTowerChecks()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode && Running) Stop(false); };
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (Running) Fail("Assembly reload interrupted tower check."); };
    }
    public static void Begin()
    {
        qaPrefix = SessionState.GetString(PrepareState + "prefix", "");
        realPrefix = SessionState.GetString(PrepareState + "realPrefix", "");
        if (!EditorApplication.isPlaying || Running || OverworldV2PlayChecks.Running || OverworldV2TutorialChecks.Running ||
            OverworldV2TraversalChecks.Running || OverworldV2CameraChecks.Running || OverworldV2WaystationChecks.Running || OverworldV2ReliefChecks.Running ||
            !SessionState.GetBool(PrepareState + "prepared", false) || SceneManager.sceneCount != 1 ||
            SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene ||
            !qaPrefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) || string.IsNullOrEmpty(realPrefix) || qaPrefix == realPrefix)
            throw new InvalidOperationException("Requires exclusive Play in the isolated scene made by OverworldV2PlayChecks.Prepare.");
        flow = Object.FindFirstObjectByType<BaselineFlow>();
        world = flow ? flow.GetComponent<BaselineWorld>() : null;
        layout = flow ? flow.GetComponent<OverworldV2Layout>() : null;
        player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null;
        health = player ? player.GetComponent<Health>() : null;
        orbit = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        host = SideWorldTravel.Instance;
        if (!flow || flow.saveKeyPrefix != qaPrefix || !world || !layout || !flow.Tutorial || !flow.endingEntrance || !flow.endingRoom ||
            !player || !health || !orbit || OrbitYaw == null || !host || !host.isActiveAndEnabled)
            throw new InvalidOperationException("Wait for scene Start; complete V2 player, ending room and persistent coroutine host required.");
        var cameraSettings = new SerializedObject(orbit);
        pivotHeight = cameraSettings.FindProperty("targetHeight").floatValue;
        mouseSensitivity = cameraSettings.FindProperty("mouseSensitivity").floatValue;
        if (mouseSensitivity <= 0) throw new InvalidOperationException("Existing mouse sensitivity is not positive.");
        Directory.CreateDirectory(Report); Directory.CreateDirectory(Images);
        File.WriteAllText(Report + "tower-checks.txt", "METHOD: isolated current-scene copy and disposable save slot; controlled placement at the start of each stair direction. " +
            "The real controller walks with injected W and mouse delta along the existing 92-segment spiral and arrival terrace. " +
            "No per-frame relocation, jump, climb, runtime stat edits or navigation-system replacement. Ending entry/return use public APIs; " +
            "victory is triggered through target Health.TryApplyDamage API, not a PlayerCombat/attack-input test. Screenshots are Game View.\n");
        primaryHash = SlotHash(realPrefix + ".primary"); backupHash = SlotHash(realPrefix + ".backup");
        oldKeyboard = Keyboard.current; oldMouse = Mouse.current;
        oldKeyboardEnabled = oldKeyboard != null && oldKeyboard.enabled; oldMouseEnabled = oldMouse != null && oldMouse.enabled;
        oldBehavior = InputSystem.settings.backgroundBehavior; oldBackground = Application.runInBackground;
        oldTimeScale = Time.timeScale; oldCursorLock = Cursor.lockState; oldCursorVisible = Cursor.visible;
        samples.Clear(); samples.Add("phase,frame,time,x,y,z,hp,grounded,swimming,climbing,mantling,cameraPivotDistance,routeProgress");
        failures = 0; finished = false; path = null; lastFrame = -1; phase = "initializing";
        Running = true; deadline = EditorApplication.timeSinceStartup + 240; nextSample = 0;
        SessionState.SetBool(State + "completed", false);
        try
        {
            keyboard = InputSystem.AddDevice<Keyboard>("V2ReliefTowerQAKeyboard"); mouse = InputSystem.AddDevice<Mouse>("V2ReliefTowerQAMouse");
            if (oldKeyboardEnabled) InputSystem.DisableDevice(oldKeyboard); if (oldMouseEnabled) InputSystem.DisableDevice(oldMouse);
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; Application.runInBackground = true;
            EditorApplication.ExecuteMenuItem("Window/General/Game"); EditorApplication.update += Tick;
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
        phase = "fresh journey"; flow.NewGame(); yield return new WaitForSecondsRealtime(.4f);
        flow.Tutorial.Cancel(); yield return new WaitForSecondsRealtime(.3f);
        Check(!flow.Tutorial.Active && !layout.BridgeOpen && !player.IsGameplayInputLocked, "Teaching cancelled without unlocking north bridge; controller available");
        // Reuse the established tower route, shifted by the authored entrance's actual height.
        // The exact XZ dimensions are an assertion about this known map, not a generic pathfinder.
        var entrance = flow.endingEntrance.position;
        Check(Mathf.Abs(entrance.x - 55.925f) < .1f && Mathf.Abs(entrance.z - 61) < .1f, "Existing west-side tower entrance identity and XZ retained");
        float top = entrance.y - .05f;
        var points = TowerPath(top);
        Record("LAYOUT tower floor=" + F(top - 20) + " terrace=" + F(top) + " entrance=" + entrance);
        yield return Walk("tower ascent", points, "tower-top-game-view.png");

        phase = "ending API round trip";
        GameSession.Instance.RestoreProgress(true, true, false);
        world.accepted[0] = true; world.opened[1] = true; world.Track(0);
        Check(flow.SaveNow(), "Disposable progress fixture saved: task 0 tracked and hidden chest 1 opened");
        Vector3 returnAt = player.transform.position;
        Check(Vector3.Distance(returnAt, entrance) < 3.4f, "Actual ascent ended within existing ending interaction distance");
        Check(flow.EnterEnding(), "Optional ending room EnterEnding API accepted at tower doorway");
        yield return new WaitForSecondsRealtime(.8f);
        Check(flow.endingRoom.IsInside && player.IsStableGrounded && !health.IsDead, "Ending room arrival settles on circular platform");
        var room = GameObject.Find("Ending Room (runtime)");
        Check(room && room.GetComponent<NonClimbableSurface>() && room.GetComponentsInChildren<BoxCollider>().Length >= 65,
            "Existing room retains non-climbable wall and overhead colliders");
        yield return Capture("ending-room-game-view.png");
        var target = GameObject.Find("Passive final figure");
        var targetHealth = target ? target.GetComponent<Health>() : null;
        Check(targetHealth && !targetHealth.IsDead, "Original passive final figure is available");
        Check(targetHealth.TryApplyDamage(new DamageInfo(targetHealth.MaxHealth, player.gameObject, target.transform.position, Vector3.forward)),
            "Controlled Health damage API killed passive figure; no attack-input claim");
        yield return new WaitForSecondsRealtime(.4f);
        Check(flow.endingRoom.Completed && flow.VictoryOpen && flow.Progress.bossDefeated && GameSession.Instance.BossDefeated,
            "Existing death event opens victory and marks boss completion");
        Check(world.accepted[0] && world.opened[1] && world.Tracked == 0 && !layout.BridgeOpen,
            "Victory retains selected task, chest, tracking and incomplete bridge state");
        Check(BaselineSaveStore.TryLoad(qaPrefix, out var saved, out var message) && saved.bossDefeated && saved.opened[1] && saved.accepted[0] && saved.tracked == 0,
            "Victory persisted into disposable slot: " + message);
        yield return Capture("victory-game-view.png");
        flow.LeaveEnding(); yield return new WaitForSecondsRealtime(.8f);
        Check(!flow.endingRoom.IsInside && !flow.VictoryOpen && Vector3.Distance(player.transform.position, returnAt) < .5f && player.IsStableGrounded,
            "LeaveEnding API returns safely to actual raised doorway pose");
        Check(!player.IsGameplayInputLocked && Time.timeScale > 0 && world.accepted[0] && world.opened[1] && world.Tracked == 0,
            "Return releases movement and retains progress");
        Check(flow.ContinueGame(), "ContinueGame API reloads the disposable victory save");
        yield return new WaitForSecondsRealtime(.7f);
        Check(flow.endingRoom.Completed && flow.Progress.bossDefeated && world.accepted[0] && world.opened[1] && world.Tracked == 0 &&
            player.IsStableGrounded && Vector3.Distance(player.transform.position, returnAt) < .65f && !flow.VictoryOpen,
            "Continue preserves completion/progress and safe high tower pose without replaying victory");

        points.Reverse();
        yield return Walk("tower descent", points, "tower-base-game-view.png");
        CheckRealSave(); finished = true; SessionState.SetBool(State + "completed", true);
        Record("COMPLETE injected-input stair directions and ending APIs. Human stair/camera comfort, E interaction and attack inputs remain manual tests.");
    }
    static List<Vector3> TowerPath(float top)
    {
        var points = new List<Vector3>();
        for (int i = 0; i <= 92; i++)
        {
            float a = (-135 + i * 360f / 92) * Mathf.Deg2Rad;
            points.Add(new Vector3(61 + Mathf.Sin(a) * 7.5f, top - 20 + i * 20f / 92, 61 + Mathf.Cos(a) * 7.5f));
        }
        for (int i = 1; i <= 12; i++)
        {
            float a = (225 + i * 45f / 12) * Mathf.Deg2Rad;
            points.Add(new Vector3(61 + Mathf.Sin(a) * 7.5f, top, 61 + Mathf.Cos(a) * 7.5f));
        }
        points.Add(new Vector3(54.6f, top, 61)); return points;
    }
    static IEnumerator Walk(string label, List<Vector3> points, string image)
    {
        phase = label; path = null; QueueInput(Vector3.zero, false);
        player.Relocate(points[0] + Vector3.up * .09f, Quaternion.Euler(0, Heading(points[0], points[1]), 0));
        orbit.ResetOrbit(Heading(points[0], points[1]), 14); Physics.SyncTransforms();
        Record("CONTROLLED_START " + label + "=" + points[0] + "; no later relocation during this direction");
        yield return new WaitForSecondsRealtime(.7f);
        Check(player.IsStableGrounded && !health.IsDead && !player.SwimmingActive, label + " controlled start settled");
        int hpBefore = health.CurrentHealth;
        distances = new float[points.Count];
        for (int i = 1; i < points.Count; i++) distances[i] = distances[i - 1] + Planar(points[i - 1], points[i]);
        progress = lastProgress = maxDeviation = maxHeightError = tightRun = longestTightCamera = 0;
        minCamera = float.PositiveInfinity; climbed = mantled = swam = false; segment = 0;
        routeDeadline = EditorApplication.timeSinceStartup + 90; stallDeadline = EditorApplication.timeSinceStartup + 10;
        path = points;
        while (path != null) yield return null;
        QueueInput(Vector3.zero, false); yield return new WaitForSecondsRealtime(.4f);
        var end = points[points.Count - 1];
        Record("METRICS " + label + " final=" + player.transform.position + " finalXZ=" + F(Planar(player.transform.position, end)) +
            " heightError=" + F(Mathf.Abs(player.transform.position.y - end.y)) + " progress=" + F(progress) + "/" + F(distances[distances.Length - 1]) +
            " maxDeviation=" + F(maxDeviation) + " maxHeightError=" + F(maxHeightError) + " minCamera=" + F(minCamera) + " longestCameraBelow2m=" + F(longestTightCamera));
        Check(Planar(player.transform.position, end) <= .4f && Mathf.Abs(player.transform.position.y - end.y) < .6f && player.IsStableGrounded,
            label + " reached exact authored endpoint and stable ground");
        Check(maxDeviation <= 1.15f && maxHeightError < 1 && !climbed && !mantled && !swam && health.CurrentHealth == hpBefore,
            label + " ordinary walk only, unchanged HP and bounded route deviation");
        // A close camera is recorded rather than disguised as a movement failure; manual comfort remains required.
        Record("OBSERVE " + label + " longest camera under 2m=" + F(longestTightCamera) + " seconds; manual orbit/comfort not proved");
        yield return Capture(image);
    }
    static Vector3 AdvanceDriver()
    {
        var p = player.transform.position;
        float best = float.PositiveInfinity, bestT = 0; int bestSegment = segment;
        for (int i = Mathf.Max(0, segment - 2); i <= Mathf.Min(path.Count - 2, segment + 8); i++)
        {
            var a = path[i]; var b = path[i + 1]; var ab = new Vector2(b.x - a.x, b.z - a.z);
            float t = Mathf.Clamp01(Vector2.Dot(new Vector2(p.x - a.x, p.z - a.z), ab) / Mathf.Max(.0001f, ab.sqrMagnitude));
            float d = Planar(p, Vector3.Lerp(a, b, t)); if (d < best) { best = d; bestT = t; bestSegment = i; }
        }
        var nearest = Vector3.Lerp(path[bestSegment], path[bestSegment + 1], bestT);
        maxDeviation = Mathf.Max(maxDeviation, best); maxHeightError = Mathf.Max(maxHeightError, Mathf.Abs(p.y - nearest.y));
        if (maxDeviation > 1.15f || maxHeightError >= 1) throw new InvalidOperationException("Tower centreline deviation/height limit exceeded.");
        float projected = Mathf.Lerp(distances[bestSegment], distances[bestSegment + 1], bestT);
        if (projected > progress) { progress = projected; segment = bestSegment; }
        if (progress > lastProgress + .1f) { lastProgress = progress; stallDeadline = EditorApplication.timeSinceStartup + 10; }
        float length = distances[distances.Length - 1];
        if (length - progress <= .6f && Planar(p, path[path.Count - 1]) <= .3f) { path = null; return Vector3.zero; }
        if (EditorApplication.timeSinceStartup > routeDeadline || EditorApplication.timeSinceStartup > stallDeadline)
            throw new TimeoutException("Tower walk hit its bounded time/stall limit.");
        float ahead = Mathf.Min(length, progress + 1.4f); int target = segment;
        while (target < path.Count - 2 && distances[target + 1] < ahead) target++;
        return Vector3.Lerp(path[target], path[target + 1], Mathf.InverseLerp(distances[target], distances[target + 1], ahead));
    }
    static void QueueInput(Vector3 target, bool drive)
    {
        if (keyboard == null || !keyboard.added || mouse == null || !mouse.added) return;
        if (!keyboard.enabled) InputSystem.EnableDevice(keyboard); if (!mouse.enabled) InputSystem.EnableDevice(mouse);
        if (Keyboard.current != keyboard) keyboard.MakeCurrent(); if (Mouse.current != mouse) mouse.MakeCurrent();
        if (!drive) { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.QueueStateEvent(mouse, new MouseState()); return; }
        float desired = Heading(player.transform.position, target), yaw = (float)OrbitYaw.GetValue(orbit);
        float correction = Mathf.Clamp(Mathf.DeltaAngle(yaw, desired) * .55f, -2.5f, 2.5f);
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(correction / mouseSensitivity, 0) });
        InputSystem.QueueStateEvent(keyboard, Mathf.Abs(Mathf.DeltaAngle(orbit.transform.eulerAngles.y, desired)) < 35 ? new KeyboardState(Key.W) : new KeyboardState());
    }
    static void Tick()
    {
        if (!Running) return;
        try
        {
            if (!EditorApplication.isPlaying || !flow || flow.saveKeyPrefix != qaPrefix || SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene)
                throw new InvalidOperationException("Play/scene/host/save-prefix changed.");
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Tower suite exceeded240seconds.");
            EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
            Vector3 target = path != null ? AdvanceDriver() : Vector3.zero; QueueInput(target, path != null);
            if (path != null)
            {
                if (health.IsDead || player.IsGameplayInputLocked || Time.timeScale <= 0) throw new InvalidOperationException("Tower walk died, locked or paused.");
                climbed |= player.ClimbingActive; mantled |= player.MantlingActive; swam |= player.SwimmingActive;
                float cameraDistance = CameraDistance(); minCamera = Mathf.Min(minCamera, cameraDistance);
                tightRun = cameraDistance < 2 ? tightRun + Time.unscaledDeltaTime : 0; longestTightCamera = Mathf.Max(longestTightCamera, tightRun);
            }
            if (EditorApplication.timeSinceStartup < nextSample) return; nextSample = EditorApplication.timeSinceStartup + .1;
            var p = player.transform.position;
            samples.Add(string.Join(",", phase, Time.frameCount, F(Time.time), F(p.x), F(p.y), F(p.z), health.CurrentHealth,
                player.IsStableGrounded, player.SwimmingActive, player.ClimbingActive, player.MantlingActive, F(CameraDistance()), F(progress)));
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }
    static IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame(); var image = ScreenCapture.CaptureScreenshotAsTexture();
        if (!image) throw new InvalidOperationException("Game View capture unavailable.");
        try { File.WriteAllBytes(Images + name, image.EncodeToPNG()); Record("GAME_VIEW " + Images + name + " " + Pose()); }
        finally { Object.Destroy(image); }
    }
    static void Check(bool ok, string message)
    {
        Record((ok ? "PASS " : "FAIL ") + message + " | " + Pose()); if (!ok) throw new InvalidOperationException(message);
    }
    static void Fail(string message)
    {
        failures++; finished = false;
        try { Record("FAIL " + message + " | " + Pose()); } finally { Stop(); }
    }
    public static void Stop(bool exitPlay = true)
    {
        if (!Running) return; Running = false; path = null; EditorApplication.update -= Tick;
        var cleanupErrors = new List<string>();
        Action<Action> restore = action => { try { action(); } catch (Exception e) { cleanupErrors.Add(e.Message); } };
        restore(() => { if (host && coroutine != null) host.StopCoroutine(coroutine); coroutine = null; });
        restore(() => { if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); keyboard = null; });
        restore(() => { if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); mouse = null; });
        restore(() => { if (oldKeyboard != null && oldKeyboard.added) { if (oldKeyboardEnabled) InputSystem.EnableDevice(oldKeyboard); else InputSystem.DisableDevice(oldKeyboard); oldKeyboard.MakeCurrent(); } });
        restore(() => { if (oldMouse != null && oldMouse.added) { if (oldMouseEnabled) InputSystem.EnableDevice(oldMouse); else InputSystem.DisableDevice(oldMouse); oldMouse.MakeCurrent(); } });
        restore(() => InputSystem.settings.backgroundBehavior = oldBehavior); restore(() => Application.runInBackground = oldBackground);
        restore(() => Time.timeScale = oldTimeScale); restore(() => { Cursor.lockState = oldCursorLock; Cursor.visible = oldCursorVisible; });
        restore(CheckRealSave);
        bool ok = finished && failures == 0 && cleanupErrors.Count == 0;
        SessionState.SetBool(State + "completed", ok); SessionState.SetString(State + "status", ok ? "PASS tower checks; restored input; Prepare teardown pending" : "FAIL/INCOMPLETE; inspect tower-checks.txt");
        try
        {
            File.WriteAllLines(Report + "tower-samples.csv", samples);
            Record("RESTORED input/background/time/cursor before Play exit. QA prefix retained through teardown; original Prepare owns disposable-slot deletion and main-scene/hash restoration. completed=" + ok + " errors=" + string.Join("; ", cleanupErrors));
        }
        finally { if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying = false; }
    }
    static void CheckRealSave()
    {
        if (SlotHash(realPrefix + ".primary") != primaryHash || SlotHash(realPrefix + ".backup") != backupHash)
            throw new InvalidOperationException("Real save primary/backup changed during isolated tower check.");
        Record("PASS real save primary/backup hashes unchanged");
    }
    static string SlotHash(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(PlayerPrefs.GetString(key))));
    }
    static void Record(string text) => File.AppendAllText(Report + "tower-checks.txt", DateTime.UtcNow.ToString("O") + " " + text + "\n");
    static float Planar(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    static float Heading(Vector3 a, Vector3 b) => Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
    static float CameraDistance() => Vector3.Distance(player.transform.position + Vector3.up * pivotHeight, orbit.transform.position);
    static string F(float value) => value.ToString("0.###", Invariant);
    static string Pose() => player ? "pos=" + player.transform.position + " HP=" + (health ? health.CurrentHealth : -1) +
        " grounded=" + player.IsStableGrounded + " swim=" + player.SwimmingActive + " climb=" + player.ClimbingActive : "no player";
}
