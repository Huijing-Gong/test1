using UnityEditor;
using UnityEngine;

public sealed class AdventureCreatureVoiceImport : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if(!assetPath.Contains("/Resources/CreatureVoices/"))return;
        var importer=(AudioImporter)assetImporter;importer.forceToMono=true;importer.loadInBackground=false;
        var data=new SerializedObject(importer);var normalize=data.FindProperty("m_Normalize");
        if(normalize!=null){normalize.boolValue=false;data.ApplyModifiedPropertiesWithoutUndo();}
        var sample=importer.defaultSampleSettings;sample.loadType=AudioClipLoadType.DecompressOnLoad;sample.preloadAudioData=true;
        sample.compressionFormat=AudioCompressionFormat.Vorbis;sample.quality=.55f;
        sample.sampleRateSetting=AudioSampleRateSetting.OverrideSampleRate;sample.sampleRateOverride=22050;importer.defaultSampleSettings=sample;
    }
}
