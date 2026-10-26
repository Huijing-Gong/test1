using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class GelBlobBuilder
{
    public const string Folder = "Assets/Art/Enemies/GelBlob/Generated";
    public const string PrefabPath = Folder + "/GelBlob.prefab";
    [MenuItem("Tools/Enemies/Build And Place Gel Blobs")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first; scene unchanged.");
        Build();
        Scene scene = EditorSceneManager.OpenScene(EnemyNavigationBuilder.ScenePath);
        Physics.SyncTransforms();
        var layout = Object.FindFirstObjectByType<OverworldV2Layout>();
        var navigation = EnemyNavigationWorld.Find(scene);
        if (!layout || !navigation) throw new InvalidOperationException("Actual map and baked navigation required.");
        string[] names = { "GelBlob_LakeShoreNorth", "GelBlob_LakeShoreSouth", "GelBlob_DampForest" };
        var positions = new Vector3[3];
        var probe = new GameObject("Temporary gel placement probe"); SceneManager.MoveGameObjectToScene(probe, scene);
        try
        {
            var ground = new EnemyGroundQuery(probe.transform);
            Vector3[] anchors = { layout.LakePosition + new Vector3(27, 0, 21),
                layout.LakePosition + new Vector3(22, 0, -48), layout.WestRegionPosition + new Vector3(8, 0, -12) };
            for (int i = 0; i < names.Length; i++)
            {
                Transform existing = Find(scene, names[i]);
                if (existing && AboveLake(layout, existing.position) && ground.TryFootprint(existing.position, Quaternion.identity, Vector2.one * (GelBlobAI.DefaultLandingRadius + .1f), 28f, .7f, .8f, out _, true)) positions[i] = existing.position;
                else if (!Placement(scene, layout, navigation, ground, anchors[i], positions, i, names[i], out positions[i]))
                    throw new InvalidOperationException("No safe, supported gel placement for " + names[i] + "; scene unchanged.");
            }
        }
        finally { Object.DestroyImmediate(probe); }
        Transform group = Find(scene, "_GelBlobEncounters");
        if (!group) { group = new GameObject("_GelBlobEncounters").transform; SceneManager.MoveGameObjectToScene(group.gameObject, scene); }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        for (int i = 0; i < names.Length; i++)
        {
            Transform existing = Find(scene, names[i]);
            if (!existing)
            {
                var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                actor.name = names[i]; actor.transform.SetParent(group, false);
                actor.transform.position = positions[i];
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
            }
            else if (Vector3.Distance(existing.position, positions[i]) > .001f)
            { existing.position = positions[i]; PrefabUtility.RecordPrefabInstancePropertyModifications(existing); }
            Debug.Log("Gel placement: " + names[i] + " feet=" + positions[i].ToString("F3"));
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        CheckAssets(); Debug.Log("GEL BUILD PASS; three supported samples; existing five enemy types untouched.");
    }
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Enemies/GelBlob", "Generated");
        Mesh hull = Save(Hull()), sphere = Save(Sphere()), ring = Save(CoreRing());
        string shared = "Assets/Art/Enemies/DomainPrototypes/Generated/";
        Material ceramic = AssetDatabase.LoadAssetAtPath<Material>(shared + "IvoryCeramic.mat");
        Material graphite = AssetDatabase.LoadAssetAtPath<Material>(shared + "Graphite.mat");
        Material moss = AssetDatabase.LoadAssetAtPath<Material>(shared + "LeafGreen.mat");
        Material line = AssetDatabase.LoadAssetAtPath<Material>(shared + "WarningLine.mat");
        Shader gelShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Enemies/GelBlob/GelBlob.shader");
        Shader lensShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Enemies/Shared/Resources/EnemyReadableCore.shader");
        if (!ceramic || !graphite || !moss || !line || !gelShader || !lensShader || ShaderUtil.ShaderHasError(gelShader))
            throw new InvalidOperationException("Required shared assets or gel shader unavailable.");
        Material gelMaterial = Material("TurquoiseGel", gelShader);
        gelMaterial.SetColor("_BaseColor", new Color(.01f, .7f, .7f, .53f));
        Material lensMaterial = Material("CyanGelCore", lensShader);
        lensMaterial.SetColor("_BaseColor", new Color(.02f, .65f, .8f)); lensMaterial.SetColor("_EmissionColor", new Color(.05f, .9f, 1f) * 1.2f);
        GameObject root = new GameObject("Gel Blob");
        try
        {
            var health = root.AddComponent<Health>(); var hp = new SerializedObject(health);
            hp.FindProperty("maxHealth").intValue = 60; hp.FindProperty("team").enumValueIndex = (int)HealthTeam.Enemy; hp.ApplyModifiedPropertiesWithoutUndo();
            var rigid = root.AddComponent<Rigidbody>(); rigid.isKinematic = true; rigid.useGravity = false;
            var collider = root.AddComponent<BoxCollider>(); collider.size = new Vector3(1.18f, .94f, 1.18f) * GelBlobAI.DefaultBodyScale;
            collider.center = Vector3.up * (.03f + collider.size.y * .5f);
            root.AddComponent<NonClimbableSurface>(); root.AddComponent<CombatFeedback>();
            Transform gel = Part(root.transform, "Single translucent gel hull", hull, gelMaterial, Vector3.zero, Vector3.one * GelBlobAI.DefaultBodyScale);
            Part(gel, "Small trapped bubble", sphere, lensMaterial, new Vector3(.2f, .72f, -.08f), Vector3.one * .07f);
            var core = new GameObject("Suspended mechanical core").transform; core.SetParent(root.transform, false);
            core.localPosition = Vector3.up * (.43f * GelBlobAI.DefaultBodyScale); core.localScale = Vector3.one * GelBlobAI.DefaultBodyScale;
            Part(core, "Graphite spherical reactor", sphere, graphite, Vector3.zero, new Vector3(.57f, .42f, .5f));
            Part(core, "Lens mechanical collar", ring, graphite, new Vector3(0, 0, .24f), Vector3.one * .19f);
            Transform lens = Part(core, "Cyan iris", sphere, lensMaterial, new Vector3(0, 0, .275f), new Vector3(.23f, .23f, .055f));
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Transform plate = Part(core, "Ivory cheek " + sign, sphere, ceramic, new Vector3(sign * .19f, .005f, .10f), new Vector3(.17f, .36f, .22f));
                plate.localRotation = Quaternion.Euler(0, sign * -25, sign * 13);
                Part(core, "Tiny moss " + sign, sphere, moss, new Vector3(sign * .19f, .17f, .10f), new Vector3(.07f, .024f, .06f));
            }
            Part(core, "Reactor top circuit", sphere, lensMaterial, new Vector3(0, .196f, 0), new Vector3(.045f, .022f, .25f));
            var warning = new GameObject("Locked landing warning").AddComponent<LineRenderer>(); warning.transform.SetParent(root.transform, false);
            warning.sharedMaterial = line; warning.useWorldSpace = true; warning.widthMultiplier = .035f; warning.enabled = false;
            warning.shadowCastingMode = ShadowCastingMode.Off; warning.receiveShadows = false; warning.numCapVertices = 1;
            var ai = root.AddComponent<GelBlobAI>(); var visual = root.AddComponent<GelBlobVisual>();
            var v = new SerializedObject(visual); Ref(v, "gel", gel); Ref(v, "core", core); Ref(v, "gelRenderer", gel.GetComponent<Renderer>());
            Ref(v, "lens", lens.GetComponent<Renderer>()); Ref(v, "warning", warning); Ref(v, "dropletMesh", sphere); Ref(v, "dropletMaterial", lensMaterial); v.ApplyModifiedPropertiesWithoutUndo();
            var a = new SerializedObject(ai); Ref(a, "visual", visual); a.ApplyModifiedPropertiesWithoutUndo();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab");
            if (!source || !source.GetComponent<StoneScoutLootDrop>()) throw new InvalidOperationException("Shared loot missing.");
            var loot = root.AddComponent<StoneScoutLootDrop>(); EditorUtility.CopySerialized(source.GetComponent<StoneScoutLootDrop>(), loot);
            var l = new SerializedObject(loot); l.FindProperty("healAmount").intValue = 8; l.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
    }
    static bool Placement(Scene scene, OverworldV2Layout layout, EnemyNavigationWorld navigation, EnemyGroundQuery ground,
        Vector3 anchor, Vector3[] selected, int count, string ownName, out Vector3 point)
    {
        point = default;
        for (int trial = 0; trial < 120; trial++)
        {
            float a = trial * 2.39996323f, r = Mathf.Sqrt(trial) * 1.15f;
            Vector3 candidate = anchor + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            if (!ground.TrySample(candidate, 30f, 50f, 28f, out RaycastHit floor)) continue;
            candidate.y = floor.point.y + .025f;
            if (!AboveLake(layout, candidate) || Flat(candidate - layout.IntroShorePosition) < 16f || Flat(candidate - layout.ArrivalPosition) < 16f ||
                Flat(candidate - layout.CampPosition) < 13f ||
                !navigation.Sample(EnemyNavigationWorld.Profile.StoneScout, candidate, .6f, out Vector3 nav) ||
                Mathf.Abs(nav.y - candidate.y) > .35f || Flat(nav - candidate) > .35f ||
                !ground.TryFootprint(candidate, Quaternion.identity, Vector2.one * (GelBlobAI.DefaultLandingRadius + .1f), 28f, .6f, .7f, out _, true)) continue;
            bool close = false; for (int i = 0; i < count; i++) close |= Flat(candidate - selected[i]) < 9f;
            foreach (Health enemy in Object.FindObjectsByType<Health>(FindObjectsSortMode.None))
                if (enemy.name != ownName && enemy.gameObject.scene == scene && enemy.Team == HealthTeam.Enemy && Flat(candidate - enemy.transform.position) < 7f) close = true;
            if (close) continue;
            Vector3 half = new Vector3(.8f, .47f, .8f) * GelBlobAI.DefaultBodyScale;
            var obstacles = Physics.OverlapBox(candidate + Vector3.up * (.03f + half.y), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = false; foreach (Collider obstacle in obstacles)
                if (!ground.AllowsSupportContact(obstacle, candidate) && obstacle.GetComponentInParent<GelBlobAI>()?.name != ownName) blocked = true;
            if (!blocked) { point = candidate; return true; }
        }
        return false;
    }
    // This map's visible lake has no blocking collider everywhere. Do not put a shore gel
    // on the approved basin floor UNDER its rendered water, even if that navmesh is valid.
    public static bool AboveLake(OverworldV2Layout layout, Vector3 point) => point.y >= layout.LakePosition.y + .15f;
    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    static Transform Find(Scene scene, string name)
    { foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }
    static void Ref(SerializedObject o, string field, Object value) => o.FindProperty(field).objectReferenceValue = value;
    static Material Material(string name, Shader shader)
    {
        string path = Folder + "/" + name + ".mat"; var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) { existing.shader = shader; EditorUtility.SetDirty(existing); return existing; }
        var material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); return material;
    }
    static Mesh Save(Mesh mesh)
    {
        string path = Folder + "/" + mesh.name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!existing) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(mesh); return existing;
    }
    static Transform Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = material.renderQueue >= 2500 ? ShadowCastingMode.Off : ShadowCastingMode.On; return go.transform;
    }
    static Mesh Hull()
    {
        const int segments = 28;
        float[] height = { 0, .035f, .15f, .33f, .54f, .73f, .86f, .90f };
        float[] radius = { .40f, .51f, .53f, .50f, .42f, .31f, .18f, 0 };
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int row = 0; row < height.Length; row++) for (int x = 0; x <= segments; x++)
        {
            float a = x * Mathf.PI * 2f / segments;
            float r = radius[row] * (1 + Mathf.Cos(a * 3f + .6f) * .065f * (1 - row / 8f));
            vertices.Add(new Vector3(Mathf.Cos(a) * r, height[row], Mathf.Sin(a) * r));
        }
        for (int row = 0; row < height.Length - 1; row++) for (int x = 0; x < segments; x++)
        { int n = row * (segments + 1) + x; Quad(triangles, n, n + segments + 1, n + segments + 2, n + 1); }
        // Close the underside so an airborne blob remains a volume rather than an open dome.
        int centre = vertices.Count; vertices.Add(Vector3.zero);
        for (int x = 0; x < segments; x++) { triangles.Add(centre); triangles.Add(x); triangles.Add(x + 1); }
        return Mesh("SoftLobedGel", vertices, triangles);
    }
    static Mesh Sphere()
    {
        const int around = 12, rows = 8; var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int y = 0; y <= rows; y++) for (int x = 0; x <= around; x++)
        { float a = x * Mathf.PI * 2 / around, p = y * Mathf.PI / rows; vertices.Add(new Vector3(Mathf.Sin(p) * Mathf.Cos(a), Mathf.Cos(p), Mathf.Sin(p) * Mathf.Sin(a)) * .5f); }
        for (int y = 0; y < rows; y++) for (int x = 0; x < around; x++)
        { int n = y * (around + 1) + x; Quad(triangles, n, n + 1, n + around + 2, n + around + 1); }
        return Mesh("SmallCoreAndDroplet", vertices, triangles);
    }
    static Mesh CoreRing()
    {
        const int around = 16, rows = 4; var v = new List<Vector3>(); var t = new List<int>();
        for (int x = 0; x <= around; x++) for (int y = 0; y <= rows; y++)
        { float a = x * Mathf.PI * 2 / around, b = y * Mathf.PI * 2 / rows, r = .8f + Mathf.Cos(b) * .2f; v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, Mathf.Sin(b) * .2f)); }
        for (int x = 0; x < around; x++) for (int y = 0; y < rows; y++)
        { int n = x * (rows + 1) + y; Quad(t, n, n + rows + 1, n + rows + 2, n + 1); }
        return Mesh("ReactorCollar", v, t);
    }
    static void Quad(List<int> t, int a, int b, int c, int d) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
    static Mesh Mesh(string name, List<Vector3> vertices, List<int> triangles)
    { var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh; }
    public static void CheckAssets()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab || !prefab.GetComponent<GelBlobAI>() || !prefab.GetComponent<GelBlobVisual>() || !prefab.GetComponent<StoneScoutLootDrop>())
            throw new InvalidOperationException("Gel components missing.");
        int transparent = 0, triangles = 0;
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>()) triangles += filter.sharedMesh.triangles.Length / 3;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>())
        { if (ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader)) throw new InvalidOperationException("Shader compilation failed."); if (renderer.sharedMaterial.renderQueue >= 2500 && !(renderer is LineRenderer)) transparent++; }
        if (transparent != 1 || triangles > 2200 || prefab.GetComponentsInChildren<Light>().Length != 0 || prefab.GetComponentsInChildren<Animator>().Length != 0)
            throw new InvalidOperationException("Gel presentation budget exceeded: " + triangles);
        Debug.Log("GEL ASSET PASS: " + triangles + " triangles; one transparent hull; zero lights/rigs.");
    }
}
