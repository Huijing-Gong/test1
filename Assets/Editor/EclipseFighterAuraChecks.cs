using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit QA fixture only: unsaved BattleScene, original public menu/practice
// APIs, no host/progression saves. The single position fixture tests visual
// following across a teleport; it is not represented as recorded gameplay.
[InitializeOnLoad]
public static class EclipseFighterAuraChecks
{
    const string Key = "QA.EclipseFighterAura.";
    const string ScenePath = "Assets/Scenes/BattleScene.unity";
    const string Output = "LocalDocs/SideA/EclipseCathedral/Aura_v004";
    static readonly Stack<IEnumerator> Routine = new Stack<IEnumerator>();
    static int checks, lastFrame;
    static bool running, stopping, oldReduced, oldBackground;
    static float oldTimeScale;
    static double deadline;
    static string runtimeError;
    static BattleArenaStages stages;
    static BattleManager manager;
    static BattleEclipseFighterAura aura;
    static GameObject blue, red;
    static RendererSnapshot[] originals;

    static EclipseFighterAuraChecks()
    {
        EditorApplication.playModeStateChanged += Changed;
    }

    sealed class RendererSnapshot
    {
        public SkinnedMeshRenderer renderer;
        public Material[] materials;
        public Mesh mesh;
        public Transform[] bones;
        public Vector3[] vertices;
        public Matrix4x4[] bindposes;
    }
    static RendererSnapshot[] Snapshot(GameObject first, GameObject second)
        => new[] { first, second }.SelectMany(f => f.GetComponentInChildren<Animator>()
            .GetComponentsInChildren<SkinnedMeshRenderer>(true)).Select(r => new RendererSnapshot {
                renderer = r, materials = r.sharedMaterials, mesh = r.sharedMesh, bones = r.bones,
                vertices = Application.isPlaying ? null : r.sharedMesh.vertices,
                bindposes = Application.isPlaying ? null : r.sharedMesh.bindposes }).ToArray();
    static void CheckOriginals(RendererSnapshot[] snapshot)
    {
        Check(snapshot.All(s => s.renderer && s.renderer.sharedMesh == s.mesh &&
            s.renderer.sharedMaterials.SequenceEqual(s.materials) && s.renderer.bones.SequenceEqual(s.bones) &&
            (s.vertices == null || s.mesh.vertices.SequenceEqual(s.vertices) && s.mesh.bindposes.SequenceEqual(s.bindposes))),
            "Original fighter meshes, material references and skeleton bindings remain unchanged");
    }
    static BattleEclipseFighterAura Find(BattleArenaStages owner)
        => owner.GetComponentsInChildren<BattleEclipseFighterAura>(true).SingleOrDefault();
    static void Check(bool condition, string message)
    {
        Debug.Log("ECLIPSE_AURA_CHECK " + (condition ? "PASS " : "FAIL ") + message);
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
    static void RequireClean()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save open scenes first; aura QA never saves/discards authored changes.");
        Directory.CreateDirectory(Output);
    }
    static void BindActors()
    {
        stages = Object.FindFirstObjectByType<BattleArenaStages>();
        manager = Object.FindFirstObjectByType<BattleManager>();
        blue = GameObject.FindGameObjectWithTag("Player");
        red = GameObject.FindGameObjectWithTag("Enemy");
        Check(stages && manager && blue && red, "Original arena and both tagged fighters exist");
    }
    static void CheckHierarchy(BattleEclipseFighterAura owned, RendererSnapshot[] snapshot)
    {
        Check(owned && owned.Ready && owned.AuraCount == 2, "Exactly two team aura subjects are ready");
        Check(owned.GetComponentsInChildren<Collider>(true).Length == 0,
            "Aura hierarchy has no collision components");
        Check(owned.ShellRenderers != null && owned.ShellRenderers.Length > 0 &&
            owned.WispRenderers != null && owned.WispRenderers.Length > 0,
            "Whole-body shell and auxiliary energy renderers exist");
        foreach (var renderer in owned.ShellRenderers.Concat(owned.WispRenderers))
        {
            Check(renderer && !renderer.transform.IsChildOf(blue.transform) && !renderer.transform.IsChildOf(red.transform),
                "Aura renderer lives outside both fighter/Animator hierarchies: " + renderer.name);
            Check(renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows,
                "Aura never casts/receives gameplay shadows: " + renderer.name);
            Check(renderer.sharedMaterials.All(m => m && m.shader && !ShaderUtil.ShaderHasError(m.shader)),
                "Aura material compiles: " + renderer.name);
        }
        foreach (var shell in owned.ShellRenderers)
        {
            var source = snapshot.FirstOrDefault(s => shell.bones.SequenceEqual(s.bones) &&
                shell.sharedMesh.vertexCount == s.mesh.vertexCount && shell.sharedMesh.subMeshCount == s.mesh.subMeshCount);
            Check(source != null && shell.sharedMesh != source.mesh,
                "Shell owns a same-size private smoothing mesh and reuses actual skeleton");
            Check(source.renderer.name.Contains("_Ranger_") && !source.renderer.name.Contains("_Belt_"),
                "Energy shell is limited to Ranger costume pieces, preserving face and belt readability");
            if (!Application.isPlaying)
                Check(shell.sharedMesh.vertices.SequenceEqual(source.vertices) &&
                    shell.sharedMesh.bindposes.SequenceEqual(source.bindposes) &&
                    Enumerable.Range(0, source.mesh.subMeshCount).All(i => shell.sharedMesh.GetIndices(i).SequenceEqual(source.mesh.GetIndices(i))),
                    "Smoothed shell keeps source positions, submesh topology and bindposes");
        }
        Check(new[] { blue, red }.All(f => !f.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Any(r => owned.ShellRenderers.Contains(r))),
            "Afterimage fighter-root skin scanning cannot capture aura shells");
    }

    [MenuItem("Tools/Side A/Validate Eclipse Fighter Aura (Static)")]
    public static void Validate()
    {
        RequireClean();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        bool reduced = BattlePresentationSettings.ReducedMotion;
        checks = 0;
        try
        {
            BattlePresentationSettings.ReducedMotion = false;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BindActors(); originals = Snapshot(blue, red);
            int colliderCount = stages.GetComponentsInChildren<Collider>(true).Length;
            Check(originals.SelectMany(s => s.materials).Any(m => m && m.HasProperty("_EclipseAuraStrength")),
                "Original team shader exposes optional aura properties");
            foreach (var material in originals.SelectMany(s => s.materials).Where(m => m).Distinct())
                if (material.HasProperty("_EclipseAuraStrength"))
                {
                    var defaults = new Material(material.shader);
                    try { Check(defaults.GetFloat("_EclipseAuraStrength") == 0,
                        "Team shader default aura strength is zero"); }
                    finally { Object.DestroyImmediate(defaults); }
                }
            Check(stages.Apply(2), "Third-stage public API applies");
            aura = Find(stages); CheckHierarchy(aura, originals); CheckOriginals(originals);
            var shells = aura.ShellRenderers.ToArray();
            var wisps = aura.WispRenderers.ToArray();
            foreach (int stage in new[] { 0, 1, 2, 0, 2 })
            {
                Check(stages.Apply(stage), "Public stage switch " + stage);
                Check(Find(stages) == aura, "Stage switches cache one aura component");
                Check(stage == 2 || aura.ShellRenderers.Concat(aura.WispRenderers)
                    .All(r => !r.enabled || !r.gameObject.activeInHierarchy),
                    "Aura hidden outside Eclipse");
                if(stage!=2)Check(originals.All(s => {
                    var b=new MaterialPropertyBlock();s.renderer.GetPropertyBlock(b);
                    return Mathf.Approximately(b.GetFloat("_EclipseAuraStrength"),0);
                }), "Earlier stages restore zero native rim/fill strength");
                Check(stages.GetComponentsInChildren<Collider>(true).Length == colliderCount,
                    "Aura never changes arena collision count");
                CheckOriginals(originals);
            }
            aura.PreviewAt(8);
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.gameObject.SetActive(false);
            PlacePreviewActor(blue, -4f); PlacePreviewActor(red, 0f);
            Physics.SyncTransforms(); aura.PreviewAt(8);
            var camera = stages.battleCamera.GetComponent<Camera>();
            camera.transform.position = new Vector3(-2, 1.9f, -10);
            camera.transform.rotation = Quaternion.Euler(6, 0, 0); camera.fieldOfView = 35; camera.aspect = 16f / 9;
            camera.GetComponent<BattlePaintedBackdrop>()?.FitNow();
            aura.PreviewAt(0);var first=Capture(camera,null);
            aura.PreviewAt(8);var second=Capture(camera, "01_Aura_Close_UnsavedIdleFixture.png");
            long difference=0;for(int i=0;i<first.Length;i++)difference+=Math.Abs(first[i]-second[i]);
            Check(difference>first.Length/1000,"Actual rendered aura pixels change at two fixed animation clocks");
            File.WriteAllText(Path.Combine(Output,"render-difference.txt"),"Aura-only clock 0 versus 8 seconds RGB absolute difference="+difference+"\n");
            // Disable verifies property cleanup without destroying platform art.
            aura.enabled = false;
            Check(shells.Concat(wisps).All(r => !r.enabled || !r.gameObject.activeInHierarchy),
                "Component disable hides external shells and energy wisps");
            foreach (var state in originals)
            {
                var block = new MaterialPropertyBlock(); state.renderer.GetPropertyBlock(block);
                Check(Mathf.Approximately(block.GetFloat("_EclipseAuraStrength"), 0),
                    "Disable resets source renderer aura strength");
            }
            Capture(camera, "02_Aura_Off_UnsavedIdleFixture.png");
            aura.enabled = true; aura.PreviewAt(8); CheckOriginals(originals);
            camera.transform.position = new Vector3(-2, 5, -34);
            camera.transform.rotation = Quaternion.Euler(8, 0, 0);
            camera.GetComponent<BattlePaintedBackdrop>()?.FitNow();
            Capture(camera, "03_Aura_Overview_UnsavedIdleFixture.png");
            var ownedMaterials = shells.Concat(wisps).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
            var ownedMeshes = shells.Select(r => r.sharedMesh).Concat(wisps.Select(r => r.GetComponent<MeshFilter>())
                .Where(f => f && f.sharedMesh).Select(f => f.sharedMesh)).Distinct().ToArray();
            Check(ownedMeshes.All(m => !originals.Any(s => s.mesh == m)), "Every owned aura mesh is distinct from original fighter source meshes");
            var auraObject = aura.gameObject;
            Object.DestroyImmediate(aura);
            Check(shells.All(r => !r) && wisps.All(r => !r), "Component destruction releases owned renderers");
            Check(ownedMaterials.All(m => !m) && ownedMeshes.All(m => !m),
                "Component destruction releases private aura materials and procedural meshes");
            Check(originals.All(s => s.mesh), "Shared source meshes survive aura cleanup");
            CheckOriginals(originals);
            Check(auraObject, "Aura cleanup keeps platform art root");
            File.WriteAllText(Path.Combine(Output, "static-validation.txt"),
                "PASS " + checks + " static checks\nOriginal Unity BattleScene, unsaved.\n" +
                "Preview fixture: original Idle pose sampled on both actors at X=-4,0 on main deck.\n" +
                "Close, aura-off comparison, and overview are actual Unity camera renders.\n" +
                "No real runtime movement or gameplay input is claimed by this static camera preview.\n");
            Debug.Log("ECLIPSE_AURA_STATIC_PASS " + checks);
        }
        finally
        {
            BattlePresentationSettings.ReducedMotion = reduced;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
    static void PlacePreviewActor(GameObject actor, float x)
    {
        var shape = actor.GetComponent<CapsuleCollider>();
        float foot = shape ? actor.transform.position.y - shape.bounds.min.y : 1f;
        actor.transform.position = new Vector3(x, foot + .02f, 0);
        var animator = actor.GetComponentInChildren<Animator>();
        animator.Rebind(); animator.Play("CharacterArmature|Idle", 0, 0); animator.Update(0);
    }
    static byte[] Capture(Camera camera, string name)
    {
        var prior = camera.targetTexture; var active = RenderTexture.active;
        var target = new RenderTexture(1920, 1080, 24);
        var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
            if(!string.IsNullOrEmpty(name))File.WriteAllBytes(Path.Combine(Output, name), texture.EncodeToPNG());
            return texture.GetRawTextureData<byte>().ToArray();
        }
        finally
        {
            camera.targetTexture = prior; RenderTexture.active = active;
            Object.DestroyImmediate(texture); target.Release(); Object.DestroyImmediate(target);
        }
    }

    public static void RunPlayAutomation() => Launch(true);
    [MenuItem("Tools/Side A/Validate Eclipse Fighter Aura (Real Play)")]
    public static void RunPlayChecks() => Launch(false);
    static void Launch(bool quit)
    {
        RequireClean();
        SessionState.SetBool(Key + "pending", true); SessionState.SetBool(Key + "quit", quit);
        SessionState.SetBool(Key + "success", false);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key + "pending", false))
        {
            running = true; stopping = false; checks = 0; runtimeError = null; lastFrame = -1;
            deadline = EditorApplication.timeSinceStartup + 60;
            oldTimeScale = Time.timeScale; oldReduced = BattlePresentationSettings.ReducedMotion;
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += Logged;
            Routine.Clear(); Routine.Push(PlayTests()); EditorApplication.update += Tick;
        }
        if (change == PlayModeStateChange.ExitingPlayMode && running && !stopping)
            Finish(false, "Play interrupted.");
        if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + "pending", false)) return;
        bool ok = SessionState.GetBool(Key + "success", false), quit = SessionState.GetBool(Key + "quit", false);
        SessionState.SetBool(Key + "pending", false); SessionState.SetBool(Key + "quit", false);
        if (quit) EditorApplication.Exit(ok ? 0 : 1);
    }
    static IEnumerator PlayTests()
    {
        yield return Delay(.7f);
        BindActors(); Check(!SideWorldTravel.Instance, "No overworld reward/save host exists in QA fixture");
        if (manager.trialMenu.IsQuickGuideOpen) manager.trialMenu.CloseQuickGuide();
        Check(manager.SelectionOpen, "Original selection ready");
        manager.trialMenu.SelectTrial(2);
        Check(stages.Apply(2), "Explicit stage fixture prepares third-stage art while selection is still open");
        aura = Find(stages);
        Check(aura && aura.Ready, "Eclipse selection builds aura");
        yield return Delay(.15f);
        Check(aura.VisibleFighterCount == 0, "Selection does not reveal fighters through aura");
        manager.trialMenu.OpenQuickGuide(); yield return Delay(.2f);
        Check(aura.VisibleFighterCount == 0, "Quick-guide menu cannot leak live fighter aura");
        manager.trialMenu.CloseQuickGuide(); yield return Delay(.1f);
        yield return RealCountdownTests();
        // Public practice action reloads the real scene after a formal start.
        // Rebind after this lifecycle boundary instead of retaining old actors.
        manager.BeginTutorialPractice();
        yield return Delay(.7f);
        BindActors(); aura = Find(stages);
        yield return Delay(.2f);
        Check(!pendingOldAura && pendingOldMaterials.All(m => !m) && pendingOldWispMeshes.All(m => !m),
            "Original public scene reload releases the previous aura component and its private resources");
        Check(aura && aura.Ready, "Fresh scene creates a ready replacement aura");
        Check(manager.IsTutorialActive && manager.IsBattleActive && !manager.IsPaused, "Public practice begins without saves/results");
        originals = Snapshot(blue, red); CheckHierarchy(aura, originals);
        Check(aura.VisibleFighterCount == 2, "Both real fighters receive their Eclipse aura");
        float clock = aura.AuraClock; yield return Delay(.4f);
        Check(aura.AuraClock > clock + .05f, "Aura advances with gameplay scaled time");
        float scale = Time.timeScale; Time.timeScale = 0; yield return Delay(.1f);
        clock = aura.AuraClock; yield return Delay(.35f);
        Check(Mathf.Abs(aura.AuraClock - clock) < .001f, "Pause freezes aura animation");
        Time.timeScale = scale;
        BattlePresentationSettings.ReducedMotion = true; yield return Delay(.1f);
        clock = aura.AuraClock; yield return Delay(.35f);
        Check(Mathf.Abs(aura.AuraClock - clock) < .001f && aura.VisibleFighterCount == 2,
            "Reduced motion holds clock and keeps readable static team rim");
        BattlePresentationSettings.ReducedMotion = false;
        var body = blue.GetComponent<Rigidbody>();
        Vector3 previous = body.position;
        body.position += Vector3.right * 3f; blue.transform.position = body.position; Physics.SyncTransforms();
        yield return Delay(.15f);
        CheckOriginals(originals);
        Check(aura.ShellRenderers.All(r => r && r.bounds.size.y < 6 && r.bounds.size.x < 6),
            "Teleport fixture keeps bounded whole-body shells, without a long interpolation trail");
        body.position = previous; blue.transform.position = previous; Physics.SyncTransforms();
        var stocks = blue.GetComponent<BattleStockLives>();
        stocks.SetTutorialRemaining(0); yield return Delay(.15f);
        Check(aura.VisibleFighterCount == 1, "Eliminated tutorial stock fixture hides only the corresponding aura");
        stocks.SetTutorialRemaining(2); yield return Delay(.15f);
        Check(aura.VisibleFighterCount == 2, "Restored tutorial fixture reuses both team aura subjects");
        foreach (int index in new[] { 0, 1, 2, 0, 2 })
        {
            Check(stages.Apply(index), "Runtime stage switch " + index); yield return Delay(.1f);
            Check(Find(stages) == aura && aura.VisibleFighterCount == (index == 2 ? 2 : 0),
                "Cached aura visibility follows selected stage");
            CheckOriginals(originals);
        }
        Check(string.IsNullOrEmpty(runtimeError), "No runtime errors, exceptions or assertions");
    }

    static IEnumerator RealCountdownTests()
    {
        BattlePresentationSettings.ReducedMotion = false;
        var initial = aura;
        var oldMaterials = initial.ShellRenderers.Concat(initial.WispRenderers)
            .SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
        var oldWispMeshes = initial.ShellRenderers.Select(r => r.sharedMesh)
            .Concat(initial.WispRenderers.Select(r => r.GetComponent<MeshFilter>())
                .Where(f => f && f.sharedMesh).Select(f => f.sharedMesh)).Distinct().ToArray();
        manager.BeginSelectedTrial();
        var intro = manager.GetComponent<BattleIntroPresentation>();
        Check(intro && intro.Active && intro.Phase == "3", "Normal public trial start enters actual countdown 3");
        var sourceRenderers = new[] { blue, red }.SelectMany(f => f.GetComponentInChildren<Animator>()
            .GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToArray();
        Check(sourceRenderers.Length > 0 && sourceRenderers.All(r =>
            r.sharedMaterials.All(m => m && m.shader && m.shader.name == "SideA/Intro Reveal")),
            "At countdown zero original renderers stay enabled while Intro Reveal hides their surfaces");
        Check(sourceRenderers.All(r => r.sharedMaterials.All(m => m.GetFloat("_RevealHeight") <= r.bounds.min.y + .02f)),
            "Zero-progress reveal cutoffs begin below the visible mesh");
        Check(initial.VisibleFighterCount == 0 && initial.ShellRenderers.Concat(initial.WispRenderers)
            .All(r => !r.enabled || !r.gameObject.activeInHierarchy),
            "Zero-progress enabled body renderers cannot leak a visible aura before reveal");
        Check(intro.RevealSurfaceCount == sourceRenderers.Length,
            "Actual intro surface scan excludes the entire external aura hierarchy");
        bool invisibleThroughout = true;
        var phases = new HashSet<string>();
        double until = EditorApplication.timeSinceStartup + 15;
        while (!manager.IsBattleActive)
        {
            if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Normal countdown/landing did not start battle.");
            if (intro.Active)
            {
                phases.Add(intro.Phase ?? "");
                invisibleThroughout &= initial.VisibleFighterCount == 0 && initial.ShellRenderers.Concat(initial.WispRenderers)
                    .All(r => !r.enabled || !r.gameObject.activeInHierarchy);
            }
            yield return null;
        }
        yield return Delay(.15f);
        Check(phases.Contains("3") && phases.Contains("2") && phases.Contains("1"),
            "All three original countdown phases actually ran");
        Check(invisibleThroughout, "No aura appears through hidden/partially revealed actors during real countdown");
        Check(!intro.Active && initial.VisibleFighterCount == 2,
            "Both team auras become visible after the original drop/landing starts combat");
        Check(sourceRenderers.All(r => r && r.sharedMaterials.All(m => m && m.shader.name != "SideA/Intro Reveal")),
            "Original intro restores body materials before live aura presentation");
        Capture(stages.battleCamera.GetComponent<Camera>(),"04_Aura_ActualPlay.png");
        var stocks=blue.GetComponent<BattleStockLives>();
        Check(stocks && stocks.LoseStock()==BattleStockLives.RingOut.Respawn && stocks.Respawning,
            "Original stock API starts a real respawn coroutine in the standalone fixture");
        yield return Delay(.2f);
        Check(initial.VisibleFighterCount==2 && stocks.Remaining==1,
            "Respawn keeps both auras visible at their current fighter positions");
        yield return Delay(.6f);
        Check(!stocks.Respawning && stocks.IsProtected && initial.VisibleFighterCount==2,
            "Aura coexists with the original completed respawn/protection state");
        // The following BeginTutorialPractice in PlayTests is the original
        // public scene-reload boundary; inspect released references afterwards.
        pendingOldAura = initial; pendingOldMaterials = oldMaterials; pendingOldWispMeshes = oldWispMeshes;
    }
    static BattleEclipseFighterAura pendingOldAura;
    static Material[] pendingOldMaterials;
    static Mesh[] pendingOldWispMeshes;
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
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play ended.");
            if (!string.IsNullOrEmpty(runtimeError)) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Aura suite exceeded 60 seconds.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
            while (Routine.Count > 0)
            {
                var iterator = Routine.Peek();
                if (!iterator.MoveNext()) { Routine.Pop(); continue; }
                if (iterator.Current is IEnumerator nested) { Routine.Push(nested); continue; }
                return;
            }
            Finish(true, "Bounded real Play aura checks completed.");
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    static void Logged(string text, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeError = text + "\n" + stack;
    }
    static void Finish(bool ok, string message)
    {
        if (stopping) return; stopping = true; running = false;
        EditorApplication.update -= Tick; Application.logMessageReceived -= Logged; Routine.Clear();
        Time.timeScale = oldTimeScale; BattlePresentationSettings.ReducedMotion = oldReduced;
        Application.runInBackground = oldBackground; SessionState.SetBool(Key + "success", ok);
        File.WriteAllText(Path.Combine(Output, "play-validation.txt"),
            (ok ? "PASS " : "FAIL ") + checks + " checks\n" + message +
            "\nActual public normal-trial countdown 3/2/1, drop/landing and subsequent public practice reload tested.\n" +
            "Original stock API also starts and completes a respawn; no physical ring-out input or main-world saves tested.\n");
        Debug.Log("ECLIPSE_AURA_PLAY_" + (ok ? "PASS " : "FAIL ") + checks + " " + message);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        else if (SessionState.GetBool(Key + "quit", false)) EditorApplication.Exit(ok ? 0 : 1);
    }
}
