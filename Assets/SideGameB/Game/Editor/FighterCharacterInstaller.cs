// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class FighterCharacterInstaller
{
    private const string PackagePath = "/Users/gonghuiji/Documents/精神食粮/2D Pixel Punch Hero 3 2.unitypackage";
    private const string FighterAssetRoot = "Assets/Punch Hero 3/Animation";
    private const string FighterControllerPath = "Assets/Punch Hero 3/Animation/Idle01.controller";
    private const string SlashTexturePath = "Assets/Game/Resources/Effects/FighterSlash.png";
    private static bool importRequested;

    static FighterCharacterInstaller()
    {
        AssetDatabase.importPackageCompleted += OnImportCompleted;
        EditorApplication.delayCall += Install;
        EditorApplication.update += InstallAfterPlayMode;
    }

    private static void InstallAfterPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Install();
        EditorApplication.update -= InstallAfterPlayMode;
    }

    [MenuItem("Pixel Runner/Add Fighter Character Choice")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;

        AnimationClip idle = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{FighterAssetRoot}/idle.anim");
        if (idle == null)
        {
            if (!importRequested && File.Exists(PackagePath))
            {
                importRequested = true;
                AssetDatabase.ImportPackage(PackagePath, false);
            }
            return;
        }

        ConfigureSlashTexture();
        ConfigureScene();
    }

    private static void ConfigureSlashTexture()
    {
        TextureImporter importer = AssetImporter.GetAtPath(SlashTexturePath) as TextureImporter;
        if (importer == null) return;
        bool needsImport = !importer.isReadable || importer.textureType != TextureImporterType.Sprite ||
                           importer.spriteImportMode != SpriteImportMode.Single || importer.filterMode != FilterMode.Point;
        if (!needsImport) return;
        importer.isReadable = true;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
    }

    private static void OnImportCompleted(string packageName)
    {
        importRequested = false;
        EditorApplication.delayCall += Install;
    }

    private static void ConfigureScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlayerTest") return;

        AnimatorController fighterController = AssetDatabase.LoadAssetAtPath<AnimatorController>(FighterControllerPath);
        if (fighterController == null) return;

        GameObject player = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Player");
        if (player == null) return;

        Animator animator = player.GetComponent<Animator>();
        PlayerCombat2D princessCombat = player.GetComponent<PlayerCombat2D>();
        FighterMeleeCombat2D fighterCombat = player.GetComponent<FighterMeleeCombat2D>();
        if (fighterCombat == null) fighterCombat = player.AddComponent<FighterMeleeCombat2D>();
        fighterCombat.enabled = false;

        CharacterSelectionUI selection = player.GetComponent<CharacterSelectionUI>();
        if (selection == null) selection = player.AddComponent<CharacterSelectionUI>();
        selection.Configure(animator.runtimeAnimatorController, fighterController);

        EditorUtility.SetDirty(fighterCombat);
        EditorUtility.SetDirty(selection);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

}
#endif

#endif
