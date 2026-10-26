// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ExtraLongGrassPlatformInstaller
{
    private const string ScenePath = "Assets/Game/Scenes/PlayerTest.unity";
    private const string SpritePath = "Assets/Game/Art/Platforms/ExtraLongGrassPlatform.png";
    private const string PlatformName = "Extra Long Grass Platform";

    static ExtraLongGrassPlatformInstaller()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Pixel Runner/Add Extra Long Grass Platform To Scene")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || GameObject.Find(PlatformName) != null)
            return;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath && scene.name != "PlayerTest")
            return;

        Sprite sprite = ImportSprite();
        if (sprite == null)
            return;

        Collider2D rightmost = FindRightmostPlatform();
        float surfaceY = rightmost != null ? rightmost.bounds.max.y : 1.4f;
        float leftEdge = rightmost != null ? rightmost.bounds.max.x + 1.3f : 43f;
        float platformWidth = sprite.texture.width / 220f;

        GameObject platform = new GameObject(PlatformName);
        Undo.RegisterCreatedObjectUndo(platform, "Add Extra Long Grass Platform");
        platform.transform.position = new Vector3(leftEdge + platformWidth * 0.5f, surfaceY - 0.28f, 0f);

        SpriteRenderer renderer = platform.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1;

        BoxCollider2D collider = platform.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(platformWidth * 0.96f, 0.14f);
        collider.offset = new Vector2(0f, 0.28f);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = platform;
        Debug.Log("Pixel Runner: Extra Long Grass Platform is now a real Scene object. Press Cmd+S to save.");
    }

    private static Collider2D FindRightmostPlatform()
    {
        Collider2D rightmost = null;
        float edge = float.MinValue;
        foreach (Collider2D candidate in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (!candidate.enabled || candidate.isTrigger || !candidate.name.Contains("Platform"))
                continue;
            if (candidate.bounds.max.x <= edge)
                continue;
            edge = candidate.bounds.max.x;
            rightmost = candidate;
        }
        return rightmost;
    }

    private static Sprite ImportSprite()
    {
        AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null)
            return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 220f;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
    }
}

#endif
