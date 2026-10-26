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

// Run after OverworldV2PlayChecks.Prepare, exclusively in its copied Play scene.
// Controlled placements and state fixtures; actual E/R/M input is separately labelled.
[InitializeOnLoad]
public static class OverworldV2WaystationChecks
{
    const string PrepareState = "QA.OverworldV2Play.";
    const string State = "QA.OverworldV2Waystations.";
    const string Report = "ToolReports/MapV2Waystations/runtime-checks.txt";
    const string Images = "Docs/MapV2/Waystations/";
    static BaselineFlow flow;
    static BaselineWorld world;
    static OverworldV2Layout layout;
    static PlayerMovement player;
    static PlayerVitals vitals;
    static Health health;
    static ThirdPersonCamera orbit;
    static BaselineWaystation north, camp;
    static Keyboard keyboard, oldKeyboard;
    static Mouse mouse, oldMouse;
    static bool oldKeyboardEnabled, oldMouseEnabled, oldBackground, finished;
    static InputSettings.BackgroundBehavior oldBehavior;
    static Coroutine coroutine;
    static double deadline;
    static readonly List<GameObject> temporary = new List<GameObject>();
    public static bool Running { get; private set; }
    public static string Status => SessionState.GetString(State + "status", "Not run");

    static OverworldV2WaystationChecks()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (Running) Fail("Assembly reload interrupted the check."); };
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(State + "awaitRestore", false)) Restoration();
        };
    }
    public static void Begin()
    {
        string prefix = SessionState.GetString(PrepareState + "prefix", "");
        if (!EditorApplication.isPlaying || Running || OverworldV2PlayChecks.Running || OverworldV2CameraChecks.Running ||
            OverworldV2TraversalChecks.Running || !SessionState.GetBool(PrepareState + "prepared", false) ||
            SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene || SceneManager.sceneCount != 1 ||
            !prefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) || prefix == SessionState.GetString(PrepareState + "realPrefix", ""))
            throw new InvalidOperationException("Prepare the isolated V2 QA scene, enter Play, then run only this suite.");
        flow = Object.FindFirstObjectByType<BaselineFlow>();
        world = flow ? flow.GetComponent<BaselineWorld>() : null;
        layout = flow ? flow.GetComponent<OverworldV2Layout>() : null;
        player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null;
        vitals = player ? player.GetComponent<PlayerVitals>() : null;
        health = player ? player.GetComponent<Health>() : null;
        orbit = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        north = camp = null;
        foreach (var station in Object.FindObjectsByType<BaselineWaystation>(FindObjectsSortMode.None))
        {
            if (station.gameObject.scene != flow.gameObject.scene) continue;
            if (station.OffersSupplies) { if (camp) throw new InvalidOperationException("Multiple supply stations."); camp = station; }
            else { if (north) throw new InvalidOperationException("Multiple north return stations."); north = station; }
        }
        if (!flow || flow.saveKeyPrefix != prefix || !world || !layout || !player || !vitals || !health || !orbit || !north || !camp)
            throw new InvalidOperationException("Missing QA host/player/two authored waystations, or unsafe save prefix.");
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); Directory.CreateDirectory(Images);
        File.WriteAllText(Report, "METHOD: copied scene and disposable save prefix from original Prepare harness. Controlled player placements, temporary QA-only obstacles and vitals/progress fixtures. E/R/M are injected into a temporary Input System keyboard and processed by actual Update/LateUpdate. No human WASD, full tutorial, A/B challenge or full route claim. Game View captures follow WaitForEndOfFrame.\n");
        oldKeyboard = Keyboard.current; oldMouse = Mouse.current;
        oldKeyboardEnabled = oldKeyboard != null && oldKeyboard.enabled; oldMouseEnabled = oldMouse != null && oldMouse.enabled;
        oldBehavior = InputSystem.settings.backgroundBehavior; oldBackground = Application.runInBackground;
        keyboard = InputSystem.AddDevice<Keyboard>("V2WaystationQAKeyboard"); mouse = InputSystem.AddDevice<Mouse>("V2WaystationQAMouse");
        if (oldKeyboardEnabled) InputSystem.DisableDevice(oldKeyboard);
        if (oldMouseEnabled) InputSystem.DisableDevice(oldMouse);
        keyboard.MakeCurrent(); mouse.MakeCurrent();
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; Application.runInBackground = true;
        SessionState.SetString(State + "prefix", prefix); SessionState.SetBool(State + "awaitRestore", true);
        SessionState.SetBool(State + "completed", false); SessionState.SetString(State + "status", "Running bounded waystation checks");
        finished = false; Running = true; deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.ExecuteMenuItem("Window/General/Game"); EditorApplication.update += Tick;
        coroutine = flow.StartCoroutine(Guarded());
    }
    static IEnumerator Guarded()
    {
        // Flatten nested enumerators so failures in key presses/placements are caught as well.
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
        flow.NewGame(); yield return new WaitForSecondsRealtime(.3f);
        Check(flow.Tutorial && flow.Tutorial.Active, "NewGame starts the real tutorial in isolated QA slot");
        flow.Tutorial.Skip(); yield return new WaitForSecondsRealtime(.4f);
        Check(layout.BridgeOpen && !flow.BlockWorldInput, "Explicit Skip opens bridge and releases interaction before station checks");
        // Seed representative existing progress, never add a reward or a new task identity.
        world.accepted[0] = true; world.completed[0] = true; world.accepted[2] = true; world.opened[1] = true; world.Track(2);
        Check(flow.SaveNow(), "Representative quest/chest/tracking fixture saved only in QA slot");
        string progress = ProgressWithoutPose(); int beacons = flow.Progress.recallBeaconsUsed;

        yield return Place(camp.InteractionPosition + Vector3.back * 1.1f, 0);
        Check(camp.CanInteract(flow) && vitals.IsAtSupplyStation && !vitals.IsAtCamp, "E5 approach is grounded, in range, visible and recognized as supply station, separate from central camp");
        vitals.Refill(); Check(vitals.ApplyEnvironmentDamage(35), "QA E5 health depletion accepted");
        SetProperty(vitals, "Stamina", 24f); SetProperty(vitals, "AidCharges", 0);
        yield return Press(Key.R);
        Check(health.CurrentHealth == health.MaxHealth && Mathf.Abs(vitals.Stamina - vitals.MaximumStamina) < .01f && vitals.AidCharges > 0,
            "Injected R at E5 replenishes health, stamina and depleted aid charges");
        yield return new WaitForEndOfFrame(); Capture("tower-camp-game-view.png");
        Check(progress == ProgressWithoutPose() && flow.Progress.recallBeaconsUsed == beacons, "E5 rest does not mutate quest/chest/equipment/tracking/bridge or recall spend");

        Check(vitals.ApplyEnvironmentDamage(25), "QA pre-return health depletion accepted");
        SetProperty(vitals, "Stamina", 31f); SetProperty(vitals, "AidCharges", 1); SetField(vitals, "recoveryWait", 20f);
        int hp = health.CurrentHealth;
        yield return Press(Key.E);
        Check(Planar(player.transform.position, layout.CampPosition) < .6f && player.IsStableGrounded, "Injected E at E5 returns safely to existing C2 camp");
        Check(health.CurrentHealth == hp && vitals.AidCharges == 1 && vitals.Stamina < 40f, "Free return does not refill HP, field aid or stamina");
        Check(!player.IsGameplayInputLocked && !orbit.IsLookInputLocked && Time.timeScale == 1, "Return restores player/camera/time state");
        Check(progress == ProgressWithoutPose() && flow.Progress.recallBeaconsUsed == beacons, "E5 return preserves every serialized progress field except intended safe pose");
        Check(!north.TryReturn(), "Out-of-range north return API is refused at C2");

        yield return Place(north.InteractionPosition + Vector3.back * 1.1f, 0);
        Check(north.CanInteract(flow) && !vitals.IsAtCamp && !vitals.IsAtSupplyStation, "B6 approach is grounded/visible, and return-only device is not a supply camp");
        vitals.Refill(); Check(vitals.ApplyEnvironmentDamage(45), "QA north health depletion accepted");
        int aidBefore = vitals.AidCharges; int healthBefore = health.CurrentHealth;
        yield return Press(Key.R);
        Check(vitals.AidCharges == aidBefore - 1 && health.CurrentHealth > healthBefore && health.CurrentHealth < health.MaxHealth,
            "Injected R at north device uses one ordinary field aid, without camp refill");
        yield return new WaitForEndOfFrame(); Capture("north-return-game-view.png");

        north.enabled = false;
        Check(!north.TryReturn(), "Disabled station refuses return"); north.enabled = true;
        flow.SetMenu(true); Check(!north.TryReturn(), "Open menu refuses station return"); flow.SetMenu(false);
        yield return new WaitForSecondsRealtime(.2f);
        Check(north.CanInteract(flow), "Station recovers after enabled/menu checks");
        // Intentionally parent the temporary obstruction under the station: own art must also block LOS.
        var midpoint = (player.transform.position + north.InteractionPosition) * .5f + Vector3.up * .9f;
        var wall = Obstacle("QA_station_child_wall", midpoint, new Vector3(1.8f, 1.8f, .16f), north.transform);
        Check(!north.CanInteract(flow) && !north.TryReturn(), "A solid station-child wall blocks interaction through its own decoration");
        RemoveObstacle(wall); Check(north.CanInteract(flow), "Removing temporary wall restores line of sight");
        var block = Obstacle("QA_C2_arrival_obstruction", layout.CampPosition + Vector3.up * .9f, new Vector3(3, 2.4f, 3), null);
        Vector3 beforeBlocked = player.transform.position; Vector3 safeBefore = flow.Progress.safePosition; float yawBefore = flow.Progress.safeYaw;
        Check(!north.TryReturn() && Vector3.Distance(beforeBlocked, player.transform.position) < .01f &&
            flow.Progress.safePosition == safeBefore && flow.Progress.safeYaw == yawBefore,
            "Blocked C2 destination refuses movement and leaves safe pose unchanged");
        RemoveObstacle(block);
        // Exercise the real store's save-refusal branch in the disposable slot, without a new runtime seam.
        // Unsupported format is a persistence failure for this transaction, not an OS disk-write fault.
        string primaryKey = flow.saveKeyPrefix + ".primary";
        string oldPrimary = PlayerPrefs.GetString(primaryKey);
        string oldBackup = PlayerPrefs.GetString(flow.saveKeyPrefix + ".backup");
        beforeBlocked = player.transform.position; safeBefore = flow.Progress.safePosition; yawBefore = flow.Progress.safeYaw;
        try
        {
            PlayerPrefs.SetString(primaryKey, "{\"formatVersion\":999}");
            Check(!north.TryReturn() && Vector3.Distance(beforeBlocked, player.transform.position) < .01f &&
                flow.Progress.safePosition == safeBefore && flow.Progress.safeYaw == yawBefore,
                "Actual SaveStore incompatible-format refusal rolls back intended destination and does not move player");
            Check(PlayerPrefs.GetString(primaryKey) == "{\"formatVersion\":999}" &&
                PlayerPrefs.GetString(flow.saveKeyPrefix + ".backup") == oldBackup && progress == ProgressWithoutPose(),
                "Save refusal preserves incompatible primary, valid backup, beacon spend and all progress");
        }
        finally { PlayerPrefs.SetString(primaryKey, oldPrimary); PlayerPrefs.Save(); }
        yield return Press(Key.E);
        Check(Planar(player.transform.position, layout.CampPosition) < .6f && player.IsStableGrounded,
            "Injected E at B6 returns to safe C2 after obstacle removal");
        Check(progress == ProgressWithoutPose() && flow.Progress.recallBeaconsUsed == beacons,
            "B6 return preserves serialized progression, manual tracking and recall spend");

        yield return Place(camp.InteractionPosition + Vector3.back * 1.1f, 0);
        vitals.Refill(); Check(vitals.ApplyEnvironmentDamage(health.MaxHealth), "QA lethal damage delivered while at E5 camp");
        yield return new WaitForSecondsRealtime(.65f);
        Check(Planar(player.transform.position, layout.CampPosition) < .6f && !health.IsDead && player.IsStableGrounded,
            "Actual Vitals death handler still respawns at C2; E5 rest never became a checkpoint");
        Check(flow.Progress.recallBeaconsUsed == beacons && progress == ProgressWithoutPose(), "Death retains seeded progress and beacon count");
        yield return Press(Key.M); Check(flow.MapOpen, "Injected M opens actual region map");
        yield return new WaitForEndOfFrame(); Capture("waystations-map-game-view.png");
        flow.CloseMapToMenu(); flow.SetMenu(false); yield return new WaitForSecondsRealtime(.25f);

        // A fresh, deliberately unfinished nonmodal lesson must survive free return without bridge unlock.
        flow.NewGame(); yield return new WaitForSecondsRealtime(.25f); flow.Tutorial.StartPractice();
        yield return Place(north.InteractionPosition + Vector3.back * 1.1f, 0);
        var lesson = flow.Tutorial.CurrentLesson;
        Check(flow.Tutorial.Active && !flow.Tutorial.ModalOpen && !layout.BridgeOpen, "QA sets actual unfinished nonmodal opening lesson with bridge closed");
        Check(north.TryReturn(), "Return API remains usable during an unfinished nonmodal lesson");
        yield return new WaitForSecondsRealtime(.35f);
        Check(flow.Tutorial.Active && flow.Tutorial.CurrentLesson == lesson && !layout.BridgeOpen && !flow.Progress.openingCompleted,
            "Free return preserves unfinished tutorial phase and closed bridge; it does not skip or complete it");
        Record("PENDING: actual OS disk failure/browser quota exhaustion not injected; unsupported-format save refusal tested above. No browser B/A challenge rerun, human WASD, all-route or full tutorial test in this suite.");
        finished = true; SessionState.SetBool(State + "completed", true);
        Record("COMPLETE bounded waystation API/input checks. Captures require visual inspection; they do not prove route comfort.");
    }
    static IEnumerator Place(Vector3 requested, float yaw)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Check(world.NavigationGround(requested, out var floor), "Controlled start has clear allowed standing ground: " + requested);
        player.Relocate(new Vector3(requested.x, floor.y - .1f, requested.z), Quaternion.Euler(0, yaw, 0));
        Physics.SyncTransforms(); orbit.ResetOrbit(yaw, 14);
        yield return new WaitForSecondsRealtime(.5f);
        Check(player.IsStableGrounded && !player.SwimmingActive && !player.ClimbingActive, "Controlled start settled on dry ground");
    }
    static IEnumerator Press(Key key)
    {
        Record("INJECT " + key + " startFrame=" + Time.frameCount);
        Record("INPUT before " + InputEvidence());
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return new WaitForSecondsRealtime(.16f);
        Record("INPUT held " + InputEvidence());
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return new WaitForSecondsRealtime(.3f);
        Record("INPUT released " + InputEvidence());
    }
    static string InputEvidence() => "frame=" + Time.frameCount + " hp=" + health.CurrentHealth + " stamina=" + vitals.Stamina +
        " aid=" + vitals.AidCharges + " supplies=" + vitals.IsAtSupplyStation + " interact=" + player.CanStartInteraction +
        " simulation=" + vitals.SimulationActive + " current=" + (Keyboard.current == null ? "none" : Keyboard.current.name) +
        " qaEnabled=" + (keyboard != null && keyboard.enabled) + " r=" + (keyboard != null && keyboard.rKey.isPressed) +
        " rEdge=" + (keyboard != null && keyboard.rKey.wasPressedThisFrame) + " focus=" + Application.isFocused +
        " window=" + (EditorWindow.focusedWindow == null ? "none" : EditorWindow.focusedWindow.GetType().Name) +
        " routing=" + InputSystem.settings.editorInputBehaviorInPlayMode;
    static string ProgressWithoutPose()
    {
        world.CopyProgressTo(flow.Progress);
        var copy = JsonUtility.FromJson<BaselineSaveData>(JsonUtility.ToJson(flow.Progress));
        copy.safePosition = Vector3.zero; copy.safeYaw = 0;
        return JsonUtility.ToJson(copy);
    }
    static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
    static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static float Planar(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    static GameObject Obstacle(string name, Vector3 position, Vector3 size, Transform parent)
    {
        var item = new GameObject(name); temporary.Add(item); item.transform.SetParent(parent, true); item.transform.position = position;
        item.AddComponent<BoxCollider>().size = size; Physics.SyncTransforms(); return item;
    }
    static void RemoveObstacle(GameObject item)
    {
        if (!item) return; item.GetComponent<Collider>().enabled = false; temporary.Remove(item); Object.Destroy(item); Physics.SyncTransforms();
    }
    static void Capture(string filename)
    {
        var pixels = ScreenCapture.CaptureScreenshotAsTexture();
        if (!pixels) throw new InvalidOperationException("Game View screenshot returned no image.");
        try { File.WriteAllBytes(Images + filename, pixels.EncodeToPNG()); Record("GAME_VIEW " + Images + filename + " frame=" + Time.frameCount + " size=" + pixels.width + "x" + pixels.height + " player=" + player.transform.position + " camera=" + Camera.main.transform.position + " euler=" + Camera.main.transform.eulerAngles); }
        finally { Object.Destroy(pixels); }
    }
    static void Tick()
    {
        if (!Running) return;
        EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        if (!EditorApplication.isPlaying || !flow || flow.saveKeyPrefix != SessionState.GetString(State + "prefix", "")) Fail("Play/host/QA prefix changed.");
        else if (EditorApplication.timeSinceStartup > deadline) Fail("Bounded suite exceeded120seconds; inspect Game View/input state.");
    }
    static void Check(bool ok, string message) { Record((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new InvalidOperationException(message); }
    static void Record(string message) => File.AppendAllText(Report, DateTime.UtcNow.ToString("O") + " " + message + "\n");
    static void Fail(string message) { finished = false; SessionState.SetBool(State + "completed", false); Record("FAIL " + message); Stop(); }
    public static void Stop(bool exitPlay = true)
    {
        if (!Running) return;
        Running = false; EditorApplication.update -= Tick;
        foreach (var item in temporary) if (item) { foreach (var collider in item.GetComponents<Collider>()) collider.enabled = false; Object.Destroy(item); }
        temporary.Clear();
        if (north) north.enabled = true;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (oldKeyboard != null && oldKeyboard.added) { if (oldKeyboardEnabled) InputSystem.EnableDevice(oldKeyboard); oldKeyboard.MakeCurrent(); }
        if (oldMouse != null && oldMouse.added) { if (oldMouseEnabled) InputSystem.EnableDevice(oldMouse); oldMouse.MakeCurrent(); }
        InputSystem.settings.backgroundBehavior = oldBehavior; Application.runInBackground = oldBackground;
        Record("RESTORED original keyboard/mouse and background behavior before Play teardown; disposable save prefix remains in place.");
        SessionState.SetString(State + "status", finished ? "Checks completed; awaiting restoration" : "INCOMPLETE/FAIL; see report");
        if (flow && coroutine != null) flow.StopCoroutine(coroutine); coroutine = null;
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
        bool restored = !EditorApplication.isPlaying && scene.path == "Assets/_Demo/Scenes/OverworldBaseline.unity" && !scene.isDirty &&
            SceneManager.sceneCount == 1 && !SessionState.GetBool(PrepareState + "prepared", false) &&
            !PlayerPrefs.HasKey(prefix + ".primary") && !PlayerPrefs.HasKey(prefix + ".backup");
        Record((restored ? "PASS " : "FAIL ") + "Editor/QA slot restoration by original Prepare harness; scene=" + scene.path + " clean=" + !scene.isDirty + " time=" + Time.timeScale + ". Real-save/main-scene hashes are recorded in ToolReports/MapV2Migration/runtime-checks.txt.");
        SessionState.SetBool(State + "awaitRestore", false);
        SessionState.SetString(State + "status", restored && SessionState.GetBool(State + "completed", false) ? "PASS waystation checks and restoration; visual review pending" : "INCOMPLETE/FAIL; inspect reports");
    }
}
