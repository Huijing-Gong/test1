using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit, bounded QA commands. Opens BattleScene without saving authored
// scenes. Play uses the original public practice/menu APIs, never a host save.
[InitializeOnLoad]
public static class EclipseAtmosphereChecks
{
    const string State = "QA.EclipseAtmosphere.";
    const string ScenePath = "Assets/Scenes/BattleScene.unity";
    const string Output = "LocalDocs/SideA/EclipseCathedral/Atmosphere_v003";
    static readonly Stack<IEnumerator> Routine = new Stack<IEnumerator>();
    static bool running, stopping, previousReduced, previousBackground;
    static float previousTimeScale;
    static int assertions, lastFrame;
    static double deadline;
    static string runtimeError;
    static BattleArenaStages stages;
    static BattleManager manager;
    static BattleEclipseAtmosphere effect;

    [Serializable] sealed class SavedScene { public string path; public bool isLoaded, isActive; }
    [Serializable] sealed class SceneSnapshot { public SavedScene[] scenes; }

    static EclipseAtmosphereChecks()
    {
        EditorApplication.playModeStateChanged += Changed;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }

    static void RequireCleanEditor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before Eclipse atmosphere checks.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save open scene edits first; these checks never discard or save them.");
    }

    static void Check(bool condition, string label)
    {
        string line = (condition ? "PASS " : "FAIL ") + label;
        Debug.Log("ECLIPSE_ATMOSPHERE_CHECK " + line);
        if (!condition) throw new InvalidOperationException(label);
        assertions++;
    }

    static BattleEclipseAtmosphere FindEffect(BattleArenaStages owner)
        => owner.GetComponentsInChildren<BattleEclipseAtmosphere>(true).SingleOrDefault();

    static Renderer[] PlatformRenderers(BattleEclipseAtmosphere atmosphere)
        => atmosphere.GetComponentsInChildren<Renderer>(true).Where(r =>
            r.sharedMaterials.Any(m => m && m.shader && m.shader.name == "FoldedRealm/EclipsePlatform")).ToArray();

    static void CheckState(BattleArenaStages owner, int index, int collisionCount,
        BattleEclipseAtmosphere cached, bool originalDepth)
    {
        Check(owner.ActiveIndex == index, "Stage index " + index);
        Check(owner.GetComponentsInChildren<Collider>(true).Length == collisionCount,
            "Atmosphere adds no colliders on stage " + index);
        Check(owner.upperPlatforms[0].gameObject.activeInHierarchy &&
            owner.upperPlatforms[2].gameObject.activeInHierarchy &&
            owner.upperPlatforms[1].gameObject.activeSelf == (index != 2),
            "Two side collision platforms / original centre visibility on stage " + index);
        var found = FindEffect(owner);
        if (!cached) { Check(!found, "No Eclipse allocation before first stage 2"); return; }
        Check(found == cached && owner.GetComponentsInChildren<BattleEclipseAtmosphere>(true).Length == 1,
            "Stage switches reuse exactly one atmosphere component");
        bool active = index == 2;
        Check(cached.gameObject.activeInHierarchy == active && cached.Ready,
            "Cached atmosphere activation matches stage " + index);
        Check(cached.MistRenderers != null && cached.MistRenderers.Length == 3 && cached.FogLayerCount == 3,
            "Exactly three reusable mist layers");
        Check(cached.MistRenderers.All(r => r && r.gameObject.activeInHierarchy == active),
            "Mist visibility matches stage " + index);
        Check(cached.CloudRenderer && cached.CloudRenderer.transform.parent == owner.paintedBackdrop.transform &&
            cached.CloudRenderer.gameObject.activeInHierarchy == active,
            "Backdrop-hosted cloud visibility matches stage " + index);
        Check(cached.EmberCount > 0 && cached.EmberCount <= 18, "Bounded procedural ember budget");
        Check(cached.GetComponentsInChildren<Collider>(true).Length == 0 &&
            !cached.CloudRenderer.GetComponent<Collider>(), "Visual-only fog, clouds and embers");
        var data = owner.battleCamera.GetComponent<UniversalAdditionalCameraData>();
        Check(data && data.requiresDepthTexture == (active || originalDepth),
            "Soft-depth camera request restored on stage " + index);
        if (!active) return;
        var renderers = cached.GetComponentsInChildren<Renderer>(true).Concat(new[] { cached.CloudRenderer });
        foreach (var r in renderers)
        {
            if (!r.name.StartsWith("Eclipse Mist", StringComparison.Ordinal) &&
                r.name != "Eclipse Embers" && r != cached.CloudRenderer) continue;
            Check(r.shadowCastingMode == ShadowCastingMode.Off && !r.receiveShadows,
                "Atmosphere renderer never casts/receives shadows: " + r.name);
            Check(r.sharedMaterials.All(m => m && m.shader && !ShaderUtil.ShaderHasError(m.shader)),
                "Atmosphere material shader compiles: " + r.name);
        }
        foreach (var r in PlatformRenderers(cached))
        {
            var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block);
            Check(Mathf.Approximately(block.GetFloat("_EclipseEnabled"), 1) &&
                Mathf.Approximately(block.GetFloat("_EclipseMotion"), BattlePresentationSettings.ReducedMotion ? 0 : 1),
                "Platform vertex motion is scoped through renderer properties: " + r.name);
        }
    }

    [MenuItem("Tools/Side A/Validate Eclipse Atmosphere (Static Render)")]
    public static void Validate()
    {
        RequireCleanEditor();
        var originalScenes = EditorSceneManager.GetSceneManagerSetup();
        bool oldReduced = BattlePresentationSettings.ReducedMotion;
        assertions = 0;
        Directory.CreateDirectory(Output);
        try
        {
            BattlePresentationSettings.ReducedMotion = false;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var owner = Object.FindFirstObjectByType<BattleArenaStages>();
            Check(owner && owner.battleCamera && owner.paintedBackdrop, "Original arena, camera and backdrop exist");
            var data = owner.battleCamera.GetComponent<Camera>().GetUniversalAdditionalCameraData();
            Check(data, "Original camera supports an explicit URP depth request");
            bool originalDepth = data.requiresDepthTexture;
            int collisionCount = owner.GetComponentsInChildren<Collider>(true).Length;
            BattleEclipseAtmosphere cached = null;
            foreach (int index in new[] { 0, 1, 2, 0, 2, 1, 2 })
            {
                Check(owner.Apply(index), "Apply existing public stage " + index);
                if (index == 2 && !cached) cached = FindEffect(owner);
                CheckState(owner, index, collisionCount, cached, originalDepth);
            }
            CheckMotionPayload(cached);
            var shader = Shader.Find("FoldedRealm/EclipseAtmosphere");
            Check(shader && !ShaderUtil.ShaderHasError(shader), "Atmosphere shader imports without errors");
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            string source = File.ReadAllText(shaderPath);
            Check(source.Contains("ZWrite Off") && source.Contains("ZTest LEqual") && source.Contains("Cull Off"),
                "Atmosphere uses depth-tested, non-depth-writing two-sided transparency");
            Check(!Application.isPlaying, "Preview is explicitly Edit mode");
            RenderPreviews(owner, cached);
            Check(!ShaderUtil.ShaderHasError(shader), "Rendered atmosphere has no shader compiler errors");
            var cloud = cached.CloudRenderer;
            var generatedRenderers = cached.MistRenderers.Concat(new[] { cloud,
                cached.GetComponentsInChildren<Renderer>().Single(r => r.name == "Eclipse Embers") }).ToArray();
            var generatedMaterials = generatedRenderers.Select(r => r.sharedMaterial).Distinct().ToArray();
            var generatedMeshes = generatedRenderers.Select(r => r.GetComponent<MeshFilter>().sharedMesh).Distinct().ToArray();
            Object.DestroyImmediate(cached.gameObject);
            Check(!cloud && generatedMaterials.All(m => !m) && generatedMeshes.All(m => !m),
                "Destroy releases backdrop cloud and all owned procedural materials/meshes");
            Check(data.requiresDepthTexture == originalDepth, "Destroy restores original camera depth request");
            Check(owner.Apply(2) && FindEffect(owner) && FindEffect(owner).Ready,
                "Public stage API can rebuild after owned art cleanup");
            File.WriteAllText(Path.Combine(Output, "static-validation.txt"),
                "PASS " + assertions + " static checks\nUnity=" + Application.unityVersion +
                "\nActual Unity camera renders at fixed atmosphere clocks 0 and 8 seconds, plus material detail.\n" +
                "Scene changes were unsaved and original scene setup was restored.\n");
            Debug.Log("ECLIPSE_ATMOSPHERE_STATIC_PASS " + assertions);
        }
        finally
        {
            BattlePresentationSettings.ReducedMotion = oldReduced;
            // Restore via opening original assets; no validation changes persist.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (originalScenes.Length > 0 && originalScenes.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(originalScenes);
        }
    }

    static void CheckMotionPayload(BattleEclipseAtmosphere atmosphere)
    {
        int staticVertices = 0, cloth = 0, pinnedCloth = 0, chips = 0, deckVertices = 0;
        bool bounded = true, deckStatic = true;
        foreach (var filter in atmosphere.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<Renderer>();
            if (!renderer || !renderer.sharedMaterials.Any(m => m && m.shader && m.shader.name == "FoldedRealm/EclipsePlatform")) continue;
            var mesh = filter.sharedMesh;
            var uv = new List<Vector4>(); mesh.GetUVs(1, uv);
            Check(uv.Count == mesh.vertexCount, "Motion mask is vertex-aligned: " + mesh.name);
            foreach (var v in uv)
            {
                bounded &= v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1 &&
                    v.z >= 0 && v.z < Mathf.PI * 2 + .001f && !(v.x > 0 && v.y > 0);
                if (v.x == 0 && v.y == 0) staticVertices++;
                if (v.y > 0) chips++;
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var material = renderer.sharedMaterials[sub];
                if (!material) continue;
                bool banner = material.name.StartsWith("Eclipse_Banner", StringComparison.Ordinal);
                bool deck = material.name.StartsWith("Eclipse_Ivory", StringComparison.Ordinal);
                if (!banner && !deck) continue;
                foreach (int index in mesh.GetIndices(sub))
                {
                    if (banner) { if (uv[index].x > 0) cloth++; else pinnedCloth++; }
                    if (deck) { deckVertices++; deckStatic &= uv[index].x == 0 && uv[index].y == 0; }
                }
            }
        }
        Check(bounded, "UV2 motion weights bounded, phases finite and cloth/chips isolated");
        Check(staticVertices > 0 && cloth > 0 && pinnedCloth > 0 && chips > 0,
            "Static structure, moving cloth, pinned banner tops and isolated chips all represented");
        Check(deckVertices > 0 && deckStatic, "Walkable ivory deck vertices stay fixed");
    }

    static void RenderPreviews(BattleArenaStages owner, BattleEclipseAtmosphere atmosphere)
    {
        var camera = owner.battleCamera.GetComponent<Camera>();
        camera.transform.position = new Vector3(-2, 5, -34);
        camera.transform.rotation = Quaternion.Euler(8, 0, 0);
        camera.fieldOfView = 35; camera.aspect = 16f / 9;
        var backdrop = camera.GetComponent<BattlePaintedBackdrop>();
        if (backdrop) backdrop.FitNow();
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.gameObject.SetActive(false);
        atmosphere.PreviewAt(0);
        var first = Capture(camera, "01_Overview_0s.png");
        atmosphere.PreviewAt(8);
        var second = Capture(camera, "02_Overview_8s.png");
        long difference = 0;
        for (int i = 0; i < first.Length; i++) difference += Math.Abs(first[i] - second[i]);
        Check(difference > first.Length / 20, "Actual rendered pixels change between 0 and 8 seconds");
        File.WriteAllText(Path.Combine(Output, "render-difference.txt"),
            "RGB absolute difference total=" + difference + " bytes=" + first.Length +
            "\nFixed camera/lighting/geometry; atmospheric and masked platform motion clocks differ.\n");
        foreach(var renderer in atmosphere.MistRenderers)renderer.enabled=false;
        var noMist=Capture(camera,null);
        foreach(var renderer in atmosphere.MistRenderers)renderer.enabled=true;
        long mistDifference=0;
        for(int i=0;i<second.Length;i++)mistDifference+=Math.Abs(second[i]-noMist[i]);
        File.AppendAllText(Path.Combine(Output,"render-difference.txt"),"Mist on/off absolute RGB difference="+mistDifference+"\n");
        Check(mistDifference>second.Length/500,"Thin mist produces visible pixels independently of platform motion: "+mistDifference);
        camera.transform.position = new Vector3(-2, .9f, -15);
        camera.fieldOfView = 40;
        if (backdrop) backdrop.FitNow();
        Capture(camera, "03_Platform_Detail.png");
    }

    static byte[] Capture(Camera camera, string name)
    {
        var priorTarget = camera.targetTexture;
        var priorActive = RenderTexture.active;
        var target = new RenderTexture(1920, 1080, 24);
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            if(!string.IsNullOrEmpty(name))File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
            return image.GetRawTextureData<byte>().ToArray();
        }
        finally
        {
            camera.targetTexture = priorTarget; RenderTexture.active = priorActive;
            Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target);
        }
    }

    public static void RunPlayAutomation() => Launch(true);
    [MenuItem("Tools/Side A/Validate Eclipse Atmosphere (Real Play)")]
    public static void RunPlayChecks() => Launch(false);
    static void Launch(bool quit)
    {
        RequireCleanEditor();
        Directory.CreateDirectory(Output);
        var original = EditorSceneManager.GetSceneManagerSetup();
        var saved = original.Select(s => new SavedScene { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive }).ToArray();
        SessionState.SetString(State + "scenes", JsonUtility.ToJson(new SceneSnapshot { scenes = saved }));
        SessionState.SetBool(State + "pending", true);
        SessionState.SetBool(State + "quit", quit);
        SessionState.SetBool(State + "success", false);
        File.WriteAllText(Path.Combine(Output, "play-validation.txt"), "REAL PLAY ATMOSPHERE QA\n" +
            "Public practice/menu fixture in standalone BattleScene. No main-world host or save writes.\n");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(State + "pending", false))
        {
            running = true; stopping = false; assertions = 0; runtimeError = null; lastFrame = -1;
            previousTimeScale = Time.timeScale;
            previousReduced = BattlePresentationSettings.ReducedMotion;
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 60;
            Application.logMessageReceived += Logged;
            Routine.Clear(); Routine.Push(PlayTests());
            EditorApplication.update += Tick;
        }
        if (change == PlayModeStateChange.ExitingPlayMode && running && !stopping)
            Finish(false, "Play was interrupted.");
        if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(State + "pending", false)) return;
        bool ok = SessionState.GetBool(State + "success", false);
        bool quit = SessionState.GetBool(State + "quit", false);
        SessionState.SetBool(State + "pending", false);
        SessionState.SetBool(State + "quit", false);
        if (quit) EditorApplication.Exit(ok ? 0 : 1);
        else
        {
            var snapshot = JsonUtility.FromJson<SceneSnapshot>(SessionState.GetString(State + "scenes", "{}"));
            if (snapshot != null && snapshot.scenes != null && snapshot.scenes.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(snapshot.scenes.Select(s => new SceneSetup {
                    path = s.path, isLoaded = s.isLoaded, isActive = s.isActive }).ToArray());
        }
    }

    static IEnumerator PlayTests()
    {
        yield return Delay(.7f);
        stages = Object.FindFirstObjectByType<BattleArenaStages>();
        manager = Object.FindFirstObjectByType<BattleManager>();
        Check(stages && manager && manager.trialMenu, "Original battle initialized");
        Check(!SideWorldTravel.Instance, "Standalone fixture has no main-world reward/save host");
        if (manager.trialMenu.IsQuickGuideOpen) manager.trialMenu.CloseQuickGuide();
        Check(manager.SelectionOpen, "Original selection is available");
        manager.trialMenu.SelectTrial(2);
        manager.BeginTutorialPractice();
        yield return Delay(.3f);
        Check(manager.IsTutorialActive && manager.IsBattleActive && !manager.IsPaused && Time.timeScale > 0,
            "Public tutorial practice provides unpaused gameplay");
        BattlePresentationSettings.ReducedMotion = false;
        effect = FindEffect(stages);
        Check(effect && effect.Ready, "Atmosphere builds in real Play");
        int collisionCount = stages.GetComponentsInChildren<Collider>(true).Length;
        float initial = effect.AtmosphereTime;
        yield return Delay(.65f);
        Check(effect.AtmosphereTime > initial + .15f, "Scaled atmosphere clock advances in gameplay");
        float savedScale = Time.timeScale;
        Time.timeScale = 0;
        yield return Delay(.15f);
        float paused = effect.AtmosphereTime;
        yield return Delay(.45f);
        Check(Mathf.Abs(effect.AtmosphereTime - paused) < .001f, "Pause freezes clouds, fog, cloth, chips and embers clock");
        Time.timeScale = savedScale;
        yield return Delay(.3f);
        Check(effect.AtmosphereTime > paused + .04f, "Clock resumes after pause");
        BattlePresentationSettings.ReducedMotion = true;
        yield return Delay(.15f);
        float reduced = effect.AtmosphereTime;
        yield return Delay(.45f);
        Check(Mathf.Abs(effect.AtmosphereTime - reduced) < .001f, "Reduced motion holds atmosphere clock");
        foreach (var renderer in PlatformRenderers(effect))
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            Check(block.GetFloat("_EclipseMotion") == 0, "Reduced motion turns off cloth/chip vertex movement");
        }
        effect.PreviewAt(1234);
        Check(Mathf.Abs(effect.AtmosphereTime - reduced) < .001f, "Edit preview API cannot override runtime clock");
        BattlePresentationSettings.ReducedMotion = false;
        yield return Delay(.3f);
        Check(effect.AtmosphereTime > reduced + .04f, "Motion setting resumes without resetting elapsed clock");
        var cloud = effect.CloudRenderer;
        foreach (int index in new[] { 0, 1, 2, 0, 2 })
        {
            Check(stages.Apply(index), "Runtime stage switch " + index);
            yield return Delay(.1f);
            Check(FindEffect(stages) == effect && cloud && effect.CloudRenderer == cloud,
                "Runtime switches reuse atmosphere and backdrop cloud");
            Check(effect.gameObject.activeInHierarchy == (index == 2) &&
                cloud.gameObject.activeInHierarchy == (index == 2) &&
                effect.MistRenderers.All(r => r.gameObject.activeInHierarchy == (index == 2)),
                "All atmosphere visuals follow runtime stage visibility");
            Check(stages.GetComponentsInChildren<Collider>(true).Length == collisionCount,
                "Runtime switches never change collision component count");
        }
        yield return Delay(.2f);
        Check(string.IsNullOrEmpty(runtimeError), "No runtime Error/Exception/Assert logged");
    }

    static IEnumerator Delay(float seconds)
    {
        double until = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < until) yield return null;
    }
    static void Tick()
    {
        if (!running || stopping) return;
        try
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play ended unexpectedly.");
            if (!string.IsNullOrEmpty(runtimeError)) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Atmosphere suite exceeded 60 seconds.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            while (Routine.Count > 0)
            {
                var current = Routine.Peek();
                if (!current.MoveNext()) { Routine.Pop(); continue; }
                if (current.Current is IEnumerator nested) { Routine.Push(nested); continue; }
                return;
            }
            Finish(true, "Bounded real Play atmosphere checks passed.");
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    static void Logged(string text, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeError = text + "\n" + stack;
    }
    static void BeforeReload() { if (running) Finish(false, "Assembly reload interrupted checks."); }
    static void Finish(bool ok, string message)
    {
        if (stopping) return;
        stopping = true; running = false;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Logged;
        Routine.Clear();
        Time.timeScale = previousTimeScale;
        BattlePresentationSettings.ReducedMotion = previousReduced;
        Application.runInBackground = previousBackground;
        SessionState.SetBool(State + "success", ok);
        File.AppendAllText(Path.Combine(Output, "play-validation.txt"),
            (ok ? "PASS " : "FAIL ") + assertions + " checks\n" + message + "\n");
        Debug.Log("ECLIPSE_ATMOSPHERE_PLAY_" + (ok ? "PASS " : "FAIL ") + assertions + " " + message);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        else if (SessionState.GetBool(State + "quit", false)) EditorApplication.Exit(ok ? 0 : 1);
    }
}
