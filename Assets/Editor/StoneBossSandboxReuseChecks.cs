using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using KingOfGraphic.MechBossMigration;

public static class StoneBossSandboxReuseChecks
{
    public static string Presentation()
    {
        var boss=UnityEngine.Object.FindFirstObjectByType<MechSentinelBattle>();
        var type=typeof(Health).Assembly.GetType("WardenContactPresentation");
        if(type==null||!boss.GetComponent(type))throw new Exception("FAIL: stone Boss still has no Warden contact presentation adapter.");
        var feedback=boss.GetComponent<MistGardenBossFeedback>();
        var flag=typeof(MistGardenBossFeedback).GetField("externalHitVisuals");
        if(flag==null||!(bool)flag.GetValue(feedback)||feedback.contactFeedback)throw new Exception("FAIL: old orange contact/amber flash still owns hit presentation.");
        return "PASS: Warden contact adapter owns visuals; old orange contact/amber flash disabled.";
    }
    public static string SourceHeavy()
    {
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(StoneBossCombatRevisionSetup.TrialController);
        var state=controller.layers[0].stateMachine.states.First(x=>x.state.name=="SwordHeavy").state;
        if(state.motion!=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/WorldHero/PlayerBattle/SwordHeavy.anim")||Mathf.Abs(state.speed-1)>.001f)
            throw new Exception("FAIL: heavy still uses the great-sword trial instead of the verified Sandbox source clip.");
        return "PASS: original Sandbox heavy and playback restored.";
    }
    public static string GuardGrip()
    {
        var original=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>().GetComponentInChildren<Animator>();
        var clone=UnityEngine.Object.Instantiate(original.gameObject);
        try {
            var animator=clone.GetComponent<Animator>();animator.Rebind();animator.Play("WorldGuardLoop",0,.5f);animator.Update(0);
            using(var handler=new HumanPoseHandler(animator.avatar,animator.transform)) {
                var pose=new HumanPose();handler.GetHumanPose(ref pose);float sum=0;int count=0;
                for(int i=0;i<HumanTrait.MuscleCount;i++){var name=HumanTrait.MuscleName[i];if(name.StartsWith("Right ")&&name.Contains("Stretched")&&!name.Contains("Thumb")){sum+=pose.muscles[i];count++;}}
                float mean=sum/Mathf.Max(1,count);
                if(mean>-.4f)throw new Exception("FAIL: guard finger curl is not a closed weapon grip; mean="+mean);
                return "PASS: guard right-hand finger curl mean="+mean;
            }
        } finally {UnityEngine.Object.DestroyImmediate(clone);}
    }
}
