using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction
{
    /// <summary>Evaluates the editable runtime at the original GIF sample times. No reference image enters rendering.</summary>
    public static class SamReferenceCapture
    {
        [Serializable] public sealed class CameraContract
        {
            public float[] unity_position_m, unity_forward, unity_up;
            public float vertical_fov_degrees, aspect_ratio, duration_s;
            public bool camera_animation;
        }
        [Serializable] public sealed class Sample
        {
            public int index, start_ms, duration_ms;
            public string sample_id, reference_sha256;
        }
        [Serializable] public sealed class Schedule
        {
            public string clip_id, skill_id, reference_index_sha256;
            public int width, height, duration_ms;
            public Sample[] frames;
        }
        [Serializable] private sealed class RenderedSample
        {
            public int index, start_ms, duration_ms;
            public double evaluated_time_s;
            public string file, sha256, reference_sha256;
        }
        [Serializable] private sealed class Report
        {
            public int captureMsaaSamples = SamFrameReadback.Samples;
            public string status = "reference_timing_capture_art_acceptance_pending";
            public string unity, graphicsDevice, skill_id, schedule_sha256, camera_sha256, build_id;
            public bool completed, reference_images_used_in_render = false;
            public int width, height, duration_ms;
            public string[] errors;
            public RenderedSample[] frames;
        }

        public static CameraContract ReadCamera(TextAsset asset)
        {
            if (asset == null) throw new InvalidOperationException("Verified camera contract has not been staged.");
            var data = JsonUtility.FromJson<CameraContract>(asset.text);
            Vector3 forward = Vector(data.unity_forward), up = Vector(data.unity_up);
            Vector(data.unity_position_m);
            if (data.camera_animation || !Finite(data.vertical_fov_degrees) || data.vertical_fov_degrees <= 1 ||
                data.vertical_fov_degrees >= 170 || Mathf.Abs(data.aspect_ratio - 16f / 9) > .0001f ||
                Mathf.Abs(forward.magnitude - 1) > .001f || Mathf.Abs(up.magnitude - 1) > .001f ||
                Mathf.Abs(Vector3.Dot(forward, up)) > .001f)
                throw new InvalidOperationException("Invalid fixed reference camera contract.");
            return data;
        }

        public static void ApplyCamera(Camera view, CameraContract camera)
        {
            var orbit = view.GetComponent<SamOrbitCamera>();
            if (orbit != null) orbit.enabled = false;
            view.transform.SetPositionAndRotation(Vector(camera.unity_position_m),
                Quaternion.LookRotation(Vector(camera.unity_forward), Vector(camera.unity_up)));
            view.fieldOfView = camera.vertical_fov_degrees;
            view.aspect = camera.aspect_ratio;
        }

        public static IEnumerator Run(SamSkillRunner runner, Camera view, TextAsset cameraAsset,
            TextAsset scheduleAsset, string output)
        {
            if (string.IsNullOrEmpty(output)) throw new ArgumentException("--sam-output is required");
            Directory.CreateDirectory(output);
            var errors = new List<string>();
            var records = new List<RenderedSample>();
            var report = new Report { unity = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName, build_id = SamBuildIdentity.Id };
            Schedule schedule = null;
            try
            {
                var camera = ReadCamera(cameraAsset);
                if (scheduleAsset == null) throw new InvalidOperationException("Original sample schedule has not been staged.");
                schedule = JsonUtility.FromJson<Schedule>(scheduleAsset.text);
                if (schedule.frames == null || schedule.frames.Length == 0 || schedule.width != 640 || schedule.height != 360)
                    throw new InvalidOperationException("Invalid reference schedule dimensions or frame list.");
                int next = 0;
                for (int i = 0; i < schedule.frames.Length; i++)
                {
                    var frame = schedule.frames[i];
                    if (frame.index != i || frame.start_ms != next || frame.duration_ms <= 0 ||
                        string.IsNullOrEmpty(frame.reference_sha256) || frame.reference_sha256.Length != 64)
                        throw new InvalidOperationException("Missing, reordered or invalid reference sample " + i);
                    next += frame.duration_ms;
                }
                if (next != schedule.duration_ms || runner.skills[0].skillId != schedule.skill_id ||
                    Math.Abs(runner.skills[0].duration * 1000 - next) > .01 || Math.Abs(camera.duration_s * 1000 - next) > .01)
                    throw new InvalidOperationException("Camera, skill and reference schedule timings do not agree.");
                ApplyCamera(view, camera);
                UnityEngine.Object.FindFirstObjectByType<SamStageLayout>()?.SetReferenceVisible(true);
                report.schedule_sha256 = Hash(scheduleAsset.bytes);
                report.camera_sha256 = Hash(cameraAsset.bytes);
                report.skill_id = schedule.skill_id;
                report.width = schedule.width; report.height = schedule.height; report.duration_ms = schedule.duration_ms;
            }
            catch (Exception exception) { errors.Add(exception.ToString()); }
            if (errors.Count > 0)
            {
                Finish(output, report, records, errors);
                yield break;
            }
            void Log(string message, string stack, LogType type)
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
            Application.logMessageReceived += Log;
            runner.automaticTick = false;
            runner.playbackSpeed = 1;
            runner.Clock.Paused = false;
            foreach (var technicalTarget in UnityEngine.Object.FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))
            {
                var renderer = technicalTarget.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = false;
            }
            var target = new RenderTexture(schedule.width, schedule.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = SamFrameReadback.Samples };
            target.Create();
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false, false);
            var readback = new SamFrameReadback(target);
            yield return null;
            if (!runner.TryStart(0)) errors.Add("Skill failed to start.");
            else
            {
                foreach (var sample in schedule.frames)
                {
                    double desired = sample.start_ms / 1000.0;
                    runner.Step(Math.Max(0, desired - runner.Clock.Time), true);
                    if (Math.Abs(runner.Clock.Time - desired) > .000001)
                    { errors.Add("Runtime did not evaluate requested original sample " + sample.index); break; }
                    yield return null;
                    RenderPipeline.SubmitRenderRequest(view, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    readback.Read(texture);
                    byte[] png = texture.EncodeToPNG();
                    string filename = $"frame_{sample.index:D3}.png";
                    File.WriteAllBytes(Path.Combine(output, filename), png);
                    records.Add(new RenderedSample { index = sample.index, start_ms = sample.start_ms,
                        duration_ms = sample.duration_ms, evaluated_time_s = runner.Clock.Time,
                        file = filename, sha256 = Hash(png), reference_sha256 = sample.reference_sha256 });
                }
                runner.Step(Math.Max(0, schedule.duration_ms / 1000.0 - runner.Clock.Time), true);
                report.completed = !runner.IsCasting && records.Count == schedule.frames.Length;
                if (!report.completed) errors.Add("Incomplete original-sample capture.");
            }
            runner.Cancel(true);
            if (runner.vfx.ActiveEffectCount != 0) errors.Add("Effect survived cancellation.");
            readback.Dispose(); target.Release();
            UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(texture);
            Application.logMessageReceived -= Log;
            Finish(output, report, records, errors);
        }

        private static void Finish(string output, Report report, List<RenderedSample> records, List<string> errors)
        {
            report.frames = records.ToArray(); report.errors = errors.ToArray();
            File.WriteAllText(Path.Combine(output, "reference_capture.json"), JsonUtility.ToJson(report, true));
            int code = report.completed && errors.Count == 0 ? 0 : 1;
            Debug.Log("SAM_REFERENCE_CAPTURE_FINISHED code=" + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
        private static Vector3 Vector(float[] value)
        {
            if (value == null || value.Length != 3 || !Finite(value[0]) || !Finite(value[1]) || !Finite(value[2]))
                throw new InvalidOperationException("Nonfinite or missing camera vector.");
            return new Vector3(value[0], value[1], value[2]);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var builder = new StringBuilder(64);
                foreach (byte item in sha.ComputeHash(bytes)) builder.Append(item.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
