using System;
using System.Linq;
using UnityEngine;
using SamProduction.BossArena;

public static class BossDefensePlayChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Place(CharacterController c,Vector3 p){c.enabled=false;c.transform.position=p;c.enabled=true;Physics.SyncTransforms();}
    static void Step(ArenaSession a,float seconds){for(int i=0;i<Mathf.CeilToInt(seconds*120);i++)a.Simulate(default,1f/120);}
    static ArenaHit Hit(ArenaSession a,long strike)=>new ArenaHit{source=a.player.fighter,strike=strike,damage=20,push=0,point=a.boss.Socket("socket_chest").position,direction=a.boss.transform.position-a.player.transform.position,skill="guard_check"};
    public static string Run()
    {
        var a=UnityEngine.Object.FindFirstObjectByType<ArenaSession>();var d=a.boss.GetComponent<ArenaBossDefense>();
        Require(d,"Boss defense missing");bool automatic=a.automaticTick,decision=a.boss.decisionEnabled;
        a.automaticTick=false;a.boss.decisionEnabled=false;
        try
        {
            a.Restart(true);Place(a.player.capsule,a.boss.transform.position+a.boss.transform.forward*2.5f);
            Require(d.BeginGuard(a),"Could not raise guard");float health=a.boss.fighter.Health;
            a.boss.fighter.Receive(Hit(a,a.NewStrike()));
            Require(Mathf.Abs(health-a.boss.fighter.Health-20)<.01f,"Startup was invulnerable");
            a.Restart(true);Place(a.player.capsule,a.boss.transform.position+a.boss.transform.forward*2.5f);
            Require(d.BeginGuard(a),"Could not reset guard");Step(a,.25f);
            Require(!a.boss.ForceAttack(ArenaAttack.Jab,a),"Guard and attack overlap");
            health=a.boss.fighter.Health;long strike=a.NewStrike();a.boss.fighter.Receive(Hit(a,strike));
            Require(Mathf.Abs(health-a.boss.fighter.Health-4)<.01f&&a.boss.fighter.BlockCount==1,"Frontal block did not reduce damage");
            a.boss.fighter.Receive(Hit(a,strike));Require(a.boss.fighter.BlockCount==1,"Duplicate guard strike");
            a.boss.fighter.Receive(Hit(a,a.NewStrike()));Require(a.boss.fighter.BlockCount==2,"Second block missing");
            health=a.boss.fighter.Health;a.boss.fighter.Receive(Hit(a,a.NewStrike()));
            Require(Mathf.Abs(health-a.boss.fighter.Health-20)<.01f,"Third block exceeded guard limit");
            a.Restart(true);Place(a.player.capsule,a.boss.transform.position+a.boss.transform.forward*2.5f);d.BeginGuard(a);Step(a,.25f);
            BossSpellPlayChecks.Capture(a,"defense-live",true);
            Place(a.player.capsule,a.boss.transform.position-a.boss.transform.forward*2.5f);health=a.boss.fighter.Health;
            a.boss.fighter.Receive(Hit(a,a.NewStrike()));Require(Mathf.Abs(health-a.boss.fighter.Health-20)<.01f,"Guard blocked rear attack");
            Require(d.Reacting,"Unblocked hit had no reaction");Step(a,.15f);BossSpellPlayChecks.Capture(a,"hurt-live",true);
            Step(a,1.3f);Require(!d.BeginGuard(a),"Guard cooldown bypassed");
            a.Restart(false);Require(!d.Active&&d.VisualCount==0,"Reset leaked defense");
            a.Restart(true);a.player.fighter.TrainingInvincible=true;
            var origin=a.boss.transform.position;var right=a.boss.transform.right;
            Place(a.player.capsule,origin+a.boss.transform.forward*9);a.boss.ForceAttack(ArenaAttack.GroundRift,a);Step(a,.16f);
            Place(a.player.capsule,origin+right*9);
            while(!a.Events.Any(e=>e.kind=="boss_spell_released"))a.Simulate(default,1f/120);
            var released=a.boss.transform.position;var target=a.player.transform.position;
            Place(a.player.capsule,origin-right*9);Step(a,2.5f);
            var impacts=a.Events.Where(e=>e.kind=="boss_spell_impact"&&e.skill=="GroundRift").ToArray();Require(impacts.Length==5,"Wrong spike count");
            var aim=Vector3.ProjectOnPlane(target-released,Vector3.up);
            foreach(var e in impacts)Require(Vector3.Angle(Vector3.ProjectOnPlane(e.point-released,Vector3.up),aim)<1,"Spike missed release heading or chased after release");
            return "PASS guard startup/front/rear/two-hit limit/strike dedup/recoil/cooldown/reset; five spikes lock release-time target";
        }
        finally{a.Restart(false);a.automaticTick=automatic;a.boss.decisionEnabled=decision;}
    }
}
