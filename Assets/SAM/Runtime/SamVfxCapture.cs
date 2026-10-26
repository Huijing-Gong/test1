using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction
{
    public static class SamVfxCapture
    {
        [Serializable] private sealed class Report
        {
            public int captureMsaaSamples = SamFrameReadback.Samples; public string status = "visibility_diagnostic_not_art_acceptance", graphicsDevice; public string[] files, errors; }
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
            foreach (var target in UnityEngine.Object.FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))
                target.GetComponent<Renderer>().enabled = false;
            var targetTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = SamFrameReadback.Samples };
            targetTexture.Create();
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false, false);
            var readback = new SamFrameReadback(targetTexture);
            var files = new List<string>();
            var cameraData = camera.GetUniversalAdditionalCameraData();
            yield return null;
            runner.TryStart(0);
            foreach (double time in new[] { .93, 1.0, 64.0/60, 1.15 })
            {
                runner.Step(time-runner.Clock.Time, true);
                foreach (SamVfxDiagnosticMode mode in Enum.GetValues(typeof(SamVfxDiagnosticMode)))
                {
                    runner.vfx.diagnosticMode = mode;
                    runner.vfx.RefreshDiagnostics();
                    cameraData.renderPostProcessing = mode == SamVfxDiagnosticMode.Final || mode == SamVfxDiagnosticMode.NoDepthFade;
                    yield return null;
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = targetTexture });
                    readback.Read(texture);
                    string stem = $"time_{(int)Math.Round(time*1000):D4}_mode_{(int)mode}";
                    File.WriteAllBytes(Path.Combine(output, stem + ".png"), texture.EncodeToPNG());
                    File.WriteAllText(Path.Combine(output, stem + ".json"), JsonUtility.ToJson(runner.vfx.CaptureDiagnostics(camera), true));
                    files.Add(stem + ".png");
                }
            }
            File.WriteAllText(Path.Combine(output, "vfx_capture.json"), JsonUtility.ToJson(new Report
                { graphicsDevice = SystemInfo.graphicsDeviceName, files = files.ToArray(), errors = errors.ToArray() }, true));
            Application.logMessageReceived -= Log;
            readback.Dispose(); targetTexture.Release();
            UnityEngine.Object.Destroy(targetTexture);
            UnityEngine.Object.Destroy(texture);
            Application.Quit(errors.Count == 0 ? 0 : 1);
        }
    }
}
