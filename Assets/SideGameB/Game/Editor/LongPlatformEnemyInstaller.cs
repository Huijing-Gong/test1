// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class LongPlatformEnemyInstaller
{
    private const string ScenePath = "Assets/Game/Scenes/PlayerTest.unity";
    private const string PlatformSpritePath = "Assets/Game/Art/Platforms/LongFlowerGrassPlatform.png";
    private const string OrcPrefabPath = "Assets/SPUM/Content/Addons/MS_Orc/2_Prefab/3_RedOrc/SPUM_20241203203508369.prefab";
    private const string HealthSpritePath = "Assets/Game/Art/HealthBarPixel.png";
    private const string PlatformName = "Long Flower Grass Platform";
    private const string EnemyName = "Red Orc Enemy";
    private const string InstallKey = "PixelRunner.LongPlatformRedOrc.FullRepair.v5";

    static LongPlatformEnemyInstaller()
    {
        EditorApplication.delayCall += InstallOnce;
    }

    [MenuItem("Pixel Runner/Add Long Platform + Red Orc (Preserve Scene)")]
    public static void InstallFromMenu()
    {
        Install(true);
    }

    private static void InstallOnce()
    {
        Install(false);
    }

    private static void Install(bool showMessage)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath && scene.name != "PlayerTest")
        {
            if (showMessage)
                Debug.LogWarning("Pixel Runner: open PlayerTest scene before adding the long platform and Red Orc.");
            return;
        }

        Sprite platformSprite = ImportPlatformSprite();
        GameObject orcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OrcPrefabPath);
        Sprite healthSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HealthSpritePath);
        if (platformSprite == null || orcPrefab == null || healthSprite == null)
        {
            Debug.LogError("Pixel Runner: a required long-platform or Red Orc asset is missing.");
            return;
        }

        bool changed = false;
        if (GameObject.Find(PlatformName) == null)
        {
            CreatePlatform(platformSprite);
            changed = true;
        }
        GameObject redOrc = FindExistingRedOrc();
        bool createdRedOrc = false;
        if (redOrc == null)
        {
            CreateEnemy(orcPrefab, healthSprite);
            changed = true;
            createdRedOrc = true;
            redOrc = GameObject.Find(EnemyName);
        }

        // Never rewrite an existing Scene enemy. Its transform, visual offset and
        // health-bar layout may have been adjusted manually by the designer.
        if (redOrc != null && createdRedOrc)
        {
            redOrc.name = EnemyName;
            RepairRedOrc(redOrc, orcPrefab, healthSprite);
            PlaceEnemyOnRightmostPlatform(redOrc);
            ConfigureFiveHitHealth(redOrc);
            ConfigureRedOrcAttack(redOrc);
        }

        if (changed)
            EditorSceneManager.MarkSceneDirty(scene);
        EditorPrefs.SetBool(InstallKey, true);
        Selection.activeGameObject = GameObject.Find(EnemyName);
        Debug.Log("Pixel Runner: long platform and Red Orc added without rebuilding the scene. Save the scene to keep them.");
    }

    private static GameObject FindExistingRedOrc()
    {
        GameObject named = GameObject.Find(EnemyName);
        if (named != null)
            return named;

        foreach (Transform candidate in Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!candidate.name.Contains("SPUM_20241203203508369") &&
                !candidate.name.Contains("Red Orc Visual"))
                continue;

            EnemyHealth existingHealth = candidate.GetComponentInParent<EnemyHealth>();
            if (existingHealth != null)
                return existingHealth.gameObject;
            if (candidate.parent != null && candidate.parent.name.Contains("Red"))
                return candidate.parent.gameObject;
            return candidate.gameObject;
        }
        return null;
    }

    private static void PlaceEnemyOnRightmostPlatform(GameObject enemy)
    {
        Collider2D rightmost = null;
        float rightmostEdge = float.MinValue;
        foreach (Collider2D candidate in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (candidate.isTrigger || !candidate.name.Contains("Platform"))
                continue;
            if (candidate.bounds.max.x > rightmostEdge)
            {
                rightmostEdge = candidate.bounds.max.x;
                rightmost = candidate;
            }
        }

        if (rightmost == null)
            return;

        Bounds bounds = rightmost.bounds;
        enemy.transform.position = new Vector3(bounds.center.x, bounds.max.y, enemy.transform.position.z);
        EnemyPatrol2D patrol = enemy.GetComponent<EnemyPatrol2D>();
        Transform visual = enemy.transform.Find("Red Orc Visual");
        if (visual == null && enemy.GetComponentInChildren<Animator>(true) != null)
            visual = enemy.transform;
        if (patrol != null)
            patrol.Configure(bounds.min.x + 0.6f, bounds.max.x - 0.6f, 0.65f, visual);
    }

    private static void ConfigureFiveHitHealth(GameObject enemy)
    {
        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        WorldHealthBar healthBar = enemy.GetComponentInChildren<WorldHealthBar>(true);
        if (health != null && healthBar != null)
            health.Configure(healthBar, 175);
    }

    private static void ConfigureRedOrcAttack(GameObject enemy)
    {
        EnemyMeleeAttack2D attack = enemy.GetComponent<EnemyMeleeAttack2D>();
        if (attack != null)
            attack.ConfigureCombat(1.25f, 0.85f, 10);
    }

    private static void RepairRedOrc(GameObject enemy, GameObject orcPrefab, Sprite healthSprite)
    {
        enemy.layer = 0;
        enemy.SetActive(true);

        Transform visual = enemy.transform.Find("Red Orc Visual");
        if (visual == null && enemy.GetComponentInChildren<Animator>(true) != null)
            visual = enemy.transform;
        if (visual == null)
        {
            GameObject visualObject = (GameObject)PrefabUtility.InstantiatePrefab(orcPrefab);
            visualObject.name = "Red Orc Visual";
            visualObject.transform.SetParent(enemy.transform, false);
            visualObject.transform.localPosition = Vector3.zero;
            visualObject.transform.localScale = Vector3.one * 1.15f;
            visual = visualObject.transform;
        }

        foreach (SpriteRenderer renderer in visual.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer.sortingOrder < 12)
                renderer.sortingOrder += 12;
            renderer.enabled = true;
        }
        foreach (Collider2D childCollider in visual.GetComponentsInChildren<Collider2D>(true))
            Object.DestroyImmediate(childCollider);

        BoxCollider2D hitbox = enemy.GetComponent<BoxCollider2D>();
        if (hitbox == null)
            hitbox = enemy.AddComponent<BoxCollider2D>();
        hitbox.enabled = true;
        hitbox.isTrigger = true;
        hitbox.size = new Vector2(1.05f, 1.5f);
        hitbox.offset = new Vector2(0f, 0.75f);

        WorldHealthBar healthBar = EnsureHealthBar(enemy, healthSprite);
        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        if (health == null)
            health = enemy.AddComponent<EnemyHealth>();
        health.Configure(healthBar, 175);

        EnemyPatrol2D patrol = enemy.GetComponent<EnemyPatrol2D>();
        if (patrol == null)
            patrol = enemy.AddComponent<EnemyPatrol2D>();
        patrol.Configure(33.2f, 41.4f, 0.65f, visual);

        EnemyMeleeAttack2D attack = enemy.GetComponent<EnemyMeleeAttack2D>();
        if (attack == null)
            attack = enemy.AddComponent<EnemyMeleeAttack2D>();
        attack.enabled = true;
        attack.ConfigureCombat(1.25f, 0.85f, 10);
    }

    private static WorldHealthBar EnsureHealthBar(GameObject enemy, Sprite healthSprite)
    {
        Transform existing = enemy.transform.Find("Health Bar");
        GameObject bar = existing != null ? existing.gameObject : new GameObject("Health Bar");
        bar.transform.SetParent(enemy.transform, false);
        bar.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        bar.transform.localScale = new Vector3(1f, 0.14f, 1f);

        SpriteRenderer background = bar.GetComponent<SpriteRenderer>();
        if (background == null)
            background = bar.AddComponent<SpriteRenderer>();
        background.sprite = healthSprite;
        background.color = new Color(0.15f, 0.05f, 0.05f, 1f);
        background.sortingOrder = 40;

        Transform fillTransform = bar.transform.Find("Fill");
        GameObject fillObject = fillTransform != null ? fillTransform.gameObject : new GameObject("Fill");
        fillObject.transform.SetParent(bar.transform, false);
        fillObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        fillObject.transform.localScale = new Vector3(0.9f, 0.65f, 1f);
        SpriteRenderer fill = fillObject.GetComponent<SpriteRenderer>();
        if (fill == null)
            fill = fillObject.AddComponent<SpriteRenderer>();
        fill.sprite = healthSprite;
        fill.color = new Color(0.2f, 0.9f, 0.25f, 1f);
        fill.sortingOrder = 41;

        WorldHealthBar healthBar = bar.GetComponent<WorldHealthBar>();
        if (healthBar == null)
            healthBar = bar.AddComponent<WorldHealthBar>();
        healthBar.Configure(fillObject.transform, 0.9f);
        return healthBar;
    }

    private static void CreatePlatform(Sprite sprite)
    {
        var platform = new GameObject(PlatformName);
        Undo.RegisterCreatedObjectUndo(platform, "Add Long Flower Grass Platform");
        const float surfaceY = 1.4f;
        float grassSurface = sprite.bounds.max.y - 0.25f;
        platform.transform.position = new Vector3(37.3f, surfaceY - grassSurface, 0f);

        SpriteRenderer renderer = platform.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1;
        BoxCollider2D collider = platform.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(sprite.bounds.size.x * 0.96f, 0.12f);
        collider.offset = new Vector2(0f, grassSurface - 0.06f);
    }

    private static void CreateEnemy(GameObject orcPrefab, Sprite healthSprite)
    {
        var enemy = new GameObject(EnemyName);
        Undo.RegisterCreatedObjectUndo(enemy, "Add Red Orc Enemy");
        const float surfaceY = 1.4f;
        enemy.transform.position = new Vector3(37.3f, surfaceY, 0f);

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(orcPrefab);
        visual.name = "Red Orc Visual";
        visual.transform.SetParent(enemy.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one * 1.15f;
        foreach (SpriteRenderer renderer in visual.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.sortingOrder += 12;
        foreach (Collider2D visualCollider in visual.GetComponentsInChildren<Collider2D>(true))
            Object.DestroyImmediate(visualCollider);

        BoxCollider2D enemyCollider = enemy.AddComponent<BoxCollider2D>();
        enemyCollider.size = new Vector2(1.05f, 1.5f);
        enemyCollider.offset = new Vector2(0f, 0.75f);
        enemyCollider.isTrigger = true;

        var bar = new GameObject("Health Bar");
        bar.transform.SetParent(enemy.transform, false);
        bar.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        SpriteRenderer background = bar.AddComponent<SpriteRenderer>();
        background.sprite = healthSprite;
        background.color = new Color(0.15f, 0.05f, 0.05f, 1f);
        background.sortingOrder = 40;
        bar.transform.localScale = new Vector3(1f, 0.14f, 1f);

        var fillObject = new GameObject("Fill");
        fillObject.transform.SetParent(bar.transform, false);
        SpriteRenderer fill = fillObject.AddComponent<SpriteRenderer>();
        fill.sprite = healthSprite;
        fill.color = new Color(0.2f, 0.9f, 0.25f, 1f);
        fill.sortingOrder = 41;
        fillObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        fillObject.transform.localScale = new Vector3(0.9f, 0.65f, 1f);

        WorldHealthBar healthBar = bar.AddComponent<WorldHealthBar>();
        healthBar.Configure(fillObject.transform, 0.9f);
        enemy.AddComponent<EnemyHealth>().Configure(healthBar, 175);
        enemy.AddComponent<EnemyPatrol2D>().Configure(33.2f, 41.4f, 0.65f, visual.transform);
        enemy.AddComponent<EnemyMeleeAttack2D>().ConfigureCombat(1.25f, 0.85f, 10);
    }

    private static Sprite ImportPlatformSprite()
    {
        AssetDatabase.ImportAsset(PlatformSpritePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(PlatformSpritePath) as TextureImporter;
        if (importer == null)
            return null;
        bool needsUpdate = importer.textureType != TextureImporterType.Sprite ||
                           !Mathf.Approximately(importer.spritePixelsPerUnit, 220f) ||
                           importer.filterMode != FilterMode.Point || importer.mipmapEnabled ||
                           importer.textureCompression != TextureImporterCompression.Uncompressed;
        if (needsUpdate)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 220f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(PlatformSpritePath);
    }
}

#endif
