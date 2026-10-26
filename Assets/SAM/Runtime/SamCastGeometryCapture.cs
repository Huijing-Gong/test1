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
    public static class SamCastGeometryCapture
    {
        [Serializable] sealed class Surface
        {
            public string name; public Vector3[] assetLocalVertices;
            public bool skinned,bakeScaleCompensation;
            public Vector3 rendererLossyScale,bakeFalseMin,bakeFalseMax,bakeTrueMin,bakeTrueMax;
            public Vector3 rendererWorldBoundsMin,rendererWorldBoundsMax;
            public Matrix4x4 rendererToAsset;
        }
        [Serializable] sealed class Node { public string name; public Matrix4x4 assetLocalMatrix; }
        [Serializable] sealed class Sample { public int time_ms; public double runnerTime,castTime; public Surface[] surfaces; public Node[] nodes; public string png; }
        [Serializable] sealed class Report
        {
            public int captureMsaaSamples = SamFrameReadback.Samples;
            public string build_id,status="actual_Unity_source_comparison_pending";
            public bool completed;
            public string[] errors; public Sample[] samples;
        }
        public static IEnumerator Run(SamSkillRunner runner,Camera camera,TextAsset cameraAsset,string output)
        {
            Directory.CreateDirectory(output);
            var errors=new List<string>();var samples=new List<Sample>();
            void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
            Application.logMessageReceived+=Log;
            runner.automaticTick=false;runner.playbackSpeed=1;runner.audioPresenter.muted=true;
            SamReferenceCapture.ApplyCamera(camera,SamReferenceCapture.ReadCamera(cameraAsset));
            UnityEngine.Object.FindFirstObjectByType<SamStageLayout>()?.SetReferenceVisible(true);
            foreach(var proxy in UnityEngine.Object.FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))proxy.GetComponent<Renderer>().enabled=false;
            var target = new RenderTexture(640,360,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB) { antiAliasing = SamFrameReadback.Samples };target.Create();
            var image=new Texture2D(640,360,TextureFormat.RGB24,false,false);
            var readback = new SamFrameReadback(target);
            var baked=new Mesh();
            yield return null;
            var cast=UnityEngine.Object.FindFirstObjectByType<SamCastReaction>();
            if(cast==null||!runner.TryStart(0))errors.Add("Cast or skill unavailable");
            else foreach(int ms in new[]{0,830,920,1080,1330,3010})
            {
                runner.Step(Math.Max(0,ms/1000.0-runner.Clock.Time),true);
                yield return null;
                var surfaces=new List<Surface>();
                foreach(var renderer in cast.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh;
                    var surface=new Surface{name=renderer.name,rendererLossyScale=renderer.transform.lossyScale,
                        rendererWorldBoundsMin=renderer.bounds.min,rendererWorldBoundsMax=renderer.bounds.max};
                    if(renderer is SkinnedMeshRenderer skin)
                    {
                        skin.BakeMesh(baked,false);baked.RecalculateBounds();
                        surface.bakeFalseMin=baked.bounds.min;surface.bakeFalseMax=baked.bounds.max;
                        // Unity6.3: true compensates the renderer scale before applying its local-to-world matrix.
                        skin.BakeMesh(baked,true);baked.RecalculateBounds();
                        surface.bakeTrueMin=baked.bounds.min;surface.bakeTrueMax=baked.bounds.max;
                        surface.skinned=true;surface.bakeScaleCompensation=true;mesh=baked;
                    }
                    else {var filter=renderer.GetComponent<MeshFilter>();if(filter==null)continue;mesh=filter.sharedMesh;}
                    Matrix4x4 local=cast.transform.worldToLocalMatrix*renderer.localToWorldMatrix;
                    surface.rendererToAsset=local;
                    surface.assetLocalVertices=mesh.vertices.Select(v=>local.MultiplyPoint3x4(v)).ToArray();
                    if(surface.assetLocalVertices.Length==0)errors.Add("Mesh has no accessible geometry: "+renderer.name);
                    surfaces.Add(surface);
                }
                var nodes=cast.GetComponentsInChildren<Transform>(true).Select(n=>new Node{name=n.name,
                    assetLocalMatrix=cast.transform.worldToLocalMatrix*n.localToWorldMatrix}).ToArray();
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                readback.Read(image);
                string file=$"time_{ms:D4}.png";File.WriteAllBytes(Path.Combine(output,file),image.EncodeToPNG());
                samples.Add(new Sample{time_ms=ms,runnerTime=runner.Clock.Time,castTime=cast.SampledTime,surfaces=surfaces.ToArray(),nodes=nodes,png=file});
                if(Math.Abs(cast.SampledTime-ms/1000.0)>1e-5)errors.Add("Cast sample time mismatch "+ms);
            }
            runner.Cancel(true);
            var report=new Report{build_id=SamBuildIdentity.Id,completed=samples.Count==6&&errors.Count==0,errors=errors.ToArray(),samples=samples.ToArray()};
            File.WriteAllText(Path.Combine(output,"cast_geometry.json"),JsonUtility.ToJson(report,false));
            Application.logMessageReceived-=Log;readback.Dispose(); target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(baked);
            Application.Quit(report.completed?0:1);
        }
    }
}
