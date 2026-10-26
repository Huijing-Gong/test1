// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class FloatingPlatformInstaller
{
    private const string ScenePath = "Assets/Game/Scenes/PlayerTest.unity";
    private const string SpritePath = "Assets/Game/Art/Platforms/FloatingStonePlatform.png";
    private const string ObjectName = "Floating Stone Platform";
    private const string InstallKey = "PixelRunner.FloatingStonePlatform.Installed.v1";

    static FloatingPlatformInstaller()
    {
        EditorApplication.delayCall += InstallOnce;
    }

    [MenuItem("Pixel Runner/Add Floating Platform (Preserve Scene)")]
    public static void InstallFromMenu()
    {
        Install(true);
    }

    private static void InstallOnce()
    {
        if (!EditorPrefs.GetBool(InstallKey, false))
            Install(false);
    }

    private static void Install(bool showMessage)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (showMessage)
                Debug.LogWarning("Pixel Runner: open PlayerTest scene first, then add the floating platform.");
            return;
        }

        GameObject existing = GameObject.Find(ObjectName);
        if (existing != null)
        {
            EditorPrefs.SetBool(InstallKey, true);
            Selection.activeGameObject = existing;
            return;
        }

        Sprite sprite = ImportSprite();
        if (sprite == null)
            return;

        var platform = new GameObject(ObjectName);
        Undo.RegisterCreatedObjectUndo(platform, "Add Floating Stone Platform");
        const float surfaceY = 1.25f;
        platform.transform.position = new Vector3(20.2f, surfaceY - sprite.bounds.max.y, 0f);

        SpriteRenderer renderer = platform.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1;

        BoxCollider2D collider = platform.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(sprite.bounds.size.x * 0.94f, 0.12f);
        collider.offset = new Vector2(0f, sprite.bounds.max.y - 0.06f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorPrefs.SetBool(InstallKey, true);
        Selection.activeGameObject = platform;
        Debug.Log("Pixel Runner: floating platform added without rebuilding the scene. Save the scene to keep it.");
    }

    private static Sprite ImportSprite()
    {
        AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null)
            return null;

        bool needsUpdate = importer.textureType != TextureImporterType.Sprite ||
                           !Mathf.Approximately(importer.spritePixelsPerUnit, 330f) ||
                           importer.filterMode != FilterMode.Point || importer.mipmapEnabled ||
                           importer.textureCompression != TextureImporterCompression.Uncompressed;
        if (needsUpdate)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 330f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
    }
}

#endif
