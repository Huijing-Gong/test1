using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class RepairWispBuilder
{
    public const string Folder = "Assets/Art/Enemies/RepairWisp/Generated";
    public const string PrefabPath = Folder + "/RepairWisp.prefab";
    private const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";

    [MenuItem("Tools/Enemies/Update Repair Wisp Support Shader")]
    public static void AddSupportShader()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before updating support visual.");
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Enemies/RepairWisp/RepairSupportShell.shader");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Support shader missing or failed compilation.");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/RepairSupportShell.mat");
        if (!material)
        {
            material = new Material(shader) { name = "Repair support energy shell" };
            AssetDatabase.CreateAsset(material, Folder + "/RepairSupportShell.mat");
        }
        material.SetColor("_SupportColor", new Color(.65f, .95f, .15f));
        material.renderQueue = 3010; material.SetFloat("_SupportStrength", 0f);
        EditorUtility.SetDirty(material);
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var effect = root.GetComponent<RepairWispSupportVfx>();
            if (!effect) effect = root.AddComponent<RepairWispSupportVfx>();
            var data = new SerializedObject(effect); Ref(data, "supportMaterial", material); data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        RepairWispChecks.Run();
        Debug.Log("Repair support shader ready; scene instances inherit prefab update, positions/tuning unchanged.");
    }

    [MenuItem("Tools/Enemies/Build and Place Repair Wisps")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before creating repair wisps.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first. Scene unchanged.");
        GameObject prefab = Build();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath,
            string.IsNullOrEmpty(SceneManager.GetActiveScene().path) ? OpenSceneMode.Single : OpenSceneMode.Additive);
        try
        {
            Health west = FindGuard(scene, "RuinPrism_WestRuins"), tower = FindGuard(scene, "RuinPrism_TowerStairs");
            EnemyNavigationWorld navigation = EnemyNavigationWorld.Find(scene);
            if (!west || !tower || !navigation) throw new InvalidOperationException("Expected west/tower guard and baked navigation missing; scene unchanged.");
            Physics.SyncTransforms();
            // Resolve both placements BEFORE changing scene objects. Existing samples are preserved.
            bool westExists = Find(scene, "RepairWisp_HiddenChest"), towerExists = Find(scene, "RepairWisp_Tower");
            Vector3 westPoint = default, towerPoint = default;
            if (!westExists && !SafePlacement(navigation, west.transform.position, out westPoint))
                throw new InvalidOperationException("No clear repair-wisp position near west guard; scene unchanged.");
            if (!towerExists && !SafePlacement(navigation, tower.transform.position, out towerPoint))
                throw new InvalidOperationException("No clear repair-wisp position near tower guard; scene unchanged.");
            Transform root = Find(scene, "_RepairWispEncounters");
            if (!root) { root = new GameObject("_RepairWispEncounters").transform; SceneManager.MoveGameObjectToScene(root.gameObject, scene); }
            if (!westExists) Place(prefab, root, scene, "RepairWisp_HiddenChest", "HiddenChestSupport", west, westPoint);
            if (!towerExists) Place(prefab, root, scene, "RepairWisp_Tower", "TowerSupport", tower, towerPoint);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("Repair Wisp ready: shared lightweight prefab; one west hidden-chest support and one tower support. Existing enemies untouched.");
        }
        finally { if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
        AddSupportShader();
    }

    private static GameObject Build()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)) return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Enemies/RepairWisp", "Generated");
        string shared = "Assets/Art/Enemies/DomainPrototypes/Generated/";
        Material ceramic = AssetDatabase.LoadAssetAtPath<Material>(shared + "IvoryCeramic.mat");
        Material graphite = AssetDatabase.LoadAssetAtPath<Material>(shared + "Graphite.mat");
        Material moss = AssetDatabase.LoadAssetAtPath<Material>(shared + "LeafGreen.mat");
        Material lineMaterial = AssetDatabase.LoadAssetAtPath<Material>(shared + "WarningLine.mat");
        if (!ceramic || !graphite || !moss || !lineMaterial) throw new InvalidOperationException("Shared Domain materials missing.");
        Material lime = new Material(ceramic) { name = "Repair lime core" };
        lime.SetColor("_BaseColor", new Color(.55f, .94f, .18f)); lime.EnableKeyword("_EMISSION");
        lime.SetColor("_EmissionColor", new Color(.55f, 1f, .17f) * 1.4f);
        // URP's validator disables _EMISSION if AnyEmissive is unset. Non-static actors don't bake light.
        lime.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        AssetDatabase.CreateAsset(lime, Folder + "/RepairLime.mat");
        Mesh bead = Save(Ellipsoid("Faceted lens", 12, 6));
        Mesh shellMesh = Save(Shell("Split ceramic housing"));
        Mesh wingMesh = Save(Wing("Short tapered repair wing"));
        Mesh mossMesh = Save(Moss("Small ceramic moss patches"));
        GameObject root = new GameObject("Repair Wisp");
        try
        {
            Health health = root.AddComponent<Health>();
            var hp = new SerializedObject(health); hp.FindProperty("maxHealth").intValue = 24;
            hp.FindProperty("team").enumValueIndex = (int)HealthTeam.Enemy; hp.ApplyModifiedPropertiesWithoutUndo();
            var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var collider = root.AddComponent<CapsuleCollider>(); collider.radius = .46f; collider.height = .92f;
            collider.center = Vector3.up * 1.05f;
            Transform shell = new GameObject("Split ceramic repair shell").transform; shell.SetParent(root.transform, false); shell.localPosition = Vector3.up * 1.05f;
            Part(shell, "Dark compact housing", bead, graphite, Vector3.zero, new Vector3(.49f, .37f, .46f));
            Part(shell, "Ivory split armor", shellMesh, ceramic, Vector3.zero, Vector3.one);
            Part(shell, "Sparse moss", mossMesh, moss, Vector3.zero, Vector3.one);
            Transform left = new GameObject("Left short wing").transform; left.SetParent(shell, false); left.localPosition = new Vector3(-.18f, .02f, 0);
            Transform right = new GameObject("Right short wing").transform; right.SetParent(shell, false); right.localPosition = new Vector3(.18f, .02f, 0);
            Part(left, "Left ceramic wing", wingMesh, ceramic, Vector3.zero, new Vector3(-1, 1, 1));
            Part(right, "Right ceramic wing", wingMesh, ceramic, Vector3.zero, Vector3.one);
            Part(left, "Left wing power bead", bead, lime, new Vector3(-.14f, -.014f, -.015f), new Vector3(.028f, .028f, .12f));
            Part(right, "Right wing power bead", bead, lime, new Vector3(.14f, -.014f, -.015f), new Vector3(.028f, .028f, .12f));
            Part(shell, "Dark lens collar", bead, graphite, new Vector3(0, -.005f, .24f), new Vector3(.24f, .24f, .08f));
            Transform core = Part(shell, "Lime repair lens", bead, lime, new Vector3(0, -.005f, .281f), new Vector3(.155f, .155f, .055f));
            var nodes = new GameObject[3];
            for (int i = 0; i < nodes.Length; i++) nodes[i] = Part(shell, "Repair charge " + (i + 1), bead, lime,
                new Vector3((i - 1) * .075f, .153f, -.065f), new Vector3(.04f, .026f, .075f)).gameObject;
            LineRenderer line = new GameObject("Single target repair beam").AddComponent<LineRenderer>();
            line.transform.SetParent(root.transform, false); line.sharedMaterial = lineMaterial;
            line.positionCount = 2; line.useWorldSpace = true; line.widthMultiplier = .03f;
            line.startColor = new Color(.72f, 1f, .28f); line.endColor = new Color(.38f, .95f, .2f);
            line.numCapVertices = 2; line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false; line.enabled = false;
            RepairWispVisual visual = root.AddComponent<RepairWispVisual>();
            var v = new SerializedObject(visual);
            Ref(v, "shell", shell); Ref(v, "leftWing", left); Ref(v, "rightWing", right);
            Ref(v, "core", core); Ref(v, "coreRenderer", core.GetComponent<Renderer>());
            var charges = v.FindProperty("chargeNodes"); charges.arraySize = nodes.Length;
            for (int i = 0; i < nodes.Length; i++) charges.GetArrayElementAtIndex(i).objectReferenceValue = nodes[i];
            v.ApplyModifiedPropertiesWithoutUndo();
            RepairWispAI ai = root.AddComponent<RepairWispAI>(); var a = new SerializedObject(ai);
            Ref(a, "visual", visual); Ref(a, "energyLine", line); a.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<CombatFeedback>();
            root.AddComponent<NonClimbableSurface>();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab");
            if (!source || !source.GetComponent<StoneScoutLootDrop>()) throw new InvalidOperationException("Shared loot source missing.");
            var loot = root.AddComponent<StoneScoutLootDrop>(); EditorUtility.CopySerialized(source.GetComponent<StoneScoutLootDrop>(), loot);
            ConfigureFeedback(root, source);
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }
    private static Transform Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        var part = new GameObject(name); part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
        part.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
        return part.transform;
    }
    private static Mesh Save(Mesh mesh) { AssetDatabase.CreateAsset(mesh, Folder + "/" + mesh.name.Replace(" ", "") + ".asset"); return mesh; }
    private static void Ref(SerializedObject data, string field, Object value) => data.FindProperty(field).objectReferenceValue = value;
    private static Mesh Ellipsoid(string name, int around, int rows)
    {
        var points = new List<Vector3>(); var triangles = new List<int>();
        for (int y = 0; y < rows; y++) for (int x = 0; x < around; x++)
        {
            float a = x * Mathf.PI * 2 / around, b = (x + 1) * Mathf.PI * 2 / around;
            float p = y * Mathf.PI / rows, q = (y + 1) * Mathf.PI / rows;
            Quad(points, triangles, Sphere(a, p), Sphere(b, p), Sphere(b, q), Sphere(a, q));
        }
        return Mesh(name, points, triangles);
    }
    private static Vector3 Sphere(float longitude, float latitude) => new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude),
        Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude)) * .5f;
    private static Mesh Shell(string name)
    {
        var points = new List<Vector3>(); var triangles = new List<int>();
        // Two curved armor petals, an open middle seam and unobstructed forward lens.
        for (int side = 0; side < 2; side++) for (int row = 0; row < 3; row++) for (int col = 0; col < 6; col++)
        {
            float a = (-90f + col * 30f) * Mathf.Deg2Rad, b = a + 30f * Mathf.Deg2Rad;
            float p = (25f + row * 25f) * Mathf.Deg2Rad, q = p + 25f * Mathf.Deg2Rad;
            float sign = side == 0 ? -1 : 1;
            Quad(points, triangles, Armor(a, p, sign), Armor(b, p, sign), Armor(b, q, sign), Armor(a, q, sign));
        }
        return Mesh(name, points, triangles, true);
    }
    private static Vector3 Armor(float a, float p, float sign) => new Vector3(sign * (.032f + Mathf.Cos(a) * Mathf.Sin(p) * .21f),
        Mathf.Cos(p) * .22f, Mathf.Sin(a) * Mathf.Sin(p) * .225f);
    private static Mesh Wing(string name)
    {
        var points = new List<Vector3>(); var triangles = new List<int>();
        Vector3[] v = { new Vector3(0, .03f, .12f), new Vector3(.21f, .08f, .065f), new Vector3(.27f, -.01f, -.09f),
            new Vector3(.08f, -.04f, -.14f), new Vector3(0, -.03f, .11f), new Vector3(.21f, .035f, .065f),
            new Vector3(.27f, -.045f, -.09f), new Vector3(.08f, -.085f, -.14f) };
        Quad(points, triangles, v[0], v[1], v[2], v[3]); Quad(points, triangles, v[7], v[6], v[5], v[4]);
        for (int i = 0; i < 4; i++) Quad(points, triangles, v[i], v[i + 4], v[(i + 1) % 4 + 4], v[(i + 1) % 4]);
        return Mesh(name, points, triangles, true);
    }
    private static Mesh Moss(string name)
    {
        var points = new List<Vector3>(); var triangles = new List<int>();
        for (int i = 0; i < 7; i++)
        {
            float x = -.19f + i * .015f, z = -.07f + (i % 3) * .04f, y = .16f + (i % 2) * .017f;
            Quad(points, triangles, new Vector3(x, y, z), new Vector3(x + .021f, y + .004f, z),
                new Vector3(x + .024f, y, z + .024f), new Vector3(x, y - .003f, z + .02f));
        }
        return Mesh(name, points, triangles, true);
    }
    private static void Quad(List<Vector3> points, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int n = points.Count; points.Add(a); points.Add(b); points.Add(c); points.Add(d);
        triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2); triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 3);
    }
    private static Mesh Mesh(string name, List<Vector3> points, List<int> triangles, bool doubleSided = false)
    {
        if (doubleSided)
        {
            int vertexCount = points.Count, triangleCount = triangles.Count;
            // Backfaces need separate vertices, otherwise opposing normals cancel to zero.
            for (int i = 0; i < vertexCount; i++) points.Add(points[i]);
            for (int i = 0; i < triangleCount; i += 3)
            { triangles.Add(triangles[i] + vertexCount); triangles.Add(triangles[i + 2] + vertexCount); triangles.Add(triangles[i + 1] + vertexCount); }
        }
        var result = new Mesh { name = name }; result.SetVertices(points); result.SetTriangles(triangles, 0);
        result.RecalculateNormals(); result.RecalculateBounds(); return result;
    }
    public static void RefreshMeshes()
    {
        foreach (Mesh mesh in new[] { Ellipsoid("Faceted lens", 12, 6), Shell("Split ceramic housing"),
            Wing("Short tapered repair wing"), Moss("Small ceramic moss patches") })
        {
            string path = Folder + "/" + mesh.name.Replace(" ", "") + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing) { Save(mesh); continue; }
            EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(mesh);
        }
        AssetDatabase.SaveAssets();
    }
    public static void FinishPresentation()
    {
        RefreshMeshes();
        Material lime = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/RepairLime.mat");
        lime.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        lime.EnableKeyword("_EMISSION"); EditorUtility.SetDirty(lime);
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (!root.GetComponent<NonClimbableSurface>()) root.AddComponent<NonClimbableSurface>();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab");
            ConfigureFeedback(root, source);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }
    private static void ConfigureFeedback(GameObject root, GameObject source)
    {
        var sourceFeedback = new SerializedObject(source.GetComponent<CombatFeedback>());
        var stoneFeedback = new SerializedObject(source.GetComponent<StoneScoutFeedback>());
        var feedback = new SerializedObject(root.GetComponent<CombatFeedback>());
        foreach (string name in new[] { "hitClip", "deathClip" })
            feedback.FindProperty(name).objectReferenceValue = sourceFeedback.FindProperty(name).objectReferenceValue
                ? sourceFeedback.FindProperty(name).objectReferenceValue : stoneFeedback.FindProperty("stoneHitClip").objectReferenceValue;
        AudioSource audio = root.GetComponent<AudioSource>(); if (!audio) audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false; audio.spatialBlend = 1f; audio.minDistance = 1f; audio.maxDistance = 12f;
        feedback.FindProperty("audioSource").objectReferenceValue = audio; feedback.ApplyModifiedPropertiesWithoutUndo();
    }
    private static bool SafePlacement(EnemyNavigationWorld world, Vector3 guard, out Vector3 point)
    {
        for (int ring = 0; ring < 2; ring++) for (int i = 0; i < 16; i++)
        {
            Vector3 desired = guard + Quaternion.Euler(0, 135f + i * 22.5f, 0) * Vector3.forward * (ring == 0 ? 2.8f : 3.6f);
            if (!world.Sample(EnemyNavigationWorld.Profile.LeafWisp, desired, 1.3f, out point) ||
                Vector3.Distance(point, desired) > 1.1f) continue;
            point += Vector3.up * .015f;
            if (Physics.CheckCapsule(point + Vector3.up * .99f, point + Vector3.up * 1.11f,
                .49f, ~0, QueryTriggerInteraction.Ignore)) continue;
            return true;
        }
        point = default; return false;
    }
    private static void Place(GameObject prefab, Transform parent, Scene scene, string name, string groupName, Health guard, Vector3 point)
    {
        var group = new GameObject(groupName).AddComponent<RepairWispGroup>(); group.transform.SetParent(parent, false);
        group.Configure(new[] { guard });
        var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        actor.name = name; actor.transform.SetParent(group.transform, false); actor.transform.position = point;
        RepairWispAI ai = actor.GetComponent<RepairWispAI>(); ai.ConfigureGroup(group);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
        PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
        Debug.Log(name + " placed on clear baked ground at " + point + "; guard=" + guard.name);
    }
    private static Transform Find(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects()) foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
    private static Health FindGuard(Scene scene, string name) { Transform t = Find(scene, name); return t ? t.GetComponent<Health>() : null; }
}
