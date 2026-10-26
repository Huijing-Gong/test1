using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction.BossArena
{
    // One actual isolated Dash execution through the saved motor, animation,
    // collision and effects. Fixed 24fps source-time capture is not native play
    // or an FPS measurement. Only the disclosed patrol feasibility gate is off.
    public sealed class ArenaReferenceDashPreview : MonoBehaviour
    {
        [Serializable] private sealed class Frame
        {
            public int frame;
            public double battle;
            public string phase;
            public float phaseTime,sourceTime,health,cameraFov;
            public Vector3 boss,player,camera;
            public Quaternion cameraRotation;
            public bool strikeOpen,released,aborted;
            public ArenaReferenceCaptureCamera.Sample reference;
        }
        [Serializable] private sealed class Report
        {
            public string scope="CONTROLLED_24FPS_ACTUAL_ISOLATED_DASH_PATROL_GATE_DISABLED_NOT_NATIVE_OR_FPS",build,error,captureScope,referenceCalibrationPath,referenceCalibrationSha256,referenceRoot;
            public bool passed;
            public Frame[] frames;
            public ArenaEventRecord[] events;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"--sam-reference-dash-preview")<0)return;
            if(args.Any(x=>x=="--sam-defense-preview"||x=="--sam-benchmark"||x.EndsWith("-review")||x.EndsWith("-contracts")))
            { Debug.LogError("Multiple simulation owners");Application.Quit(2);return; }
            new GameObject("Controlled original-source Dash reference capture").AddComponent<ArenaReferenceDashPreview>();
        }
        private IEnumerator Start()
        {
            yield return null;
            string output=null,error=null;ArenaSession arena=null;ArenaDashMotor dash=null;
            RenderTexture target=null;Texture2D image=null;ArenaReferenceCaptureCamera reference=null;
            var frames=new List<Frame>();bool started=false;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");
                if(at<0||at+1>=args.Length)throw new ArgumentException("Fresh output required");
                var requested=Path.GetFullPath(args[at+1]);
                if(Directory.Exists(requested)||File.Exists(requested))throw new IOException("Preserve previous capture");
                Directory.CreateDirectory(requested);output=requested;
                arena=FindFirstObjectByType<ArenaSession>();dash=arena.boss.DashMotor;
                arena.automaticTick=false;arena.view.automaticTick=false;arena.boss.decisionEnabled=false;
                arena.playerStart=new Vector3(arena.bossStart.x,.05f,arena.bossStart.z-5);
                arena.Restart(true);arena.boss.ResetBoss(arena.bossStart,Quaternion.Euler(0,180,0));
                dash.requirePatrolFeasibility=false;arena.player.view=null;
                arena.view.ResetCamera();arena.view.TickLook(Vector2.zero,true);
                target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);target.Create();
                image=new Texture2D(1280,720,TextureFormat.RGB24,false);
                at=Array.IndexOf(args,"--sam-reference-calibration");
                if(at<0||at+1>=args.Length)throw new ArgumentException("Dash calibration required");
                reference=new ArenaReferenceCaptureCamera(arena.view.GetComponent<Camera>(),args[at+1],"Dash",arena.boss.transform.position,arena.boss.transform.position);
            }
            catch(Exception e){error=e.ToString();}
            if(error==null)for(int i=0;i<192;i++)
            {
                try
                {
                    if(!started&&arena.BattleTime>=.5)
                    {started=arena.boss.TrySkill(ArenaAttack.DashPunch);if(!started)throw new InvalidOperationException("Actual isolated Dash start rejected");}
                    arena.SimulateElapsed(default,1d/24);arena.view.TickCamera(1f/24);
                    if(dash.Aborted)throw new InvalidOperationException("Actual Dash aborted; reference mapping must not conceal it: "+dash.Outcome);
                    var sample=reference.Apply(dash.Phase.ToString(),dash.PhaseTime,1280f/720);
                    RenderPipeline.SubmitRenderRequest(reference.Camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                    var previous=RenderTexture.active;RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();RenderTexture.active=previous;
                    File.WriteAllBytes(Path.Combine(output,"frame_"+i.ToString("D4")+".png"),image.EncodeToPNG());
                    frames.Add(new Frame{frame=i,battle=arena.BattleTime,phase=dash.Phase.ToString(),phaseTime=dash.PhaseTime,
                        sourceTime=dash.SourceTime,health=arena.player.fighter.Health,boss=arena.boss.transform.position,
                        player=arena.player.transform.position,camera=reference.Camera.transform.position,cameraRotation=reference.Camera.transform.rotation,
                        cameraFov=reference.Camera.fieldOfView,strikeOpen=dash.StrikeOpen,released=dash.Released,aborted=dash.Aborted,reference=sample});
                }
                catch(Exception e){error=e.ToString();break;}
                yield return null;
            }
            bool passed=error==null&&started&&dash.Released&&!dash.Aborted&&!dash.Active&&
                arena.Events.Count(e=>e.kind=="dash_strike_open")==1&&arena.Events.Count(e=>e.kind=="dash_recovery_complete")==1;
            if(output!=null)File.WriteAllText(Path.Combine(output,"preview_report.json"),JsonUtility.ToJson(new Report{build=Application.version,error=error,passed=passed,
                captureScope="MANUAL_REFERENCE_QA_SEED_NOT_FULL_FRAME_MATCH",referenceCalibrationPath=reference?.CalibrationPath,
                referenceCalibrationSha256=reference?.CalibrationSha256,referenceRoot=reference?.Settings.reference_root,frames=frames.ToArray(),events=arena?.Events.ToArray()},true));
            reference?.Dispose();if(target!=null){target.Release();Destroy(target);}if(image!=null)Destroy(image);
            if(error!=null)Debug.LogError(error);Debug.Log("SAM_REFERENCE_DASH "+(passed?"PASS":"FAIL"));Application.Quit(passed?0:2);
        }
    }
}
