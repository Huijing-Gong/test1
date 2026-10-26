using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction
{
    public static class SamVfxLayerCapture
    {
        [Serializable] private sealed class Frame { public string file,layer;public double time,requestedTime;public int activeEffects,pooledEffects,isolatedRenderers;public string[] firedEvents; }
        [Serializable] private sealed class Report
        {
            public int captureMsaaSamples = SamFrameReadback.Samples;
            public string status="same_time_actor_effect_layers_not_art_acceptance",build_id;
            public bool visibility_did_not_advance_simulation;
            public Frame[] frames;public string[] errors;
        }
        public static IEnumerator Run(SamSkillRunner runner,Camera camera,TextAsset cameraAsset,string output)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--sam-texture-angles")>=0)
            {yield return SamTextureAngleCapture.Run(runner,camera,cameraAsset,output);yield break;}
            Directory.CreateDirectory(output);var errors=new List<string>();var frames=new List<Frame>();
            void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
            Application.logMessageReceived+=Log;
            runner.automaticTick=false;runner.playbackSpeed=1;runner.audioPresenter.muted=true;
            SamReferenceCapture.ApplyCamera(camera,SamReferenceCapture.ReadCamera(cameraAsset));
            UnityEngine.Object.FindFirstObjectByType<SamStageLayout>()?.SetReferenceVisible(true);
            foreach(var proxy in UnityEngine.Object.FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))proxy.GetComponent<Renderer>().enabled=false;
            var target = new RenderTexture(640,360,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB) { antiAliasing = SamFrameReadback.Samples };target.Create();
            var image=new Texture2D(640,360,TextureFormat.RGB24,false,false);
            var readback = new SamFrameReadback(target);
            bool inspectCores=Array.IndexOf(Environment.GetCommandLineArgs(),"--sam-vfx-core-layers")>=0;
            var isolatedNames=new Dictionary<string,string>{
                {"volume_only","Energy / finite flame volume"},
                {"cloud_only","Energy / open impact flame masses"},
                {"transfer_only","Energy / short fist transfer"},
                {"spray_only","Energy / unequal upper and lower impact jets"},
                {"texture_only","Energy / original thick flame lobes"},
                {"rays_only","Energy / three dimensional release rays"}
            };
            var layerNames=new List<string>{"both","punch_only","heat_only","actor_effects_off"};
            if(inspectCores)layerNames.AddRange(isolatedNames.Keys);
            var observedEvents=new List<string>();
            void OnEvent(SamTimedEvent evt){observedEvents.Add(evt.id);}
            runner.Presented+=OnEvent;
            yield return null;
            if(!runner.TryStart(0))errors.Add("Layer review skill did not start.");
            foreach(double time in new[]{0.0,.42,.92,1.0,1.08,1.17,1.33,1.67,2.92})
            {
                runner.Step(Math.Max(0,time-runner.Clock.Time),true);runner.Clock.Paused=true;
                if(Math.Abs(runner.Clock.Time-time)>1e-9)errors.Add("Layer sample missed exact requested time: "+time);
                if(!observedEvents.SequenceEqual(runner.Current.events.Where(e=>e.time<=time+1e-9).Select(e=>e.id)))
                    errors.Add("Layer sample has missing or duplicate boundary events: "+time);
                var before=runner.vfx.CaptureDiagnostics(camera);
                var ageSignature=string.Join("|",before.effects.Select(e=>e.kind+":"+e.seed+":"+e.age.ToString("R")));
                int active=runner.vfx.ActiveEffectCount,pooled=runner.vfx.PooledEffectCount;
                foreach(string layer in layerNames)
                {
                    bool isolated=isolatedNames.TryGetValue(layer,out string selectedName);
                    runner.vfx.SetArtVisibility(isolated||layer=="both"||layer=="punch_only",layer=="both"||layer=="heat_only");
                    int isolatedCount=0;
                    if(isolated)
                    {
                        isolatedCount=runner.vfx.IsolatePunchRendererForReview(selectedName);
                        bool textureExpected=runner.vfx.impactTexture!=null&&runner.vfx.impactTexture.enabled;
                        int expectedCount=layer=="rays_only"?(runner.vfx.punchEnergy.rays.enabled?1:0):layer=="texture_only"?(textureExpected?1:0):
                            layer=="spray_only"&&textureExpected&&runner.vfx.impactTexture.replaceProceduralSpray?0:1;
                        if(time>=1f&&time<=1.08f&&isolatedCount!=expectedCount)
                            errors.Add("Peak isolation expected "+expectedCount+" owned renderers: "+layer+" at "+time+", got "+isolatedCount);
                    }
                    yield return null;
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                    readback.Read(image);
                    string file=$"time_{(int)Math.Round(time*1000):D4}_{layer}.png";
                    File.WriteAllBytes(Path.Combine(output,file),image.EncodeToPNG());
                    var after=runner.vfx.CaptureDiagnostics(camera);
                    if(isolated&&after.effects.SelectMany(e=>e.renderers).Any(r=>r.enabled&&r.active&&r.name!=selectedName))
                        errors.Add("A non-target effect renderer survived isolation: "+file);
                    string afterSignature=string.Join("|",after.effects.Select(e=>e.kind+":"+e.seed+":"+e.age.ToString("R")));
                    if(afterSignature!=ageSignature||runner.vfx.ActiveEffectCount!=active||runner.vfx.PooledEffectCount!=pooled)
                        errors.Add("Visibility toggle changed simulation: "+file);
                    File.WriteAllText(Path.Combine(output,Path.ChangeExtension(file,"json")),JsonUtility.ToJson(after,true));
                    frames.Add(new Frame{file=file,layer=layer,time=runner.Clock.Time,requestedTime=time,firedEvents=observedEvents.ToArray(),activeEffects=active,pooledEffects=pooled,isolatedRenderers=isolatedCount});
                }
                runner.vfx.SetArtVisibility(true,true);runner.Clock.Paused=false;
            }
            runner.Cancel(true);
            if(runner.vfx.ActiveEffectCount!=0)errors.Add("Layer review left an active effect after cancel.");
            File.WriteAllText(Path.Combine(output,"layers.json"),JsonUtility.ToJson(new Report{build_id=SamBuildIdentity.Id,
                visibility_did_not_advance_simulation=errors.Count==0,frames=frames.ToArray(),errors=errors.ToArray()},true));
            runner.Presented-=OnEvent;
            Application.logMessageReceived-=Log;readback.Dispose(); target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);
            Application.Quit(errors.Count==0?0:1);
        }
    }
}
