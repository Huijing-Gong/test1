using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using KingOfGraphic.Hero;
using SamProduction.BossArena;

[InitializeOnLoad]
public static class ResponsiveSprintProbe
{
    static ResponsiveSprintProbe(){EditorApplication.update+=()=>{
        if(EditorApplication.isPlaying&&File.Exists("Temp/sprint-probe.txt")&&!Object.FindFirstObjectByType<ResponsiveSprintObserver>())new GameObject("Opt-in runtime sprint observer").AddComponent<ResponsiveSprintObserver>();
    };}
}
public sealed class ResponsiveSprintObserver:MonoBehaviour
{
    float next;string previous;
    void LateUpdate(){
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+.08f;
        var a=Object.FindFirstObjectByType<ArenaSession>();if(!a)return;
        var k=Keyboard.current;var input=HeroCombatInput.Read(true);
        string row=$"raw={k?.leftShiftKey.isPressed}/{k?.rightShiftKey.isPressed} edge={k?.leftShiftKey.wasPressedThisFrame} focus={Application.isFocused} lock={Cursor.lockState} sprint={HeroDashInput.Held} binding={WorldInputBindings.Held("Sprint")} intent={input.run} W={k?.wKey.isPressed} speed={a.player.Speed:F2} pose={a.player.Hero.CurrentAction} state={a.State}";
        if(row==previous)return;previous=row;Directory.CreateDirectory("LocalDocs/ResponsiveSword");File.AppendAllText("LocalDocs/ResponsiveSword/sprint-runtime.txt",Time.unscaledTime.ToString("F2")+" "+row+"\n");
    }
}
