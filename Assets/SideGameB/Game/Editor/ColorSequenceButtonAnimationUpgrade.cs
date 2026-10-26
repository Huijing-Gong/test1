// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ColorSequenceButtonAnimationUpgrade
{
    private const string RootName = "Color Sequence Puzzle";
    private const string MarkerName = "Real Button Press Animation v3";
    private const string Art = "Assets/Game/Art/ColorSequencePuzzle/";
    static ColorSequenceButtonAnimationUpgrade() { EditorApplication.delayCall += Apply; }

    private static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlayerTest") return;
        GameObject root = scene.GetRootGameObjects().FirstOrDefault(x => x.name == RootName);
        if (root == null || root.transform.Find(MarkerName) != null) return;
        ColorSequencePuzzle puzzle = root.GetComponent<ColorSequencePuzzle>();
        string[] names = { "Red", "Blue", "Green", "Yellow" };
        PuzzleColor[] colors = { PuzzleColor.Red, PuzzleColor.Blue, PuzzleColor.Green, PuzzleColor.Yellow };
        for (int i = 0; i < names.Length; i++)
        {
            Transform sensor = root.transform.Find(names[i] + " Button");
            Transform visual = sensor != null ? sensor.Find("Visual") : null;
            ColorSequenceButton2D button = sensor != null ? sensor.GetComponent<ColorSequenceButton2D>() : null;
            if (button == null || visual == null) continue;
            Sprite half = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "Button" + names[i] + "Half.png");
            Sprite pressed = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "Button" + names[i] + "Pressed.png");
            button.Configure(colors[i], puzzle, visual, 0.45f, half, pressed);
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
