using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction
{
    public static class SamShadowDiagnostic
    {
        [Serializable] private sealed class Variant
        {
            public string name; public int resolution, cascades;
            public float depthBias, normalBias; public bool pipelineBias, disabled;
        }
        [Serializable] private sealed class Frame
        {
            public string file, view, variant; public float time; public int debugMode;
            public Vector3 cameraPosition, cameraTarget; public float cameraFov;
        }
        [Serializable] private sealed class Report
        {
            public int captureMsaaSamples = SamFrameReadback.Samples;
            public string build_id, status="shadow_factor_comparison_not_art_acceptance", graphicsDevice;
            public float distance; public Vector3 fourCascadeSplits;
            public Variant[] variants; public Frame[] frames; public string[] errors;
        }
        public static IEnumerator Run(SamSkillRunner runner, Camera camera, string output)
        {
            Directory.CreateDirectory(output);
            var errors = new List<string>();
            void Log(string message, string stack, LogType type)
            { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors.Add(message); }
            Application.logMessageReceived += Log;
            runner.automaticTick=false; runner.audioPresenter.muted=true;
            var orbit=camera.GetComponent<SamOrbitCamera>(); if(orbit!=null)orbit.enabled=false;
            var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var key=GameObject.Find("Key light").GetComponent<Light>();
            var lightData=key.GetUniversalAdditionalLightData();
            int oldResolution=pipeline.mainLightShadowmapResolution, oldCascades=pipeline.shadowCascadeCount;
            Vector3 oldSplits=pipeline.cascade4Split;
            bool oldPipelineBias=lightData.usePipelineSettings;
            float oldDepth=key.shadowBias,oldNormal=key.shadowNormalBias;
            LightShadows oldShadows=key.shadows;
            var variants=new[]{
                new Variant{name="baseline",resolution=oldResolution,cascades=oldCascades,pipelineBias=oldPipelineBias,depthBias=oldPipelineBias?pipeline.shadowDepthBias:oldDepth,normalBias=oldPipelineBias?pipeline.shadowNormalBias:oldNormal},
                new Variant{name="legacy2048",resolution=2048,cascades=2,pipelineBias=true,depthBias=pipeline.shadowDepthBias,normalBias=pipeline.shadowNormalBias},
                new Variant{name="resolution4096",resolution=4096,cascades=2,pipelineBias=true,depthBias=pipeline.shadowDepthBias,normalBias=pipeline.shadowNormalBias},
                new Variant{name="four_cascades",resolution=4096,cascades=4,pipelineBias=true,depthBias=pipeline.shadowDepthBias,normalBias=pipeline.shadowNormalBias},
                new Variant{name="custom005_012",resolution=4096,cascades=4,depthBias=.05f,normalBias=.12f},
                new Variant{name="custom015_025",resolution=4096,cascades=4,depthBias=.15f,normalBias=.25f},
                new Variant{name="custom050_050",resolution=4096,cascades=4,depthBias=.5f,normalBias=.5f},
                new Variant{name="shadows_disabled",resolution=4096,cascades=4,pipelineBias=true,disabled=true}
            };
            var materials=new List<Material>();
            foreach(var renderer in runner.GetComponentsInChildren<SkinnedMeshRenderer>())materials.AddRange(renderer.materials);
            var target = new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB) { antiAliasing = SamFrameReadback.Samples };
            target.Create();
            var texture=new Texture2D(1280,720,TextureFormat.RGB24,false,false);
            var readback = new SamFrameReadback(target);
            var frames=new List<Frame>();
            var motor=runner.GetComponent<SamCharacterMotor>();
            Vector3 origin=runner.transform.position; Quaternion rotation=runner.transform.rotation;
            Vector3 splits=new Vector3(.12f,.30f,.60f);
            yield return null;
            foreach(float time in new[]{0f,1f})
            {
                runner.Cancel(true); motor.Teleport(origin,rotation); runner.TryStart(0); runner.Step(time,true); runner.vfx.StopAll(true);
                foreach(bool closeup in new[]{true,false})
                {
                    string view=closeup?"closeup":"full_body";
                    Vector3 aim=closeup?runner.Socket("head").position-Vector3.up*.30f:runner.transform.position+Vector3.up*1.6f;
                    Vector3 back=runner.transform.TransformDirection(new Vector3(.25f,.05f,1)).normalized;
                    camera.transform.SetPositionAndRotation(aim+back*(closeup?2.75f:6.4f),Quaternion.LookRotation(-back,Vector3.up));
                    camera.fieldOfView=closeup?45:38;
                    foreach(var variant in variants)
                    {
                        pipeline.mainLightShadowmapResolution=variant.resolution;
                        pipeline.shadowCascadeCount=variant.cascades;
                        pipeline.cascade4Split=splits;
                        lightData.usePipelineSettings=variant.pipelineBias;
                        key.shadowBias=variant.depthBias; key.shadowNormalBias=variant.normalBias;
                        key.shadows=variant.disabled?LightShadows.None:LightShadows.Soft;
                        foreach(int mode in new[]{0,5})
                        {
                            foreach(var material in materials)material.SetFloat("_DebugView",mode);
                            yield return null;
                            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                            readback.Read(texture);
                            string file=$"pose_{time:F0}_{view}_{variant.name}_mode_{mode}.png";
                            File.WriteAllBytes(Path.Combine(output,file),texture.EncodeToPNG());
                            frames.Add(new Frame{file=file,view=view,variant=variant.name,time=time,debugMode=mode,cameraPosition=camera.transform.position,cameraTarget=aim,cameraFov=camera.fieldOfView});
                        }
                    }
                }
            }
            pipeline.mainLightShadowmapResolution=oldResolution;pipeline.shadowCascadeCount=oldCascades;pipeline.cascade4Split=oldSplits;
            lightData.usePipelineSettings=oldPipelineBias;key.shadowBias=oldDepth;key.shadowNormalBias=oldNormal;key.shadows=oldShadows;
            foreach(var material in materials)material.SetFloat("_DebugView",0);
            File.WriteAllText(Path.Combine(output,"shadow_diagnostic.json"),JsonUtility.ToJson(new Report{
                build_id=SamBuildIdentity.Id,graphicsDevice=SystemInfo.graphicsDeviceName,distance=pipeline.shadowDistance,
                fourCascadeSplits=splits,variants=variants,frames=frames.ToArray(),errors=errors.ToArray()},true));
            Application.logMessageReceived-=Log;readback.Dispose(); target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(texture);
            Application.Quit(errors.Count==0?0:1);
        }
    }
}
