using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Uses OverworldV2PlayChecks.Prepare's copied scene and isolated save slot.
// Public API playback and actual Game View captures; no NPC/input-playthrough claim.
[InitializeOnLoad]
public static class OverworldV2CameraChecks
{
    const string PrepareState = "QA.OverworldV2Play.";
    const string State = "QA.OverworldV2Camera.";
    const string Report = "ToolReports/MapV2Migration/camera-checks.txt";
    const string Images = "Docs/MapV2/Migration/";
    static BaselineFlow flow;
    static BaselineWorld world;
    static BaselineOpeningCamera tour;
    static ThirdPersonCamera follow;
    static PlayerMovement player;
    static Coroutine routine;
    static double deadline;
    static bool finished;
    public static bool Running { get; private set; }
    public static string Status => SessionState.GetString(State + "status", "Not run");

    static OverworldV2CameraChecks()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }
    public static void Begin()
    {
        string expected = SessionState.GetString(PrepareState + "prefix", "");
        if (!EditorApplication.isPlaying || Running || OverworldV2PlayChecks.Running ||
            !SessionState.GetBool(PrepareState + "prepared", false) ||
            SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene || SceneManager.sceneCount != 1 ||
            !expected.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) ||
            expected == SessionState.GetString(PrepareState + "realPrefix", ""))
            throw new InvalidOperationException("Prepare the isolated V2 QA scene, enter Play, then run this check alone.");
        flow = Object.FindFirstObjectByType<BaselineFlow>();
        world = flow ? flow.GetComponent<BaselineWorld>() : null;
        player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null;
        follow = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (!flow || flow.saveKeyPrefix != expected || !flow.Tutorial || !world || !player || !follow ||
            !flow.GetComponent<OverworldV2Layout>())
            throw new InvalidOperationException("Missing started host dependencies or an unsafe QA save prefix.");
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); Directory.CreateDirectory(Images);
        File.WriteAllText(Report, "METHOD: isolated copied scene; NewGame/Skip, controlled C2 placement and BaselineOpeningCamera.Play API. " +
            "No NPC approach, E/keyboard input, Esc/Tab skip or manual WASD claim. Input devices are unchanged. " +
            "Images are actual Game View captures taken after WaitForEndOfFrame, not Scene View/offscreen camera renders.\n");
        SessionState.SetString(State + "prefix", expected);
        SessionState.SetBool(State + "awaitRestore", true);
        SessionState.SetBool(State + "completed", false);
        SessionState.SetString(State + "status", "Running API camera playback and map capture");
        finished = false; Running = true; deadline = EditorApplication.timeSinceStartup + 65;
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
            bool hasNext;
            try { hasNext = steps.MoveNext(); }
            catch (Exception exception) { Fail(exception.ToString()); yield break; }
            if (!hasNext) { Stop(); yield break; }
            yield return steps.Current;
        }
    }
    static IEnumerator Run()
    {
        flow.NewGame(); yield return new WaitForSecondsRealtime(.25f);
        Check(flow.Started && flow.Tutorial.Active, "NewGame started the actual tutorial in the QA slot");
        flow.Tutorial.Skip(); yield return new WaitForSecondsRealtime(.25f);
        Check(!flow.Tutorial.Active && !player.IsGameplayInputLocked && Time.timeScale == 1,
            "Explicit Skip releases normal play before the independent camera API check");
        var layout = flow.GetComponent<OverworldV2Layout>();
        Vector3 standing = layout.CampPosition + new Vector3(0,0,-4);
        Check(world.NavigationGround(standing, out var floor), "Controlled C2 camera entry position has clear standing ground");
        player.Relocate(new Vector3(standing.x,floor.y-.1f,standing.z),Quaternion.identity);
        Physics.SyncTransforms(); follow.ResetOrbit(0,14);
        yield return new WaitForSecondsRealtime(.4f);
        Transform guide = null;
        foreach (var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None))
            if (item.gameObject.scene == world.gameObject.scene && item.action == BaselineInteraction.ActionKind.AcceptTask && item.index == 0)
                guide = item.transform;
        Check(guide, "Actual guide transform is available for the final shot");
        tour = flow.GetComponent<BaselineOpeningCamera>();
        if (!tour) tour = flow.gameObject.AddComponent<BaselineOpeningCamera>();
        // Match the tutorial's real camera ownership for HUD/Flow suppression.
        // This is fixture setup, not a claim that NPC approach input was exercised.
        typeof(BaselineCampTutorial).GetProperty("CameraTour").SetValue(flow.Tutorial,tour);
        bool callback = false;
        Check(tour.Play(flow, () => callback = true, guide), "Actual 14-second scenic camera API starts");
        Check(follow.CinematicControlled && player.IsGameplayInputLocked && Time.timeScale == 0,
            "Camera owns the existing view and tutorial pause/input lock");
        for (int i=0;i<tour.PlannedShots.Count;i++)
        {
            var shot = tour.PlannedShots[i];
            bool occupied = Physics.CheckSphere(shot.position,.32f,~0,QueryTriggerInteraction.Ignore);
            var hits = Physics.OverlapSphere(shot.position,.32f,~0,QueryTriggerInteraction.Ignore);
            string colliderNames = string.Join(" | ",Array.ConvertAll(hits,hit=>hit.name));
            Record("SHOT_ENDPOINT index="+i+" t="+F(shot.time)+" position="+V(shot.position)+" focus="+V(shot.focus)+
                " CheckSphere(r=.32)="+occupied+" colliders="+colliderNames);
        }
        float[] times = {4.6f,8.1f,11.6f};
        string[] names = {"camera-01-lake.png","camera-02-tower.png","camera-03-guide.png"};
        for (int i=0;i<times.Length;i++)
        {
            while (tour.IsPlaying && tour.Elapsed < times[i]) yield return null;
            Check(tour.IsPlaying, "Playback remains active at capture threshold "+F(times[i]));
            yield return new WaitForEndOfFrame();
            Capture(names[i],"targetElapsed="+F(times[i])+" actualElapsed="+F(tour.Elapsed));
        }
        while (tour.IsPlaying) yield return null;
        yield return null; yield return null;
        Check(callback && tour.Elapsed >= BaselineOpeningCamera.Duration, "Full playback finishes through its real callback");
        Check(Time.timeScale == 1 && !follow.CinematicControlled && !follow.IsLookInputLocked && !player.IsGameplayInputLocked,
            "Natural completion restores TimeScale=1, camera ownership and player/look input");
        Check(flow.ShowMap(), "ShowMap API opens the current regional map");
        yield return null; yield return new WaitForEndOfFrame();
        Capture("camera-04-region-map.png","mapOpen="+flow.MapOpen);
        flow.CloseMapToMenu(); flow.SetMenu(false);
        yield return null; yield return null;
        Check(!flow.MapOpen && !flow.MenuOpen && Time.timeScale == 1 && !player.IsGameplayInputLocked && !follow.IsLookInputLocked,
            "Closing the map restores normal play");
        finished = true;
        SessionState.SetBool(State + "completed", true);
        SessionState.SetString(State + "status", "Camera/map API checks completed; awaiting Edit-mode restoration");
        Record("COMPLETED camera playback, four Game View captures and input/time restoration checks. Endpoint collision results are observations; composition and readability require visual review.");
    }
    static void Capture(string filename,string detail)
    {
        Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
        if (!texture) throw new InvalidOperationException("Game View screenshot returned no pixels.");
        try
        {
            File.WriteAllBytes(Images+filename,texture.EncodeToPNG());
            var camera = Camera.main;
            Record("GAME_VIEW "+Images+filename+" frame="+Time.frameCount+" size="+texture.width+"x"+texture.height+" "+detail+
                " cameraPosition="+V(camera.transform.position)+" cameraEuler="+V(camera.transform.eulerAngles)+" fov="+F(camera.fieldOfView));
        }
        finally { Object.Destroy(texture); }
    }
    static void Tick()
    {
        if (!Running) return;
        EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        if (!EditorApplication.isPlaying || !flow || flow.saveKeyPrefix != SessionState.GetString(State+"prefix",""))
        { Fail("Play/host/QA prefix changed during the check."); return; }
        if (EditorApplication.timeSinceStartup > deadline) Fail("Camera check exceeded 65 seconds; Game View may not have rendered.");
    }
    static void Check(bool ok,string message)
    {
        Record((ok?"PASS ":"FAIL ")+message);
        if (!ok) throw new InvalidOperationException(message);
    }
    static string F(float value) => value.ToString("F3",CultureInfo.InvariantCulture);
    static string V(Vector3 value) => "("+F(value.x)+","+F(value.y)+","+F(value.z)+")";
    static void Record(string line) => File.AppendAllText(Report,DateTime.UtcNow.ToString("O")+" "+line+"\n");
    static void Fail(string message)
    {
        finished=false; SessionState.SetBool(State+"completed",false); Record("FAIL "+message); Stop();
    }
    public static void Stop(bool exitPlay=true)
    {
        EditorApplication.update-=Tick; Running=false;
        if (tour && tour.IsPlaying) tour.Abort();
        if (flow && flow.MapOpen) { flow.CloseMapToMenu(); flow.SetMenu(false); }
        if (!finished) { SessionState.SetString(State+"status","Incomplete/failed; see camera-checks.txt"); Record("INCOMPLETE stopped before normal completion"); }
        if (flow && routine != null) flow.StopCoroutine(routine);
        routine = null;
        if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying=false;
    }
    static void BeforeReload() { if (Running) Fail("Interrupted by assembly reload"); }
    static void OnModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode && Running) Stop(false);
        if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(State+"awaitRestore",false))
            EditorApplication.delayCall += RecordRestoration;
    }
    static void RecordRestoration()
    {
        string prefix=SessionState.GetString(State+"prefix","");
        var scene=SceneManager.GetActiveScene();
        bool restored=!EditorApplication.isPlaying && scene.path=="Assets/_Demo/Scenes/OverworldBaseline.unity" && !scene.isDirty &&
            SceneManager.sceneCount==1 && !SessionState.GetBool(PrepareState+"prepared",false) &&
            !PlayerPrefs.HasKey(prefix+".primary") && !PlayerPrefs.HasKey(prefix+".backup");
        Record((restored?"PASS ":"FAIL ")+"Edit restoration and QA-slot cleanup by original Prepare harness; scene="+scene.path+
            "; clean="+!scene.isDirty+"; time="+Time.timeScale+". See runtime-checks.txt for real-save/main-scene hash evidence.");
        SessionState.SetBool(State+"awaitRestore",false);
        if (SessionState.GetBool(State+"completed",false) && restored)
            SessionState.SetString(State+"status","PASS camera/map API check and Edit restoration; visual review pending");
        else if (!restored) SessionState.SetString(State+"status","FAIL Edit restoration/QA cleanup; see camera-checks.txt");
    }
}
