using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GameInteractionSetup
{
    [MenuItem("ArtLab/Game/Update interaction prompts and arena")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Exit Play Mode and save the current scene first.");
        var scene = EditorSceneManager.OpenScene(ChestDemoSetup.ScenePath);
        ConfigureScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorSceneManager.SaveScene(scene, TeamShaderSetup.StartScene, true);
        Debug.Log("GAME_INTERACTIONS_INSTALLED");
    }

    public static void ConfigureScene()
    {
        var guide = UnityEngine.Object.FindFirstObjectByType<TeamShaderShowcase>();
        if (!guide || !guide.sword || !guide.shield) throw new InvalidOperationException("Missing scene effects.");
        if (!guide.sword.GetComponent<SwordInteractionHint>()) guide.sword.gameObject.AddComponent<SwordInteractionHint>();
        var arena = GameObject.Find("Arena");
        var bounds = GameObject.Find("CircularPlatform").GetComponent<Renderer>().bounds;
        Vector3 center = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
        float height = new SerializedObject(guide.shield).FindProperty("height").floatValue;
        var previous = arena.transform.Find("ArenaBlockingWalls");
        if (previous) UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var walls = new GameObject("ArenaBlockingWalls", typeof(NonClimbableSurface));
        walls.transform.SetParent(arena.transform);
        walls.transform.position = center;
        const int segments = 64;
        float length = 2 * radius * Mathf.Tan(Mathf.PI / segments) + .05f;
        float wallHeight = height + 2;
        for (int i = 0; i < segments; i++)
        {
            float angle = 2 * Mathf.PI * i / segments;
            Vector3 normal = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var wall = new GameObject("BarrierWall", typeof(BoxCollider));
            wall.transform.SetParent(walls.transform, false);
            wall.transform.localPosition = normal * radius + Vector3.up * (wallHeight * .5f - .2f);
            wall.transform.localRotation = Quaternion.LookRotation(normal);
            wall.GetComponent<BoxCollider>().size = new Vector3(length, wallHeight, .3f);
        }
        var gate = arena.GetComponent<ArenaBarrierLock>();
        if (!gate) gate = arena.AddComponent<ArenaBarrierLock>();
        gate.Configure(guide.shield, walls, center, radius, height);
        EditorUtility.SetDirty(gate);
    }
}
