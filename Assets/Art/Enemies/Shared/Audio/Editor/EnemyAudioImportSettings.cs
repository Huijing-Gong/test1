using UnityEditor;
using UnityEngine;

// Short original mono transients: tiny PCM avoids streaming/decode latency on cues.
public sealed class EnemyAudioImportSettings : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/Art/Enemies/Shared/Audio/Resources/EnemyAudio/")) return;
        var importer = (AudioImporter)assetImporter;
        importer.forceToMono = true;
        var serialized = new SerializedObject(importer);
        var normalize = serialized.FindProperty("m_Normalize");
        if (normalize != null) { normalize.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        importer.loadInBackground = false;
        var settings = importer.defaultSampleSettings;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.preloadAudioData = true;
        settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
        settings.sampleRateOverride = 22050;
        importer.defaultSampleSettings = settings;
    }
}
