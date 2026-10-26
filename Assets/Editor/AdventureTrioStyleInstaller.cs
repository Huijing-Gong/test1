using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Only the disposable integration project may author these local copies. Neither
// the shared vendor atlas/importer nor any existing encounter source is modified.
public static class AdventureTrioStyleInstaller
{
    public const string Folder = "Assets/Art/Enemies/AdventureTrio/Styled";
    public const string Output = "LocalDocs/Enemies/TrioStyles-20261021";
    internal const string SentinelFolder = "Assets/Art/Enemies/AdventureTrio/SilverSentinel";
    const string Source = "Assets/RPGMonsterWave03PBR";
    const string Albedo = Source + "/Texture/DefaultPBR/Albedo.png";
    const string Emission = Source + "/Texture/DefaultPBR/Emission.png";
    const string ShaderName = "FoldedRealm/AdventureCreatureToon";
    const string Marker = "_AdventureTrio_v1";
    const string DemonFolder = "Assets/Art/Enemies/DemonKingTrial";
    const string DemonPrefab = DemonFolder + "/DemonKingScoutTrial.prefab";
    const float KnightHeight = 3.15f, KnightRadius = .8f;

    enum Role { FishBody, FishWeapon, Mushroom, KnightBody, KnightShield, KnightSpear }
    enum Palette { Neutral, Black, Grey, Red, Gold, Brown, Green, Blue, Purple }
    sealed class ActorSnapshot
    {
        public Health health;
        public Vector3 position, scale;
        public Quaternion rotation;
        public string prefab, appearance;
        public int hp;
        public bool protectAppearance;
    }
    sealed class TextureSet
    {
        public Texture2D albedo, emission, tech;
    }
    static readonly string[] Kinds = { "Fishman", "Mushroom", "BishopKnight" };
    static readonly string[] NativeNames = { "FishmanPBR", "MushroomSmilePBR", "BishopKnightPBR" };
    static readonly string[] PrefabNames = { "Fishman", "SmilingMushroom", "BishopKnight" };

    [MenuItem("Tools/Adventure Trio/Refresh Original Knight Shield Material Only")]
    public static void RefreshKnightShieldOnly()
    {
        Guard(); RequireCleanScenes();
        var current=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Knight/KnightShield.mat");
        if(current&&current.shader&&current.shader.name==KnightShieldFinishInstaller.ShaderName)
        { KnightShieldFinishInstaller.Install();return; }
        var protectedPaths=Directory.GetFiles(Folder+"/Knight","KnightSpear*",SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(SentinelFolder,"*",SearchOption.AllDirectories))
            .Concat(new[]{SilverSentinelRemodelInstaller.PrefabPath});
        var before=protectedPaths.ToDictionary(p=>p,HashFile);
        var atlas=Decode(Albedo);var emission=Decode(Emission);
        try
        {
            var textures=MakeTextures(Role.KnightShield,atlas,emission,new HashSet<Vector2Int>());
            MakeMaterial(Role.KnightShield,Shader.Find(ShaderName),textures);
            AssetDatabase.SaveAssets();
            foreach(var entry in before)Need(HashFile(entry.Key)==entry.Value,"Shield-only update preserved "+entry.Key);
            Debug.Log("KNIGHT SHIELD MATERIAL PASS: original mesh/UV/rig preserved; navy face and warm-gold trim; spear and armor files unchanged.");
        }
        finally { Object.DestroyImmediate(atlas);Object.DestroyImmediate(emission); }
    }

    [MenuItem("Tools/Adventure Trio/Apply Approved Styles And Validate")]
    public static void BuildAndValidate()
    {
        Guard(); RequireCleanScenes();
        var scene = SceneManager.GetSceneByPath(AdventureTrioInstaller.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(AdventureTrioInstaller.ScenePath);
        try { BuildValidatedScene(scene); }
        catch
        {
            // The entry point requires clean scenes. Prefab saves may dirty their
            // scene instances before a later asset or placement assertion fails;
            // restore that clean on-disk scene so this entry point can be retried.
            // Partial generated assets are deliberately retained and rebuilt from
            // their native sources on the next attempt, preserving existing GUIDs.
            EditorSceneManager.OpenScene(AdventureTrioInstaller.ScenePath, OpenSceneMode.Single);
            throw;
        }
    }

    static void BuildValidatedScene(Scene scene)
    {
        var group = scene.GetRootGameObjects().SingleOrDefault(g => g.name == Marker);
        Need(group && group.transform.childCount == 3, "The installed trio must exist before styling");
        var knight = group.GetComponentsInChildren<BishopKnightAI>(true).Single().gameObject;
        var protectedActors = Find<Health>(scene).Where(h => h.Team == HealthTeam.Enemy && h.gameObject != knight)
            .Select(h => new ActorSnapshot { health = h, position = h.transform.position, rotation = h.transform.rotation,
                scale = h.transform.localScale, prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(h.gameObject),
                hp = h.MaxHealth, protectAppearance = !h.transform.IsChildOf(group.transform), appearance = Appearance(h.gameObject) }).ToArray();
        Need(protectedActors.Length == 19 && protectedActors.Count(a => a.protectAppearance) == 17,
            "All 19 other actors, including the 17 original encounters, must be accounted for");
        var layout = Find<OverworldV2Layout>(scene).Single();
        string layoutBefore = EditorJsonUtility.ToJson(layout);
        var hashes = CaptureSourceHashes(protectedActors.Where(a => a.protectAppearance));
        Vector3 knightBefore = knight.transform.position;
        EnsureFolder(Folder); Directory.CreateDirectory(Output);
        var audit = new StringBuilder("kind,renderer,mesh,sourceMesh,vertices,triangles,atlasColumn,atlasRowTop,verticesUsingCell,sourceColor,semantic,eyeCell\n");
        var mapping = new StringBuilder("kind,renderer,originalMaterial,styledMaterial,styledAlbedo,emissionMask,techMask\n");
        var sourceAtlas = Decode(Albedo); var sourceEmission = Decode(Emission);
        try
        {
            Need(sourceAtlas.width == 512 && sourceAtlas.height == 512, "Expected the original 512-square palette atlas");
            var shader = Shader.Find(ShaderName); Need(shader, "Creature toon shader must compile before styling: " + ShaderName);
            for (int i = 0; i < Kinds.Length; i++)
            {
                var native = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/Prefab/Characters/" + NativeNames[i] + ".prefab");
                Need(native, "Source native prefab exists: " + NativeNames[i]);
                BuildPrefab(Kinds[i], PrefabNames[i], native, shader, sourceAtlas, sourceEmission, audit, mapping);
            }
        }
        finally { Object.DestroyImmediate(sourceAtlas); Object.DestroyImmediate(sourceEmission); }

        // Saving an existing prefab preserves its GUID and propagates its authored
        // values to scene instances. Clear only a stale Knight health override, if
        // present, so the miniboss health remains inherited from the source prefab.
        var knightHealth = new SerializedObject(knight.GetComponent<Health>());
        var maxHealth = knightHealth.FindProperty("maxHealth");
        if (maxHealth.prefabOverride)
            PrefabUtility.RevertPropertyOverride(maxHealth, InteractionMode.AutomatedAction);
        Physics.SyncTransforms();
        if (!AdventureTrioInstaller.StyledKnightSiteSafe(scene, knight, knightBefore, out string previousSiteReason))
        {
            Vector3 chosen = AdventureTrioInstaller.FindStyledKnightSite(scene, knight);
            Need(Flat(chosen - knightBefore) <= 8.001f, "Knight relocation stays within 8m of its existing home");
            knight.transform.position = chosen;
            PrefabUtility.RecordPrefabInstancePropertyModifications(knight.transform);
            Physics.SyncTransforms();
        }

        VerifyProtectedActors(protectedActors);
        Need(EditorJsonUtility.ToJson(layout) == layoutBefore, "Terrain and navigation layout references are unchanged");
        VerifySourceHashes(hashes);
        foreach (var actor in group.transform.Cast<Transform>())
            ValidateStyledActor(actor.gameObject, actor.GetComponent<FishmanAI>() ? "Fishman" : actor.GetComponent<SmilingMushroomAI>() ? "Mushroom" : "BishopKnight");
        AdventureTrioInstaller.ValidateScene(scene);
        Need(knight.GetComponent<Health>().MaxHealth == 240 && !new SerializedObject(knight.GetComponent<Health>()).FindProperty("maxHealth").prefabOverride,
            "240 HP is inherited from the existing Knight prefab");
        Need(Find<BishopKnightAI>(scene).Length == 1, "Exactly one territorial Knight miniboss exists");

        // No scene save occurs until every placement, appearance and preservation
        // assertion above has succeeded. Generated-only assets can be regenerated.
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Need(EditorSceneManager.SaveScene(scene), "Validated styled trio scene saved");
        File.WriteAllText(Output + "/mesh-uv-palette-audit.csv", audit.ToString());
        File.WriteAllText(Output + "/material-mapping.csv", mapping.ToString());
        File.WriteAllText(Output + "/placements.csv", Placements(group));
        File.WriteAllLines(Output + "/source-sha256.txt", hashes.OrderBy(p => p.Key).Select(p => p.Value + "  " + p.Key));
        File.WriteAllLines(Output + "/preserved-19-actors.txt", protectedActors.Select(a =>
            a.health.name + " | " + a.position.ToString("F5") + " | scale=" + a.scale.ToString("F5") + " | hp=" + a.hp + " | " + a.prefab));
        File.WriteAllLines(Output + "/generated-manifest.txt", AssetDatabase.GetAllAssetPaths()
            .Where(p => p == Folder || p.StartsWith(Folder + "/", StringComparison.Ordinal)).OrderBy(p => p));
        File.WriteAllText(Output + "/README.md", Report(knightBefore, knight.transform.position, previousSiteReason, hashes.Count));
        Debug.Log("ADVENTURE_TRIO STYLES PASS: approved per-species palettes; 3.15m/240HP Silver Sentinel; 19 other actors and source hashes preserved. " + Output);
    }

    public static void Validate()
    {
        Guard(); var scene = SceneManager.GetSceneByPath(AdventureTrioInstaller.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) { RequireCleanScenes(); scene = EditorSceneManager.OpenScene(AdventureTrioInstaller.ScenePath); }
        var group = scene.GetRootGameObjects().Single(g => g.name == Marker);
        foreach (var actor in group.transform.Cast<Transform>())
            ValidateStyledActor(actor.gameObject, actor.GetComponent<FishmanAI>() ? "Fishman" : actor.GetComponent<SmilingMushroomAI>() ? "Mushroom" : "BishopKnight");
        AdventureTrioInstaller.ValidateScene(scene);
    }

    // Optional asset-only diagnostic. Does not load/save a scene, modify a mesh,
    // change an importer or instantiate a material. It evaluates the minimum-AA
    // support of the current shader formula on actual UV0/UV3 triangle samples.
    public static void AuditFishTechCoverage()
    {
        Guard(); Directory.CreateDirectory(Output);
        var report = new StringBuilder("renderer,vertices,triangles,maskVertices,traceVertices,maskTriangles,traceTriangles,maskSamples,traceSamples,maxTrace,maskMinX,maskMinY,maskMinZ,maskMaxX,maskMaxY,maskMaxZ\n");
        var points = new StringBuilder("renderer,vertex,uvX,uvY,qX,qY,qZ,maskR,maskG,maskB,trace\n");
        foreach (string name in new[] { "Fishman", "Harpoon" })
        {
            bool weapon = name == "Harpoon";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Fishman/" + name + "_BindPoseTech.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(weapon ? Role.FishWeapon : Role.FishBody));
            Need(mesh && material, "Generate the styled Fishman before the UV3 coverage audit");
            var mask = Decode(AssetDatabase.GetAssetPath(material.GetTexture("_TechMask")));
            try
            {
                var uv = mesh.uv; var coordinates = new List<Vector4>(); mesh.GetUVs(2, coordinates);
                Need(uv.Length == mesh.vertexCount && coordinates.Count == uv.Length, "Exact Fishman UV0/UV3 diagnostic vertex correspondence");
                int maskVertices = 0, traceVertices = 0, maskTriangles = 0, traceTriangles = 0, maskSamples = 0, traceSamples = 0;
                float maximum = 0; Vector3 minimum = Vector3.one * float.PositiveInfinity, maximumPoint = Vector3.one * float.NegativeInfinity;
                for (int i = 0; i < uv.Length; i++)
                {
                    Color region = TechMaskAt(mask, uv[i]); Vector3 q = coordinates[i];
                    if ((weapon ? region.b : Mathf.Max(region.r, region.g)) <= .5f) continue;
                    float trace = TechTrace(q, region, weapon); maskVertices++;
                    if (trace > .01f) traceVertices++;
                    minimum = Vector3.Min(minimum, q); maximumPoint = Vector3.Max(maximumPoint, q);
                    points.AppendLine(FormattableString.Invariant($"{name},{i},{uv[i].x:F6},{uv[i].y:F6},{q.x:F6},{q.y:F6},{q.z:F6},{region.r:F3},{region.g:F3},{region.b:F3},{trace:F6}"));
                }
                int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2]; bool hasMask = false, hasTrace = false;
                    for (int row = 0; row <= 8; row++) for (int column = 0; column <= 8 - row; column++)
                    {
                        float wa = row / 8f, wb = column / 8f, wc = 1 - wa - wb;
                        Color region = TechMaskAt(mask, uv[a] * wa + uv[b] * wb + uv[c] * wc);
                        if ((weapon ? region.b : Mathf.Max(region.r, region.g)) <= .5f) continue;
                        Vector3 q = coordinates[a] * wa + coordinates[b] * wb + coordinates[c] * wc;
                        float trace = TechTrace(q, region, weapon); hasMask = true; maskSamples++; maximum = Mathf.Max(maximum, trace);
                        if (trace > .01f) { hasTrace = true; traceSamples++; }
                    }
                    if (hasMask) maskTriangles++; if (hasTrace) traceTriangles++;
                }
                report.AppendLine(FormattableString.Invariant($"{name},{uv.Length},{triangles.Length / 3},{maskVertices},{traceVertices},{maskTriangles},{traceTriangles},{maskSamples},{traceSamples},{maximum:F6},{minimum.x:F6},{minimum.y:F6},{minimum.z:F6},{maximumPoint.x:F6},{maximumPoint.y:F6},{maximumPoint.z:F6}"));
            }
            finally { Object.DestroyImmediate(mask); }
        }
        File.WriteAllText(Output + "/tech-uv3-coverage.csv", report.ToString());
        File.WriteAllText(Output + "/tech-uv3-mask-vertices.csv", points.ToString());
        Debug.Log("ADVENTURE_TRIO TECH UV3 COVERAGE (read-only assets; eight-subdivision barycentric samples; derivative AA may widen actual traces):\n" + report);
    }

    static Color TechMaskAt(Texture2D mask, Vector2 uv) => mask.GetPixel(Mathf.Clamp(Mathf.FloorToInt(uv.x * mask.width), 0, mask.width - 1),
        Mathf.Clamp(Mathf.FloorToInt(uv.y * mask.height), 0, mask.height - 1));
    static float TechEdge(float from, float to, float value)
    { float t = Mathf.InverseLerp(from, to, value); return t * t * (3 - 2 * t); }
    static float TechLine(float distance, float width) => 1 - TechEdge(width, width + .0015f, distance);
    static float TechTrace(Vector3 q, Color mask, bool weapon)
    {
        if (weapon) return TechLine(Mathf.Abs(q.y - (.982f - .970f * q.x)), .008f) *
            (1 - TechEdge(.035f, .065f, Mathf.Abs(q.z - (1.015f * q.x - .017f)))) *
            TechEdge(.25f, .30f, q.x) * (1 - TechEdge(.90f, .95f, q.x)) * mask.b;
        // Native body renderer is rotated -90 degrees about X: original Z is
        // body height, while original Y is depth. The harpoon has its own frame.
        q = new Vector3(q.x, q.z, q.y);
        float side = Mathf.Abs(q.x - .5f);
        float shoulder = TechLine(Mathf.Abs(q.y - (.64f + .16f * side)), .009f) * TechEdge(.13f, .18f, side) * (1 - TechEdge(.32f, .38f, side));
        float cheek = TechLine(Mathf.Abs(q.y - (.82f - .24f * side)), .007f) * TechEdge(.025f, .07f, side) * (1 - TechEdge(.16f, .20f, side));
        float fin = TechLine(Mathf.Abs(q.y - .6f), .007f) * TechEdge(.08f, .19f, side);
        return Mathf.Clamp01((shoulder + cheek) * mask.r + fin * mask.g * .65f);
    }

    internal static void ValidateStyledActor(GameObject actor, string kind)
    {
        var style = actor.GetComponent<AdventureCreatureStyle>();
        Need(style && style.species == Species(kind), "Species style controller is assigned: " + actor.name);
        var visual = actor.GetComponent<AdventureCreatureAI>().animator.gameObject;
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        Need(style.targetRenderers != null && style.targetRenderers.Length == renderers.Length &&
            style.targetRenderers.Distinct().Count() == renderers.Length && renderers.All(r => style.targetRenderers.Contains(r)),
            "Only all original visual renderers are assigned to the style controller: " + actor.name);
        int index = Array.IndexOf(Kinds, kind);
        var native = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/Prefab/Characters/" + NativeNames[index] + ".prefab");
        var sourceRenderers = native.GetComponentsInChildren<Renderer>(true);
        Need(renderers.Length == sourceRenderers.Length, "Original renderer count retained: " + actor.name);
        // A deliberate, positively identified Knight remodel has its own mesh,
        // multi-material and native-rig validator. Do not relax the vendor-mesh
        // identity checks for Fishman, Mushroom or a partially installed Knight.
        bool remodeledKnight = kind == "BishopKnight" && IsRemodeledKnight(actor);
        if (remodeledKnight)
        {
            Need(renderers.Where(r=>r is SkinnedMeshRenderer).All(r => RendererMesh(r) && AssetDatabase.GetAssetPath(RendererMesh(r))
                .StartsWith(SentinelFolder + "/", StringComparison.Ordinal)), "Knight armor uses independent assets; shield and spear are validated against exact originals");
            SilverSentinelRemodelInstaller.ValidateActor(actor);
        }
        foreach (var renderer in renderers)
        {
            if (remodeledKnight) continue;
            Role role = RendererRole(kind, renderer);
            string expected = MaterialPath(role);
            Need(renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials.All(m => m && AssetDatabase.GetAssetPath(m) == expected && m.shader.name == ShaderName),
                "Exact styled material path and shader: " + kind + "/" + renderer.name + " -> " + expected);
            var sourceRenderer = sourceRenderers.Single(r => r.name == renderer.name && r.GetType() == renderer.GetType());
            Mesh original = RendererMesh(sourceRenderer), actual = RendererMesh(renderer);
            Need(original && actual, "Original and styled renderer mesh references exist");
            if (kind == "Fishman")
            {
                Need(AssetDatabase.GetAssetPath(actual).StartsWith(Folder + "/Fishman/", StringComparison.Ordinal), "Fish mesh copy is local to Styled");
                VerifyMesh(original, actual);
                var uv3 = new List<Vector4>(); actual.GetUVs(2, uv3);
                Need(uv3.Count == actual.vertexCount && uv3.All(v => v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1 && v.z >= 0 && v.z <= 1),
                    "Fish UV3 contains normalized original bind-pose coordinates");
            }
            else Need(original == actual, "Original mesh reference retained: " + kind + "/" + renderer.name);
        }
        if (kind == "BishopKnight")
        {
            var shape = actor.GetComponent<CapsuleCollider>(); var ai = actor.GetComponent<BishopKnightAI>();
            Need(actor.transform.localScale == Vector3.one && Mathf.Abs(shape.height - KnightHeight) < .001f &&
                Mathf.Abs(shape.radius - KnightRadius) < .001f && shape.center == new Vector3(0, 1.575f, 0),
                "Knight physical root stays unit scale with the enlarged body capsule");
            Need(actor.GetComponent<Health>().MaxHealth == 240 && ai.IsTerritoryBoss && ai.noticeRange == 9f && ai.leashRange == 11f && ai.walkSpeed == 1.6f,
                "Knight miniboss health and territorial movement configured");
            Bounds body = BakedBodyBounds(visual, actor.transform); float height = body.size.y;
            Need(Mathf.Abs(height - KnightHeight) < (remodeledKnight ? .06f : .025f),
                "Knight " + (remodeledKnight ? "remodeled" : "original native") + " body is fitted once to 3.15m: " + height);
            Need(Mathf.Abs(body.min.y) < .025f && Flat(body.center) < .025f,
                "Knight native visual remains centred and grounded on its physical root on every build");
        }
    }

    internal static bool IsRemodeledKnight(GameObject actor)
    {
        var ai = actor.GetComponent<BishopKnightAI>();
        return ai && ai.animator && ai.animator.GetComponentsInChildren<Renderer>(true).Any(r =>
            RendererMesh(r) && AssetDatabase.GetAssetPath(RendererMesh(r)).StartsWith(SentinelFolder + "/", StringComparison.Ordinal));
    }

    static void BuildPrefab(string kind, string prefabName, GameObject native, Shader shader, Texture2D atlas, Texture2D originalEmission,
        StringBuilder audit, StringBuilder mapping)
    {
        string prefabPath = AdventureTrioInstaller.Folder + "/" + prefabName + ".prefab";
        Need(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), "Existing generated prefab must be retained: " + prefabPath);
        string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            // Re-applying the older palette installer must never silently
            // replace the approved remesh or its dedicated animation copies.
            if (kind == "BishopKnight" && IsRemodeledKnight(root))
            {
                ValidateStyledActor(root, kind);
                Debug.Log("ADVENTURE_TRIO STYLES: preserved the independently validated Silver Sentinel remodel.");
                return;
            }
            var ai = root.GetComponent<AdventureCreatureAI>(); Need(ai && ai.animator, "Existing creature native animator is assigned");
            var visual = ai.animator.gameObject;
            var nativeRenderers = native.GetComponentsInChildren<Renderer>(true);
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            var sourceByRenderer = renderers.ToDictionary(r => r, r => nativeRenderers.Single(s => s.name == r.name && s.GetType() == r.GetType()));
            var eyeCells = kind == "Fishman" ? FishEyeCells(nativeRenderers.OfType<SkinnedMeshRenderer>().Single(), atlas, originalEmission) : new HashSet<Vector2Int>();
            var materials = new Dictionary<Role, Material>();
            foreach (var renderer in renderers)
            {
                var sourceRenderer = sourceByRenderer[renderer]; var sourceMesh = RendererMesh(sourceRenderer);
                Need(sourceMesh, "Native mesh found for " + renderer.name);
                Role role = RendererRole(kind, renderer);
                if (!materials.TryGetValue(role, out var material))
                {
                    TextureSet textures = MakeTextures(role, atlas, originalEmission, eyeCells);
                    material = MakeMaterial(role, shader, textures); materials.Add(role, material);
                }
                AuditMesh(kind, renderer.name, sourceMesh, atlas, eyeCells, audit);
                mapping.AppendLine(Csv(kind) + "," + Csv(renderer.name) + "," + Csv(string.Join(";", sourceRenderer.sharedMaterials.Select(AssetDatabase.GetAssetPath))) + "," +
                    Csv(MaterialPath(role)) + "," + Csv(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"))) + "," +
                    Csv(AssetDatabase.GetAssetPath(material.GetTexture("_EmissionMap"))) + "," + Csv(AssetDatabase.GetAssetPath(material.GetTexture("_TechMask"))));
                renderer.sharedMaterials = Enumerable.Repeat(material, sourceRenderer.sharedMaterials.Length).ToArray();
                if (kind == "Fishman") SetRendererMesh(renderer, CopyFishMesh(sourceMesh, renderer.name, role == Role.FishWeapon));
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                if (renderer.GetComponent<MeshFilter>()) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.GetComponent<MeshFilter>());
            }
            var style = root.GetComponent<AdventureCreatureStyle>() ?? root.AddComponent<AdventureCreatureStyle>();
            style.species = Species(kind); style.targetRenderers = renderers;
            if (kind == "BishopKnight")
            {
                Need(root.transform.localScale == Vector3.one, "Knight physical root must already be unit scale");
                AdventureTrioInstaller.FitStyledKnight(visual);
                var shape = root.GetComponent<CapsuleCollider>(); shape.height = KnightHeight; shape.radius = KnightRadius; shape.center = Vector3.up * 1.575f;
                var health = new SerializedObject(root.GetComponent<Health>()); health.FindProperty("maxHealth").intValue = 240; health.ApplyModifiedPropertiesWithoutUndo();
                ai.noticeRange = 9f; ai.leashRange = 11f; ai.walkSpeed = 1.6f;
                var boss = new SerializedObject(ai);
                SetBool(boss, "isTerritoryBoss", true);
                SetFloat(boss, "guardEnterRange", 3.2f); SetFloat(boss, "approachStop", 2.9f);
                boss.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
            }
            EditorUtility.SetDirty(style);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved); Need(saved, "Styled existing prefab saved");
            Need(AssetDatabase.AssetPathToGUID(prefabPath) == prefabGuid, "Existing generated prefab GUID preserved");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static HashSet<Vector2Int> FishEyeCells(SkinnedMeshRenderer skin, Texture2D atlas, Texture2D emission)
    {
        var mesh = skin.sharedMesh; var uv = mesh.uv; var weights = mesh.boneWeights;
        var totals = new Dictionary<Vector2Int, int>(); var eyes = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < uv.Length; i++)
        {
            var cell = Cell(uv[i]); Count(totals, cell);
            var weight = weights[i];
            float eyeWeight = EyeBone(skin, weight.boneIndex0) ? weight.weight0 : 0;
            eyeWeight += EyeBone(skin, weight.boneIndex1) ? weight.weight1 : 0;
            eyeWeight += EyeBone(skin, weight.boneIndex2) ? weight.weight2 : 0;
            eyeWeight += EyeBone(skin, weight.boneIndex3) ? weight.weight3 : 0;
            if (eyeWeight > .55f) Count(eyes, cell);
        }
        var selected = new HashSet<Vector2Int>(eyes.Where(p => p.Value >= 3 && (float)p.Value / totals[p.Key] >= .55f).Select(p => p.Key));
        // Fish does not use the vendor atlas's two emissive cells. Bone occupancy
        // identifies dedicated eye UV cells without painting the whole skin cyan.
        Need(selected.Count > 0, "Fishman has dedicated eye palette cells identified from original eye-bone weights");
        Debug.Log("ADVENTURE_TRIO FISH EYE CELLS (top-origin): " + string.Join(";", selected.OrderBy(c => c.y).ThenBy(c => c.x)));
        return selected;
    }
    static bool EyeBone(SkinnedMeshRenderer skin, int index)
    {
        if (index < 0 || index >= skin.bones.Length || !skin.bones[index]) return false;
        string name = skin.bones[index].name;
        return name.Equals("fishman_eyeLeft", StringComparison.OrdinalIgnoreCase) || name.Equals("fishman_eyeRight", StringComparison.OrdinalIgnoreCase);
    }

    static TextureSet MakeTextures(Role role, Texture2D source, Texture2D emission, HashSet<Vector2Int> eyeCells)
    {
        string folder = RoleFolder(role); EnsureFolder(folder);
        string stem = RoleStem(role); int width = source.width, height = source.height;
        var color = new Color32[width * height]; var glow = new Color32[color.Length]; var tech = new Color32[color.Length];
        var pixels = source.GetPixels32(); var sourceGlow = emission.GetPixels32();
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x; Color original = pixels[i]; var semantic = Classify(original);
            var cell = new Vector2Int(x / 32, 15 - y / 32);
            color[i] = Remap(role, original, semantic, cell);
            glow[i] = new Color32(0, 0, 0, 255); tech[i] = new Color32(0, 0, 0, 255);
            if (role == Role.FishBody)
            {
                if (semantic == Palette.Blue && !eyeCells.Contains(cell)) tech[i] = new Color32(255, 0, 0, 255);
                if (semantic == Palette.Green && !eyeCells.Contains(cell)) tech[i] = new Color32(0, 255, 0, 255);
                if (eyeCells.Contains(cell)) glow[i] = new Color32(255, 255, 255, 255);
            }
            else if (role == Role.FishWeapon)
            {
                // The atlas permits only the shaft's original brown palette. The
                // shader's bind-pose UV3 pattern limits this to a tiny cyan channel.
                if (semantic == Palette.Brown || cell.y == 1 && cell.x >= 13)
                    tech[i] = new Color32(0, 0, 255, 255);
            }
            else if (role == Role.KnightBody && Math.Max(sourceGlow[i].r, Math.Max(sourceGlow[i].g, sourceGlow[i].b)) > 18)
                glow[i] = new Color32(255, 255, 255, 255);
        }
        return new TextureSet {
            albedo = WriteTexture(folder + "/" + stem + "_Albedo.png", width, height, color, true),
            emission = WriteTexture(folder + "/" + stem + "_Emission.png", width, height, glow, false),
            tech = WriteTexture(folder + "/" + stem + "_TechMask.png", width, height, tech, false) };
    }

    static Color Remap(Role role, Color source, Palette semantic, Vector2Int cell)
    {
        Color.RGBToHSV(source, out _, out _, out float value);
        if (role == Role.Mushroom)
        {
            if (semantic == Palette.Red) return Shade(Hex(0xE96F5B), value, .28f, .98f);
            if (semantic == Palette.Brown || semantic == Palette.Gold) return Shade(Hex(0xF4E6C8), value, .60f, 1f);
            if (semantic == Palette.Grey && value > .55f) return Hex(0xFFF1D4);
            if (semantic == Palette.Black || semantic == Palette.Grey) return Hex(0x3B2D30);
            return Hex(0xF3E7CF);
        }
        if (role == Role.FishWeapon)
        {
            if (semantic == Palette.Brown || semantic == Palette.Gold) return Shade(Hex(0x5C6C76), value, .34f, .91f);
            if (semantic == Palette.Black || semantic == Palette.Grey) return Shade(Hex(0xB4C7CD), value, .16f, .98f);
            return Shade(Hex(0x92A8B1), value, .28f, .97f);
        }
        if (role == Role.FishBody)
        {
            if (semantic == Palette.Blue) return Shade(Hex(0x27BFA9), value, .22f, .98f);
            if (semantic == Palette.Green)
            {
                // Preserve native fin shading; pale tips occupy only the brightest
                // original green swatches, leaving a clearly navy fin silhouette.
                if (value > .49f) return Color.Lerp(Hex(0x338AA5), Hex(0xA3E7E0), Mathf.InverseLerp(.49f, .67f, value));
                return Shade(Hex(0x243E66), value, .08f, .5f);
            }
            if (semantic == Palette.Brown || semantic == Palette.Gold) return Shade(Hex(0xF1EBD6), value, .40f, .98f);
            if (semantic == Palette.Black || semantic == Palette.Grey) return value < .12f ? Hex(0x17343F) : Shade(Hex(0xE0EBE4), value, .2f, .95f);
            return Shade(Hex(0x279F97), value, .24f, .98f);
        }
        // C follows the actual native UV occupancy, rather than assuming every
        // grey swatch is armour: dark 4/15 supplies the skirt and undersuit, while
        // the brighter plate swatches must retain their silver response.
        if (role == Role.KnightShield)
        {
            if (cell == new Vector2Int(4, 14)) return Shade(Hex(0xDBAE58), value, .03f, .6f);
            if (semantic == Palette.Gold || semantic == Palette.Brown || semantic == Palette.Red)
                return Shade(Hex(0xDBAE58),value,.16f,.98f);
            if (semantic == Palette.Black) return Hex(0x101923);
            return Shade(Hex(0x172B4B),value,.05f,.75f);
        }
        if (role == Role.KnightBody)
        {
            if (cell == new Vector2Int(4, 15)) return Shade(Hex(0x303B68), value, .08f, .18f);
            if (cell == new Vector2Int(8, 2) || cell == new Vector2Int(9, 2) ||
                cell == new Vector2Int(4, 3) || cell == new Vector2Int(5, 3))
                return Shade(Hex(0x3B477A), value, .05f, .95f);
            if (cell.y == 5 && cell.x >= 12 && cell.x <= 15)
                return Shade(Hex(0xAF9353), value, .16f, .98f);
        }
        // Other dedicated metal plates stay silver; native coloured insets stay
        // indigo, and the spear's original warm ornament palette stays old gold.
        if (role == Role.KnightShield && semantic == Palette.Blue) return Shade(Hex(0x434A83), value, .08f, .95f);
        if (semantic == Palette.Purple) return Shade(Hex(0x434776), value, .06f, .9f);
        if (semantic == Palette.Gold || semantic == Palette.Brown || semantic == Palette.Red) return Shade(Hex(0xAF9353), value, .16f, .98f);
        if (semantic == Palette.Blue) return Shade(Hex(0xC3CDD0), value, .05f, .98f);
        if (semantic == Palette.Black) return Hex(0x26313E);
        return Shade(Hex(0xCBD2D3), value, .03f, .98f);
    }
    static Color Shade(Color target, float value, float from, float to)
    {
        float t = Mathf.InverseLerp(from, to, value);
        // The source atlas bakes very dark PBR values. Keep its relative variation
        // while lifting it into the readable, illustrated palette approved here.
        return target * Mathf.Lerp(.76f, 1.06f, t) + new Color(0, 0, 0, 1 - Mathf.Lerp(.76f, 1.06f, t));
    }
    static Palette Classify(Color color)
    {
        Color.RGBToHSV(color, out float hue, out float saturation, out float value);
        if (saturation < .12f) return value < .09f ? Palette.Black : Palette.Grey;
        if (hue < .035f || hue > .95f) return Palette.Red;
        if (hue < .16f) return hue > .085f && value > .48f ? Palette.Gold : Palette.Brown;
        if (hue < .48f) return Palette.Green;
        if (hue < .72f) return Palette.Blue;
        return Palette.Purple;
    }

    static Material MakeMaterial(Role role, Shader shader, TextureSet textures)
    {
        string path = MaterialPath(role); var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader; material.name = RoleStem(role);
        material.SetTexture("_BaseMap", textures.albedo); material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_EmissionMap", textures.emission); material.SetColor("_EmissionColor", Hex(0x71E9EA));
        material.SetTexture("_TechMask", textures.tech);
        bool mushroom = role == Role.Mushroom, fish = role == Role.FishBody, weapon = role == Role.FishWeapon;
        material.SetColor("_ShadowColor", mushroom ? new Color(.62f, .47f, .43f, 1) : fish ? new Color(.38f, .63f, .67f, 1) : new Color(.45f, .49f, .61f, 1));
        material.SetColor("_DeepShadowColor", mushroom ? new Color(.36f, .25f, .26f, 1) : new Color(.22f, .28f, .38f, 1));
        Set(material, "_ShadeThreshold", .36f); Set(material, "_DeepShadeThreshold", -.16f); Set(material, "_ShadeSoftness", mushroom ? .16f : .055f);
        Set(material, "_FormStrength", mushroom ? .36f : .22f); Set(material, "_AmbientStrength", mushroom ? .46f : .42f);
        Set(material, "_Metallic", mushroom || fish ? 0f : weapon ? .62f : .33f);
        Set(material, "_Smoothness", mushroom ? .13f : fish ? .29f : .37f);
        Set(material, "_SpecularStrength", mushroom ? .035f : fish ? .12f : .24f);
        Set(material, "_LightColorPreservation", .9f); Set(material, "_ShadowLightSaturation", .10f);
        Set(material, "_AdditionalLightStrength", .48f); Set(material, "_GrazingShade", .11f);
        Set(material, "_RimStrength", mushroom ? .025f : .055f); Set(material, "_RimPower", 4.5f);
        material.SetColor("_RimColor", fish ? Hex(0x75D6D2) : Hex(0xA4B2C7));
        Set(material, "_EmissionStrength", mushroom ? 0f : fish ? .65f : role == Role.KnightBody ? .8f : 0f);
        Set(material, "_TechStrength", fish ? .65f : weapon ? .8f : 0f);
        Set(material, "_TechWeapon", weapon ? 1f : 0f); Set(material, "_StyleClock", 0); Set(material, "_TechPulse", 0); Set(material, "_StyleLive", 1);
        Set(material, "_BumpScale", 0); Set(material, "_OcclusionStrength", 0); Set(material, "_Heat", 0);
        if(role==Role.KnightShield)
        {
            material.SetColor("_ShadowColor",new Color(.40f,.45f,.54f,1));
            material.SetColor("_DeepShadowColor",new Color(.16f,.20f,.29f,1));
            material.SetColor("_SpecularColor",Hex(0xF6E8CF));
            material.SetColor("_RimColor",Hex(0xBCCADB));
            Set(material,"_ShadeSoftness",.24f);Set(material,"_FormStrength",.62f);
            Set(material,"_Metallic",.60f);Set(material,"_Smoothness",.43f);
            Set(material,"_SpecularStrength",.62f);Set(material,"_AmbientStrength",.32f);
            Set(material,"_RimStrength",.012f);Set(material,"_GrazingShade",.10f);
        }
        EditorUtility.SetDirty(material); return material;
    }
    static void Set(Material material, string property, float value)
    { Need(material.HasProperty(property), "Approved toon shader property exists: " + property); material.SetFloat(property, value); }
    static void SetFloat(SerializedObject data, string property, float value)
    { var field = data.FindProperty(property); Need(field != null, "Configured serialized float exists: " + property); field.floatValue = value; }
    static void SetBool(SerializedObject data, string property, bool value)
    { var field = data.FindProperty(property); Need(field != null, "Configured serialized bool exists: " + property); field.boolValue = value; }

    static Mesh CopyFishMesh(Mesh source, string rendererName, bool weapon)
    {
        string path = Folder + "/Fishman/" + rendererName + "_BindPoseTech.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh) { mesh = Object.Instantiate(source); AssetDatabase.CreateAsset(mesh, path); }
        else EditorUtility.CopySerialized(source, mesh);
        mesh.name = rendererName + " original geometry with bind pose tech UV3";
        Bounds bounds = source.bounds; var vertices = source.vertices;
        int major = bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z ? 0 : bounds.size.y >= bounds.size.z ? 1 : 2;
        int second = (major + 1) % 3, third = (major + 2) % 3;
        var coordinates = new List<Vector4>(vertices.Length);
        foreach (var vertex in vertices)
        {
            var p = new Vector3(Normalize(vertex.x, bounds.min.x, bounds.max.x), Normalize(vertex.y, bounds.min.y, bounds.max.y), Normalize(vertex.z, bounds.min.z, bounds.max.z));
            coordinates.Add(weapon ? new Vector4(p[major], p[second], p[third], 0) : new Vector4(p.x, p.y, p.z, 0));
        }
        mesh.SetUVs(2, coordinates); EditorUtility.SetDirty(mesh); VerifyMesh(source, mesh); return mesh;
    }
    static float Normalize(float value, float min, float max) => max - min < .000001f ? .5f : Mathf.Clamp01((value - min) / (max - min));
    static void VerifyMesh(Mesh original, Mesh actual)
    {
        Need(original.vertexCount == actual.vertexCount && original.vertices.SequenceEqual(actual.vertices) &&
            original.normals.SequenceEqual(actual.normals) && original.tangents.SequenceEqual(actual.tangents) && original.uv.SequenceEqual(actual.uv) &&
            original.bindposes.SequenceEqual(actual.bindposes) && original.boneWeights.SequenceEqual(actual.boneWeights) && original.subMeshCount == actual.subMeshCount,
            "Fish clone retains original vertex geometry, normals, tangents, UV0, rig bind poses and weights");
        for (int i = 0; i < original.subMeshCount; i++)
            Need(original.GetTopology(i) == actual.GetTopology(i) && original.GetIndices(i).SequenceEqual(actual.GetIndices(i)), "Original Fish submesh indices/topology retained");
    }

    static void AuditMesh(string kind, string renderer, Mesh mesh, Texture2D atlas, HashSet<Vector2Int> eyes, StringBuilder audit)
    {
        var cells = mesh.uv.GroupBy(Cell).OrderBy(g => g.Key.y).ThenBy(g => g.Key.x);
        foreach (var cell in cells)
        {
            Color color = atlas.GetPixel(cell.Key.x * 32 + 16, (15 - cell.Key.y) * 32 + 16);
            audit.AppendLine(Csv(kind) + "," + Csv(renderer) + "," + Csv(mesh.name) + "," + Csv(AssetDatabase.GetAssetPath(mesh)) + "," +
                mesh.vertexCount + "," + mesh.triangles.Length / 3 + "," + cell.Key.x + "," + cell.Key.y + "," + cell.Count() + "," +
                Csv("#" + ColorUtility.ToHtmlStringRGB(color)) + "," + Classify(color) + "," + eyes.Contains(cell.Key));
        }
    }
    static Vector2Int Cell(Vector2 uv) => new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * 16), 0, 15),
        15 - Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * 16), 0, 15));
    static void Count(Dictionary<Vector2Int, int> values, Vector2Int key) { values[key] = values.TryGetValue(key, out int n) ? n + 1 : 1; }
    static Texture2D Decode(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        Need(texture.LoadImage(File.ReadAllBytes(path), false), "Decode original PNG independently of its shared importer: " + path); return texture;
    }
    static Texture2D WriteTexture(string path, int width, int height, Color32[] pixels, bool srgb)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, !srgb);
        try { texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { Object.DestroyImmediate(texture); }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default; importer.sRGBTexture = srgb; importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = false; importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 512; importer.isReadable = false;
        importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Role RendererRole(string kind, Renderer renderer)
    {
        if (kind == "Mushroom") return Role.Mushroom;
        if (kind == "Fishman") return renderer.name.Equals("Harpoon", StringComparison.OrdinalIgnoreCase) ? Role.FishWeapon : Role.FishBody;
        if (renderer.name.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0) return Role.KnightShield;
        if (renderer.name.IndexOf("Spear", StringComparison.OrdinalIgnoreCase) >= 0) return Role.KnightSpear;
        return Role.KnightBody;
    }
    static string RoleFolder(Role role) => Folder + "/" + (role == Role.Mushroom ? "Mushroom" : role == Role.FishBody || role == Role.FishWeapon ? "Fishman" : "Knight");
    static string RoleStem(Role role) => role == Role.Mushroom ? "MushroomSoft" : role == Role.FishBody ? "FishmanBody" : role == Role.FishWeapon ? "FishmanHarpoon" :
        role == Role.KnightShield ? "KnightShield" : role == Role.KnightSpear ? "KnightSpear" : "KnightArmor";
    static string MaterialPath(Role role) => RoleFolder(role) + "/" + RoleStem(role) + ".mat";
    static AdventureCreatureStyle.Species Species(string kind) => kind == "Mushroom" ? AdventureCreatureStyle.Species.Mushroom :
        kind == "Fishman" ? AdventureCreatureStyle.Species.Fishman : AdventureCreatureStyle.Species.Knight;
    static Mesh RendererMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
        var filter = renderer.GetComponent<MeshFilter>();
        return filter ? filter.sharedMesh : null;
    }
    static void SetRendererMesh(Renderer renderer, Mesh mesh)
    { if (renderer is SkinnedMeshRenderer skin) skin.sharedMesh = mesh; else renderer.GetComponent<MeshFilter>().sharedMesh = mesh; }
    static Color Hex(uint value) => new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1);

    static Bounds BakedBodyBounds(GameObject visual, Transform reference = null)
    {
        bool found = false; Bounds bounds = default; var mesh = new Mesh();
        try
        {
            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.BakeMesh(mesh, true);
                foreach (var vertex in mesh.vertices)
                {
                    var p = skin.transform.TransformPoint(vertex);
                    if (reference) p = reference.InverseTransformPoint(p);
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                }
                mesh.Clear();
            }
        }
        finally { Object.DestroyImmediate(mesh); }
        Need(found, "Native skinned body exists"); return bounds;
    }
    static string Appearance(GameObject actor) => string.Join("\n", actor.GetComponentsInChildren<Renderer>(true).Select(r =>
        HierarchyPath(r.transform, actor.transform) + "|" + r.GetType().Name + "|" + AssetDatabase.GetAssetPath(RendererMesh(r)) + "|" +
        string.Join(";", r.sharedMaterials.Select(AssetDatabase.GetAssetPath))).OrderBy(s => s));
    static string HierarchyPath(Transform child, Transform root)
    { string path = child.name; while (child.parent && child != root) { child = child.parent; path = child.name + "/" + path; } return path; }
    static void VerifyProtectedActors(IEnumerable<ActorSnapshot> snapshots)
    {
        foreach (var actor in snapshots)
            Need(actor.health && actor.health.transform.position == actor.position && actor.health.transform.rotation == actor.rotation &&
                actor.health.transform.localScale == actor.scale && actor.health.MaxHealth == actor.hp &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(actor.health.gameObject) == actor.prefab &&
                (!actor.protectAppearance || Appearance(actor.health.gameObject) == actor.appearance),
                "Protected encounter placement, health, scale, source and original appearance preserved: " + (actor.health ? actor.health.name : "missing actor"));
    }
    static Dictionary<string, string> CaptureSourceHashes(IEnumerable<ActorSnapshot> snapshots)
    {
        // A retry may only extend the first successful preservation record. Never
        // silently establish a new baseline over previously protected files.
        var hashes = ReadProtectedManifest(); VerifySourceHashes(hashes);
        var paths = new HashSet<string>(Directory.GetFiles(Source, "*", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')));
        Need(Directory.Exists(DemonFolder) && File.Exists(DemonPrefab), "Existing DemonKing trial assets are present for source protection");
        paths.UnionWith(Directory.GetFiles(DemonFolder, "*", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')));
        AddSourceDependencies(DemonPrefab, paths);
        foreach (var snapshot in snapshots)
        {
            AddSourceDependencies(snapshot.prefab, paths);
            // Original encounters can override the Scout prefab's appearance in
            // the scene. Protect the actually rendered mesh/material/controller
            // dependency closure, including import metadata, as well as its root
            // prefab; the DemonKing trial is one such authored scene override.
            foreach (var renderer in snapshot.health.GetComponentsInChildren<Renderer>(true))
            {
                AddSourceDependencies(AssetDatabase.GetAssetPath(RendererMesh(renderer)), paths);
                foreach (var material in renderer.sharedMaterials)
                    if (material) AddSourceDependencies(AssetDatabase.GetAssetPath(material), paths);
                if (renderer is SpriteRenderer sprite && sprite.sprite)
                    AddSourceDependencies(AssetDatabase.GetAssetPath(sprite.sprite), paths);
            }
            foreach (var animator in snapshot.health.GetComponentsInChildren<Animator>(true))
            {
                AddSourceDependencies(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController), paths);
                AddSourceDependencies(AssetDatabase.GetAssetPath(animator.avatar), paths);
            }
        }
        foreach (string path in paths.OrderBy(p => p))
            if (!hashes.ContainsKey(path)) hashes.Add(path, HashFile(path));
        return hashes;
    }
    static void AddSourceDependencies(string sourcePath, HashSet<string> paths)
    {
        if (string.IsNullOrEmpty(sourcePath)) return;
        foreach (string path in AssetDatabase.GetDependencies(sourcePath, true))
        {
            if (File.Exists(path)) paths.Add(path);
            if (File.Exists(path + ".meta")) paths.Add(path + ".meta");
        }
    }
    static Dictionary<string, string> ReadProtectedManifest()
    {
        var hashes = new Dictionary<string, string>(); string path = Output + "/source-sha256.txt";
        if (!File.Exists(path)) return hashes;
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            Need(line.Length > 66 && line.Substring(0, 64).All(Uri.IsHexDigit) && line.Substring(64, 2) == "  ",
                "Existing source preservation manifest has valid SHA256 records");
            string sourcePath = line.Substring(66);
            Need(!hashes.ContainsKey(sourcePath), "Existing source preservation manifest contains no duplicate paths");
            hashes.Add(sourcePath, line.Substring(0, 64).ToLowerInvariant());
        }
        return hashes;
    }
    static string HashFile(string path)
    { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static void VerifySourceHashes(Dictionary<string, string> hashes)
    { foreach (var entry in hashes) Need(File.Exists(entry.Key) && HashFile(entry.Key) == entry.Value, "Protected source file/hash unchanged: " + entry.Key); }
    static string Placements(GameObject group)
    {
        var csv = new StringBuilder("name,x,y,z,rootScaleX,rootScaleY,rootScaleZ,capsuleHeight,capsuleRadius,health,prefab\n");
        foreach (var actor in group.transform.Cast<Transform>())
        {
            var p = actor.position; var s = actor.localScale; var capsule = actor.GetComponent<CapsuleCollider>();
            csv.AppendLine(FormattableString.Invariant($"{actor.name},{p.x:F5},{p.y:F5},{p.z:F5},{s.x:F5},{s.y:F5},{s.z:F5},{capsule.height:F5},{capsule.radius:F5},{actor.GetComponent<Health>().MaxHealth},{PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(actor.gameObject)}"));
        }
        return csv.ToString();
    }
    static string Report(Vector3 before, Vector3 after, string previousSiteReason, int hashCount) =>
        "# Approved trio styles — 2026-10-21\n\n" +
        "Mushroom: warm coral cap, cream native spots, ivory stem, soft matte light response.\n\n" +
        "Fishman: lake jade/turquoise body, ivory belly, navy fins with pale cyan tips, sparse cyan ancient-tech marks and an independent gunmetal/steel harpoon. " +
        "The RGB tech mask is restricted to original blue skin / green fin / brown weapon palette regions. Fish UV3 stores normalized original bind-pose coordinates, and the harpoon's largest bound axis becomes X. " +
        "Original vertices, triangles, skinning, bind poses and UV0 are checked unchanged. Eye emission uses dedicated native eye-bone UV cells, listed in the audit.\n\n" +
        "Silver Sentinel: C silver/indigo/old-gold palette; native spear, shield, rig and animations retained. Shield grey face cells are indigo and native dark edge cell (4,14) is old gold. Body dark undersuit/skirt cell (4,15) and native skirt cells (8,2), (9,2), (4,3), (5,3) are indigo; bright metal plates remain silver. Native ornament cells (12..15,5) are old gold. Atlas cell rows are counted from the top. " +
        "One territorial miniboss, 3.15m visual body, 240 inherited HP, unit physical root, capsule 3.15m × radius 0.8m, centre (0,1.575,0), notice 9m, leash 11m, walk 1.6m/s. " +
        "Only the visual root is fitted to the target body height. Fitting measures the current native idle body and preserves root scale while sampling, so repeated authoring does not multiply scale.\n\n" +
        "Knight home before: " + before.ToString("F5") + "; after: " + after.ToString("F5") + ". Previous footprint result: " + previousSiteReason + ". " +
        "The existing home is kept if its enlarged capsule is safe; otherwise only supported open points within 8m are eligible, using the existing Scout navigation.\n\n" +
        "The 19 other encounter roots retain position, rotation, scale, HP and source-prefab references. The 17 original encounters also retain renderer/material/mesh references. " +
        hashCount + " protected source/dependency files were SHA-256 checked before and after authoring, including the shared RPG atlas and DemonKing dependencies. No terrain or navigation bake is performed.\n\n" +
        "See material-mapping.csv, mesh-uv-palette-audit.csv (atlas rows counted from top), placements.csv, source-sha256.txt and generated-manifest.txt for exact assets and authored placements.\n";
    static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    static float Flat(Vector3 value) => new Vector2(value.x, value.z).magnitude;
    static T[] Find<T>(Scene scene) where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.gameObject.scene == scene).ToArray();
    static void EnsureFolder(string path)
    { if (AssetDatabase.IsValidFolder(path)) return; EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/')); AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path)); }
    static void Guard()
    {
        Need(Application.dataPath.StartsWith("/private/tmp/adventure-trio-", StringComparison.Ordinal), "Disposable adventure-trio scratch project only");
        Need(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before approved style authoring");
    }
    static void RequireCleanScenes()
    { for (int i = 0; i < SceneManager.sceneCount; i++) Need(!SceneManager.GetSceneAt(i).isDirty, "Existing unsaved scene edits must be retained before style authoring"); }
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
