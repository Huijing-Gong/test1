using System;
using System.Linq;
using UnityEngine;
using SamProduction.BossArena;
public static class BossAuthoredPunchChecks
{
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Place(CharacterController c,Vector3 p){c.enabled=false;c.transform.position=p;c.enabled=true;Physics.SyncTransforms();}
    public static string Run()
    {
        var a=UnityEngine.Object.FindFirstObjectByType<ArenaSession>();var library=a.boss.GetComponent<ArenaPunchLibrary>();
        Require(library&&library.punches.Length==8,"Missing original punch library");
        bool auto=a.automaticTick,decision=a.boss.decisionEnabled;float worstPose=0;
        a.automaticTick=false;a.boss.decisionEnabled=false;
        var animator=a.boss.animationDriver.GetComponent<Animator>();
        var bones=new[]{HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand}.Select(animator.GetBoneTransform).ToArray();
        try
        {
            foreach(var p in library.punches)
            {
                a.Restart(true);a.player.fighter.TrainingInvincible=true;
                Place(a.player.capsule,a.boss.transform.position+a.boss.transform.forward*((p.minimumRange+p.maximumRange)*.5f));
                Require(a.boss.ForceAttack(p.attack,a),"Could not start "+p.attack);
                for(int frame=0;frame<Mathf.CeilToInt((p.clip.length+.2f)*120);frame++)
                {
                    a.Simulate(default,1f/120);
                    if(a.boss.Attacking&&frame%5==0)
                    {
                        var actual=bones.Select(b=>b.localRotation).ToArray();
                        a.boss.animationDriver.PoseContactClip(p.clip,a.boss.GetComponent<ArenaEclipseBossMotor>().SkillTime,0);
                        for(int b=0;b<bones.Length;b++)worstPose=Mathf.Max(worstPose,Quaternion.Angle(actual[b],bones[b].localRotation));
                    }
                }
                Require(a.player.fighter.HitCount==p.contacts.Length,"Wrong contact count: "+p.attack+" got "+a.player.fighter.HitCount);
                Require(a.Events.Count(e=>e.kind=="boss_v13_release")==p.contacts.Length,"Duplicate or missing VFX release");
                Require(!a.boss.Attacking,"Failed recovery "+p.attack);
                a.Restart(true);Place(a.player.capsule,a.boss.transform.position-a.boss.transform.forward*3);a.boss.ForceAttack(p.attack,a);
                for(int frame=0;frame<Mathf.CeilToInt((p.clip.length+.2f)*120);frame++)a.Simulate(default,1f/120);
                Require(a.player.fighter.HitCount==0,"Punch hits player outside authored path: "+p.attack);
            }
            Require(worstPose<.1f,"Source arm pose was modified");
            return $"PASS eight original clips, all intended contacts, unique releases, recovery, behind-player misses; shoulder/elbow/wrist difference from source={worstPose:F4} degrees";
        }
        finally{a.Restart(false);a.player.fighter.TrainingInvincible=false;a.automaticTick=auto;a.boss.decisionEnabled=decision;}
    }
}
