using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Opt-in deterministic checks of the real scene, CharacterControllers and
    // combat resolver. These do not certify native input, sound, visuals or FPS.
    public sealed class ArenaDashContracts : MonoBehaviour
    {
        [Serializable] private sealed class Trace
        {
            public string caseName, phase, clip, playerAction, bossState;
            public double simulationTime, battleTime;
            public float sourceTime, phaseTime, executionTime, playerHealth, bossHealth, headingYaw;
            public Vector3 boss, player, fist, leftFoot, rightFoot, velocity, lockedForward, flameA, flameB;
            public float flameRadius;
            public long execution, strike;
            public int sequenceIndex, fistQueries, flameQueries, ownedLoops, activeEffects;
            public bool locked, strikeOpen, flameGeometry, invulnerable, grounded, patrolActive, patrolInvalid, aborted, soleSampled;
            public ArenaCommand command;
            public ArenaFoundationSoleProbe.Sample sole;
            public Vector3 patrolLeftSole, patrolRightSole, patrolReturnPoint, patrolEndpoint;
            public float patrolElapsed, patrolPathLength, patrolAnkleError, patrolBoneLengthError, patrolReachMargin, patrolFacingError;
            public float patrolPelvisAdjustment, patrolMaximumPelvisAdjustment;
            public Vector3 patrolVelocity;
            public bool patrolForwardCurve;
            public int patrolStepIndex, patrolReturnIndex, patrolTargetIndex, patrolCompletedIntervals;
            public bool patrolLeftSupported, patrolRightSupported, patrolReturnReached, patrolFinalReached;
            public bool sourceSoleReferenceSampled, sourceReferenceLeftLivePoseUnchanged;
            public int clipInstanceId;
            public ArenaDashSourceSoleReference.Sample sourceSoleReference;
        }
        [Serializable] private sealed class Evidence
        {
            public string name, scope, detail;
            public bool passed;
            public ArenaEventRecord[] combat;
            public ArenaDashEvent[] dash;
            public ArenaStrikeObservation[] strikeQueries;
            public ArenaPatrolPlanner.PatrolEvent[] patrol;
        }
        [Serializable] private sealed class Report
        {
            public int schema = 2;
            public string kind = "headless_actual_dash_physics_contracts_not_visual_audio_native_input_or_performance";
            public string unity, build, graphics;
            public bool passed, integratedLoopPassed, nativeInputVerified = false, performanceVerified = false;
            public bool fixedEndpointCoverageVerified = false, soleGeometryVerified = false;
            public bool forwardOnlyPatrolRequired = true;
            public Vector3 patrolPointA,patrolPointB;
            public bool sourceSoleReferenceEnabled, sourceSoleReferenceDisposed;
            public int syntheticSourceSoleNumericalChecks;
            public float simulationStep = Step;
            public Evidence[] cases;
            public Trace[] traces;
        }
        private const float Step = 1f / 120;
        private ArenaSession arena;
        private ArenaDashMotor dash;
        private ArenaDashPresentation presentation;
        private ArenaFoundationSoleProbe sole;
        private ArenaDashSourceSoleReference sourceSoleReference;
        private SkinnedMeshRenderer liveMesh;
        private bool sourceReferenceDisposed;
        private int numericalChecks;
        [Serializable] private sealed class SourceIdentity { public string source_sha256; public SourceClipIdentity[] clips; }
        [Serializable] private sealed class SourceClipIdentity { public string id, fbx_sha256; }
        private SourceIdentity sourceIdentity;
        private readonly List<Evidence> cases = new List<Evidence>();
        private readonly List<Trace> traces = new List<Trace>(30000);
        private readonly List<GameObject> blockers = new List<GameObject>();
        private readonly List<ArenaEventRecord> combatHistory = new List<ArenaEventRecord>();
        private readonly List<ArenaDashEvent> dashHistory = new List<ArenaDashEvent>();
        private readonly List<ArenaStrikeObservation> strikeQueries = new List<ArenaStrikeObservation>();
        private new string name;
        private string previousClip;
        private int tick, clipSequence;
        private double clock;
        private Vector3 defaultBoss, defaultPlayer;
        private bool integratedPassed, endpointPassed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--sam-dash-contracts") < 0) return;
            foreach (string flag in new[] { "--sam-benchmark", "--sam-foundation-review", "--sam-foundation-gameplay-review", "--sam-foundation-contracts", "--sam-dash-review" })
                if (Array.IndexOf(args, flag) >= 0) { Debug.LogError("Multiple simulation owners: " + flag); Application.Quit(2); return; }
            new GameObject("Production18 dash physics contracts").AddComponent<ArenaDashContracts>();
        }
        private IEnumerator Start()
        {
            yield return null;
            string output = ""; bool writable = false;
            try
            {
                var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--sam-output");
                Require(index >= 0 && index + 1 < args.Length, "A new output directory is required.");
                output = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(output);
                Require(!File.Exists(Path.Combine(output, "dash_contracts.json")), "Preserve existing reports."); writable = true;
                arena = FindFirstObjectByType<ArenaSession>();
                Require(arena != null, "Saved arena is missing.");
                arena.automaticTick = false; arena.view.automaticTick = false;
                arena.StrikeObserved += ObserveStrike;
                defaultBoss = arena.bossStart; defaultPlayer = arena.playerStart;
                // DSP does not advance in this synchronous test. Asset/lifecycle
                // state is recorded, but actual audio is verified in real time.
                if (arena.sound != null) arena.sound.enabled = false;
                arena.sound = null;
                dash = arena.boss.DashMotor; presentation = dash != null ? dash.presentation : null;
                Require(dash != null && dash.Initialized && presentation != null, "Saved Round2 scene and exported clips/presentation are required.");
                liveMesh = arena.boss.GetComponentInChildren<SkinnedMeshRenderer>();
                sole = new ArenaFoundationSoleProbe(liveMesh);
                sourceIdentity = JsonUtility.FromJson<SourceIdentity>(ArenaMotionEncoding.Text(dash.clips.motionData));
                if (ArenaDashSourceSoleReference.Requested) numericalChecks = SourceSoleReferenceNumericalChecks.Run();
                Check("stationary_hit_full_recovery", "isolated_skill_actual_collision_patrol_gate_disabled", HitAndRecovery);
                Check("flame_volume_hits_beyond_fist", "isolated_skill_real_narrow_phase_flame_only_hit_patrol_gate_disabled", FlameOnly);
                Check("late_command_dodge_misses", "isolated_skill_actual_command_and_collision_patrol_gate_disabled", LateDodge);
                Check("moving_target_then_locked_heading", "isolated_skill_actual_command_tracking_patrol_gate_disabled", MovingTarget);
                Check("world_wall_aborts_without_slide_damage", "isolated_skill_actual_world_collision_patrol_gate_disabled", Wall);
                Check("far_target_has_bounded_full_recovery", "isolated_skill_pursuit_limit_patrol_gate_disabled", FarTarget);
                Check("cancel_during_charge_cleans_and_settles", "isolated_skill_lifecycle_patrol_gate_disabled", CancelCharge);
                Check("cancel_locked_release_keeps_compatible_legs", "isolated_skill_lifecycle_patrol_gate_disabled", CancelLocked);
                Check("airborne_lethal_descends_and_restart_cleans", "direct_lethal_injection_lifecycle_only_not_hit_collision", AirborneDeath);
                Check("default_ai_complete_patrol_attack_patrol_loop", "integrated_default_sample_ai_actual_timing_patrol_gate_enabled", Integrated);
            }
            catch (Exception e) { cases.Add(new Evidence { name = "setup", scope = "setup", detail = e.ToString() }); }
            finally {
                if (arena != null) arena.StrikeObserved -= ObserveStrike;
                sourceSoleReference?.Dispose(); sourceReferenceDisposed = sourceSoleReference != null && sourceSoleReference.DisposedAndObjectsDestroyed;
                sole?.Dispose(); RemoveBlockers();
            }
            bool passed = cases.Count == 10 && cases.All(c => c.passed);
            if (writable) File.WriteAllText(Path.Combine(output, "dash_contracts.json"), JsonUtility.ToJson(new Report {
                passed = passed, integratedLoopPassed = integratedPassed, fixedEndpointCoverageVerified = endpointPassed, unity = Application.unityVersion, build = Application.version,
                graphics = SystemInfo.graphicsDeviceName, sourceSoleReferenceEnabled = sourceSoleReference != null,
                patrolPointA=arena.boss.PatrolPlanner.pointA,patrolPointB=arena.boss.PatrolPlanner.pointB,
                sourceSoleReferenceDisposed = sourceReferenceDisposed, syntheticSourceSoleNumericalChecks = numericalChecks,
                cases = cases.ToArray(), traces = traces.ToArray() }, true));
            Debug.Log("DASH_CONTRACTS_COMPLETE passed=" + passed + " cases=" + cases.Count);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
#else
            Application.Quit(passed ? 0 : 1);
#endif
        }
        private void Check(string caseName, string scope, Action body)
        {
            name = caseName;
            combatHistory.Clear(); dashHistory.Clear(); strikeQueries.Clear();
            var row = new Evidence { name = name, scope = scope, detail = "" };
            try {
                Fixture();
                if (ArenaDashSourceSoleReference.Requested && sourceSoleReference == null)
                    sourceSoleReference = new ArenaDashSourceSoleReference(arena.boss.visual, liveMesh);
                body(); row.passed = true;
            }
            catch (Exception e) { row.detail = e.ToString(); }
            if (arena != null) row.combat = combatHistory.Concat(arena.Events).ToArray();
            if (dash != null) row.dash = dashHistory.Concat(dash.Events).ToArray();
            row.strikeQueries = strikeQueries.ToArray();
            row.patrol = arena?.boss?.PatrolPlanner?.Events.ToArray();
            cases.Add(row);
        }
        private void Fixture(float distance = 5)
        {
            RemoveBlockers();
            arena.boss.useDashSampleAI = false; arena.boss.decisionEnabled = false;
            dash.requirePatrolFeasibility = false;
            arena.bossStart = new Vector3(0, .025f, 0); arena.playerStart = new Vector3(0, .05f, distance);
            arena.Restart(true); arena.boss.ResetBoss(arena.bossStart, Quaternion.identity);
            // Makes test move axes explicit; actual camera input is a separate test.
            arena.player.view = null;
            previousClip = null; StepMany(.12f);
            Require(!dash.Active && arena.boss.FoundationMotor.IsGuard, "Fixture did not reset to guard.");
        }
        private void StartDash()
        {
            Require(arena.boss.TrySkill(ArenaAttack.DashPunch), "Actual Brain rejected dash start: " + dash.Outcome);
            Sample(default);
        }
        private void HitAndRecovery()
        {
            float hp = arena.player.fighter.Health; int releases = presentation.ReleaseCount; StartDash();
            Until(() => dash.Phase == ArenaDashPhase.LockRelease, 6);
            var locked = arena.boss.transform.rotation;
            Require(dash.FistQueryCount == 0 && dash.FlameQueryCount == 0 && arena.player.fighter.Health == hp,
                "Charge or slide queried damage or reduced health.");
            Until(() => dash.Phase == ArenaDashPhase.Recover, 2, () => {
                Require(Quaternion.Angle(locked, arena.boss.transform.rotation) < .02f, "Locked heading changed before recovery."); return default;
            });
            var recoverStart = dash.StateOrigin; float peak = arena.boss.transform.position.y;
            Until(() => !dash.Active, 3, () => { peak = Mathf.Max(peak, arena.boss.transform.position.y); return default; });
            Require(!dash.Aborted, "Normal attack aborted: " + dash.Outcome);
            Require(Count("hit") == 1 && Mathf.Abs(hp - arena.player.fighter.Health - dash.damage) < .001f,
                "Stationary actual target must take exactly one configured damage event from fist/fire.");
            Require(dash.FistQueryCount > 0 && dash.FlameQueryCount > 0 && dash.MissingFlameGeometrySamples == 0,
                "Both fist and actual flame volumes must be queried with valid geometry.");
            var hits = arena.Events.Where(e => e.kind == "hit" && e.skill == "dash_punch").ToArray();
            Require(hits[0].strike == dash.StrikeId, "Damage identity differs from release identity.");
            var expected = recoverStart + locked * dash.clips.Motion("dash_recover").EndPosition;
            Require(Vector3.ProjectOnPlane(arena.boss.transform.position - expected, Vector3.up).magnitude < .005f,
                "Recovery did not consume the source horizontal trajectory.");
            Require(peak - recoverStart.y > .27f && arena.boss.capsule.isGrounded, "Full authored hop and physical landing were not observed.");
            var charge = Event("dash_charge_started"); var slide = Event("dash_slide_started");
            Require(Math.Abs(slide.executionTime - charge.executionTime - 2) < Step * 1.1, "Continuous charge duration is not 2 seconds.");
            Require(presentation.ReleaseCount - releases == 1, "Hit release duplicated or omitted its presentation.");
            StepMany(1.5f);
            Require(!presentation.TryGetDamageGeometry(dash.ExecutionId, out _, out _, out _), "Damage geometry persisted after the release lifetime.");
            Require(presentation.OwnedLoopHandleCount == 0 && presentation.OwnedVoiceHandleCount == 0 && presentation.ActiveEffectCount == 0,
                "Owned voice/loop/effect state leaked after full recovery.");
        }
        private void LateDodge()
        {
            Require(cases.Any(c => c.name == "stationary_hit_full_recovery" && c.passed),
                "The same stationary target must be proven vulnerable before this miss can certify a successful dodge.");
            float hp = arena.player.fighter.Health; int releases = presentation.ReleaseCount; StartDash();
            Until(() => dash.Phase == ArenaDashPhase.LockRelease, 6);
            float strikeAt = dash.clips.EventTime("dash_lock_release", "strike_open");
            Require(strikeAt >= .30f, "Lock-to-strike window is too short for this intended late dodge test.");
            Until(() => dash.PhaseTime >= strikeAt - .12f, 1);
            Tick(new ArenaCommand { dodge = true, move = Vector2.right });
            Until(() => !dash.Active, 4);
            Require(dash.Released && !dash.Aborted, "The Boss must actually finish its punch while the player dodges.");
            Require(arena.Events.Any(e => e.kind == "player_dodge_started"), "No actual player dodge command was consumed.");
            Require(Count("hit") == 0 && arena.player.fighter.Health == hp, "The correctly timed last dodge was hit.");
            Require(presentation.ReleaseCount - releases == 1, "A miss must still emit exactly one visible punch/fire release.");
        }
        private void FlameOnly()
        {
            Fixture(2.60f); StartDash(); Until(() => !dash.Active, 7);
            var queries = strikeQueries.Where(q => q.skill == "dash_punch" && q.target == "Player").ToArray();
            Require(dash.Released && !dash.Aborted && Count("hit") == 1, "Flame-only fixture did not complete one actual hit.");
            Require(queries.Any(q => q.queryTag == "fist") && queries.Any(q => q.queryTag == "flame"), "Actual fist/flame query observations missing.");
            Require(!queries.Any(q => q.queryTag == "fist" && q.intersects), "Flame-only fixture also entered the physical fist sweep.");
            Require(queries.Count(q => q.accepted) == 1 && queries.Single(q => q.accepted).queryTag == "flame",
                "The actual fire geometry must own the only accepted hit beyond the fist sweep.");
            Require(Mathf.Abs(arena.player.fighter.maximumHealth - arena.player.fighter.Health - dash.damage) < .001f,
                "Flame-only hit damage differs from the shared configured attack.");
        }
        private void MovingTarget()
        {
            StartDash(); Until(() => dash.IsSliding, 3);
            float beforeYaw = arena.boss.transform.eulerAngles.y;
            StepMany(.32f, new ArenaCommand { move = Vector2.right, run = true });
            Require(dash.IsSliding || dash.IsLocked, "Pursuit unexpectedly stopped while target moved.");
            Require(Mathf.Abs(Mathf.DeltaAngle(beforeYaw, arena.boss.transform.eulerAngles.y)) > 1,
                "Sliding did not track the actual moving player.");
            Until(() => dash.IsLocked, 4);
            var locked = arena.boss.transform.rotation;
            Until(() => dash.Phase == ArenaDashPhase.Recover, 2, () => {
                Require(Quaternion.Angle(locked, arena.boss.transform.rotation) < .02f, "Moving target retargeted the locked final punch.");
                return new ArenaCommand { move = Vector2.left, run = true };
            });
            Until(() => !dash.Active, 3);
            Require(dash.Released, "The actual tracked target never reached the release state.");
        }
        private void Wall()
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube); blockers.Add(block);
            block.name = "CONTRACT actual wall"; block.layer = 8;
            block.transform.position = new Vector3(0, 1.6f, 1.55f); block.transform.localScale = new Vector3(5, 3.2f, .3f);
            Physics.SyncTransforms(); StartDash(); Until(() => !dash.Active, 9);
            Require(dash.Aborted && !dash.Released && Count("hit") == 0 && dash.FistQueryCount == 0 && dash.FlameQueryCount == 0,
                "An obstructed slide must abort without damage/release queries.");
            Require(arena.boss.transform.position.z < 1.41f && arena.boss.capsule.isGrounded,
                "Boss tunneled through the real wall or failed to land.");
            Require(dash.Events.Any(e => e.kind == "dash_abort" && e.detail.Contains("blocked")), "Actual wall abort reason was not recorded.");
        }
        private void FarTarget()
        {
            Fixture(14); StartDash(); Until(() => !dash.Active, 9);
            Require(dash.Aborted && !dash.Released && Count("hit") == 0 && dash.SlidePathLength <= dash.maximumSlidePath + .002f,
                "Unreached far target exceeded the pursuit bound or fabricated a final hit.");
            Require(arena.boss.capsule.isGrounded, "Bounded far-target recovery did not reach actual ground.");
        }
        private void CancelCharge()
        {
            StartDash(); StepMany(.8f); dash.Cancel("contract_explicit_cancel", false);
            Until(() => dash.Phase == ArenaDashPhase.Recover, 2);
            var origin = dash.StateOrigin; var rotation = arena.boss.transform.rotation;
            float peak = origin.y;
            Until(() => !dash.Active, 3, () => { peak = Mathf.Max(peak, arena.boss.transform.position.y); return default; });
            var expected = origin + rotation * dash.clips.Motion("dash_abort_recover").EndPosition;
            Require(Vector3.ProjectOnPlane(arena.boss.transform.position - expected, Vector3.up).magnitude <= .005f
                && peak - origin.y > .27f && arena.boss.capsule.isGrounded,
                "Loaded cancellation did not execute its actual XYZ rear hop and physical landing.");
            Require(Mathf.Abs(Event("dash_takeoff").phaseTime - .65f) <= Step * 1.1f
                && Event("dash_landed").phaseTime >= 1.15f - Step * 1.1f
                && Mathf.Abs(Event("dash_recovery_complete").phaseTime - 1.8f) <= Step * 1.1f,
                "Dedicated cancel recovery used the wrong takeoff, landing or completion timing.");
            StepMany(1.5f);
            Require(dash.Aborted && !dash.Released && Count("hit") == 0, "Cancelled charge caused a hit or release.");
            var recover = dash.Events.First(e => e.kind == "dash_recover_started");
            Require(recover.detail == "dash_abort_recover" && Math.Abs(recover.executionTime - 2) < Step * 1.1, "Ordinary cancel truncated the continuous two-second charge or used the wrong source.");
            Require(traces.Any(t => t.caseName == name && t.clip == "dash_abort_recover" && t.sourceTime >= 1.79f),
                "Loaded cancellation did not consume the complete dedicated recovery source.");
            Require(presentation.OwnedLoopHandleCount == 0 && presentation.OwnedVoiceHandleCount == 0 && presentation.ActiveEffectCount == 0 && !dash.StrikeOpen,
                "Cancellation leaked an owned voice/loop/effect or an open damage window.");
        }
        private void CancelLocked()
        {
            int releases = presentation.ReleaseCount;
            StartDash(); Until(() => dash.Phase == ArenaDashPhase.LockRelease && dash.PhaseTime >= .10f, 6);
            dash.Cancel("contract_cancel_locked_before_strike", false);
            Until(() => !dash.Active, 5); StepMany(1.5f);
            Require(dash.Aborted && !dash.Released && Count("hit") == 0 && presentation.ReleaseCount == releases,
                "Cancelled final release still emitted a hit or release.");
            var sequence = traces.Where(t => t.caseName == name).ToArray();
            Require(!sequence.Any(t => t.clip == "dash_abort_recover") && sequence.Any(t => t.clip == "dash_lock_release" && t.sourceTime >= .89f)
                && sequence.Any(t => t.clip == "dash_recover" && t.sourceTime >= 1.39f),
                "Locked cancellation must finish compatible release legs and the full original recovery.");
            Require(presentation.OwnedLoopHandleCount == 0 && presentation.OwnedVoiceHandleCount == 0 && presentation.ActiveEffectCount == 0
                && !dash.StrikeOpen && arena.boss.capsule.isGrounded, "Cancelled final release did not cleanly finish on the floor.");
        }
        private void AirborneDeath()
        {
            StartDash(); Until(() => dash.Phase == ArenaDashPhase.Recover && dash.PhaseTime >= .58f, 7);
            Require(!arena.boss.capsule.isGrounded, "Lethal fixture did not reach the actual airborne part.");
            arena.boss.fighter.Receive(new ArenaHit { source = arena.player.fighter, strike = arena.NewStrike(),
                skill = "contract_direct_lethal_lifecycle_only", damage = arena.boss.fighter.Health + 1,
                point = arena.boss.transform.position, direction = Vector3.forward });
            Until(() => !dash.Active, 4); StepMany(4);
            Require(arena.State == ArenaRoundState.Victory && arena.boss.capsule.isGrounded && !dash.StrikeOpen,
                "Lethal airborne interruption left a live attack or suspended root.");
            Require(presentation.OwnedLoopHandleCount == 0 && presentation.OwnedVoiceHandleCount == 0 && presentation.ActiveEffectCount == 0,
                "Death left active owned effects or voice/loop handles.");
            combatHistory.AddRange(arena.Events); dashHistory.AddRange(dash.Events);
            arena.Restart(true); StepMany(.2f);
            Require(arena.Active && !arena.boss.fighter.Dead && !arena.player.fighter.Dead && !dash.Active,
                "Restart failed to restore the ordinary living fighters.");
            Require(presentation.ExecutionId == 0 && presentation.ActiveEffectCount == 0 && presentation.OwnedLoopHandleCount == 0 &&
                presentation.OwnedVoiceHandleCount == 0 && !arena.player.fighter.Invulnerable,
                "Restart retained an execution, active effect, or invulnerability.");
        }
        private void Integrated()
        {
            arena.boss.useDashSampleAI = true; arena.boss.decisionEnabled = true;
            dash.requirePatrolFeasibility = true;
            arena.bossStart = defaultBoss; arena.playerStart = defaultPlayer;
            arena.Restart(true); arena.player.view = arena.view;
            Until(() => arena.Events.Count(e => e.kind == "boss_attack_started" && e.skill == "dash_punch") >= 3,
                45, () => { Require(!arena.boss.PatrolPlanner.Invalid, "Integrated patrol failed: " + arena.boss.PatrolPlanner.Failure); return default; });
            arena.boss.decisionEnabled = false;
            Until(() => !dash.Active, 8);
            var starts = arena.Events.Where(e => e.kind == "boss_patrol_started").ToArray();
            var ends = arena.Events.Where(e => e.kind == "boss_patrol_completed").ToArray();
            Require(starts.Length >= 3 && ends.Length >= 3, "No complete patrol before and after the first attack.");
            for (int i = 0; i < 3; i++)
            {
                double duration = ends[i].time - starts[i].time;
                Require(duration >= 4 - Step && duration <= 6 + Step,
                    "Actual full patrol/return/facing duration was " + duration + " seconds.");
            }
            Require(arena.Events.Any(e => e.kind == "dash_heading_locked") && arena.Events.Any(e => e.kind == "dash_strike_open"),
                "Default AI patrol loop never reached an actual final punch.");
            Require(!arena.Events.Any(e => e.kind == "dash_isolated_skill_test"), "Integrated loop disabled its feasibility requirement.");
            ArenaDashReviewEvidence.ValidateExecutions(arena.Events);
            var planner = arena.boss.PatrolPlanner;
            var patrolEvents = planner.Events.ToArray();
            var completions = patrolEvents.Where(e => e.kind == "boss_patrol_completed").ToArray();
            Require(planner.CompletedIntervals >= 3 && completions.Length >= 3, "Complete physical endpoint intervals missing.");
            for (int i = 0; i < 3; i++)
            {
                var finish = completions[i];
                var begin = patrolEvents.Last(e => e.kind == "boss_patrol_started" && e.battleTime <= finish.battleTime);
                var visited = patrolEvents.Where(e => e.kind == "boss_patrol_endpoint_reached" && e.battleTime >= begin.battleTime - Step
                    && e.battleTime <= finish.battleTime + Step).ToArray();
                // Q15 permits a complete interval to stop partway along AB.
                // Endpoint claims still require physical arrival; absence of a
                // claim is not permission to silently reverse the target.
                var stop=patrolEvents.Last(e => (e.kind=="boss_patrol_midroute_stop"||e.kind=="boss_patrol_endpoint_reached")
                    && e.battleTime<=finish.battleTime+Step&&e.battleTime>=begin.battleTime-Step);
                Require(Vector3.ProjectOnPlane(finish.actor-finish.target,Vector3.up).magnitude<=.005f,
                    "Completed forward interval did not actually reach its planned route stop.");
                Require(planner.DistanceFromRoute(finish.actor)<=.005f,
                    "Completed interval did not stop on the fixed AB route.");
                if(i>0)
                {
                    var previousFinish=completions[i-1];
                    Require(begin.nextEndpointIndex==previousFinish.nextEndpointIndex,
                        "Patrol direction changed without actual endpoint arrival, or failed to reverse after arrival.");
                }
                foreach (var visit in visited)
                {
                    var fixedPoint = visit.actualEndpointIndex == 0 ? planner.pointA : planner.pointB;
                    Require(Vector3.ProjectOnPlane(visit.actor - fixedPoint, Vector3.up).magnitude <= .005f,
                        "An endpoint was declared reached outside the original 5mm positional tolerance.");
                }
                var selectedSource = planner.Motor.clips.SourceContract(begin.clipId);
                Require(begin.clipId == finish.clipId && begin.stepCount == selectedSource.steps.Length
                    && finish.stepCount == selectedSource.steps.Length
                    && Mathf.Abs(begin.duration - selectedSource.duration) <= .001f,
                    "Actual patrol selection changed or does not match its complete authored source.");
                Require(arena.Events.Count(e => e.kind == "boss_foot_contact" && e.time >= begin.battleTime - Step && e.time <= finish.battleTime + Step) == selectedSource.steps.Length,
                    "Patrol did not record every complete swing-foot contact of the selected source.");
            }
            Require(completions.Take(3).Any(e=>e.clipId==ArenaPatrolClips.LeftId),
                "Extended forward review must physically execute the left-leading variant.");
            endpointPassed = true;
            integratedPassed = true;
        }
        private void Until(Func<bool> condition, float limit, Func<ArenaCommand> command = null)
        {
            double end = clock + limit;
            while (!condition() && clock < end) Tick(command != null ? command() : default);
            Require(condition(), "Timed out after " + limit + "s; phase=" + dash.Phase + ", outcome=" + dash.Outcome + ", boss=" + arena.boss.StateName);
        }
        private void StepMany(float seconds, ArenaCommand command = default)
        {
            int count = Mathf.CeilToInt(seconds / Step);
            for (int i = 0; i < count; i++) { Tick(command); command = command.WithoutEdges(); }
        }
        private void Tick(ArenaCommand command)
        {
            arena.Simulate(command, Step); clock += Step; tick++; Sample(command);
        }
        private void Sample(ArenaCommand command)
        {
            var planner = arena.boss.PatrolPlanner;
            var patrol = planner?.Motor;
            bool patrolActive = patrol != null && patrol.Active;
            string clip = dash.Active ? dash.ClipId : patrolActive ? patrol.ClipId : arena.boss.FoundationMotor.ClipId;
            float sourceTime = dash.Active ? dash.SourceTime : patrolActive ? patrol.SourceTime : arena.boss.FoundationMotor.StateTime;
            if (clip != previousClip) { clipSequence++; previousClip = clip; }
            bool geometry = presentation.TryGetDamageGeometry(dash.ExecutionId, out var a, out var b, out float radius);
            var row = new Trace { caseName = name, simulationTime = clock, battleTime = arena.BattleTime, sequenceIndex = clipSequence, aborted = dash.Aborted,
                phase = dash.Phase.ToString(), clip = clip, sourceTime = sourceTime, phaseTime = dash.PhaseTime,
                executionTime = dash.ExecutionTime, boss = arena.boss.transform.position, player = arena.player.transform.position,
                fist = arena.boss.FistPosition, leftFoot = arena.boss.Socket("socket_foot.L").position,
                rightFoot = arena.boss.Socket("socket_foot.R").position, headingYaw = arena.boss.transform.eulerAngles.y,
                velocity = dash.ActualVelocity, lockedForward = dash.LockedForward, execution = dash.ExecutionId, strike = dash.StrikeId,
                fistQueries = dash.FistQueryCount, flameQueries = dash.FlameQueryCount, locked = dash.HeadingLocked,
                strikeOpen = dash.StrikeOpen, flameGeometry = geometry, flameA = a, flameB = b, flameRadius = radius,
                playerHealth = arena.player.fighter.Health, bossHealth = arena.boss.fighter.Health,
                playerAction = arena.player.ActionName, invulnerable = arena.player.fighter.Invulnerable,
                grounded = arena.boss.capsule.isGrounded, bossState = arena.boss.StateName,
                patrolActive = arena.boss.PatrolPlanner != null && arena.boss.PatrolPlanner.Active,
                patrolInvalid = arena.boss.PatrolPlanner != null && arena.boss.PatrolPlanner.Invalid,
                ownedLoops = presentation.OwnedLoopHandleCount, activeEffects = presentation.ActiveEffectCount, command = command };
            if (tick % 3 == 0 && sole != null) { row.soleSampled = true; row.sole = sole.Capture(clipSequence, sourceTime); }
            if (dash.Active) row.clipInstanceId = dash.clips.Clip(clip).GetInstanceID();
            if (sourceSoleReference != null && dash.Active && row.soleSampled && row.sole.reliable && row.sole.hasPreviousSameClip
                && sourceTime > row.sole.previousSourceTime + .000001f)
            {
                row.sourceSoleReferenceSampled = true;
                var bones = liveMesh.bones;
                var positions = bones.Select(b => b.localPosition).ToArray();
                var rotations = bones.Select(b => b.localRotation).ToArray();
                var scales = bones.Select(b => b.localScale).ToArray();
                var shapeWeights = Enumerable.Range(0, liveMesh.sharedMesh.blendShapeCount).Select(i => liveMesh.GetBlendShapeWeight(i)).ToArray();
                var actorPosition = arena.boss.transform.position; var actorRotation = arena.boss.transform.rotation;
                row.sourceSoleReference = sourceSoleReference.CapturePair(dash.clips.Clip(clip), dash.clips.Motion(clip), clipSequence,
                    row.sole.previousSourceTime, sourceTime, sourceIdentity.source_sha256, sourceIdentity.clips.Single(c => c.id == clip).fbx_sha256);
                row.sourceReferenceLeftLivePoseUnchanged = actorPosition == arena.boss.transform.position && actorRotation == arena.boss.transform.rotation;
                for (int i = 0; i < bones.Length; i++) row.sourceReferenceLeftLivePoseUnchanged &= positions[i] == bones[i].localPosition
                    && rotations[i] == bones[i].localRotation && scales[i] == bones[i].localScale;
                for (int i = 0; i < shapeWeights.Length; i++) row.sourceReferenceLeftLivePoseUnchanged &= shapeWeights[i] == liveMesh.GetBlendShapeWeight(i);
                if (!row.sourceReferenceLeftLivePoseUnchanged) throw new InvalidOperationException("Isolated source sampler changed a live actor/skin pose.");
            }
            if (patrolActive)
            {
                row.patrolLeftSole = patrol.LeftSole; row.patrolRightSole = patrol.RightSole;
                row.patrolLeftSupported = patrol.LeftSupported; row.patrolRightSupported = patrol.RightSupported;
                row.patrolReturnPoint = patrol.Path.returnPoint; row.patrolEndpoint = patrol.Path.endpoint;
                row.patrolElapsed = patrol.Elapsed; row.patrolPathLength = patrol.ActualPathLength;
                row.patrolVelocity=patrol.ActualVelocity;row.patrolForwardCurve=patrol.Path.forwardCurve!=null;
                row.patrolAnkleError = patrol.MaximumAnkleError; row.patrolBoneLengthError = patrol.MaximumBoneLengthError;
                row.patrolReachMargin = patrol.MinimumReachMargin; row.patrolFacingError = patrol.FinalFacingError;
                row.patrolPelvisAdjustment = patrol.CurrentPelvisAdjustment; row.patrolMaximumPelvisAdjustment = patrol.MaximumPelvisAdjustment;
                row.patrolStepIndex = patrol.StepIndex; row.patrolReturnIndex = patrol.Path.returnIndex; row.patrolTargetIndex = patrol.Path.targetIndex;
                row.patrolReturnReached = patrol.ReturnEndpointReached; row.patrolFinalReached = patrol.FinalEndpointReached;
                row.patrolCompletedIntervals = planner.CompletedIntervals;
            }
            traces.Add(row);
        }
        private int Count(string kind) => arena.Events.Count(e => e.kind == kind && e.skill == "dash_punch");
        private void ObserveStrike(ArenaStrikeObservation value) { strikeQueries.Add(value); }
        private ArenaDashEvent Event(string kind) => dash.Events.First(e => e.kind == kind);
        private void RemoveBlockers()
        {
            foreach (var item in blockers) if (item != null) DestroyImmediate(item);
            blockers.Clear(); Physics.SyncTransforms();
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
