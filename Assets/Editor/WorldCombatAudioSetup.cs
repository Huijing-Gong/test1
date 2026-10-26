using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KingOfGraphic.MechBossMigration;

public static class WorldCombatAudioSetup
{
    const string AudioFolder="Assets/_Demo/Audio/WorldCombat/";
    static T Add<T>(GameObject owner)where T:Component{var item=owner.GetComponent<T>();return item?item:Undo.AddComponent<T>(owner);}
    static AudioClip Clip(string path){var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);if(!clip)throw new Exception("Missing audio "+path);return clip;}
    static bool StoneName(string name){name=name.ToLowerInvariant();return new[]{"limestone","paving","cobalt","marble","cobble","plank","dock","wood","stone floor"}.Any(name.Contains);}
    public static string Install()
    {
        if(Application.isPlaying)throw new Exception("Use edit mode.");
        var boss=UnityEngine.Object.FindFirstObjectByType<MechSentinelBattle>();
        if(!boss||!boss.player)throw new Exception("Open the main world with its island Boss.");
        var player=boss.player;var flow=UnityEngine.Object.FindFirstObjectByType<BaselineFlow>();
        var map=Add<WorldFootstepSurfaces>(flow.gameObject);
        Undo.RecordObject(map,"Map visible footstep surfaces");
        map.stoneSurfaces=UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(f=>{
            if(f.gameObject.scene!=flow.gameObject.scene||!f.sharedMesh||!f.sharedMesh.isReadable)return false;
            var r=f.GetComponent<MeshRenderer>();
            return r&&r.bounds.size.y<.45f&&r.bounds.size.x>.5f&&r.bounds.size.z>.5f&&r.sharedMaterials.Any(m=>m&&StoneName(m.name));
        }).ToArray();
        var sword=Add<WorldPlayerAudio>(player.gameObject);Undo.RecordObject(sword,"Install selected sword whoosh");
        sword.bladeWhoosh=Clip("Assets/SAM/BossArena/Audio/Clips/BladeWhoosh.wav");sword.swingVolume=.38f;
        var feet=Add<GroundedFootstepAudio>(player.gameObject);Undo.RecordObject(feet,"Install hero ground footsteps");
        feet.stoneStep=Clip(AudioFolder+"StoneFootstep.wav");feet.grassStep=Clip(AudioFolder+"GrassFootstep.ogg");
        feet.surfaces=map;feet.heavy=false;feet.volume=.18f;
        var impact=Add<StoneBossAudio>(boss.gameObject);Undo.RecordObject(impact,"Install ground impact audio");
        impact.stompImpact=Clip(AudioFolder+"StompImpact.wav");impact.impactVolume=.62f;
        var heavy=Add<GroundedFootstepAudio>(boss.gameObject);Undo.RecordObject(heavy,"Install heavy Boss footsteps");
        heavy.stoneStep=heavy.grassStep=Clip("Assets/SAM/BossArena/Audio/Clips/StepHeavy.wav");heavy.surfaces=map;heavy.heavy=true;heavy.volume=.32f;
        foreach(var item in new UnityEngine.Object[]{map,sword,feet,impact,heavy}){EditorUtility.SetDirty(item);PrefabUtility.RecordPrefabInstancePropertyModifications(item);}
        foreach(var path in new[]{AudioFolder+"StoneFootstep.wav",AudioFolder+"GrassFootstep.ogg",AudioFolder+"StompImpact.wav"}){
            var importer=(AudioImporter)AssetImporter.GetAtPath(path);var settings=importer.defaultSampleSettings;
            importer.forceToMono=true;settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.65f;
            importer.defaultSampleSettings=settings;importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);EditorSceneManager.SaveScene(flow.gameObject.scene);
        return "Installed selected actor audio; "+map.stoneSurfaces.Length+" visible stone/wood surfaces; original Boss hit clips untouched.";
    }
}
