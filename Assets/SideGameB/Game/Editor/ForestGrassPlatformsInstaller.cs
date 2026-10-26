// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ForestGrassPlatformsInstaller
{
    private const string RootName = "Two Forest Grass Platforms";
    private const string Art = "Assets/Game/Art/Platforms/";
    static ForestGrassPlatformsInstaller()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.update += InstallAfterPlayMode;
    }

    private static void InstallAfterPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Install();
        EditorApplication.update -= InstallAfterPlayMode;
    }

    [MenuItem("Pixel Runner/Add Two Forest Grass Platforms (Preserve Scene)")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlayerTest" || scene.GetRootGameObjects().Any(x => x.name == RootName)) return;
        Prepare(Art + "ForestGrassPlatformA.png"); Prepare(Art + "ForestGrassPlatformB.png");
        Sprite a = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "ForestGrassPlatformA.png");
        Sprite b = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "ForestGrassPlatformB.png");
        if (a == null || b == null) return;
        Bounds anchor = FindRightmostWalkable();
        float width = Mathf.Max(a.bounds.size.x, b.bounds.size.x);
        float x = anchor.size == Vector3.zero ? 20f : anchor.max.x + 1.5f + width * 0.5f;
        float baseY = anchor.size == Vector3.zero ? 0f : anchor.max.y;
        GameObject root = new GameObject(RootName);
        CreatePlatform("Forest Grass Platform A", root.transform, a, new Vector3(x, baseY + 4.5f, 0f), 0.384f);
        CreatePlatform("Forest Grass Platform B", root.transform, b, new Vector3(x, baseY + 0.8f, 0f), 0.464f);
        Undo.RegisterCreatedObjectUndo(root, "Add Two Forest Grass Platforms");
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); Selection.activeGameObject = root;
    }

    private static void CreatePlatform(string name, Transform parent, Sprite sprite, Vector3 worldSurface, float spriteSurfaceOffset)
    {
        GameObject platform = new GameObject(name); platform.transform.SetParent(parent, false); platform.transform.position = worldSurface;
        BoxCollider2D floor = platform.AddComponent<BoxCollider2D>(); floor.size = new Vector2(sprite.bounds.size.x * 0.96f, 0.25f); floor.offset = new Vector2(0f, -0.125f);
        GameObject visual = new GameObject("Visual"); visual.transform.SetParent(platform.transform, false); visual.transform.localPosition = new Vector3(0f, -spriteSurfaceOffset, 0f);
        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = 2;
    }

    private static void Prepare(string path)
    {
        AssetDatabase.Refresh();
        if (AssetImporter.GetAtPath(path) is TextureImporter i)
        { i.textureType = TextureImporterType.Sprite; i.spritePixelsPerUnit = 220; i.filterMode = FilterMode.Point; i.textureCompression = TextureImporterCompression.Uncompressed; i.alphaIsTransparency = true; i.SaveAndReimport(); }
    }

    private static Bounds FindRightmostWalkable()
    {
        Bounds best = default; float right = float.NegativeInfinity;
        foreach (BoxCollider2D c in Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None))
            if (c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy && c.bounds.size.x >= 1.5f && c.bounds.size.x <= 40f && c.bounds.max.x > right)
            { right = c.bounds.max.x; best = c.bounds; }
        return best;
    }
}
#endif

#endif
