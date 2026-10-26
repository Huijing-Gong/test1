using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
using SamProduction.BossArena;

// Opt-in real arena simulation using a body capsule contact inside the real dodge
// window. Movement, input, sword collision and damage all use production code.
[InitializeOnLoad]
public static class ResponsiveFocusChecks
{
    const string Request="Temp/responsive-focus-check.txt";
    static double started;
    static ResponsiveFocusChecks(){EditorApplication.update+=Watch;}
    static void Require(bool value,string why){if(!value)throw new Exception(why);}
    static void Watch(){
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlaying){started=0; UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/SAM/BossArena/Scenes/SAM_BossArena.unity");EditorApplication.isPlaying=true;return;}
        if(started==0){started=EditorApplication.timeSinceStartup;return;}
        if(EditorApplication.timeSinceStartup-started<2)return;
        var a=UnityEngine.Object.FindFirstObjectByType<ArenaSession>();if(!a||!a.player.Hero)return;
        File.Delete(Request);string result="";
        bool automatic=a.automaticTick,decisions=a.boss.decisionEnabled;
        Vector3 ps=a.playerStart,bs=a.bossStart;
        try{
            var gesture=new HeroDashGesture();
            gesture.Sample(true,true,false,0);Require(!gesture.Tap&&!gesture.Sprint,"Press immediately dodged/sprinted");
            gesture.Sample(false,false,true,.10f);Require(gesture.Tap&&!gesture.Sprint,"Short tap missing");
            gesture.Sample(false,false,false,.11f);Require(!gesture.Tap,"Tap repeated");
            gesture.Sample(true,true,false,1);gesture.Sample(true,false,false,1.21f);Require(gesture.Sprint&&!gesture.Tap,"Hold did not sprint");
            gesture.Sample(false,false,true,1.8f);Require(!gesture.Tap&&!gesture.Sprint,"Run release caused dodge");
            gesture.Sample(true,true,false,2,false);gesture.Sample(true,false,false,3);Require(!gesture.Tap&&!gesture.Sprint,"UI hold leaked");
            result+="PASS tap release evade, 0.20s hold sprint, no dodge on sprint release, UI suppression\n";
            var pose=a.player.Hero;
            var update=typeof(HeroPoseDriver).GetMethod("UpdateBackSword",BindingFlags.NonPublic|BindingFlags.Instance);
            pose.SetSwordCarried(true);pose.SetSwordVisible(false);update.Invoke(pose,null);
            Require(pose.BackSwordVisible,"Idle sword not carried on back");
            var back=pose.transform.Find("Back carried sword (visual only)");Require(back!=null,"Back visual missing");
            foreach(var collider in back.GetComponentsInChildren<Collider>(true))Require(!collider.enabled,"Back sword owns a hitbox");
            pose.SetSwordVisible(true);Require(pose.DrawingFromBack,"Fast draw missing");
            typeof(HeroPoseDriver).GetField("drawAge",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pose,.121f);update.Invoke(pose,null);
            Require(!pose.BackSwordVisible&&pose.sword.gameObject.activeSelf,"Draw did not transfer to original sword by 0.12s");
            pose.SetSwordCarried(false);pose.SetSwordVisible(false);update.Invoke(pose,null);Require(!pose.BackSwordVisible,"Unequipped ghost sword");
            pose.SetSwordCarried(true);
            result+="PASS visible back carry / visual-only copy / fast draw hand transfer by 0.12s / unequip hides\n";

            foreach(int hz in new[]{30,60,144}){
                var seq=new HeroFocusSequence();seq.Begin(Vector3.zero);
                for(int i=1;i<=hz/2;i++)seq.Advance(1f/hz,Vector3.forward*(4f*i/(hz/2)));
                seq.Arrive();Require(seq.AttackInput(true,false),"Held LMB rejected");
                for(int i=1;i<=6;i++)seq.ObserveStrike(true,i);
                Require(!seq.AttackInput(true,true)&&seq.ShouldReturn(true,false),"Six-strike return missing");
                Vector3 p=Vector3.forward*4;seq.Return(p);
                for(int i=0;i<hz*2&&!seq.Returned(p);i++)p+=seq.ReturnStep(p,1f/hz);
                Require(seq.Returned(p)&&p.magnitude<.13f,"Return at "+hz+"Hz: "+p);
                result+="PASS shared sequence "+hz+"Hz\n";
            }
            a.automaticTick=false;a.boss.decisionEnabled=false;
            a.playerStart=ps;a.bossStart=ps+Vector3.forward*4;
            a.Restart(true);a.boss.fighter.TrainingInvincible=true;
            a.player.transform.rotation=Quaternion.Euler(0,180,0);
            a.Simulate(new ArenaCommand{attack=true},1f/60);
            Require(Vector3.Dot(a.player.transform.forward,(a.boss.transform.position-a.player.transform.position).normalized)>.95f,"Attack did not face enemy behind player");
            result+="PASS attack aims at enemy behind player\n";
            a.Restart(true);a.boss.fighter.TrainingInvincible=true;
            a.Simulate(new ArenaCommand{dodge=true},.0833333f);
            Vector3 contact=a.player.transform.position;
            a.StrikeCapsule(a.boss.fighter,a.NewStrike(),"qa_body_contact",contact+Vector3.up*.4f,contact+Vector3.up*1.25f,.25f,10,1);
            Require(a.player.fighter.PerfectDodgeCount==1,"Actual body contact did not confirm perfect dodge");
            Require(a.player.FocusActive&&Mathf.Abs(a.player.FocusEnemyScale-.16f)<.001f,"Perfect notification did not start slow motion");
            string hits=""; Action<ArenaHit> record=hit=>hits+=a.player.HeroMotionId+"@"+a.player.HeroMotionTime.ToString("F2")+","; a.boss.fighter.Damaged+=record;
            Vector3 origin=a.player.FocusOrigin;bool ready=false,returned=false;int strikes=0;
            for(int i=0;i<1000;i++){
                a.Simulate(new ArenaCommand{attackHeld=true},1f/120);
                ready|=a.player.FocusPhase==HeroFocusPhase.Ready||a.player.FocusPhase==HeroFocusPhase.Striking;
                returned|=a.player.FocusPhase==HeroFocusPhase.Return;
                strikes=Mathf.Max(strikes,a.player.FocusStrikeCount);
                if(!a.player.FocusActive)break;
            }
            a.boss.fighter.Damaged-=record;result+="Hit motions: "+hits+"\n";
            float error=Vector3.ProjectOnPlane(a.player.transform.position-origin,Vector3.up).magnitude;
            Require(ready&&returned&&!a.player.FocusActive,"Incomplete focus stages ready="+ready+" return="+returned+" phase="+a.player.FocusPhase);
            Require(strikes==6,"Held LMB strike count "+strikes);
            Require(error<.16f,"Return error "+error);
            Require(a.boss.fighter.HitCount>0,"Actual sword never hit target");
            Require(a.player.FocusEnemyScale==1,"Slow motion leaked");
            result+="PASS arena approach / hold LMB six strikes / actual sword hits="+a.boss.fighter.HitCount+" / return error="+error+"m / speed restored\n";
            a.Restart(true);
            a.Simulate(new ArenaCommand{author=new HeroAuthorInput{roll=true}},1f/60);
            Require(a.player.HeroMotionId.StartsWith("DODGE_"),"V triggered "+a.player.HeroMotionId);
            result+="PASS V uses short evade\n";
            result="PASS\n"+result;
        }catch(Exception e){result="FAIL\n"+result+e;}
        finally{a.playerStart=ps;a.bossStart=bs;a.Restart(false);a.automaticTick=automatic;a.boss.decisionEnabled=decisions;Cursor.lockState=CursorLockMode.None;}
        Directory.CreateDirectory("LocalDocs/ResponsiveSword");CaptureBack(a);File.WriteAllText("LocalDocs/ResponsiveSword/focus-validation.txt",result);Debug.Log(result);
    }
    static void CaptureBack(ArenaSession a){
        a.player.GetComponent<ArenaCurrentCharacter>().PoseHero(.016f,Vector3.zero,false,false,true);
        var pose=a.player.Hero;pose.SetSwordVisible(false);
        typeof(HeroPoseDriver).GetMethod("UpdateBackSword",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(pose,null);
        var go=new GameObject("Temporary back sword review camera");var camera=go.AddComponent<Camera>();
        camera.transform.position=a.player.transform.position-a.player.transform.forward*2.8f+a.player.transform.right*.8f+Vector3.up*1.4f;
        camera.transform.LookAt(a.player.transform.position+Vector3.up*1.0f);camera.fieldOfView=36;camera.cullingMask=~(1<<5);
        var rt=new RenderTexture(720,900,24);var previous=RenderTexture.active;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(720,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,720,900),0,0);image.Apply();File.WriteAllBytes("LocalDocs/ResponsiveSword/back-carry.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}
        finally{RenderTexture.active=previous;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);}
    }

}
