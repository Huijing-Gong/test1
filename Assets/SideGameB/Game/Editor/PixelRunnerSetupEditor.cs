// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public static class PixelRunnerSetupEditor
{
    private const string ScenePath = "Assets/Game/Scenes/PlayerTest.unity";
    private const string CharacterRoot = "Assets/Sword Princess/HD/Hero";
    private const string BackgroundPath = "Assets/Game/Art/Generated/FantasyValleyBackground.png";
    private const string PlatformPath = "Assets/Game/Art/Generated/SimpleGrassPlatform-v3.png";
    private const string BridgePlatformPath = "Assets/Game/Art/Generated/SimpleForestBridge-v3.png";
    private const string FireballFolder = "Assets/Warped Explosions pack 2/Sprites/explosion-e";
    private const string ExplosionFolder = "Assets/Warped Explosions pack 2/Sprites/explosion-f";
    private const string DiamondAnimationFolder = "Assets/Game/Art/DiamondAnimation";
    private const string SpikeStripPath = "Assets/Game/Art/Hazards/SpikeStrip.png";
    private const string GoblinSpritePath = "Assets/RPG Monster PixelArt/Sprites/goblin.png";
    private const string GoblinAnimationPath = "Assets/RPG Monster PixelArt/Animation/goblin/goblin_left.anim";
    private const string OrcPrefabPath = "Assets/SPUM/Content/Addons/MS_Orc/2_Prefab/1_GreenOrc/SPUM_20241203203040230.prefab";

    [MenuItem("Pixel Runner/DANGER - Reset Entire Player Test Scene")]
    public static void BuildTestScene()
    {
        EnsureFolder("Assets/Game");
        EnsureFolder("Assets/Game/Scenes");
        EnsureFolder("Assets/Game/Animations");
        EnsureFolder("Assets/Game/Art");
        EnsureFolder("Assets/Game/Prefabs");

        Sprite backgroundSprite = ImportGeneratedSprite(BackgroundPath, 114f);
        Sprite platformSprite = ImportGeneratedSprite(PlatformPath, 200f);
        Sprite bridgePlatformSprite = ImportGeneratedSprite(BridgePlatformPath, 200f);
        Sprite spikeSprite = ImportGeneratedSprite(SpikeStripPath, 500f);
        ConfigureSpriteFolder(FireballFolder, 64f);
        ConfigureSpriteFolder(ExplosionFolder, 32f);
        ConfigureSpriteFolder(DiamondAnimationFolder, 320f);
        ConfigureGoblinSprite();
        AnimatorController controller = CreateAnimatorController();
        GameObject explosionPrefab = CreateExplosionPrefab();
        GameObject projectilePrefab = CreateProjectilePrefab(explosionPrefab);
        AnimationClip diamondClip = CreateClip("diamond sparkle", DiamondAnimationFolder, 6f, true);
        AnimatorController diamondController = CreateSingleStateController(
            "Assets/Game/Animations/Diamond.controller", "sparkle", diamondClip);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "PlayerTest";

        CreatePlatform(platformSprite, "Main Platform A", 0f, -1.5f, 6f);
        CreatePlatform(bridgePlatformSprite, "Connecting Bridge", 4.25f, -1.5f, 2.8f);
        CreatePlatform(platformSprite, "Main Platform B", 8.5f, -1.5f, 6f);
        CreatePlatform(platformSprite, "Main Platform C", 15f, -0.15f, 5.5f);
        CreateSpikeHazard(spikeSprite, "Spike Trap A", 1.65f, -1.5f, 2.35f);
        CreateSpikeHazard(spikeSprite, "Spike Trap C", 15.25f, -0.15f, 2.35f);
        Sprite diamondSprite = LoadFirstSprite(DiamondAnimationFolder);
        CreateDiamonds(diamondSprite, diamondController);
        GameObject player = CreatePlayer(controller, projectilePrefab);
        CreateEnemy(CreateSolidSprite());
        CreateCamera(player.transform, backgroundSprite);
        PlayerHealthHud playerHud = CreatePlayerHud(CreateSolidSprite());
        player.AddComponent<PlayerHealth>().Configure(playerHud, 100);

        EditorSceneManager.SaveScene(scene, ScenePath);
        if (!EditorBuildSettings.scenes.Any(item => item.path == ScenePath))
            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
        Selection.activeGameObject = GameObject.Find("Player");
        Debug.Log("Pixel Runner: PlayerTest scene created. Press Play; move with A/D or arrows and jump with Space/W/Up.");
    }

    private static AnimatorController CreateAnimatorController()
    {
        const string controllerPath = "Assets/Game/Animations/Princess.controller";
        AssetDatabase.DeleteAsset(controllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        AnimationClip idle = CreateClip("idle", CharacterRoot + "/idle", 8f, true);
        AnimationClip run = CreateClip("run", CharacterRoot + "/run", 12f, true);
        AnimationClip jump = CreateClip("jump", CharacterRoot + "/jump", 10f, false);
        AnimationClip doubleJump = CreateClip("double jump", CharacterRoot + "/Double Jump", 12f, false);
        AnimationClip skill3 = CreateClip("skill3", CharacterRoot + "/skill3", 12f, false);
        AnimationClip hurt = CreateClip("hurt", CharacterRoot + "/hurt", 10f, false);
        AnimationClip die = CreateClip("die", CharacterRoot + "/die", 10f, false);

        AnimatorState idleState = stateMachine.AddState("idle");
        idleState.motion = idle;
        stateMachine.defaultState = idleState;
        stateMachine.AddState("run").motion = run;
        stateMachine.AddState("jump").motion = jump;
        stateMachine.AddState("double jump").motion = doubleJump;
        stateMachine.AddState("skill3").motion = skill3;
        stateMachine.AddState("hurt").motion = hurt;
        stateMachine.AddState("die").motion = die;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimationClip CreateClip(string name, string folder, float frameRate, bool loop)
    {
        string clipPath = $"Assets/Game/Animations/{name}.anim";
        AssetDatabase.DeleteAsset(clipPath);

        Sprite[] sprites = AssetDatabase.FindAssets("t:Sprite", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(ExtractFrameNumber)
            .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
            .Where(sprite => sprite != null)
            .ToArray();

        var clip = new AnimationClip { frameRate = frameRate, name = name };
        var binding = new EditorCurveBinding
        {
            path = string.Empty,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite"
        };
        ObjectReferenceKeyframe[] frames = sprites.Select((sprite, index) => new ObjectReferenceKeyframe
        {
            time = index / frameRate,
            value = sprite
        }).ToArray();
        AnimationUtility.SetObjectReferenceCurve(clip, binding, frames);
        AnimationUtility.SetAnimationClipSettings(clip, new AnimationClipSettings { loopTime = loop });
        AssetDatabase.CreateAsset(clip, clipPath);
        return clip;
    }

    private static int ExtractFrameNumber(string path)
    {
        Match match = Regex.Match(Path.GetFileNameWithoutExtension(path), @"(\d+)$");
        return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
    }

    private static AnimatorController CreateSingleStateController(string path, string stateName, AnimationClip clip)
    {
        AssetDatabase.DeleteAsset(path);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimatorState state = controller.layers[0].stateMachine.AddState(stateName);
        state.motion = clip;
        controller.layers[0].stateMachine.defaultState = state;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static GameObject CreateExplosionPrefab()
    {
        AnimationClip clip = CreateClip("magic explosion", ExplosionFolder, 14f, false);
        AnimatorController controller = CreateSingleStateController(
            "Assets/Game/Animations/MagicExplosion.controller", "explode", clip);
        Sprite firstFrame = LoadFirstSprite(ExplosionFolder);

        var source = new GameObject("Magic Explosion");
        SpriteRenderer renderer = source.AddComponent<SpriteRenderer>();
        renderer.sprite = firstFrame;
        renderer.sortingOrder = 30;
        source.AddComponent<Animator>().runtimeAnimatorController = controller;
        source.AddComponent<ExplosionDamage2D>();
        source.transform.localScale = Vector3.one * 1.35f;

        const string path = "Assets/Game/Prefabs/MagicExplosion.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
        Object.DestroyImmediate(source);
        return prefab;
    }

    private static GameObject CreateProjectilePrefab(GameObject explosionPrefab)
    {
        AnimationClip clip = CreateClip("magic fireball", FireballFolder, 18f, true);
        AnimatorController controller = CreateSingleStateController(
            "Assets/Game/Animations/MagicFireball.controller", "fly", clip);
        Sprite firstFrame = LoadFirstSprite(FireballFolder);

        var source = new GameObject("Magic Fireball");
        SpriteRenderer renderer = source.AddComponent<SpriteRenderer>();
        renderer.sprite = firstFrame;
        renderer.sortingOrder = 25;
        source.AddComponent<Animator>().runtimeAnimatorController = controller;
        Rigidbody2D body = source.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        CircleCollider2D collider = source.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.22f;
        source.AddComponent<MagicProjectile2D>().SetExplosionPrefab(explosionPrefab);
        source.transform.localScale = Vector3.one * 0.9f;

        const string path = "Assets/Game/Prefabs/MagicFireball.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
        Object.DestroyImmediate(source);
        return prefab;
    }

    private static Sprite LoadFirstSprite(string folder)
    {
        string path = AssetDatabase.FindAssets("t:Sprite", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(ExtractFrameNumber)
            .First();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite CreateGroundSprite()
    {
        const string texturePath = "Assets/Game/Art/TestGround.png";
        if (!File.Exists(texturePath))
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            Color dark = new(0.12f, 0.15f, 0.20f, 1f);
            Color light = new(0.22f, 0.34f, 0.27f, 1f);
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                texture.SetPixel(x, y, y > 11 ? light : dark);
            texture.Apply();
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = 16f;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
    }

    private static Sprite CreateSolidSprite()
    {
        const string texturePath = "Assets/Game/Art/HealthBarPixel.png";
        if (!File.Exists(texturePath))
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 1f;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
    }

    private static void ConfigureSpriteFolder(string folder, float pixelsPerUnit)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureGoblinSprite()
    {
        AssetDatabase.ImportAsset(GoblinSpritePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(GoblinSpritePath) as TextureImporter;
        if (importer == null)
            return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 24f;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static void CreateCamera(Transform target, Sprite backgroundSprite)
    {
        var cameraObject = new GameObject("Main Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 4.5f;
        camera.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 1f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<CameraFollow2D>().SetTarget(target);

        var background = new GameObject("Fantasy Valley Background");
        background.transform.SetParent(cameraObject.transform, false);
        background.transform.localPosition = new Vector3(0f, 0f, 10f);
        background.transform.localScale = Vector3.one * 1.4f;
        SpriteRenderer backgroundRenderer = background.AddComponent<SpriteRenderer>();
        backgroundRenderer.sprite = backgroundSprite;
        backgroundRenderer.sortingOrder = -100;
    }

    private static void CreatePlatform(Sprite sprite, string name, float x, float surfaceY, float width)
    {
        var platform = new GameObject(name);
        Vector2 spriteSize = sprite.bounds.size;
        float uniformScale = width / spriteSize.x;
        float walkableTop = FindWalkableTopLocalY(sprite);
        platform.transform.localScale = new Vector3(uniformScale, uniformScale, 1f);
        platform.transform.position = new Vector3(x, surfaceY - walkableTop * uniformScale, 0f);
        SpriteRenderer renderer = platform.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1;
        BoxCollider2D collider = platform.AddComponent<BoxCollider2D>();
        float colliderHeight = Mathf.Max(0.12f / uniformScale, spriteSize.y * 0.06f);
        collider.size = new Vector2(spriteSize.x * 0.96f, colliderHeight);
        collider.offset = new Vector2(0f, walkableTop - colliderHeight * 0.5f);
    }

    private static void CreateSpikeHazard(Sprite sprite, string name, float x, float surfaceY, float width)
    {
        var hazard = new GameObject(name);
        Vector2 spriteSize = sprite.bounds.size;
        float scale = width / spriteSize.x;
        hazard.transform.localScale = new Vector3(scale, scale, 1f);
        hazard.transform.position = new Vector3(x, surfaceY - sprite.bounds.min.y * scale, 0f);

        SpriteRenderer renderer = hazard.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 8;

        BoxCollider2D collider = hazard.AddComponent<BoxCollider2D>();
        float colliderHeight = spriteSize.y * 0.9f;
        collider.size = new Vector2(spriteSize.x * 0.98f, colliderHeight);
        collider.offset = new Vector2(0f, sprite.bounds.max.y - colliderHeight * 0.5f);
        hazard.AddComponent<SpikeHazard2D>();
    }

    private static float FindWalkableTopLocalY(Sprite sprite)
    {
        string path = AssetDatabase.GetAssetPath(sprite);
        byte[] png = File.ReadAllBytes(path);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(png);
        Color32[] pixels = texture.GetPixels32();
        int requiredOpaquePixels = Mathf.CeilToInt(texture.width * 0.7f);
        int surfaceRowFromTop = -1;

        for (int yFromTop = 0; yFromTop < texture.height; yFromTop++)
        {
            int textureY = texture.height - 1 - yFromTop;
            int opaquePixels = 0;
            int rowStart = textureY * texture.width;
            for (int x = 0; x < texture.width; x++)
            {
                if (pixels[rowStart + x].a > 16)
                    opaquePixels++;
            }

            if (opaquePixels >= requiredOpaquePixels)
            {
                surfaceRowFromTop = yFromTop;
                break;
            }
        }

        int textureHeight = texture.height;
        Object.DestroyImmediate(texture);
        if (surfaceRowFromTop < 0)
            return sprite.bounds.extents.y * 0.25f;

        float normalizedFromCenter = 0.5f - surfaceRowFromTop / (float)textureHeight;
        return normalizedFromCenter * sprite.bounds.size.y;
    }

    private static Sprite ImportGeneratedSprite(string path, float pixelsPerUnit)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var textureSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(textureSettings);
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 4096;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static GameObject CreatePlayer(AnimatorController controller, GameObject projectilePrefab)
    {
        Sprite idleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CharacterRoot + "/idle/Idle01.png");
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, -0.8f, 0f);
        player.transform.localScale = Vector3.one * 0.85f;

        SpriteRenderer renderer = player.AddComponent<SpriteRenderer>();
        renderer.sprite = idleSprite;
        renderer.sortingOrder = 10;

        Rigidbody2D body = player.AddComponent<Rigidbody2D>();
        body.freezeRotation = true;
        body.gravityScale = 2.5f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        BoxCollider2D collider = player.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.42f, 0.8f);
        collider.offset = new Vector2(0f, 0.04f);

        Animator animator = player.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        player.AddComponent<PlayerController2D>();
        player.AddComponent<PlayerCombat2D>().Configure(projectilePrefab);
        return player;
    }

    private static void CreateEnemy(Sprite healthBarSprite)
    {
        GameObject orcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OrcPrefabPath);
        if (orcPrefab == null)
        {
            Debug.LogError($"Pixel Runner: ORC prefab was not found at {OrcPrefabPath}");
            return;
        }

        var enemy = new GameObject("Orc Enemy");
        const float platformSurfaceY = -1.5f;
        enemy.transform.position = new Vector3(8.5f, platformSurfaceY, 0f);

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(orcPrefab);
        visual.name = "Orc Visual";
        visual.transform.SetParent(enemy.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one * 1.15f;
        foreach (SpriteRenderer renderer in visual.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.sortingOrder += 12;
        foreach (Collider2D visualCollider in visual.GetComponentsInChildren<Collider2D>(true))
            Object.DestroyImmediate(visualCollider);

        BoxCollider2D collider = enemy.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(1.05f, 1.5f);
        collider.offset = new Vector2(0f, 0.75f);
        collider.isTrigger = true;

        var bar = new GameObject("Health Bar");
        bar.transform.SetParent(enemy.transform, false);
        bar.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        SpriteRenderer background = bar.AddComponent<SpriteRenderer>();
        background.sprite = healthBarSprite;
        background.color = new Color(0.15f, 0.05f, 0.05f, 1f);
        background.sortingOrder = 40;
        bar.transform.localScale = new Vector3(1f, 0.14f, 1f);

        var fillObject = new GameObject("Fill");
        fillObject.transform.SetParent(bar.transform, false);
        SpriteRenderer fill = fillObject.AddComponent<SpriteRenderer>();
        fill.sprite = healthBarSprite;
        fill.color = new Color(0.2f, 0.9f, 0.25f, 1f);
        fill.sortingOrder = 41;
        fillObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        fillObject.transform.localScale = new Vector3(0.9f, 0.65f, 1f);

        WorldHealthBar healthBar = bar.AddComponent<WorldHealthBar>();
        healthBar.Configure(fillObject.transform, 0.9f);
        enemy.AddComponent<EnemyHealth>().Configure(healthBar, 100);
        enemy.AddComponent<EnemyPatrol2D>().Configure(5.8f, 11.2f, 0.65f, visual.transform);
        enemy.AddComponent<EnemyMeleeAttack2D>();
    }

    private static PlayerHealthHud CreatePlayerHud(Sprite solidSprite)
    {
        CreatePixelHeartSprites(out Sprite fullHeart, out Sprite halfHeart, out Sprite emptyHeart);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasObject = new GameObject("Player HUD");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        var eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();

        Image diamondIcon = CreateUiImage("Diamond Icon", canvasObject.transform,
            LoadFirstSprite(DiamondAnimationFolder), Color.white);
        diamondIcon.preserveAspect = true;
        SetRect(diamondIcon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-132f, -24f), new Vector2(52f, 52f), new Vector2(1f, 1f));

        Text diamondCount = CreateUiText("Diamond Count", canvasObject.transform, font,
            "x 00", 34, Color.black, TextAnchor.MiddleLeft);
        diamondCount.fontStyle = FontStyle.Bold;
        SetRect(diamondCount.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-112f, -28f), new Vector2(100f, 48f), new Vector2(0f, 1f));
        canvasObject.AddComponent<DiamondCounterHud>().Configure(diamondCount);

        var hearts = new Image[10];
        for (int i = 0; i < hearts.Length; i++)
        {
            hearts[i] = CreateUiImage($"Heart {i + 1}", canvasObject.transform, fullHeart, Color.white);
            hearts[i].preserveAspect = true;
            SetRect(hearts[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f + i * 49f, -28f), new Vector2(44f, 44f), new Vector2(0f, 1f));
        }

        Image failurePanel = CreateUiImage("Failure Panel", canvasObject.transform, solidSprite,
            new Color(0.07f, 0.015f, 0.08f, 0.84f));
        SetRect(failurePanel.rectTransform, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));

        Text failure = CreateUiText("Failure Text", failurePanel.transform, font,
            "DEFEAT", 58, new Color(1f, 0.68f, 0.78f, 1f), TextAnchor.MiddleCenter);
        failure.fontStyle = FontStyle.Bold;
        SetRect(failure.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 80f), new Vector2(900f, 220f), new Vector2(0.5f, 0.5f));

        PlayerHealthHud hud = canvasObject.AddComponent<PlayerHealthHud>();
        Image buttonImage = CreateUiImage("Restart Button", failurePanel.transform, solidSprite,
            new Color(0.86f, 0.20f, 0.36f, 1f));
        SetRect(buttonImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -100f), new Vector2(300f, 78f), new Vector2(0.5f, 0.5f));
        Button restartButton = buttonImage.gameObject.AddComponent<Button>();
        restartButton.targetGraphic = buttonImage;

        Text restartText = CreateUiText("Restart Text", buttonImage.transform, font,
            "RESTART", 34, Color.white, TextAnchor.MiddleCenter);
        restartText.fontStyle = FontStyle.Bold;
        SetRect(restartText.rectTransform, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));

        hud.Configure(hearts, fullHeart, halfHeart, emptyHeart, failurePanel.gameObject, restartButton);
        return hud;
    }

    private static void CreatePixelHeartSprites(out Sprite full, out Sprite half, out Sprite empty)
    {
        full = CreatePixelHeartSprite("HeartFull", 2);
        half = CreatePixelHeartSprite("HeartHalf", 1);
        empty = CreatePixelHeartSprite("HeartEmpty", 0);
    }

    private static Sprite CreatePixelHeartSprite(string name, int fillUnits)
    {
        const string pattern = "00111011100|01111111110|11111111111|11111111111|01111111110|00111111100|00011111000|00001110000|00000100000";
        const int width = 11;
        string[] rows = pattern.Split('|');
        var texture = new Texture2D(width, rows.Length, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;

        Color32 clear = new(0, 0, 0, 0);
        Color32 outline = new(45, 13, 22, 255);
        Color32 red = new(235, 35, 67, 255);
        Color32 shine = new(255, 112, 126, 255);
        Color32 emptyColor = new(70, 44, 52, 255);
        for (int y = 0; y < rows.Length; y++)
        for (int x = 0; x < width; x++)
        {
            if (rows[y][x] == '0')
            {
                texture.SetPixel(x, rows.Length - 1 - y, clear);
                continue;
            }

            bool edge = x == 0 || x == width - 1 || y == 0 || y == rows.Length - 1 ||
                rows[y][Mathf.Max(0, x - 1)] == '0' || rows[y][Mathf.Min(width - 1, x + 1)] == '0' ||
                rows[Mathf.Max(0, y - 1)][x] == '0' || rows[Mathf.Min(rows.Length - 1, y + 1)][x] == '0';
            bool filled = fillUnits == 2 || (fillUnits == 1 && x <= width / 2);
            texture.SetPixel(x, rows.Length - 1 - y, edge ? outline : filled ? red : emptyColor);
            if (filled && !edge && x == 2 && y == 2)
                texture.SetPixel(x, rows.Length - 1 - y, shine);
        }
        texture.Apply();

        string path = $"Assets/Game/Art/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = 11f;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite CreatePixelDiamondSprite()
    {
        const string path = "Assets/Game/Art/DiamondPlaceholder.png";
        const string pattern = "000010000|000111000|001111100|011111110|111111111|011111110|001111100|000111000|000010000";
        const int width = 9;
        string[] rows = pattern.Split('|');
        var texture = new Texture2D(width, rows.Length, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        Color32 clear = new(0, 0, 0, 0);
        Color32 edge = new(17, 70, 120, 255);
        Color32 blue = new(31, 203, 255, 255);
        Color32 shine = new(205, 250, 255, 255);
        for (int y = 0; y < rows.Length; y++)
        for (int x = 0; x < width; x++)
        {
            if (rows[y][x] == '0')
            {
                texture.SetPixel(x, rows.Length - 1 - y, clear);
                continue;
            }
            bool border = x == 0 || x == width - 1 || y == 0 || y == rows.Length - 1 ||
                rows[y][Mathf.Max(0, x - 1)] == '0' || rows[y][Mathf.Min(width - 1, x + 1)] == '0';
            texture.SetPixel(x, rows.Length - 1 - y, border ? edge : (x <= 3 && y <= 3 ? shine : blue));
        }
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = 9f;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void CreateDiamonds(Sprite sprite, RuntimeAnimatorController controller)
    {
        Vector2[] positions =
        {
            new(-1.5f, -0.72f), new(1.3f, -0.72f),
            new(4.25f, -0.72f), new(6.5f, -0.72f),
            new(9.2f, -0.72f), new(14.5f, 0.63f), new(16.2f, 0.63f)
        };

        for (int i = 0; i < positions.Length; i++)
        {
            var diamond = new GameObject($"Diamond {i + 1}");
            diamond.transform.position = new Vector3(positions[i].x, positions[i].y, 0f);
            diamond.transform.localScale = Vector3.one * 0.42f;
            SpriteRenderer renderer = diamond.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 15;
            Animator animator = diamond.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            CircleCollider2D trigger = diamond.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.42f;
            diamond.AddComponent<DiamondCollectible>();
        }
    }

    private static Image CreateUiImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        Image image = gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        return image;
    }

    private static Text CreateUiText(string name, Transform parent, Font font, string value,
        int size, Color color, TextAnchor alignment)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        Text text = gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, name);
    }
}

#endif
