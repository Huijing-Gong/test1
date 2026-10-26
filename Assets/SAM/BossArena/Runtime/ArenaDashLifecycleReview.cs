using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction.BossArena
{
    // Opt-in visual evidence owner. Every pose, root and event comes from the
    // normal Session/Brain/Motor; no forced pose, root or gravity overrides.
    public sealed class ArenaDashLifecycleReview : MonoBehaviour
    {
        [Serializable] private sealed class SourceClip
        { public string id = "", file = "", clip = "", source_action = "", fbx_sha256 = ""; public float duration = 0; }
        [Serializable] private sealed class SourceIdentity
        { public string source_blend = "", source_sha256 = ""; public SourceClip[] clips = Array.Empty<SourceClip>(); }
        [Serializable] private sealed class SourceRecord { public string library, motionAsset, motionJsonSha256; public SourceIdentity declaredSource; }
        [Serializable] private sealed class Sample
        {
            public int stage, displayFrame, fistQueries, flameQueries, ownedLoops, ownedVoices, activeVoices, activeEffects, arenaVoices, arenaEffects;
            public double wallTime, simulatedTime, stageTime, battleTime, dspTime, rawFrameInterval;
            public string phase, clip, foundationState, roundState, bossState, playerAction;
            public float sourceTime, phaseTime, executionTime, foundationTime, bossHealth, playerHealth;
            public Vector3 boss, player, fist, leftFoot, rightFoot, velocity, cameraPosition, cameraEuler;
            public Quaternion bossRotation;
            public bool grounded, dead, dashActive, aborted, cancelPending, releaseSuppressed, strikeOpen, invulnerable, focused, insideFrustum;
            public long execution, presentationExecution;
            public ArenaCommand command;
            // Present on actual displayed frames only; all 49 bone local TRS and
            // four corrective weights are observed, never assigned by this tool.
            public Vector3[] bonePositions, boneScales;
            public Quaternion[] boneRotations;
            public float[] shapeWeights;
        }
        [Serializable] private sealed class Observation
        {
            public int stage;
            public string kind, detail;
            public double wallTime, simulatedTime, stageTime, dspTime;
            public Sample actual;
            public ArenaEventRecord combat;
            public ArenaDashEvent dash;
            public ArenaDashPresentation.PresentationEvent presentation;
            public ArenaSoundEvent sound;
        }
        [Serializable] private sealed class CaseResult
        {
            public string id, intervention;
            public bool passed, grounded, tailsCleared, restartReadyClean;
            public double simulatedDuration, wallDuration, tailSeconds, restartReadySeconds;
            public float cancelAtSourceTime, chargeDuration, releaseDuration, recoveryDuration, deathDuration, observedDeathTime, hopRise, finalPlanarRootError;
            public Vector3 recoverOrigin, finalRoot, lethalRoot, groundedAfterLethal;
            public int fistQueries, flameQueries, releases;
        }
        [Serializable] private sealed class Report
        {
            public int schema = 1, width, height, captureFramerate, targetFrameRate, vSyncCount;
            public string kind = "real_wall_clock_dash_lifecycle_scripted_cancel_and_direct_lethal_visual_review";
            public string unity, build, graphics, view;
            public string clockPolicy = "Measured wall elapsed enters ordinary ArenaSession.Simulate in <=1/120s portions. Sub-microsecond remainder is carried to the next display frame. No fixed frame advance, dropped-time clamp or captureFramerate.";
            public string fixturePolicy = "Three explicitly isolated skill fixtures; patrol gate and autonomous decisions are disabled. Cancel calls are scripted motor API requests. Lethal damage is explicitly injected with Fighter.Receive, not a collision or native input test. Actual physics, normal death animation and normal Restart(false) remain authoritative.";
            public string visualScope = "Displayed 49-bone poses and four shapes, actual actors and camera recorded. Frustum containment does not prove occlusion/art. Native input, performance, patrol and Round 2 approval remain separate.";
            public bool completeSequence, nativeInputVerified = false, performanceVerified = false, roundPassed = false;
            public double startedUnityRealtime, wallDuration, simulatedDuration, maximumFrameInterval, pendingWallSeconds;
            public string[] boneNames, errors;
            public SourceRecord[] sources;
            public CaseResult[] cases;
            public Sample[] frames, simulationSamples;
            public Observation[] observations;
        }
        private static readonly string[] Titles = {
            "CHARGE CANCEL / FULL 2.0s CHARGE + 1.8s NO-PUNCH HOP",
            "LOCK CANCEL AT 0.10s / FULL 0.9s LEGS + 1.4s RECOVERY",
            "DIRECT LETHAL AT AIRBORNE APEX / DESCEND + FULL DEATH + READY"
        };
        private ArenaSession arena;
        private ArenaDashMotor dash;
        private ArenaFoundationMotor foundation;
        private SkinnedMeshRenderer skin;
        private Camera reviewCamera;
        private string output, view;
        private readonly List<Sample> frames = new List<Sample>(), samples = new List<Sample>();
        private readonly List<Observation> observations = new List<Observation>();
        private readonly List<CaseResult> results = new List<CaseResult>();
        private readonly List<string> errors = new List<string>();
        private readonly List<SourceRecord> sources = new List<SourceRecord>();
        private readonly Plane[] planes = new Plane[6];
        private int stage, eventCursor, dashCursor, presentationCursor, soundCursor, releaseBefore;
        private double started, previous, simulated, pending, stageTime, stageWall, maxInterval, settledAt = -1, restartAt = -1;
        private bool ready, displayed, finishing, written, writable, began, intervened, recoveryTracked, lethalLanded;
        private float initialHealth, cancelTime, peak, deathObserved;
        private Vector3 recoveryOrigin, recoveryEnd, lethalRoot, lethalGround;
        private Quaternion recoveryRotation;
        private CaseResult deathResult;
        private GUIStyle label;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--sam-dash-lifecycle-review") < 0) return;
            foreach (string flag in new[] { "--sam-benchmark", "--sam-foundation-review", "--sam-foundation-gameplay-review", "--sam-foundation-contracts", "--sam-dash-contracts", "--sam-dash-review" })
                if (Array.IndexOf(args, flag) >= 0) { Debug.LogError("Lifecycle review rejected another simulation owner: " + flag); Application.Quit(2); return; }
            new GameObject("Production18 actual dash lifecycle review").AddComponent<ArenaDashLifecycleReview>();
        }
        private static string Arg(string key, string fallback)
        { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        private IEnumerator Start()
        {
            yield return null;
            try
            {
                output = Path.GetFullPath(Arg("--sam-output", Path.Combine(Application.persistentDataPath, "dash_lifecycle_review")));
                Require(!File.Exists(Path.Combine(output, "realtime_dash_lifecycle.json")), "Preserve old evidence; select a new run.");
                Directory.CreateDirectory(output); writable = true;
                arena = FindFirstObjectByType<ArenaSession>(); dash = arena != null ? arena.boss.DashMotor : null;
                Require(dash != null && dash.Initialized && dash.presentation != null, "Saved dash scene is unavailable.");
                foundation = arena.boss.FoundationMotor;
                Require(foundation != null && foundation.Initialized && dash.clips.HasClip("dash_abort_recover"), "Full F cancel and foundation death sources are required.");
                dash.clips.Validate();
                sources.Add(Source("Dash18", dash.clips.motionData)); sources.Add(Source("Foundation18", foundation.foundation.motionData));
                arena.automaticTick = false; arena.view.automaticTick = false;
                arena.boss.decisionEnabled = false; arena.boss.useDashSampleAI = false; dash.requirePatrolFeasibility = false;
                arena.Restart(false);
                Application.runInBackground = true; Time.captureFramerate = 0; Time.timeScale = 1;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
                Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
                view = Arg("--sam-view", "side"); Require(view == "front" || view == "side", "Choose front or side.");
                reviewCamera = arena.view.GetComponent<Camera>(); reviewCamera.fieldOfView = 43;
                skin = arena.boss.GetComponentInChildren<SkinnedMeshRenderer>();
                Require(skin != null && skin.bones.Length == 49 && skin.sharedMesh.blendShapeCount == 4, "Unexpected visible rig/shape count.");
                PositionCamera(); displayed = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            catch (Exception e) { Fail(e); yield break; }
            yield return null; yield return new WaitForEndOfFrame(); yield return new WaitForSecondsRealtime(1.2f);
            try { started = previous = Time.realtimeSinceStartupAsDouble; BeginStage(0); ready = true; Capture(0); }
            catch (Exception e) { Fail(e); }
        }
        private static SourceRecord Source(string library, TextAsset asset)
        {
            Require(asset != null, "Missing actual bound motion JSON.");
            var declared = JsonUtility.FromJson<SourceIdentity>(ArenaMotionEncoding.Text(asset));
            Require(declared != null && declared.source_sha256.Length == 64 && declared.clips.Length > 0,
                "Bound source identity is incomplete: " + library);
            using (var hash = SHA256.Create())
                return new SourceRecord { library = library, motionAsset = asset.name,
                    motionJsonSha256 = BitConverter.ToString(hash.ComputeHash(ArenaMotionEncoding.SourceBytes(asset))).Replace("-", "").ToLowerInvariant(),
                    declaredSource = declared };
        }
        private double Wall => started == 0 ? 0 : Time.realtimeSinceStartupAsDouble - started;
        private void BeginStage(int index)
        {
            Drain(); stage = index; stageTime = 0; stageWall = Wall; settledAt = restartAt = -1;
            began = intervened = recoveryTracked = lethalLanded = false; cancelTime = peak = deathObserved = 0;
            arena.bossStart = new Vector3(0, .025f, 0); arena.playerStart = new Vector3(0, .05f, 5);
            arena.Restart(true); arena.boss.ResetBoss(arena.bossStart, Quaternion.identity); arena.player.view = null;
            eventCursor = dashCursor = soundCursor = 0; releaseBefore = dash.presentation.ReleaseCount;
            initialHealth = arena.player.fighter.Health; Observe("fixture_started", Titles[index] + "; scripted isolated lifecycle fixture, no native input claim.");
            Drain(); PositionCamera();
        }
        private void Update()
        {
            if (!ready || finishing || written) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            try
            {
                Require(Time.captureFramerate == 0 && Mathf.Abs(Time.timeScale - 1) < .00001f, "Real-time clock changed.");
                double now = Time.realtimeSinceStartupAsDouble, raw = now - previous; previous = now;
                Require(raw >= 0 && raw <= 5, "Unreviewable wall-clock interval: " + raw);
                maxInterval = Math.Max(maxInterval, raw); pending += raw;
                while (pending > .000001 && !finishing)
                {
                    InspectAndIntervene(); if (finishing) break;
                    float step = (float)Math.Min(1d / 120, pending);
                    ArenaCommand command = default;
                    arena.Simulate(command, step); simulated += step; stageTime += step; pending -= step;
                    Drain(); samples.Add(Read(command, 0, false)); InspectProgress();
                }
                PositionCamera(); Capture(raw);
            }
            catch (Exception e) { Fail(e); }
        }
        private void InspectAndIntervene()
        {
            Require(stageTime < 17, "Lifecycle timed out before complete actual recovery/death/tails: " + Titles[stage]);
            if (restartAt >= 0)
            {
                Require(CleanReady(), "Restart did not remain clean Ready.");
                if (stageTime - restartAt >= 1.2)
                {
                    deathResult.restartReadyClean = true; deathResult.restartReadySeconds = stageTime - restartAt;
                    deathResult.simulatedDuration = stageTime; deathResult.wallDuration = Wall - stageWall; deathResult.passed = true;
                    results.Add(deathResult); Observe("fixture_completed", "Actual full death and tails, normal Restart(false), then 1.2s clean Ready.");
                    finishing = true; StartCoroutine(Finish());
                }
                return;
            }
            if (!began && stageTime >= .25)
            {
                Observe("scripted_skill_request", "Ordinary Boss.TrySkill(DashPunch); patrol feasibility explicitly disabled for this isolated lifecycle fixture.");
                Require(arena.boss.TrySkill(ArenaAttack.DashPunch), "Boss refused actual skill request."); began = true;
            }
            if (began && !intervened)
            {
                if (stage == 0 && dash.Phase == ArenaDashPhase.Charge && dash.SourceTime >= .8f)
                {
                    cancelTime = dash.SourceTime; Observe("scripted_ordinary_cancel", "Motor.Cancel(false) at charge ~0.8s; full 2s charge must continue.");
                    dash.Cancel("review_ordinary_charge_cancel", false); intervened = true;
                    Require(dash.CancelPending && dash.Phase == ArenaDashPhase.Charge, "Charge cancellation did not queue until loaded endpoint.");
                }
                else if (stage == 1 && dash.Phase == ArenaDashPhase.LockRelease && dash.SourceTime >= .10f)
                {
                    cancelTime = dash.SourceTime; Observe("scripted_ordinary_cancel", "Motor.Cancel(false) at locked release ~0.10s; suppress damage and retain compatible complete legs.");
                    dash.Cancel("review_locked_release_cancel", false); intervened = true;
                    Require(dash.ReleaseSuppressed && !dash.StrikeOpen, "Locked cancellation failed to suppress release.");
                }
                else if (stage == 2 && dash.Phase == ArenaDashPhase.Recover && dash.ClipId == "dash_recover" && dash.SourceTime >= dash.clips.EventTime("dash_recover", "hop_apex"))
                {
                    Require(!arena.boss.capsule.isGrounded, "Direct lethal fixture did not reach an actual airborne apex.");
                    cancelTime = dash.SourceTime; lethalRoot = arena.boss.transform.position;
                    Observe("direct_lethal_injection_NOT_COLLISION_NOT_NATIVE_INPUT", "Fighter.Receive(health+1) at authored recover apex; lifecycle test only. Ordinary death callbacks and actual descent remain enabled.");
                    bool accepted = arena.boss.fighter.Receive(new ArenaHit { source = arena.player.fighter, strike = arena.NewStrike(),
                        skill = "review_direct_lethal_lifecycle_only", damage = arena.boss.fighter.Health + 1,
                        point = lethalRoot, direction = Vector3.forward });
                    Require(accepted && arena.boss.fighter.Dead && !dash.StrikeOpen, "Direct lethal fixture failed or left damage open."); intervened = true;
                }
                Drain();
            }
        }
        private void InspectProgress()
        {
            if (restartAt >= 0) return;
            if (dash.Active && dash.Phase == ArenaDashPhase.Recover && !recoveryTracked)
            {
                recoveryTracked = true; recoveryOrigin = dash.StateOrigin; recoveryRotation = arena.boss.transform.rotation; peak = recoveryOrigin.y;
                Observe("actual_recovery_entered", dash.ClipId + "; track actual XYZ until physical landing and complete source endpoint.");
            }
            if (recoveryTracked) peak = Mathf.Max(peak, arena.boss.transform.position.y);
            if (stage == 2 && intervened && arena.boss.capsule.isGrounded && !lethalLanded)
            { lethalLanded = true; lethalGround = arena.boss.transform.position; Observe("actual_ground_after_lethal", "Actual CharacterController grounded after physical descent."); }
            if (foundation.DeathStarted) deathObserved = Mathf.Max(deathObserved, foundation.StateTime);
            bool finished = began && intervened && !dash.Active && (stage != 2 || foundation.State == ArenaFoundationMotor.MotionState.DeathHold);
            if (!finished) return;
            if (settledAt < 0) { settledAt = stageTime; recoveryEnd = arena.boss.transform.position; Observe("actual_complete_pose_hold", "Continue ordinary simulation through all owned presentation and arena sound/effect tails before any reset."); }
            Require(stageTime - settledAt < 4, "Actual sound/effect ownership did not clear after full motion.");
            if (stageTime - settledAt < .8 || !Cleared()) return;
            var result = ValidateStage();
            if (stage < 2)
            { result.passed = true; results.Add(result); Observe("fixture_completed", "Full source motion, actual grounding and all sound/effect tails observed."); BeginStage(stage + 1); }
            else
            {
                deathResult = result; Observe("normal_restart_requested", "Ordinary Session.Restart(false), after full DeathHold and all tails. No pose/health patching.");
                Drain(); arena.Restart(false); eventCursor = dashCursor = soundCursor = 0; restartAt = stageTime;
                Require(CleanReady(), "Normal restart failed to clear live lifecycle state."); Drain(); Observe("normal_restart_ready", "Actual clean Ready; continue 1.2s ordinary simulation and displayed recording.");
            }
        }
        private CaseResult ValidateStage()
        {
            Require(arena.boss.capsule.isGrounded && !dash.StrikeOpen && Cleared(), "Motion/tails did not finish cleanly grounded.");
            var result = new CaseResult { id = Titles[stage], intervention = stage == 2 ? "explicit_direct_lethal_injection_not_collision_or_native" : "scripted_ordinary_motor_cancel_false",
                grounded = true, tailsCleared = true, simulatedDuration = stageTime, wallDuration = Wall - stageWall, tailSeconds = stageTime - settledAt,
                cancelAtSourceTime = cancelTime, recoverOrigin = recoveryOrigin, finalRoot = recoveryEnd, hopRise = peak - recoveryOrigin.y,
                fistQueries = dash.FistQueryCount, flameQueries = dash.FlameQueryCount, releases = dash.presentation.ReleaseCount - releaseBefore };
            var recover = dash.Events.First(e => e.kind == "dash_recover_started");
            var complete = dash.Events.Last(e => e.kind == "dash_recovery_complete");
            if (stage > 0) result.chargeDuration = dash.Events.First(e => e.kind == "dash_slide_started").executionTime;
            Require(cancelTime >= (stage == 0 ? .8f : stage == 1 ? .1f : dash.clips.EventTime("dash_recover", "hop_apex")) - .00001f
                && cancelTime <= (stage == 0 ? .8f : stage == 1 ? .1f : dash.clips.EventTime("dash_recover", "hop_apex")) + .009f,
                "Lifecycle intervention missed its requested actual source-time boundary.");
            if (stage < 2)
            {
                Require(dash.Aborted && !dash.Released && dash.FistQueryCount == 0 && dash.FlameQueryCount == 0 && result.releases == 0
                    && arena.player.fighter.Health == initialHealth && !arena.Events.Any(e => e.kind == "hit" && e.skill == "dash_punch"), "Cancelled action emitted damage/query/release.");
                string id = stage == 0 ? "dash_abort_recover" : "dash_recover";
                Require(recover.detail == id && Mathf.Abs(complete.phaseTime - dash.clips.Motion(id).duration) <= .0001f, "Wrong/truncated cancellation recovery source.");
                result.recoveryDuration = complete.phaseTime;
                Vector3 expected = recoveryOrigin + recoveryRotation * dash.clips.Motion(id).EndPosition;
                result.finalPlanarRootError = Vector3.ProjectOnPlane(recoveryEnd - expected, Vector3.up).magnitude;
                Require(result.finalPlanarRootError <= .005f && result.hopRise > .27f, "Cancelled recovery omitted actual -1.15m XYZ hop.");
                var off = dash.Events.First(e => e.kind == "dash_takeoff"); var land = dash.Events.First(e => e.kind == "dash_landed");
                Require(Mathf.Abs(off.phaseTime - dash.clips.EventTime(id, "hop_takeoff")) < .009f && land.phaseTime >= dash.clips.EventTime(id, "hop_land") - .009f, "Actual hop lifecycle markers disagree with source.");
                if (stage == 0)
                {
                    result.chargeDuration = recover.executionTime;
                    Require(Mathf.Abs(result.chargeDuration - dash.clips.Motion("dash_charge").duration) <= .009f && !dash.Events.Any(e => e.kind == "dash_slide_started"), "Cancel truncated full charge or entered a damaging route.");
                }
                else
                {
                    var locked = dash.Events.First(e => e.kind == "dash_lockrelease_started");
                    result.releaseDuration = recover.executionTime - locked.executionTime;
                    Require(Mathf.Abs(result.releaseDuration - dash.clips.Motion("dash_lock_release").duration) <= .009f
                        && !samples.Any(s => s.stage == stage && s.clip == "dash_abort_recover"), "Lock cancel replaced/truncated compatible release legs.");
                }
            }
            else
            {
                result.deathDuration = foundation.foundation.Motion("death").duration; result.observedDeathTime = deathObserved;
                result.lethalRoot = lethalRoot; result.groundedAfterLethal = lethalGround;
                Require(arena.State == ArenaRoundState.Victory && lethalLanded && lethalRoot.y - lethalGround.y > .20f
                    && dash.Events.Any(e => e.kind == "dash_abort_landing") && arena.Events.Any(e => e.kind == "boss_death_finished")
                    && deathObserved >= result.deathDuration - .0001f, "Direct lethal did not physically descend and consume full normal death source.");
            }
            return result;
        }
        private bool Cleared() => dash.presentation.OwnedLoopHandleCount == 0 && dash.presentation.OwnedVoiceHandleCount == 0
            && dash.presentation.ActiveVoiceCount == 0 && dash.presentation.ActiveEffectCount == 0
            && (arena.sound == null || arena.sound.ActiveVoiceCount == 0) && (arena.effects == null || arena.effects.ActiveCount == 0);
        private bool CleanReady() => arena.State == ArenaRoundState.Ready && !arena.boss.fighter.Dead && !arena.player.fighter.Dead
            && arena.boss.fighter.Health == arena.boss.fighter.maximumHealth && arena.player.fighter.Health == arena.player.fighter.maximumHealth
            && !dash.Active && !dash.StrikeOpen && !foundation.DeathStarted && foundation.ClipId == "guard"
            && dash.presentation.ExecutionId == 0 && !arena.player.fighter.Invulnerable && !arena.boss.fighter.Invulnerable && Cleared();
        private void Drain()
        {
            if (arena == null) return;
            while (eventCursor < arena.Events.Count) { var o = Observe("combat_event", ""); o.combat = arena.Events[eventCursor++]; }
            if (dash == null) return;
            while (dashCursor < dash.Events.Count) { var o = Observe("dash_event", ""); o.dash = dash.Events[dashCursor++]; }
            while (presentationCursor < dash.presentation.Events.Count) { var o = Observe("presentation_DSP_event", ""); o.presentation = dash.presentation.Events[presentationCursor++]; }
            if (arena.sound != null) while (soundCursor < arena.sound.Events.Count) { var o = Observe("arena_sound_DSP_event", ""); o.sound = arena.sound.Events[soundCursor++]; }
        }
        private Observation Observe(string kind, string detail)
        {
            var o = new Observation { stage = stage, kind = kind, detail = detail, wallTime = Wall, simulatedTime = simulated,
                stageTime = stageTime, dspTime = AudioSettings.dspTime };
            if (kind != "combat_event" && kind != "dash_event" && kind != "presentation_DSP_event" && kind != "arena_sound_DSP_event" && skin != null)
                o.actual = Read(default, 0, false);
            observations.Add(o); return o;
        }
        private Sample Read(ArenaCommand command, double raw, bool pose)
        {
            var p = dash.presentation;
            var row = new Sample { stage = stage, displayFrame = Time.frameCount, wallTime = Wall, simulatedTime = simulated, stageTime = stageTime,
                battleTime = arena.BattleTime, dspTime = AudioSettings.dspTime, rawFrameInterval = raw, phase = dash.Phase.ToString(),
                clip = dash.Active ? dash.ClipId : foundation.ClipId, sourceTime = dash.Active ? dash.SourceTime : foundation.StateTime,
                foundationState = foundation.State.ToString(), foundationTime = foundation.StateTime, phaseTime = dash.PhaseTime, executionTime = dash.ExecutionTime,
                roundState = arena.State.ToString(), bossState = arena.boss.StateName, playerAction = arena.player.ActionName,
                bossHealth = arena.boss.fighter.Health, playerHealth = arena.player.fighter.Health, boss = arena.boss.transform.position, bossRotation = arena.boss.transform.rotation,
                player = arena.player.transform.position, fist = arena.boss.FistPosition, leftFoot = arena.boss.Socket("socket_foot.L").position, rightFoot = arena.boss.Socket("socket_foot.R").position,
                velocity = dash.ActualVelocity, grounded = arena.boss.capsule.isGrounded, dead = arena.boss.fighter.Dead, dashActive = dash.Active,
                aborted = dash.Aborted, cancelPending = dash.CancelPending, releaseSuppressed = dash.ReleaseSuppressed, strikeOpen = dash.StrikeOpen,
                invulnerable = arena.player.fighter.Invulnerable, focused = Application.isFocused, execution = dash.ExecutionId, presentationExecution = p.ExecutionId,
                fistQueries = dash.FistQueryCount, flameQueries = dash.FlameQueryCount, ownedLoops = p.OwnedLoopHandleCount, ownedVoices = p.OwnedVoiceHandleCount,
                activeVoices = p.ActiveVoiceCount, activeEffects = p.ActiveEffectCount, arenaVoices = arena.sound != null ? arena.sound.ActiveVoiceCount : 0,
                arenaEffects = arena.effects != null ? arena.effects.ActiveCount : 0, command = command };
            if (pose)
            {
                row.bonePositions = skin.bones.Select(b => b.localPosition).ToArray(); row.boneRotations = skin.bones.Select(b => b.localRotation).ToArray();
                row.boneScales = skin.bones.Select(b => b.localScale).ToArray(); row.shapeWeights = Enumerable.Range(0, 4).Select(skin.GetBlendShapeWeight).ToArray();
                row.cameraPosition = reviewCamera.transform.position; row.cameraEuler = reviewCamera.transform.eulerAngles;
                GeometryUtility.CalculateFrustumPlanes(reviewCamera, planes); row.insideFrustum = Inside(skin.bounds) && Inside(arena.player.capsule.bounds);
            }
            return row;
        }
        private void Capture(double raw) { var row = Read(default, raw, true); frames.Add(row); Require(row.insideFrustum, "Actual displayed actor bounds left the review frustum."); }
        private void PositionCamera()
        {
            if (reviewCamera == null || skin == null) return;
            Bounds bounds = skin.bounds; bounds.Encapsulate(arena.player.capsule.bounds); bounds.Expand(.8f);
            Vector3 forward = Vector3.ProjectOnPlane(arena.boss.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 offset = (view == "side" ? right + forward * .05f : forward + right * .03f) + Vector3.up * .25f;
            Quaternion rotation = Quaternion.LookRotation(-offset.normalized);
            float tangent = Mathf.Tan(reviewCamera.fieldOfView * .5f * Mathf.Deg2Rad), distance = 7;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = Quaternion.Inverse(rotation) * (point - bounds.center);
                distance = Mathf.Max(distance, Mathf.Abs(local.y) / (tangent * .63f) - local.z, Mathf.Abs(local.x) / (tangent * reviewCamera.aspect * .82f) - local.z);
            }
            reviewCamera.transform.SetPositionAndRotation(bounds.center + offset.normalized * (distance + .4f), rotation);
        }
        private bool Inside(Bounds bounds)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                foreach (var plane in planes) if (plane.GetDistanceToPoint(point) < 0) return false;
            }
            return true;
        }
        private IEnumerator Finish() { yield return new WaitForEndOfFrame(); Save(true); Application.Quit(); }
        private void OnGUI()
        {
            if (!displayed) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 19, normal = { textColor = Color.white }, wordWrap = true };
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * (Screen.width / 1920f));
            GUI.Box(new Rect(24, 240, 1220, 118), GUIContent.none);
            GUI.Label(new Rect(38, 246, 1192, 108), "DASH LIFECYCLE / " + Titles[stage] + "\n" +
                (stage == 2 ? "DIRECT LETHAL INJECTION: lifecycle only, not collision/native input" : "Scripted ordinary Cancel(false): actual continuous source and physics") +
                "\n" + (restartAt >= 0 ? "NORMAL RESTART / CLEAN READY HOLD" : arena != null ? arena.State + " / " + dash.Phase : "Preparing") +
                " / real wall clock + actual framebuffer/audio / " + view, label);
            GUI.matrix = old;
        }
        private void Save(bool complete)
        {
            if (written || !writable) return; written = true; Drain();
            File.WriteAllText(Path.Combine(output, "realtime_dash_lifecycle.json"), JsonUtility.ToJson(new Report {
                completeSequence = complete && results.Count == 3 && results.All(c => c.passed), width = Screen.width, height = Screen.height,
                captureFramerate = Time.captureFramerate, targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount,
                unity = Application.unityVersion, build = Application.version, graphics = SystemInfo.graphicsDeviceName, view = view,
                startedUnityRealtime = started, wallDuration = Wall, simulatedDuration = simulated, maximumFrameInterval = maxInterval, pendingWallSeconds = pending,
                boneNames = skin != null ? skin.bones.Select(b => b.name).ToArray() : Array.Empty<string>(), sources = sources.ToArray(), cases = results.ToArray(),
                frames = frames.ToArray(), simulationSamples = samples.ToArray(), observations = observations.ToArray(), errors = errors.ToArray() }, true));
            Debug.Log("DASH_LIFECYCLE_REVIEW_COMPLETE complete=" + complete + " cases=" + results.Count + " frames=" + frames.Count + " errors=" + errors.Count);
        }
        private void Fail(Exception e) { errors.Add(e.ToString()); Debug.LogException(e); ready = false; finishing = true; Save(false); Application.Quit(2); }
        private void OnApplicationQuit() { if (!written) Save(false); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
