using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class EnemyNavigationBuilder
{
    public const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    public const string Folder = "Assets/_Demo/Navigation";
    private static readonly string[] Names = { "StoneScout", "LeafWisp", "RuinPrism", "RailBeetle" };
    private static readonly float[] Radii = { .60f, .60f, 1.40f, 1.55f };
    private static readonly float[] Slopes = { 35f, 27f, 27f, 31.5f };

    [MenuItem("Tools/Enemies/Bake Shared Safe Navigation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before baking navigation.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene edits before baking. Nothing changed.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var generated = new NavMeshData[4];
        var types = new int[4];
        try
        {
            OverworldV2Layout layout = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<OverworldV2Layout>(true)) { layout = root.GetComponentInChildren<OverworldV2Layout>(true); break; }
            if (!layout) throw new InvalidOperationException("No authored navigation surfaces in this map.");
            var serializedLayout = new SerializedObject(layout);
            GameObject bridge = serializedLayout.FindProperty("northBridge").objectReferenceValue as GameObject;
            var sources = new List<NavMeshBuildSource>();
            Bounds bounds = new Bounds(new Vector3(layout.MapCenter.x, 30f, layout.MapCenter.y),
                new Vector3(layout.MapHalfExtent * 2f, 100f, layout.MapHalfExtent * 2f));
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var source = sources[i]; Collider collider = source.component as Collider;
                if (!collider || collider.gameObject.scene != scene || collider.isTrigger ||
                    collider.GetComponentInParent<Health>() || collider.GetComponentInParent<WaterVolume>() ||
                    (bridge && collider.transform.IsChildOf(bridge.transform)))
                { sources.RemoveAt(i); continue; }
                // Only explicitly authored walking surfaces are floors. Tree trunks/ruins block paths.
                source.area = layout.IsNavigationSurface(collider) ? 0 : 1;
                sources[i] = source;
            }
            foreach (WaterVolume water in Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None))
            {
                if (water.gameObject.scene != scene || !water.IsValid || !water.VolumeCollider) continue;
                Bounds box = water.VolumeCollider.bounds;
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox,
                    transform = Matrix4x4.TRS(box.center, Quaternion.identity, Vector3.one), size = box.size, area = 1 });
            }
            if (sources.Count < 1) throw new InvalidOperationException("No valid source colliders; scene unchanged.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Demo", "Navigation");
            for (int i = 0; i < generated.Length; i++)
            {
                NavMeshBuildSettings settings = Settings(i);
                types[i] = settings.agentTypeID;
                generated[i] = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
                if (!generated[i]) throw new InvalidOperationException("Navigation bake failed for " + Names[i]);
                generated[i].name = "Enemy navigation " + Names[i];
            }
            // Unload the old native tiles BEFORE modifying their serialized data.
            // Copying a live NavMeshData first can corrupt tile links inside Unity's native manager.
            EnemyNavigationWorld navigation = EnemyNavigationWorld.Find(scene);
            if (navigation) navigation.enabled = false;
            var assets = new NavMeshData[4];
            for (int i = 0; i < assets.Length; i++)
            {
                string path = Folder + "/" + Names[i] + "Navigation.asset";
                NavMeshData existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
                if (existing) { EditorUtility.CopySerialized(generated[i], existing); EditorUtility.SetDirty(existing); assets[i] = existing; }
                else { AssetDatabase.CreateAsset(generated[i], path); assets[i] = generated[i]; generated[i] = null; }
            }
            if (!navigation)
            {
                var root = new GameObject("_EnemySafeNavigation"); SceneManager.MoveGameObjectToScene(root, scene);
                navigation = root.AddComponent<EnemyNavigationWorld>();
            }
            // Re-register after replacing serialized data. Never attach auto-moving NavMeshAgents to the enemies.
            navigation.enabled = false;
            var data = new SerializedObject(navigation);
            var array = data.FindProperty("bakedData"); array.arraySize = 4;
            var ids = data.FindProperty("agentTypes"); ids.arraySize = 4;
            for (int i = 0; i < 4; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = assets[i];
            for (int i = 0; i < 4; i++) ids.GetArrayElementAtIndex(i).intValue = types[i];
            data.ApplyModifiedPropertiesWithoutUndo(); navigation.enabled = true;
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("Enemy navigation baked: 4 shared body/slope profiles, " + sources.Count +
                " physical sources. Actors retain their own collision, leash, hover height and attacks. Bridge/water are excluded.");
        }
        finally
        {
            foreach (NavMeshData data in generated) if (data) Object.DestroyImmediate(data);
            if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static NavMeshBuildSettings Settings(int i)
    {
        string name = "ANCA Enemy " + Names[i];
        NavMeshBuildSettings result = default; bool found = false;
        for (int j = 0; j < NavMesh.GetSettingsCount(); j++)
        {
            var candidate = NavMesh.GetSettingsByIndex(j);
            if (NavMesh.GetSettingsNameFromID(candidate.agentTypeID) == name) { result = candidate; found = true; break; }
        }
        if (!found) result = NavMesh.CreateSettings();
        result.agentRadius = Radii[i]; result.agentHeight = i == 3 ? 1.6f : 2f;
        result.agentSlope = Slopes[i]; result.agentClimb = .15f;
        result.overrideVoxelSize = true; result.voxelSize = .12f;
        result.overrideTileSize = true; result.tileSize = 128; result.minRegionArea = 1f;
        // Match the package's Navigation window: persist only these four named agent entries.
        var project = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"));
        var settings = project.FindProperty("m_Settings");
        var names = project.FindProperty("m_SettingNames");
        for (int j = 0; j < settings.arraySize; j++)
        {
            var entry = settings.GetArrayElementAtIndex(j);
            if (entry.FindPropertyRelative("agentTypeID").intValue != result.agentTypeID) continue;
            names.GetArrayElementAtIndex(j).stringValue = name;
            entry.FindPropertyRelative("agentRadius").floatValue = result.agentRadius;
            entry.FindPropertyRelative("agentHeight").floatValue = result.agentHeight;
            entry.FindPropertyRelative("agentSlope").floatValue = result.agentSlope;
            entry.FindPropertyRelative("agentClimb").floatValue = result.agentClimb;
            entry.FindPropertyRelative("manualCellSize").boolValue = true;
            entry.FindPropertyRelative("cellSize").floatValue = .12f;
            entry.FindPropertyRelative("manualTileSize").boolValue = true;
            entry.FindPropertyRelative("tileSize").intValue = 128;
            entry.FindPropertyRelative("minRegionArea").floatValue = 1f;
            break;
        }
        project.ApplyModifiedPropertiesWithoutUndo();
        return result;
    }
}
