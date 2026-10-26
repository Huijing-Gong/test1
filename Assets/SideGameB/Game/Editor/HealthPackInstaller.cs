// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class HealthPackInstaller
{
    private const string ScenePath = "Assets/Game/Scenes/PlayerTest.unity";
    private const string FramesFolder = "Assets/Game/Art/HealthPackAnimation";
    private const string ObjectName = "Animated Health Pack";
    private const string InstallKey = "PixelRunner.AnimatedHealthPack.Repaired.v2";

    static HealthPackInstaller()
    {
        EditorApplication.delayCall += InstallOnce;
    }

    [MenuItem("Pixel Runner/Add Animated Health Pack (Preserve Scene)")]
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
        if (scene.path != ScenePath && scene.name != "PlayerTest")
        {
            if (showMessage)
                Debug.LogWarning("Pixel Runner: open PlayerTest scene before adding the health pack.");
            return;
        }

        GameObject existing = GameObject.Find(ObjectName);
        if (existing != null)
        {
            Sprite[] existingFrames = ImportFrames();
            if (existingFrames.Length == 0)
                return;
            RuntimeAnimatorController existingController = CreateController(existingFrames);
            ConfigureHealthPack(existing, existingFrames[0], existingController);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorPrefs.SetBool(InstallKey, true);
            Selection.activeGameObject = existing;
            Debug.Log("Pixel Runner: existing health pack components repaired. Save the scene to keep the fix.");
            return;
        }

        Sprite[] frames = ImportFrames();
        if (frames.Length == 0)
            return;
        RuntimeAnimatorController controller = CreateController(frames);

        var healthPack = new GameObject(ObjectName);
        Undo.RegisterCreatedObjectUndo(healthPack, "Add Animated Health Pack");
        healthPack.transform.position = new Vector3(30.7f, 3.02f, 0f);
        healthPack.transform.localScale = Vector3.one * 0.52f;

        ConfigureHealthPack(healthPack, frames[0], controller);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorPrefs.SetBool(InstallKey, true);
        Selection.activeGameObject = healthPack;
        Debug.Log("Pixel Runner: animated health pack added without rebuilding the scene. Save the scene to keep it.");
    }

    private static void ConfigureHealthPack(GameObject healthPack, Sprite firstFrame,
        RuntimeAnimatorController controller)
    {
        SpriteRenderer renderer = healthPack.GetComponent<SpriteRenderer>();
        if (renderer == null)
            renderer = healthPack.AddComponent<SpriteRenderer>();
        renderer.sprite = firstFrame;
        renderer.sortingOrder = 16;

        Animator animator = healthPack.GetComponent<Animator>();
        if (animator == null)
            animator = healthPack.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.enabled = true;

        CircleCollider2D trigger = healthPack.GetComponent<CircleCollider2D>();
        if (trigger == null)
            trigger = healthPack.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 0.48f;

        if (healthPack.GetComponent<HealthPackPickup2D>() == null)
            healthPack.AddComponent<HealthPackPickup2D>();
    }

    private static Sprite[] ImportFrames()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { FramesFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 300f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        return AssetDatabase.FindAssets("t:Sprite", new[] { FramesFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path)
            .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
            .Where(sprite => sprite != null)
            .ToArray();
    }

    private static RuntimeAnimatorController CreateController(Sprite[] frames)
    {
        const string clipPath = "Assets/Game/Animations/health pack loop.anim";
        const string controllerPath = "Assets/Game/Animations/HealthPack.controller";
        AssetDatabase.DeleteAsset(clipPath);
        AssetDatabase.DeleteAsset(controllerPath);

        const float frameRate = 8f;
        var clip = new AnimationClip { name = "health pack loop", frameRate = frameRate };
        ObjectReferenceKeyframe[] keys = frames.Select((sprite, index) => new ObjectReferenceKeyframe
        {
            time = index / frameRate,
            value = sprite
        }).ToArray();
        AnimationUtility.SetObjectReferenceCurve(clip,
            EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite"), keys);
        AnimationUtility.SetAnimationClipSettings(clip, new AnimationClipSettings { loopTime = true });
        AssetDatabase.CreateAsset(clip, clipPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorState state = controller.layers[0].stateMachine.AddState("loop");
        state.motion = clip;
        controller.layers[0].stateMachine.defaultState = state;
        AssetDatabase.SaveAssets();
        return controller;
    }
}

#endif
