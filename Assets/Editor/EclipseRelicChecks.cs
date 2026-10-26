using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Small explicit Edit-mode check; all Bake fixtures use private mesh copies.
// No asset, prefab or scene is saved and no Play hooks are registered.
public static class EclipseRelicChecks
{
    const string Art = "Assets/Art/SideA/Eclipse/";
    const string Output = "LocalDocs/SideA/EclipseCathedral/RelicGlow_v006";
    static int checks;
    static void Check(bool condition, string message)
    {
        Debug.Log("ECLIPSE_RELIC_CHECK " + (condition ? "PASS " : "FAIL ") + message);
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static List<Vector4> UV(Mesh mesh, int channel)
    { var result = new List<Vector4>(); mesh.GetUVs(channel, result); return result; }

    sealed class Geometry
    {
        readonly Vector3[] vertices, normals;
        readonly Vector4[] tangents;
        readonly Color[] colours;
        readonly Vector2[] uv0;
        readonly int[][] indices;
        readonly Material[] materials;
        readonly Bounds bounds;
        public Geometry(Mesh mesh, Renderer renderer)
        {
            vertices = mesh.vertices; normals = mesh.normals; tangents = mesh.tangents;
            colours = mesh.colors; uv0 = mesh.uv; bounds = mesh.bounds;
            materials = renderer.sharedMaterials;
            indices = Enumerable.Range(0, mesh.subMeshCount).Select(i => mesh.GetIndices(i)).ToArray();
        }
        public bool Matches(Mesh mesh, Renderer renderer) => vertices.SequenceEqual(mesh.vertices) &&
            normals.SequenceEqual(mesh.normals) && tangents.SequenceEqual(mesh.tangents) &&
            colours.SequenceEqual(mesh.colors) && uv0.SequenceEqual(mesh.uv) &&
            bounds == mesh.bounds && materials.SequenceEqual(renderer.sharedMaterials) &&
            indices.Length == mesh.subMeshCount && Enumerable.Range(0, indices.Length)
                .All(i => indices[i].SequenceEqual(mesh.GetIndices(i)));
    }

    static void CheckImportedModel(string model)
    {
        string path = Art + "Eclipse_" + model + "_v001.fbx";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(source, model + " original imported FBX exists");
        var fixture = Object.Instantiate(source);
        var privateMeshes = new Dictionary<Mesh, Mesh>();
        try
        {
            foreach (var filter in fixture.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var original = filter.sharedMesh;
                if (!privateMeshes.TryGetValue(original, out var copy))
                { copy = Object.Instantiate(original); privateMeshes.Add(original, copy); }
                filter.sharedMesh = copy;
            }
            var filters = fixture.GetComponentsInChildren<MeshFilter>(true);
            var originals = filters.Select(f => new Geometry(f.sharedMesh, f.GetComponent<Renderer>())).ToArray();
            var previousMotion = filters.Select(f => UV(f.sharedMesh, 1)).ToArray();
            var first = EclipseMotionImport.Bake(fixture);
            Check(first.GlowingChipComponents == 1 && first.GlowingChipVertices > 0,
                model + " marks exactly one selected isolated stone component");
            Check((model != "Main" || first.FocusVertices > 0) && first.PendantVertices > 0 && first.ChainVertices > 0 && first.RelicVertices > 0,
                model + " contains authored ring, medallion, chain and pendant payload");
            int[] coverage = new int[5];
            var chipBounds = new Bounds(); bool foundChip = false;
            for (int meshIndex = 0; meshIndex < filters.Length; meshIndex++)
            {
                var filter = filters[meshIndex]; var mesh = filter.sharedMesh; var renderer = filter.GetComponent<Renderer>();
                var motion = UV(mesh, 1); var relic = UV(mesh, 2);
                Check(motion.Count == mesh.vertexCount && relic.Count == mesh.vertexCount,
                    model + " both metadata channels match the original vertex count");
                Check(originals[meshIndex].Matches(mesh, renderer),
                    model + " Bake preserves geometry, normals, colours, UV0, topology, bounds and materials");
                Check(previousMotion[meshIndex].Count == motion.Count && Enumerable.Range(0, motion.Count).All(i =>
                    previousMotion[meshIndex][i].x == motion[i].x && previousMotion[meshIndex][i].y == motion[i].y &&
                    previousMotion[meshIndex][i].z == motion[i].z),
                    model + " relic Bake preserves existing cloth/chip weights and phases");
                bool bounded = true, materialIsolation = true;
                for (int i = 0; i < motion.Count; i++)
                {
                    int type = Mathf.RoundToInt(motion[i].w); var data = relic[i];
                    bounded &= Finite(motion[i].w) && Mathf.Abs(motion[i].w - type) < .0001f && type >= 0 && type <= 4 &&
                        Finite(data.x) && Finite(data.y) && Finite(data.z) && Finite(data.w) &&
                        data.z >= 0 && data.z <= 1 && data.w >= 0 && data.w < Mathf.PI * 2 + .001f;
                    if (type < 0 || type > 4) continue;
                    coverage[type]++;
                    if (type == 0) bounded &= data == Vector4.zero;
                    if (type == 4)
                    {
                        bounded &= motion[i].y == 1 && Mathf.Abs(data.x) <= .501f && Mathf.Abs(data.y) <= .501f;
                        Vector3 world = filter.transform.TransformPoint(mesh.vertices[i]);
                        if (!foundChip) { chipBounds = new Bounds(world, Vector3.zero); foundChip = true; }
                        else chipBounds.Encapsulate(world);
                    }
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    string name = renderer.sharedMaterials[sub] ? renderer.sharedMaterials[sub].name : "";
                    bool gold = name.StartsWith("Eclipse_Gold", StringComparison.Ordinal);
                    bool basalt = name.StartsWith("Eclipse_Basalt", StringComparison.Ordinal);
                    foreach (int vertex in mesh.GetIndices(sub))
                    {
                        float type = motion[vertex].w;
                        if (type > .5f && type < 3.5f) materialIsolation &= gold;
                        if (type > 3.5f) materialIsolation &= basalt && motion[vertex].y == 1;
                        if (!gold && !basalt) materialIsolation &= type == 0;
                    }
                }
                Check(bounded, model + " relic types, gain, phases and stone-local coordinates are bounded");
                Check(materialIsolation, model + " relic masks affect only gold ornaments and selected floating basalt");
            }
            Check(coverage[0] > 0 && coverage[2] > 0 && coverage[3] > 0 && coverage[4] > 0 &&
                (model != "Main" || coverage[1] > 0), model + " required static and relic regions are populated");
            Check(foundChip && Mathf.Abs(chipBounds.center.x - (model == "Main" ? 7.3f : -1.1f)) < .35f,
                model + " selected stone lies near intended authored X position");
            var firstUV2 = filters.Select(f => UV(f.sharedMesh, 1)).ToArray();
            var firstUV3 = filters.Select(f => UV(f.sharedMesh, 2)).ToArray();
            var second = EclipseMotionImport.Bake(fixture);
            Check(Enumerable.Range(0, filters.Length).All(i =>
                firstUV2[i].SequenceEqual(UV(filters[i].sharedMesh, 1)) &&
                firstUV3[i].SequenceEqual(UV(filters[i].sharedMesh, 2)) &&
                originals[i].Matches(filters[i].sharedMesh, filters[i].GetComponent<Renderer>())) &&
                first.RelicVertices == second.RelicVertices && first.GlowingChipComponents == second.GlowingChipComponents,
                model + " repeated Bake is deterministic and geometry-preserving");
            File.AppendAllText(Path.Combine(Output, "mask-coverage.txt"), model + " type0..4=" +
                string.Join(",", coverage.Select(n => n.ToString())) + " glowingChipX=" + chipBounds.center.x + "\n");
        }
        finally
        {
            Object.DestroyImmediate(fixture);
            foreach (var mesh in privateMeshes.Values) Object.DestroyImmediate(mesh);
        }
    }

    [MenuItem("Tools/Side A/Validate Eclipse Relic Glow (Static)")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first; relic QA never saves/discards them.");
        var previous = EditorSceneManager.GetSceneManagerSetup();
        bool reduced = BattlePresentationSettings.ReducedMotion;
        checks = 0; Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "mask-coverage.txt"), "Private mesh-copy fixtures only\n");
        try
        {
            BattlePresentationSettings.ReducedMotion = false;
            Check(EclipseMotionImport.MotionDataVersion >= 4, "Importer version invalidates the previous UV cache");
            CheckImportedModel("Main"); CheckImportedModel("Upper");
            EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
            var stages = Object.FindFirstObjectByType<BattleArenaStages>();
            Check(stages && stages.Apply(2), "Original third-stage public API applies");
            var atmosphere = stages.GetComponentInChildren<BattleEclipseAtmosphere>();
            var art = atmosphere.gameObject;
            var platforms = art.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.sharedMaterials
                .Any(m => m && m.shader && m.shader.name == "FoldedRealm/EclipsePlatform")).ToArray();
            Check(platforms.Length == 3, "Relic decoration reuses the three existing platform renderers");
            Check(platforms.SelectMany(r => r.sharedMaterials).Distinct().Count() == 6,
                "Relic decoration reuses the six existing platform materials");
            int renderers = art.GetComponentsInChildren<Renderer>(true).Length;
            int lights = art.GetComponentsInChildren<Light>(true).Length;
            int colliders = stages.GetComponentsInChildren<Collider>(true).Length;
            foreach (int index in new[] { 0, 1, 2, 0, 2 })
            {
                Check(stages.Apply(index), "Public stage switch " + index);
                Check(stages.GetComponentsInChildren<BattleEclipseAtmosphere>(true).Single() == atmosphere &&
                    art.GetComponentsInChildren<Renderer>(true).Length == renderers &&
                    art.GetComponentsInChildren<Light>(true).Length == lights &&
                    stages.GetComponentsInChildren<Collider>(true).Length == colliders,
                    "Stage switching caches art without growing renderer/light/collision budgets");
            }
            var shader = Shader.Find("FoldedRealm/EclipsePlatform");
            Check(shader && !ShaderUtil.ShaderHasError(shader), "Platform relic shader compiles");
            var defaults = new Material(shader);
            try { Check(defaults.HasProperty("_RelicGlow") && defaults.GetFloat("_RelicGlow") == 1,
                "Relic glow has an explicit enabled default"); }
            finally { Object.DestroyImmediate(defaults); }
            CapturePreviews(stages, atmosphere, platforms);
            Check(!ShaderUtil.ShaderHasError(shader), "Actual relic renders introduce no shader compiler errors");
            File.WriteAllText(Path.Combine(Output, "validation.txt"), "PASS " + checks + " static checks\n" +
                "UV2 types / UV3 local coordinates, coverage, material isolation, unchanged geometry and repeatable private Bake.\n" +
                "No added platform renderer/material/light; cached stage switching preserves component budgets.\n" +
                "Actual Unity unsaved camera previews; atmosphere/aura frozen at 8 seconds while only platform MPB clocks change.\n" +
                "GlowOn/GlowOff at the same clock isolates new relic emission. Clock0/4.5 also shares existing cloth/crimson pulse timing.\n" +
                "Real pause/ReducedMotion timing remains covered by the existing atmosphere Play suite.\n");
            Debug.Log("ECLIPSE_RELIC_PASS " + checks);
        }
        finally
        {
            BattlePresentationSettings.ReducedMotion = reduced;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (previous.Length > 0 && previous.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
    }

    static void CapturePreviews(BattleArenaStages stages, BattleEclipseAtmosphere atmosphere, MeshRenderer[] platforms)
    {
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.gameObject.SetActive(false);
        atmosphere.PreviewAt(8); artAura(stages)?.PreviewAt(8);
        var camera = stages.battleCamera.GetComponent<Camera>();
        camera.transform.position = new Vector3(-2, -1.4f, -12);
        camera.transform.rotation = Quaternion.identity; camera.fieldOfView = 35; camera.aspect = 16f / 9;
        camera.GetComponent<BattlePaintedBackdrop>()?.FitNow();
        var originals = platforms.Select(r => { var b = new MaterialPropertyBlock(); r.GetPropertyBlock(b); return b; }).ToArray();
        try
        {
            SetRelic(platforms, 0, 1); var first = Capture(camera, "01_Relic_Clock0_UnsavedFixture.png");
            SetRelic(platforms, 4.5f, 1); var second = Capture(camera, "02_Relic_Clock4p5_UnsavedFixture.png");
            SetRelic(platforms, 4.5f, 0); var off = Capture(camera, "03_Relic_GlowOff_Clock4p5_UnsavedFixture.png");
            long timing = Difference(first, second), glow = Difference(second, off);
            Check(timing > first.Length / 1000, "Fixed-camera rendered platform pixels respond to the two controlled clocks");
            Check(glow > first.Length / 1000, "Same-clock GlowOn/GlowOff produces independently visible relic emission");
            File.WriteAllText(Path.Combine(Output, "render-difference.txt"),
                "Clock0/4.5 RGB absolute difference=" + timing + "\nSame-clock GlowOn/GlowOff difference=" + glow + "\n");
            SetRelic(platforms, 4.5f, 1);
            camera.transform.position = new Vector3(-2, 5, -34); camera.transform.rotation = Quaternion.Euler(8, 0, 0);
            camera.GetComponent<BattlePaintedBackdrop>()?.FitNow();
            Capture(camera, "04_Relic_Overview_UnsavedFixture.png");
        }
        finally { for (int i = 0; i < platforms.Length; i++) if (platforms[i]) platforms[i].SetPropertyBlock(originals[i]); }
    }
    static BattleEclipseFighterAura artAura(BattleArenaStages stages) => stages.GetComponentInChildren<BattleEclipseFighterAura>();
    static void SetRelic(MeshRenderer[] platforms, float clock, float enabled)
    {
        foreach (var renderer in platforms)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            block.SetFloat("_EclipseClock", clock); block.SetFloat("_RelicGlow", enabled); renderer.SetPropertyBlock(block);
        }
    }
    static long Difference(byte[] a, byte[] b)
    { long sum = 0; for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]); return sum; }

    // Optional explicit action, deliberately not part of Validate. This is a
    // fixed-camera Unity material-animation preview, not combat recording.
    public static void RenderMotionPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first.");
        var previous = EditorSceneManager.GetSceneManagerSetup();
        bool reduced = BattlePresentationSettings.ReducedMotion;
        string frames = Path.GetFullPath(Path.Combine(Output,"MotionFrames"));
        try
        {
            BattlePresentationSettings.ReducedMotion = false;
            Directory.CreateDirectory(frames);
            EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
            var stages = Object.FindFirstObjectByType<BattleArenaStages>();
            if (!stages || !stages.Apply(2)) throw new InvalidOperationException("Eclipse stage is missing.");
            var atmosphere = stages.GetComponentInChildren<BattleEclipseAtmosphere>();
            var aura=stages.GetComponentInChildren<BattleEclipseFighterAura>();
            if(aura)aura.enabled=false;
            foreach(var tag in new[]{"Player","Enemy"})
            {
                var actor=GameObject.FindGameObjectWithTag(tag);
                if(actor)actor.SetActive(false);
            }
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.gameObject.SetActive(false);
            var camera = stages.battleCamera.GetComponent<Camera>();
            camera.transform.position = new Vector3(-2, -1.4f, -12);
            camera.transform.rotation = Quaternion.identity; camera.fieldOfView = 35; camera.aspect = 16f / 9;
            camera.GetComponent<BattlePaintedBackdrop>()?.FitNow();
            for (int frame = 0; frame < 72; frame++)
            {
                atmosphere.PreviewAt(frame / 8f);
                Capture(camera, Path.Combine(frames, "frame-" + frame.ToString("D4") + ".png"), 960, 540);
            }
            File.WriteAllText(Path.Combine(frames, "ACTUAL_UNITY_MATERIAL_PREVIEW.txt"),
                "Actual Unity camera render: 72 frames at 8 fps, 9 seconds, 960x540.\n" +
                "Fixed camera observes the main gold medallion and pendant.\n" +
                "Atmosphere.PreviewAt(frame/8f) explicitly samples the material animation.\n" +
                "Actor visuals hidden for a decoration-only inspection.\n"+
                "Unsaved Edit-mode fixture, not actual combat/gameplay recording.\n");
            Debug.Log("ECLIPSE_RELIC_MOTION_PREVIEW 72 frames " + frames);
        }
        finally
        {
            BattlePresentationSettings.ReducedMotion = reduced;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (previous.Length > 0 && previous.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
    }

    static byte[] Capture(Camera camera, string name, int width = 1920, int height = 1080)
    {
        var target = new RenderTexture(width, height, 24); var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG()); return image.GetRawTextureData<byte>().ToArray();
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target);
        }
    }
}
