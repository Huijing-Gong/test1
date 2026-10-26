// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ColorSequencePuzzleMechanicsFix
{
    private const string RootName = "Color Sequence Puzzle";
    private const string MarkerName = "Platform And Button Mechanics v2";

    static ColorSequencePuzzleMechanicsFix() { EditorApplication.delayCall += Apply; }

    [MenuItem("Pixel Runner/Fix Color Puzzle Platform And Buttons")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlayerTest") return;
        GameObject root = scene.GetRootGameObjects().FirstOrDefault(x => x.name == RootName);
        if (root == null || root.transform.Find(MarkerName) != null) return;

        ColorSequencePuzzle puzzle = root.GetComponent<ColorSequencePuzzle>();
        Transform platform = root.transform.Find("Puzzle Platform");
        if (puzzle == null || platform == null) return;

        // The supplied sprite's visible top is 0.536 local units above its pivot.
        // At scale 1.6 this places the visible walking surface exactly at root Y = 0.
        platform.localPosition = new Vector3(0f, -0.858f, 0f);
        BoxCollider2D floor = platform.GetComponent<BoxCollider2D>();
        if (floor != null)
        {
            floor.size = new Vector2(9.55f, 0.22f);
            floor.offset = new Vector2(0f, 0.426f);
        }

        string[] names = { "Red", "Blue", "Green", "Yellow" };
        PuzzleColor[] colors = { PuzzleColor.Red, PuzzleColor.Blue, PuzzleColor.Green, PuzzleColor.Yellow };
        for (int i = 0; i < names.Length; i++)
        {
            Transform sensor = root.transform.Find(names[i] + " Button");
            if (sensor == null) continue;
            sensor.localPosition = new Vector3(sensor.localPosition.x, 0.273f, sensor.localPosition.z);
            Transform visual = sensor.Find("Visual");
            if (visual == null) continue;
            visual.localPosition = Vector3.zero;

            BoxCollider2D trigger = sensor.GetComponent<BoxCollider2D>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
                trigger.size = new Vector2(1.15f, 0.45f);
                trigger.offset = new Vector2(0f, 0.20f);
            }

            // This top collider carries the princess and descends with the visual button.
            BoxCollider2D top = visual.GetComponent<BoxCollider2D>();
            if (top == null) top = visual.gameObject.AddComponent<BoxCollider2D>();
            SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
            float width = renderer != null && renderer.sprite != null ? renderer.sprite.bounds.size.x * 0.78f : 0.9f;
            float topY = renderer != null && renderer.sprite != null ? renderer.sprite.bounds.max.y : 0.27f;
            top.isTrigger = false;
            top.size = new Vector2(width, 0.12f);
            top.offset = new Vector2(0f, topY - 0.06f);

            ColorSequenceButton2D button = sensor.GetComponent<ColorSequenceButton2D>();
            if (button != null) button.Configure(colors[i], puzzle, visual, 0.45f);
        }

        GameObject marker = new GameObject(MarkerName);
        marker.transform.SetParent(root.transform, false);
        marker.hideFlags = HideFlags.HideInHierarchy;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
#endif

#endif
