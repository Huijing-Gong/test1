using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
using SamProduction.BossArena;

[InitializeOnLoad]
public static class ResponsiveFocusVisualChecks
{
    static ResponsiveFocusVisualChecks(){EditorApplication.update+=()=>{
        if(!File.Exists("Temp/focus-visual-check.txt")||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlaying){UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/SAM/BossArena/Scenes/SAM_BossArena.unity");EditorApplication.isPlaying=true;return;}
        var a=Object.FindFirstObjectByType<ArenaSession>();if(!a||!a.player.Hero)return;
        File.Delete("Temp/focus-visual-check.txt");new GameObject("Opt-in Link time visual check").AddComponent<ResponsiveFocusVisualRunner>();
    };}
}
public sealed class ResponsiveFocusVisualRunner:MonoBehaviour
{
    IEnumerator Start(){
        yield return new WaitForSecondsRealtime(1);
        var a=Object.FindFirstObjectByType<ArenaSession>();bool auto=a.automaticTick,decisions=a.boss.decisionEnabled;
        string folder="LocalDocs/ResponsiveSword/FocusFX";Directory.CreateDirectory(folder);
        float returnStart=-1,returnSeconds=-1;bool returned=false;int maxStrikes=0,maxGhosts=0;string result="FAIL interrupted";
        try{
            a.automaticTick=false;a.boss.decisionEnabled=false;a.Restart(true);a.boss.fighter.TrainingInvincible=true;
            a.Simulate(new ArenaCommand{dodge=true},.0833333f);var p=a.player.transform.position;
            a.StrikeCapsule(a.boss.fighter,a.NewStrike(),"qa_actual_body_contact",p+Vector3.up*.4f,p+Vector3.up*1.25f,.25f,10,1);
            Vector3 origin=a.player.FocusOrigin;var fx=a.player.GetComponent<HeroFocusEffects>();
            if(!a.player.FocusActive||!fx||!fx.Ready)throw new System.Exception("Actual perfect dodge did not create ready VFX");
            float began=Time.realtimeSinceStartup,next=began;int frame=0;
            while(a.player.FocusActive&&Time.realtimeSinceStartup-began<8){
                a.SimulateElapsed(new ArenaCommand{attackHeld=true},Time.unscaledDeltaTime);
                if(a.player.FocusPhase==HeroFocusPhase.Return){returned=true;if(returnStart<0)returnStart=Time.realtimeSinceStartup;}
                maxStrikes=Mathf.Max(maxStrikes,a.player.FocusStrikeCount);maxGhosts=Mathf.Max(maxGhosts,fx.GhostCount);
                yield return new WaitForEndOfFrame();
                if(Time.realtimeSinceStartup>=next){ScreenCapture.CaptureScreenshot(folder+"/frame-"+(frame++).ToString("D3")+".png");next=Time.realtimeSinceStartup+.08f;}
            }
            returnSeconds=returnStart<0?-1:Time.realtimeSinceStartup-returnStart;
            float error=Vector3.ProjectOnPlane(a.player.transform.position-origin,Vector3.up).magnitude;
            yield return new WaitForSecondsRealtime(.5f);
            if(!returned||a.player.FocusActive||error>.16f||maxStrikes!=6||maxGhosts==0||fx.BurstCount<8||fx.FilterWeight>.01f)throw new System.Exception($"return={returned}, error={error}, strikes={maxStrikes}, ghosts={maxGhosts}, bursts={fx.BurstCount}, post={fx.FilterWeight}");
            result=$"PASS real perfect-dodge contact; 6 strikes; ghosts={maxGhosts}; bursts={fx.BurstCount}; return={returnSeconds:F3}s; position error={error:F3}m; filter restored={fx.FilterWeight:F2}; captured frames={frame}";
        }finally{
            File.WriteAllText(folder+"/validation.txt",result);Debug.Log(result);a.Restart(false);a.automaticTick=auto;a.boss.decisionEnabled=decisions;
        }
    }
}
