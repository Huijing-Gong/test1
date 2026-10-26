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
    /// <summary>Reproducible actual-GPU look development views. Review status stays pending until inspected.</summary>
    public static class SamLightingReview
    {
        [Serializable] private sealed class Record
        { public string file, lighting, view; public float pose; public Vector3 cameraPosition, headViewport, geometryCenter; }
        [Serializable] private sealed class Report
        {
            public int captureMsaaSamples = SamFrameReadback.Samples;
            public string status = "rendered_for_manual_material_review_not_accepted";
            public string graphicsDevice, build_id;
            public Record[] frames;
            public string[] errors;
        }
        public static IEnumerator Run(SamSkillRunner runner, Camera camera, string output)
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
            foreach (var targetObject in UnityEngine.Object.FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))
            {
                targetObject.GetComponent<Renderer>().enabled = false;
                targetObject.GetComponent<Collider>().enabled = false;
            }
            foreach (var existing in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) existing.enabled = false;
            var key = new GameObject("Look review key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.shadows = LightShadows.Soft;
            key.GetUniversalAdditionalLightData().usePipelineSettings = false;
            key.shadowBias = .5f;
            key.shadowNormalBias = .5f;
            var fill = new GameObject("Look review fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.LookRotation(new Vector3(.6f, -.2f, .4f));
            RenderSettings.ambientMode = AmbientMode.Trilight;
            var buffer = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = SamFrameReadback.Samples };
            buffer.Create();
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false, false);
            var readback = new SamFrameReadback(buffer);
            var records = new List<Record>();
            var motor = runner.GetComponent<SamCharacterMotor>();
            Vector3 origin = runner.transform.position;
            Quaternion rotation = runner.transform.rotation;
            var head = runner.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head");
            if (head == null) throw new InvalidOperationException("Lighting review requires the actual head bone.");
            camera.fieldOfView = 34;
            yield return null;
            foreach (string rig in new[] { "neutral", "cool", "fire" })
            {
                key.transform.rotation = Quaternion.LookRotation(new Vector3(.35f, -.6f, -.75f));
                key.color = rig == "fire" ? new Color(1f, .39f, .10f) : rig == "cool" ? new Color(.56f, .72f, 1f) : Color.white;
                key.intensity = rig == "neutral" ? 1.1f : 1.65f;
                fill.color = rig == "fire" ? new Color(.25f, .40f, .74f) : new Color(.76f, .80f, .91f);
                fill.intensity = .26f;
                RenderSettings.ambientSkyColor = rig == "neutral" ? new Color(.22f, .22f, .22f) : new Color(.12f, .15f, .24f);
                RenderSettings.ambientEquatorColor = new Color(.12f, .12f, .14f);
                RenderSettings.ambientGroundColor = new Color(.055f, .05f, .045f);
                foreach (float time in new[] { 0f, 1f })
                {
                    runner.Cancel(true);
                    motor.Teleport(origin, rotation);
                    runner.TryStart(0);
                    runner.Step(time, true);
                    runner.vfx.StopAll(true);
                    var points = new List<Vector3>();
                    foreach (var renderer in runner.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var mesh = new Mesh();
                        renderer.BakeMesh(mesh, true);
                        foreach(var vertex in mesh.vertices) points.Add(renderer.transform.TransformPoint(vertex));
                        UnityEngine.Object.Destroy(mesh);
                    }
                    if(points.Count==0)throw new InvalidOperationException("Lighting review has no evaluated skin geometry.");
                    var bounds = new Bounds(points[0],Vector3.zero);
                    foreach(var point in points)bounds.Encapsulate(point);
                    Vector3 center = bounds.center;
                    foreach (string angle in new[] { "front", "quarter", "back", "head" })
                    {
                        Vector3 back = runner.transform.TransformDirection(angle=="quarter" ? new Vector3(.56f,.13f,.83f) :
                            angle=="back" ? new Vector3(0,.025f,-1) : new Vector3(.05f,.025f,1)).normalized;
                        Vector3 aim = angle=="head" ? head.position-Vector3.up*.12f : center;
                        Quaternion viewRotation=Quaternion.LookRotation(-back,Vector3.up);
                        Vector3 right=viewRotation*Vector3.right,up=viewRotation*Vector3.up;
                        float tanV=Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f),tanH=tanV*16f/9;
                        float distance=angle=="head" ? 2.35f : 1f;
                        if(angle!="head")foreach(var point in points)
                        {
                            Vector3 q=point-aim;
                            distance=Mathf.Max(distance,Vector3.Dot(q,back)+Mathf.Max(Mathf.Abs(Vector3.Dot(q,right))/(tanH*.9f),Mathf.Abs(Vector3.Dot(q,up))/(tanV*.9f)));
                        }
                        camera.transform.SetPositionAndRotation(aim+back*distance,viewRotation);
                        yield return null;
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = buffer });
                        readback.Read(texture);
                        string file = $"{rig}_pose_{time:F0}_{angle}.png";
                        File.WriteAllBytes(Path.Combine(output, file), texture.EncodeToPNG());
                        Vector3 headViewport=camera.WorldToViewportPoint(head.position);
                        if(headViewport.z<=0||headViewport.x<.05f||headViewport.x>.95f||headViewport.y<.05f||headViewport.y>.95f)
                            errors.Add("Head anchor outside material review frame: "+file);
                        records.Add(new Record { file = file, lighting = rig, view = angle, pose = time, cameraPosition = camera.transform.position,
                            headViewport=headViewport,geometryCenter=center });
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "lighting_review.json"), JsonUtility.ToJson(new Report
                { graphicsDevice = SystemInfo.graphicsDeviceName, build_id=SamBuildIdentity.Id, frames = records.ToArray(), errors = errors.ToArray() }, true));
            Application.logMessageReceived -= Log;
            readback.Dispose(); buffer.Release();
            UnityEngine.Object.Destroy(buffer);
            UnityEngine.Object.Destroy(texture);
            Application.Quit(errors.Count == 0 ? 0 : 1);
        }
    }
}
