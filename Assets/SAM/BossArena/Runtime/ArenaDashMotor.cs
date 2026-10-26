using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    public enum ArenaDashPhase { Inactive, Charge, Slide, LockRelease, Recover, AbortLanding, Complete, AwaitingAbortAsset }
    [Serializable] public sealed class ArenaDashEvent
    {
        public string kind, detail, phase;
        public double battleTime;
        public long executionId, strikeId;
        public float executionTime, phaseTime, headingYaw, pathLength;
        public Vector3 actor, actualVelocity;
    }
    // No Update: the ordinary BossBrain remains the single simulation owner.
    public sealed class ArenaDashMotor : MonoBehaviour
    {
        public ArenaDashClips clips;
        public ArenaDashPresentation presentation;
        [Header("Trial implementation parameters; source markers own animation timing")]
        public float slideSpeed = 7, slideAcceleration = 16, slideTurnSpeed = 160;
        public float nearLockDistance = 2.65f, nearLockAngle = 22, maximumSlideTime = 3, maximumSlidePath = 4;
        public float blockedAbortTime = .25f, fistRadius = .48f, damage = 28, push = 4;
        public bool requirePatrolFeasibility = true;
        public bool LastReleaseRecoveryFeasible { get; private set; }
        public bool LastAbortRecoveryFeasible { get; private set; }
        public string LastReleaseRecoveryFailure { get; private set; } = "";
        public string LastAbortRecoveryFailure { get; private set; } = "";
        public int ReleaseRecoveryChecks { get; private set; }
        public int AbortRecoveryChecks { get; private set; }
        public ArenaDashPhase Phase { get; private set; }
        public string ClipId { get; private set; } = "";
        public float PhaseTime { get; private set; }
        public float ExecutionTime { get; private set; }
        public float PhaseProgress => string.IsNullOrEmpty(ClipId) ? 0 : Mathf.Clamp01(SourceTime / clips.Motion(ClipId).duration);
        public float SourceTime => Phase == ArenaDashPhase.AbortLanding || Phase == ArenaDashPhase.AwaitingAbortAsset ? abortSourceTime : Phase == ArenaDashPhase.Slide ? Mathf.Repeat(PhaseTime, clips.Motion(ClipId).duration) : PhaseTime;
        public bool Active => Phase != ArenaDashPhase.Inactive && Phase != ArenaDashPhase.Complete;
        public bool Initialized { get; private set; }
        public bool HeadingLocked { get; private set; }
        public bool IsSliding => Phase == ArenaDashPhase.Slide;
        public bool IsLocked => HeadingLocked;
        public float StateTime => PhaseTime;
        public Vector3 LockedForward => LockedHeading * Vector3.forward;
        public string LastFailure => Aborted ? Outcome : "";
        public bool StrikeOpen { get; private set; }
        public bool Released { get; private set; }
        public bool Aborted { get; private set; }
        public bool CancelPending => pendingAbort;
        public bool ReleaseSuppressed => releaseSuppressed;
        public string Outcome { get; private set; } = "";
        public Quaternion LockedHeading { get; private set; }
        public Vector3 StateOrigin { get; private set; }
        public Vector3 ActualVelocity { get; private set; }
        public float SlidePathLength { get; private set; }
        public long ExecutionId { get; private set; }
        public long StrikeId { get; private set; }
        public int FistQueryCount { get; private set; }
        public int FlameQueryCount { get; private set; }
        public int MissingFlameGeometrySamples { get; private set; }
        public CollisionFlags LastCollisionFlags { get; private set; }
        public IReadOnlyList<ArenaDashEvent> Events => events;
        public bool Telegraph => Phase == ArenaDashPhase.Charge || Phase == ArenaDashPhase.Slide ||
            (Phase == ArenaDashPhase.LockRelease && PhaseTime < clips.EventTime("dash_lock_release", "strike_open"));
        private ArenaBossBrain owner;
        private ArenaSession arena;
        private ArenaFoundationMotor foundation;
        private ArenaPatrolPlanner patrol;
        private ArenaAnimation animation;
        private ArenaDashPoseResidual residual;
        private readonly List<ArenaDashEvent> events = new List<ArenaDashEvent>();
        private readonly HashSet<string> fired = new HashSet<string>();
        private Quaternion heading;
        private Vector3 velocity, brakingVelocity, previousFist;
        private float blockedTime, vertical = -2, brakeDuration, abortLandingTime, abortSourceTime;
        private bool previousFistValid, airborne, landed, cancelForDeath, abortUsesBrake, landingFailureReported, immediatePresentationCancelled, pendingAbort, releaseSuppressed;
        private const string DamageSkill = "dash_punch";

        public void Initialize(ArenaBossBrain boss)
        {
            if (Initialized) return;
            owner = boss; foundation = boss.FoundationMotor; animation = boss.animationDriver;
            if (clips == null) clips = GetComponent<ArenaDashClips>();
            if (presentation == null) presentation = GetComponent<ArenaDashPresentation>();
            if (clips == null || !clips.Configured) throw new InvalidOperationException("Bind the actual dash source assets before initializing its motor.");
            clips.Validate();
            patrol = GetComponent<ArenaPatrolPlanner>();
            residual = new ArenaDashPoseResidual(owner.GetComponentInChildren<SkinnedMeshRenderer>(), animation);
            brakeDuration = clips.EventTime("dash_lock_release", "brake_settled");
            Initialized = true; ResetMotor();
        }
        public void ResetMotor()
        {
            presentation?.ResetAll(); Phase = ArenaDashPhase.Inactive; ClipId = ""; PhaseTime = ExecutionTime = 0;
            StrikeOpen = Released = HeadingLocked = Aborted = previousFistValid = airborne = landed = cancelForDeath = false;
            velocity = ActualVelocity = brakingVelocity = Vector3.zero; vertical = -2; blockedTime = 0;
            SlidePathLength = 0; Outcome = ""; FistQueryCount = FlameQueryCount = MissingFlameGeometrySamples = 0;
            ExecutionId = StrikeId = 0; LastCollisionFlags = CollisionFlags.None;
            abortLandingTime = abortSourceTime = 0; abortUsesBrake = landingFailureReported = immediatePresentationCancelled = pendingAbort = releaseSuppressed = false;
            residual?.Clear(); fired.Clear(); events.Clear();
            ReleaseRecoveryChecks=AbortRecoveryChecks=0;LastReleaseRecoveryFeasible=LastAbortRecoveryFeasible=false;
            LastReleaseRecoveryFailure=LastAbortRecoveryFailure="";
        }
        public bool TryBegin(ArenaSession session)
        {
            if (!Initialized || Active || !foundation.IsGuard || owner.fighter.Dead || !session.Active) return false;
            arena = session;
            if (requirePatrolFeasibility && (patrol == null || !RecoveryFeasible(owner.transform.position, owner.transform.rotation, Vector3.zero)))
            { Record("dash_start_infeasible", "No feasible complete-motion recovery/patrol plan at the actual start."); return false; }
            ExecutionId = session.NewStrike(); StrikeId = session.NewStrike();
            ExecutionTime = SlidePathLength = 0; Aborted = Released = HeadingLocked = StrikeOpen = false; Outcome = "";
            FistQueryCount = FlameQueryCount = MissingFlameGeometrySamples = 0;
            cancelForDeath = airborne = landed = immediatePresentationCancelled = pendingAbort = releaseSuppressed = false; velocity = Vector3.zero; heading = owner.transform.rotation;
            foundation.SuspendForAttack();
            Begin("dash_charge", ArenaDashPhase.Charge, .14f);
            presentation?.BeginCharge(ExecutionId, owner.Socket("socket_hand.R"), owner.Socket("socket_chest"));
            Record("boss_attack_started", "Continuous approximately two second charge; isolated R2 dash sample.");
            if (!requirePatrolFeasibility) Record("dash_isolated_skill_test", "Patrol feasibility explicitly disabled; this execution cannot certify the integrated patrol loop.");
            return true;
        }
        public void TickPresentation(float delta) { presentation?.Tick(delta); }
        public void Tick(float delta, ArenaSession session)
        {
            if (!Initialized || !Active || delta <= 0) return;
            if (delta > .25f || float.IsNaN(delta) || float.IsInfinity(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            arena = session; float remaining = delta;
            while (remaining > .000001f && Active)
            {
                if (Phase == ArenaDashPhase.AwaitingAbortAsset)
                { MoveDelta(Vector3.zero,remaining,true); residual.RestoreObservedPose(); ExecutionTime+=remaining; return; }
                if (Phase == ArenaDashPhase.AbortLanding)
                { TickAbortLanding(remaining); return; }
                if (Phase == ArenaDashPhase.Slide)
                { float part = Mathf.Min(remaining, 1f / 120); remaining -= TickSlide(part); continue; }
                DispatchMarkers();
                if (!Active) break;
                var motion = clips.Motion(ClipId);
                if (PhaseTime >= motion.duration - .000001f) { FinishClip(); continue; }
                float next = Mathf.Min(motion.duration, PhaseTime + remaining);
                next = Mathf.Min(next, (Mathf.Floor(PhaseTime * motion.fps + .0001f) + 1) / motion.fps);
                foreach (var marker in clips.Events(ClipId))
                    if (!fired.Contains(marker.id) && marker.time > PhaseTime + .000001f) next = Mathf.Min(next, marker.time);
                float partTime = next - PhaseTime;
                if (partTime <= 0) throw new InvalidOperationException("Dash source clock cannot advance.");
                motion.Sample(PhaseTime, out var before, out _); motion.Sample(next, out var after, out float yaw);
                Vector3 request = heading * (after - before);
                if (Phase == ArenaDashPhase.LockRelease || (Phase == ArenaDashPhase.Recover && abortUsesBrake))
                    request += brakingVelocity * BrakeIntegral(PhaseTime, next, Phase==ArenaDashPhase.Recover ? clips.EventTime(ClipId,"brake_settled") : brakeDuration);
                // A zero-height source tail must resume physical floor contact.
                // Keeping `airborne` latched here can leave a nanometre gap with
                // no downward Move, so isGrounded never refreshes after landing.
                bool authoredAir = Phase == ArenaDashPhase.Recover && (after.y > .00001f || before.y > .00001f
                    || (airborne && next < clips.EventTime(ClipId, "hop_land")));
                MoveDelta(request, partTime, !authoredAir);
                ExecutionTime += partTime;
                if (Phase == ArenaDashPhase.LockRelease && request.sqrMagnitude > .000004f &&
                    Vector3.ProjectOnPlane(ActualVelocity * partTime, Vector3.up).magnitude < Vector3.ProjectOnPlane(request, Vector3.up).magnitude * .25f)
                { SuppressLockedRelease("release_obstructed"); }
                PhaseTime = next;
                owner.transform.rotation = heading * Quaternion.Euler(0, yaw, 0);
                Pose(partTime);
                if (Phase == ArenaDashPhase.Recover)
                {
                    bool ceiling = authoredAir && (LastCollisionFlags & CollisionFlags.Above) != 0;
                    bool earlyFloor = (LastCollisionFlags & CollisionFlags.Below) != 0 &&
                        PhaseTime < clips.EventTime(ClipId, "hop_land") - .015f && after.y > .01f;
                    if (ceiling || earlyFloor) { BeginAbortLanding(ceiling ? "hop_ceiling_contact" : "hop_early_ground_contact", false); remaining -= partTime; continue; }
                    if ((LastCollisionFlags & CollisionFlags.Below) != 0 && PhaseTime >= clips.EventTime(ClipId, "hop_land") - .015f) Land();
                }
                if (Phase == ArenaDashPhase.LockRelease)
                {
                    presentation?.UpdateLock(ExecutionId, Mathf.Clamp01(PhaseTime / clips.EventTime("dash_lock_release", "strike_open")));
                    // Sweep the interval ending at strike_close before disabling it.
                    if (StrikeOpen) ResolveStrike();
                }
                else if (Phase == ArenaDashPhase.Charge) presentation?.UpdateCharge(ExecutionId, PhaseProgress);
                DispatchMarkers(); remaining -= partTime;
                if (PhaseTime >= motion.duration - .000001f) FinishClip();
            }
            if (!Active && remaining > .000001f) foundation.Tick(remaining, arena);
        }
        private float TickSlide(float delta)
        {
            Vector3 target = Vector3.ProjectOnPlane(arena.player.transform.position - owner.transform.position, Vector3.up);
            float angle = target.sqrMagnitude > .001f ? Vector3.Angle(owner.transform.forward, target) : 0;
            if (target.magnitude <= nearLockDistance && angle <= nearLockAngle)
            {
                if (requirePatrolFeasibility && !RecoveryFeasible(owner.transform.position, owner.transform.rotation, velocity)) { BeginRecover(true, "near_target_return_infeasible"); return 0; }
                heading = owner.transform.rotation; LockedHeading = heading; HeadingLocked = true; brakingVelocity = velocity;
                Begin("dash_lock_release", ArenaDashPhase.LockRelease, brakeDuration);
                presentation?.Lock(ExecutionId, StateOrigin, heading * Vector3.forward);
                Record("dash_heading_locked", "Captured actual heading once; no impact-frame retarget."); return 0;
            }
            if (PhaseTime >= maximumSlideTime || SlidePathLength >= maximumSlidePath)
            { BeginRecover(true, "pursuit_time_or_path_limit_without_near_lock"); return 0; }
            Quaternion nextHeading = target.sqrMagnitude > .001f ? Quaternion.RotateTowards(owner.transform.rotation, Quaternion.LookRotation(target), slideTurnSpeed * delta) : owner.transform.rotation;
            Vector3 wanted = nextHeading * Vector3.forward * slideSpeed;
            if (target.magnitude < nearLockDistance && angle > nearLockAngle) wanted = Vector3.zero;
            Vector3 nextVelocity = Vector3.MoveTowards(velocity, wanted, slideAcceleration * delta);
            Vector3 requested = nextVelocity * delta;
            float remainingPath = Mathf.Max(0, maximumSlidePath - SlidePathLength);
            if (requested.magnitude > remainingPath) requested = requested.normalized * remainingPath;
            // The next legal position must retain a return plan. Reject before
            // moving; never teleport back to a previously feasible snapshot.
            if (requirePatrolFeasibility && !RecoveryFeasible(owner.transform.position + requested, nextHeading, nextVelocity))
            { BeginRecover(true, "pursuit_return_leash_reached_without_lock"); return 0; }
            owner.transform.rotation = nextHeading; heading = nextHeading;
            MoveDelta(requested, delta, true);
            Vector3 actual = Vector3.ProjectOnPlane(ActualVelocity * delta, Vector3.up);
            SlidePathLength += actual.magnitude; velocity = Vector3.ProjectOnPlane(ActualVelocity, Vector3.up);
            bool blocked = requested.sqrMagnitude > 1e-8f && actual.magnitude < requested.magnitude * .25f;
            blockedTime = blocked ? blockedTime + delta : 0;
            PhaseTime += delta; ExecutionTime += delta; Pose(delta);
            presentation?.UpdateSlide(ExecutionId, ActualVelocity, PhaseProgress);
            if (blockedTime >= blockedAbortTime) BeginRecover(true, "pursuit_blocked_without_lock");
            // There is deliberately no damage query in this entire state.
            return delta;
        }
        private bool RecoveryFeasible(Vector3 position, Quaternion rotation, Vector3 currentVelocity)
        {
            if (patrol == null) return false;
            if (!clips.HasClip("dash_abort_recover")) return false;
            Vector3 abortLanding = position + currentVelocity * (clips.EventTime("dash_abort_recover","brake_settled") * .5f) + rotation * clips.Motion("dash_abort_recover").EndPosition;
            Vector3 releaseLanding = position + currentVelocity*(brakeDuration*.5f)
                + rotation*(clips.Motion("dash_lock_release").EndPosition+clips.Motion("dash_recover").EndPosition);
            // A pursued state must keep both the successful release and a
            // no-punch abort recoverable; abort never receives release travel.
            // A live 120Hz pursuit proposal must not enumerate every complete
            // turn pair inside a displayed frame. A conservative rejection uses
            // the existing full abort animation; accepted plans still run every
            // contact, reach and world check. Preserve the R2 isolated policy.
            bool exhaustive=!owner.ProductionAI;
            ReleaseRecoveryChecks++;
            LastReleaseRecoveryFeasible = patrol.CanPlan(releaseLanding, rotation, arena.player.transform.position, true,exhaustive);
            LastReleaseRecoveryFailure = patrol.LastPredictionFailure;
            AbortRecoveryChecks++;
            LastAbortRecoveryFeasible = patrol.CanPlan(abortLanding, rotation, arena.player.transform.position, true,exhaustive);
            LastAbortRecoveryFailure = patrol.LastPredictionFailure;
            return LastReleaseRecoveryFeasible && LastAbortRecoveryFeasible;
        }
        private static float BrakeIntegral(float from, float to, float duration)
        {
            float a = Mathf.Clamp01(from / duration), b = Mathf.Clamp01(to / duration);
            return duration * (BrakePrimitive(b) - BrakePrimitive(a));
        }
        private static float BrakePrimitive(float x) => x - 2.5f * x * x * x * x + 3 * x * x * x * x * x - x * x * x * x * x * x;
        private void MoveDelta(Vector3 requested, float delta, bool groundGravity)
        {
            var capsule = owner.capsule;
            if (!capsule.enabled) throw new InvalidOperationException("Dash cannot move a disabled collision controller.");
            Vector3 before = owner.transform.position;
            Vector3 displacement = requested;
            if (groundGravity)
            {
                if (capsule.isGrounded && vertical < 0) vertical = -2;
                vertical = Mathf.Max(-25, vertical - 24 * delta); displacement += Vector3.up * vertical * delta;
            }
            // CharacterController.Move owns complete XYZ collision, including
            // ceilings and early floors. Never absolute-transform the actor.
            LastCollisionFlags = capsule.Move(displacement);
            ActualVelocity = (owner.transform.position - before) / delta;
        }
        private void Pose(float delta)
        {
            animation.PoseContactClip(clips.Clip(ClipId), SourceTime, delta, Phase == ArenaDashPhase.Slide);
            residual.Apply(PhaseTime); residual.Observe(delta);
        }
        private void Begin(string id, ArenaDashPhase phase, float residualDuration)
        {
            residual.Begin(clips.Clip(id), residualDuration);
            Phase = phase; ClipId = id; PhaseTime = 0; StateOrigin = owner.transform.position;
            fired.Clear(); previousFistValid = false; blockedTime = 0;
            Record("dash_" + phase.ToString().ToLowerInvariant() + "_started", id);
            Pose(0);
        }
        private void DispatchMarkers()
        {
            foreach (var marker in clips.Events(ClipId))
            {
                if (marker.time > PhaseTime + .000001f || !fired.Add(marker.id)) continue;
                if (Phase == ArenaDashPhase.LockRelease && marker.id == "release_swing" && !releaseSuppressed) presentation?.BeginSwing(ExecutionId);
                else if (Phase == ArenaDashPhase.LockRelease && marker.id == "strike_open")
                {
                    if (releaseSuppressed) { Record("dash_release_suppressed","Cancelled lock action finishes its current leg pose without a fist/flame release."); continue; }
                    StrikeOpen = Released = true;
                    previousFist = owner.FistPosition; previousFistValid = true;
                    presentation?.ReleasePunch(ExecutionId, StrikeId, owner.Socket("socket_hand.R"), LockedHeading * Vector3.forward);
                    Record("dash_strike_open", "Fist and flame share one strike identity."); ResolveStrike();
                }
                else if (Phase == ArenaDashPhase.LockRelease && marker.id == "strike_close")
                { StrikeOpen = previousFistValid = false; Record("dash_strike_closed", "Residual visual flame is nondamaging."); }
                else if (Phase == ArenaDashPhase.Recover && marker.id == "hop_takeoff")
                { airborne = true; landed = false; presentation?.RecoverTakeoff(ExecutionId); Record("dash_takeoff", "Authored XYZ owns vertical motion, no added gravity."); }
                else if (Phase == ArenaDashPhase.Recover && marker.id == "hop_land")
                {
                    if (owner.capsule.isGrounded || (LastCollisionFlags & CollisionFlags.Below) != 0) Land();
                    else Record("dash_planned_land_pending", "Await actual collision; source marker alone is not ground contact.");
                }
            }
        }
        private void ResolveStrike()
        {
            if (!StrikeOpen || !HeadingLocked || Phase != ArenaDashPhase.LockRelease || !arena.Active || owner.fighter.Dead) return;
            Vector3 fist = owner.FistPosition;
            FistQueryCount++;
            arena.StrikeCapsule(owner.fighter, StrikeId, DamageSkill, previousFistValid ? previousFist : fist, fist, fistRadius, damage, push, "fist");
            previousFist = fist; previousFistValid = true;
            if (!StrikeOpen || Phase != ArenaDashPhase.LockRelease || !arena.Active) return;
            if (presentation != null && presentation.TryGetDamageGeometry(ExecutionId, out var a, out var b, out float radius))
            { FlameQueryCount++; arena.StrikeCapsule(owner.fighter, StrikeId, DamageSkill, a, b, radius, damage, push, "flame"); }
            else MissingFlameGeometrySamples++;
        }
        private void FinishClip()
        {
            if (Phase == ArenaDashPhase.Charge)
            {
                if (pendingAbort) { pendingAbort=false; BeginRecover(true,Outcome); return; }
                velocity = Vector3.zero; Begin("dash_slide_loop", ArenaDashPhase.Slide, 0);
                presentation?.BeginSlide(ExecutionId, owner.Socket("socket_foot.L"), owner.Socket("socket_foot.R"));
            }
            else if (Phase == ArenaDashPhase.LockRelease) BeginRecover(releaseSuppressed, releaseSuppressed ? Outcome : "release_complete");
            else if (Phase == ArenaDashPhase.Recover)
            {
                if (!owner.capsule.isGrounded) { BeginAbortLanding("source_recovery_finished_above_actual_ground", false); return; }
                Land(); CompleteExecution();
            }
        }
        private void BeginRecover(bool aborted, string reason)
        {
            bool fromLoaded=Phase==ArenaDashPhase.Charge||Phase==ArenaDashPhase.Slide;
            StrikeOpen = previousFistValid = false; Aborted |= aborted; Outcome = reason;
            if (aborted) Record("dash_abort", reason);
            heading = HeadingLocked ? LockedHeading : owner.transform.rotation;
            brakingVelocity = Vector3.ProjectOnPlane(ActualVelocity, Vector3.up); abortUsesBrake = aborted&&fromLoaded;
            airborne = landed = false;
            string id=fromLoaded?"dash_abort_recover":"dash_recover";
            if(!clips.HasClip(id))
            {
                abortSourceTime=SourceTime;residual.Observe(0);presentation?.Cancel(ExecutionId,true);immediatePresentationCancelled=true;
                Phase=ArenaDashPhase.AwaitingAbortAsset;Aborted=true;Outcome="Dedicated abort source is unavailable; old crossing-leg residual fallback is forbidden.";
                Record("dash_abort_source_unavailable",Outcome);return;
            }
            Begin(id, ArenaDashPhase.Recover, fromLoaded ? clips.EventTime(id,"brake_settled") : 0);
            presentation?.BeginRecover(ExecutionId); Record("boss_recovery_started", reason);
        }
        private void Land()
        {
            if (landed) return; landed = true; airborne = false; vertical = -2;
            if (!immediatePresentationCancelled) presentation?.RecoverLand(ExecutionId);
            Record("dash_landed", "Actual CharacterController ground contact.");
        }
        public void Cancel(string reason, bool forDeath)
        {
            if (!Active) return;
            if (Phase == ArenaDashPhase.AbortLanding && (!forDeath || cancelForDeath)) return;
            if (!forDeath && ((Phase==ArenaDashPhase.Charge&&pendingAbort)||(Phase==ArenaDashPhase.LockRelease&&releaseSuppressed)||Phase==ArenaDashPhase.AwaitingAbortAsset)) return;
            StrikeOpen = previousFistValid = false; Aborted = true; cancelForDeath |= forDeath; Outcome = reason;
            if (forDeath) { presentation?.Cancel(ExecutionId, true); immediatePresentationCancelled = true; }
            Record("dash_cancelled", reason);
            if(forDeath)
            {
                if(!owner.capsule.isGrounded)BeginAbortLanding(reason,true);else CompleteExecution();
                return;
            }
            if(Phase==ArenaDashPhase.Charge)
            {pendingAbort=true;Record("dash_cancel_pending_charge_end","Complete the current charge to its loaded pose before dedicated no-punch recovery.");}
            else if(Phase==ArenaDashPhase.Slide)BeginRecover(true,reason);
            else if(Phase==ArenaDashPhase.LockRelease)SuppressLockedRelease(reason);
            // Existing Recover/AbortLanding continues the actual current leg
            // action. It is never redirected into an incompatible loaded pose.
        }
        private void SuppressLockedRelease(string reason)
        {
            if(releaseSuppressed)return;
            releaseSuppressed=true;Aborted=true;Outcome=reason;StrikeOpen=previousFistValid=false;
            presentation?.SuppressRelease(ExecutionId);
            Record("dash_locked_release_cancelled",reason+"; finish current source legs, then use the original compatible recovery.");
        }
        private void BeginAbortLanding(string reason, bool forDeath)
        {
            StrikeOpen = previousFistValid = false; Aborted = true; cancelForDeath |= forDeath; Outcome = reason;
            if (Phase == ArenaDashPhase.AbortLanding) return;
            abortSourceTime = SourceTime; residual.Observe(0);
            if (!immediatePresentationCancelled) presentation?.Cancel(ExecutionId, true);
            immediatePresentationCancelled = true; Phase = ArenaDashPhase.AbortLanding;
            vertical = Mathf.Min(0, ActualVelocity.y); abortLandingTime = 0; landingFailureReported = false;
            Record("dash_abort_landing", reason);
        }
        private void TickAbortLanding(float delta)
        {
            vertical = Mathf.Max(-25, vertical - 24 * delta);
            MoveDelta(Vector3.up * vertical * delta, delta, false);
            abortLandingTime += delta; ExecutionTime += delta;
            // Keep the last actual airborne pose while falling; never freeze the
            // actor's root in the air or apply the remaining planned hop twice.
            residual.RestoreObservedPose();
            if (owner.capsule.isGrounded || (LastCollisionFlags & CollisionFlags.Below) != 0) { Land(); CompleteExecution(); }
            else if (abortLandingTime > 3 && !landingFailureReported)
            { landingFailureReported = true; Record("dash_no_ground_failure", "Real descent found no floor; execution is not complete."); }
        }
        private void CompleteExecution()
        {
            StrikeOpen = previousFistValid = false; Phase = ArenaDashPhase.Complete;
            if (!immediatePresentationCancelled) presentation?.Complete(ExecutionId);
            Record("dash_recovery_complete", Outcome);
            owner.OnDashFinished(cancelForDeath || owner.fighter.Dead, Aborted);
        }
        private void Record(string kind, string detail)
        {
            if (arena == null) return;
            events.Add(new ArenaDashEvent { kind = kind, detail = detail, phase = Phase.ToString(), executionId = ExecutionId, strikeId = StrikeId,
                battleTime = arena.BattleTime, executionTime = ExecutionTime, phaseTime = PhaseTime, headingYaw = heading.eulerAngles.y,
                pathLength = SlidePathLength, actor = owner.transform.position, actualVelocity = ActualVelocity });
            arena.Record(kind, DamageSkill, owner.fighter, StrikeId, owner.transform.position);
        }
    }

    // Post-PlayableGraph correction for the actual visible variable slide phase.
    // Quintic residual preserves entry pose/velocity and reaches canonical source
    // pose with zero residual velocity at braking end. It is not an IK proof;
    // the actual rigid boots and bone lengths must be checked in runtime evidence.
    internal sealed class ArenaDashPoseResidual
    {
        private readonly SkinnedMeshRenderer mesh;
        private readonly Transform[] bones;
        private readonly ArenaAnimation animation;
        private readonly Vector3[] observedP, velocityP, velocityR, deltaP, deltaV, deltaR, deltaW, entryP;
        private readonly Quaternion[] observedR;
        private readonly Quaternion[] entryR;
        private readonly float[] observedS, velocityS, deltaS, deltaSV, entryS;
        private bool observed;
        private float duration;
        public ArenaDashPoseResidual(SkinnedMeshRenderer renderer, ArenaAnimation driver)
        {
            mesh = renderer; bones = renderer.bones; animation = driver; int n = bones.Length, s = renderer.sharedMesh.blendShapeCount;
            observedP = new Vector3[n]; observedR = new Quaternion[n]; velocityP = new Vector3[n]; deltaP = new Vector3[n];
            velocityR = new Vector3[n]; deltaV = new Vector3[n]; deltaR = new Vector3[n]; deltaW = new Vector3[n]; entryP = new Vector3[n]; entryR = new Quaternion[n];
            observedS = new float[s]; velocityS = new float[s]; deltaS = new float[s]; deltaSV = new float[s]; entryS = new float[s];
        }
        public void Clear()
        {
            observed = false; duration = 0;
            Array.Clear(velocityP, 0, velocityP.Length); Array.Clear(velocityR, 0, velocityR.Length); Array.Clear(velocityS, 0, velocityS.Length);
        }
        public void RestoreObservedPose()
        {
            for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = observedP[i]; bones[i].localRotation = observedR[i]; }
            for (int i = 0; i < observedS.Length; i++) mesh.SetBlendShapeWeight(i, observedS[i]);
        }
        public void Observe(float dt)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (dt > .000001f && observed)
                { velocityP[i] = (bones[i].localPosition - observedP[i]) / dt; velocityR[i] = Log(bones[i].localRotation * Quaternion.Inverse(observedR[i])) / dt; }
                observedP[i] = bones[i].localPosition; observedR[i] = bones[i].localRotation;
            }
            for (int i = 0; i < observedS.Length; i++)
            { float value = mesh.GetBlendShapeWeight(i); if (dt > .000001f && observed) velocityS[i] = (value - observedS[i]) / dt; observedS[i] = value; }
            observed = true;
        }
        public void Begin(AnimationClip clip, float seconds)
        {
            // Capture the actual outgoing result before asking the graph for the
            // canonical next entry. Velocity comes from real prior pose samples.
            Observe(0); duration = seconds;
            animation.PoseContactClip(clip, 0, 0);
            for (int i = 0; i < bones.Length; i++)
            { entryP[i] = bones[i].localPosition; entryR[i] = bones[i].localRotation; deltaP[i] = observedP[i] - entryP[i]; deltaR[i] = Log(observedR[i] * Quaternion.Inverse(entryR[i])); }
            for (int i = 0; i < observedS.Length; i++) { entryS[i] = mesh.GetBlendShapeWeight(i); deltaS[i] = observedS[i] - entryS[i]; }
            const float sampleStep = 1f / 600;
            animation.PoseContactClip(clip, sampleStep, 0);
            for (int i = 0; i < bones.Length; i++)
            { deltaV[i] = velocityP[i] - (bones[i].localPosition - entryP[i]) / sampleStep; deltaW[i] = velocityR[i] - Log(bones[i].localRotation * Quaternion.Inverse(entryR[i])) / sampleStep; }
            for (int i = 0; i < observedS.Length; i++) deltaSV[i] = velocityS[i] - (mesh.GetBlendShapeWeight(i) - entryS[i]) / sampleStep;
            animation.PoseContactClip(clip, 0, 0);
            Apply(0);
        }
        public void Apply(float time)
        {
            if (duration <= 0 || time >= duration) return;
            float x = Mathf.Clamp01(time / duration), x2 = x * x, x3 = x2 * x, x4 = x3 * x, x5 = x4 * x;
            float p = 1 - 10 * x3 + 15 * x4 - 6 * x5, v = duration * (x - 6 * x3 + 8 * x4 - 3 * x5);
            for (int i = 0; i < bones.Length; i++)
            { bones[i].localPosition += deltaP[i] * p + deltaV[i] * v; bones[i].localRotation = Exp(deltaR[i] * p + deltaW[i] * v) * bones[i].localRotation; }
            for (int i = 0; i < observedS.Length; i++) mesh.SetBlendShapeWeight(i, Mathf.Clamp(mesh.GetBlendShapeWeight(i) + deltaS[i] * p + deltaSV[i] * v, 0, 100));
        }
        private static Vector3 Log(Quaternion q) { if (q.w < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); q.ToAngleAxis(out float angle, out var axis); return angle < .00001f ? Vector3.zero : axis * angle; }
        private static Quaternion Exp(Vector3 v) => v.sqrMagnitude < 1e-12f ? Quaternion.identity : Quaternion.AngleAxis(v.magnitude, v.normalized);
    }
}
