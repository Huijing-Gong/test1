using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit, additive installation. Does not regenerate the map, player, or existing enemies.
public static class EnemyIntegrationSetup
{
    [MenuItem("Tools/Enemies/Install Five Enemy Integration")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before installing encounters.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save edited scenes first.");
        Scene scene = EditorSceneManager.OpenScene(EnemyNavigationBuilder.ScenePath);
        if (EnemyDistributionInstaller.HasLayout(scene))
        {
            EnemyDistributionInstaller.ValidateAuthoring();
            Validate();
            Debug.Log("Regional P0 layout preserved; legacy integration placement skipped.");
            return;
        }
        Physics.SyncTransforms();
        EnemyNavigationBuilder.Run();
        Transform existingRail = Find(scene, RailBeetleBuilder.InstanceName);
        if (!existingRail || !EnemyFoundationPlacementChecks.TryFitSpawn(existingRail.GetComponent<Health>(), existingRail.position, out _))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RailBeetleBuilder.PrefabPath);
            if (!prefab || !TryRailPlacement(scene, prefab, out Vector3 point))
                throw new InvalidOperationException("No safe Rail Beetle corridor. No new encounter created.");
            Transform group = Find(scene, RailBeetleBuilder.GroupName);
            if (!group)
            {
                group = new GameObject(RailBeetleBuilder.GroupName).transform;
                SceneManager.MoveGameObjectToScene(group.gameObject, scene);
            }
            var actor = existingRail ? existingRail.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            actor.name = RailBeetleBuilder.InstanceName;
            actor.transform.SetParent(group, false);
            actor.transform.position = point;
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        RepairWispBuilder.Run();
        EnemyFoundationPlacementChecks.FixInvalidPlacements();
        Validate();
    }

    public static void Validate()
    {
        Scene scene = EditorSceneManager.OpenScene(EnemyNavigationBuilder.ScenePath);
        int scouts = 0, leaf = 0, prisms = 0, rail = 0, repair = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                    throw new InvalidOperationException("Missing script on " + child.name);
            foreach (Health health in root.GetComponentsInChildren<Health>(true))
            {
                if (health.GetComponent<StoneScoutRoadsideAI>()) scouts++;
                if (health.GetComponent<RailBeetleAI>()) rail++;
                if (health.GetComponent<RepairWispAI>()) repair++;
                var domain = health.GetComponent<DomainPrototypeEnemy>();
                if (domain && domain.EnemyKind == DomainPrototypeEnemy.Kind.LeafWisp) leaf++;
                if (domain && domain.EnemyKind == DomainPrototypeEnemy.Kind.RuinPrism) prisms++;
            }
        }
        if (scouts < 1 || leaf < 1 || prisms < 1 || rail != 1 || repair != 2)
            throw new InvalidOperationException($"Unexpected enemy counts: {scouts}/{leaf}/{prisms}/{rail}/{repair}");
        EnemyFoundationPlacementChecks.RunAll();
        Debug.Log($"FIVE ENEMY INTEGRATION PASS: Scout={scouts}, Leaf={leaf}, Prism={prisms}, Rail={rail}, Repair={repair}; no missing scene scripts.");
    }

    public static void FitAndValidate()
    {
        EnemyFoundationPlacementChecks.FixInvalidPlacements();
        Validate();
    }

    static Transform Find(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
        return null;
    }

    static bool TryRailPlacement(Scene scene, GameObject prefab, out Vector3 point)
    {
        var navigation = EnemyNavigationWorld.Find(scene);
        var layout = UnityEngine.Object.FindFirstObjectByType<OverworldV2Layout>();
        float scale = layout.PositionScaleFromVersion2;
        Vector3 desired = new Vector3(41 * scale, 0, 34 * scale);
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        probe.name = "Temporary Rail Beetle placement probe";
        try
        {
            for (int ring = 0; ring <= 24; ring++)
            for (int i = 0; i < 16; i++)
            {
                Vector3 candidate = desired + Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward * ring;
                candidate.y = layout.EastRegionPosition.y;
                if (!navigation.Sample(EnemyNavigationWorld.Profile.RailBeetle, candidate, 35, out Vector3 onMesh) ||
                    Vector2.Distance(new Vector2(onMesh.x, onMesh.z), new Vector2(candidate.x, candidate.z)) > .75f) continue;
                probe.transform.position = onMesh + Vector3.up * .015f;
                Physics.SyncTransforms();
                if (!EnemyFoundationPlacementChecks.TryFitSpawn(probe.GetComponent<Health>(), probe.transform.position, out point)) continue;
                bool corridor = true;
                for (int step = 1; step <= 3 && corridor; step++)
                {
                    Vector3 ahead = point + Vector3.forward * (2 * step);
                    corridor = navigation.Sample(EnemyNavigationWorld.Profile.RailBeetle, ahead, 1.3f, out Vector3 sample) &&
                        Vector2.Distance(new Vector2(sample.x, sample.z), new Vector2(ahead.x, ahead.z)) < .4f;
                }
                if (!corridor) continue;
                Debug.Log("Rail Beetle safe updated-map spawn: " + point);
                return true;
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(probe); }
        point = default; return false;
    }
}
