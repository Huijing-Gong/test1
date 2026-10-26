using UnityEditor;
using UnityEngine;

public sealed class PlayerHealthTextureImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (assetPath != "Assets/_Demo/Resources/PlayerHealthUI/HealthBarFigma.png") return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None; importer.isReadable = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 1024; importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
    }
}
