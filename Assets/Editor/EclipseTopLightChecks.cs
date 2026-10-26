using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Small explicit Edit-mode regression. Every scene change and idle/camera pose
// is an unsaved fixture. Does not start Play, touch progression or save assets.
public static class EclipseTopLightChecks
{
    const string ScenePath = "Assets/Scenes/BattleScene.unity";
    const string Output = "LocalDocs/SideA/EclipseCathedral/TopLight_v005";
    const string Gate = "_EclipseTopLight";
    static int checks;

    struct SunState
    {
        public Quaternion rotation;
        public Color colour;
        public float intensity, strength, bias, normalBias;
        public bool temperature;
        public SunState(Light sun)
        {
            rotation = sun.transform.rotation; colour = sun.color; intensity = sun.intensity;
            strength = sun.shadowStrength; bias = sun.shadowBias; normalBias = sun.shadowNormalBias;
            temperature = sun.useColorTemperature;
        }
        public bool Matches(Light sun) => Quaternion.Angle(rotation, sun.transform.rotation) < .01f &&
            Close(colour, sun.color) && Mathf.Abs(intensity - sun.intensity) < .0001f &&
            Mathf.Abs(strength - sun.shadowStrength) < .0001f && Mathf.Abs(bias - sun.shadowBias) < .0001f &&
            Mathf.Abs(normalBias - sun.shadowNormalBias) < .0001f && temperature == sun.useColorTemperature;
    }
    struct AmbientState
    {
        public AmbientMode mode;
        public Color sky, equator, ground;
        public float intensity;
        public static AmbientState Capture() => new AmbientState { mode = RenderSettings.ambientMode,
            sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor,
            ground = RenderSettings.ambientGroundColor, intensity = RenderSettings.ambientIntensity };
        public bool Matches() => RenderSettings.ambientMode == mode && Close(sky, RenderSettings.ambientSkyColor) &&
            Close(equator, RenderSettings.ambientEquatorColor) && Close(ground, RenderSettings.ambientGroundColor) &&
            Mathf.Abs(intensity - RenderSettings.ambientIntensity) < .0001f;
        public void ApplyFixture()
        {
            RenderSettings.ambientMode = mode; RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = intensity;
        }
    }
    static bool Close(Color a, Color b) => Mathf.Abs(a.r - b.r) < .0001f &&
        Mathf.Abs(a.g - b.g) < .0001f && Mathf.Abs(a.b - b.b) < .0001f && Mathf.Abs(a.a - b.a) < .0001f;
    static void Check(bool condition, string message)
    {
        Debug.Log("ECLIPSE_TOPLIGHT_CHECK " + (condition ? "PASS " : "FAIL ") + message);
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
    static SkinnedMeshRenderer[] BodyRenderers(GameObject actor) => actor.GetComponentInChildren<Animator>(true)
        .GetComponentsInChildren<SkinnedMeshRenderer>(true);
    static float ReadGate(Renderer renderer)
    {
        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
        return block.GetFloat(Gate);
    }
    static void AssertGates(SkinnedMeshRenderer[] renderers, float value, string context)
        => Check(renderers.Length > 0 && renderers.All(r => Mathf.Approximately(ReadGate(r), value)),
            context + ": all real body renderers have stage gate " + value);

    [MenuItem("Tools/Side A/Validate Eclipse Top Light (Static)")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save open scene edits first. Top-light QA never saves or discards them.");
        var originalScenes = EditorSceneManager.GetSceneManagerSetup();
        bool reduced = BattlePresentationSettings.ReducedMotion;
        checks = 0; Directory.CreateDirectory(Output);
        try
        {
            BattlePresentationSettings.ReducedMotion = false;
            var battle = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var stages = Object.FindFirstObjectByType<BattleArenaStages>();
            var lighting = stages ? stages.GetComponent<BattleStageLighting>() : null;
            Check(stages && lighting && lighting.sceneSun, "Original stage and explicit scene sun exist");
            var blue = GameObject.FindGameObjectWithTag("Player");
            var red = GameObject.FindGameObjectWithTag("Enemy");
            Check(blue && red, "Both real fighters exist");
            var bodies = BodyRenderers(blue).Concat(BodyRenderers(red)).ToArray();
            var teamShader = Shader.Find("SideA/HumanoidTeamLit");
            Check(teamShader && !ShaderUtil.ShaderHasError(teamShader), "Updated team shader compiles");
            var defaults = new Material(teamShader);
            try { Check(defaults.HasProperty(Gate) && defaults.GetFloat(Gate) == 0,
                "Shared team shader top-light default is zero"); }
            finally { Object.DestroyImmediate(defaults); }
            Check(stages.Apply(0), "Apply original daylight stage");
            var daySun = new SunState(lighting.sceneSun); var dayAmbient = AmbientState.Capture();
            AssertGates(bodies, 0, "Daylight");
            Check(stages.Apply(2), "Apply Eclipse top-light stage");
            var expectedDirection = new Vector3(.15f, -.98f, .10f).normalized;
            Check(Vector3.Angle(lighting.sceneSun.transform.forward, expectedDirection) < .05f,
                "Third-stage main light points nearly straight down");
            Check(Close(lighting.sceneSun.color, new Color(.88f, .94f, 1)) &&
                Mathf.Abs(lighting.sceneSun.intensity - 1.35f) < .0001f && !lighting.sceneSun.useColorTemperature,
                "Third-stage moon-white key preserves warm ivory rather than orange stone");
            Check(-lighting.sceneSun.transform.forward.y > .95f && lighting.sceneSun.intensity > .24f,
                "Overhead key dominates existing faint red secondary rim");
            AssertGates(bodies, 1, "Eclipse");
            Check(stages.Apply(0) && daySun.Matches(lighting.sceneSun) && dayAmbient.Matches(),
                "0 to 2 to 0 restores original sunlight and ambient exactly");
            AssertGates(bodies, 0, "Restored daylight");
            Check(stages.Apply(1), "Apply original twilight stage");
            var twilightSun = new SunState(lighting.sceneSun); var twilightAmbient = AmbientState.Capture();
            Check(stages.Apply(2) && stages.Apply(1) && twilightSun.Matches(lighting.sceneSun) && twilightAmbient.Matches(),
                "1 to 2 to 1 restores original twilight sunlight and ambient");
            AssertGates(bodies, 0, "Restored twilight");

            // An unrelated renderer payload must survive every light update.
            var oldBlock = new MaterialPropertyBlock(); bodies[0].GetPropertyBlock(oldBlock);
            var sentinel = new MaterialPropertyBlock(); bodies[0].GetPropertyBlock(sentinel);
            var baseColour = new Color(.37f, .41f, .45f, 1); var teamColour = new Color(.12f, .23f, .34f, 1);
            var auraColour = new Color(.17f, .37f, .87f, 1);
            sentinel.SetColor("_BaseColor", baseColour); sentinel.SetColor("_TeamColor", teamColour);
            sentinel.SetColor("_EclipseAuraColor", auraColour); sentinel.SetFloat("_EclipseAuraStrength", .73f);
            sentinel.SetFloat("_EclipseAuraClock", 4.37f);
            sentinel.SetFloat("_TopLightQaSentinel", .381f); bodies[0].SetPropertyBlock(sentinel);
            // Call the public lighting API directly: stage reactivation can
            // legitimately ask the separate aura owner to update its own MPB.
            lighting.ApplyStage(2);
            bodies[0].GetPropertyBlock(sentinel);
            Check(Close(sentinel.GetColor("_BaseColor"), baseColour) && Close(sentinel.GetColor("_TeamColor"), teamColour) &&
                Close(sentinel.GetColor("_EclipseAuraColor"), auraColour) &&
                Mathf.Approximately(sentinel.GetFloat("_EclipseAuraStrength"), .73f) &&
                Mathf.Approximately(sentinel.GetFloat("_EclipseAuraClock"), 4.37f) &&
                Mathf.Approximately(sentinel.GetFloat("_TopLightQaSentinel"), .381f),
                "Top-light MPB update preserves body/team colours, aura colour/strength/clock and unrelated payload");
            bodies[0].SetPropertyBlock(oldBlock); lighting.ApplyStage(2);

            // Unsaved additive host exercises scene ownership without loading
            // the real world, creating a BaselineFlow or touching a user save.
            var host = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(host);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.123f, .234f, .345f);
            RenderSettings.ambientEquatorColor = new Color(.456f, .321f, .246f);
            RenderSettings.ambientGroundColor = new Color(.175f, .275f, .375f);
            RenderSettings.ambientIntensity = .419f;
            var hostAmbient = AmbientState.Capture();
            var hostLight = new GameObject("QA host light", typeof(Light)).GetComponent<Light>();
            hostLight.type = LightType.Directional; hostLight.color = new Color(.35f, .65f, .45f); hostLight.intensity = .43f;
            var hostSun = new SunState(hostLight);
            var hostFighter = new GameObject("QA host tagged actor", typeof(Animator)); hostFighter.tag = "Player";
            var hostSkin = new GameObject("QA host costume", typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>();
            hostSkin.transform.SetParent(hostFighter.transform, false);
            hostSkin.sharedMesh = bodies[0].sharedMesh; hostSkin.sharedMaterials = bodies[0].sharedMaterials;
            var hostBlock = new MaterialPropertyBlock(); hostBlock.SetFloat(Gate, .381f); hostSkin.SetPropertyBlock(hostBlock);
            foreach (var renderer in bodies)
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                block.SetFloat(Gate, 0); renderer.SetPropertyBlock(block);
            }
            Check(stages.Apply(2) && hostAmbient.Matches() && hostSun.Matches(hostLight),
                "Applying inactive-scene Eclipse never changes host ambient or explicit host light");
            AssertGates(bodies, 1, "Host active / Battle fighters");
            Check(Mathf.Approximately(ReadGate(hostSkin), .381f),
                "Same-tag host actor keeps its original MPB outside the BattleScene");
            Check(SceneManager.SetActiveScene(battle), "Return active scene to Battle fixture");
            lighting.ApplyStage(2);
            Check(!hostAmbient.Matches(), "Active BattleScene owns its distinct Eclipse ambient");
            Check(SceneManager.SetActiveScene(host), "Return to host fixture");
            // This fixture explicitly establishes the host environment. It does
            // not claim SceneManager automatically restores runtime ambient.
            hostAmbient.ApplyFixture();
            Check(stages.Apply(2) && hostAmbient.Matches(),
                "After host owner reestablishes its environment, inactive Battle updates preserve it");
            Check(SceneManager.SetActiveScene(battle), "Restore Battle for actual camera previews");
            lighting.ApplyStage(2);
            EditorSceneManager.CloseScene(host, true);

            RenderPreviews(stages, blue, red, bodies);
            Check(stages.Apply(0) && daySun.Matches(lighting.sceneSun), "Final earlier-stage switch still restores day key");
            AssertGates(bodies, 0, "Final daylight");

            // Mirror SideWorldTravel return order: host becomes active before
            // the BattleScene is unloaded. Never restore Battle ambient into it.
            var finalHost = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(finalHost);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.211f, .322f, .433f);
            RenderSettings.ambientEquatorColor = new Color(.144f, .255f, .366f);
            RenderSettings.ambientGroundColor = new Color(.188f, .177f, .166f);
            RenderSettings.ambientIntensity = .513f;
            var exitAmbient = AmbientState.Capture();
            Check(EditorSceneManager.CloseScene(battle, true) && exitAmbient.Matches(),
                "Host-active BattleScene unload preserves host environment");
            File.WriteAllText(Path.Combine(Output, "validation.txt"), "PASS " + checks + " static checks\n" +
                "Sun/day/twilight restore, per-scene MPB gate, additive host ambient/light isolation.\n" +
                "Previews are actual Unity renders of unsaved Idle/position/camera fixtures, not recorded gameplay.\n" +
                "Close AuraOn/AuraOff retain top-light gate1; overview retains v004 energy aura.\n" +
                "Real Play is verified separately by the existing aura/atmosphere suites.\n");
            Debug.Log("ECLIPSE_TOPLIGHT_PASS " + checks);
        }
        finally
        {
            BattlePresentationSettings.ReducedMotion = reduced;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (originalScenes.Length > 0 && originalScenes.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(originalScenes);
        }
    }
    static void RenderPreviews(BattleArenaStages stages, GameObject blue, GameObject red, SkinnedMeshRenderer[] bodies)
    {
        Check(stages.Apply(2), "Eclipse render fixture ready");
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.gameObject.SetActive(false);
        PlaceIdle(blue, -4); PlaceIdle(red, 0); Physics.SyncTransforms();
        var atmosphere = stages.GetComponentInChildren<BattleEclipseAtmosphere>();
        var aura = stages.GetComponentInChildren<BattleEclipseFighterAura>();
        Check(atmosphere && aura && aura.Ready, "Existing atmosphere and both team auras retained");
        atmosphere.PreviewAt(8); aura.PreviewAt(8);
        var camera = stages.battleCamera.GetComponent<Camera>();
        camera.transform.position = new Vector3(-2, 1.9f, -10);
        camera.transform.rotation = Quaternion.Euler(6, 0, 0); camera.fieldOfView = 35; camera.aspect = 16f / 9;
        camera.GetComponent<BattlePaintedBackdrop>()?.FitNow();
        AssertGates(bodies, 1, "AuraOn close-up");
        Capture(camera, "01_TopLight_Close_AuraOn_UnsavedIdleFixture.png");
        aura.enabled = false;
        AssertGates(bodies, 1, "AuraOff close-up");
        Check(aura.VisibleFighterCount == 0 && aura.ShellRenderers.Concat(aura.WispRenderers)
            .All(r => !r.enabled || !r.gameObject.activeInHierarchy), "AuraOff comparison removes only energy aura");
        Capture(camera, "02_TopLight_Close_AuraOff_UnsavedIdleFixture.png");
        aura.enabled = true; aura.PreviewAt(8);
        camera.transform.position = new Vector3(-2, 5, -34); camera.transform.rotation = Quaternion.Euler(8, 0, 0);
        camera.GetComponent<BattlePaintedBackdrop>()?.FitNow();
        Capture(camera, "03_TopLight_Overview_UnsavedIdleFixture.png");
        Check(!ShaderUtil.ShaderHasError(Shader.Find("FoldedRealm/EclipsePlatform")) &&
            !ShaderUtil.ShaderHasError(Shader.Find("SideA/HumanoidTeamLit")), "Both rendered lighting shaders remain error-free");
    }
    static void PlaceIdle(GameObject actor, float x)
    {
        var shape = actor.GetComponent<CapsuleCollider>();
        float foot = shape ? actor.transform.position.y - shape.bounds.min.y : 1f;
        actor.transform.position = new Vector3(x, foot + .02f, 0);
        var animator = actor.GetComponentInChildren<Animator>();
        animator.Rebind(); animator.Play("CharacterArmature|Idle", 0, 0); animator.Update(0);
    }
    static void Capture(Camera camera, string name)
    {
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var target = new RenderTexture(1920, 1080, 24);
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target);
        }
    }
}
