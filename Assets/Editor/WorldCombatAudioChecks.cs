using System;
using System.Reflection;
using UnityEngine;

public static class WorldCombatAudioChecks
{
    static Type Type(string name){foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){var t=a.GetType(name);if(t!=null)return t;}throw new Exception("FAIL: missing "+name);}
    static void Check(bool v,string message){if(!v)throw new Exception("FAIL: "+message);}
    public static string Footfalls()
    {
        var type=Type("FootfallGate");var gate=Activator.CreateInstance(type);
        int Tick(float l,float r,float distance,float time,bool eligible=true)=>(int)type.GetMethod("Observe").Invoke(gate,new object[]{l,r,distance,time,eligible});
        Check(Tick(0,0,0,0)==0,"standing must not emit footsteps");
        Check(Tick(.12f,0,.08f,.1f)==0,"lift is not a landing");
        Check(Tick(.02f,0,.1f,.3f)==1,"descending lifted left foot must sound once");
        Check(Tick(.02f,0,.1f,.31f)==0,"planted foot must not repeat");
        Check(Tick(0,.12f,.08f,.45f)==0,"opposite foot must lift first");
        Check(Tick(0,.01f,.1f,.65f)==2,"alternating right contact must sound once");
        Check(Tick(.15f,0,0,.8f)==0&&Tick(0,0,0,1)==0,"animation moving at a blocked wall must be silent");
        Check(Tick(.15f,0,.1f,1.2f,false)==0&&Tick(0,0,.1f,1.4f,false)==0,"airborne and locked actions must be silent");
        Check(Tick(0,0,8,2)==0,"relocation must not become a burst of footsteps");
        return "PASS: alternating foot contacts; idle/wall/airborne/relocation stay silent.";
    }
    public static string SceneWiring()
    {
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var boss=UnityEngine.Object.FindFirstObjectByType<KingOfGraphic.MechBossMigration.MechSentinelBattle>();
        var playerAudio=player.GetComponent(Type("WorldPlayerAudio"));
        var bossAudio=boss.GetComponent(Type("StoneBossAudio"));
        Check(playerAudio&&bossAudio,"main player and island Boss must own selected audio");
        Check(player.GetComponent(Type("GroundedFootstepAudio"))&&boss.GetComponent(Type("GroundedFootstepAudio")),"both actors need actual-contact footsteps");
        var old=boss.GetComponent<KingOfGraphic.MechBossMigration.MistGardenBossFeedback>();
        Check(old.hitSound&&old.hitSound.name=="Grenade_Golem_Hit_01","Boss must retain its original authored hit sound");
        return "PASS: selected actor audio wired; original Boss hit clip retained.";
    }
}
