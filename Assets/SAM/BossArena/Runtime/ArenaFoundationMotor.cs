using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Manually ticked foundation motion. This component supplies explicit moves,
    // not the final patrol schedule or phase/attack AI. No automatic Update.
    public sealed class ArenaFoundationMotor : MonoBehaviour
    {
        public enum MotionState { Guard, Starting, Walking, Stopping, Turning, HitReaction, Rebalancing, Dying, DeathHold }
        public ArenaProductionClips foundation;
        public MotionState State { get; private set; } = MotionState.Guard;
        public string ClipId { get; private set; } = "guard";
        public float StateTime { get => (float)sourceClock; private set => sourceClock = value; }
        private double sourceClock;
        public bool Busy => suspended || State != MotionState.Guard || guardSettleTime > 0;
        public bool DeathStarted => State == MotionState.Dying || State == MotionState.DeathHold;
        public bool IsGuard => initialized && !Busy;
        public bool Initialized => initialized;
        public float HeadingYaw => heading.eulerAngles.y;
        public float ActualSpeed { get; private set; }
        public float WalkPhase => State == MotionState.Walking ? Mathf.Repeat(loopDistance / walkMotion.stride, 1) : 0;
        public bool Blocked { get; private set; }
        public string DisplayState => State == MotionState.Turning ? "TURNING" : State == MotionState.HitReaction ? "HIT REACTION"
            : DeathStarted ? "DOWN" : State == MotionState.Rebalancing ? "REBALANCING" : Blocked ? "PATH BLOCKED"
            : State == MotionState.Starting ? "STARTING" : State == MotionState.Walking ? "APPROACHING"
            : State == MotionState.Stopping ? "STOPPING" : "WATCHING";

        [Serializable] private sealed class Marker { public string id = "", socket = ""; public float time; }
        [Serializable] private sealed class MarkedClip { public string id = ""; public Marker[] events = Array.Empty<Marker>(); }
        [Serializable] private sealed class MarkedLibrary { public MarkedClip[] clips = Array.Empty<MarkedClip>(); }
        private readonly Dictionary<string, ArenaMotionClip> motions = new Dictionary<string, ArenaMotionClip>();
        private readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        private readonly Dictionary<string, Marker[]> markers = new Dictionary<string, Marker[]>();
        private readonly HashSet<int> firedMarkers = new HashSet<int>();
        private ArenaBossBrain owner;
        private ArenaSession session;
        private ArenaAnimation animation;
        private CharacterController capsule;
        private ArenaMotionClip walkMotion;
        private Quaternion heading;
        private float vertical = -2, blockedTime, loopDistance, rebalanceTime, guardSettleTime;
        private int loopsRemaining;
        private bool initialized, suspended, obstructionReported;
        private const int MovementMask = (1 << 8) | (1 << 9);
        private const float Epsilon = .000001f;

        public void Initialize(ArenaBossBrain boss)
        {
            if (initialized)
            {
                if (owner != boss) throw new InvalidOperationException("Foundation motor cannot change its owning actor.");
                return;
            }
            owner = boss != null ? boss : GetComponent<ArenaBossBrain>();
            if (owner == null) throw new InvalidOperationException("Foundation motor needs its actual Boss actor.");
            if (foundation == null) foundation = GetComponent<ArenaProductionClips>();
            if (foundation == null) throw new InvalidOperationException("Foundation clip library is missing.");
            capsule = owner.capsule; animation = owner.animationDriver;
            if (capsule == null || animation == null || owner.fighter == null) throw new InvalidOperationException("Boss bindings are incomplete.");
            foreach (string id in foundation.ids)
            {
                var motion = foundation.Motion(id); var clip = foundation.Clip(id);
                if (clip == null || Mathf.Abs(clip.length - motion.duration) > .002f)
                    throw new InvalidOperationException("Foundation animation/trajectory duration mismatch: " + id);
                // Foundation18 assets are grounded. Airborne skills need their
                // own motor policy and must not silently inherit this gravity.
                foreach (var point in motion.trajectory)
                    if (Mathf.Abs(point.Position.y) > .000001f) throw new InvalidOperationException("Grounded foundation motor received vertical root motion: " + id);
                motions.Add(id, motion); clips.Add(id, clip);
            }
            foreach (string required in new[] { "guard", "walk_start", "walk_loop", "walk_stop", "turn_left_180", "turn_right_180", "hit_light", "death" })
                if (!motions.ContainsKey(required)) throw new InvalidOperationException("Foundation clip missing: " + required);
            walkMotion = motions["walk_loop"];
            if (walkMotion.stride <= 0 || walkMotion.speed <= 0 || Mathf.Abs(walkMotion.EndPosition.z - walkMotion.stride) > .0001f)
                throw new InvalidOperationException("Foundation walk stride is inconsistent.");
            foreach (var point in walkMotion.trajectory)
                if (Mathf.Abs(point.Position.x) > .000001f || Mathf.Abs(point.yaw) > .000001f
                    || Mathf.Abs(point.Position.z - point.time * walkMotion.speed) > .0001f)
                    throw new InvalidOperationException("Distance-driven foundation loop must be straight and constant speed.");
            var exportedMarkers = JsonUtility.FromJson<MarkedLibrary>(ArenaMotionEncoding.Text(foundation.motionData));
            if (exportedMarkers != null && exportedMarkers.clips != null)
                foreach (var marked in exportedMarkers.clips) markers[marked.id] = marked.events ?? Array.Empty<Marker>();
            // These fields also define ArenaAnimation's reset and frozen outgoing
            // walk behavior. Keep the old attack fields and their API untouched.
            animation.guard = clips["guard"]; animation.walk = clips["walk_loop"];
            animation.startShort = clips["walk_start"]; animation.stopShort = clips["walk_stop"];
            animation.walkStride = walkMotion.stride;
            owner.fighter.Damaged += OnDamaged;
            initialized = true; heading = owner.transform.rotation;
        }

        public void ResetMotor()
        {
            RequireInitialized(); State = MotionState.Guard; ClipId = "guard"; StateTime = 0;
            loopsRemaining = 0; loopDistance = blockedTime = rebalanceTime = ActualSpeed = 0;
            guardSettleTime = 0; firedMarkers.Clear();
            suspended = Blocked = obstructionReported = false; vertical = -2; heading = owner.transform.rotation;
        }
        public bool HasClip(string id) => initialized && clips.ContainsKey(id);
        public float StraightSegmentDistance(int loopCount)
        {
            RequireInitialized();
            if (loopCount < 0 || loopCount > 64) throw new ArgumentOutOfRangeException(nameof(loopCount));
            return motions["walk_start"].EndPosition.z + loopCount * walkMotion.stride + motions["walk_stop"].EndPosition.z;
        }
        public bool BeginWalkSegment(int loopCount)
        {
            float distance = StraightSegmentDistance(loopCount);
            if (!CanBegin()) return false;
            heading = owner.transform.rotation;
            // Reserve the complete explicit segment. A new moving obstruction
            // is still handled by the per-step cast and rebalance below.
            if (SweepFraction(heading * Vector3.forward * (distance + .002f)) < .98f)
            { Record("boss_foundation_path_unavailable", "walk_start"); return false; }
            loopsRemaining = loopCount; loopDistance = 0;
            Begin("walk_start", MotionState.Starting, GuardEntryBlend());
            return true;
        }
        public float TurnDuration(int degrees, bool right) => motions.TryGetValue("turn_"+(right?"right_":"left_")+degrees,out var motion)?motion.duration:0;
        public bool TryTurn(int degrees, bool right)
        {
            if (degrees != 15 && degrees != 45 && degrees != 90 && degrees != 180) return false;
            if (!CanBegin()) return false;
            string id = "turn_" + (right ? "right_" : "left_") + degrees;
            if (!motions.TryGetValue(id, out var motion)) return false;
            foreach (var point in motion.trajectory)
                if (point.Position.sqrMagnitude > .0000000001f) return false;
            float expected = right ? degrees : -degrees;
            if (Mathf.Abs(motion.trajectory[motion.trajectory.Length - 1].yaw - expected) > .001f) return false;
            heading = owner.transform.rotation;
            Begin(id, MotionState.Turning, GuardEntryBlend());
            return true;
        }
        public bool TryTurn180(bool right) => TryTurn(180, right);
        public bool TryHitReaction()
        {
            // Full-body hit is only authored from guard. Never cut a planted
            // half-step/turn, interrupt an attack, or queue a belated reaction.
            if (!CanBegin()) return false;
            heading = owner.transform.rotation; Begin("hit_light", MotionState.HitReaction, .14f);
            return true;
        }
        public bool BeginDeath()
        {
            RequireInitialized();
            if (DeathStarted) return false;
            suspended = false; loopsRemaining = 0; heading = owner.transform.rotation;
            Begin("death", MotionState.Dying, .14f);
            return true;
        }
        public void SuspendForAttack()
        {
            RequireInitialized();
            if (!IsGuard) throw new InvalidOperationException("Attack can begin only from stable foundation guard.");
            suspended = true; ActualSpeed = 0;
        }
        public void ResumeGuard(float blend = .14f)
        {
            RequireInitialized();
            if (DeathStarted) return;
            suspended = false; loopsRemaining = 0; heading = owner.transform.rotation;
            Begin("guard", MotionState.Guard, blend);
        }
        public void RequestStop()
        {
            if (!initialized || DeathStarted) return;
            if (State == MotionState.Starting) loopsRemaining = 0;
            else if (State == MotionState.Walking) loopsRemaining = Mathf.Min(1, loopsRemaining);
            // Turning finishes its complete authored angle. No planted yaw snap.
        }

        public void Tick(float delta, ArenaSession arena)
        {
            RequireInitialized(); session = arena;
            if (delta < 0 || delta > .25f || float.IsNaN(delta) || float.IsInfinity(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            if (suspended || delta == 0) return;
            double remaining = delta;
            while (remaining > 1e-10)
            {
                if (State == MotionState.Guard)
                {
                    guardSettleTime = Mathf.Max(0, guardSettleTime - (float)remaining);
                    MoveRequested(Vector3.zero, (float)remaining); sourceClock = (sourceClock + remaining) % motions["guard"].duration;
                    animation.PoseContactClip(clips["guard"], StateTime, (float)remaining, true); return;
                }
                if (State == MotionState.DeathHold)
                {
                    MoveRequested(Vector3.zero, (float)remaining);
                    animation.PoseContactClip(clips["death"], motions["death"].duration, (float)remaining); return;
                }
                if (State == MotionState.Rebalancing)
                {
                    float part = (float)Math.Min(remaining, Mathf.Max(Epsilon, .25f - rebalanceTime));
                    MoveRequested(Vector3.zero, part); rebalanceTime += part; StateTime += part;
                    animation.Pose(clips["guard"], StateTime, part, true, .20f); remaining -= part;
                    if (rebalanceTime >= .25f - Epsilon) { State = MotionState.Guard; Record("boss_foundation_rebalanced", "guard"); }
                    continue;
                }
                var motion = motions[ClipId];
                // Cut at source sample knots. Within each linear source interval,
                // a partial legal displacement maps exactly to a partial time.
                // This retains the authored tiny backward movements in start/stop.
                double knot = (Math.Floor(sourceClock * motion.fps + .0001) + 1) / motion.fps;
                double nextClock = Math.Min(motion.duration, Math.Min(sourceClock + remaining, knot));
                double consumed = nextClock - sourceClock;
                float next = (float)nextClock, partTime = (float)consumed;
                if (partTime <= 0)
                {
                    if (StateTime >= motion.duration - Epsilon) { CompleteMotion(); continue; }
                    throw new InvalidOperationException("Foundation source clock could not advance: " + ClipId);
                }
                motion.Sample(StateTime, out var oldPosition, out _); motion.Sample(next, out var newPosition, out _);
                Vector3 requested = heading * (newPosition - oldPosition);
                Vector3 actual = MoveRequested(requested, partTime);
                float progress = requested.sqrMagnitude <= 1e-12f ? 1 : Mathf.Clamp01(Vector3.Dot(actual, requested) / requested.sqrMagnitude);
                bool obstructed = requested.sqrMagnitude > 1e-10f && progress < .25f;
                Vector3 lateral = actual - heading * Vector3.forward * Vector3.Dot(actual, heading * Vector3.forward);
                if (lateral.sqrMagnitude > .000004f) obstructed = true;
                float previousTime = StateTime;
                if (State == MotionState.Dying || State == MotionState.Turning || State == MotionState.HitReaction) sourceClock = nextClock;
                else if (State == MotionState.Walking)
                {
                    loopDistance += Vector3.Dot(actual, heading * Vector3.forward);
                    StateTime = Mathf.Clamp(loopDistance / walkMotion.stride * walkMotion.duration, 0, next);
                }
                else sourceClock += consumed * progress;
                SetBlocked(obstructed, partTime);
                motion.Sample(StateTime, out _, out float yaw);
                owner.transform.rotation = heading * Quaternion.Euler(0, yaw, 0);
                animation.PoseContactClip(clips[ClipId], StateTime, partTime);
                DispatchFootMarkers(previousTime, StateTime);
                remaining -= consumed;
                if (State == MotionState.Dying && progress < .98f && requested.sqrMagnitude > 1e-10f)
                    Record("boss_death_root_obstructed", ClipId);
                if (blockedTime >= .22f && (State == MotionState.Starting || State == MotionState.Walking || State == MotionState.Stopping))
                { BeginRebalance(); continue; }
                if (StateTime >= motion.duration - Epsilon) CompleteMotion();
            }
        }
        public void ManualTick(float delta, ArenaSession arena) => Tick(delta, arena);

        private void CompleteMotion()
        {
            var completed = State; string id = ClipId;
            StateTime = motions[id].duration; animation.PoseContactClip(clips[id], StateTime, 0);
            if (completed == MotionState.Starting)
            {
                Record("boss_start_finished", id);
                Begin(loopsRemaining > 0 ? "walk_loop" : "walk_stop", loopsRemaining > 0 ? MotionState.Walking : MotionState.Stopping, 0);
            }
            else if (completed == MotionState.Walking)
            {
                loopsRemaining--; Record("boss_walk_cycle_finished", id);
                Begin(loopsRemaining > 0 ? "walk_loop" : "walk_stop", loopsRemaining > 0 ? MotionState.Walking : MotionState.Stopping, 0);
            }
            else if (completed == MotionState.Dying)
            { State = MotionState.DeathHold; ActualSpeed = 0; Record("boss_death_finished", id); }
            else
            {
                Record(completed == MotionState.Stopping ? "boss_stop_finished" : completed == MotionState.Turning ? "boss_turn_finished" : "boss_hit_reaction_finished", id);
                heading = owner.transform.rotation;
                Begin("guard", MotionState.Guard, completed == MotionState.HitReaction ? .14f : 0);
            }
        }
        private float GuardEntryBlend() => StateTime > Epsilon ? .14f : 0;
        private bool CanBegin() => initialized && IsGuard && !owner.Attacking && !owner.fighter.Dead;
        private void Begin(string id, MotionState state, float blend)
        {
            State = state; ClipId = id; StateTime = 0; loopDistance = 0;
            guardSettleTime = state == MotionState.Guard ? blend : 0;
            firedMarkers.Clear();
            blockedTime = 0; Blocked = obstructionReported = false;
            animation.Pose(clips[id], 0, 0, false, blend);
            if (state != MotionState.Guard)
                Record(state == MotionState.Starting ? "boss_start_step" : state == MotionState.Stopping ? "boss_stop_step"
                    : state == MotionState.Turning ? "boss_turn_started" : state == MotionState.HitReaction ? "boss_hit_reaction_started"
                    : state == MotionState.Dying ? "boss_death_started" : "boss_walk_cycle_started", id);
        }
        private void BeginRebalance()
        {
            string interrupted = ClipId;
            loopsRemaining = 0; suspended = false; rebalanceTime = 0; heading = owner.transform.rotation;
            Begin("guard", MotionState.Rebalancing, .20f);
            Record("boss_obstructed_step_settle", interrupted);
        }
        private Vector3 MoveRequested(Vector3 requested, float delta)
        {
            float allowed = SweepFraction(requested);
            if (capsule.isGrounded && vertical < 0) vertical = -2;
            vertical = Mathf.Max(-25, vertical - 24 * delta);
            Vector3 before = owner.transform.position;
            if (capsule.enabled) capsule.Move(requested * allowed + Vector3.up * vertical * delta);
            Vector3 actual = Vector3.ProjectOnPlane(owner.transform.position - before, Vector3.up);
            ActualSpeed = delta > 0 ? actual.magnitude / delta : 0;
            return actual;
        }
        private float SweepFraction(Vector3 requested)
        {
            if (!capsule.enabled) return 0;
            float distance = requested.magnitude;
            if (distance < Epsilon) return 1;
            Vector3 centre = owner.transform.TransformPoint(capsule.center);
            float radius = Mathf.Max(.03f, capsule.radius - capsule.skinWidth * .5f);
            float offset = Mathf.Max(0, capsule.height * .5f - capsule.radius);
            if (Physics.CapsuleCast(centre + Vector3.up * offset, centre - Vector3.up * offset, radius, requested / distance,
                out var hit, distance + .015f, MovementMask, QueryTriggerInteraction.Ignore))
                return Mathf.Clamp01((hit.distance - .012f) / distance);
            return 1;
        }
        private void SetBlocked(bool value, float delta)
        {
            Blocked = value; blockedTime = value ? blockedTime + delta : 0;
            if (blockedTime > .18f && !obstructionReported)
            { obstructionReported = true; Record("boss_locomotion_obstructed", State.ToString()); }
            if (!value && obstructionReported)
            { obstructionReported = false; Record("boss_locomotion_resumed", State.ToString()); }
        }
        private void DispatchFootMarkers(float before, float after)
        {
            if (!markers.TryGetValue(ClipId, out var values)) return;
            for (int i = 0; i < values.Length; i++)
            {
                var marker = values[i];
                if (!(marker.id.StartsWith("footstep", StringComparison.Ordinal) || marker.id == "toe_step")
                    || firedMarkers.Contains(i) || marker.time <= before + .000001f || marker.time > after + .000001f
                    || string.IsNullOrEmpty(marker.socket)) continue;
                firedMarkers.Add(i);
                // Source events supply time; the currently evaluated actual
                // socket supplies position. No second socket-height detector.
                session?.Record("boss_foot_contact", marker.socket.EndsWith(".R", StringComparison.Ordinal) ? "right_step" : "left_step",
                    owner.fighter, 0, owner.Socket(marker.socket).position);
            }
        }
        private void Record(string kind, string detail) => session?.Record(kind, detail, owner.fighter, 0, owner.transform.position);
        private void OnDamaged(ArenaHit hit) { if (!owner.fighter.Dead && !owner.Attacking && IsGuard) TryHitReaction(); }
        private void RequireInitialized() { if (!initialized) throw new InvalidOperationException("Initialize the foundation motor before use."); }
        private void OnDestroy() { if (initialized && owner != null && owner.fighter != null) owner.fighter.Damaged -= OnDamaged; }
    }
}
