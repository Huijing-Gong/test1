// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public static class WebGLSizeOptimizer
{
    private const string AppliedKey = "PixelRunner.WebGLSizeOptimizer.v5.GitHubPagesFallback";

    static WebGLSizeOptimizer()
    {
        EditorApplication.delayCall += ApplyOnce;
    }

    [MenuItem("Pixel Runner/Optimize WebGL Build Size")]
    public static void Apply()
    {
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        // GitHub Pages cannot add Unity's required Content-Encoding header for
        // Brotli files, so let the Unity loader decompress them in the browser.
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;

        CompressLargeWebGLTextures();

        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(AppliedKey, true);
        Debug.Log("Pixel Runner: WebGL size optimization enabled (Brotli, size-optimized IL2CPP, stripping, and high-quality Crunch compression for large textures). Gameplay logic was not changed.");
    }

    private static void CompressLargeWebGLTextures()
    {
        string[] roots = { "Assets/Game/Art", "Assets/Game/Resources", "Assets/Resources" };
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", roots))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!File.Exists(path) || new FileInfo(path).Length < 350 * 1024)
                continue;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                continue;

            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("WebGL");
            settings.name = "WebGL";
            settings.overridden = true;
            settings.maxTextureSize = Mathf.Min(importer.maxTextureSize, 2048);
            settings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            settings.format = importer.DoesSourceTextureHaveAlpha()
                ? TextureImporterFormat.DXT5Crunched
                : TextureImporterFormat.DXT1Crunched;
            settings.textureCompression = TextureImporterCompression.CompressedHQ;
            settings.compressionQuality = 80;
            settings.crunchedCompression = true;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
        }
    }

    private static void ApplyOnce()
    {
        if (!EditorPrefs.GetBool(AppliedKey, false))
            Apply();
    }
}
#endif

#endif
