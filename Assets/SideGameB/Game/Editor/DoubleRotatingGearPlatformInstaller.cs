// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class DoubleRotatingGearPlatformInstaller
{
    private const string ScenePath = "Assets/Game/Scenes/PlayerTest.unity";
    private const string BaseSpritePath = "Assets/Game/Art/Hazards/DoubleGearPlatformBase.png";
    private const string GearSpritePath = "Assets/Game/Art/Hazards/SpikedGear.png";
    private const string RootName = "Double Rotating Gear Platform";
    private const float PixelsPerUnit = 220f;

    static DoubleRotatingGearPlatformInstaller()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Pixel Runner/Add Double Rotating Gear Platform To Scene")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath && scene.name != "PlayerTest")
            return;

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (EnsureWalkableTop(existing))
                EditorSceneManager.MarkSceneDirty(scene);
            return;
        }

        Sprite baseSprite = ImportSprite(BaseSpritePath);
        Sprite gearSprite = ImportSprite(GearSpritePath);
        if (baseSprite == null || gearSprite == null)
            return;

        Collider2D rightmost = FindRightmostPlatform();
        float targetSurfaceY = rightmost != null ? rightmost.bounds.max.y : 1.4f;
        float leftEdge = rightmost != null ? rightmost.bounds.max.x + 1.3f : 64f;
        float baseWidth = baseSprite.bounds.size.x;
        float baseHeight = baseSprite.bounds.size.y;
        float surfaceLocalY = baseHeight * 0.305f;

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Add Double Rotating Gear Platform");
        root.transform.position = new Vector3(leftEdge + baseWidth * 0.5f, targetSurfaceY - surfaceLocalY, 0f);

        GameObject baseObject = new GameObject("Static Platform And Ropes");
        baseObject.transform.SetParent(root.transform, false);
        SpriteRenderer baseRenderer = baseObject.AddComponent<SpriteRenderer>();
        baseRenderer.sprite = baseSprite;
        baseRenderer.sortingOrder = 2;

        BoxCollider2D topCollider = root.AddComponent<BoxCollider2D>();
        topCollider.size = new Vector2(baseWidth * 0.94f, 0.28f);
        topCollider.offset = new Vector2(0f, surfaceLocalY - 0.14f);

        float gearWorldSize = baseWidth * 0.245f;
        float gearScale = gearWorldSize / gearSprite.bounds.size.x;
        float gearY = -baseHeight * 0.262f;
        CreateGear(root.transform, "Left Rotating Gear", -baseWidth * 0.184f, gearY, gearSprite, gearScale, 95f);
        CreateGear(root.transform, "Right Rotating Gear", baseWidth * 0.245f, gearY, gearSprite, gearScale, -95f);
        EnsureWalkableTop(root);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        Debug.Log("Pixel Runner: Double rotating gear platform added as real Scene objects. Press Cmd+S to save.");
    }

    private static bool EnsureWalkableTop(GameObject root)
    {
        Transform artwork = root.transform.Find("Static Platform And Ropes");
        SpriteRenderer renderer = artwork != null ? artwork.GetComponent<SpriteRenderer>() : null;
        if (renderer == null || renderer.sprite == null)
            return false;

        float width = renderer.sprite.bounds.size.x;
        float surfaceLocalY = renderer.sprite.bounds.size.y * 0.305f;
        Transform existingTop = root.transform.Find("Walkable Top Collider");
        GameObject top = existingTop != null ? existingTop.gameObject : new GameObject("Walkable Top Collider");
        bool changed = existingTop == null;
        if (existingTop == null)
        {
            Undo.RegisterCreatedObjectUndo(top, "Add walkable gear-platform top");
            top.transform.SetParent(root.transform, false);
        }

        Vector3 wantedPosition = new Vector3(0f, surfaceLocalY - 0.18f, 0f);
        if (Vector3.Distance(top.transform.localPosition, wantedPosition) > 0.001f)
        {
            Undo.RecordObject(top.transform, "Align walkable gear-platform top");
            top.transform.localPosition = wantedPosition;
            changed = true;
        }

        BoxCollider2D collider = top.GetComponent<BoxCollider2D>();
        if (collider == null)
        {
            collider = Undo.AddComponent<BoxCollider2D>(top);
            changed = true;
        }
        Vector2 wantedSize = new Vector2(width * 0.94f, 0.36f);
        if (!collider.enabled || collider.isTrigger || Vector2.Distance(collider.size, wantedSize) > 0.001f)
        {
            Undo.RecordObject(collider, "Configure walkable gear-platform top");
            collider.enabled = true;
            collider.isTrigger = false;
            collider.offset = Vector2.zero;
            collider.size = wantedSize;
            EditorUtility.SetDirty(collider);
            changed = true;
        }

        Rigidbody2D body = top.GetComponent<Rigidbody2D>();
        if (body == null)
        {
            body = Undo.AddComponent<Rigidbody2D>(top);
            body.bodyType = RigidbodyType2D.Static;
            changed = true;
        }

        // Avoid two overlapping platform colliders after upgrading an older scene.
        BoxCollider2D oldRootCollider = root.GetComponent<BoxCollider2D>();
        if (oldRootCollider != null && oldRootCollider.enabled)
        {
            Undo.RecordObject(oldRootCollider, "Replace old gear-platform collider");
            oldRootCollider.enabled = false;
            changed = true;
        }
        return changed;
    }

    private static void CreateGear(Transform parent, string name, float x, float y, Sprite sprite, float scale, float speed)
    {
        GameObject gear = new GameObject(name);
        gear.transform.SetParent(parent, false);
        gear.transform.localPosition = new Vector3(x, y, 0f);
        gear.transform.localScale = Vector3.one * scale;

        SpriteRenderer renderer = gear.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 6;

        CircleCollider2D collider = gear.AddComponent<CircleCollider2D>();
        collider.radius = sprite.bounds.extents.x * 0.88f;
        // Half a heart per contact, with the same outward knockback used by spikes.
        gear.AddComponent<SpikeHazard2D>().Configure(5, 1f);
        gear.AddComponent<RotatingHazard2D>().Configure(speed);
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

    private static Sprite ImportSprite(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}

#endif
