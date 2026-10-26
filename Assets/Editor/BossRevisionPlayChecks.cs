using System;
using System.Linq;
using UnityEngine;
using SamProduction.BossArena;

public static class BossRevisionPlayChecks
{
    static void Require(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static void Place(CharacterController c,Vector3 p){c.enabled=false;c.transform.position=p;c.enabled=true;Physics.SyncTransforms();}
    public static string Run()
    {
        var a=UnityEngine.Object.FindFirstObjectByType<ArenaSession>();
        bool automatic=a.automaticTick,decisions=a.boss.decisionEnabled;
        a.automaticTick=false;a.boss.decisionEnabled=false;
        var m=a.boss.GetComponent<ArenaEclipseBossMotor>();var animator=a.boss.animationDriver.GetComponent<Animator>();
        try
        {
            string originalReport=BossAuthoredPunchChecks.Run();
            a.Restart(true);Require(!a.boss.ForceAttack(ArenaAttack.DashPunch,a),"Removed DashPunch still available");float ground=a.boss.transform.position.y;
            a.boss.fighter.Receive(new ArenaHit{source=a.player.fighter,strike=a.NewStrike(),damage=a.boss.fighter.maximumHealth*.51f,push=0,skill="phase_check"});
            a.boss.decisionEnabled=true;
            for(int i=0;i<100;i++)a.Simulate(default,1f/120);
            Require(m.CombatPhase==2&&m.CurrentAttack==ArenaAttack.Meteor,"Phase did not enter Meteor directly");
            Require(!a.Events.Any(e=>e.kind=="boss_attack_started"&&e.skill=="Ignite"),"Separate phase animation returned");
            a.boss.decisionEnabled=false;
            a.Restart(true);a.player.fighter.TrainingInvincible=true;a.boss.ForceAttack(ArenaAttack.Meteor,a);
            for(int i=0;i<720;i++)a.Simulate(default,1f/120);
            Require(a.boss.animationDriver.ClipName==m.meteorRaiseClip.name&&Mathf.Abs(m.SourceAnimationTime-1.3f)<.001f,"Meteor did not hold original open-arms pose");
            for(int i=0;i<480;i++)a.Simulate(default,1f/120);
            Require(a.Events.Count(e=>e.kind=="boss_v13_release"&&e.skill=="Meteor_0")==1,"Meteor release duplicated or absent");
            Require(!m.Attacking&&Mathf.Abs(a.boss.transform.position.y-ground)<.05f,"Meteor failed to land/recover");
            return originalReport+"; phase directly enters Meteor; one Meteor impact and landing";
        }
        finally{a.Restart(false);a.player.fighter.TrainingInvincible=false;a.automaticTick=automatic;a.boss.decisionEnabled=decisions;}
    }
}
