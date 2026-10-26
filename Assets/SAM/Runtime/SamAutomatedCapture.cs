using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction
{
    public sealed class SamAutomatedCapture : MonoBehaviour
    {
        public SamSkillRunner runner;
        public Camera view;
        public SamPreviewControls controls;
        public TextAsset referenceCamera, referenceSchedule;
        public GameObject qaCharacterPrefab;
        private readonly List<string> errors = new List<string>();

        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--sam-shadow-diagnostic") >= 0)
            {
                if (controls != null) { controls.enabled = false; controls.showPanel = false; }
                yield return SamShadowDiagnostic.Run(runner, view, Argument(args,"--sam-output"));
                yield break;
            }
            if (Array.IndexOf(args, "--sam-cast-geometry") >= 0)
            {
                if (controls != null) { controls.enabled = false; controls.showPanel = false; }
                yield return SamCastGeometryCapture.Run(runner, view, referenceCamera, Argument(args,"--sam-output"));
                yield break;
            }
            if (Array.IndexOf(args, "--sam-vfx-layers") >= 0)
            {
                if (controls != null) { controls.enabled = false; controls.showPanel = false; }
                yield return SamVfxLayerCapture.Run(runner, view, referenceCamera, Argument(args,"--sam-output"));
                yield break;
            }
            if (Array.IndexOf(args, "--sam-performance") >= 0)
            {
                if (controls != null) { controls.enabled = false; controls.showPanel = false; }
                yield return SamPerformanceCapture.Run(runner, view, referenceCamera, Argument(args,"--sam-output"));
                yield break;
            }
            if (Array.IndexOf(args, "--sam-reference-capture") >= 0)
            {
                if (controls != null) { controls.enabled = false; controls.showPanel = false; }
                yield return SamReferenceCapture.Run(runner, view, referenceCamera, referenceSchedule, Argument(args, "--sam-output"));
                yield break;
            }
            if (Array.IndexOf(args, "--sam-vfx-diagnostic") >= 0)
            {
                if (controls != null) { controls.enabled = false; controls.showPanel = false; }
                yield return SamVfxCapture.Run(runner, view, Argument(args, "--sam-output"));
                yield break;
            }
            if (Array.IndexOf(args, "--sam-lighting-review") >= 0)
            {
                if (controls != null) { controls.enabled = false; controls.showPanel = false; }
                yield return SamLightingReview.Run(runner, view, Argument(args, "--sam-output"));
                yield break;
            }
            if (Array.IndexOf(args, "--sam-material-diagnostic") >= 0)
            {
                if (controls != null) { controls.enabled = false; controls.showPanel = false; }
                yield return SamMaterialDiagnostic.Run(runner, view, Argument(args, "--sam-output"), Array.IndexOf(args,"--sam-material-closeup")>=0);
                yield break;
            }
            if (Array.IndexOf(args, "--sam-qa") >= 0)
            {
                if (controls != null) { controls.enabled = false; controls.showPanel = false; }
                yield return SamRuntimeQa.Run(runner, Argument(args, "--sam-output"), qaCharacterPrefab);
                yield break;
            }
            bool continuousReference=Array.IndexOf(args,"--sam-reference-continuous")>=0;
            if (Array.IndexOf(args, "--sam-capture") < 0 && !continuousReference) yield break;
            string output = Argument(args, "--sam-output");
            if (string.IsNullOrEmpty(output)) throw new ArgumentException("--sam-output is required");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += Log;
            Time.captureFramerate = 60;
            Application.targetFrameRate = -1;
            runner.automaticTick = false;
            foreach (var technicalTarget in FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))
                technicalTarget.GetComponent<Renderer>().enabled = false;
            if (controls != null) { controls.enabled = false; controls.showPanel = false; }
            if(continuousReference)
            {
                SamReferenceCapture.ApplyCamera(view,SamReferenceCapture.ReadCamera(referenceCamera));
                FindFirstObjectByType<SamStageLayout>()?.SetReferenceVisible(true);
            }
            var orbit = view.GetComponent<SamOrbitCamera>();
            if (orbit != null) orbit.enabled = false;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = SamFrameReadback.Samples };
            target.Create();
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false, false);
            var readback = new SamFrameReadback(target);
            var report = new CaptureReport { graphicsDevice = SystemInfo.graphicsDeviceName, unity = Application.unityVersion,
                width = target.width, height = target.height, status = "integration_capture_not_art_acceptance", build_id = SamBuildIdentity.Id };
            if(continuousReference)
            {
                report.status="continuous_reference_composition_art_acceptance_pending";
                report.targetPresentation="technical collision proxy hidden; reference supporting cast and independent response visible";
            }
            var frameRecords = new List<FrameRecord>();
            var eventRecords = new List<string>();
            runner.Presented += e => eventRecords.Add(e.id + "@" + e.time.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            yield return null;
            if (!runner.TryStart(0))
            {
                errors.Add("DashPunch could not start");
            }
            else
            {
                int count = Mathf.CeilToInt((float)runner.Current.duration * 60);
                for (int index = 0; index < count; index++)
                {
                    if (index > 0) runner.Step(Math.Max(0, index / 60.0 - runner.Clock.Time), true);
                    yield return null;
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    RenderPipeline.SubmitRenderRequest(view, request);
                    readback.Read(texture);
                    string filename = $"frame_{index:D4}.png";
                    File.WriteAllBytes(Path.Combine(output, filename), texture.EncodeToPNG());
                    var hand = runner.Socket("socket_hand.R");
                    var left = runner.Socket("socket_foot.L");
                    var right = runner.Socket("socket_foot.R");
                    frameRecords.Add(new FrameRecord { index = index, time = runner.Clock.Time, file = filename,
                        actorPosition = runner.transform.position, hand = hand.position, leftFoot = left.position, rightFoot = right.position });
                    foreach (var bone in runner.GetComponentsInChildren<Transform>())
                        if (!Finite(bone.position)) errors.Add("Nonfinite transform: " + bone.name + " at " + index);
                }
                runner.Step(Math.Max(0, runner.Current.duration - runner.Clock.Time), true);
                yield return null;
                report.hitCount = runner.HitCount;
                report.completed = !runner.IsCasting;
                report.events = eventRecords.ToArray();
                if (report.hitCount != 1) errors.Add("Expected one contact with the test target; observed " + report.hitCount);
                if (eventRecords.Count != runner.Current.events.Length) errors.Add("Event count does not match the skill definition");
                runner.Cancel(true);
                yield return null;
                report.remainingEffects = runner.vfx != null ? runner.vfx.ActiveEffectCount : 0;
            }
            report.frames = frameRecords.ToArray();
            report.errors = errors.ToArray();
            report.sourceSkill = runner.Current != null ? runner.Current.skillId : "missing";
            File.WriteAllText(Path.Combine(output, "capture_report.json"), JsonUtility.ToJson(report, true));
            readback.Dispose(); target.Release();
            Destroy(target);
            Destroy(texture);
            Application.logMessageReceived -= Log;
            Time.captureFramerate = 0;
            int code = errors.Count == 0 && report.completed && report.remainingEffects == 0 ? 0 : 1;
            Debug.Log("SAM_CAPTURE_FINISHED " + output + " code=" + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        private static string Argument(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        private void Log(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition);
        }
        [Serializable] private sealed class CaptureReport
        {
            public int captureMsaaSamples = SamFrameReadback.Samples;
            public string status, graphicsDevice, unity, sourceSkill, build_id;
            public string targetPresentation = "technical collision proxy remains active; renderer hidden; reference supporting cast not present";
            public int width, height, hitCount, remainingEffects;
            public bool completed;
            public string[] events, errors;
            public FrameRecord[] frames;
        }
        [Serializable] private sealed class FrameRecord
        {
            public int index;
            public double time;
            public string file;
            public Vector3 actorPosition, hand, leftFoot, rightFoot;
        }
    }
}
