// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ColorSequencePuzzleInstaller
{
    private const string RootName = "Color Sequence Puzzle";
    private const string Art = "Assets/Game/Art/ColorSequencePuzzle/";

    static ColorSequencePuzzleInstaller() { EditorApplication.delayCall += InstallWhenReady; }

    [MenuItem("Pixel Runner/Add Color Sequence Puzzle (Preserve Scene)")]
    public static void InstallFromMenu() { Install(true); }

    private static void InstallWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (SceneManager.GetActiveScene().name == "PlayerTest") Install(false);
    }

    private static void Install(bool showMessage)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (showMessage) EditorUtility.DisplayDialog("Color Puzzle", "Please stop Play Mode first.", "OK");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlayerTest") return;
        if (scene.GetRootGameObjects().Any(go => go.name == RootName))
        {
            if (showMessage) EditorUtility.DisplayDialog("Color Puzzle", "The puzzle already exists. Nothing was changed.", "OK");
            return;
        }

        PrepareSprites();
        Sprite platformSprite = Load("PuzzlePlatformBase.png");
        Sprite runeBase = Load("RuneStripUnlit.png");
        Sprite doorClosed = Load("DoorClosed.png");
        Sprite doorOpen = Load("DoorOpen.png");
        if (platformSprite == null || runeBase == null || doorClosed == null || doorOpen == null) return;

        Bounds anchor = FindRightmostWalkableBounds();
        float surfaceY = anchor.size == Vector3.zero ? 0f : anchor.max.y;
        float platformWidth = platformSprite.bounds.size.x * 1.6f;
        float centerX = (anchor.size == Vector3.zero ? 24f : anchor.max.x + 1.25f + platformWidth * 0.5f);

        GameObject root = new GameObject(RootName);
        root.transform.position = new Vector3(centerX, surfaceY, 0f);
        ColorSequencePuzzle puzzle = root.AddComponent<ColorSequencePuzzle>();

        GameObject platform = SpriteObject("Puzzle Platform", root.transform, platformSprite, 1.6f, 2);
        platform.transform.localPosition = new Vector3(0f, -0.42f, 0f);
        BoxCollider2D floor = platform.AddComponent<BoxCollider2D>();
        floor.size = new Vector2(platformSprite.bounds.size.x * 0.97f, 0.30f);
        floor.offset = new Vector2(0f, 0.39f);

        PuzzleColor[] colors = { PuzzleColor.Red, PuzzleColor.Blue, PuzzleColor.Green, PuzzleColor.Yellow };
        string[] names = { "Red", "Blue", "Green", "Yellow" };
        float[] xs = { -5.35f, -2.05f, 1.25f, 4.55f };
        ColorSequenceButton2D[] buttons = new ColorSequenceButton2D[4];
        for (int i = 0; i < 4; i++)
        {
            GameObject sensor = new GameObject(names[i] + " Button");
            sensor.transform.SetParent(root.transform, false);
            sensor.transform.localPosition = new Vector3(xs[i], 0.16f, 0f);
            BoxCollider2D trigger = sensor.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true; trigger.size = new Vector2(1.15f, 0.34f);
            GameObject visual = SpriteObject("Visual", sensor.transform, Load("Button" + names[i] + ".png"), 1f, 6);
            visual.transform.localPosition = Vector3.zero;
            buttons[i] = sensor.AddComponent<ColorSequenceButton2D>();
            buttons[i].Configure(colors[i], puzzle, visual.transform);
        }

        GameObject runeRoot = SpriteObject("Overhead Rune Stone", root.transform, runeBase, 0.76f, 3);
        runeRoot.transform.localPosition = new Vector3(-0.6f, 3.15f, 0f);
        string[] order = { "Blue", "Green", "Yellow", "Red" };
        SpriteRenderer[] lights = new SpriteRenderer[4];
        for (int i = 0; i < order.Length; i++)
        {
            GameObject light = SpriteObject(order[i] + " Rune Light", runeRoot.transform, Load("RuneLit" + order[i] + ".png"), 1f, 4);
            light.transform.localPosition = Vector3.zero;
            lights[i] = light.GetComponent<SpriteRenderer>();
            lights[i].enabled = false;
        }

        GameObject door = SpriteObject("Sequence Door", root.transform, doorClosed, 0.75f, 5);
        door.transform.localPosition = new Vector3(7.05f, -doorClosed.bounds.min.y * 0.75f, 0f);
        BoxCollider2D blocker = door.AddComponent<BoxCollider2D>();
        blocker.size = new Vector2(doorClosed.bounds.size.x * 0.46f, doorClosed.bounds.size.y * 0.73f);
        blocker.offset = new Vector2(0f, doorClosed.bounds.size.y * 0.10f);
        puzzle.Configure(buttons, lights, door.GetComponent<SpriteRenderer>(), doorOpen, blocker);

        Undo.RegisterCreatedObjectUndo(root, "Add Color Sequence Puzzle");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        if (showMessage) EditorUtility.DisplayDialog("Color Puzzle", "Added the isolated color-sequence puzzle. Existing scene objects were not changed.", "OK");
    }

    private static GameObject SpriteObject(string name, Transform parent, Sprite sprite, float scale, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * scale;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite; renderer.sortingOrder = order;
        return go;
    }

    private static Bounds FindRightmostWalkableBounds()
    {
        Bounds best = default; float right = float.NegativeInfinity;
        foreach (BoxCollider2D c in UnityEngine.Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None))
        {
            if (!c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy || c.bounds.size.x < 1.5f || c.bounds.size.x > 40f) continue;
            if (c.bounds.max.x > right) { right = c.bounds.max.x; best = c.bounds; }
        }
        return best;
    }

    private static Sprite Load(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + file);

    private static void PrepareSprites()
    {
        AssetDatabase.Refresh();
        foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { Art }).Select(AssetDatabase.GUIDToAssetPath))
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
            bool changed = importer.textureType != TextureImporterType.Sprite || importer.spritePixelsPerUnit != 220 || importer.filterMode != FilterMode.Point || importer.textureCompression != TextureImporterCompression.Uncompressed;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 220;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            if (changed) importer.SaveAndReimport();
        }
        AssetDatabase.Refresh();
    }
}
#endif

#endif
