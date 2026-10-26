using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run once in the open Unity Editor. Assets are reusable; scene placement is idempotent.
public static class DomainPrototypeBuilder
{
    private const string Folder = "Assets/Art/Enemies/DomainPrototypes/Generated";
    private const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    private const string LootSource = "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab";
    private const string GroupName = "_DomainPrototypeEncounters";
    private const string WispName = "LeafWisp_EastForest";
    private const string PrismName = "RuinPrism_WestRuins";

    [MenuItem("Tools/Art/Build and Place Domain Prototype Enemies")]
    public static void BuildAndPlace()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Leave Play mode before building enemy prototypes.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder();
        Material stone = Material("IvoryCeramic", new Color(0.79f, 0.78f, 0.66f), false);
        Material shadow = Material("Graphite", new Color(0.12f, 0.18f, 0.19f), false);
        Material moss = Material("LeafGreen", new Color(0.33f, 0.52f, 0.27f), false);
        Material cyan = Material("CyanCircuit", new Color(0.08f, 0.78f, 0.91f), true);
        Material amber = Material("AmberLens", new Color(1f, 0.51f, 0.12f), true);
        Material line = WarningMaterial();
        GameObject seed = BuildSeed(cyan, amber);
        GameObject wisp = BuildEnemy(DomainPrototypeEnemy.Kind.LeafWisp,
            stone, shadow, moss, cyan, amber, line, seed);
        GameObject prism = BuildEnemy(DomainPrototypeEnemy.Kind.RuinPrism,
            stone, shadow, moss, cyan, amber, line, seed);
        Place(wisp, prism);
        DomainPrototypeChecks.Run();
        Debug.Log("Domain enemies ready: one Leaf Wisp and four shared-prefab Ruin Prism guards. " +
            "Prism instances have separate palettes and balance, and reset on a new journey.");
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Art/Enemies/DomainPrototypes", "Generated");
    }

    private static Material Material(string name, Color color, bool glow)
    {
        string path = Folder + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Standard");
        if (!shader) throw new InvalidOperationException("No Lit shader available for Domain enemies.");
        Material result = new Material(shader) { name = name, color = color };
        if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", color);
        if (glow && result.HasProperty("_EmissionColor"))
        {
            result.EnableKeyword("_EMISSION");
            result.SetColor("_EmissionColor", color * 2f);
            result.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(result, path);
        return result;
    }

    private static Material WarningMaterial()
    {
        string path = Folder + "/WarningLine.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (!shader) shader = Shader.Find("Sprites/Default");
        if (!shader) throw new InvalidOperationException("No vertex-colour line shader available.");
        Material result = new Material(shader) { name = "WarningLine" };
        if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(result, path);
        return result;
    }

    private static GameObject BuildSeed(Material cyan, Material amber)
    {
        string path = Folder + "/LeafSeedProjectile.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing) return existing;
        GameObject root = new GameObject("LeafSeedProjectile");
        root.AddComponent<DomainSeedProjectile>();
        Part(root.transform, "Graphite seed", PrimitiveType.Sphere, amber,
            Vector3.zero, new Vector3(0.17f, 0.12f, 0.38f), Vector3.zero);
        Part(root.transform, "Cyan tip", PrimitiveType.Sphere, cyan,
            new Vector3(0, 0, 0.16f), new Vector3(0.1f, 0.1f, 0.16f), Vector3.zero);
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
    }

    private static GameObject BuildEnemy(DomainPrototypeEnemy.Kind kind,
        Material stone, Material shadow, Material moss, Material cyan, Material amber,
        Material line, GameObject seed)
    {
        bool wisp = kind == DomainPrototypeEnemy.Kind.LeafWisp;
        string path = Folder + (wisp ? "/LeafWisp.prefab" : "/RuinPrism.prefab");
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing) return existing;

        GameObject root = new GameObject(wisp ? "Leaf Wisp" : "Ruin Prism");
        Health health = root.AddComponent<Health>();
        SerializedObject healthData = new SerializedObject(health);
        healthData.FindProperty("maxHealth").intValue = wisp ? 35 : 90;
        healthData.FindProperty("team").enumValueIndex = (int)HealthTeam.Enemy;
        healthData.ApplyModifiedPropertiesWithoutUndo();
        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.center = Vector3.up * (wisp ? 1.2f : 1.18f);
        collider.height = wisp ? 1.55f : 2.36f;
        collider.radius = wisp ? 0.48f : 0.78f;
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        root.AddComponent<NonClimbableSurface>();

        Transform visual = new GameObject("Animated ceramic shell").transform;
        visual.SetParent(root.transform, false);
        if (!wisp) visual.localScale = Vector3.one * 1.25f;
        Transform muzzle = null;
        if (wisp)
        {
            Part(visual, "dark seed body", PrimitiveType.Sphere, shadow,
                new Vector3(0, 1.22f, 0), new Vector3(0.68f, 0.68f, 0.62f), Vector3.zero);
            Part(visual, "ivory crown", PrimitiveType.Sphere, stone,
                new Vector3(0, 1.45f, -0.11f), new Vector3(0.77f, 0.36f, 0.62f), Vector3.zero);
            Part(visual, "front lens socket", PrimitiveType.Sphere, shadow,
                new Vector3(0, 1.25f, 0.31f), new Vector3(0.4f, 0.4f, 0.17f), Vector3.zero);
            Part(visual, "cyan lens", PrimitiveType.Sphere, cyan,
                new Vector3(0, 1.25f, 0.39f), new Vector3(0.22f, 0.22f, 0.12f), Vector3.zero);
            for (int side = -1; side <= 1; side += 2)
            {
                Part(visual, side < 0 ? "left leaf wing" : "right leaf wing", PrimitiveType.Sphere,
                    stone, new Vector3(side * 0.48f, 1.3f, -0.06f),
                    new Vector3(0.57f, 0.14f, 0.3f), new Vector3(0, side * 24f, side * 23f));
                Part(visual, "leaf tip", PrimitiveType.Sphere, moss,
                    new Vector3(side * 0.69f, 1.36f, -0.15f), new Vector3(0.19f, 0.08f, 0.17f),
                    new Vector3(0, 0, side * 23f));
                Part(visual, "side circuit", PrimitiveType.Cube, cyan,
                    new Vector3(side * 0.32f, 1.23f, 0.29f), new Vector3(0.04f, 0.3f, 0.025f),
                    new Vector3(0, 0, side * 22f));
            }
            muzzle = new GameObject("Seed muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0, 1.25f, 0.58f);
        }
        else
        {
            GameObject stoneBase = Part(root.transform, "broken stone base", PrimitiveType.Cylinder, stone,
                new Vector3(0, 0.22f, 0), new Vector3(1.8f, 0.22f, 1.8f), Vector3.zero);
            stoneBase.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
            Part(root.transform, "dark plinth", PrimitiveType.Cylinder, shadow,
                new Vector3(0, 0.51f, 0), new Vector3(1.08f, 0.14f, 1.08f), Vector3.zero);
            Part(visual, "floating core", PrimitiveType.Sphere, cyan,
                new Vector3(0, 1.24f, 0), new Vector3(0.48f, 0.48f, 0.48f), Vector3.zero);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Part(visual, "ceramic prism " + i, PrimitiveType.Cube, stone,
                    new Vector3(radial.x * 0.37f, 1.27f, radial.z * 0.37f),
                    new Vector3(0.36f, 0.75f, 0.19f), new Vector3(0, -i * 90f, 42f));
                Part(visual, "amber warning lens " + i, PrimitiveType.Sphere, amber,
                    new Vector3(radial.x * 0.58f, 1.25f, radial.z * 0.58f),
                    new Vector3(0.12f, 0.12f, 0.12f), Vector3.zero);
            }
        }

        LineRenderer warning = new GameObject("Readable attack warning").AddComponent<LineRenderer>();
        warning.transform.SetParent(root.transform, false);
        warning.useWorldSpace = true;
        warning.loop = !wisp;
        warning.widthMultiplier = wisp ? 0.035f : 0.09f;
        warning.numCornerVertices = 2;
        warning.material = line;
        warning.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        warning.receiveShadows = false;
        warning.enabled = false;

        DomainPrototypeEnemy ai = root.AddComponent<DomainPrototypeEnemy>();
        SerializedObject aiData = new SerializedObject(ai);
        aiData.FindProperty("kind").enumValueIndex = (int)kind;
        aiData.FindProperty("visualRoot").objectReferenceValue = visual;
        aiData.FindProperty("muzzle").objectReferenceValue = muzzle;
        aiData.FindProperty("warningLine").objectReferenceValue = warning;
        aiData.FindProperty("seedProjectile").objectReferenceValue = seed;
        aiData.FindProperty("noticeRange").floatValue = wisp ? 8f : 3.5f;
        aiData.FindProperty("windup").floatValue = wisp ? 0.72f : 1.05f;
        aiData.FindProperty("cooldown").floatValue = wisp ? 1.85f : 1.3f;
        aiData.FindProperty("aimLockLeadSeconds").floatValue = 0.22f;
        aiData.FindProperty("pulseRadius").floatValue = 5.4f;
        aiData.FindProperty("damage").intValue = wisp ? 7 : 14;
        aiData.ApplyModifiedPropertiesWithoutUndo();

        StoneScoutLootDrop drop = root.AddComponent<StoneScoutLootDrop>();
        GameObject lootSource = AssetDatabase.LoadAssetAtPath<GameObject>(LootSource);
        if (lootSource && lootSource.GetComponent<StoneScoutLootDrop>())
        {
            SerializedObject from = new SerializedObject(lootSource.GetComponent<StoneScoutLootDrop>());
            SerializedObject into = new SerializedObject(drop);
            into.FindProperty("shardMaterial").objectReferenceValue =
                from.FindProperty("shardMaterial").objectReferenceValue;
            into.FindProperty("pickupClip").objectReferenceValue =
                from.FindProperty("pickupClip").objectReferenceValue;
            into.FindProperty("healAmount").intValue = wisp ? 10 : 15;
            into.ApplyModifiedPropertiesWithoutUndo();
        }
        root.AddComponent<CombatFeedback>();
        GameObject result = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return result;
    }

    private static GameObject Part(Transform parent, string name, PrimitiveType type, Material material,
        Vector3 position, Vector3 scale, Vector3 angles)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.transform.localEulerAngles = angles;
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
        return part;
    }

    private static void Place(GameObject wisp, GameObject prism)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform group = null;
        MeshCollider terrain = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == GroupName) group = root.transform;
            foreach (MeshCollider candidate in root.GetComponentsInChildren<MeshCollider>(true))
                if (candidate.name == "LakeTerrain_REPLACE_OR_SCULPT") terrain = candidate;
        }
        if (!terrain) throw new InvalidOperationException("V2 terrain missing; prefabs built but scene unchanged.");
        if (group && group.Find(WispName) && group.Find(PrismName) &&
            group.Find("RuinPrism_SideA") && group.Find("RuinPrism_SideB") &&
            group.Find("RuinPrism_TowerStairs")) return;
        if (group && group.childCount > 0 && (!group.Find(WispName) || !group.Find(PrismName)))
            throw new InvalidOperationException("Unrecognized partial Domain group; existing scene unchanged.");
        Physics.SyncTransforms();
        if (!group)
        {
            group = new GameObject(GroupName).transform;
            SceneManager.MoveGameObjectToScene(group.gameObject, scene);
        }
        PlaceMissing(wisp, WispName, new Vector2(57f, 12f), 9, terrain, group, scene);
        PlaceMissing(prism, PrismName, new Vector2(-63f, -23.3f), 9, terrain, group, scene);
        PlaceMissing(prism, "RuinPrism_SideA", new Vector2(-59f, -60f), 2, terrain, group, scene,
            DomainPrototypeEnemy.PrismPalette.SideAEmber, 70, 10, 4.4f, 1.2f, 1.6f);
        PlaceMissing(prism, "RuinPrism_SideB", new Vector2(58f, -65f), 2, terrain, group, scene,
            DomainPrototypeEnemy.PrismPalette.SideBViolet, 85, 12, 5f, 1.08f, 1.4f);
        PlaceMissing(prism, "RuinPrism_TowerStairs", new Vector2(53f, 54f), 2, terrain, group, scene,
            DomainPrototypeEnemy.PrismPalette.TowerGold, 120, 18, 5.8f, 1f, 1.2f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Placed missing Domain guards on checked walkable terrain; existing instances preserved.");
    }

    private static void PlaceMissing(GameObject prefab, string name, Vector2 desired, int searchRadius,
        MeshCollider terrain, Transform group, Scene scene,
        DomainPrototypeEnemy.PrismPalette palette = DomainPrototypeEnemy.PrismPalette.WestCyan,
        int health = 0, int damage = 0, float radius = 0f, float windup = 0f, float cooldown = 0f)
    {
        if (group.Find(name)) return;
        if (!FindClearGround(terrain, desired, searchRadius, out Vector3 ground))
            throw new InvalidOperationException("No walkable, clear ground for " + name + " near " + desired + ".");
        GameObject instance = AddInstance(prefab, name, ground, group, scene);
        if (health <= 0) return;
        SerializedObject hp = new SerializedObject(instance.GetComponent<Health>());
        hp.FindProperty("maxHealth").intValue = health;
        hp.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject ai = new SerializedObject(instance.GetComponent<DomainPrototypeEnemy>());
        ai.FindProperty("prismPalette").enumValueIndex = (int)palette;
        ai.FindProperty("damage").intValue = damage;
        ai.FindProperty("pulseRadius").floatValue = radius;
        ai.FindProperty("windup").floatValue = windup;
        ai.FindProperty("cooldown").floatValue = cooldown;
        ai.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<Health>());
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<DomainPrototypeEnemy>());
    }

    private static GameObject AddInstance(GameObject prefab, string name, Vector3 place, Transform group, Scene scene)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = name;
        instance.transform.SetParent(group, true);
        instance.transform.position = place + Vector3.up * 0.015f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        return instance;
    }

    private static bool FindClearGround(MeshCollider terrain, Vector2 desired, int radius, out Vector3 result)
    {
        for (int r = 0; r <= radius; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
            float x = desired.x + dx, z = desired.y + dz;
            if (!Probe(terrain, x, z, out RaycastHit center)) continue;
            bool level = true;
            foreach (Vector2 offset in new[] { new Vector2(.7f, 0), new Vector2(-.7f, 0),
                         new Vector2(0, .7f), new Vector2(0, -.7f) })
                if (!Probe(terrain, x + offset.x, z + offset.y, out RaycastHit edge) ||
                    Mathf.Abs(edge.point.y - center.point.y) > .25f) { level = false; break; }
            if (!level) continue;
            bool blocked = false;
            foreach (Collider hit in Physics.OverlapCapsule(center.point + Vector3.up * .5f,
                         center.point + Vector3.up * 1.7f, .8f))
                if (hit != terrain && !hit.isTrigger && hit.gameObject.scene == terrain.gameObject.scene)
                { blocked = true; break; }
            if (blocked) continue;
            result = center.point;
            return true;
        }
        result = default;
        return false;
    }

    private static bool Probe(MeshCollider terrain, float x, float z, out RaycastHit hit)
    {
        return terrain.Raycast(new Ray(new Vector3(x, 100f, z), Vector3.down), out hit, 200f) &&
            Vector3.Angle(hit.normal, Vector3.up) <= 25f;
    }
}

public static class DomainPrototypeChecks
{
    private const string Folder = "Assets/Art/Enemies/DomainPrototypes/Generated/";
    private const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";

    [MenuItem("Tools/Art/Check Domain Prototype Enemies")]
    public static void Run()
    {
        CheckPrefab("LeafWisp.prefab", DomainPrototypeEnemy.Kind.LeafWisp);
        CheckPrefab("RuinPrism.prefab", DomainPrototypeEnemy.Kind.RuinPrism);
        GameObject seed = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "LeafSeedProjectile.prefab");
        if (!seed || !seed.GetComponent<DomainSeedProjectile>())
            throw new InvalidOperationException("Leaf seed projectile prefab is missing.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Transform group = null;
            OverworldV2Layout layout = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "_DomainPrototypeEncounters") group = root.transform;
                if (root.GetComponentInChildren<OverworldV2Layout>(true)) layout = root.GetComponentInChildren<OverworldV2Layout>(true);
            }
            if (!group || !layout || group.childCount != 5)
                throw new InvalidOperationException("Expected one wisp and four Prism guards on V2 terrain.");
            Transform wisp = group.Find("LeafWisp_EastForest");
            Transform prism = group.Find("RuinPrism_WestRuins");
            Transform sideA = group.Find("RuinPrism_SideA");
            Transform sideB = group.Find("RuinPrism_SideB");
            Transform tower = group.Find("RuinPrism_TowerStairs");
            if (!wisp || !prism || wisp.position.x < 35f || prism.position.x > -40f)
                throw new InvalidOperationException("Domain enemies are not in their intended regions.");
            float scale = layout.PositionScaleFromVersion2;
            bool regional = EnemyDistributionInstaller.HasLayout(scene);
            if (regional) EnemyDistributionInstaller.ValidateAuthoring();
            CheckVariant(sideA, regional && sideA ? new Vector2(sideA.position.x, sideA.position.z) : new Vector2(-59f, -60f) * scale, DomainPrototypeEnemy.PrismPalette.SideAEmber, 70, 10);
            CheckVariant(sideB, regional && sideB ? new Vector2(sideB.position.x, sideB.position.z) : new Vector2(58f, -65f) * scale, DomainPrototypeEnemy.PrismPalette.SideBViolet, 85, 12);
            // Current integrated map moved the tower guard onto the safe eastern landing.
            CheckVariant(tower, regional && tower ? new Vector2(tower.position.x, tower.position.z) : new Vector2(91f, 80f), DomainPrototypeEnemy.PrismPalette.TowerGold, 120, 18);
            if (!regional && Vector2.Distance(new Vector2(prism.position.x, prism.position.z),
                    new Vector2(-63f, -22f) * scale) > 1.5f * scale)
                throw new InvalidOperationException("Ruin Prism is too far from the west-ruins objective.");
            foreach (Transform enemy in new[] { wisp, prism, sideA, sideB, tower })
                if (!EnemyFoundationPlacementChecks.TryFitSpawn(enemy.GetComponent<Health>(), enemy.position, out Vector3 fitted) ||
                    Mathf.Abs(enemy.position.y - fitted.y) > 0.08f)
                    throw new InvalidOperationException(enemy.name + " is not on the walking surface.");
            Debug.Log("Domain enemy check PASS: shared prefabs, four grounded Prism guards, unique palette and health/damage tiers.");
        }
        finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
    }

    private static void CheckVariant(Transform instance, Vector2 expected,
        DomainPrototypeEnemy.PrismPalette palette, int expectedHealth, int expectedDamage)
    {
        if (!instance || Vector2.Distance(new Vector2(instance.position.x, instance.position.z), expected) > 2.5f)
            throw new InvalidOperationException("Prism guard is missing or too far from " + expected + ".");
        DomainPrototypeEnemy ai = instance.GetComponent<DomainPrototypeEnemy>();
        if (!ai || ai.EnemyKind != DomainPrototypeEnemy.Kind.RuinPrism || ai.Palette != palette ||
            instance.GetComponent<Health>().MaxHealth != expectedHealth ||
            new SerializedObject(ai).FindProperty("damage").intValue != expectedDamage)
            throw new InvalidOperationException(instance.name + " has wrong palette or balance.");
    }

    private static void CheckPrefab(string name, DomainPrototypeEnemy.Kind expected)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name);
        if (!prefab || !prefab.GetComponent<DomainPrototypeEnemy>() ||
            !prefab.GetComponent<StoneScoutLootDrop>() || !prefab.GetComponent<CombatFeedback>() ||
            !prefab.GetComponent<CapsuleCollider>() || !prefab.GetComponent<Health>() ||
            prefab.GetComponent<Health>().Team != HealthTeam.Enemy)
            throw new InvalidOperationException(name + " has missing combat components.");
        SerializedObject data = new SerializedObject(prefab.GetComponent<DomainPrototypeEnemy>());
        if (data.FindProperty("kind").enumValueIndex != (int)expected ||
            data.FindProperty("warningLine").objectReferenceValue == null ||
            (expected == DomainPrototypeEnemy.Kind.LeafWisp &&
             data.FindProperty("seedProjectile").objectReferenceValue == null))
            throw new InvalidOperationException(name + " has missing attack references.");
        if (expected == DomainPrototypeEnemy.Kind.RuinPrism)
        {
            Transform stoneBase = prefab.transform.Find("broken stone base");
            Transform shell = prefab.transform.Find("Animated ceramic shell");
            if (!stoneBase || !stoneBase.GetComponent<BoxCollider>() ||
                !shell || shell.localScale.x < 1.24f ||
                prefab.GetComponent<Health>().MaxHealth < 90 ||
                data.FindProperty("pulseRadius").floatValue < 5.4f ||
                data.FindProperty("windup").floatValue > 1.06f ||
                data.FindProperty("cooldown").floatValue > 1.31f ||
                data.FindProperty("damage").intValue < 14)
                throw new InvalidOperationException("Ruin Prism is missing its larger body, solid base or current combat balance.");
        }
    }
}

// On the first import, install automatically only when the intended scene is active and clean.
// A dirty or different scene is never closed or overwritten; the menu command remains available.
[InitializeOnLoad]
internal static class DomainPrototypeAutoInstall
{
    private const string AttemptedKey = "DomainPrototype.AutoInstall.20260930";

    static DomainPrototypeAutoInstall()
    {
        EditorApplication.delayCall += TryInstall;
    }

    private static void TryInstall()
    {
        if (SessionState.GetBool(AttemptedKey, false) ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene active = SceneManager.GetActiveScene();
        if (active.path != "Assets/_Demo/Scenes/OverworldBaseline.unity" || active.isDirty)
            return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/Enemies/DomainPrototypes/Generated/LeafWisp.prefab")) return;
        SessionState.SetBool(AttemptedKey, true);
        try { DomainPrototypeBuilder.BuildAndPlace(); }
        catch (Exception error)
        {
            Debug.LogWarning("Domain enemy auto-install deferred: " + error.Message +
                ". Use Tools/Art/Build and Place Domain Prototype Enemies after checking the scene.");
        }
    }
}

// MaterialPropertyBlock colours are non-serialized, so refresh the four scene previews in Edit mode.
[InitializeOnLoad]
internal static class DomainPrismPaletteEditorPreview
{
    private static double nextRefresh;

    static DomainPrismPaletteEditorPreview()
    {
        EditorApplication.update += Refresh;
    }

    private static void Refresh()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.timeSinceStartup < nextRefresh) return;
        nextRefresh = EditorApplication.timeSinceStartup + 1.5;
        Scene scene = SceneManager.GetSceneByPath("Assets/_Demo/Scenes/OverworldBaseline.unity");
        if (!scene.isLoaded) return;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == "_DomainPrototypeEncounters")
                foreach (DomainPrototypeEnemy enemy in root.GetComponentsInChildren<DomainPrototypeEnemy>(true))
                    enemy.RefreshPalettePreview();
    }
}
