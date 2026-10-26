using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    [Serializable] public sealed class ArenaDashMarker { public string id, socket; public float time; }
    // Independent asset binding: authored airborne root Y must not enter the
    // grounded Foundation14 library or be applied by two animation owners.
    public sealed class ArenaDashClips : MonoBehaviour
    {
        public string[] ids = Array.Empty<string>();
        public AnimationClip[] clips = Array.Empty<AnimationClip>();
        public TextAsset motionData;
        [Serializable] private sealed class MarkedClip { public string id; public ArenaDashMarker[] events; }
        [Serializable] private sealed class MarkedData { public MarkedClip[] clips; }
        private ArenaMotionData data;
        private readonly Dictionary<string, ArenaDashMarker[]> markers = new Dictionary<string, ArenaDashMarker[]>();
        public bool Configured => motionData != null && ids != null && clips != null && ids.Length == clips.Length && ids.Length >= 4;
        public ArenaMotionClip Motion(string id) { Load(); return data.Get(id); }
        public AnimationClip Clip(string id)
        {
            for (int i = 0; i < ids.Length; i++) if (ids[i] == id && clips[i] != null) return clips[i];
            throw new InvalidOperationException("Missing actual dash animation: " + id);
        }
        public bool HasClip(string id)
        {
            for (int i = 0; i < ids.Length; i++) if (ids[i] == id && clips[i] != null) return true;
            return false;
        }
        public ArenaDashMarker[] Events(string id) { Load(); return markers.TryGetValue(id, out var values) ? values : Array.Empty<ArenaDashMarker>(); }
        public float EventTime(string clip, string marker)
        {
            foreach (var value in Events(clip)) if (value.id == marker) return value.time;
            throw new InvalidOperationException("Missing authored dash marker " + clip + "/" + marker);
        }
        public void Validate()
        {
            Load();
            foreach (string id in new[] { "dash_charge", "dash_slide_loop", "dash_lock_release", "dash_recover" })
            {
                var motion = Motion(id);
                if (Mathf.Abs(Clip(id).length - motion.duration) > .002f) throw new InvalidOperationException("Dash clip/trajectory duration mismatch: " + id);
                var unique = new HashSet<string>();
                foreach (var marker in Events(id))
                    if (string.IsNullOrEmpty(marker.id) || !unique.Add(marker.id) || float.IsNaN(marker.time) || float.IsInfinity(marker.time) || marker.time < 0 || marker.time > motion.duration + .00001f)
                        throw new InvalidOperationException("Invalid dash marker time: " + id + "/" + marker.id);
            }
            if (Mathf.Abs(Motion("dash_charge").duration - 2) > .1f) throw new InvalidOperationException("Ordinary charge must retain its approximately two second rhythm.");
            foreach (var point in Motion("dash_slide_loop").trajectory)
                if (point.Position.sqrMagnitude > 1e-10f || Mathf.Abs(point.yaw) > .00001f)
                    throw new InvalidOperationException("Pursuit loop must be in place; runtime owns its path.");
            float brake = EventTime("dash_lock_release", "brake_settled"), open = EventTime("dash_lock_release", "strike_open"), close = EventTime("dash_lock_release", "strike_close");
            if (!(brake > 0 && open > brake && close > open && close < Motion("dash_lock_release").duration))
                throw new InvalidOperationException("Dash braking/release marker order is invalid.");
            float swing = EventTime("dash_lock_release", "release_swing");
            if (EventTime("dash_lock_release", "heading_locked") != 0 || swing < brake || swing >= open)
                throw new InvalidOperationException("The heading lock and explicit swing cue must retain their source marker order.");
            float takeoff = EventTime("dash_recover", "hop_takeoff"), land = EventTime("dash_recover", "hop_land");
            if (!(takeoff > 0 && land > takeoff && land < Motion("dash_recover").duration)) throw new InvalidOperationException("Dash hop markers are invalid.");
            float apex = EventTime("dash_recover", "hop_apex");
            if (!(apex > takeoff && apex < land) || Mathf.Abs(EventTime("dash_recover", "recover_complete") - Motion("dash_recover").duration) > .00001f)
                throw new InvalidOperationException("The full recovery markers must include the authored apex and final endpoint.");
            if (Mathf.Abs(Motion("dash_recover").EndPosition.y) > .00001f) throw new InvalidOperationException("Dash recovery must end grounded in its local source coordinates.");
            if (HasClip("dash_abort_recover"))
            {
                var abort = Motion("dash_abort_recover");
                if (Mathf.Abs(Clip("dash_abort_recover").length-abort.duration) > .002f || Mathf.Abs(abort.EndPosition.z+1.15f) > .00001f || Mathf.Abs(abort.EndPosition.y) > .00001f)
                    throw new InvalidOperationException("Dedicated no-punch abort must retain its full authored grounded -1.15m recovery.");
                float settled=EventTime("dash_abort_recover","brake_settled"), off=EventTime("dash_abort_recover","hop_takeoff"), apexAbort=EventTime("dash_abort_recover","hop_apex"), on=EventTime("dash_abort_recover","hop_land");
                if (!(settled>0&&off>settled&&apexAbort>off&&on>apexAbort&&on<abort.duration)
                    || Mathf.Abs(EventTime("dash_abort_recover","recover_complete")-abort.duration)>.00001f)
                    throw new InvalidOperationException("Dedicated abort marker order is invalid.");
                foreach(var marker in Events("dash_abort_recover"))
                    if(marker.id.StartsWith("strike",StringComparison.Ordinal)||marker.id=="release_swing")
                        throw new InvalidOperationException("No-punch abort cannot contain strike or swing markers.");
            }
        }
        private void Load()
        {
            if (data != null) return;
            if (!Configured) throw new InvalidOperationException("Bind the actual four dash FBXs and source trajectory before running.");
            var loaded = ArenaMotionData.Read(motionData);
            var marked = JsonUtility.FromJson<MarkedData>(ArenaMotionEncoding.Text(motionData));
            markers.Clear();
            if (marked?.clips != null) foreach (var clip in marked.clips) markers.Add(clip.id, clip.events ?? Array.Empty<ArenaDashMarker>());
            data = loaded;
        }
    }
}
