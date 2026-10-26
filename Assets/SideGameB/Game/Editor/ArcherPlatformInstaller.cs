// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ArcherPlatformInstaller
{
    private const string ScenePath = "Assets/Game/Scenes/PlayerTest.unity";
    private const string PlatformPath = "Assets/Game/Art/Platforms/ArcherStonePlatform.png";
    private const string ArrowPath = "Assets/Game/Art/Enemies/ArcherArrow.png";
    private const string ArcherPrefabPath = "Assets/SPUM/Content/Addons/MS_Orc/2_Prefab/1_GreenOrc/SPUM_20241203203035089.prefab";
    private const string HealthSpritePath = "Assets/Game/Art/HealthBarPixel.png";
    private const string PlatformName = "Archer Stone Platform";
    private const string EnemyName = "Orc Archer Enemy";

    static ArcherPlatformInstaller()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Pixel Runner/Add Archer Platform + Ranged Enemy")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath && scene.name != "PlayerTest")
            return;

        Sprite platformSprite = ImportSprite(PlatformPath, 220f);
        Sprite arrowSprite = ImportSprite(ArrowPath, 520f);
        Sprite healthSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HealthSpritePath);
        GameObject archerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArcherPrefabPath);
        if (platformSprite == null || arrowSprite == null || healthSprite == null || archerPrefab == null)
            return;

        float width = platformSprite.bounds.size.x;
        float visualTop = platformSprite.bounds.max.y - 0.34f;

        GameObject platform = GameObject.Find(PlatformName);
        if (platform == null)
        {
            Collider2D rightmost = FindRightmostPlatform();
            float leftEdge = rightmost != null ? rightmost.bounds.max.x + 1.4f : 70f;
            float surfaceY = rightmost != null ? rightmost.bounds.max.y + 2.4f : 7f;
            platform = new GameObject(PlatformName);
            Undo.RegisterCreatedObjectUndo(platform, "Add archer platform and enemy");
            platform.transform.position = new Vector3(leftEdge + width * 0.5f, surfaceY - visualTop, 0f);
            SpriteRenderer platformRenderer = platform.AddComponent<SpriteRenderer>();
            platformRenderer.sprite = platformSprite;
            platformRenderer.sortingOrder = 2;
            BoxCollider2D platformCollider = platform.AddComponent<BoxCollider2D>();
            platformCollider.isTrigger = false;
            platformCollider.size = new Vector2(width * 0.96f, 0.24f);
            platformCollider.offset = new Vector2(0f, visualTop - 0.12f);
        }

        GameObject enemy = GameObject.Find(EnemyName) ?? GameObject.Find("Blue Orc Archer Enemy");
        if (enemy == null)
        {
            enemy = new GameObject(EnemyName);
            Undo.RegisterCreatedObjectUndo(enemy, "Add Orc archer enemy");
            Collider2D platformCollider = platform.GetComponent<Collider2D>();
            float surfaceY = platformCollider != null ? platformCollider.bounds.max.y : platform.transform.position.y + visualTop;
            enemy.transform.position = new Vector3(platform.transform.position.x, surfaceY, 0f);
        }
        enemy.name = EnemyName;

        Transform visual = EnsureBowVisual(enemy, archerPrefab);
        BoxCollider2D hitbox = enemy.GetComponent<BoxCollider2D>();
        if (hitbox == null)
            hitbox = enemy.AddComponent<BoxCollider2D>();
        hitbox.enabled = true;
        hitbox.isTrigger = true;
        hitbox.size = new Vector2(1.05f, 1.5f);
        hitbox.offset = new Vector2(0f, 0.75f);

        WorldHealthBar bar = enemy.GetComponentInChildren<WorldHealthBar>(true);
        if (bar == null)
            bar = CreateHealthBar(enemy, healthSprite);
        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        if (health == null)
            health = enemy.AddComponent<EnemyHealth>();
        health.Configure(bar, 105);
        EnemyArcherAttack2D attack = enemy.GetComponent<EnemyArcherAttack2D>();
        if (attack == null)
            attack = enemy.AddComponent<EnemyArcherAttack2D>();
        attack.enabled = true;
        attack.Configure(arrowSprite, visual);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = enemy;
        Debug.Log("Pixel Runner: Archer platform and ranged enemy added as real Scene objects. Press Cmd+S to save.");
    }

    private static Transform EnsureBowVisual(GameObject enemy, GameObject prefab)
    {
        Transform visual = enemy.transform.Find("Green Orc Archer Visual");
        Transform oldVisual = enemy.transform.Find("Blue Orc Archer Visual");
        if (oldVisual != null)
            Undo.DestroyObjectImmediate(oldVisual.gameObject);
        if (visual == null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, enemy.transform);
            instance.name = "Green Orc Archer Visual";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localScale = Vector3.one * 1.1f;
            visual = instance.transform;
        }

        foreach (Collider2D item in visual.GetComponentsInChildren<Collider2D>(true))
            Object.DestroyImmediate(item);
        foreach (SpriteRenderer item in visual.GetComponentsInChildren<SpriteRenderer>(true))
        {
            string path = AssetDatabase.GetAssetPath(item.sprite);
            bool isWeapon = path.Contains("/6_Weapons/") || path.Contains("BowBack");
            bool isBow = path.Contains("/2_Bow/");
            if (isWeapon)
            {
                item.enabled = isBow;
                if (isBow)
                    item.gameObject.SetActive(true);
            }
            item.sortingOrder = Mathf.Max(item.sortingOrder, 14);
        }
        foreach (Animator animator in visual.GetComponentsInChildren<Animator>(true))
            if (HasParameter(animator, "1_Move"))
                animator.SetBool("1_Move", false);
        return visual;
    }

    private static WorldHealthBar CreateHealthBar(GameObject enemy, Sprite sprite)
    {
        GameObject bar = new GameObject("Health Bar");
        bar.transform.SetParent(enemy.transform, false);
        bar.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        bar.transform.localScale = new Vector3(1f, 0.14f, 1f);
        SpriteRenderer background = bar.AddComponent<SpriteRenderer>();
        background.sprite = sprite;
        background.color = new Color(0.15f, 0.05f, 0.05f, 1f);
        background.sortingOrder = 40;
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(bar.transform, false);
        fill.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        fill.transform.localScale = new Vector3(0.9f, 0.65f, 1f);
        SpriteRenderer fillRenderer = fill.AddComponent<SpriteRenderer>();
        fillRenderer.sprite = sprite;
        fillRenderer.color = new Color(0.2f, 0.9f, 0.25f, 1f);
        fillRenderer.sortingOrder = 41;
        WorldHealthBar healthBar = bar.AddComponent<WorldHealthBar>();
        healthBar.Configure(fill.transform, 0.9f);
        return healthBar;
    }

    private static Collider2D FindRightmostPlatform()
    {
        Collider2D result = null;
        float edge = float.MinValue;
        foreach (Collider2D candidate in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (!candidate.enabled || candidate.isTrigger || !candidate.name.Contains("Platform"))
                continue;
            if (candidate.bounds.max.x <= edge)
                continue;
            edge = candidate.bounds.max.x;
            result = candidate;
        }
        return result;
    }

    private static Sprite ImportSprite(string path, float ppu)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static bool HasParameter(Animator animator, string name)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.name == name)
                return true;
        return false;
    }
}

#endif
