// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class LongStoneWallPlatformInstaller
{
    private const string RootName = "Long Stone Wall Platform";
    private const string SpritePath = "Assets/Game/Art/Platforms/LongStoneWallPlatform.png";
    static LongStoneWallPlatformInstaller()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.update += InstallAfterPlayMode;
    }

    private static void InstallAfterPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Install(); EditorApplication.update -= InstallAfterPlayMode;
    }

    [MenuItem("Pixel Runner/Add Long Stone Wall Platform (Preserve Scene)")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlayerTest" || scene.GetRootGameObjects().Any(x => x.name == RootName)) return;
        AssetDatabase.Refresh();
        if (AssetImporter.GetAtPath(SpritePath) is TextureImporter i)
        { i.textureType = TextureImporterType.Sprite; i.spritePixelsPerUnit = 220; i.filterMode = FilterMode.Point; i.textureCompression = TextureImporterCompression.Uncompressed; i.alphaIsTransparency = true; i.SaveAndReimport(); }
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath); if (sprite == null) return;
        Bounds anchor = FindRightmostWalkable();
        float x = anchor.size == Vector3.zero ? 20f : anchor.max.x + 1.5f + sprite.bounds.size.x * 0.5f;
        float y = anchor.size == Vector3.zero ? 0f : anchor.max.y + 1f;
        GameObject root = new GameObject(RootName); root.transform.position = new Vector3(x, y, 0f);
        BoxCollider2D floor = root.AddComponent<BoxCollider2D>(); floor.size = new Vector2(sprite.bounds.size.x * 0.96f, 0.28f); floor.offset = new Vector2(0f, -0.14f);
        GameObject visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false); visual.transform.localPosition = new Vector3(0f, -0.386f, 0f);
        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = 2;
        Undo.RegisterCreatedObjectUndo(root, "Add Long Stone Wall Platform");
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); Selection.activeGameObject = root;
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
