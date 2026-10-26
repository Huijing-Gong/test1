using System;
using System.Linq;
using KingOfGraphic.MechBossMigration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StoneBossIslandSetup
{
    public const string RootName = "_StoneBossLakeIsland";
    public const string WorldPath = "Assets/_Demo/Scenes/OverworldBaseline.unity";

    [MenuItem("Tools/Stone Boss/Place on lake island")]
    public static void Install()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != WorldPath)
            throw new InvalidOperationException("Open the main world in edit mode first.");
        if (scene.GetRootGameObjects().Any(x => x.name == RootName))
            throw new InvalidOperationException("The lake island encounter already exists; edit that instance.");

        var floor = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
            .First(x => x.gameObject.scene == scene && x.name == "Own unified main-world floor collider");
        var water = UnityEngine.Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None)
            .First(x => x.name == "Own continuous lake swimming volume");
        var player = UnityEngine.Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)
            .First(x => x.gameObject.scene == scene && x.gameObject.activeInHierarchy);
        var centerPosition = new Vector3(-53.06f, 5.75f, 29.76f);
        Physics.SyncTransforms();
        if (!floor.Raycast(new Ray(centerPosition + Vector3.up * 5, Vector3.down), out var hit, 10))
            throw new InvalidOperationException("No real island ground below the encounter center.");
        centerPosition.y = hit.point.y;

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/MechBossMigration/Compact/MechStoneSentinelCompact.prefab");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        Undo.RegisterCreatedObjectUndo(root, "Place lake island stone boss");
        root.name = RootName;
        root.transform.position = centerPosition;
        var center = new GameObject("Lake island arena center");
        center.transform.SetParent(root.transform, false);
        var encounter = root.GetComponentInChildren<MechBossEncounter>(true);
        var boss = encounter.boss;
        boss.transform.position = centerPosition + Vector3.forward * 2.8f;
        boss.transform.rotation = Quaternion.Euler(0, 180, 0);
        boss.arenaCenter = center.transform;
        boss.arenaMoveRadius = 8.8f;
        boss.engageRadius = 8;
        boss.meleeStartRange = 3.6f;
        boss.showDebugHud = false;
        boss.player = player;
        encounter.player = player;
        var feedback = boss.GetComponent<MistGardenBossFeedback>();
        feedback.displayName = "Stone Garden Sentinel";
        feedback.enableDebugPreview = feedback.showDebugHud = false;

        var constraint = Undo.AddComponent<MechIslandGroundConstraint>(boss.gameObject);
        constraint.arenaCenter = center.transform;
        constraint.groundCollider = floor;
        constraint.moveRadius = 8.8f;
        constraint.playerContainmentRadius = 10.6f;
        constraint.waterSurfaceHeight = water.WaterSurfaceY;
        constraint.footprintRadius = 1.35f;

        ConfigureShoreline(encounter, constraint);
        encounter.barrier.SetActive(false);
        foreach (var modified in new UnityEngine.Object[] { root.transform, boss.transform, boss,
                     encounter, feedback, constraint, encounter.barrier.transform })
        {
            EditorUtility.SetDirty(modified);
            PrefabUtility.RecordPrefabInstancePropertyModifications(modified);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
    }

    public static void ConfigureShoreline(MechBossEncounter encounter, MechIslandGroundConstraint constraint)
    {
        if (!encounter.barrier.GetComponent<CameraCollisionPassthrough>())
            Undo.AddComponent<CameraCollisionPassthrough>(encounter.barrier);
        var boss = encounter.boss;
        boss.arenaMoveRadius = constraint.moveRadius = 7.8f;
        constraint.playerContainmentRadius = 8.6f;
        // Combat ends at the flat plateau. The outer slope/shoreline is excluded;
        // changing terrain later only requires retuning this authored boundary.
        encounter.barrier.transform.localScale = new Vector3(9.4f / 15, 1, 9.4f / 15);
        foreach (var box in encounter.barrier.GetComponentsInChildren<BoxCollider>(true))
        {
            box.size = new Vector3(box.size.x, 12, box.size.z);
            box.center = new Vector3(0, -1.5f, 0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(box);
        }
        var pixels = encounter.barrier.GetComponentInChildren<KingOfGraphic.MechBossMigration.PixelShieldController>(true);
        pixels.ConfigureGeometry(15, 10.5f, 96);
        var reveal = encounter.barrier.GetComponent<MechArenaBarrierReveal>();
        reveal.wallHeight = 10.5f;
        reveal.baseOffset = -1.5f;
        foreach (var modified in new UnityEngine.Object[] { boss, constraint, pixels, reveal, encounter.barrier.transform })
        {
            EditorUtility.SetDirty(modified);
            PrefabUtility.RecordPrefabInstancePropertyModifications(modified);
        }
    }
}
