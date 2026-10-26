using UnityEditor;
using UnityEngine;

public sealed class P4JournalTextureImport : AssetPostprocessor
{
    public override uint GetVersion() => 2;

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/_Demo/Resources/P4Journal/")) return;
        var importer=(TextureImporter)assetImporter;
        importer.textureType=TextureImporterType.Default;
        importer.textureShape=TextureImporterShape.Texture2D;
        importer.alphaSource=TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency=true;
        bool stillPage=assetPath.Contains("/Pages/") || assetPath.EndsWith("/BookmarkTop.png");
        importer.mipmapEnabled=stillPage;
        importer.isReadable=false;
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.maxTextureSize=2048;
        importer.textureCompression=stillPage?TextureImporterCompression.Uncompressed:TextureImporterCompression.CompressedHQ;
        importer.compressionQuality=95;
        importer.crunchedCompression=!stillPage;
        importer.filterMode=stillPage?FilterMode.Trilinear:FilterMode.Bilinear;
        importer.wrapMode=TextureWrapMode.Clamp;
    }
}
