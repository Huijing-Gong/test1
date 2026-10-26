// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class VerticalMovingPlatformInstaller
{
    private const string HorizontalName = "Moving Stone Platform";
    private const string RootName = "Vertical Moving Stone Platform";
    private const string SpritePath = "Assets/Game/Art/Platforms/MovingStonePlatform.png";
    static VerticalMovingPlatformInstaller() { EditorApplication.delayCall += Install; }

    [MenuItem("Pixel Runner/Add Vertical Moving Stone Platform (Preserve Scene)")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlayerTest") return;
        if (scene.GetRootGameObjects().Any(x => x.name == RootName)) return;
        GameObject horizontal = scene.GetRootGameObjects().FirstOrDefault(x => x.name == HorizontalName);
        if (horizontal == null) return;
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        HorizontalMovingPlatform2D horizontalMovement = horizontal.GetComponent<HorizontalMovingPlatform2D>();
        if (sprite == null || horizontalMovement == null) return;

        // At the horizontal platform's right endpoint, the two platform edges meet for transfer.
        float centerGap = sprite.bounds.size.x * 0.94f;
        Vector3 start = horizontal.transform.position + new Vector3(3f + centerGap, 0f, 0f);
        GameObject go = new GameObject(RootName);
        go.transform.position = start;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 3;
        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(sprite.bounds.size.x * 0.94f, 0.28f);
        collider.offset = new Vector2(0f, 0.35f);
        Rigidbody2D body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.useFullKinematicContacts = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.freezeRotation = true;
        VerticalMovingPlatform2D movement = go.AddComponent<VerticalMovingPlatform2D>();
        movement.Configure(4f, 0.9f);

        Undo.RegisterCreatedObjectUndo(go, "Add Vertical Moving Stone Platform");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = go;
    }
}
#endif

#endif
