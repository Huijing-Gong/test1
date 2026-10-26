// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class VictoryPortalInstaller
{
    private const string PuzzleName = "Color Sequence Puzzle";
    private const string TriggerName = "Victory Portal Trigger";
    static VictoryPortalInstaller()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.update += InstallAfterPlayMode;
    }

    private static void InstallAfterPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Install(); EditorApplication.update -= InstallAfterPlayMode;
    }

    [MenuItem("Pixel Runner/Add Victory Trigger To Sequence Door")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlayerTest") return;
        GameObject puzzle = scene.GetRootGameObjects().FirstOrDefault(x => x.name == PuzzleName);
        if (puzzle == null) return;
        Transform door = puzzle.transform.Find("Sequence Door");
        if (door == null || door.Find(TriggerName) != null) return;
        GameObject trigger = new GameObject(TriggerName);
        trigger.transform.SetParent(door, false);
        trigger.transform.localPosition = new Vector3(0f, 0.10f, 0f);
        trigger.AddComponent<VictoryPortalTrigger2D>();
        BoxCollider2D area = trigger.AddComponent<BoxCollider2D>();
        area.isTrigger = true;
        area.size = new Vector2(1.20f, 2.35f);
        area.offset = new Vector2(0f, 0.05f);
        Undo.RegisterCreatedObjectUndo(trigger, "Add Victory Portal Trigger");
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
}
#endif

#endif
