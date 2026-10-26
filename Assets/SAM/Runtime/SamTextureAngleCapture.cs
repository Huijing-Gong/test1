using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction
{
    public static class SamTextureAngleCapture
    {
        [Serializable] class Frame
        {
            public string file,sha256,view,layer;public double time;
            public Vector3 cameraPosition,cameraForward;public int isolatedRenderers;
        }
        [Serializable] class Report
        {
            public string build_id,status="actual_multiview_texture_capture_art_review_pending";
            public bool stable;public int captureMsaaSamples=SamFrameReadback.Samples;
            public Frame[] frames;public string[] errors;
        }
        public static IEnumerator Run(SamSkillRunner runner,Camera camera,TextAsset cameraAsset,string output)
        {
            Directory.CreateDirectory(output);var frames=new List<Frame>();var errors=new List<string>();
            void Log(string m,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);}
            Application.logMessageReceived+=Log;
            runner.automaticTick=false;runner.playbackSpeed=1;runner.audioPresenter.muted=true;
            var orbit=camera.GetComponent<SamOrbitCamera>();if(orbit!=null)orbit.enabled=false;
            SamReferenceCapture.ApplyCamera(camera,SamReferenceCapture.ReadCamera(cameraAsset));
            Vector3 refPosition=camera.transform.position;Quaternion refRotation=camera.transform.rotation;
            UnityEngine.Object.FindFirstObjectByType<SamStageLayout>()?.SetReferenceVisible(true);
            foreach(var proxy in UnityEngine.Object.FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))proxy.GetComponent<Renderer>().enabled=false;
            var target=new RenderTexture(640,360,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=SamFrameReadback.Samples};target.Create();
            var image=new Texture2D(640,360,TextureFormat.RGB24,false,false);var readback=new SamFrameReadback(target);
            yield return null;
            if(!runner.TryStart(0))errors.Add("Multi-view skill did not start");
            Vector3 release=Vector3.zero,direction=Vector3.forward;
            foreach(double time in new[]{.92,1.0,1.17})
            {
                runner.Step(Math.Max(0,time-runner.Clock.Time),true);runner.Clock.Paused=true;
                if(Math.Abs(runner.Clock.Time-time)>1e-9)errors.Add("Missed exact multi-view time");
                if(time==.92){release=runner.Socket("socket_hand.R").position;direction=runner.HitDirection("socket_hand.R");}
                Vector3 center=release+direction*runner.vfx.punchEnergy.impactDistance+runner.vfx.punchEnergy.impactWorldOffset;
                var before=runner.vfx.CaptureDiagnostics(camera);
                string Signature()=>string.Join("|",runner.vfx.CaptureDiagnostics(camera).effects.Select(e=>e.kind+":"+e.seed+":"+e.age.ToString("R")));
                string signature=Signature();int active=runner.vfx.ActiveEffectCount,pool=runner.vfx.PooledEffectCount;
                foreach(string view in new[]{"reference","side","axial_minus8","axial_minus2","axial_minus1","axial","axial_plus1","axial_plus2","axial_plus8","reverse","elevated"})
                {
                    if(view=="reference")camera.transform.SetPositionAndRotation(refPosition,refRotation);
                    else
                    {
                        Vector3 ray=view=="side"?Vector3.Cross(Vector3.up,direction).normalized:
                            view=="reverse"?-direction: view=="elevated"?(direction-Vector3.up*.8f).normalized:
                            Quaternion.AngleAxis(view=="axial_minus8"?-8:view=="axial_plus8"?8:
                                view=="axial_minus2"?-2:view=="axial_plus2"?2:view=="axial_minus1"?-1:view=="axial_plus1"?1:0,Vector3.up)*direction;
                        camera.transform.position=center-ray*5;
                        camera.transform.rotation=Quaternion.LookRotation(center-camera.transform.position,Vector3.up);
                    }
                    foreach(string layer in new[]{"combined","texture_only"})
                    {
                        runner.vfx.SetArtVisibility(true,layer=="combined");
                        int isolated=layer=="texture_only"?runner.vfx.IsolatePunchRendererForReview("Energy / original thick flame lobes"):0;
                        if(layer=="texture_only"&&time==1.0&&isolated!=1)errors.Add("Missing texture in peak multi-view");
                        yield return null;
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});readback.Read(image);
                        string file=$"time_{(int)Math.Round(time*1000):D4}_{view}_{layer}.png";byte[] png=image.EncodeToPNG();
                        File.WriteAllBytes(Path.Combine(output,file),png);
                        using(var hash=SHA256.Create())frames.Add(new Frame{file=file,sha256=BitConverter.ToString(hash.ComputeHash(png)).Replace("-","").ToLowerInvariant(),
                            time=runner.Clock.Time,view=view,layer=layer,cameraPosition=camera.transform.position,cameraForward=camera.transform.forward,isolatedRenderers=isolated});
                        if(Signature()!=signature||runner.vfx.ActiveEffectCount!=active||runner.vfx.PooledEffectCount!=pool)
                            errors.Add("Changing review camera advanced effect state: "+file);
                    }
                }
                runner.vfx.SetArtVisibility(true,true);runner.Clock.Paused=false;
            }
            runner.Cancel(true);if(runner.vfx.ActiveEffectCount!=0)errors.Add("Multi-view cancel retained effects");
            File.WriteAllText(Path.Combine(output,"texture_angles.json"),JsonUtility.ToJson(new Report{build_id=SamBuildIdentity.Id,stable=errors.Count==0,frames=frames.ToArray(),errors=errors.ToArray()},true));
            Application.logMessageReceived-=Log;readback.Dispose();target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);
            Application.Quit(errors.Count==0?0:1);
        }
    }
}
