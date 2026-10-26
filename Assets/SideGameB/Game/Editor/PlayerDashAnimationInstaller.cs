// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerDashAnimationInstaller
{
    private const string ControllerPath = "Assets/Game/Animations/Princess.controller";
    private const string DashClipPath = "Assets/Sword Princess/Animation/dash.anim";
    private const string WindSpritePath = "Assets/Resources/DashWind.png";

    static PlayerDashAnimationInstaller()
    {
        EditorApplication.delayCall += Install;
    }

    private static void Install()
    {
        ConfigureWindSprite();
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimationClip dashClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DashClipPath);
        if (controller == null || dashClip == null)
            return;

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        if (stateMachine.states.Any(child => child.state.name == "dash"))
            return;

        stateMachine.AddState("dash").motion = dashClip;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("Pixel Runner: dash animation added without rebuilding the scene.");
    }

    private static void ConfigureWindSprite()
    {
        AssetDatabase.ImportAsset(WindSpritePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(WindSpritePath) as TextureImporter;
        if (importer == null)
            return;

        bool needsUpdate = importer.textureType != TextureImporterType.Sprite ||
                           importer.spriteImportMode != SpriteImportMode.Single ||
                           !Mathf.Approximately(importer.spritePixelsPerUnit, 600f) ||
                           importer.filterMode != FilterMode.Point || importer.mipmapEnabled ||
                           importer.textureCompression != TextureImporterCompression.Uncompressed;
        if (!needsUpdate)
            return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 600f;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }
}

#endif
