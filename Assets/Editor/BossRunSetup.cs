using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SamProduction.BossArena;

public static class BossRunSetup
{
    public const string Path="Assets/BossSpells/Clips/BossRun.anim";
    [MenuItem("ArtLab/Boss/Prepare grounded run")]
    public static void Prepare()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Prepare outside play mode");
        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AuthorMotions/Clips/Expanded/MapRun.anim");
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Path);
        if(!clip){clip=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(clip,Path);}
        else EditorUtility.CopySerialized(source,clip);
        clip.name="BossRun";AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            if(binding.type==typeof(Animator)&&(binding.propertyName=="RootT.x"||binding.propertyName=="RootT.z"))
                AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,clip.length,0));
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=true;
        settings.keepOriginalOrientation=true;
        AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);AssetDatabase.SaveAssets();
    }
    public static void Apply(float measuredStride)
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Save outside play mode");
        var brain=UnityEngine.Object.FindFirstObjectByType<ArenaBossBrain>();
        var clips=brain.GetComponent<ArenaEclipseClips>();
        Undo.RecordObjects(new UnityEngine.Object[]{clips,brain.v13Motor,brain.animationDriver},"Improve Boss run");
        clips.RUN=AssetDatabase.LoadAssetAtPath<AnimationClip>(Path);clips.authoredLocomotion=true;
        brain.animationDriver.authoredRun=clips.RUN;
        brain.v13Motor.runStride=measuredStride;
        // Preserve the measured stride while restoring the source's natural cadence.
        brain.v13Motor.runSpeed=measuredStride/clips.RUN.length;
        EditorUtility.SetDirty(clips);EditorUtility.SetDirty(brain.v13Motor);EditorUtility.SetDirty(brain.animationDriver);
        EditorSceneManager.MarkSceneDirty(brain.gameObject.scene);EditorSceneManager.SaveScene(brain.gameObject.scene);
    }
}
