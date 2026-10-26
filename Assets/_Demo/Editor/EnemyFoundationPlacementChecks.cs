using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Read-only preflight for authored spawns. Includes the body footprint and obstacles, not just a centre ray.
public static class EnemyFoundationPlacementChecks
{
    [MenuItem("Tools/Enemies/Check Enemy Spawn Footprints And Clearance")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before placement checks.");
        Scene scene = SceneManager.GetSceneByPath(EnemyNavigationBuilder.ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(EnemyNavigationBuilder.ScenePath, OpenSceneMode.Additive);
        int count = 0;
        var failures = new List<string>();
        try
        {
            Physics.SyncTransforms();
            foreach (var health in Object.FindObjectsByType<Health>(FindObjectsSortMode.None))
            {
                if (health.gameObject.scene != scene || !(health.GetComponent<StoneScoutRoadsideAI>() ||
                    health.GetComponent<DomainPrototypeEnemy>() || health.GetComponent<RailBeetleAI>() ||
                    health.GetComponent<RepairWispAI>())) continue;
                try { Check(health); count++; }
                catch (Exception error)
                {
                    string suggestion = FindSafe(health, out Vector3 point) ? " Suggested safe pose=" + point.ToString("F3") : " No safe pose within 4 m.";
                    failures.Add(error.Message + suggestion);
                }
            }
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
        if (count == 0) throw new InvalidOperationException("No enemy placements found.");
        Debug.Log("Enemy spawn foundation PASS: " + count + " authored bodies have full floor support and projected physical clearance; no scene saved.");
    }

    public static bool TryFitSpawn(Health health, Vector3 candidate, out Vector3 point)
    {
        try
        {
            point = Check(health, candidate);
            point = Check(health, point);
            Check(health, point);
            return true;
        }
        catch (InvalidOperationException) { point = default; return false; }
    }

    static Vector3 Check(Health health, Vector3? candidate = null)
    {
        Transform actor = health.transform; var query = new EnemyGroundQuery(actor);
        CapsuleCollider capsule = health.GetComponent<CapsuleCollider>();
        if (!capsule) throw new InvalidOperationException(actor.name + " has no physical body.");
        DomainPrototypeEnemy domain = health.GetComponent<DomainPrototypeEnemy>();
        bool prism = domain && domain.EnemyKind == DomainPrototypeEnemy.Kind.RuinPrism;
        BoxCollider box = health.GetComponent<RailBeetleAI>() ? health.GetComponent<BoxCollider>() :
            prism ? health.GetComponentInChildren<BoxCollider>() : null;
        Vector3 scale = actor.lossyScale;
        Vector2 footprint = Vector2.one * capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        Quaternion facing = actor.rotation;
        if (box)
        {
            footprint = new Vector2(box.size.x * Mathf.Abs(box.transform.lossyScale.x) * .5f,
                box.size.z * Mathf.Abs(box.transform.lossyScale.z) * .5f);
            facing = box.transform.rotation;
        }
        float slope = health.GetComponent<StoneScoutRoadsideAI>() ? 35f : health.GetComponent<RailBeetleAI>() ? 31.5f : 27f;
        float curvedSpan = health.GetComponent<RailBeetleAI>() ? .72f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) : 0f;
        Vector3 position = candidate ?? actor.position;
        if (!query.TryFootprint(position - Vector3.up * .015f, facing, footprint,
            slope, .7f, .7f, out float y, !box, curvedSpan))
            throw new InvalidOperationException(actor.name + " lacks full walkable support at " + actor.position);
        // Evaluate the grounded spawn pose without mutating transforms or serialized data.
        Vector3 lift = position - actor.position; lift.y = y + .015f - actor.position.y;
        if (Mathf.Abs(lift.y) > .7f) throw new InvalidOperationException(actor.name + " is on the wrong ground layer.");
        foreach (Collider body in health.GetComponentsInChildren<Collider>(true))
        {
            if (!body.enabled || body.isTrigger) continue;
            foreach (Collider obstacle in Physics.OverlapBox(body.bounds.center + lift, body.bounds.extents + Vector3.one * .01f))
            {
                if (!obstacle || obstacle.isTrigger || obstacle.transform.IsChildOf(actor)) continue;
                if (Physics.ComputePenetration(body, body.transform.position + lift, body.transform.rotation,
                    obstacle, obstacle.transform.position, obstacle.transform.rotation, out _, out float distance) && distance > .04f)
                    throw new InvalidOperationException(actor.name + " spawn body intersects " + obstacle.name + " by " + distance.ToString("F3") + " m.");
            }
        }
        return actor.position + lift;
    }

    static bool FindSafe(Health actor, out Vector3 point)
    {
        point = default;
        foreach (float radius in new[] { .5f, 1f, 1.3f, 2f, 3f, 4f })
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f;
                if (TryFitSpawn(actor, actor.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius, out point)) return true;
            }
        return false;
    }

    public static void RunAll()
    { Run(); RepairWispChecks.RunAll(); }

    [MenuItem("Tools/Enemies/Fix Invalid Enemy Spawns Nearby")]
    public static void FixInvalidPlacements()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before placement changes.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene edits first; nothing changed.");
        Scene scene = EditorSceneManager.OpenScene(EnemyNavigationBuilder.ScenePath);
        var moves = new Dictionary<Transform, Vector3>(); Physics.SyncTransforms();
        foreach (var health in Object.FindObjectsByType<Health>(FindObjectsSortMode.None))
        {
            if (health.gameObject.scene != scene || !(health.GetComponent<StoneScoutRoadsideAI>() ||
                health.GetComponent<DomainPrototypeEnemy>() || health.GetComponent<RailBeetleAI>() || health.GetComponent<RepairWispAI>())) continue;
            try
            {
                Vector3 fit = Check(health);
                if (Vector3.Distance(fit, health.transform.position) > .02f)
                {
                    if (!TryFitSpawn(health, fit, out fit)) throw new InvalidOperationException("Ground-fit pose is not stable.");
                    moves.Add(health.transform, fit);
                }
            }
            catch (InvalidOperationException)
            {
                if (!FindSafe(health, out Vector3 point)) throw new InvalidOperationException(health.name + " has no safe nearby spawn; scene unchanged.");
                moves.Add(health.transform, point);
            }
        }
        foreach (var move in moves)
        {
            Undo.RecordObject(move.Key, "Fit enemy spawn to safe full footprint");
            move.Key.position = move.Value; PrefabUtility.RecordPrefabInstancePropertyModifications(move.Key);
            Debug.Log("Enemy spawn corrected: " + move.Key.name + " -> " + move.Value.ToString("F3"));
        }
        Undo.FlushUndoRecordObjects();
        if (moves.Count > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        Run();
    }
}
