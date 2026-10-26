using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using KingOfGraphic.Hero;
using SamProduction.BossArena;

[InitializeOnLoad]
public static class ResponsiveSprintInputChecks
{
    static ResponsiveSprintInputChecks(){EditorApplication.update+=()=>{
        if(!File.Exists("Temp/sprint-input-check.txt")||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
        var arena=UnityEngine.Object.FindFirstObjectByType<ArenaSession>();if(!arena||!arena.player.Hero)return;
        File.Delete("Temp/sprint-input-check.txt");new GameObject("Opt-in keyboard pipeline checks").AddComponent<ResponsiveSprintInputRunner>();
    };}
}
public sealed class ResponsiveSprintInputRunner:MonoBehaviour
{
    IEnumerator Start(){
        yield return new WaitForSecondsRealtime(1);
        var a=UnityEngine.Object.FindFirstObjectByType<ArenaSession>();var original=Keyboard.current;
        var keyboard=InputSystem.AddDevice<Keyboard>("Sprint check keyboard");keyboard.MakeCurrent();
        bool decisions=a.boss.decisionEnabled;a.boss.decisionEnabled=false;a.Restart(true);
        a.player.fighter.TrainingInvincible=true;
        float walk=0,run=0;string pose="",result="";bool sprint=false,dodge=false;
        try{
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));yield return new WaitForSecondsRealtime(.55f);walk=a.player.Speed;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A,Key.LeftShift));
            float end=Time.realtimeSinceStartup+1;
            while(Time.realtimeSinceStartup<end){yield return null;run=Mathf.Max(run,a.player.Speed);sprint|=HeroDashInput.Held;if(a.player.Speed>5.6f)pose=a.player.Hero.CurrentAction;dodge|=a.player.HeroMotionId?.StartsWith("DODGE_")==true;}
            result=$"walk={walk:F2}, run={run:F2}, sprint={sprint}, pose={pose}, initialDodge={dodge}";
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));yield return new WaitForSecondsRealtime(.15f);
            bool releaseDodge=a.player.HeroMotionId?.StartsWith("DODGE_")==true;
            result+=$", releaseDodge={releaseDodge}";
            if(walk<3.8f||run<5.7f||!sprint||dodge||releaseDodge||string.IsNullOrEmpty(pose))throw new Exception(result);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.4f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift));yield return new WaitForSecondsRealtime(.07f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.08f);
            if(!(a.player.HeroMotionId?.StartsWith("DODGE_")==true))throw new Exception("Short tap did not evade; "+result);
            result="PASS complete InputSystem keyboard pipeline: "+result+", shortTapDodge=True";
        }finally{
            InputSystem.RemoveDevice(keyboard);if(original!=null)original.MakeCurrent();a.boss.decisionEnabled=decisions;a.Restart(false);
            Directory.CreateDirectory("LocalDocs/ResponsiveSword");File.WriteAllText("LocalDocs/ResponsiveSword/sprint-pipeline.txt",result);Debug.Log(result);
        }
    }
}
