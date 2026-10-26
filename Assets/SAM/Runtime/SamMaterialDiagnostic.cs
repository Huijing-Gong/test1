using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction
{
    public static class SamMaterialDiagnostic
    {
        [Serializable] private sealed class MaterialInfo
        {
            public string renderer, material, shader; public int submesh; public uint indices; public Color color;
            public float occlusionStrength,shadeSoftness,specularStrength,heatTintStrength;
        }
        [Serializable] private sealed class CameraInfo { public float pose; public Vector3 position,target; public float fov; }
        [Serializable] private sealed class Report
        {
            public int captureMsaaSamples = SamFrameReadback.Samples;
            public string graphicsDevice,build_id,status="material_factor_diagnostic_not_art_acceptance";
            public bool closeup; public MaterialInfo[] materials; public CameraInfo[] cameras;
            public string[] files, errors,modeLabels;
        }

        public static IEnumerator Run(SamSkillRunner runner, Camera camera, string output,bool closeup=false)
        {
            Directory.CreateDirectory(output);
            var errors = new List<string>();
            void Log(string message, string stack, LogType type)
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
            Application.logMessageReceived += Log;
            runner.automaticTick = false;
            runner.audioPresenter.muted = true;
            var orbit = camera.GetComponent<SamOrbitCamera>();
            if (orbit != null) orbit.enabled = false;
            var materials = new List<Material>();
            var info = new List<MaterialInfo>();
            foreach (var renderer in runner.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var instances = renderer.materials;
                materials.AddRange(instances);
                for (int i = 0; i < instances.Length; i++)
                    info.Add(new MaterialInfo { renderer = renderer.name, material = instances[i].name,
                        shader = instances[i].shader.name, submesh = i, indices = renderer.sharedMesh.GetIndexCount(i),
                        color = instances[i].GetColor("_BaseColor"),occlusionStrength=instances[i].GetFloat("_OcclusionStrength"),
                        shadeSoftness=instances[i].GetFloat("_ShadeSoftness"),specularStrength=instances[i].GetFloat("_SpecularStrength"),
                        heatTintStrength=instances[i].GetFloat("_HeatTintStrength") });
            }
            var filenames = new List<string>();
            var cameras=new List<CameraInfo>();
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = SamFrameReadback.Samples };
            target.Create();
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false, false);
            var readback = new SamFrameReadback(target);
            var motor = runner.GetComponent<SamCharacterMotor>();
            Vector3 origin = runner.transform.position;
            Quaternion rotation = runner.transform.rotation;
            yield return null;
            foreach (float time in new[] { 0f, 1f })
            {
                runner.Cancel(true);
                motor.Teleport(origin, rotation);
                runner.TryStart(0);
                runner.Step(time, true);
                runner.vfx.StopAll(true);
                if(closeup)
                {
                    Vector3 aim=runner.Socket("head").position-Vector3.up*.30f;
                    Vector3 back=runner.transform.TransformDirection(new Vector3(.25f,.05f,1)).normalized;
                    camera.transform.SetPositionAndRotation(aim+back*2.75f,Quaternion.LookRotation(-back,Vector3.up));
                    camera.fieldOfView=45;
                    cameras.Add(new CameraInfo{pose=time,position=camera.transform.position,target=aim,fov=camera.fieldOfView});
                }
                for (int mode = 0; mode <= 13; mode++)
                {
                    for(int m=0;m<materials.Count;m++)
                    {
                        var material=materials[m];
                        material.shader = Shader.Find(mode == 9 ? "Universal Render Pipeline/Lit" : "SamProduction/CharacterToon");
                        if(mode!=9)
                        {
                            material.SetFloat("_DebugView",mode<9?mode:0);
                            material.SetFloat("_OcclusionStrength",mode==10||mode==11?0:info[m].occlusionStrength);
                            material.SetFloat("_ShadeSoftness",mode==11?.22f:info[m].shadeSoftness);
                            material.SetFloat("_SpecularStrength",info[m].specularStrength);
                            bool darkJoint=info[m].material.IndexOf("Joints",StringComparison.OrdinalIgnoreCase)>=0;
                            material.SetFloat("_HeatTintStrength",darkJoint&&mode==12?.04f:darkJoint&&mode==13?0:info[m].heatTintStrength);
                        }
                    }
                    yield return null;
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    readback.Read(texture);
                    string name = $"pose_{time:F0}_mode_{mode}.png";
                    File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                    filenames.Add(name);
                }
            }
            var report = new Report { graphicsDevice = SystemInfo.graphicsDeviceName,build_id=SamBuildIdentity.Id,closeup=closeup,
                materials = info.ToArray(),cameras=cameras.ToArray(), files = filenames.ToArray(), errors = errors.ToArray(),
                modeLabels=new[]{"production","base color","vertex normals","shading normals","NdotL","main shadow",
                    "toon plus AO without spec rim emission","constant grey","vertex normal Lambert","URP Lit comparison",
                    "production AO disabled","production AO disabled and shade softness 0.22",
                    "production with dark joint heat tint 0.04 only","production with dark joint heat tint 0 only"} };
            File.WriteAllText(Path.Combine(output, "material_diagnostic.json"), JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= Log;
            readback.Dispose(); target.Release();
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(texture);
            Application.Quit(errors.Count == 0 ? 0 : 1);
        }
    }
}
