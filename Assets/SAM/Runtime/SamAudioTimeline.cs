using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction
{
    public enum SamAudioAttachment { FollowSocket, FixedEventPosition }
    public enum SamAudioRole { OneShot, CombatHeatLoop, ImpactCombined, ImpactCore, ImpactTail }
    public enum SamImpactAudioMode { Combined, Split }

    [Serializable]
    public sealed class SamAudioCue
    {
        public string id;
        public double time;
        public AudioClip clip;
        public string socket;
        public SamAudioAttachment attachment;
        public SamAudioRole role;
        [Range(0, 4)] public float gain = 1;
        // Absolute skill seconds; independently editable from the visual heat curve.
        public AnimationCurve gainOverSkillTime = AnimationCurve.Constant(0, 3.01f, 1);

        internal SamAudioCue Snapshot() => new SamAudioCue {
            id = id, time = time, clip = clip, socket = socket, attachment = attachment,
            role = role, gain = gain, gainOverSkillTime = gainOverSkillTime == null ? null :
                new AnimationCurve(gainOverSkillTime.keys) { preWrapMode = gainOverSkillTime.preWrapMode,
                    postWrapMode = gainOverSkillTime.postWrapMode }
        };
    }

    /// <summary>An execution snapshot, dispatched exclusively by the existing SamSkillClock.</summary>
    public sealed class SamAudioTimeline
    {
        private const string Prefix = "__sam_audio__/";
        private readonly Dictionary<string, SamAudioCue> cues = new Dictionary<string, SamAudioCue>(StringComparer.Ordinal);
        public bool UsesTimeline { get; private set; }
        public IReadOnlyList<SamTimedEvent> Events { get; private set; }
        public int SelectedCueCount => cues.Count;
        public bool TryGet(string eventId, out SamAudioCue cue) => cues.TryGetValue(eventId, out cue);

        public static SamAudioTimeline Create(SamSkillDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var result = new SamAudioTimeline { UsesTimeline = definition.useAudioTimeline };
            if (!result.UsesTimeline) { result.Events = definition.events; return result; }
            if (!Finite(definition.duration) || definition.duration <= 0)
                throw new ArgumentException("Audio timeline needs a finite positive duration.");
            if (!Enum.IsDefined(typeof(SamImpactAudioMode), definition.impactAudioMode))
                throw new ArgumentException("Unknown impact audio mode.");

            var merged = new List<SamTimedEvent>();
            var visualIds = new HashSet<string>(StringComparer.Ordinal);
            double previous = -1;
            foreach (var e in definition.events ?? Array.Empty<SamTimedEvent>())
            {
                if (e == null || string.IsNullOrEmpty(e.id) || e.id.StartsWith(Prefix, StringComparison.Ordinal) ||
                    !visualIds.Add(e.id) || !Finite(e.time) || e.time < previous || e.time > definition.duration ||
                    e.time < 0 || e.kind == SamEventKind.AudioCue)
                    throw new ArgumentException("Visual events must retain unique IDs and stable in-range chronological order.");
                previous = e.time;
                merged.Add(e);
            }
            var cueIds = new HashSet<string>(StringComparer.Ordinal);
            int combined = 0, core = 0, tail = 0, heat = 0;
            double coreTime = -1, tailTime = -1;
            foreach (var authored in definition.audioCues ?? Array.Empty<SamAudioCue>())
            {
                if (authored == null || string.IsNullOrEmpty(authored.id) || !cueIds.Add(authored.id) ||
                    !Finite(authored.time) || authored.time < 0 || authored.time > definition.duration ||
                    authored.clip == null || string.IsNullOrEmpty(authored.socket) ||
                    !Finite(authored.gain) || authored.gain < 0 || authored.gain > 4 ||
                    !Enum.IsDefined(typeof(SamAudioAttachment), authored.attachment) ||
                    !Enum.IsDefined(typeof(SamAudioRole), authored.role))
                    throw new ArgumentException("Audio cues need unique IDs, clips, sockets and valid times/gains/roles.");
                if (authored.gainOverSkillTime != null)
                    foreach (var key in authored.gainOverSkillTime.keys)
                        if (!Finite(key.time) || !Finite(key.value) || key.value < 0)
                            throw new ArgumentException("Audio gain envelopes require finite nonnegative keys.");
                if (authored.role == SamAudioRole.CombatHeatLoop)
                {
                    if (++heat > 1 || authored.time != 0 || authored.attachment != SamAudioAttachment.FollowSocket)
                        throw new ArgumentException("Combat heat has one following loop at time zero.");
                    if (!definition.startsOverheated) continue;
                }
                if (authored.role == SamAudioRole.ImpactCombined && definition.impactAudioMode != SamImpactAudioMode.Combined) continue;
                if ((authored.role == SamAudioRole.ImpactCore || authored.role == SamAudioRole.ImpactTail) &&
                    definition.impactAudioMode != SamImpactAudioMode.Split) continue;
                if (authored.role == SamAudioRole.ImpactCombined) combined++;
                if (authored.role == SamAudioRole.ImpactCore) { core++; coreTime = authored.time; }
                if (authored.role == SamAudioRole.ImpactTail) { tail++; tailTime = authored.time; }
                if ((authored.role == SamAudioRole.ImpactCombined || authored.role == SamAudioRole.ImpactCore ||
                    authored.role == SamAudioRole.ImpactTail) && authored.attachment != SamAudioAttachment.FixedEventPosition)
                    throw new ArgumentException("Energy impact layers must keep their release position.");
                var cue = authored.Snapshot();
                string marker = Prefix + cue.id;
                result.cues.Add(marker, cue);
                // Stable insertion: existing visual/hit events run first at equal times; cue authoring order is retained.
                int index = merged.FindIndex(e => e.time > cue.time);
                var timed = new SamTimedEvent { id = marker, time = cue.time, kind = SamEventKind.AudioCue, socket = cue.socket };
                if (index < 0) merged.Add(timed); else merged.Insert(index, timed);
            }
            if (combined > 1 || core > 1 || tail > 1 || (core != tail) || (core == 1 && coreTime != tailTime))
                throw new ArgumentException("Use one combined impact OR one simultaneous core/tail pair.");
            result.Events = merged.ToArray();
            return result;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
