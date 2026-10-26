// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class HorizontalMovingPlatformInstaller
{
    private const string RootName = "Moving Stone Platform";
    private const string SpritePath = "Assets/Game/Art/Platforms/MovingStonePlatform.png";
    static HorizontalMovingPlatformInstaller() { EditorApplication.delayCall += Install; }

    [MenuItem("Pixel Runner/Add Moving Stone Platform (Preserve Scene)")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlayerTest") return;
        if (scene.GetRootGameObjects().Any(x => x.name == RootName)) return;

        AssetDatabase.Refresh();
        if (AssetImporter.GetAtPath(SpritePath) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 220f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (sprite == null) return;

        Bounds anchor = FindRightmostWalkable();
        float x = anchor.size == Vector3.zero ? 12f : anchor.max.x + 5.5f;
        float y = anchor.size == Vector3.zero ? 3f : anchor.max.y + 2.4f;
        GameObject go = new GameObject(RootName);
        go.transform.position = new Vector3(x, y, 0f);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 3;

        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(sprite.bounds.size.x * 0.94f, 0.28f);
        // The visible walking edge is around 0.49 local units above the image pivot.
        collider.offset = new Vector2(0f, 0.35f);
        Rigidbody2D body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.useFullKinematicContacts = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.freezeRotation = true;
        HorizontalMovingPlatform2D movement = go.AddComponent<HorizontalMovingPlatform2D>();
        movement.Configure(3f, 1f);

        Undo.RegisterCreatedObjectUndo(go, "Add Moving Stone Platform");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = go;
    }

    private static Bounds FindRightmostWalkable()
    {
        Bounds best = default; float right = float.NegativeInfinity;
        foreach (BoxCollider2D c in Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None))
        {
            if (!c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy || c.bounds.size.x < 1.5f || c.bounds.size.x > 40f) continue;
            if (c.bounds.max.x > right) { right = c.bounds.max.x; best = c.bounds; }
        }
        return best;
    }
}
#endif

#endif
