using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction.BossArena
{
    // A labelled original-speed fixed-step render of the actual source stomp,
    // capsule/ring collision and new defense. It does not measure wall FPS.
    public sealed class ArenaDefensePreview : MonoBehaviour
    {
        [Serializable] private sealed class Frame
        { public int frame; public double battle; public float health,sourceTime; public int block,perfectBlock; public string outcome; public Vector3 player,boss,camera; public int feedback; public Quaternion cameraRotation,gameplayCameraRotation; public Vector3 gameplayCamera; public float cameraFov,gameplayCameraFov; public ArenaReferenceCaptureCamera.Sample reference; }
        [Serializable] private sealed class Report
        { public string scope="CONTROLLED_24FPS_SOURCE_TIME_RENDER_ACTUAL_STOMP_AND_DEFENSE_NOT_NATIVE_OR_PERFORMANCE",build,unity,error; public bool passed; public Frame[] frames; public ArenaEventRecord[] events; public string captureScope,referenceCalibrationPath,referenceCalibrationSha256,referenceRoot; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--sam-defense-preview")>=0)
                new GameObject("Controlled actual stomp defense preview").AddComponent<ArenaDefensePreview>();
        }
        private IEnumerator Start()
        {
            yield return null;
            var rows=new List<Frame>();string output=null,error=null;
            ArenaSession arena=null;RenderTexture target=null;Texture2D image=null;
            ArenaReferenceCaptureCamera referenceCapture=null;
            bool started=false,pressed=false;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");
                if(at<0)throw new InvalidOperationException("Output required");
                var requestedOutput=Path.GetFullPath(args[at+1]);if(Directory.Exists(requestedOutput)||File.Exists(requestedOutput))throw new IOException("Preserve previous preview");Directory.CreateDirectory(requestedOutput);output=requestedOutput;
                arena=FindFirstObjectByType<ArenaSession>();arena.automaticTick=false;arena.view.automaticTick=false;
                arena.Restart(true);arena.boss.decisionEnabled=false;
                arena.player.ResetPlayer(new Vector3(0,.05f,1.5f),Quaternion.identity,false);
                arena.player.guard.ResetGuard(false);arena.view.ResetCamera();arena.view.TickLook(Vector2.zero,true);
                if(Array.IndexOf(args,"--sam-no-foot-impact")>=0)
                    arena.boss.GetComponent<ArenaStompMotor>().emitFootImpact=false;
                target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);target.Create();
                image=new Texture2D(1280,720,TextureFormat.RGB24,false);
                if(Array.IndexOf(args,"--sam-reference-capture")>=0)
                {
                    int calibrationAt=Array.IndexOf(args,"--sam-reference-calibration");
                    if(calibrationAt<0||calibrationAt+1>=args.Length)throw new InvalidOperationException("Reference calibration JSON required");
                    var motor=arena.boss.GetComponent<ArenaStompMotor>();
                    var contact=arena.boss.transform.TransformPoint(motor.localStampContact);contact.y=arena.boss.transform.position.y;
                    referenceCapture=new ArenaReferenceCaptureCamera(arena.view.GetComponent<Camera>(),args[calibrationAt+1],"Stomp",arena.boss.transform.position,contact);
                }
            }
            catch(Exception e){error=e.ToString();}
            if(error==null)
            {
                for(int i=0;i<144;i++)
                {
                    try
                    {
                        if(!started&&arena.BattleTime>=.5)
                        {started=arena.boss.TrySkill(ArenaAttack.Stomp);if(!started)throw new InvalidOperationException("Actual source stomp rejected");}
                        var stomp=arena.boss.GetComponent<ArenaStompMotor>();
                        bool want=started&&stomp.SourceTime>=2.38f;
                        // Press shortly before the wave reaches the actual capsule,
                        // not merely before an animation/telegraph event.
                        ArenaCommand command=new ArenaCommand{guardHeld=want,guardPressed=want&&!pressed};
                        if(want)pressed=true;
                        arena.SimulateElapsed(command,1d/24);arena.view.TickCamera(1f/24);
                        var gameCamera=arena.view.GetComponent<Camera>();var camera=gameCamera;
                        ArenaReferenceCaptureCamera.Sample reference=null;
                        if(referenceCapture!=null)
                        { reference=referenceCapture.Apply("Stomp",stomp.SourceTime,1280f/720);camera=referenceCapture.Camera; }
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                        var previous=RenderTexture.active;RenderTexture.active=target;
                        image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();RenderTexture.active=previous;
                        File.WriteAllBytes(Path.Combine(output,"frame_"+i.ToString("D4")+".png"),image.EncodeToPNG());
                        rows.Add(new Frame{frame=i,battle=arena.BattleTime,health=arena.player.fighter.Health,sourceTime=stomp.SourceTime,
                            block=arena.player.fighter.BlockCount,perfectBlock=arena.player.fighter.PerfectBlockCount,
                            outcome=arena.player.fighter.LastDefense.outcome.ToString(),player=arena.player.transform.position,boss=arena.boss.transform.position,
                            camera=camera.transform.position,cameraRotation=camera.transform.rotation,cameraFov=camera.fieldOfView,gameplayCamera=gameCamera.transform.position,gameplayCameraRotation=gameCamera.transform.rotation,gameplayCameraFov=gameCamera.fieldOfView,reference=reference,feedback=arena.defenseFeedback.ActiveFeedbackCount});
                    }
                    catch(Exception e){error=e.ToString();break;}
                    yield return null;
                }
            }
            bool passed=error==null&&started&&arena.player.fighter.BlockCount==1&&arena.player.fighter.PerfectBlockCount==1&&!arena.boss.Attacking;
            if(output!=null)File.WriteAllText(Path.Combine(output,"preview_report.json"),JsonUtility.ToJson(new Report{unity=Application.unityVersion,build=Application.version,error=error,passed=passed,captureScope=referenceCapture==null?"GAMEPLAY_CAMERA":"MANUAL_REFERENCE_QA_SEED_NOT_FULL_FRAME_MATCH",referenceCalibrationPath=referenceCapture?.CalibrationPath,referenceCalibrationSha256=referenceCapture?.CalibrationSha256,referenceRoot=referenceCapture?.Settings.reference_root,frames=rows.ToArray(),events=arena!=null?arena.Events.ToArray():null},true));
            referenceCapture?.Dispose();
            if(target!=null){target.Release();Destroy(target);}if(image!=null)Destroy(image);
            if(error!=null)Debug.LogError(error);Debug.Log("SAM_DEFENSE_PREVIEW "+(passed?"PASS":"FAIL"));
            Application.Quit(passed?0:2);
        }
    }
}
