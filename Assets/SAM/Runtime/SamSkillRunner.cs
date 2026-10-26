using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction
{
    [RequireComponent(typeof(SamCharacterMotor))]
    public sealed class SamSkillRunner : MonoBehaviour
    {
        public SamAnimationDriver animationDriver;
        public SamVfxPresenter vfx;
        public SamAudioPresenter audioPresenter;
        public SamSkillDefinition[] skills = Array.Empty<SamSkillDefinition>();
        [Range(0, 2)] public float playbackSpeed = 1;
        public bool automaticTick = true;
        public SamAudioOutputMode audioOutputMode = SamAudioOutputMode.Automatic;
        public bool RealtimeAudio => audioOutputMode == SamAudioOutputMode.Realtime ||
            (audioOutputMode == SamAudioOutputMode.Automatic && automaticTick);
        public SamSkillClock Clock { get; } = new SamSkillClock();
        public SamSkillDefinition Current { get; private set; }
        public int HitCount { get; private set; }
        public bool IsCasting => Clock.Running;
        public event Action<SamTimedEvent> Presented;
        public event Action<SamAudioCue, long> AudioPresented;
        private SamAudioTimeline audioTimeline;
        public event Action<SamSkillDefinition, double> PoseSampled;
        public event Action PresentationReset;
        private readonly Dictionary<string, Transform> sockets = new Dictionary<string, Transform>();
        private readonly HashSet<string> hits = new HashSet<string>();
        private readonly Collider[] hitBuffer = new Collider[32];
        private SamCharacterMotor motor;
        private Vector3 origin;
        private Quaternion orientation;
        private bool pendingEnd;
        private double effectTime;

        private void Awake()
        {
            motor = GetComponent<SamCharacterMotor>();
            if (animationDriver == null) animationDriver = GetComponentInChildren<SamAnimationDriver>();
            if (vfx == null) vfx = GetComponent<SamVfxPresenter>();
            if (vfx != null) vfx.manualTick = true;
            if (audioPresenter == null) audioPresenter = GetComponent<SamAudioPresenter>();
            foreach (var item in GetComponentsInChildren<Transform>(true)) sockets[item.name] = item;
            Clock.Fired += HandleEvent;
            Clock.Finished += HandleFinish;
            Clock.PauseChanged += HandlePause;
        }

        public bool TryStart(int index)
        {
            if (index < 0 || index >= skills.Length || skills[index] == null || skills[index].animationClip == null) return false;
            var definition = skills[index];
            if (definition.startsOverheated && (!sockets.ContainsKey(definition.heatChestSocket) ||
                !sockets.ContainsKey(definition.heatLeftShoulderSocket) || !sockets.ContainsKey(definition.heatRightShoulderSocket)))
                throw new InvalidOperationException("Overheated skill needs its three armour-surface heat sockets.");
            if (IsCasting && Current != null && Clock.Time < Current.recoverTime) return false;
            var requestedAudio = SamAudioTimeline.Create(definition);
            if (requestedAudio.UsesTimeline)
                foreach (var e in requestedAudio.Events)
                    if (e.kind == SamEventKind.AudioCue && !sockets.ContainsKey(e.socket))
                        throw new InvalidOperationException("Audio cue socket missing: " + e.socket);
            long beforeCancel = Clock.ExecutionId;
            Cancel(true);
            if (Clock.ExecutionId != beforeCancel) return false;
            Current = skills[index];
            audioTimeline = requestedAudio;
            origin = transform.position;
            // Authored yaw may have a nonzero first key. Preserve the actor's current facing on entry
            // instead of adding that authoring offset again on every replay.
            orientation = transform.rotation * Quaternion.Inverse(Quaternion.Euler(0, Current.yaw.Evaluate(0), 0));
            hits.Clear();
            pendingEnd = false;
            effectTime = 0;
            animationDriver.Play(Current.animationClip);
            var requested = Current;
            long expected = Clock.ExecutionId + 1;
            audioPresenter?.BeginExecution(audioTimeline, Socket, expected, playbackSpeed, RealtimeAudio);
            audioPresenter?.SetPlayback(playbackSpeed, false);
            Clock.Begin(Current.duration, audioTimeline.Events);
            if (!Clock.Running || Clock.ExecutionId != expected || Current != requested) return false;
            Sample(0, 0, true);
            if (!Clock.Running || Clock.ExecutionId != expected || Current != requested) return false;
            if (Current.startsOverheated)
                vfx?.BeginCombatHeat(Socket(Current.heatChestSocket), Socket(Current.heatLeftShoulderSocket),
                    Socket(Current.heatRightShoulderSocket), unchecked((int)expected * 397 ^ StableHash(Current.skillId)));
            audioPresenter?.Commit(0, RealtimeAudio);
            return true;
        }

        public void Step(double delta, bool exact = false)
        {
            if (delta < 0 || double.IsNaN(delta) || double.IsInfinity(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            audioPresenter?.SetPlayback(playbackSpeed, Clock.Paused || playbackSpeed <= 0);
            if (Clock.Paused || playbackSpeed <= 0) return;
            if (IsCasting)
            {
                double remaining = delta * playbackSpeed;
                long execution = Clock.ExecutionId;
                while (remaining > 1e-12 && Clock.Running)
                {
                    double nextQuantum = (Math.Floor(Clock.Time * 120 + 1e-7) + 1) / 120;
                    double step = Math.Min(remaining, nextQuantum - Clock.Time);
                    Clock.Advance(step);
                    if (Clock.ExecutionId != execution || (!Clock.Running && !pendingEnd)) return;
                    Sample(Clock.Time, (float)step, exact);
                    if (Clock.ExecutionId != execution || (!Clock.Running && !pendingEnd)) return;
                    AdvanceEffects(Clock.Time);
                    remaining -= step;
                }
                if (pendingEnd)
                {
                    animationDriver.Recover();
                    vfx?.SetHeat(0);
                    vfx?.StopAll(false);
                    audioPresenter?.CompleteExecution(); // Match completion of the current visual heat lifecycle.
                    pendingEnd = false;
                }
                else audioPresenter?.Commit(Clock.Time, RealtimeAudio);
            }
            else
            {
                animationDriver?.Sample(Clock.Time, (float)(delta * playbackSpeed), exact);
                vfx?.Advance((float)(delta * playbackSpeed));
                audioPresenter?.Advance(delta * playbackSpeed, Clock.Time);
            }
        }

        private void Update() { if (automaticTick) Step(UnityEngine.Time.deltaTime); }

        public void Cancel(bool immediate = false)
        {
            long execution = Clock.ExecutionId;
            // Read pause before Clock.Cancel clears it. A manual driver may never Step again.
            bool stopAudioNow = immediate || Clock.Paused || playbackSpeed <= 0 || !automaticTick;
            audioPresenter?.StopAll(stopAudioNow);
            Clock.Cancel();
            if (Clock.ExecutionId != execution) return;
            animationDriver?.Recover();
            vfx?.SetHeat(0);
            vfx?.StopAll(immediate);
            audioPresenter?.SetPlayback(playbackSpeed, false);
            pendingEnd = false;
            PresentationReset?.Invoke();
        }

        public Transform Socket(string name)
        {
            if (!string.IsNullOrEmpty(name) && sockets.TryGetValue(name, out var value)) return value;
            return transform;
        }

        public Vector3 HitDirection(string socketName)
        {
            if (Current != null && !string.IsNullOrEmpty(Current.hitDirectionBone))
            {
                Vector3 axis = Socket(socketName).position - Socket(Current.hitDirectionBone).position;
                if (axis.sqrMagnitude > .000001f) return axis.normalized;
            }
            return transform.forward;
        }

        private void Sample(double time, float delta, bool exact)
        {
            if (Current == null) return;
            motor.ApplySkillPosition(origin + orientation * Current.MotionAt(time),
                orientation * Quaternion.Euler(0, Current.yaw.Evaluate((float)time), 0));
            animationDriver.Sample(time, delta, exact);
            vfx?.SampleReleasedTransfers();
            vfx?.SetHeat(Current.heat.Evaluate((float)time));
            PoseSampled?.Invoke(Current, time);
        }

        private void HandleEvent(SamTimedEvent evt, long execution)
        {
            // Resolve the skeleton at the event's own time, including low-FPS jumps.
            Sample(evt.time, 0, true);
            if (!Clock.Running || Clock.ExecutionId != execution) return;
            AdvanceEffects(evt.time);
            var socket = Socket(evt.socket);
            if (evt.kind == SamEventKind.AudioCue)
            {
                if (audioTimeline != null && audioTimeline.TryGet(evt.id, out var cue))
                {
                    audioPresenter?.Play(cue, socket, execution);
                    AudioPresented?.Invoke(cue.Snapshot(), execution);
                }
                return; // Keep Presented and gameplay/VFX observers on the original visual event stream.
            }
            bool legacyAudio = audioTimeline == null || !audioTimeline.UsesTimeline;
            Vector3 direction = evt.kind == SamEventKind.Hit || evt.effect == "PunchTrail"
                ? HitDirection(evt.socket) : transform.forward;
            if (evt.kind == SamEventKind.Effect || evt.kind == SamEventKind.Ignition || evt.kind == SamEventKind.Land)
                vfx?.Play(evt.effect, socket, socket.position, direction, unchecked((int)execution * 397 ^ StableHash(evt.id)));
            if (evt.kind == SamEventKind.StopEffects) vfx?.StopTransient(false);
            if (legacyAudio && (evt.kind == SamEventKind.Effect || evt.kind == SamEventKind.Ignition || evt.kind == SamEventKind.Land))
                audioPresenter?.Play(evt.effect, socket.position, unchecked((int)execution * 397 ^ StableHash(evt.id)), evt.time);
            if (evt.kind == SamEventKind.Hit)
            {
                Physics.SyncTransforms();
                // The visible reference release is an energy strike; physical fist contact is occluded.
                int count = Current.hitReach > 0
                    ? Physics.OverlapCapsuleNonAlloc(socket.position, socket.position + direction * Current.hitReach,
                        Current.hitRadius, hitBuffer, ~0, QueryTriggerInteraction.Collide)
                    : Physics.OverlapSphereNonAlloc(socket.position, Current.hitRadius, hitBuffer, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < count; i++)
                {
                    var target = hitBuffer[i].GetComponentInParent<SamDamageable>();
                    if (target == null || target.transform.IsChildOf(transform)) continue;
                    string key = execution + ":" + evt.id + ":" + target.GetInstanceID();
                    if (hits.Add(key)) { target.Receive(Current.damage * evt.value, direction); HitCount++; }
                }
                vfx?.Play(string.IsNullOrEmpty(evt.effect) ? "PunchImpact" : evt.effect,
                    socket, socket.position, direction, unchecked((int)execution * 397 ^ StableHash(evt.id)));
                if (legacyAudio) audioPresenter?.Play("PunchImpact", socket.position, unchecked((int)execution * 397 ^ StableHash(evt.id)), evt.time);
            }
            Presented?.Invoke(evt);
        }

        private void HandleFinish(bool cancelled, long _) { pendingEnd = !cancelled; }
        private void HandlePause(bool paused)
        {
            audioPresenter?.SetPlayback(playbackSpeed, paused || playbackSpeed <= 0);
            if (!paused && Clock.Running) audioPresenter?.Commit(Clock.Time, RealtimeAudio);
        }
        private void AdvanceEffects(double time)
        {
            vfx?.Advance((float)Math.Max(0, time - effectTime));
            effectTime = time;
        }
        private static int StableHash(string value)
        {
            unchecked { int hash = 23; foreach (char c in value ?? "") hash = hash * 31 + c; return hash; }
        }
        private void OnDisable() { Cancel(true); }
        private void OnDestroy() { Clock.Fired -= HandleEvent; Clock.Finished -= HandleFinish; Clock.PauseChanged -= HandlePause; }
    }
}
