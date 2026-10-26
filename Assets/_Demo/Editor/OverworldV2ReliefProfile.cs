using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Import into an Editor folder only when ready. Call the existing Prepare(),
// enter Play, wait for Start, then Begin(). No input devices are created.
[InitializeOnLoad]
public static class OverworldV2ReliefProfile
{
    const string PrepareState = "QA.OverworldV2Play.";
    const string State = "QA.OverworldV2ReliefProfile.";
    const string Report = "ToolReports/MapV2Relief/editor-profile.txt";
    const string Csv = "ToolReports/MapV2Relief/editor-profile-frames.csv";
    const string Images = "Docs/MapV2/Relief/ProfileGameView/";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string[] OrbitFields = { "yaw", "pitch", "currentCameraDistance", "collisionDistanceVelocity",
        "positionSmoothVelocity", "shakeAmplitude", "shakeDuration", "shakeTimeRemaining", "shakePhase", "appliedShakeOffset" };
    static readonly Dictionary<FieldInfo, object> oldOrbitFields = new Dictionary<FieldInfo, object>();
    static BaselineFlow flow;
    static BaselineWorld world;
    static OverworldV2Layout layout;
    static PlayerMovement player;
    static ThirdPersonCamera follow;
    static Camera camera;
    static Coroutine routine;
    static string prefix, phase;
    static double deadline;
    static bool snapshot, finished, oldMoveLock, oldLookLock, oldCinematic, oldBackground, oldCursorVisible;
    static CursorLockMode oldCursorLock;
    static float oldTimeScale, oldFov;
    static Vector3 oldPlayerPosition, oldCameraPosition;
    static Quaternion oldPlayerRotation, oldCameraRotation;
    public static bool Running { get; private set; }
    public static string Status => Running ? phase : SessionState.GetString(State + "status", "Not run");

    static OverworldV2ReliefProfile()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (Running) Fail("Assembly reload interrupted profiling."); };
    }

    public static void Begin()
    {
        prefix = SessionState.GetString(PrepareState + "prefix", "");
        if (!EditorApplication.isPlaying || Running || OverworldV2PlayChecks.Running || OverworldV2ReliefChecks.Running ||
            OverworldV2TutorialChecks.Running || OverworldV2TraversalChecks.Running || OverworldV2CameraChecks.Running ||
            OverworldV2WaystationChecks.Running || !SessionState.GetBool(PrepareState + "prepared", false) ||
            SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene ||
            !prefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) || prefix == SessionState.GetString(PrepareState + "realPrefix", ""))
            throw new InvalidOperationException("Run alone in the copied scene prepared by OverworldV2PlayChecks.Prepare.");
        flow = Object.FindFirstObjectByType<BaselineFlow>();
        world = flow ? flow.GetComponent<BaselineWorld>() : null;
        layout = flow ? flow.GetComponent<OverworldV2Layout>() : null;
        player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null;
        camera = Camera.main; follow = camera ? camera.GetComponent<ThirdPersonCamera>() : null;
        if (!flow || flow.saveKeyPrefix != prefix || !flow.Tutorial || !world || !layout || !player || !camera || !follow || !flow.endingEntrance)
            throw new InvalidOperationException("Missing started QA host, camera, layout or tower anchor.");
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); Directory.CreateDirectory(Images);
        File.WriteAllText(Report, "METHOD: Unity Editor Play in Prepare's copied scene and disposable save slot. " +
            "NewGame/Skip APIs, five controlled player placements and normal third-person follow camera with input locked. " +
            "Each pose settles for 2 real seconds, then records 4 real seconds of Time.unscaledDeltaTime. " +
            "This is an Editor observation, NOT a standalone/WebGL benchmark or continuous gameplay test. " +
            "Editor overhead, vSync, Game View resolution and background workload influence the values. No FPS target/pass verdict.\n" +
            "Memory is Unity Profiler endpoint counters, not OS process RAM/working set or measured GPU VRAM; no forced GC. " +
            "Screenshot encoding and report IO occur AFTER each measurement window. No quality setting, object visibility or input device changes.\n");
        File.WriteAllText(Csv, "pose,frame,unscaled_delta_ms\n");
        Record("ENV unity=" + Application.unityVersion + " os=" + SystemInfo.operatingSystem + " cpu=" + SystemInfo.processorType +
            " logicalProcessors=" + SystemInfo.processorCount + " hardwareSystemMemoryMiB=" + SystemInfo.systemMemorySize +
            " gpu=" + SystemInfo.graphicsDeviceName + " graphicsAPI=" + SystemInfo.graphicsDeviceType +
            " driver=" + SystemInfo.graphicsDeviceVersion + " reportedGPUCapacityMiB=" + SystemInfo.graphicsMemorySize);
        Record("ENV GameView=" + Screen.width + "x" + Screen.height + " display=" + Screen.currentResolution +
            " quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()] + " vSyncCount=" + QualitySettings.vSyncCount +
            " targetFrameRate=" + Application.targetFrameRate + " profilerEnabled=" + Profiler.enabled);
        oldBackground = Application.runInBackground; oldTimeScale = Time.timeScale;
        oldCursorLock = Cursor.lockState; oldCursorVisible = Cursor.visible;
        SessionState.SetString(State + "prefix", prefix); SessionState.SetBool(State + "awaitRestore", true);
        SessionState.SetBool(State + "completed", false);
        finished = false; snapshot = false; Running = true; phase = "initializing";
        deadline = EditorApplication.timeSinceStartup + 110;
        Application.runInBackground = true;
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        EditorApplication.update += Tick;
        routine = flow.StartCoroutine(GuardedRun());
    }

    static IEnumerator GuardedRun()
    {
        var steps = Run();
        while (Running)
        {
            bool next;
            try { next = steps.MoveNext(); }
            catch (Exception exception) { Fail(exception.ToString()); yield break; }
            if (!next) { Stop(); yield break; }
            yield return steps.Current;
        }
    }

    static IEnumerator Run()
    {
        flow.NewGame(); yield return new WaitForSecondsRealtime(.25f);
        if (!flow.Started || !flow.Tutorial.Active) throw new InvalidOperationException("QA NewGame did not start.");
        flow.Tutorial.Skip(); yield return new WaitForSecondsRealtime(.35f); yield return null; yield return null;
        if (flow.Tutorial.Active || flow.MenuOpen || Time.timeScale != 1 || player.IsGameplayInputLocked || follow.CinematicControlled)
            throw new InvalidOperationException("Normal QA play did not resume after Skip.");
        CaptureTemporaryState();
        var positions = new[] { layout.ArrivalPosition, layout.CampPosition + new Vector3(0,0,-4),
            new Vector3(38,30,31), layout.IntroShorePosition + Vector3.back * .8f, flow.endingEntrance.position + Vector3.left * 1.2f };
        string[] names = { "spawn", "hub", "central-forest", "lake-shore", "tower-top" };
        float[] headings = { 0,-45,45,-5,225 }; float[] pitches = { 12,16,16,14,22 };
        var samples = new List<double>(2048); var frames = new List<int>(2048);
        for (int i=0;i<positions.Length;i++)
        {
            phase = names[i];
            if (!world.NavigationGround(positions[i], out var floor)) throw new InvalidOperationException("No valid standing ground for " + phase);
            player.Relocate(new Vector3(positions[i].x,floor.y-.1f,positions[i].z),Quaternion.Euler(0,headings[i],0));
            // Relocate releases the input lock; reapply only this fixture's locks.
            player.SetGameplayInputLocked(true); follow.SetLookInputLocked(true);
            Physics.SyncTransforms(); follow.ResetOrbit(headings[i],pitches[i]);
            yield return new WaitForSecondsRealtime(2);
            if (player.SwimmingActive || player.ClimbingActive || player.MantlingActive)
                throw new InvalidOperationException("Controlled profiling pose entered traversal state: " + phase);
            samples.Clear(); frames.Clear();
            double started = Time.realtimeSinceStartupAsDouble, end = started + 4;
            while (Time.realtimeSinceStartupAsDouble < end)
            {
                yield return null;
                if (Time.timeScale != 1 || !player.IsGameplayInputLocked || !follow.IsLookInputLocked || flow.MenuOpen)
                    throw new InvalidOperationException("User/UI or lock state changed during sampling.");
                samples.Add(Time.unscaledDeltaTime * 1000.0); frames.Add(Time.frameCount);
                if (samples.Count >= 10000) throw new InvalidOperationException("Bounded sample count exceeded.");
            }
            double elapsed = Time.realtimeSinceStartupAsDouble - started;
            if (samples.Count == 0) throw new InvalidOperationException("No rendered-loop samples recorded.");
            var sorted = samples.ToArray(); Array.Sort(sorted); double total=0; foreach (double value in samples) total+=value;
            Record("POSE " + phase + " samples=" + samples.Count + " elapsedSeconds=" + F(elapsed) +
                " meanMs=" + F(total/samples.Count) + " p50Ms=" + F(Percentile(sorted,.50)) + " p95Ms=" + F(Percentile(sorted,.95)) +
                " minMs=" + F(sorted[0]) + " maxMs=" + F(sorted[sorted.Length-1]) + " percentile=nearest-rank" +
                " player=" + V(player.transform.position) + " camera=" + V(camera.transform.position) +
                " cameraEuler=" + V(camera.transform.eulerAngles) + " fov=" + F(camera.fieldOfView) + " GameView=" + Screen.width + "x" + Screen.height);
            Record("MEMORY_ENDPOINT " + phase + " unityAllocatedBytes=" + Profiler.GetTotalAllocatedMemoryLong() +
                " unityReservedBytes=" + Profiler.GetTotalReservedMemoryLong() + " monoUsedBytes=" + Profiler.GetMonoUsedSizeLong() +
                " monoReservedBytes=" + Profiler.GetMonoHeapSizeLong() + " graphicsDriverAllocatedBytes=" + GraphicsBytes());
            var csv = new StringBuilder(samples.Count*40);
            for (int n=0;n<samples.Count;n++) csv.Append(phase).Append(',').Append(frames[n]).Append(',').Append(F(samples[n])).Append('\n');
            File.AppendAllText(Csv,csv.ToString());
            yield return new WaitForEndOfFrame();
            Texture2D pixels = ScreenCapture.CaptureScreenshotAsTexture();
            if (!pixels) throw new InvalidOperationException("Game View capture returned no pixels.");
            try { File.WriteAllBytes(Images+phase+".png",pixels.EncodeToPNG()); Record("GAME_VIEW " + Images+phase+".png frame="+Time.frameCount+" size="+pixels.width+"x"+pixels.height); }
            finally { Object.Destroy(pixels); }
            yield return null;
        }
        finished = true; SessionState.SetBool(State+"completed",true);
        Record("COMPLETED five bounded Editor observations; no performance compliance claim. Screenshot allocations may influence later memory endpoints; counters are not clean-scene peaks.");
    }

    static void CaptureTemporaryState()
    {
        oldMoveLock=player.IsGameplayInputLocked; oldLookLock=follow.IsLookInputLocked; oldCinematic=follow.CinematicControlled;
        oldPlayerPosition=player.transform.position; oldPlayerRotation=player.transform.rotation;
        oldCameraPosition=camera.transform.position; oldCameraRotation=camera.transform.rotation; oldFov=camera.fieldOfView;
        oldOrbitFields.Clear();
        foreach (string name in OrbitFields)
        {
            var field=typeof(ThirdPersonCamera).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
            if (field==null) throw new InvalidOperationException("Camera state field changed: "+name);
            oldOrbitFields[field]=field.GetValue(follow);
        }
        snapshot=true;
    }
    static double Percentile(double[] sorted,double q) => sorted[Mathf.Clamp((int)Math.Ceiling(q*sorted.Length)-1,0,sorted.Length-1)];
    static string GraphicsBytes()
    {
        try
        {
            var method=typeof(Profiler).GetMethod("GetAllocatedMemoryForGraphicsDriver",BindingFlags.Public|BindingFlags.Static,null,Type.EmptyTypes,null);
            if (method==null) return "unavailable(API absent)";
            long value=Convert.ToInt64(method.Invoke(null,null),Inv);
            return value>0 ? value.ToString(Inv) : "unavailable(counter returned "+value.ToString(Inv)+")";
        }
        catch (Exception e) { return "unavailable("+e.GetType().Name+")"; }
    }
    static void Tick()
    {
        if (!Running) return;
        if (!EditorApplication.isPlaying || !flow || flow.saveKeyPrefix!=prefix) { Fail("Play state or isolated prefix changed."); return; }
        if (EditorApplication.timeSinceStartup>deadline) Fail("110-second bound exceeded; Game View may not be rendering.");
    }
    static void Fail(string message)
    {
        finished=false; SessionState.SetBool(State+"completed",false); Record("INCOMPLETE "+message); Stop();
    }
    public static void Stop(bool exitPlay=true)
    {
        EditorApplication.update-=Tick; Running=false;
        if (flow && routine!=null) flow.StopCoroutine(routine); routine=null;
        try
        {
            if (snapshot && player && follow && camera)
            {
                player.Relocate(oldPlayerPosition,oldPlayerRotation); player.SetGameplayInputLocked(oldMoveLock);
                follow.SetLookInputLocked(oldLookLock); follow.SetCinematicControl(oldCinematic);
                foreach (var pair in oldOrbitFields) pair.Key.SetValue(follow,pair.Value);
                camera.transform.SetPositionAndRotation(oldCameraPosition,oldCameraRotation); camera.fieldOfView=oldFov;
                Physics.SyncTransforms(); Record("RESTORED fixture player pose/input locks and full changed camera orbit state before Play exit.");
            }
        }
        catch (Exception e) { finished=false; SessionState.SetBool(State+"completed",false); Record("RESTORATION_ERROR "+e); }
        finally
        {
            snapshot=false; oldOrbitFields.Clear(); Application.runInBackground=oldBackground;
            Time.timeScale=oldTimeScale; Cursor.lockState=oldCursorLock; Cursor.visible=oldCursorVisible;
            SessionState.SetString(State+"status",finished?"Observations complete; awaiting Edit restoration":"Incomplete; see editor-profile.txt");
            if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying=false;
        }
    }
    static void OnModeChanged(PlayModeStateChange change)
    {
        if (change==PlayModeStateChange.ExitingPlayMode && Running) Stop(false);
        if (change==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(State+"awaitRestore",false)) EditorApplication.delayCall+=RecordRestoration;
    }
    static void RecordRestoration()
    {
        string slot=SessionState.GetString(State+"prefix",""); var scene=SceneManager.GetActiveScene();
        bool restored=!EditorApplication.isPlaying && scene.path=="Assets/_Demo/Scenes/OverworldBaseline.unity" && !scene.isDirty &&
            SceneManager.sceneCount==1 && !SessionState.GetBool(PrepareState+"prepared",false) &&
            !PlayerPrefs.HasKey(slot+".primary") && !PlayerPrefs.HasKey(slot+".backup");
        Record("RESTORATION_CHECK restored="+restored+" scene="+scene.path+" clean="+!scene.isDirty+" timeScale="+F(Time.timeScale)+
            "; original Prepare harness supplies real-save/main-scene hash audit in ToolReports/MapV2Migration/runtime-checks.txt.");
        SessionState.SetBool(State+"awaitRestore",false);
        SessionState.SetString(State+"status",restored && SessionState.GetBool(State+"completed",false)?
            "Five Editor observations complete; restored and isolated slot cleared (no FPS verdict)":"Incomplete or restoration issue; see report");
    }
    static string F(double value) => value.ToString("F3",Inv);
    static string V(Vector3 value) => "("+F(value.x)+","+F(value.y)+","+F(value.z)+")";
    static void Record(string value) => File.AppendAllText(Report,DateTime.UtcNow.ToString("O")+" "+value+"\n");
}
