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
    public static class SamPerformanceCapture
    {
        [Serializable] private sealed class Timing
        {
            public ulong timestamp;
            public double cpuFrameMs, mainThreadMs, renderThreadMs, presentWaitMs, gpuMs;
            public double currentSkillTimeAtReadback;
            public double currentCompletedRenderWallMs;
            public uint syncInterval;
            public float widthScale, heightScale;
        }
        [Serializable] private sealed class Stats
        { public int samples; public double medianMs,p95Ms,p99Ms,maxMs; }
        [Serializable] private sealed class Report
        {
            public string status="single_actor_candidate_benchmark_not_full_production_acceptance",build_id,device,unity;
            public string renderPath="explicit_1440p_render_with_synchronous_one_pixel_GPU_readback";
            public bool timingFeatureEnabled,gpuTimingAvailable,completed,actualRenderVerified;
            public int width,height,startedReplays,completedCameraRenders,gpuReadbacksCompleted;
            public double warmupSeconds,measuredSeconds;
            public Stats cpu,gpu,completedRenderWall;
            public string[] errors;
            public Timing[] timings;
        }
        public static IEnumerator Run(SamSkillRunner runner,Camera view,TextAsset cameraAsset,string output)
        {
            Directory.CreateDirectory(output);
            var errors=new List<string>();
            void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
            Application.logMessageReceived+=Log;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Time.captureFramerate=0;
            SamReferenceCapture.ApplyCamera(view,SamReferenceCapture.ReadCamera(cameraAsset));
            UnityEngine.Object.FindFirstObjectByType<SamStageLayout>()?.SetReferenceVisible(true);
            foreach(var proxy in UnityEngine.Object.FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))proxy.GetComponent<Renderer>().enabled=false;
            var target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){name="Measured 1440p camera target",antiAliasing=4};
            target.Create();view.enabled=false;view.aspect=16f/9;
            var resolved=new RenderTexture(2560,1440,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){name="GPU completion readback resolve",antiAliasing=1};
            resolved.Create();
            runner.automaticTick=false;runner.playbackSpeed=1;runner.audioPresenter.muted=true;
            var motor=runner.GetComponent<SamCharacterMotor>();
            Vector3 origin=runner.transform.position;Quaternion rotation=runner.transform.rotation;
            var report=new Report{build_id=SamBuildIdentity.Id,device=SystemInfo.graphicsDeviceName,unity=Application.unityVersion,
                timingFeatureEnabled=FrameTimingManager.IsFeatureEnabled(),width=target.width,height=target.height,warmupSeconds=5};
            var records=new List<Timing>(10000);var timestamps=new HashSet<ulong>();var buffer=new FrameTiming[1];
            var completedTimes=new List<double>(4000);
            void Rendered(ScriptableRenderContext context,Camera renderedCamera){if(renderedCamera==view)report.completedCameraRenders++;}
            RenderPipelineManager.endCameraRendering+=Rendered;
            yield return null;
            RenderPipeline.SubmitRenderRequest(view,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            SaveImage(target,Path.Combine(output,"start_1440p.png"));
            int firstRenders=report.completedCameraRenders;
            double started=Time.realtimeSinceStartupAsDouble;
            double measuredStart=started+report.warmupSeconds;
            while(Time.realtimeSinceStartupAsDouble-started<25)
            {
                if(!runner.IsCasting)
                {
                    runner.Cancel(true);motor.Teleport(origin,rotation);
                    foreach(var proxy in UnityEngine.Object.FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))proxy.ResetTarget();
                    if(!runner.TryStart(0)){errors.Add("Benchmark skill restart failed.");break;}
                    report.startedReplays++;
                }
                runner.Step(Time.unscaledDeltaTime,true);
                // Force observable GPU completion. This is CPU-to-GPU completion latency including sync overhead,
                // not a native GPU timestamp or a presented frame-rate measurement.
                long beforeRender=System.Diagnostics.Stopwatch.GetTimestamp();
                RenderPipeline.SubmitRenderRequest(view,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                target.ResolveAntiAliasedSurface(resolved);
                var readback=AsyncGPUReadback.Request(resolved,0,0,1,0,1,0,1,TextureFormat.RGBA32);
                readback.WaitForCompletion();
                if(readback.hasError||!readback.done){errors.Add("GPU completion readback failed");break;}
                report.gpuReadbacksCompleted++;
                double completedMs=(System.Diagnostics.Stopwatch.GetTimestamp()-beforeRender)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                if(Time.realtimeSinceStartupAsDouble>=measuredStart)completedTimes.Add(completedMs);
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                if(Time.realtimeSinceStartupAsDouble>=measuredStart&&FrameTimingManager.GetLatestTimings(1,buffer)>0)
                {
                    var f=buffer[0];
                    if(f.frameStartTimestamp!=0&&timestamps.Add(f.frameStartTimestamp))records.Add(new Timing{timestamp=f.frameStartTimestamp,
                        cpuFrameMs=f.cpuFrameTime,mainThreadMs=f.cpuMainThreadFrameTime,renderThreadMs=f.cpuRenderThreadFrameTime,
                        presentWaitMs=f.cpuMainThreadPresentWaitTime,gpuMs=f.gpuFrameTime,currentSkillTimeAtReadback=runner.Clock.Time,
                        syncInterval=f.syncInterval,widthScale=f.widthScale,heightScale=f.heightScale,currentCompletedRenderWallMs=completedMs});
                }
            }
            report.measuredSeconds=Math.Max(0,Time.realtimeSinceStartupAsDouble-measuredStart);
            report.timings=records.ToArray();report.cpu=Summarize(records.Select(r=>r.cpuFrameMs));report.gpu=Summarize(records.Select(r=>r.gpuMs));
            report.gpuTimingAvailable=report.gpu.samples>0;
            report.completedRenderWall=Summarize(completedTimes);
            report.actualRenderVerified=report.completedCameraRenders>firstRenders+120&&report.gpuReadbacksCompleted>120&&view.aspect==16f/9&&target.width==2560&&target.height==1440;
            if(!report.timingFeatureEnabled||report.cpu.samples==0)errors.Add("Frame timing unavailable; do not infer performance from this run.");
            if(!report.actualRenderVerified)errors.Add("Actual 1440p camera render workload was not verified.");
            report.completed=errors.Count==0&&report.measuredSeconds>=19.5&&report.actualRenderVerified;
            SaveImage(target,Path.Combine(output,"end_1440p.png"));
            runner.Cancel(true);
            report.errors=errors.ToArray();
            File.WriteAllText(Path.Combine(output,"performance.json"),JsonUtility.ToJson(report,true));
            RenderPipelineManager.endCameraRendering-=Rendered;
            view.targetTexture=null;
            target.Release();UnityEngine.Object.Destroy(target);
            resolved.Release();UnityEngine.Object.Destroy(resolved);
            Application.logMessageReceived-=Log;
            Application.Quit(report.completed?0:1);
        }
        private static void SaveImage(RenderTexture target,string path)
        {
            var previous=RenderTexture.active;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false,false);
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=previous;
            File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.Destroy(image);
        }
        private static Stats Summarize(IEnumerable<double> values)
        {
            var sorted=values.Where(v=>v>0&&!double.IsNaN(v)&&!double.IsInfinity(v)).OrderBy(v=>v).ToArray();
            if(sorted.Length==0)return new Stats();
            double At(double fraction)=>sorted[Math.Min(sorted.Length-1,(int)Math.Ceiling(fraction*sorted.Length)-1)];
            return new Stats{samples=sorted.Length,medianMs=At(.5),p95Ms=At(.95),p99Ms=At(.99),maxMs=sorted[sorted.Length-1]};
        }
    }
}
