using UnityEditor;
using UnityEngine;

public sealed class P3RewardTextureImport : AssetPostprocessor
{
    public override uint GetVersion() => 1;

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/_Demo/Resources/P3Rewards/")) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        bool pixelIcon = assetPath.EndsWith("IslandRelicIcon.png") || assetPath.EndsWith("RidgeCharmIcon.png");
        pixelIcon |= assetPath.EndsWith("IslandRelicIconV5.png") || assetPath.EndsWith("RidgeCharmIconV5.png") || assetPath.EndsWith("RunicGuardIconV5.png");
        bool rewardIcon = assetPath.EndsWith("IslandRelicIconV2.png") ||
                          assetPath.EndsWith("RidgeCharmIconV2.png") ||
                          assetPath.EndsWith("RunicGuardIcon.png") ||
                          assetPath.EndsWith("IslandRelicIconV3.png") ||
                          assetPath.EndsWith("RidgeCharmIconV3.png") ||
                          assetPath.EndsWith("RunicGuardIconV2.png") ||
                          assetPath.EndsWith("IslandRelicIconV4.png") ||
                          assetPath.EndsWith("RidgeCharmIconV4.png") ||
                          assetPath.EndsWith("RunicGuardIconV4.png") ||
                          assetPath.EndsWith("IslandRelicIconV6.png") ||
                          assetPath.EndsWith("RidgeCharmIconV6.png") ||
                          assetPath.EndsWith("RunicGuardIconV6.png");
        importer.textureCompression = pixelIcon || rewardIcon ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
        importer.compressionQuality = 95;
        importer.crunchedCompression = true;
        importer.filterMode = pixelIcon ? FilterMode.Point : FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
    }
}
