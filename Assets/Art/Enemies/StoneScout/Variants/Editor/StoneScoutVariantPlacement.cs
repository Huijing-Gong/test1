using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One compact encounter area; leaves the rest of the overworld free for other enemies.
public static class StoneScoutVariantPlacement
{
    private const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    private const string CombatPrefabPath = "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab";
    private const float MinSpacing = 3.5f;
    private static readonly Vector2[] Points =
    {
        new Vector2(40f, -22f), new Vector2(50f, -22f),
        new Vector2(40f, -11f), new Vector2(50f, -11f),
        new Vector2(40f,   0f), new Vector2(50f,   0f)
    };
    private static readonly string[] Names =
    {
        "StoneScout_Forest_01", "StoneScout_Forest_02",
        "StoneScout_Sand_01", "StoneScout_Sand_02",
        "StoneScout_Awakened_01", "StoneScout_Awakened_02"
    };

    public static void Preflight()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try {
            if (EnemyDistributionInstaller.HasLayout(scene)) EnemyDistributionInstaller.ValidateAuthoring();
            else Validate(scene, false, out _);
        }
        finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
    }

    [MenuItem("Tools/Art/Place Stone Scout Variants Together")]
    public static void Place()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before placing variants.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (EnemyDistributionInstaller.HasLayout(scene))
        {
            EnemyDistributionInstaller.ValidateAuthoring();
            Debug.Log("Regional P0 layout preserved; legacy six-Scout grid placement skipped.");
            return;
        }
        Vector3[] destinations = Validate(scene, true, out Transform group);
        for (int i = 0; i < Points.Length; i++)
        {
            Transform enemy = group.GetChild(i);
            Undo.RecordObject(enemy, "Group Stone Scout variants");
            enemy.position = destinations[i];
            enemy.name = Names[i];
            StoneScoutAppearance appearance = enemy.GetComponent<StoneScoutAppearance>();
            Undo.RecordObject(appearance, "Set Stone Scout appearance");
            appearance.SetLook(
                i < 2 ? StoneScoutAppearance.Look.Forest :
                i < 4 ? StoneScoutAppearance.Look.Sand : StoneScoutAppearance.Look.Awakened);
            PrefabUtility.RecordPrefabInstancePropertyModifications(appearance);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Grouped six Stone Scouts in the eastern encounter area. " +
            "Forest: " + destinations[0] + ", " + destinations[1] +
            "; Sand: " + destinations[2] + ", " + destinations[3] +
            "; Awakened: " + destinations[4] + ", " + destinations[5]);
    }

    private static Vector3[] Validate(Scene scene, bool requireAppearance, out Transform group)
    {
        group = null;
        MeshCollider ground = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "_EnemyCombatPreview") group = root.transform;
            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
                if (collider.name == "LakeTerrain_REPLACE_OR_SCULPT") ground = collider;
        }
        if (group == null || group.childCount != Points.Length || ground == null)
            throw new InvalidOperationException("Expected six existing Stone Scouts and V2 terrain; scene unchanged.");

        // Find six nearby, level, separated spots before moving the first enemy.
        Physics.SyncTransforms();
        var destinations = new Vector3[Points.Length];
        for (int i = 0; i < Points.Length; i++)
        {
            Transform enemy = group.GetChild(i);
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(enemy.gameObject) != CombatPrefabPath ||
                (requireAppearance && enemy.GetComponent<StoneScoutAppearance>() == null) ||
                !enemy.name.StartsWith("StoneScout_", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected encounter child: " + enemy.name);
            if (!TryFindPosition(scene, group, ground, Points[i], destinations, i, out destinations[i]))
                throw new InvalidOperationException("No clear, walkable spot near " + Points[i] + "; scene unchanged.");
        }
        return destinations;
    }

    private static bool TryFindPosition(Scene scene, Transform group, MeshCollider ground,
        Vector2 preferred, Vector3[] chosen, int count, out Vector3 result)
    {
        // Keep the full set in a single 18 x 30 metre patch of the eastern map.
        for (int radius = 0; radius <= 9; radius++)
        for (int dx = -radius; dx <= radius; dx++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius) continue;
            float x = preferred.x + dx;
            float z = preferred.y + dz;
            if (x < 36f || x > 54f || z < -26f || z > 4f) continue;
            if (!TryLevelGround(ground, x, z, out Vector3 groundPoint)) continue;

            bool separated = true;
            for (int i = 0; i < count; i++)
                if (Vector2.Distance(new Vector2(x, z), new Vector2(chosen[i].x, chosen[i].z)) < MinSpacing)
                    separated = false;
            if (!separated) continue;

            // The body capsule must not intersect nearby static props or ruins.
            Vector3 lower = groundPoint + Vector3.up * 0.6f;
            Vector3 upper = groundPoint + Vector3.up * 1.25f;
            bool blocked = false;
            foreach (Collider collider in Physics.OverlapCapsule(lower, upper, 0.53f))
            {
                if (collider == ground || collider.isTrigger || collider.gameObject.scene != scene ||
                    collider.transform.IsChildOf(group)) continue;
                blocked = true;
                break;
            }
            if (blocked) continue;

            result = groundPoint + Vector3.up * 0.015f;
            return true;
        }
        result = default;
        return false;
    }

    private static bool TryLevelGround(MeshCollider ground, float x, float z, out Vector3 point)
    {
        point = default;
        if (!Probe(ground, x, z, out RaycastHit centre)) return false;
        point = centre.point;
        // A valid centre alone can put half the enemy over a cliff or inside a rock.
        const float footprint = 0.45f;
        Vector2[] offsets =
        {
            new Vector2(footprint, 0), new Vector2(-footprint, 0),
            new Vector2(0, footprint), new Vector2(0, -footprint)
        };
        foreach (Vector2 offset in offsets)
            if (!Probe(ground, x + offset.x, z + offset.y, out RaycastHit edge) ||
                Mathf.Abs(edge.point.y - point.y) > 0.32f)
                return false;
        return true;
    }

    private static bool Probe(MeshCollider ground, float x, float z, out RaycastHit hit)
    {
        return ground.Raycast(new Ray(new Vector3(x, 100f, z), Vector3.down), out hit, 200f) &&
            Vector3.Angle(hit.normal, Vector3.up) <= 30f;
    }
}
