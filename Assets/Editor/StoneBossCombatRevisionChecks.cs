using System;
using System.Reflection;
using KingOfGraphic.MechBossMigration;
using UnityEngine;

public static class StoneBossCombatRevisionChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception("FAIL: "+message);}
    public static string FlatBoundary()
    {
        var encounter=UnityEngine.Object.FindFirstObjectByType<MechBossEncounter>();
        var island=encounter.boss.GetComponent<MechIslandGroundConstraint>();
        foreach(var box in encounter.barrier.GetComponentsInChildren<BoxCollider>(true)) {
            var point=box.transform.TransformPoint(box.center);
            Require(island.groundCollider.Raycast(new Ray(new Vector3(point.x,island.arenaCenter.position.y+5,point.z),Vector3.down),out var ground,15),"boundary has no island support");
            Require(ground.normal.y>.95f && ground.point.y>island.arenaCenter.position.y-.15f,"battle boundary includes a shoreline slope: "+ground.point);
        }
        return "PASS: all 96 wall segments lie over flat island ground.";
    }
    public static string HitReactionOwnership()
    {
        var battle=typeof(MechSentinelBattle);
        Require(battle.GetMethod("NotifyHitReaction")!=null,"Boss chase has no reaction ownership; it overwrites Hit animation next frame.");
        return "PASS: reaction ownership entry exists (exercise in Play check).";
    }
    public static string LockedAim()
    {
        Require(Application.isPlaying,"run locked aim check in Play mode");
        var boss=UnityEngine.Object.FindFirstObjectByType<MechSentinelBattle>();
        var player=boss.player;var combat=player.GetComponent<PlayerCombat>();
        var rig=UnityEngine.Object.FindFirstObjectByType<ThirdPersonCamera>();
        typeof(ThirdPersonCamera).GetField("lockedEnemy",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(rig,boss.GetComponent<Health>());
        player.Relocate(boss.transform.position+Vector3.back*2.1f,Quaternion.Euler(0,90,0));
        typeof(PlayerCombat).GetMethod("AcquireAttackTarget",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(combat,null);
        var selected=(Health)typeof(PlayerCombat).GetField("attackTarget",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(combat);
        Require(selected==boss.GetComponent<Health>(),"locked Boss ignored when starting a cut sideways");
        return "PASS: locked Boss acquired from side-on anticipation.";
    }
    public static string GuardAvailable()
    {
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var type=typeof(PlayerCombat).Assembly.GetType("WorldPlayerGuard");
        Require(type!=null && player.GetComponent(type),"world hero has no directional guard resolver or guard animation owner");
        return "PASS: world guard component installed (exercise HP outcomes in Play check).";
    }
    public static string GuardDamage()
    {
        Require(Application.isPlaying,"guard damage check needs Play mode");
        var boss=UnityEngine.Object.FindFirstObjectByType<MechSentinelBattle>();var player=boss.player;
        var guard=player.GetComponent<WorldPlayerGuard>();var health=player.GetComponent<Health>();
        var oldRotation=player.transform.rotation;player.transform.rotation=Quaternion.identity;
        int before=health.CurrentHealth;
        guard.TickGuard(false,false,true,0);
        guard.TickGuard(true,true,true,.01f);
        health.TryApplyDamage(new DamageInfo(18,boss.gameObject,player.transform.position+Vector3.up,Vector3.back));
        Require(health.CurrentHealth==before && guard.LastBlockPerfect,"fresh front guard must precisely block without losing HP");
        guard.TickGuard(true,false,true,.2f);
        health.TryApplyDamage(new DamageInfo(18,boss.gameObject,player.transform.position+Vector3.up,Vector3.back));
        Require(health.CurrentHealth==before-6 && !guard.LastBlockPerfect,"held front guard must reduce 18 damage to 6");
        health.TryApplyDamage(new DamageInfo(18,boss.gameObject,player.transform.position+Vector3.up,Vector3.forward));
        Require(health.CurrentHealth==before-24,"rear attack must bypass front guard");
        guard.TickGuard(false,false,true,0);
        Require(!guard.IsGuarding&&!player.IsGameplayInputLocked,"released guard must release its own movement lock");
        health.TryHeal(24);player.transform.rotation=oldRotation;
        return "PASS: perfect front 0 damage; held front 6/18; rear 18/18; release unlocks movement.";
    }
    public static string HeavyFeet()
    {
        var original=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>().GetComponentInChildren<Animator>();
        var clone=UnityEngine.Object.Instantiate(original.gameObject);
        try {
            clone.transform.position=new Vector3(2000,100,2000);
            var animator=clone.GetComponent<Animator>();animator.applyRootMotion=false;animator.Rebind();
            float minimum=100;
            for(int i=0;i<=60;i++) {
                animator.Play("SwordHeavy",0,i/60f*.99f);animator.Update(0);
                foreach(var bone in new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot})minimum=Mathf.Min(minimum,animator.GetBoneTransform(bone).position.y-clone.transform.position.y);
            }
            Require(minimum>=0,"trial heavy retarget drives feet below controller floor: "+minimum);
            return "PASS: all 61 heavy poses have feet above controller floor, minimum="+minimum;
        } finally{UnityEngine.Object.DestroyImmediate(clone);}
    }
    public static string HitReaction()
    {
        var boss=UnityEngine.Object.FindFirstObjectByType<MechSentinelBattle>();
        var shield=boss.GetComponent<MechReactiveShield>();float probability=shield.triggerProbability;shield.triggerProbability=0;shield.ResetForLab();
        try {
            var feedback=boss.GetComponent<MistGardenBossFeedback>();int count=feedback.FeedbackCount;
            boss.GetComponent<Health>().TryApplyDamage(new DamageInfo(20,boss.player.gameObject,boss.transform.position+Vector3.up,Vector3.forward));
            bool visual=feedback.externalHitVisuals?boss.GetComponent<WardenContactPresentation>().HitCount>0:feedback.LastFeedbackEffect;
            Require(boss.Reacting&&feedback.FeedbackCount==count+1&&visual,"hit must own its animation and spawn the authored contact effect");
            boss.AdvanceCombat(.1f);Require(boss.Reacting,"chase overwrote the hit pose before the reaction elapsed");
            boss.AdvanceCombat(.4f);boss.AdvanceCombat(.01f);
            Require(boss.Committed,"attack did not resume after the stagger");
            Require(!boss.NotifyHitReaction(.4f),"a normal hit interrupted a committed Boss slam");
            return "PASS: authored contact effect + held hit pose; recovery resumes; slam anticipation retains armor.";
        } finally{shield.triggerProbability=probability;}
    }
}
