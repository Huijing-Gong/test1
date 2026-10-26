using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    public enum ArenaAttack { Jab, Stomp, ComboTwo, ComboThree, DashPunch, Ignite, DiveKick, Meteor, FireWave, FireBird, LightningCall, GroundRift, SwordFall, ThunderOrb, DelayedBlast, BoxLeft, BoxRight, BoxStepLeft, BoxStepRight, BoxHeavy, BoxJab, BoxCross, BoxHook }

    [RequireComponent(typeof(CharacterController), typeof(ArenaFighter))]
    public sealed class ArenaBossBrain : MonoBehaviour
    {
        public ArenaEclipseBossMotor v13Motor;
        public ArenaFighter fighter;
        public CharacterController capsule;
        public ArenaAnimation animationDriver;
        public Transform visual;
        public TextAsset motionData;
        public bool decisionEnabled = true;
        [Tooltip("R2 complete dash sample only. This is not the final two-phase R4 AI.")]
        public bool useDashSampleAI;
        [Tooltip("Production two-phase pool, using the same authored patrol intervals.")]
        public bool useTwoPhaseAI;
        public bool ProductionAI => v13Motor != null ? decisionEnabled : useTwoPhaseAI && !useDashSampleAI && decisionEnabled;
        private bool PatrolAI => useDashSampleAI || useTwoPhaseAI;
        private bool samIgnitionPending;
        public bool IgnitionPending { get => v13Motor != null ? v13Motor.IgnitionPending : samIgnitionPending; private set => samIgnitionPending = value; }
        public int CombatPhase => v13Motor != null ? v13Motor.CombatPhase : igniteMotor != null && igniteMotor.Completed ? 2 : 1;
        private bool halfHealthRecorded;
        private int poolCursor;
        public float walkSpeed = 1.68073654f, acceleration = 4, turnSpeed = 95;
        public float jabDistance = 1.85f, stompDistance = 2.8f;
        public float jabDamage = 18, stompDamage = 28;
        public float jabRadius = .48f, stompRadius = 3.25f;
        public Vector3 stompLocalContact = new Vector3(.350594f, 0, .327716f);
        public string ActionName => v13Motor != null ? v13Motor.ActionName : meteorMotor != null && meteorMotor.Active ? "METEOR FINALE" : diveMotor != null && diveMotor.Active ? "ASCENDING DIVE KICK" : igniteMotor != null && igniteMotor.Active ? "FURNACE IGNITION" : attacking ? current == ArenaAttack.DashPunch ? "PURSUIT DRIVE PUNCH" : current == ArenaAttack.Stomp ? "FURNACE STOMP" : current == ArenaAttack.ComboThree ? "OVERHEAT COMBINATION" : current == ArenaAttack.ComboTwo ? "DRIVE COMBINATION" : "DRIVE PUNCH" : fighter != null && fighter.Dead ? "DISABLED" : "FURNACE SENTINEL";
        public string StateName => v13Motor != null ? v13Motor.StateName : fighter != null && fighter.Dead ? "DOWN"
            : dashMotor != null && dashMotor.Phase == ArenaDashPhase.AwaitingAbortAsset ? "DASH SOURCE UNAVAILABLE"
            : attacking ? Telegraph ? "WINDUP" : IsRecovering ? "RECOVERY" : "ATTACK"
            : PatrolAI && patrolPlanner != null && !patrolPlanner.Available ? "PATROL UNAVAILABLE"
            : PatrolAI && patrolPlanner != null && patrolPlanner.RecoveryActive ? "RETURNING"
            : PatrolAI && patrolPlanner != null && patrolPlanner.Invalid ? "PATROL INFEASIBLE"
            : PatrolAI && patrolPlanner != null && patrolPlanner.Active ? "PATROL"
            : foundationMotor != null ? foundationMotor.DisplayState
            : bridgeSettleTime > 0 ? "REBALANCING" : movementBlocked ? "PATH BLOCKED" : locomotion == Locomotion.Starting ? "STARTING"
            : locomotion == Locomotion.Stopping ? "STOPPING" : locomotion == Locomotion.Walking ? "APPROACHING" : "WATCHING";
        public bool Telegraph => v13Motor != null ? v13Motor.Telegraph : meteorMotor != null && meteorMotor.Active ? meteorMotor.Telegraph : diveMotor != null && diveMotor.Active ? diveMotor.Telegraph : igniteMotor != null && igniteMotor.Active ? false : stompMotor != null && stompMotor.Active ? stompMotor.Telegraph : attacking && (current == ArenaAttack.DashPunch ? dashMotor != null && dashMotor.Telegraph : (skillTime < (current == ArenaAttack.Stomp ? 1f : .43f)
            || (current == ArenaAttack.ComboThree && skillTime >= 1.25f && skillTime < 1.766667f)));
        public bool IsRecovering => v13Motor != null ? v13Motor.IsRecovering : meteorMotor != null && meteorMotor.Active ? meteorMotor.Recovering : diveMotor != null && diveMotor.Active ? diveMotor.Recovering : igniteMotor != null && igniteMotor.Active ? false : stompMotor != null && stompMotor.Active ? stompMotor.Recovering : attacking && (current == ArenaAttack.DashPunch ? dashMotor.Phase == ArenaDashPhase.Recover || dashMotor.Phase == ArenaDashPhase.AbortLanding : skillTime >= RecoveryTime);
        public float RecoveryTime => v13Motor != null ? v13Motor.RecoveryTime : stompMotor != null && stompMotor.Active ? stompMotor.Settings.stampTime : current == ArenaAttack.Stomp ? 1.14f : current == ArenaAttack.ComboTwo ? 1.133333f : current == ArenaAttack.ComboThree ? 1.933333f : .60f;
        private int samComboHitIndex;
        public int ComboHitIndex { get => v13Motor != null ? v13Motor.ComboHitIndex : samComboHitIndex; private set => samComboHitIndex = value; }
        public Vector3 GroundCentre => v13Motor != null ? v13Motor.GroundCentre : meteorMotor != null && meteorMotor.Active ? meteorMotor.Centre : diveMotor != null && diveMotor.Active ? diveMotor.Centre : stompMotor != null && stompMotor.Active ? stompMotor.Centre : groundCentre;
        public float SkillProgress => v13Motor != null ? v13Motor.SkillProgress : meteorMotor != null && meteorMotor.Active ? meteorMotor.SourceTime/meteorMotor.Settings.completeSeconds : diveMotor != null && diveMotor.Active ? diveMotor.SourceTime / diveMotor.Settings.completeSeconds : igniteMotor != null && igniteMotor.Active ? igniteMotor.SourceTime / igniteMotor.Settings.completeSeconds : stompMotor != null && stompMotor.Active ? stompMotor.Progress : attacking ? current == ArenaAttack.DashPunch ? dashMotor.PhaseProgress : Mathf.Clamp01(skillTime / Duration) : 0;
        public float SkillTime => v13Motor != null ? v13Motor.SkillTime : meteorMotor != null && meteorMotor.Active ? meteorMotor.SourceTime : diveMotor != null && diveMotor.Active ? diveMotor.SourceTime : igniteMotor != null && igniteMotor.Active ? igniteMotor.SourceTime : stompMotor != null && stompMotor.Active ? stompMotor.SourceTime : attacking && current == ArenaAttack.DashPunch ? dashMotor.ExecutionTime : skillTime;
        public bool Attacking => v13Motor != null ? v13Motor.Attacking : attacking;
        public ArenaAttack CurrentAttack => v13Motor != null ? v13Motor.CurrentAttack : current;
        public float ActualSpeed { get => v13Motor != null ? v13Motor.ActualSpeed : attacking && current == ArenaAttack.DashPunch ? Vector3.ProjectOnPlane(dashMotor.ActualVelocity, Vector3.up).magnitude : foundationMotor != null && !attacking ? foundationMotor.ActualSpeed : actualSpeed; private set => actualSpeed = value; }
        public float WalkPhase => v13Motor != null ? v13Motor.WalkPhase : foundationMotor != null ? foundationMotor.WalkPhase : (float)(walkCycles - Math.Floor(walkCycles));
        public float LocomotionTime => v13Motor != null ? v13Motor.LocomotionTime : foundationMotor != null ? foundationMotor.StateTime : bridgeTime;
        public bool LocomotionBlocked => v13Motor != null ? v13Motor.Blocked : foundationMotor != null ? foundationMotor.Blocked : movementBlocked;
        public ArenaFoundationMotor FoundationMotor => foundationMotor;
        public ArenaDashMotor DashMotor => dashMotor;
        public ArenaStompMotor StompMotor => stompMotor;
        public ArenaIgniteMotor IgniteMotor => igniteMotor;
        public ArenaDiveMotor DiveMotor => diveMotor;
        public ArenaMeteorMotor MeteorMotor => meteorMotor;
        public ArenaPatrolPlanner PatrolPlanner => patrolPlanner;
        public Vector3 FistPosition => Socket("socket_hand.R").position;
        public Vector3 FootPosition => Socket("socket_foot.R").position;
        private readonly Dictionary<string, Transform> sockets = new Dictionary<string, Transform>();
        private ArenaAttack current;
        private bool attacking, impactReleased, recoveryReported;
        private float skillTime, speed, vertical = -2, cooldown = .6f, idleTime, deathTime;
        private double walkCycles;
        private long strike, attackGroup;
        private int attackCount;
        private Vector3 groundCentre, visualPosition;
        private Quaternion visualRotation;
        private ArenaSession session;
        private ArenaFoundationMotor foundationMotor;
        private ArenaDashMotor dashMotor;
        private ArenaStompMotor stompMotor;
        private ArenaIgniteMotor igniteMotor;
        private ArenaDiveMotor diveMotor;
        private ArenaMeteorMotor meteorMotor;
        private ArenaPatrolPlanner patrolPlanner;
        private bool needsPatrol;
        private float actualSpeed, foundationDecisionDelay;
        private enum Locomotion { Guard, Starting, Walking, Stopping }
        private Locomotion locomotion;
        private ArenaMotionClip startMotion, stopMotion;
        private float bridgeTime, blockedTime, bridgeSettleTime;
        private Quaternion bridgeHeading;
        private bool movementBlocked, blockedReported;
        private int turnSide = 1;
        private const int MovementMask = (1 << 8) | (1 << 9);
        private AnimationClip AttackClip => current == ArenaAttack.Stomp ? animationDriver.stomp : current == ArenaAttack.ComboTwo ? animationDriver.comboTwo : current == ArenaAttack.ComboThree ? animationDriver.comboThree : animationDriver.jab;
        private float Duration => AttackClip.length;
        private int PunchCount => current == ArenaAttack.ComboThree ? 3 : current == ArenaAttack.ComboTwo ? 2 : 1;
        private static readonly float[] PunchStarts = { .43f, 1.016667f, 1.766667f };
        private static readonly float[] PunchEnds = { .58f, 1.116667f, 1.916667f };
        private static readonly float[] SwingStarts = { .30f, .90f, 1.60f };
        private static readonly string[] PunchSkills = { "drive_punch", "cross_punch", "hammer_punch" };
        private readonly Vector3[] previousHands = new Vector3[2];
        private readonly bool[] liftedFeet = new bool[2];

        public bool ForceAttack(ArenaAttack attack, ArenaSession arena = null)
        {
            if (arena != null) session = arena;
            return v13Motor != null ? v13Motor.ForceAttack(attack, session) : TrySkill(attack);
        }

        private void Awake()
        {
            if (fighter == null) fighter = GetComponent<ArenaFighter>();
            if (capsule == null) capsule = GetComponent<CharacterController>();
            if (animationDriver == null) animationDriver = GetComponentInChildren<ArenaAnimation>();
            if (visual == null && animationDriver != null) visual = animationDriver.transform;
            foreach (var t in GetComponentsInChildren<Transform>(true)) sockets[t.name] = t;
            if (visual != null) { visualPosition = visual.localPosition; visualRotation = visual.localRotation; }
            if (v13Motor == null) v13Motor = GetComponent<ArenaEclipseBossMotor>();
            if (v13Motor != null) { v13Motor.Initialize(this); return; }
            foundationMotor = GetComponent<ArenaFoundationMotor>();
            if (foundationMotor != null)
            {
                foundationMotor.Initialize(this);
                walkSpeed = foundationMotor.foundation.Motion("walk_loop").speed;
                patrolPlanner = GetComponent<ArenaPatrolPlanner>();
                patrolPlanner?.Initialize(this);
                dashMotor = GetComponent<ArenaDashMotor>();
                if (dashMotor != null) dashMotor.Initialize(this);
                stompMotor=GetComponent<ArenaStompMotor>();
                stompMotor?.Initialize(this);
                igniteMotor=GetComponent<ArenaIgniteMotor>();igniteMotor?.Initialize(this);
                diveMotor=GetComponent<ArenaDiveMotor>();diveMotor?.Initialize(this);
                meteorMotor=GetComponent<ArenaMeteorMotor>();meteorMotor?.Initialize(this);
                return;
            }
            var trajectories = ArenaMotionData.Read(motionData);
            startMotion = trajectories.Get("start_short"); stopMotion = trajectories.Get("stop_short");
            var walking = trajectories.Get("walk_loop");
            animationDriver.walkStride = walking.stride;
            walkSpeed = walking.speed;
            if (animationDriver.startShort == null || animationDriver.stopShort == null
                || Mathf.Abs(animationDriver.startShort.length - startMotion.duration) > .001f
                || Mathf.Abs(animationDriver.stopShort.length - stopMotion.duration) > .001f)
                throw new InvalidOperationException("Boss Arena start/stop clips must match the sampled trajectory durations.");
        }

        public Transform Socket(string name)
        {
            if (v13Motor != null) return v13Motor.Socket(name);
            if (sockets.TryGetValue(name, out var value)) return value;
            throw new InvalidOperationException("Missing Boss gameplay socket: " + name);
        }

        public void ResetBoss(Vector3 position, Quaternion rotation)
        {
            if (v13Motor != null) { v13Motor.ResetMotor(position, rotation); return; }
            bool wasEnabled = capsule.enabled;
            capsule.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            capsule.enabled = wasEnabled;
            if (wasEnabled)
            {
                // Spawn height includes clearance. Resolve it against the real
                // floor before guard or the first committed support-foot step.
                Physics.SyncTransforms();
                capsule.Move(Vector3.down * .25f);
            }
            speed = skillTime = idleTime = deathTime = ActualSpeed = 0;
            cooldown = .65f;
            vertical = -2;
            walkCycles = 0;
            attackCount = 0;
            attacking = impactReleased = false;
            recoveryReported = false; ComboHitIndex = -1;
            attackGroup = 0; liftedFeet[0] = liftedFeet[1] = false;
            locomotion = Locomotion.Guard;
            bridgeTime = blockedTime = bridgeSettleTime = 0;
            movementBlocked = blockedReported = false;
            turnSide = 1;
            foundationDecisionDelay = 0;
            if (visual != null) { visual.localPosition = visualPosition; visual.localRotation = visualRotation; }
            foundationMotor?.ResetMotor();
            dashMotor?.ResetMotor();
            stompMotor?.ResetMotor();
            igniteMotor?.ResetMotor();
            diveMotor?.ResetMotor();
            meteorMotor?.ResetMotor();
            patrolPlanner?.ResetPlanner(); needsPatrol = PatrolAI;
            IgnitionPending = halfHealthRecorded = false; poolCursor = 0;
            animationDriver.ResetPose();
        }

        public void CancelAttack()
        {
            if (v13Motor != null) { v13Motor.CancelAttack(); return; }
            if(meteorMotor != null && meteorMotor.Active) { meteorMotor.Cancel(fighter.Dead); return; }
            if(diveMotor != null && diveMotor.Active) { diveMotor.Cancel(fighter.Dead); return; }
            if(igniteMotor != null && igniteMotor.Active) { igniteMotor.Cancel(fighter.Dead); return; }
            if(stompMotor != null && stompMotor.Active) { stompMotor.Cancel(fighter.Dead); return; }
            if (patrolPlanner != null && patrolPlanner.Motor != null && (patrolPlanner.Active || patrolPlanner.Motor.Active))
            {
                patrolPlanner.Cancel(session, fighter.Dead ? "boss_died" : "arena_or_attack_cancelled", fighter.Dead);
                return;
            }
            if (current == ArenaAttack.DashPunch && dashMotor != null && dashMotor.Active)
            {
                dashMotor.Cancel(fighter.Dead ? "boss_died" : "arena_or_attack_cancelled", fighter.Dead);
                session?.effects?.HideWarning();
                return;
            }
            bool wasAttacking = attacking;
            session?.sound?.CancelGroup(attackGroup);
            attacking = impactReleased = false;
            speed = 0;
            locomotion = Locomotion.Guard;
            bridgeTime = blockedTime = bridgeSettleTime = 0;
            movementBlocked = blockedReported = false;
            session?.effects?.HideWarning();
            if (foundationMotor != null)
            {
                if (fighter.Dead) foundationMotor.BeginDeath();
                else if (wasAttacking) foundationMotor.ResumeGuard();
                else foundationMotor.RequestStop();
            }
        }

        public bool TrySkill(ArenaAttack attack)
        {
            if (v13Motor != null) return v13Motor.ForceAttack(attack, session);
            if (session == null || !session.Active || fighter.Dead || attacking || bridgeSettleTime > 0 || locomotion != Locomotion.Guard) return false;
            if (foundationMotor != null && !foundationMotor.IsGuard) return false;
            if (attack == ArenaAttack.Meteor)
            {
                if(meteorMotor==null || !meteorMotor.TryBegin(session))return false;
                current=attack;attacking=true;skillTime=0;attackCount++;return true;
            }
            if (attack == ArenaAttack.DiveKick)
            {
                if(diveMotor==null || !diveMotor.TryBegin(session))return false;
                current=attack;attacking=true;skillTime=0;attackCount++;return true;
            }
            if (attack == ArenaAttack.Ignite)
            {
                if(igniteMotor==null || !igniteMotor.TryBegin(session))return false;
                current=attack;attacking=true;skillTime=0;attackCount++;return true;
            }
            if (attack == ArenaAttack.Stomp && stompMotor != null)
            {
                if(!stompMotor.TryBegin(session))return false;
                current=attack;attacking=true;skillTime=0;attackCount++;return true;
            }
            if (attack == ArenaAttack.DashPunch)
            {
                if (dashMotor == null || !dashMotor.TryBegin(session)) return false;
                current = attack; attacking = true; skillTime = 0; attackCount++;
                return true;
            }
            foundationMotor?.SuspendForAttack();
            current = attack;
            attacking = true;
            impactReleased = false;
            recoveryReported = false; ComboHitIndex = -1;
            skillTime = speed = 0;
            strike = session.NewStrike();
            attackGroup = strike;
            attackCount++;
            animationDriver.Pose(AttackClip, 0, 0, false, .14f);
            previousHands[0] = FistPosition; previousHands[1] = Socket("socket_hand.L").position;
            groundCentre = transform.position;
            if (current == ArenaAttack.Stomp)
                groundCentre = transform.TransformPoint(stompLocalContact);
            session.effects?.Charge(current == ArenaAttack.Stomp ? Socket("socket_foot.R") : Socket("socket_hand.R"));
            session.Record("boss_attack_started", current.ToString(), fighter, strike, transform.position);
            return true;
        }

        public void Tick(float delta, ArenaSession arena)
        {
            session = arena;
            if (v13Motor != null) { v13Motor.Tick(delta, arena); return; }
            if (ProductionAI && arena.Active && !fighter.Dead && !halfHealthRecorded
                && fighter.Health <= fighter.maximumHealth * .5f)
            {
                halfHealthRecorded = true;
                IgnitionPending = igniteMotor != null && igniteMotor.BeginCount == 0;
                if (IgnitionPending) arena.Record("half_health_ignition_queued", "phase_ignite", fighter, 0, transform.position);
            }
            idleTime += delta;
            dashMotor?.TickPresentation(delta);
            igniteMotor?.TickPresentation(delta);
            if(meteorMotor != null && meteorMotor.Active) { meteorMotor.Tick(delta,arena); return; }
            if(diveMotor != null && diveMotor.Active) { diveMotor.Tick(delta,arena); return; }
            if(igniteMotor != null && igniteMotor.Active) { igniteMotor.Tick(delta,arena); return; }
            if(stompMotor != null && stompMotor.Active) { stompMotor.Tick(delta,arena); return; }
            if (dashMotor != null && dashMotor.Active)
            {
                if (fighter.Dead) dashMotor.Cancel("boss_died", true);
                else if (!arena.Active && dashMotor.Phase != ArenaDashPhase.Recover && dashMotor.Phase != ArenaDashPhase.AbortLanding)
                    dashMotor.Cancel("arena_inactive", false);
                if (dashMotor.Active) dashMotor.Tick(delta, arena);
                return;
            }
            if (patrolPlanner != null && patrolPlanner.Motor != null && (patrolPlanner.Active || patrolPlanner.Motor.Active))
            {
                if (!arena.Active || fighter.Dead || !decisionEnabled || !PatrolAI)
                    patrolPlanner.Cancel(arena, fighter.Dead ? "boss_died" : "patrol_controller_or_arena_cancelled", fighter.Dead);
                patrolPlanner.Tick(delta, arena);
                return;
            }
            if (foundationMotor != null) { FoundationTick(delta, arena); return; }
            if (!arena.Active || fighter.Dead)
            {
                if (attacking) CancelAttack();
                locomotion = Locomotion.Guard;
                movementBlocked = false;
                animationDriver.Pose(animationDriver.guard, idleTime, delta, true);
                Move(Vector3.zero, delta);
                if (fighter.Dead && visual != null)
                {
                    deathTime += delta;
                    float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(deathTime / 1.25f));
                    visual.localRotation = visualRotation * Quaternion.Euler(0, 0, 65 * t);
                    visual.localPosition = visualPosition + new Vector3(0, -.24f * t, 0);
                }
                return;
            }
            if (attacking) { AttackTick(delta); return; }
            cooldown = Mathf.Max(0, cooldown - delta);
            if(bridgeSettleTime > 0)
            {
                bridgeSettleTime = Mathf.Max(0, bridgeSettleTime - delta);
                Move(Vector3.zero, delta);
                animationDriver.Pose(animationDriver.guard, idleTime, delta, true, .20f);
                return;
            }
            Vector3 toPlayer = ToPlayer();
            float distance = toPlayer.magnitude;
            bool nextStomp = attackCount % 3 == 1;
            float wantedRange = nextStomp ? stompDistance : jabDistance;
            float angle = toPlayer.sqrMagnitude < .001f ? 0 : Vector3.Angle(transform.forward, toPlayer);
            if (locomotion == Locomotion.Starting || locomotion == Locomotion.Stopping)
            { BridgeTick(delta); return; }
            if (locomotion == Locomotion.Walking)
            { WalkTick(delta, wantedRange); return; }

            speed = 0;
            Move(Vector3.zero, delta);
            animationDriver.PoseContactClip(animationDriver.guard, idleTime, delta, true);
            // A player underneath the fists or tight against the flank is a stomp
            // target. This readable radial attack does not require a planted-foot
            // spin or a committed forward step through the player's collider.
            if(decisionEnabled && distance<=stompDistance && (distance<1.15f || angle>=24))
            {
                SetBlocked(false,delta);
                if(cooldown<=0)TrySkill(ArenaAttack.Stomp);
                return;
            }
            if (decisionEnabled && cooldown <= 0 && distance <= wantedRange + .15f && angle < 24)
            { TrySkill(nextStomp ? ArenaAttack.Stomp : ArenaAttack.ComboTwo); return; }
            if (decisionEnabled && (distance > wantedRange + .12f || angle > 24))
            {
                // Avoid starting a committed planted-foot bridge into a known wall.
                Vector3 startAndStop = transform.rotation * (startMotion.EndPosition + stopMotion.EndPosition);
                if (SweepFraction(startAndStop) > .98f)
                { BeginBridge(true); BridgeTick(delta); }
                else SetBlocked(true, delta);
            }
            else SetBlocked(false, delta);
        }

        internal void OnGroundSkillFinished(bool dead) => OnDashFinished(dead, false);

        internal void OnDashFinished(bool dead, bool aborted)
        {
            attacking = false; locomotion = Locomotion.Guard; idleTime = 0; skillTime = 0;
            session?.effects?.HideWarning();
            if (dead) { foundationMotor.BeginDeath(); return; }
            foundationMotor.ResumeGuard(0);
            needsPatrol = true;
        }
        private void TickDashSample(float delta, ArenaSession arena)
        {
            if (patrolPlanner == null)
            {
                foundationMotor.Tick(delta, arena);
                return; // Missing explicit route binding cannot masquerade as a patrol pass.
            }
            if (patrolPlanner.Active) { patrolPlanner.Tick(delta, arena); return; }
            if (patrolPlanner.Invalid)
            {
                patrolPlanner.TickRecoveryWatchdog(arena);
                patrolPlanner.TryReconsider(arena);
                if(patrolPlanner.Active)patrolPlanner.Tick(delta,arena);
                else foundationMotor.Tick(delta,arena);
                return;
            }
            if (needsPatrol)
            {
                if (!foundationMotor.IsGuard) { foundationMotor.Tick(delta, arena); return; }
                needsPatrol = false;
                if (patrolPlanner.Begin(arena)) patrolPlanner.Tick(delta, arena);
                else foundationMotor.Tick(delta, arena);
                return;
            }
            if (patrolPlanner.Complete && foundationMotor.IsGuard)
            {
                Vector3 toPlayer = ToPlayer();
                // A moving target cannot revoke physically completed steps.
                // Dash turns in its slide, aerial skills turn after takeoff,
                // and radial/ignition skills do not need a planted yaw snap.
                if (ProductionAI || toPlayer.sqrMagnitude < .001f || Vector3.Angle(transform.forward, toPlayer) <= patrolPlanner.finalFacingTolerance)
                {
                    if (!(ProductionAI ? TryProductionSkill(arena) : TrySkill(ArenaAttack.DashPunch)))
                        patrolPlanner.Invalidate(arena, "Actual patrol endpoint cannot start a dash with a viable recovery/return. No charge or patrol pass is inferred.");
                }
                else patrolPlanner.Invalidate(arena, "Player moved outside the planned final facing tolerance; the original patrol budget has ended.");
            }
            foundationMotor.Tick(delta, arena);
        }

        private bool TryProductionSkill(ArenaSession arena)
        {
            // The completed interval is consumed once. A half-health event never
            // restarts it, and no ordinary attack can overtake the queued ignition.
            if (IgnitionPending)
            {
                if (!TrySkill(ArenaAttack.Ignite)) return false;
                IgnitionPending = false; poolCursor = 0;
                arena.Record("queued_ignition_dispatched", "phase_ignite", fighter, 0, transform.position);
                return true;
            }
            var pool = CombatPhase == 2 ? PhaseTwoPool : PhaseOnePool;
            for (int offset = 0; offset < pool.Length; offset++)
            {
                int selected = (poolCursor + offset) % pool.Length;
                if (!TrySkill(pool[selected])) continue;
                poolCursor = (selected + 1) % pool.Length;
                arena.Record("ai_skill_selected", pool[selected].ToString(), fighter, 0, transform.position);
                return true;
            }
            return false;
        }
        private static readonly ArenaAttack[] PhaseOnePool = { ArenaAttack.DashPunch, ArenaAttack.DiveKick, ArenaAttack.Stomp };
        private static readonly ArenaAttack[] PhaseTwoPool = { ArenaAttack.Meteor, ArenaAttack.DashPunch, ArenaAttack.Stomp, ArenaAttack.DiveKick };

        // Pick an actual landing that can continue the fixed route without a
        // teleport or an extra unbudgeted walk. The caller also moves its warning
        // and damage centre when the attack is tied to a physical foot contact.
        public bool TryRouteLanding(Vector3 desired, Vector3 fallback, Quaternion heading, out Vector3 landing)
        {
            landing = desired;
            if (!ProductionAI) return true;
            if (patrolPlanner == null || session == null) return false;
            // Three original sample locations bound the synchronous landing
            // decision. Rejection remains conservative; every accepted point
            // still passes the full current contact/world preflight.
            string failures="";
            foreach (float fraction in new[] { 0f, .5f, 1f })
            {
                Vector3 candidate = Vector3.Lerp(desired, fallback, fraction);
                candidate.y = fallback.y;
                if (patrolPlanner.DistanceFromRoute(candidate) > patrolPlanner.maximumRouteDistance) continue;
                if (!patrolPlanner.CanPlan(candidate, heading, session.player.transform.position, true, false))
                { failures += "fraction="+fraction+" "+patrolPlanner.LastPredictionFailure+" | ";continue; }
                landing = candidate;
                if(fraction>0)session.Record("boss_landing_recovery_fallback","Fresh accepted bounded landing fraction="+fraction+"; prior rejection: "+failures,fighter,0,landing);
                return true;
            }
            session.Record("boss_landing_search_rejected",failures,fighter,0,fallback);
            return false;
        }

        private void FoundationTick(float delta, ArenaSession arena)
        {
            // Victory is raised by the health event before the next tick. The
            // actual death clip must keep playing after the arena stops fighting.
            if (fighter.Dead || foundationMotor.DeathStarted)
            {
                if (attacking) CancelAttack();
                if (!foundationMotor.DeathStarted) foundationMotor.BeginDeath();
                foundationMotor.Tick(delta, arena);
                return;
            }
            if (!arena.Active)
            {
                if (attacking) CancelAttack();
                foundationMotor.RequestStop();
                foundationMotor.Tick(delta, arena);
                return;
            }
            if (attacking) { AttackTick(delta); return; }
            if (PatrolAI && dashMotor != null && decisionEnabled)
            { TickDashSample(delta, arena); return; }
            cooldown = Mathf.Max(0, cooldown - delta);
            foundationDecisionDelay = Mathf.Max(0, foundationDecisionDelay - delta);
            if (decisionEnabled && foundationMotor.IsGuard && foundationDecisionDelay <= 0)
                ChooseFoundationAction();
            if (attacking) AttackTick(delta);
            else foundationMotor.Tick(delta, arena);
        }

        private void ChooseFoundationAction()
        {
            Vector3 toPlayer = ToPlayer();
            float distance = toPlayer.magnitude;
            float signedAngle = distance < .001f ? 0 : Vector3.SignedAngle(transform.forward, toPlayer, Vector3.up);
            float angle = Mathf.Abs(signedAngle);
            bool nextStomp = attackCount % 3 == 1;
            float wantedRange = nextStomp ? stompDistance : jabDistance;
            // Existing attacks remain placeholders while the foundation motor is
            // integrated. This is not the final 4–6 second patrol/charge schedule.
            if (distance <= stompDistance && (distance < 1.15f || angle >= 24))
            {
                if (cooldown <= 0) TrySkill(ArenaAttack.Stomp);
                return;
            }
            if (cooldown <= 0 && distance <= wantedRange + .15f && angle < 24)
            {
                TrySkill(nextStomp ? ArenaAttack.Stomp : ArenaAttack.ComboTwo);
                return;
            }
            if (angle > 7.5f)
            {
                // Select a complete authored turn, then reconsider the target
                // only after both feet reach its endpoint. Never scale its yaw.
                int selected = 0;
                float bestError = angle;
                foreach (int degrees in new[] { 180, 90, 45, 15 })
                {
                    if (degrees > angle + 7.5f || !foundationMotor.HasClip("turn_" + (signedAngle > 0 ? "right_" : "left_") + degrees)) continue;
                    float error = Mathf.Abs(angle - degrees);
                    if (error < bestError) { bestError = error; selected = degrees; }
                }
                if (selected > 0 && foundationMotor.TryTurn(selected, signedAngle > 0)) return;
                foundationDecisionDelay = .20f;
                return;
            }
            // A straight segment is committed at its entry heading. Finish an
            // integer number of full gait cycles before the phase-zero stop.
            float forwardDistance = Vector3.Dot(toPlayer, transform.forward);
            float room = forwardDistance - wantedRange;
            float bridges = foundationMotor.StraightSegmentDistance(0);
            if (room >= bridges)
            {
                int loops = Mathf.Clamp(Mathf.FloorToInt((room - bridges) / animationDriver.walkStride), 0, 2);
                for (int count = loops; count >= 0; count--)
                    if (foundationMotor.BeginWalkSegment(count)) return;
            }
            // Desired attack range is not a collision boundary. A complete
            // start/stop may finish slightly inside it, which closes the gap
            // left when less than one authored bridge pair remains outside it.
            bool outsideAttackRange = distance > wantedRange + .15f;
            float minimumGap = capsule.radius + session.player.capsule.radius + .35f;
            if (outsideAttackRange && room < bridges && forwardDistance - bridges >= minimumGap
                && foundationMotor.BeginWalkSegment(0)) return;
            // If another complete step cannot fit, use the existing radial
            // attack only while the player is already inside its valid range.
            if (outsideAttackRange && distance <= stompDistance && cooldown <= 0)
            {
                TrySkill(ArenaAttack.Stomp);
                return;
            }
            foundationDecisionDelay = .20f;
        }

        private Vector3 ToPlayer() => Vector3.ProjectOnPlane(session.player.transform.position - transform.position, Vector3.up);

        private void BeginBridge(bool start)
        {
            locomotion = start ? Locomotion.Starting : Locomotion.Stopping;
            bridgeHeading = transform.rotation;
            bridgeTime = 0;
            movementBlocked = blockedReported = false; blockedTime = 0;
            idleTime = 0;
            animationDriver.PoseContactClip(start ? animationDriver.startShort : animationDriver.stopShort, 0, 0);
            session.Record(start ? "boss_start_step" : "boss_stop_step", "locomotion", fighter, 0, transform.position);
        }

        private void BridgeTick(float delta)
        {
            bool starting = locomotion == Locomotion.Starting;
            var motion = starting ? startMotion : stopMotion;
            var clip = starting ? animationDriver.startShort : animationDriver.stopShort;
            float next = Mathf.Min(motion.duration, bridgeTime + delta);
            motion.Sample(bridgeTime, out var before, out _);
            motion.Sample(next, out var after, out _);
            // JSON is already in Unity [-x,z,-y] space. Retain lateral weight shift;
            // applying the Blender conversion again would mirror the actual footwork.
            Vector3 requested = bridgeHeading * (after - before);
            Vector3 actual = MoveRequested(requested, delta);
            float progress = requested.sqrMagnitude < 1e-12f ? 1
                : Mathf.Clamp01(Vector3.Dot(actual, requested) / requested.sqrMagnitude);
            bridgeTime += (next - bridgeTime) * progress;
            SetBlocked(progress < .25f && requested.sqrMagnitude > 1e-10f, delta);
            motion.Sample(bridgeTime, out _, out float yaw);
            transform.rotation = bridgeHeading * Quaternion.Euler(0, yaw, 0);
            animationDriver.PoseContactClip(clip, bridgeTime, delta);
            FootstepContacts();
            speed = ActualSpeed;
            if(blockedTime >= .22f)
            {
                // The player can step into an already committed stopping bridge.
                // Freezing distance-driven phase there holds a raised foot forever.
                // Keep the actual legal actor position and settle visibly into guard;
                // no attack or new translation starts during the short rebalance.
                string blockedBridge = locomotion.ToString();
                locomotion = Locomotion.Guard;
                speed = 0; idleTime = 0;
                bridgeSettleTime = .25f;
                cooldown = Mathf.Max(cooldown, .35f);
                animationDriver.Pose(animationDriver.guard, 0, 0, true, .20f);
                SetBlocked(false, delta);
                session.Record("boss_obstructed_step_settle", blockedBridge, fighter, 0, transform.position);
                return;
            }
            if (bridgeTime >= motion.duration - .00001f)
            {
                bridgeTime = motion.duration;
                animationDriver.PoseContactClip(clip, bridgeTime, 0);
                locomotion = starting ? Locomotion.Walking : Locomotion.Guard;
                walkCycles = 0;
                idleTime = 0;
                speed = starting ? walkSpeed : 0;
                SetBlocked(false, delta);
                session.Record(starting ? "boss_start_finished" : "boss_stop_finished", "locomotion", fighter, 0, transform.position);
            }
        }

        private bool StopAtBoundary(float range)
        {
            if (!decisionEnabled) return true;
            Vector3 toPlayer = ToPlayer();
            float distance = toPlayer.magnitude;
            float angle = distance < .001f ? 0 : Vector3.Angle(transform.forward, toPlayer);
            float stopTravel = stopMotion.EndPosition.z;
            if (angle <= 26)
            {
                if (distance <= range + stopTravel + .10f) return true;
                // If another full same-foot cycle would overrun the player, stop
                // here. A subsequent start+stop gives a shorter, correctly planted step.
                float minimumGap = capsule.radius + session.player.capsule.radius + .35f;
                if (distance - animationDriver.walkStride - stopTravel < minimumGap) return true;
            }
            return SweepFraction(transform.forward * (animationDriver.walkStride + stopTravel + .06f)) < .98f;
        }

        private void WalkTick(float delta, float range)
        {
            float remaining = delta;
            // At 120 Hz at most one cycle boundary is crossed; the loop also keeps
            // the final partial interval without an artificial pause at every seam.
            for (int iteration = 0; iteration < 3 && remaining > .000001f; iteration++)
            {
                double fraction = walkCycles - Math.Floor(walkCycles);
                if (fraction < .000001 && StopAtBoundary(range))
                { BeginBridge(false); BridgeTick(remaining); return; }
                Vector3 target = ToPlayer();
                if (target.sqrMagnitude > .001f)
                {
                    Vector3 direction = target.normalized;
                    float signed = Vector3.SignedAngle(transform.forward, direction, Vector3.up);
                    if (Mathf.Abs(signed) > 8) turnSide = signed > 0 ? 1 : -1;
                    // Keep moving around a close target while finishing the planted
                    // cycle, rather than pushing straight through their capsule.
                    if (target.magnitude < 1.45f)
                        direction = Quaternion.Euler(0, turnSide * 80, 0) * direction;
                    float rate = movementBlocked ? Mathf.Min(12, turnSpeed) : turnSpeed;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), rate * remaining);
                }
                float toBoundary = (float)(1 - fraction) * animationDriver.walkStride;
                float portion = Mathf.Min(remaining, toBoundary / walkSpeed);
                Vector3 requested = transform.forward * walkSpeed * portion;
                Vector3 actual = MoveRequested(requested, portion);
                float travel = Mathf.Max(0, Vector3.Dot(actual, transform.forward));
                walkCycles += travel / animationDriver.walkStride;
                if (toBoundary - travel < .00001f) walkCycles = Math.Ceiling(walkCycles - .000001);
                SetBlocked(travel < requested.magnitude * .25f, portion);
                animationDriver.PoseContactClip(animationDriver.walk, walkCycles * animationDriver.walk.length, portion, true);
                FootstepContacts();
                speed = ActualSpeed;
                remaining -= portion;
                if (movementBlocked) break;
            }
        }

        private Vector3 MoveRequested(Vector3 requested, float delta)
        {
            float allowed = SweepFraction(requested);
            return Move(delta > 0 ? requested * allowed / delta : Vector3.zero, delta);
        }

        private float SweepFraction(Vector3 requested)
        {
            float length = requested.magnitude;
            if (length < .000001f || !capsule.enabled) return capsule.enabled ? 1 : 0;
            Vector3 centre = transform.TransformPoint(capsule.center);
            float radius = Mathf.Max(.03f, capsule.radius - capsule.skinWidth * .5f);
            float offset = Mathf.Max(0, capsule.height * .5f - capsule.radius);
            if (Physics.CapsuleCast(centre + Vector3.up * offset, centre - Vector3.up * offset,
                radius, requested / length, out var hit, length + .015f, MovementMask, QueryTriggerInteraction.Ignore))
                return Mathf.Clamp01((hit.distance - .012f) / length);
            return 1;
        }

        private void SetBlocked(bool blocked, float delta)
        {
            movementBlocked = blocked;
            blockedTime = blocked ? blockedTime + delta : 0;
            if (blockedTime > .18f && !blockedReported)
            {
                blockedReported = true;
                session.Record("boss_locomotion_obstructed", locomotion.ToString(), fighter, 0, transform.position);
            }
            if (!blocked && blockedReported)
            {
                session.Record("boss_locomotion_resumed", locomotion.ToString(), fighter, 0, transform.position);
                blockedReported = false;
            }
        }

        private Vector3 Move(Vector3 velocity, float delta)
        {
            if (capsule.isGrounded && vertical < 0) vertical = -2;
            vertical = Mathf.Max(-25, vertical - 24 * delta);
            Vector3 before = transform.position;
            if (capsule.enabled) capsule.Move(velocity * delta + Vector3.up * vertical * delta);
            Vector3 actual = Vector3.ProjectOnPlane(transform.position - before, Vector3.up);
            ActualSpeed = delta > 0 ? actual.magnitude / delta : 0;
            return actual;
        }

        private void FootstepContacts()
        {
            for(int i = 0; i < 2; i++)
            {
                var foot = Socket(i == 0 ? "socket_foot.R" : "socket_foot.L");
                float height = foot.position.y - transform.position.y;
                if(height > .06f) liftedFeet[i] = true;
                else if(height <= .025f && liftedFeet[i])
                {
                    liftedFeet[i] = false;
                    session.Record("boss_foot_contact", i == 0 ? "right_step" : "left_step", fighter, 0, foot.position);
                }
            }
        }

        private void AttackTick(float delta)
        {
            float previousTime = skillTime;
            skillTime += delta;
            Move(Vector3.zero, delta);
            // The approach owns heading. The authored planted feet remain fixed
            // throughout the entire combination; later punches can genuinely miss.
            animationDriver.Pose(AttackClip, skillTime, delta);
            if (current != ArenaAttack.Stomp)
            {
                if(current == ArenaAttack.ComboThree && previousTime < 1.25f && skillTime >= 1.25f)
                {
                    var hand = Socket("socket_hand.R");
                    session.effects?.Charge(hand);
                    session.EmitSound(ArenaSound.HeavyCharge, hand.position, attackGroup, hand);
                    session.Record("boss_finisher_charge", "hammer_punch", fighter, attackGroup, hand.position);
                }
                if (skillTime < .43f) session.effects?.WarnStrike(transform.position, transform.forward, 1.9f, .85f, skillTime / .43f);
                else if(current == ArenaAttack.ComboThree && skillTime >= 1.25f && skillTime < 1.766667f)
                    session.effects?.WarnStrike(transform.position, transform.forward, 2.1f, 1.05f, (skillTime - 1.25f) / .516667f);
                else session.effects?.HideWarning();
                for(int hit = 0; hit < PunchCount; hit++)
                {
                    int side = hit == 1 ? 1 : 0;
                    var hand = Socket(side == 1 ? "socket_hand.L" : "socket_hand.R");
                    if(previousTime < SwingStarts[hit] && skillTime >= SwingStarts[hit])
                    {
                        session.effects?.Swing(hand, transform.forward);
                        session.EmitSound(hit == 2 ? ArenaSound.SwingHeavy : side == 1 ? ArenaSound.SwingLeft : ArenaSound.SwingRight,
                            hand.position, attackGroup, hand);
                    }
                    if(skillTime >= PunchStarts[hit] && previousTime <= PunchEnds[hit])
                    {
                        Vector3 before = previousHands[side];
                        if(ComboHitIndex != hit)
                        {
                            ComboHitIndex = hit;
                            if(hit > 0) strike = session.NewStrike();
                            // A new strike starts at its current hand, never at the
                            // opposite hand or a previous punch's long-discarded point.
                            before = hand.position;
                            session.Record("boss_punch_window", PunchSkills[hit], fighter, strike, hand.position);
                        }
                        session.StrikeCapsule(fighter, strike, PunchSkills[hit], before, hand.position,
                            hit == 2 ? jabRadius + .06f : jabRadius, hit == 2 ? jabDamage * 1.4f : jabDamage,
                            hit == PunchCount - 1 ? 4 : 1.3f);
                    }
                }
                previousHands[0] = FistPosition; previousHands[1] = Socket("socket_hand.L").position;
            }
            else
            {
                if (skillTime <= .20f) groundCentre = transform.TransformPoint(stompLocalContact);
                if (skillTime < 1f) session.effects?.WarnRing(groundCentre, stompRadius, skillTime);
                if (!impactReleased && skillTime >= 1f)
                {
                    groundCentre = FootPosition;
                    groundCentre.y = transform.position.y;
                    impactReleased = true;
                    session.effects?.HideWarning();
                    session.effects?.GroundPulse(groundCentre, stompRadius);
                    session.Record("boss_ground_contact", "furnace_stomp", fighter, strike, groundCentre);
                }
                float age = skillTime - 1f;
                if (impactReleased && age <= .40f)
                {
                    float radius = ArenaCombatFx.PulseRadius(age, stompRadius);
                    session.StrikeRing(fighter, strike, "furnace_stomp", groundCentre, radius, Mathf.Max(0, radius - .75f), stompDamage, 6);
                }
            }
            if(!recoveryReported && skillTime >= RecoveryTime)
            {
                recoveryReported = true;
                session.Record("boss_recovery_started", current.ToString(), fighter, attackGroup, transform.position);
            }
            if (skillTime >= Duration)
            {
                attacking = false;
                locomotion = Locomotion.Guard;
                idleTime = 0;
                cooldown = .30f;
                session.effects?.HideWarning();
                session.Record("boss_recovered", current.ToString(), fighter, strike, transform.position);
                foundationMotor?.ResumeGuard();
            }
        }
    }
}
