using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SamProduction.BossArena
{
    // Optional offline QA view. Never takes ownership of ArenaThirdPersonCamera or input.
    // No animation, damage, actor, cinematic or game-camera state is changed here.
    public sealed class ArenaReferenceCaptureCamera : IDisposable
    {
        [Serializable] public sealed class ReferenceFrame
        { public int index, start_ms, duration_ms; public string path, sha256; }
        [Serializable] public sealed class Anchor
        { public float source_seconds; public int reference_ms, reference_frame_index, reference_start_ms, reference_duration_ms; public string event_name, evidence; }
        [Serializable] public sealed class Segment
        { public string phase, clock, evidence; public Anchor[] anchors; }
        [Serializable] public sealed class Target
        { public string actor, evidence; public int reference_frame_index; public bool partial; public float x, y, width, height; }
        [Serializable] public sealed class Shot
        {
            public string id, anchor, notes; public int start_ms, end_ms;
            public Vector3 position_offset_world, focus_offset_world;
            public float vertical_fov_degrees; public Target[] bbox_targets;
        }
        [Serializable] public sealed class Calibration
        {
            public int schemaVersion, rendererIndex;
            public string profile, skill, scope, reference_root, notes;
            public ReferenceFrame[] reference_frames; public Segment[] segments; public Shot[] shots;
        }
        [Serializable] public sealed class Sample
        {
            public string profile, phase, clock, shot, reference_path, reference_sha256;
            public float source_seconds, reference_ms, vertical_fov_degrees, aspect;
            public int reference_frame_index, reference_start_ms, reference_duration_ms;
            public Vector3 position_world, focus_world; public Quaternion rotation_world;
            // Manual target boxes, top-left normalized coordinates; not measurements of this render.
            public Target[] manual_bbox_targets;
        }

        public Camera Camera { get; private set; }
        public Calibration Settings { get; private set; }
        public string CalibrationPath { get; private set; }
        public string CalibrationSha256 { get; private set; }
        private readonly Vector3 bossInitial, contactInitial;

        public ArenaReferenceCaptureCamera(Camera gameCamera, string calibrationPath, string expectedSkill,
            Vector3 bossInitialPosition, Vector3 contactInitialPosition)
        {
            if (gameCamera == null) throw new ArgumentNullException(nameof(gameCamera));
            CalibrationPath = Path.GetFullPath(calibrationPath);
            Settings = JsonUtility.FromJson<Calibration>(File.ReadAllText(CalibrationPath));
            Validate(Settings, expectedSkill);
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(CalibrationPath))
                CalibrationSha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            bossInitial = bossInitialPosition; contactInitial = contactInitialPosition;
            var go = new GameObject("SAM optional reference capture camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera = go.AddComponent<Camera>(); Camera.CopyFrom(gameCamera);
            Camera.enabled = false; Camera.targetTexture = null; Camera.rect = new Rect(0, 0, 1, 1);
            Camera.usePhysicalProperties = false; Camera.ResetProjectionMatrix(); Camera.ResetWorldToCameraMatrix();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Base; data.SetRenderer(Settings.rendererIndex);
            var original = gameCamera.GetComponent<UniversalAdditionalCameraData>();
            if (original != null)
            {
                data.renderShadows = original.renderShadows;
                data.requiresDepthTexture = original.requiresDepthTexture;
                data.requiresColorTexture = original.requiresColorTexture;
                data.renderPostProcessing = original.renderPostProcessing;
                data.volumeLayerMask = original.volumeLayerMask; data.volumeTrigger = original.volumeTrigger;
                data.antialiasing = original.antialiasing; data.antialiasingQuality = original.antialiasingQuality;
                data.stopNaN = original.stopNaN; data.dithering = original.dithering;
            }
            // Untagged GameObject, no AudioListener, no camera stack; only explicit render requests use it.
        }

        public Sample Apply(string phase, float sourceSeconds, float aspect)
        {
            if (!Finite(sourceSeconds) || sourceSeconds < 0 || !Finite(aspect) || aspect <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceSeconds));
            Segment segment = null;
            foreach (var item in Settings.segments) if (item.phase == phase) { segment = item; break; }
            if (segment == null) throw new InvalidOperationException("No reviewed reference mapping for phase: " + phase);
            float milliseconds = Map(segment.anchors, sourceSeconds);
            Shot shot = null; ReferenceFrame frame = null;
            foreach (var item in Settings.shots)
                if (milliseconds >= item.start_ms && milliseconds < item.end_ms) { shot = item; break; }
            foreach (var item in Settings.reference_frames)
                if (milliseconds >= item.start_ms && milliseconds < item.start_ms + item.duration_ms) { frame = item; break; }
            if (shot == null || frame == null) throw new InvalidOperationException("Reference time has no shot/frame interval.");
            Vector3 origin = shot.anchor == "contact_initial" ? contactInitial : bossInitial;
            Vector3 position = origin + shot.position_offset_world, focus = origin + shot.focus_offset_world;
            Camera.aspect = aspect; Camera.fieldOfView = shot.vertical_fov_degrees;
            Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
            return new Sample { profile = Settings.profile, phase = phase, clock = segment.clock,
                source_seconds = sourceSeconds, reference_ms = milliseconds, shot = shot.id,
                reference_frame_index = frame.index, reference_start_ms = frame.start_ms, reference_duration_ms = frame.duration_ms,
                reference_path = frame.path, reference_sha256 = frame.sha256,
                vertical_fov_degrees = Camera.fieldOfView, aspect = aspect,
                position_world = position, focus_world = focus, rotation_world = Camera.transform.rotation,
                manual_bbox_targets = shot.bbox_targets };
        }

        private static float Map(Anchor[] anchors, float time)
        {
            if (time <= anchors[0].source_seconds) return anchors[0].reference_ms;
            for (int i = 1; i < anchors.Length; i++) if (time <= anchors[i].source_seconds)
            {
                var a = anchors[i - 1]; var b = anchors[i];
                float t = (time - a.source_seconds) / (b.source_seconds - a.source_seconds);
                return a.reference_ms + (b.reference_ms - a.reference_ms) * t;
            }
            return anchors[anchors.Length - 1].reference_ms;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static void Validate(Calibration c, string expectedSkill)
        {
            if (c == null || c.schemaVersion != 1 || c.skill != expectedSkill ||
                c.scope != "MANUAL_REFERENCE_QA_SEED_NOT_FULL_FRAME_MATCH" ||
                c.reference_frames == null || c.reference_frames.Length == 0 ||
                c.segments == null || c.segments.Length == 0 || c.shots == null || c.shots.Length == 0)
                throw new InvalidOperationException("Reference calibration schema/skill/scope mismatch.");
            int end = 0;
            foreach (var f in c.reference_frames)
            {
                if (f.start_ms != end || f.duration_ms <= 0 || string.IsNullOrEmpty(f.path) || string.IsNullOrEmpty(f.sha256))
                    throw new InvalidOperationException("Reference frame intervals must be contiguous and attributed.");
                end = f.start_ms + f.duration_ms;
            }
            int shotEnd = 0;
            foreach (var s in c.shots)
            {
                if (s.start_ms != shotEnd || s.end_ms <= s.start_ms ||
                    (s.anchor != "boss_initial" && s.anchor != "contact_initial") ||
                    !Finite(s.position_offset_world) || !Finite(s.focus_offset_world) ||
                    (s.focus_offset_world-s.position_offset_world).sqrMagnitude < .0001f ||
                    !Finite(s.vertical_fov_degrees) || s.vertical_fov_degrees < 15 || s.vertical_fov_degrees > 90)
                    throw new InvalidOperationException("Invalid fixed reference shot pose/interval.");
                shotEnd = s.end_ms;
                if (s.bbox_targets == null) continue;
                foreach (var t in s.bbox_targets)
                    if (!Finite(t.x) || !Finite(t.y) || !Finite(t.width) || !Finite(t.height) ||
                        t.x < 0 || t.y < 0 || t.width <= 0 || t.height <= 0 || t.x+t.width > 1.00001f || t.y+t.height > 1.00001f)
                        throw new InvalidOperationException("Invalid manual normalized target box.");
            }
            if (shotEnd != end) throw new InvalidOperationException("Shots must cover the full reference interval.");
            foreach (var segment in c.segments)
            {
                if (string.IsNullOrEmpty(segment.phase) || segment.anchors == null || segment.anchors.Length == 0)
                    throw new InvalidOperationException("Empty phase mapping.");
                float previous = -1; int previousReference = -1;
                foreach (var a in segment.anchors)
                {
                    if (!Finite(a.source_seconds) || a.source_seconds < 0 || a.source_seconds <= previous ||
                        a.reference_ms < previousReference || a.reference_ms < 0 || a.reference_ms >= end)
                        throw new InvalidOperationException("Phase anchors must be finite and monotonic.");
                    ReferenceFrame found = null;
                    foreach (var f in c.reference_frames) if (f.index == a.reference_frame_index) { found = f; break; }
                    if (found == null || a.reference_start_ms != found.start_ms || a.reference_duration_ms != found.duration_ms ||
                        a.reference_ms < found.start_ms || a.reference_ms >= found.start_ms+found.duration_ms)
                        throw new InvalidOperationException("Anchor disagrees with original frame start/duration metadata.");
                    previous = a.source_seconds; previousReference = a.reference_ms;
                }
            }
        }

        public void Dispose()
        {
            if (Camera == null) return;
            UnityEngine.Object.Destroy(Camera.gameObject); Camera = null;
        }
    }
}
