using UnityEditor;
using UnityEngine;

public sealed class RioUITextureImport : AssetPostprocessor
{
    public override uint GetVersion() => 4;

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/_Demo/Resources/RioDialogue/") &&
            !assetPath.StartsWith("Assets/_Demo/Resources/SideWorldDialogue/") &&
            !assetPath.StartsWith("Assets/_Demo/Resources/GuardianDialogue/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false; importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
    }
}
