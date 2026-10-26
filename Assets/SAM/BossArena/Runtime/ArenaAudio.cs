using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    public enum ArenaSound
    {
        Preload, HeavyCharge, SwingRight, SwingLeft, SwingHeavy, StompGround,
        EmberTail, HitMetal, HitBody, StepHeavy, StepLight, Dodge, Vent, BladeWhoosh
    }

    [Serializable]
    public sealed class ArenaSoundAsset
    {
        public ArenaSound cue;
        public AudioClip clip;
        [Range(0, 1)] public float gain = 1;
    }

    [Serializable]
    public sealed class ArenaSoundEvent
    {
        public int generation;
        public long occurrence, group;
        public string cue, result;
        public double eventTime, dspStart, dispatchDsp, lateness;
        public Vector3 position;
        public float gain, pitch;
        public bool verifiedDamage;
    }

    /// <summary>
    /// Event backend, not an attack timeline. The animation/combat owner supplies
    /// actual markers. Only the real fighter Damaged event can emit contact audio.
    /// Tick is called after the same outer simulation step used by live play/capture.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaAudio : MonoBehaviour
    {
        public ArenaSoundAsset[] bank = Array.Empty<ArenaSoundAsset>();
        [Range(0, 1)] public float masterVolume = .72f;
        [Range(0, 1)] public float spatialBlend = .9f;
        [Range(8, 48)] public int maximumVoices = 24;
        public float minimumDistance = 2.5f, maximumDistance = 32;
        public int ActiveVoiceCount { get { int n = 0; foreach (Voice v in voices) if (v.active) n++; return n; } }
        public int PooledVoiceCount => voices.Count;
        public int SubmittedCount { get; private set; }
        public int MissingClipCount { get; private set; }
        public int CapacityDroppedCount { get; private set; }
        public int VerifiedImpactCount { get; private set; }
        public int Generation { get; private set; }
        public bool CaptureClockActive { get; private set; }
        public IReadOnlyList<ArenaSoundEvent> Events => events;

        private sealed class Voice
        {
            public AudioSource source;
            public Transform follow;
            public bool active, impact;
            public int importance;
            public long group;
            public double endDsp;
        }
        private readonly List<Voice> voices = new List<Voice>();
        private readonly Dictionary<ArenaSound, ArenaSoundAsset> clips = new Dictionary<ArenaSound, ArenaSoundAsset>();
        private readonly HashSet<string> seen = new HashSet<string>();
        private readonly Queue<string> seenOrder = new Queue<string>();
        private readonly List<ArenaSoundEvent> events = new List<ArenaSoundEvent>();
        private ArenaSession arena;
        private double captureOriginTime, captureOriginDsp;

        public void Bind(ArenaSession session)
        {
            Unbind();
            if (session == null || session.player == null || session.boss == null)
                throw new ArgumentException("Audio requires both actual arena fighters.", nameof(session));
            arena = session;
            RebuildBank();
            arena.player.fighter.Damaged += OnDamage;
            arena.boss.fighter.Damaged += OnDamage;
            ResetAudio();
        }

        public void RebuildBank()
        {
            clips.Clear();
            foreach (ArenaSoundAsset asset in bank)
            {
                if (asset == null) continue;
                if (clips.ContainsKey(asset.cue)) throw new InvalidOperationException("Duplicate audio cue: " + asset.cue);
                clips.Add(asset.cue, asset);
            }
            // Build the complete pool before combat; recurring sounds allocate no GameObjects.
            while (voices.Count < maximumVoices)
            {
                var obj = new GameObject("Arena audio voice " + voices.Count);
                obj.transform.SetParent(transform, false);
                var source = obj.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.dopplerLevel = 0;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = minimumDistance;
                source.maxDistance = maximumDistance;
                source.spatialBlend = spatialBlend;
                source.reverbZoneMix = .25f;
                voices.Add(new Voice { source = source });
            }
        }

        /// <summary>
        /// occurrence must identify this marker (e.g. actionExecution*32+markerIndex),
        /// not a render frame. group identifies its owning action for cancellation.
        /// HitMetal/HitBody are deliberately rejected here; Bind owns real hit audio.
        /// </summary>
        public bool Emit(ArenaSound cue, Vector3 position, double eventTime, long occurrence,
            Transform follow = null, float gain = 1, long group = 0)
        {
            if (cue == ArenaSound.HitMetal || cue == ArenaSound.HitBody)
                throw new InvalidOperationException("Impact cues require an actual fighter.Damaged event.");
            return Dispatch(cue, position, eventTime, occurrence, follow, gain, group, false, 0);
        }

        private void OnDamage(ArenaHit hit)
        {
            if (arena == null || hit.source == null || hit.target == null) return;
            ArenaSound cue = hit.target.team == ArenaTeam.Boss ? ArenaSound.HitMetal : ArenaSound.HitBody;
            // Distinct targets can be hit by one strike; event dedup is per real target.
            Dispatch(cue, hit.point, arena.BattleTime, hit.strike, null, 1, 0, true, hit.target.GetInstanceID());
        }

        private bool Dispatch(ArenaSound cue, Vector3 position, double time, long occurrence,
            Transform follow, float gain, long group, bool verifiedDamage, int target)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0 || !Finite(position))
                throw new ArgumentOutOfRangeException(nameof(time));
            if (!isActiveAndEnabled) return false;
            string key = ((int)cue) + "/" + occurrence + "/" + target;
            if (!seen.Add(key)) return false;
            seenOrder.Enqueue(key);
            while (seenOrder.Count > 4096) seen.Remove(seenOrder.Dequeue());
            var record = new ArenaSoundEvent { generation = Generation, occurrence = occurrence, group = group,
                cue = cue.ToString(), eventTime = time, position = position, verifiedDamage = verifiedDamage,
                dispatchDsp = AudioSettings.dspTime, gain = gain, pitch = 1 };
            if (events.Count == 4096) events.RemoveAt(0);
            events.Add(record);
            if (!clips.TryGetValue(cue, out ArenaSoundAsset asset) || asset.clip == null)
            {
                MissingClipCount++; record.result = "missing_clip";
                Debug.LogWarning("ARENA_AUDIO_MISSING " + cue); return false;
            }
            if (asset.clip.loadState != AudioDataLoadState.Loaded)
            {
                // Scene build must use PCM/Preload/DecompressOnLoad; do not silently drop
                // an unloaded important attack transient and pretend playback occurred.
                MissingClipCount++; record.result = "clip_not_loaded";
                Debug.LogWarning("ARENA_AUDIO_NOT_LOADED " + cue); return false;
            }
            Tick(time);
            int importance = Importance(cue);
            Voice voice = null, victim = null;
            foreach (Voice candidate in voices)
            {
                if (!candidate.active) { voice = candidate; break; }
                if (candidate.importance < importance &&
                    (victim == null || candidate.importance < victim.importance)) victim = candidate;
            }
            if (voice == null && victim != null) { Stop(victim); voice = victim; }
            if (voice == null)
            { CapacityDroppedCount++; record.result = "voice_capacity"; return false; }
            float pitch = 1;
            if (cue == ArenaSound.StepHeavy || cue == ArenaSound.StepLight)
                pitch += ((int)(unchecked((ulong)occurrence * 2654435761UL) % 9) - 4) * .008f;
            double onset = CaptureClockActive ? captureOriginDsp + time - captureOriginTime : AudioSettings.dspTime;
            double dspNow = AudioSettings.dspTime;
            // Capture remains tied to its semantic origin. A large late event is an
            // integration failure, never repaired by rebuilding a fixed soundtrack.
            double late = Math.Max(0, dspNow - onset);
            if (CaptureClockActive && late > .035)
                throw new InvalidOperationException("Arena audio capture event is late by " + late.ToString("F6") + "s: " + cue);
            record.dspStart = Math.Max(dspNow, onset); record.lateness = late;
            record.pitch = pitch; record.gain = Mathf.Clamp01(masterVolume * asset.gain * Mathf.Max(0, gain));
            var source = voice.source;
            source.clip = asset.clip;
            source.pitch = pitch;
            source.volume = record.gain;
            source.spatialBlend = spatialBlend;
            source.transform.position = follow != null ? follow.position : position;
            source.priority = 256 - importance;
            voice.active = true; voice.follow = follow; voice.group = group;
            voice.impact = verifiedDamage || cue == ArenaSound.StompGround;
            voice.importance = importance;
            voice.endDsp = record.dspStart + asset.clip.length / pitch;
            if (CaptureClockActive) source.PlayScheduled(record.dspStart);
            else source.Play();
            record.result = "submitted";
            SubmittedCount++;
            if (verifiedDamage) VerifiedImpactCount++;
            return true;
        }

        public void Tick(double time)
        {
            double dsp = AudioSettings.dspTime;
            foreach (Voice voice in voices)
            {
                if (!voice.active) continue;
                if (voice.follow != null) voice.source.transform.position = voice.follow.position;
                if (dsp > voice.endDsp + .05) Stop(voice);
            }
        }

        public void CancelGroup(long group, bool keepContactTails = true)
        {
            if (group == 0) return;
            foreach (Voice voice in voices)
                if (voice.active && voice.group == group && (!keepContactTails || !voice.impact)) Stop(voice);
        }

        public void ResetAudio()
        {
            foreach (Voice voice in voices) Stop(voice);
            seen.Clear(); seenOrder.Clear(); events.Clear(); Generation++;
            SubmittedCount = MissingClipCount = CapacityDroppedCount = VerifiedImpactCount = 0;
            // Do not silently remap an active capture after a gameplay restart.
            // The capture owner must end/restart its recording and origin explicitly.
        }

        public void BeginCaptureClock(double semanticOrigin, double dspOrigin)
        {
            if (CaptureClockActive) throw new InvalidOperationException("Audio capture clock already active.");
            if (ActiveVoiceCount != 0) throw new InvalidOperationException("Reset audio before capture begins.");
            captureOriginTime = semanticOrigin; captureOriginDsp = dspOrigin; CaptureClockActive = true;
        }

        public void EndCaptureClock() { CaptureClockActive = false; }

        public bool ReadVoiceOutput(float[] samples, int channel = 0)
        {
            foreach (Voice voice in voices)
                if (voice.active && voice.source.isPlaying)
                { voice.source.GetOutputData(samples, channel); return true; }
            Array.Clear(samples, 0, samples.Length); return false;
        }

        private static int Importance(ArenaSound cue)
        {
            switch (cue)
            {
                case ArenaSound.HitBody: case ArenaSound.HitMetal: case ArenaSound.StompGround: return 220;
                case ArenaSound.HeavyCharge: case ArenaSound.Preload: return 190;
                case ArenaSound.SwingHeavy: case ArenaSound.SwingRight: case ArenaSound.SwingLeft: return 170;
                case ArenaSound.Dodge: case ArenaSound.BladeWhoosh: return 160;
                case ArenaSound.Vent: return 100;
                case ArenaSound.StepHeavy: case ArenaSound.StepLight: return 80;
                default: return 50;
            }
        }

        private static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
            float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));
        private static void Stop(Voice voice)
        {
            if (voice.source != null) { voice.source.Stop(); voice.source.clip = null; }
            voice.active = false; voice.follow = null; voice.group = 0;
        }
        private void Unbind()
        {
            if (arena == null) return;
            if (arena.player != null && arena.player.fighter != null) arena.player.fighter.Damaged -= OnDamage;
            if (arena.boss != null && arena.boss.fighter != null) arena.boss.fighter.Damaged -= OnDamage;
            arena = null;
        }
        private void OnDisable() { ResetAudio(); }
        private void OnDestroy() { Unbind(); foreach (Voice voice in voices) Stop(voice); }
    }
}
