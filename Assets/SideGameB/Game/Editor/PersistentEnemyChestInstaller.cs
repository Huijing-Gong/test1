// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PersistentEnemyChestInstaller
{
    private const string ScenePath = "Assets/Game/Scenes/PlayerTest.unity";
    private const string BluePrefabPath = "Assets/SPUM/Content/Addons/MS_Orc/2_Prefab/2_BlueOrc/SPUM_20241203203504469.prefab";
    private const string RedPrefabPath = "Assets/SPUM/Content/Addons/MS_Orc/2_Prefab/3_RedOrc/SPUM_20241203203508369.prefab";
    private const string HealthSpritePath = "Assets/Game/Art/HealthBarPixel.png";
    private const string ClosedPath = "Assets/Game/Art/Chest/Chest/TreasureChestClosed.png";
    private const string OpenPath = "Assets/Game/Art/Chest/Chest/TreasureChestOpen.png";
    private const string PotionPath = "Assets/Game/Art/Chest/Chest/CuteTripleJumpPotion.png";
    private const string CutePanelPath = "Assets/Game/Art/Chest/Chest/CuteRewardPanel.png";
    private const string RepairKey = "PixelRunner.PersistentRedOrcChest.RepairV3";
    private const string DuplicateCleanupKey = "PixelRunner.EnemyDuplicateCleanup.V4";
    private const string ScreenshotLayoutRepairKey = "PixelRunner.ScreenshotLayoutRepair.V5";
    private const string RedRestoreKey = "PixelRunner.RedOrcOnChestPlatform.V7";
    private const string RedVisualRepairKey = "PixelRunner.RedOrcSingleVisualAboveChest.V8";
    private const string CuteChestUiKey = "PixelRunner.CuteChestRewardUi.V11";

    static PersistentEnemyChestInstaller()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += Install;
    }

    [MenuItem("Pixel Runner/Save Red Orc + Chest Into Scene")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath && scene.name != "PlayerTest")
            return;

        GameObject red = FindClosestNamedEnemy("Red Orc Enemy", new Vector3(38.3f, 5.8860626f, 0f));
        Collider2D chestPlatform = FindChestPlatform();
        bool changed = false;
        bool createdRed = false;
        if (red == null && chestPlatform != null)
        {
            GameObject redPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RedPrefabPath);
            Sprite healthSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HealthSpritePath);
            if (redPrefab != null && healthSprite != null)
            {
                red = CreateRedEnemy(redPrefab, healthSprite, chestPlatform);
                createdRed = true;
                changed = true;
            }
        }
        if (red == null)
        {
            Debug.LogWarning("Pixel Runner: the Red Orc or the chest platform could not be found.");
            return;
        }
        // Existing red-orc visuals and health bars belong to the saved Scene.
        // Only a freshly created enemy may receive installer defaults.
        if (createdRed && !EditorPrefs.GetBool(RedVisualRepairKey, false))
        {
            NormalizeCharacterVisual(red, "Red Orc Visual");
            EditorPrefs.SetBool(RedVisualRepairKey, true);
            changed = true;
        }
        GameObject green = GameObject.Find("Orc Enemy");
        if (createdRed && !EditorPrefs.GetBool(RedRestoreKey, false))
        {
            RemoveOtherRedEnemies(red);
            if (chestPlatform != null)
                RepairRedEnemyOnPlatform(red, chestPlatform);
            EditorPrefs.SetBool(RedRestoreKey, true);
            changed = true;
        }
        if (!EditorPrefs.GetBool(CuteChestUiKey, false))
        {
            TripleJumpChest existingChest = Object.FindFirstObjectByType<TripleJumpChest>();
            Sprite cutePanel = ImportSprite(CutePanelPath, 300f);
            Sprite cutePotion = ImportSprite(PotionPath, 300f);
            if (existingChest != null && cutePanel != null && cutePotion != null)
                existingChest.ApplyCuteStyle(cutePanel, cutePotion);
            EditorPrefs.SetBool(CuteChestUiKey, true);
            changed = true;
        }
        if (GameObject.Find("Triple Jump Chest") == null)
        {
            Sprite closed = ImportSprite(ClosedPath, 300f);
            Sprite opened = ImportSprite(OpenPath, 300f);
            Sprite potion = ImportSprite(PotionPath, 300f);
            Sprite cutePanel = ImportSprite(CutePanelPath, 300f);
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            Collider2D redPlatform = FindPlatformUnder(red.transform.position);
            if (closed != null && opened != null && potion != null && cutePanel != null && canvas != null && redPlatform != null)
            {
                CreateChest(closed, opened, potion, cutePanel, canvas, redPlatform);
                changed = true;
            }
        }

        if (!changed)
            return;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Pixel Runner: Red Orc and Triple Jump Chest are real Scene objects. Press Cmd+S to save.");
    }

    private static GameObject FindClosestNamedEnemy(string name, Vector3 target)
    {
        GameObject best = null;
        float bestDistance = float.MaxValue;
        foreach (EnemyHealth health in Object.FindObjectsByType<EnemyHealth>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (health.gameObject.name != name)
                continue;
            float distance = Vector3.SqrMagnitude(health.transform.position - target);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            best = health.gameObject;
        }
        return best;
    }

    private static void CleanupDuplicateEnemies(GameObject green, GameObject red)
    {
        GameObject blue = null;
        foreach (EnemyHealth health in Object.FindObjectsByType<EnemyHealth>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            GameObject enemy = health.gameObject;
            if (enemy == green || enemy == red)
                continue;
            if (enemy.name == "Blue Orc Enemy" && blue == null)
            {
                blue = enemy;
                continue;
            }
            Undo.DestroyObjectImmediate(enemy);
        }

        RemoveExtraCharacterVisuals(red, "Red Orc Visual");
        if (blue != null)
            RemoveExtraCharacterVisuals(blue, "Blue Orc Visual");
    }

    private static void RemoveExtraCharacterVisuals(GameObject enemy, string visualToKeep)
    {
        for (int i = enemy.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = enemy.transform.GetChild(i);
            if (child.name == visualToKeep || child.GetComponentInChildren<Animator>(true) == null)
                continue;
            Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static Collider2D FindChestPlatform()
    {
        GameObject chest = GameObject.Find("Triple Jump Chest");
        if (chest != null)
        {
            Collider2D underChest = FindPlatformUnder(chest.transform.position);
            if (underChest != null)
                return underChest;
        }

        Collider2D highest = null;
        float highestTop = float.MinValue;
        foreach (Collider2D candidate in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (!candidate.enabled || candidate.isTrigger || candidate.bounds.size.x < 3f)
                continue;
            if (candidate.bounds.max.y <= highestTop)
                continue;
            highestTop = candidate.bounds.max.y;
            highest = candidate;
        }
        return highest;
    }

    private static GameObject CreateRedEnemy(GameObject redPrefab, Sprite healthSprite, Collider2D platform)
    {
        GameObject enemy = new GameObject("Red Orc Enemy");
        Undo.RegisterCreatedObjectUndo(enemy, "Add persistent Red Orc");
        enemy.transform.position = new Vector3(platform.bounds.center.x + 0.7f, platform.bounds.max.y, 0f);

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(redPrefab, enemy.transform);
        visual.name = "Red Orc Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * 1.15f;
        foreach (Collider2D item in visual.GetComponentsInChildren<Collider2D>(true))
            item.enabled = false;
        foreach (SpriteRenderer renderer in visual.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.sortingOrder += 12;
        NormalizeCharacterVisual(enemy, "Red Orc Visual");

        BoxCollider2D hitbox = enemy.AddComponent<BoxCollider2D>();
        hitbox.isTrigger = true;
        hitbox.size = new Vector2(1.05f, 1.5f);
        hitbox.offset = new Vector2(0f, 0.75f);

        WorldHealthBar bar = CreateHealthBar(enemy, healthSprite);
        enemy.AddComponent<EnemyHealth>().Configure(bar, 175);
        enemy.AddComponent<EnemyPatrol2D>().Configure(
            platform.bounds.min.x + 0.65f, platform.bounds.max.x - 0.65f, 0.65f, visual.transform);
        enemy.AddComponent<EnemyMeleeAttack2D>().ConfigureCombat(1.25f, 0.85f, 10);
        return enemy;
    }

    private static WorldHealthBar CreateHealthBar(GameObject enemy, Sprite sprite)
    {
        GameObject bar = new GameObject("Health Bar");
        bar.transform.SetParent(enemy.transform, false);
        bar.transform.localPosition = new Vector3(0f, 1.5f, 0f);
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

    private static void RepairRedEnemyOnPlatform(GameObject red, Collider2D platform)
    {
        RepairRedEnemy(red);
        red.transform.position = new Vector3(platform.bounds.center.x + 0.7f, platform.bounds.max.y, 0f);
        Transform visual = red.transform.Find("Red Orc Visual");
        red.GetComponent<EnemyHealth>().Configure(red.GetComponentInChildren<WorldHealthBar>(true), 175);
        red.GetComponent<EnemyPatrol2D>().Configure(
            platform.bounds.min.x + 0.65f, platform.bounds.max.x - 0.65f, 0.65f, visual);
        red.GetComponent<EnemyMeleeAttack2D>().ConfigureCombat(1.25f, 0.85f, 10);
    }

    private static void RemoveOtherRedEnemies(GameObject redToKeep)
    {
        foreach (EnemyHealth health in Object.FindObjectsByType<EnemyHealth>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (health.gameObject == redToKeep || health.gameObject.name != "Red Orc Enemy")
                continue;
            Undo.DestroyObjectImmediate(health.gameObject);
        }
    }

    private static void CreateBlueEnemy(GameObject green, GameObject bluePrefab, Collider2D platform)
    {
        GameObject enemy = Object.Instantiate(green);
        Undo.RegisterCreatedObjectUndo(enemy, "Add persistent Blue Orc");
        enemy.name = "Blue Orc Enemy";
        enemy.transform.position = new Vector3(platform.bounds.center.x, platform.bounds.max.y, 0f);
        enemy.SetActive(false);

        Transform oldVisual = FindVisualChild(enemy);
        if (oldVisual != null)
        {
            oldVisual.gameObject.SetActive(false);
            foreach (Collider2D item in oldVisual.GetComponentsInChildren<Collider2D>(true))
                item.enabled = false;
        }

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(bluePrefab, enemy.transform);
        visual.name = "Blue Orc Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * 1.08f;
        foreach (Collider2D item in visual.GetComponentsInChildren<Collider2D>(true))
            item.enabled = false;
        foreach (SpriteRenderer renderer in visual.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.sortingOrder += 12;

        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        health.Configure(enemy.GetComponentInChildren<WorldHealthBar>(true), 105);
        enemy.GetComponent<EnemyPatrol2D>().Configure(
            platform.bounds.min.x + 0.65f, platform.bounds.max.x - 0.65f, 0.6f, visual.transform);
        enemy.GetComponent<EnemyMeleeAttack2D>().ConfigureCombat(1.3f, 0.85f, 10);
        enemy.SetActive(true);
    }

    private static void RepairRedEnemy(GameObject red)
    {
        red.SetActive(true);
        red.transform.position = new Vector3(38.3f, 5.8860626f, 0f);
        red.transform.rotation = Quaternion.identity;

        BoxCollider2D hitbox = red.GetComponent<BoxCollider2D>();
        if (hitbox == null)
            hitbox = red.AddComponent<BoxCollider2D>();
        hitbox.enabled = true;
        hitbox.isTrigger = true;
        hitbox.size = new Vector2(1.05f, 1.5f);
        hitbox.offset = new Vector2(0f, 0.75f);

        Transform visual = red.transform.Find("Red Orc Visual");
        if (visual == null)
            visual = FindVisualChild(red);
        if (visual != null)
            visual.gameObject.SetActive(true);
        foreach (Animator animator in red.GetComponentsInChildren<Animator>(true))
        {
            if (animator.gameObject.name == "HorseRoot")
                animator.gameObject.SetActive(false);
            else if (animator.gameObject.activeInHierarchy)
                animator.enabled = true;
        }
        NormalizeCharacterVisual(red, "Red Orc Visual");

        EnemyHealth health = red.GetComponent<EnemyHealth>();
        if (health == null)
            health = red.AddComponent<EnemyHealth>();
        health.Configure(red.GetComponentInChildren<WorldHealthBar>(true), 175);

        Collider2D platform = FindPlatformUnder(red.transform.position);
        EnemyPatrol2D patrol = red.GetComponent<EnemyPatrol2D>();
        if (patrol == null)
            patrol = red.AddComponent<EnemyPatrol2D>();
        if (platform != null)
            patrol.Configure(platform.bounds.min.x + 0.65f, platform.bounds.max.x - 0.65f, 0.65f, visual);

        EnemyMeleeAttack2D attack = red.GetComponent<EnemyMeleeAttack2D>();
        if (attack == null)
            attack = red.AddComponent<EnemyMeleeAttack2D>();
        attack.enabled = true;
        attack.ConfigureCombat(1.25f, 0.85f, 10);
    }

    private static void RepairBlueEnemy(GameObject blue, Vector3 redPosition)
    {
        Collider2D platform = FindLongPlatformBelow(redPosition);
        if (platform == null)
            return;
        blue.SetActive(true);
        blue.transform.position = new Vector3(platform.bounds.center.x, platform.bounds.max.y, 0f);
        blue.transform.rotation = Quaternion.identity;

        Transform visual = blue.transform.Find("Blue Orc Visual");
        if (visual != null)
            visual.gameObject.SetActive(true);
        foreach (Animator animator in blue.GetComponentsInChildren<Animator>(true))
        {
            if (animator.gameObject.name == "HorseRoot")
                animator.gameObject.SetActive(false);
            else if (animator.gameObject.activeInHierarchy)
                animator.enabled = true;
        }

        EnemyHealth health = blue.GetComponent<EnemyHealth>();
        health.Configure(blue.GetComponentInChildren<WorldHealthBar>(true), 105);
        blue.GetComponent<EnemyPatrol2D>().Configure(
            platform.bounds.min.x + 0.65f, platform.bounds.max.x - 0.65f, 0.6f, visual);
        blue.GetComponent<EnemyMeleeAttack2D>().ConfigureCombat(1.3f, 0.8f, 10);
    }

    private static void NormalizeCharacterVisual(GameObject enemy, string visualName)
    {
        Transform visual = enemy.transform.Find(visualName);
        if (visual == null)
            return;
        visual.gameObject.SetActive(true);

        foreach (Animator animator in visual.GetComponentsInChildren<Animator>(true))
        {
            bool isHorse = animator.gameObject.name == "HorseRoot";
            animator.gameObject.SetActive(!isHorse);
            if (!isHorse)
                animator.enabled = true;
        }

        SortingGroup[] groups = visual.GetComponentsInChildren<SortingGroup>(true);
        foreach (SortingGroup group in groups)
        {
            if (group.gameObject.name == "UnitRoot")
                group.sortingOrder = 25;
        }
    }

    private static void CreateChest(Sprite closed, Sprite opened, Sprite potion, Sprite cutePanel,
        Canvas canvas, Collider2D platform)
    {
        GameObject chest = new GameObject("Triple Jump Chest");
        Undo.RegisterCreatedObjectUndo(chest, "Add persistent Triple Jump Chest");
        chest.transform.position = new Vector3(platform.bounds.min.x + 1f, platform.bounds.max.y, 0f);
        chest.transform.localScale = Vector3.one * 0.25f;
        TripleJumpChest behaviour = chest.AddComponent<TripleJumpChest>();
        behaviour.ConfigurePersistent(closed, opened, potion, canvas.transform);
        behaviour.ApplyCuteStyle(cutePanel, potion);
    }

    private static Sprite ImportSprite(string path, float pixelsPerUnit)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Collider2D FindLongPlatformBelow(Vector3 position)
    {
        Collider2D best = null;
        float bestTop = float.MinValue;
        foreach (Collider2D candidate in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (!candidate.enabled || candidate.isTrigger)
                continue;
            Bounds bounds = candidate.bounds;
            if (position.x < bounds.min.x || position.x > bounds.max.x || bounds.max.y > position.y - 1.4f)
                continue;
            if (bounds.size.x < 4f || bounds.max.y <= bestTop)
                continue;
            bestTop = bounds.max.y;
            best = candidate;
        }
        return best;
    }

    private static Collider2D FindPlatformUnder(Vector3 position)
    {
        Collider2D best = null;
        float closest = float.MaxValue;
        foreach (Collider2D candidate in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (!candidate.enabled || candidate.isTrigger)
                continue;
            Bounds bounds = candidate.bounds;
            if (position.x < bounds.min.x || position.x > bounds.max.x)
                continue;
            float distance = position.y - bounds.max.y;
            if (distance < -0.3f || distance >= closest)
                continue;
            closest = distance;
            best = candidate;
        }
        return best;
    }

    private static Transform FindVisualChild(GameObject enemy)
    {
        Animator animator = enemy.GetComponentInChildren<Animator>(true);
        if (animator == null)
            return null;
        Transform visual = animator.transform;
        while (visual.parent != null && visual.parent != enemy.transform)
            visual = visual.parent;
        return visual.parent == enemy.transform ? visual : null;
    }
}

#endif
