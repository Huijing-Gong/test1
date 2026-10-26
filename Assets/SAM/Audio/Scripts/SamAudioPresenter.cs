using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction
{
    public enum SamAudioOutputMode { Automatic, SilentCapture, Realtime }

    /// <summary>Audio backend projection of the runner's one semantic clock. No independent Update.</summary>
    [DisallowMultipleComponent]
    public sealed class SamAudioPresenter : MonoBehaviour
    {
        public AudioClip thrusterDraft, punchImpactDraft, landDustDraft, ignitionDraft, mechanicalDraft;
        public AudioClip punchWhooshDraft;
        [Range(0, 1)] public float masterVolume = .45f;
        [Range(0, 1)] public float spatialBlend = 1;
        [Range(1, 24)] public int maximumVoices = 12;
        public bool muted;
        public float PlaybackSpeed => playbackSpeed;
        public bool PlaybackPaused => playbackPaused;
        public int ActiveVoiceCount { get { int n = 0; foreach (var v in voices) if (v.active) n++; return n; } }
        public int ActiveLoopCount { get { int n = 0; foreach (var v in voices) if (v.active && v.source.loop) n++; return n; } }
        public int ScheduledVoiceCount { get { int n = 0; foreach (var v in voices) if (v.active && v.submitted && v.dspStart > AudioSettings.dspTime) n++; return n; } }
        public int PooledVoiceCount => voices.Count;
        public int BackendPlayingCount { get { int n = 0; foreach (var v in voices) if (v.source != null && v.source.isPlaying) n++; return n; } }
        // Counts backend Play/PlayScheduled requests, NOT proof of PCM output or audibility.
        public int StartedVoiceCount { get; private set; }
        public int DispatchedCueCount { get; private set; }
        public int ExpiredSkippedCount { get; private set; }
        public int SilentCueCount { get; private set; }
        public int CapacitySkippedCount { get; private set; }
        public int StolenVoiceCount { get; private set; }
        public int TransportRebuildCount { get; private set; }
        public int CursorWriteCount { get; private set; }
        public double MaximumFixedLateness { get; private set; }
        public const double ScheduleAheadSeconds = .2;
        public const double MaximumFixedLateSeconds = .1;

        public struct VoiceState
        {
            public string cueId;
            public long execution;
            public Vector3 position;
            public bool followsSocket, loop, paused, submitted, scheduled;
            public double age, renderAge, lateness, dspStart;
            public float pitch, volume;
            public int sampleCursor;
        }
        private sealed class Entry
        {
            internal SamAudioCue cue;
            internal Transform socket;
            internal Vector3 fixedPosition;
            internal bool dispatched, terminal, everSubmitted, renderOriginSet;
            internal double renderOrigin, lateness;
            internal Voice voice;
        }
        private sealed class Voice
        {
            internal AudioSource source;
            internal Entry entry;
            internal long execution;
            internal bool active, fading, paused, submitted;
            internal float fade = 1;
            internal double age, renderAge, dspStart;
        }
        private readonly List<Voice> voices = new List<Voice>(12);
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private long execution;
        private float playbackSpeed = 1;
        private bool playbackPaused, realtime, mappingValid, mappingDirty;
        private double semanticTime, anchorSkill, anchorDsp;

        public VoiceState[] InspectVoices()
        {
            var result = new List<VoiceState>();
            foreach (var v in voices)
                if (v.active && v.source != null) result.Add(new VoiceState {
                    cueId = v.entry.cue.id, execution = v.execution, position = v.source.transform.position,
                    followsSocket = v.entry.cue.attachment == SamAudioAttachment.FollowSocket,
                    loop = v.source.loop, paused = v.paused, submitted = v.submitted,
                    scheduled = v.submitted && v.dspStart > AudioSettings.dspTime,
                    age = v.age, renderAge = v.renderAge, lateness = v.entry.lateness, dspStart = v.dspStart,
                    pitch = v.source.pitch, volume = v.source.volume, sampleCursor = v.source.timeSamples });
            return result.ToArray();
        }

        public void BeginExecution(SamAudioTimeline timeline, Func<string, Transform> socketResolver,
            long id, float speed, bool audible)
        {
            StopAll(true);
            execution = id; semanticTime = 0; realtime = audible;
            playbackSpeed = speed; playbackPaused = speed <= 0;
            mappingValid = false; mappingDirty = false;
            if (timeline == null || !timeline.UsesTimeline) return;
            foreach (var marker in timeline.Events)
                if (timeline.TryGet(marker.id, out var cue)) entries.Add(cue.id, new Entry {
                    cue = cue.Snapshot(), socket = socketResolver(cue.socket), renderOrigin = cue.time });
        }

        /// <summary>Synchronize controls even while paused; no Play or seek on an unchanged normal frame.</summary>
        public void SetPlayback(float speed, bool paused)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0 || speed > 3)
                throw new ArgumentOutOfRangeException(nameof(speed));
            paused = paused || speed <= 0 || AudioListener.pause;
            bool changed = speed != playbackSpeed || paused != playbackPaused;
            playbackSpeed = speed; playbackPaused = paused;
            if (!changed) return;
            mappingDirty = true;
            foreach (var v in voices)
            {
                if (!v.active || v.source == null) continue;
                if (v.fading) { Stop(v); continue; }
                // Pause on a future scheduled source is not used as a cancellation contract.
                if (!v.entry.dispatched) { Stop(v); continue; }
                v.source.pitch = speed;
                if (paused && !v.paused) { if (v.submitted) v.source.Pause(); v.paused = true; }
            }
        }

        /// <summary>Called only by an actual clock marker after its exact pose and execution guard.</summary>
        public void Play(SamAudioCue cue, Transform socket, long id)
        {
            if (cue == null) throw new ArgumentNullException(nameof(cue));
            if (id != execution || !entries.TryGetValue(cue.id, out var e) || e.dispatched) return;
            e.dispatched = true; e.socket = socket;
            e.fixedPosition = socket != null ? socket.position : transform.position;
            DispatchedCueCount++;
            if (!realtime) SilentCueCount++;
        }

        public void Play(string cueId, Vector3 position, int seed)
            => Play(cueId, position, seed, semanticTime);

        public void Play(string cueId, Vector3 position, int seed, double skillTime)
        {
            var clip = Resolve(cueId);
            if (clip == null) return;
            string key = "legacy/" + seed + "/" + cueId;
            if (entries.ContainsKey(key)) return;
            entries.Add(key, new Entry {
                cue = new SamAudioCue { id = key, clip = clip, time = skillTime,
                    attachment = SamAudioAttachment.FixedEventPosition },
                dispatched = true, fixedPosition = position, renderOrigin = skillTime });
            DispatchedCueCount++;
            if (!realtime) SilentCueCount++;
        }

        /// <summary>Once per outer Step, after all 120 Hz substeps. DSP is an output map, not an event cursor.</summary>
        public void Commit(double skillTime, bool audible)
        {
            if (double.IsNaN(skillTime) || double.IsInfinity(skillTime) || skillTime < 0)
                throw new ArgumentOutOfRangeException(nameof(skillTime));
            semanticTime = skillTime;
            if (realtime != audible) { realtime = audible; mappingDirty = true; }
            if (muted || !isActiveAndEnabled) { foreach (var v in voices) Stop(v); mappingDirty = true; return; }
            if (playbackPaused) return;
            double dsp = AudioSettings.dspTime;
            // Resync a discontinuous manual jump / stalled transport, never each 120 Hz substep.
            bool discontinuity = realtime && mappingValid &&
                Math.Abs(anchorSkill + (dsp - anchorDsp) * playbackSpeed - skillTime) > .075 * Math.Max(.1, playbackSpeed);
            if (!mappingValid || mappingDirty || discontinuity)
            {
                if (mappingValid) TransportRebuildCount++;
                foreach (var v in voices) if (v.active) Stop(v);
                anchorSkill = skillTime; anchorDsp = dsp; mappingValid = true; mappingDirty = false;
            }
            foreach (var e in entries.Values)
            {
                if (e.terminal) continue;
                double age = skillTime - e.cue.time;
                bool loop = e.cue.role == SamAudioRole.CombatHeatLoop;
                bool fixedPoint = e.cue.attachment == SamAudioAttachment.FixedEventPosition;
                if (!e.dispatched && (fixedPoint || !realtime || age < -ScheduleAheadSeconds * playbackSpeed)) continue;
                if (!e.renderOriginSet && e.dispatched)
                {
                    e.lateness = Math.Max(0, age) / Math.Max(.0001, playbackSpeed);
                    if (fixedPoint) MaximumFixedLateness = Math.Max(MaximumFixedLateness, e.lateness);
                    // A fixed transient keeps its attack. Its render cursor starts now, with explicit latency.
                    if (realtime && fixedPoint && !loop && !e.everSubmitted &&
                        (e.lateness > MaximumFixedLateSeconds || age >= e.cue.clip.length))
                    { Expire(e); continue; }
                    e.renderOrigin = realtime && fixedPoint ? skillTime : e.cue.time;
                    e.renderOriginSet = true;
                }
                double renderAge = skillTime - e.renderOrigin;
                if (!loop && renderAge >= e.cue.clip.length)
                { Expire(e); continue; }
                var voice = e.voice;
                if (voice == null || !voice.active)
                {
                    voice = Acquire(e);
                    if (voice == null)
                    {
                        // A future source can retry while another clip is about to free a slot.
                        // A due fixed transient is explicitly dropped, never deferred into a lost attack.
                        if (e.dispatched) { e.terminal = true; CapacitySkippedCount++; }
                        continue;
                    }
                    voice.age = Math.Max(0, age); voice.renderAge = Math.Max(0, renderAge);
                    ApplyVolume(voice, Math.Max(skillTime, e.cue.time));
                    if (realtime)
                    {
                        voice.source.pitch = playbackSpeed;
                        int cursor = SampleOffset(e.cue.clip, Math.Max(0, renderAge), loop);
                        voice.source.timeSamples = cursor; CursorWriteCount++;
                        double onset = fixedPoint ? dsp : anchorDsp + (e.renderOrigin - anchorSkill) / playbackSpeed;
                        voice.dspStart = Math.Max(dsp, onset);
                        if (onset > dsp) voice.source.PlayScheduled(onset);
                        else voice.source.Play();
                        voice.submitted = true; e.everSubmitted = true; StartedVoiceCount++;
                    }
                }
                voice.age = Math.Max(0, age); voice.renderAge = Math.Max(0, renderAge);
                voice.source.pitch = playbackSpeed;
                if (!fixedPoint && e.socket != null) voice.source.transform.position = e.socket.position;
                ApplyVolume(voice, skillTime);
            }
        }

        public static int SampleOffset(AudioClip clip, double age, bool loop)
        {
            long sample = (long)Math.Floor(Math.Max(0, age) * clip.frequency);
            return loop ? (int)(sample % clip.samples) : (int)Math.Min(clip.samples - 1L, sample);
        }

        public bool ReadOutput(string cueId, float[] samples)
        {
            foreach (var v in voices)
                if (v.active && v.submitted && v.entry.cue.id == cueId)
                { v.source.GetOutputData(samples, 0); return true; }
            Array.Clear(samples, 0, samples.Length); return false;
        }

        private Voice Acquire(Entry entry)
        {
            for (int i = voices.Count - 1; i >= 0; --i) if (voices[i].source == null) voices.RemoveAt(i);
            if (ActiveVoiceCount >= Mathf.Max(1, maximumVoices)) return null;
            Voice voice = null;
            foreach (var candidate in voices) if (!candidate.active) { voice = candidate; break; }
            if (voice == null && voices.Count < Mathf.Max(1, maximumVoices))
            {
                var emitter = new GameObject("SAM SFX / " + name);
                var source = emitter.AddComponent<AudioSource>();
                source.playOnAwake = false; source.dopplerLevel = 0;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 1.4f; source.maxDistance = 28;
                voice = new Voice { source = source }; voices.Add(voice);
            }
            if (voice == null) return null; // Never steal heat or another already scheduled onset.
            voice.entry = entry; entry.voice = voice; voice.execution = execution;
            voice.active = true; voice.fade = 1;
            voice.source.clip = entry.cue.clip;
            voice.source.loop = entry.cue.role == SamAudioRole.CombatHeatLoop;
            voice.source.spatialBlend = spatialBlend;
            voice.source.transform.position = entry.cue.attachment == SamAudioAttachment.FollowSocket && entry.socket != null
                ? entry.socket.position : entry.fixedPosition;
            return voice;
        }

        private void Expire(Entry e)
        {
            if (!e.everSubmitted && realtime && e.dispatched) ExpiredSkippedCount++;
            if (e.voice != null) Stop(e.voice);
            e.terminal = true;
        }

        /// <summary>Idle cancellation fade only; it still advances exclusively from Runner.Step.</summary>
        public void Advance(double skillDelta, double skillTime)
        {
            if (playbackPaused) return;
            foreach (var v in voices)
            {
                if (!v.active || !v.fading) continue;
                if (v.entry.cue.attachment == SamAudioAttachment.FollowSocket && v.entry.socket != null)
                    v.source.transform.position = v.entry.socket.position;
                v.age += skillDelta; v.renderAge += skillDelta;
                v.fade = Mathf.Max(0, v.fade - (float)(skillDelta / .08));
                if (v.fade <= 0) Stop(v); else ApplyVolume(v, skillTime);
            }
        }

        public void StopAll(bool immediate)
        {
            // Clear every pending semantic request and future DSP request, including pre-dispatch sources.
            entries.Clear(); mappingValid = false; mappingDirty = false;
            foreach (var v in voices)
            {
                if (!v.active) continue;
                if (immediate || playbackPaused || AudioListener.pause || !isActiveAndEnabled || !v.entry.dispatched ||
                    (v.submitted && v.dspStart > AudioSettings.dspTime)) Stop(v);
                else { v.fading = true; v.fade = 1; }
            }
        }

        public void CompleteExecution()
        {
            if (realtime)
                foreach (var e in entries.Values)
                    if (e.dispatched && !e.everSubmitted && !e.terminal) ExpiredSkippedCount++;
            StopAll(true);
        }
        private void ApplyVolume(Voice v, double time)
        {
            float envelope = v.entry.cue.gainOverSkillTime == null ? 1 : v.entry.cue.gainOverSkillTime.Evaluate((float)time);
            if (float.IsNaN(envelope) || float.IsInfinity(envelope)) envelope = 0;
            v.source.volume = Mathf.Clamp01(masterVolume * v.entry.cue.gain * Mathf.Max(0, envelope) * v.fade);
        }
        private static void Stop(Voice v)
        {
            // Stop and detach the resource: no reliance on isPlaying for scheduled/paused ownership.
            if (v.source != null) { v.source.Stop(); v.source.clip = null; v.source.loop = false; }
            if (v.entry != null && v.entry.voice == v) v.entry.voice = null;
            v.active = false; v.fading = false; v.paused = false; v.submitted = false;
        }
        private AudioClip Resolve(string id)
        {
            switch (id)
            {
                case "Thruster": return thrusterDraft;
                case "PunchImpact": return punchImpactDraft;
                case "LandDust": return landDustDraft;
                case "PunchTrail": return punchWhooshDraft;
                case "Ignition": return ignitionDraft;
                case "Mechanical": return mechanicalDraft;
                default: return null;
            }
        }
        private void OnDisable() { StopAll(true); }
        private void OnDestroy()
        {
            StopAll(true);
            foreach (var voice in voices) if (voice.source != null)
            {
                if (Application.isPlaying) Destroy(voice.source.gameObject);
                else DestroyImmediate(voice.source.gameObject);
            }
            voices.Clear();
        }
    }
}
