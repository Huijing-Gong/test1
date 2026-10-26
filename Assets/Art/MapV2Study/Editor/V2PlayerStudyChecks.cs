using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit, isolated validation only. Never starts from a runtime initializer or writes game progress.
[InitializeOnLoad]
public static class V2PlayerStudyChecks
{
    const string ScenePath = "Assets/Art/MapV2Study/PlayerStudyValidation.unity";
    const string DefaultReport = "ToolReports/MapV2Plan/PlayerStudy/";
    const string TowerReport = "ToolReports/MapV2Plan/TowerArrival/Play/";
    const string KeyPrefix = "V2.PlayerStudy.";
    static string Report => SessionState.GetString(KeyPrefix + "report", DefaultReport);
    static IEnumerator routine;
    static Keyboard keyboard, previousKeyboard;
    static Mouse mouse, previousMouse;
    static InputSettings.BackgroundBehavior previousBackground;
    static bool previousRunInBackground;
    static bool previousKeyboardEnabled, previousMouseEnabled;
    static readonly FieldInfo OrbitYaw = typeof(ThirdPersonCamera).GetField("yaw", BindingFlags.Instance | BindingFlags.NonPublic);
    static PlayerMovement player;
    static ThirdPersonCamera orbit;
    static Camera camera;
    static MeshCollider terrain;
    static Dictionary<int, List<Vector3>> routes;
    static readonly List<string> log = new List<string>();
    static readonly List<string> samples = new List<string>();
    static Vector3? drive;
    static double resume, deadline, driveDeadline;
    static bool timedOut;
    static int lastFrame, captureIndex;
    static string activeCase;
    static double nextSample;
    static float minDistance, closeSeconds, maxCloseSeconds, currentCloseSeconds;
    static float climbSeconds, mantleSeconds;
    static float maxStairHeightError;
    // Bounded tower test driver, not a game navigation system. All movement still comes from W + mouse input.
    static List<Vector3> towerPath;
    static float[] towerDistances;
    static int towerSegment;
    static float towerProgress, routeDeviation, maxRouteDeviation, routeHeightError;
    static float signedYaw, absoluteYaw, previousYaw, lastProgress;
    static double stallDeadline;
    static string driveFailure;
    static bool steering;
    public static bool Running => routine != null;
    public static string Status => Running ? activeCase + " at " + player.transform.position : SessionState.GetString(KeyPrefix + "result", "Not running");

    static V2PlayerStudyChecks()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }

    public static void Prepare() => Prepare(false);
    public static void PrepareTowerOnly() => Prepare(true);
    static void Prepare(bool towerOnly)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Running || SessionState.GetBool(KeyPrefix+"prepared",false)) throw new InvalidOperationException("Requires clean Edit mode and no existing prepared override.");
        var original = SceneManager.GetActiveScene();
        if (original.isDirty || SceneManager.sceneCount != 1) throw new InvalidOperationException("Preserve the current unsaved/additive scene first.");
        var originalCamera = Camera.main;
        if (!originalCamera || !originalCamera.GetComponent<ThirdPersonCamera>()) throw new InvalidOperationException("Need the existing gameplay camera configuration.");
        SessionState.SetString(KeyPrefix + "report", towerOnly ? TowerReport : DefaultReport);
        Directory.CreateDirectory(Report);
        SessionState.SetString(KeyPrefix + "originalScene", original.path);
        SessionState.SetString(KeyPrefix + "startOverride", EditorSceneManager.playModeStartScene ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "");
        SessionState.SetInt(KeyPrefix + "cursorLock", (int)Cursor.lockState);
        SessionState.SetBool(KeyPrefix + "cursorVisible", Cursor.visible);
        SessionState.SetFloat(KeyPrefix + "timeScale", Time.timeScale);
        Scene stage = default;
        try
        {
            stage = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(stage);
            var study = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/MapV2Study/OverworldV2_VisualStudy.prefab"), stage);
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Demo/Prefabs/Characters/XBotPlayer.prefab"), stage);
            actor.name = "V2 Study - actual XBotPlayer prefab";
            foreach (var c in study.GetComponentsInChildren<MeshCollider>()) if (c.name == "VISUAL_STUDY_Terrain_NoGameplayMigration") terrain = c;
            actor.transform.position = new Vector3(0, Ground(new Vector3(0, 0, -75)) + .08f, -75);
            var camObject = Object.Instantiate(originalCamera.gameObject);
            SceneManager.MoveGameObjectToScene(camObject, stage);
            camObject.name = "V2 Study - copied gameplay camera";
            var so = new SerializedObject(camObject.GetComponent<ThirdPersonCamera>());
            so.FindProperty("target").objectReferenceValue = actor.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            camObject.GetComponent<ThirdPersonCamera>().ResetOrbit(0, 14);
            var sky = camObject.GetComponent<Skybox>(); if (!sky) sky = camObject.AddComponent<Skybox>();
            sky.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/MapV2Study/StudySky.mat");
            camObject.GetComponent<Camera>().clearFlags = CameraClearFlags.Skybox;
            var lightObject = new GameObject("V2 Study daylight");
            SceneManager.MoveGameObjectToScene(lightObject, stage);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1; light.color = new Color(1, .97f, .9f); light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(43, -37, 0);
            RenderSettings.sun = light; RenderSettings.skybox = sky.material;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.37f, .43f, .49f);
            RenderSettings.ambientEquatorColor = new Color(.29f, .31f, .3f);
            RenderSettings.ambientGroundColor = new Color(.17f, .18f, .16f);
            RenderSettings.fog = false;
            if (Object.FindFirstObjectByType<BaselineFlow>() && Object.FindFirstObjectByType<BaselineFlow>().gameObject.scene == stage) throw new Exception("Validation scene must not contain Flow.");
            if (!EditorSceneManager.SaveScene(stage, ScenePath)) throw new Exception("Could not save isolated scene.");
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            if (stage.IsValid()) EditorSceneManager.CloseScene(stage, true);
            terrain = null;
        }
        AssetDatabase.ImportAsset(ScenePath);
        var startScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (!startScene) throw new Exception("Missing validation SceneAsset; will not enter Play.");
        EditorSceneManager.playModeStartScene = startScene;
        SessionState.SetBool(KeyPrefix + "prepared", true);
        SessionState.SetString(KeyPrefix + "result", "Prepared isolated scene; not yet run");
        File.WriteAllText(Report + "preparation.txt", "Original=" + original.path + " dirty=" + original.isDirty + "\nPlayer and camera are existing prefab/configuration; no Flow/World/Session/Vitals/WaterVolume.\nPlay override=" + ScenePath);
    }

    public static void Begin() => Begin(false);
    public static void BeginTowerOnly() => Begin(true);
    static void Begin(bool towerOnly)
    {
        if (!EditorApplication.isPlaying || Running || SceneManager.GetActiveScene().path != ScenePath || SceneManager.sceneCount!=1) throw new InvalidOperationException("Enter only the prepared isolated scene.");
        if (Object.FindFirstObjectByType<BaselineFlow>() || Object.FindFirstObjectByType<GameSession>() || Object.FindFirstObjectByType<SideWorldTravel>() || Object.FindFirstObjectByType<PlayerVitals>() || Object.FindFirstObjectByType<WaterVolume>()) throw new InvalidOperationException("Unexpected game session/water/vitals; abort isolated validation.");
        player = Object.FindFirstObjectByType<PlayerMovement>(); camera = Camera.main; orbit = camera.GetComponent<ThirdPersonCamera>();
        foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)) if (c.name == "VISUAL_STUDY_Terrain_NoGameplayMigration") terrain = c;
        if (!player || !terrain || !orbit) throw new Exception("Incomplete study fixture.");
        if (OrbitYaw == null) throw new Exception("Cannot observe existing camera orbit yaw; abort rather than change its behavior.");
        SessionState.SetString(KeyPrefix + "report", towerOnly ? TowerReport : DefaultReport);
        Directory.CreateDirectory(Report);
        routes = new Dictionary<int, List<Vector3>>();
        foreach (var line in towerOnly ? Array.Empty<string>() : File.ReadAllLines("ToolReports/MapV2Plan/VisualStudy/route-centrelines.csv"))
        {
            var a = line.Split(','); int id = int.Parse(a[0]); if (!routes.ContainsKey(id)) routes[id] = new List<Vector3>();
            routes[id].Add(new Vector3(float.Parse(a[2], CultureInfo.InvariantCulture), float.Parse(a[3], CultureInfo.InvariantCulture), float.Parse(a[4], CultureInfo.InvariantCulture)));
        }
        previousKeyboard = Keyboard.current; previousMouse = Mouse.current;
        previousKeyboardEnabled=previousKeyboard!=null&&previousKeyboard.enabled;previousMouseEnabled=previousMouse!=null&&previousMouse.enabled;
        previousBackground = InputSystem.settings.backgroundBehavior; previousRunInBackground = Application.runInBackground;
        SessionState.SetInt(KeyPrefix + "background", (int)previousBackground);
        SessionState.SetBool(KeyPrefix + "runInBackground", previousRunInBackground);
        SessionState.SetBool(KeyPrefix + "keyboardEnabled", previousKeyboardEnabled);
        SessionState.SetBool(KeyPrefix + "mouseEnabled", previousMouseEnabled);
        Application.runInBackground = true; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("V2PlayerStudyKeyboard"); mouse = InputSystem.AddDevice<Mouse>("V2PlayerStudyMouse");
        // Unity input devices only: OS keyboard/mouse remain available. Restore their original enabled state on stop.
        if(previousKeyboardEnabled)InputSystem.DisableDevice(previousKeyboard);
        if(previousMouseEnabled)InputSystem.DisableDevice(previousMouse);
        keyboard.MakeCurrent(); mouse.MakeCurrent();
        log.Clear(); samples.Clear(); samples.Add("case,time,x,y,z,cameraDistance,climbing,mantling,grounded,orbitYaw,cameraYaw,routeProgress,routeDeviation,routeHeightError,signedYaw,absoluteYaw");
        Record("START real Play Mode, actual PlayerMovement/Animator/ThirdPersonCamera, injected keyboard/mouse; no Vitals/Flow/save/water/quests.");
        Record("Physical keyboard/mouse temporarily disabled inside Unity InputSystem only; previous enabled states restored on stop. Steering reads private orbit yaw but changes it only through queued Mouse.delta, not ResetOrbit per frame.");
        Record("camera FOV=" + camera.fieldOfView + " near=" + camera.nearClipPlane + " aspect=" + camera.aspect + " targetHeight=1.05 distance=6; runtime LOD, no forced LOD0");
        Record("scope=" + (towerOnly ? "tower stair foot to terrace door, then controlled placement at door and reverse" : "original land routes and tower") + "; prior keyboardEnabled=" + previousKeyboardEnabled + "; prior mouseEnabled=" + previousMouseEnabled);
        routine = towerOnly ? RunTowerOnly() : Run(); resume = 0; deadline = EditorApplication.timeSinceStartup + 480; lastFrame = -1; captureIndex = 0;
        towerPath = null; nextSample = 0; activeCase = "settling";
        EditorApplication.update += Tick;
    }

    static float Ground(Vector3 p)
    {
        RaycastHit hit; if (!terrain.Raycast(new Ray(new Vector3(p.x, 80, p.z), Vector3.down), out hit, 160)) throw new Exception("No terrain at " + p);
        return hit.point.y;
    }
    static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    static void Record(string text) { log.Add(text); File.WriteAllLines(Report + "runtime-checks.txt", log); }
    static void Capture(string name)
    {
        ScreenCapture.CaptureScreenshot(Report + name + ".png");
        Record("CAPTURE " + name + " source=Game View; player=" + player.transform.position + " camera=" + camera.transform.position + " rotation=" + camera.transform.eulerAngles);
    }
    static float Heading(Vector3 a, Vector3 b) => Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
    static void Place(Vector3 p, float yaw, float pitch = 14)
    {
        drive = null; Keys(); player.Relocate(p, Quaternion.Euler(0, yaw, 0)); orbit.ResetOrbit(yaw, pitch); Physics.SyncTransforms();
        previousYaw = (float)OrbitYaw.GetValue(orbit); signedYaw = absoluteYaw = 0;
    }
    static IEnumerator Walk(string label, List<Vector3> points, bool relocate, bool usePointHeight = false)
    {
        activeCase = label; minDistance = 100; closeSeconds = currentCloseSeconds = maxCloseSeconds = climbSeconds = mantleSeconds = maxStairHeightError = 0;
        if (relocate) Place(new Vector3(points[0].x, (usePointHeight ? points[0].y : Ground(points[0])) + .09f, points[0].z), Heading(points[0], points[1]));
        yield return .5f;
        bool reached = true; int index = 1;
        for (; index < points.Count; index++)
        {
            drive = points[index]; timedOut = false;
            float distance = Vector2.Distance(new Vector2(player.transform.position.x, player.transform.position.z), new Vector2(drive.Value.x, drive.Value.z));
            driveDeadline = EditorApplication.timeSinceStartup + Mathf.Max(5, distance / 2.25f * 2 + 3);
            while (drive.HasValue) yield return .02f;
            if (timedOut) { reached = false; break; }
        }
        Keys(); yield return .35f;
        float expectedY=usePointHeight?points[points.Count-1].y:Ground(points[points.Count-1]);
        float heightError=Mathf.Abs(player.transform.position.y-expectedY);
        bool walkOnly=reached&&heightError<.6f&&player.IsStableGrounded&&climbSeconds<.001f&&mantleSeconds<.001f&&maxStairHeightError<1;
        Record((walkOnly ? "PASS " : "FAIL ") + label + " reached=" + reached + " walkOnly="+walkOnly+" heightError="+heightError.ToString("F3")+" grounded="+player.IsStableGrounded+" maxStairHeightError="+maxStairHeightError.ToString("F3")+" node=" + index + "/" + (points.Count-1) + " final=" + player.transform.position + " minCamera=" + minDistance.ToString("F2") + " below3Seconds=" + closeSeconds.ToString("F2") + " longestBelow3=" + maxCloseSeconds.ToString("F2") + " climbSeconds=" + climbSeconds.ToString("F2") + " mantleSeconds=" + mantleSeconds.ToString("F2"));
        Capture((++captureIndex).ToString("D2") + "-" + label);
        File.WriteAllLines(Report + "movement-camera-samples.csv", samples);
        yield return .25f;
    }
    static List<Vector3> TowerArrivalPath()
    {
        var points = new List<Vector3>();
        for (int i = 0; i <= 92; i++)
        {
            float a = (-135 + i * 360f / 92) * Mathf.Deg2Rad;
            points.Add(new Vector3(61 + Mathf.Sin(a) * 7.5f, 18 + i * 20f / 92, 61 + Mathf.Cos(a) * 7.5f));
        }
        // Continue around the level arrival terrace, then approach the door without crossing the tower body.
        for (int i = 1; i <= 12; i++)
        {
            float a = (225 + i * 45f / 12) * Mathf.Deg2Rad;
            points.Add(new Vector3(61 + Mathf.Sin(a) * 7.5f, 38, 61 + Mathf.Cos(a) * 7.5f));
        }
        points.Add(new Vector3(54.6f, 38, 61));
        return points;
    }
    static IEnumerator WalkTowerArrival(string label, List<Vector3> points)
    {
        activeCase = label;
        minDistance = 100; closeSeconds = currentCloseSeconds = maxCloseSeconds = climbSeconds = mantleSeconds = maxStairHeightError = 0;
        routeDeviation = maxRouteDeviation = routeHeightError = towerProgress = lastProgress = 0;
        driveFailure = ""; timedOut = false;
        Place(points[0] + Vector3.up * .09f, Heading(points[0], points[1]));
        yield return .7f;
        Capture((++captureIndex).ToString("D2") + "-" + label + "-start");
        towerDistances = new float[points.Count];
        for (int i = 1; i < points.Count; i++) towerDistances[i] = towerDistances[i - 1] + HorizontalDistance(points[i - 1], points[i]);
        towerSegment = 0; towerPath = points;
        driveDeadline = EditorApplication.timeSinceStartup + Mathf.Max(60, towerDistances[points.Count - 1] / 2.25f * 3 + 10);
        stallDeadline = EditorApplication.timeSinceStartup + 10;
        Record("METHOD " + label + " controlled start; W and mouse delta only; 1.4 m look-ahead; local segment projection advances passed points; no jumps/climbs commanded; XZ deviation limit=1.15, height limit=1.0, final XZ<=0.35 and Y<0.6.");
        while (towerPath != null) yield return .02f;
        Keys(); InputSystem.QueueStateEvent(mouse, new MouseState()); yield return .4f;
        var end = points[points.Count - 1];
        float finalXZ = HorizontalDistance(player.transform.position, end);
        float heightError = Mathf.Abs(player.transform.position.y - end.y);
        bool passed = !timedOut && string.IsNullOrEmpty(driveFailure) && finalXZ <= .35f && heightError < .6f && player.IsStableGrounded && maxRouteDeviation <= 1.15f && maxStairHeightError < 1 && climbSeconds < .001f && mantleSeconds < .001f;
        Record((passed ? "PASS " : "FAIL ") + label + " reason=" + driveFailure + " final=" + player.transform.position + " finalXZ=" + finalXZ.ToString("F3") + " heightError=" + heightError.ToString("F3") + " grounded=" + player.IsStableGrounded + " progress=" + towerProgress.ToString("F2") + "/" + towerDistances[points.Count - 1].ToString("F2") + " maxRouteDeviation=" + maxRouteDeviation.ToString("F3") + " maxRouteHeightError=" + maxStairHeightError.ToString("F3") + " signedYaw=" + signedYaw.ToString("F1") + " absoluteYaw=" + absoluteYaw.ToString("F1") + " minCamera=" + minDistance.ToString("F2") + " below3Seconds=" + closeSeconds.ToString("F2") + " longestBelow3=" + maxCloseSeconds.ToString("F2") + " climbSeconds=" + climbSeconds.ToString("F2") + " mantleSeconds=" + mantleSeconds.ToString("F2"));
        Capture((++captureIndex).ToString("D2") + "-" + label + "-end");
        File.WriteAllLines(Report + "movement-camera-samples.csv", samples);
        yield return .3f;
    }
    static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    static void EndTowerDrive(string failure)
    {
        driveFailure = failure; timedOut = !string.IsNullOrEmpty(failure); towerPath = null; drive = null;
        Keys(); InputSystem.QueueStateEvent(mouse, new MouseState());
    }
    static void AdvanceTowerDriver()
    {
        var p = player.transform.position;
        float best = float.PositiveInfinity, bestT = 0;
        int bestSegment = towerSegment;
        // Limit the nearest-point search to nearby segments: a spiral repeats XZ positions at different heights.
        for (int i = Mathf.Max(0, towerSegment - 2); i <= Mathf.Min(towerPath.Count - 2, towerSegment + 8); i++)
        {
            var a = towerPath[i]; var b = towerPath[i + 1];
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            float t = Mathf.Clamp01(Vector2.Dot(new Vector2(p.x - a.x, p.z - a.z), ab) / Mathf.Max(.0001f, ab.sqrMagnitude));
            float d = HorizontalDistance(p, Vector3.Lerp(a, b, t));
            if (d < best) { best = d; bestT = t; bestSegment = i; }
        }
        var nearest = Vector3.Lerp(towerPath[bestSegment], towerPath[bestSegment + 1], bestT);
        routeDeviation = best; routeHeightError = Mathf.Abs(p.y - nearest.y);
        maxRouteDeviation = Mathf.Max(maxRouteDeviation, routeDeviation);
        maxStairHeightError = Mathf.Max(maxStairHeightError, routeHeightError);
        if (routeDeviation > 1.15f) { EndTowerDrive("route deviation exceeded 1.15 m"); return; }
        if (routeHeightError >= 1) { EndTowerDrive("route height error reached 1 m"); return; }
        float projectedProgress = Mathf.Lerp(towerDistances[bestSegment], towerDistances[bestSegment + 1], bestT);
        if (projectedProgress > towerProgress) { towerProgress = projectedProgress; towerSegment = bestSegment; }
        if (towerProgress > lastProgress + .1f) { lastProgress = towerProgress; stallDeadline = EditorApplication.timeSinceStartup + 10; }
        float length = towerDistances[towerDistances.Length - 1];
        if (length - towerProgress <= .6f && HorizontalDistance(p, towerPath[towerPath.Count - 1]) <= .3f) { EndTowerDrive(""); return; }
        if (EditorApplication.timeSinceStartup > driveDeadline) { EndTowerDrive("bounded route deadline"); return; }
        if (EditorApplication.timeSinceStartup > stallDeadline) { EndTowerDrive("no 0.1 m route progress for 10 seconds"); return; }
        float lookAhead = Mathf.Min(length, towerProgress + 1.4f);
        int targetSegment = towerSegment;
        while (targetSegment < towerPath.Count - 2 && towerDistances[targetSegment + 1] < lookAhead) targetSegment++;
        drive = Vector3.Lerp(towerPath[targetSegment], towerPath[targetSegment + 1], Mathf.InverseLerp(towerDistances[targetSegment], towerDistances[targetSegment + 1], lookAhead));
    }
    static IEnumerator RunTowerOnly()
    {
        yield return .7f;
        var path = TowerArrivalPath();
        var up = WalkTowerArrival("tower-arrival-up", path); while (up.MoveNext()) yield return up.Current;
        path = new List<Vector3>(path); path.Reverse();
        var down = WalkTowerArrival("tower-arrival-down", path); while (down.MoveNext()) yield return down.Current;
        Record("FINISHED tower-only actual input checks; each direction has a controlled starting placement. No portal/ending/water/vitals/save regression, and no claim of manual WASD play or camera comfort.");
    }
    static IEnumerator Run()
    {
        yield return .7f;
        Capture("00-spawn"); yield return .3f;
        var paths = new[] { 0, 1, 7, 5, 9 };
        foreach (int id in paths)
        {
            var walk = Walk("route" + id, routes[id], true); while (walk.MoveNext()) yield return walk.Current;
        }
        var stairs = new List<Vector3>();
        for (int i=0; i<=92; i++) { float a=(-135+i*360f/92)*Mathf.Deg2Rad; stairs.Add(new Vector3(61+Mathf.Sin(a)*7.5f,18+i*20f/92,61+Mathf.Cos(a)*7.5f)); }
        var up = Walk("tower-up", stairs, true, true); while(up.MoveNext()) yield return up.Current;
        stairs.Reverse(); var down = Walk("tower-down", stairs, true, true); while(down.MoveNext()) yield return down.Current;
        // Stationary controlled orbit exposes close vegetation/columns without repeatedly clearing smoothing.
        activeCase = "forest-orbit"; Place(new Vector3(46,Ground(new Vector3(46,0,-15))+.1f,-15), 27); yield return .8f;
        steering = true; double until=EditorApplication.timeSinceStartup+4;
        while(EditorApplication.timeSinceStartup<until) yield return .03f;
        steering = false; InputSystem.QueueStateEvent(mouse,new MouseState()); Keys(); yield return .4f; Capture("08-forest-orbit"); yield return .25f;
        Record("FINISHED isolated checks. Not a water/vitals/save/gameplay regression.");
    }
    static void Tick()
    {
        try { TickSafe(); }
        catch(Exception e) { Record("FAIL input/sampling exception "+e); Stop(true); }
    }
    static void TickSafe()
    {
        if (!EditorApplication.isPlaying || !player) { Stop(false); return; }
        if (EditorApplication.timeSinceStartup > deadline) { Record("FAIL global 480-second safety deadline"); Stop(true); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.frameCount != lastFrame)
        {
            lastFrame = Time.frameCount;
            if(Keyboard.current!=keyboard||Mouse.current!=mouse){Record("FAIL input device changed during automated check");Stop(true);return;}
            if (towerPath != null) AdvanceTowerDriver();
            if (drive.HasValue)
            {
                var d=drive.Value-player.transform.position;
                if(towerPath == null && activeCase.StartsWith("tower-"))maxStairHeightError=Mathf.Max(maxStairHeightError,Mathf.Abs(d.y));
                d.y=0;
                if (towerPath == null && d.magnitude<.28f) { drive=null; Keys(); }
                else if (EditorApplication.timeSinceStartup>driveDeadline) { timedOut=true; drive=null; Keys(); }
                else
                {
                    float desired=Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg;
                    float orbitYaw=(float)OrbitYaw.GetValue(orbit);
                    float correction=Mathf.Clamp(Mathf.DeltaAngle(orbitYaw,desired)*.55f,-2.5f,2.5f);
                    Cursor.lockState=CursorLockMode.Locked; Cursor.visible=false;
                    InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(correction/.12f,0)});
                    Keys(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y,desired))<35 ? new[]{Key.W} : new Key[0]);
                }
            }
            else if (steering) InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(5,0)});
            float currentYaw = (float)OrbitYaw.GetValue(orbit);
            float yawDelta = Mathf.DeltaAngle(previousYaw, currentYaw);
            signedYaw += yawDelta; absoluteYaw += Mathf.Abs(yawDelta); previousYaw = currentYaw;
            float distance=Vector3.Distance(camera.transform.position,player.transform.position+Vector3.up*1.05f);
            minDistance=Mathf.Min(minDistance,distance);
            if (distance<3) { closeSeconds+=Time.deltaTime; currentCloseSeconds+=Time.deltaTime; maxCloseSeconds=Mathf.Max(maxCloseSeconds,currentCloseSeconds); } else currentCloseSeconds=0;
            if (player.ClimbingActive) climbSeconds+=Time.deltaTime;
            if (player.MantlingActive) mantleSeconds+=Time.deltaTime;
            if (EditorApplication.timeSinceStartup>=nextSample)
            {
                nextSample=EditorApplication.timeSinceStartup+.1;var p=player.transform.position;
                samples.Add(string.Format(CultureInfo.InvariantCulture,"{0},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6},{7},{8},{9:F3},{10:F3},{11:F3},{12:F3},{13:F3},{14:F3},{15:F3}",activeCase,Time.time,p.x,p.y,p.z,distance,player.ClimbingActive,player.MantlingActive,player.IsStableGrounded,currentYaw,camera.transform.eulerAngles.y,towerProgress,routeDeviation,routeHeightError,signedYaw,absoluteYaw));
            }
        }
        if (EditorApplication.timeSinceStartup<resume) return;
        try
        {
            if (!routine.MoveNext()) { Stop(true); return; }
            resume=EditorApplication.timeSinceStartup+(routine.Current is float delay ? delay : .02);
        }
        catch(Exception e) { Record("FAIL exception "+e); Stop(true); }
    }
    public static void Stop(bool exitPlay = true)
    {
        EditorApplication.update-=Tick; routine=null; drive=null; towerPath=null; steering=false;
        RestoreInput();
        if(samples.Count>0) File.WriteAllLines(Report+"movement-camera-samples.csv",samples);
        SessionState.SetString(KeyPrefix+"result","Stopped; results in "+Report+"runtime-checks.txt");
        if(exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying=false;
    }
    public static void CancelPrepared()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play first.");
        OnModeChanged(PlayModeStateChange.EnteredEditMode);
    }
    static void BeforeReload()
    {
        if(Running){Record("INCOMPLETE interrupted by assembly reload");Stop(true);}else RestoreInput();
    }
    static void RestoreInput()
    {
        if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if(mouse!=null && mouse.added) InputSystem.RemoveDevice(mouse);
        if(keyboard!=null || mouse!=null) { InputSystem.settings.backgroundBehavior=previousBackground;Application.runInBackground=previousRunInBackground; }
        keyboard=null;mouse=null;
        if(previousKeyboard!=null&&previousKeyboard.added&&previousKeyboardEnabled!=previousKeyboard.enabled) { if(previousKeyboardEnabled)InputSystem.EnableDevice(previousKeyboard);else InputSystem.DisableDevice(previousKeyboard); }
        if(previousMouse!=null&&previousMouse.added&&previousMouseEnabled!=previousMouse.enabled) { if(previousMouseEnabled)InputSystem.EnableDevice(previousMouse);else InputSystem.DisableDevice(previousMouse); }
        if(previousKeyboard!=null && previousKeyboard.added)previousKeyboard.MakeCurrent();
        if(previousMouse!=null && previousMouse.added)previousMouse.MakeCurrent();
        if(SessionState.GetBool(KeyPrefix+"prepared",false)){
            Cursor.lockState=(CursorLockMode)SessionState.GetInt(KeyPrefix+"cursorLock",0);Cursor.visible=SessionState.GetBool(KeyPrefix+"cursorVisible",true);
            Time.timeScale=SessionState.GetFloat(KeyPrefix+"timeScale",1);
        }
    }
    static void OnModeChanged(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.ExitingPlayMode && Running) Stop(false);
        if(state!=PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(KeyPrefix+"prepared",false))return;
        RestoreInput();
        string old=SessionState.GetString(KeyPrefix+"startOverride","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(old)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(old);
        SessionState.SetBool(KeyPrefix+"prepared",false);
        var active=SceneManager.GetActiveScene();
        File.WriteAllText(Report+"restoration.txt","scene="+active.path+" dirty="+active.isDirty+" play="+EditorApplication.isPlaying+" time="+Time.timeScale+" count="+SceneManager.sceneCount+" startOverride="+(EditorSceneManager.playModeStartScene?AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene):"none")+"\nkeyboard="+(Keyboard.current!=null?Keyboard.current.name:"none")+" mouse="+(Mouse.current!=null?Mouse.current.name:"none")+" background="+InputSystem.settings.backgroundBehavior+" runInBackground="+Application.runInBackground+"\noriginal keyboardEnabled="+SessionState.GetBool(KeyPrefix+"keyboardEnabled",false)+" mouseEnabled="+SessionState.GetBool(KeyPrefix+"mouseEnabled",false)+" restored keyboardEnabled="+(Keyboard.current!=null&&Keyboard.current.enabled)+" mouseEnabled="+(Mouse.current!=null&&Mouse.current.enabled));
    }
}
