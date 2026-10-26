using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction.BossArena
{
    // Runs after the ordinary LateUpdate camera in observation mode. This class
    // is dormant unless one of its explicit QA command-line flags is supplied.
    [DefaultExecutionOrder(10000)]
    public sealed class ArenaCameraContracts : MonoBehaviour
    {
        [Serializable] private sealed class Row
        {
            public string name, error;
            public bool passed;
            public float firstReturnTime = -1, controlRestoreTime = -1, impactLead;
            public int returnSamples;
            public float returnTravel, maxAngularSpeed, releasePositionStep, releaseAngleStep, releaseFovStep;
            public float explicitReleasePositionStep, explicitReleaseAngleStep, explicitReleaseFovStep;
            public Sample release;
            public ArenaEventRecord[] events;
        }

        [Serializable] private sealed class Sample
        {
            public string test, sourceClock, state, action;
            public int frame, phase;
            public double wall, battle;
            public float interval, sourceTime, fov, aspect, angleStep, angularSpeed, positionStep;
            public float returnWeight, hazardWeight, groupDistance, playerHeightFraction;
            public bool owned, locked, focused, automaticSession, automaticCamera, playerBodyVisible, worldConstrained;
            public bool warningActive, nearArcVisible, nearestEscapeVisible, keyboardMove, dodgeEdge, lockEdge;
            public Vector2 mouseDelta;
            public Vector3 position, player, boss, playerFootViewport, playerHeadViewport, playerCentreViewport;
            public Vector3 nearEdgeViewport, nearLeftViewport, nearRightViewport, nearestEscapeViewport;
            public Quaternion rotation;
        }

        [Serializable] private sealed class Report
        {
            public string scope, build, status;
            public bool passed, completed, nativeInputObservation, visualAndPerformanceAcceptance = false;
            public int width, height, focusedFrames, inputObservedFrames, ordinaryLockedFrames, ordinaryAngularLimitWarnings;
            public float aspect, duration, maxOrdinaryAngularSpeed;
            public string projectionPolicy = "Real Camera.WorldToViewportPoint; visible means z beyond near clip and x/y inside [0.03,0.97]. Body probes use the actual player CharacterController bounds. Near arc is the camera-nearest meteor edge plus/minus 12 degrees. No pixel/occlusion/artistic acceptance is inferred.";
            public Row[] cases;
            public Sample[] samples;
            public ArenaEventRecord[] events;
        }

        private const float Step = 1f / 120;
        private ArenaSession arena;
        private ArenaThirdPersonCamera view;
        private Camera cameraComponent;
        private readonly List<Row> rows = new List<Row>();
        private readonly List<Sample> samples = new List<Sample>();
        private readonly List<ArenaEventRecord> observedEvents = new List<ArenaEventRecord>();
        private Row current;
        private string output;
        private bool observe, ready, saved, quitObservation;
        private double observationStarted, previousWall;
        private float observationSeconds = 90;
        private Sample previous;
        private int seenEvents, focusedFrames, inputFrames, ordinaryFrames, angularWarnings;
        private float maxOrdinaryAngularSpeed;
        private double previousBattle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            bool contracts = Array.IndexOf(args, "--sam-camera-contracts") >= 0;
            bool observer = Array.IndexOf(args, "--sam-camera-observe") >= 0;
            if (!contracts && !observer) return;
            if ((contracts && observer) || args.Any(x => x != "--sam-camera-contracts" &&
                (x.EndsWith("-contracts", StringComparison.Ordinal) || x.EndsWith("-review", StringComparison.Ordinal) || x == "--sam-benchmark")))
            { Debug.LogError("Camera QA requires a single simulation owner or the unmodified native input path."); Application.Quit(2); return; }
            new GameObject("Camera actual-state QA").AddComponent<ArenaCameraContracts>();
        }

        private IEnumerator Start()
        {
            yield return null;
            try
            {
                var args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "--sam-output");
                Require(at >= 0 && at + 1 < args.Length, "Camera QA requires --sam-output.");
                observe = Array.IndexOf(args, "--sam-camera-observe") >= 0;
                output = Path.Combine(Path.GetFullPath(args[at + 1]), observe ? "camera_native_observation.json" : "camera_contracts.json");
                Require(!File.Exists(output), "Refusing to overwrite camera evidence.");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                arena = FindFirstObjectByType<ArenaSession>();
                Require(arena != null && arena.view != null && arena.player != null && arena.boss != null, "Real arena components missing.");
                view = arena.view; cameraComponent = view.GetComponent<Camera>();
                if (observe)
                {
                    Require(arena.automaticTick && view.automaticTick, "Native observation cannot replace the normal input/camera driver.");
                    at = Array.IndexOf(args, "--sam-camera-observe-seconds");
                    if (at >= 0) Require(at + 1 < args.Length && float.TryParse(args[at + 1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out observationSeconds) && observationSeconds >= 5 && observationSeconds <= 600, "Invalid observation duration.");
                    quitObservation = Array.IndexOf(args, "--sam-camera-observe-quit") >= 0;
                    observationStarted = previousWall = Time.realtimeSinceStartupAsDouble;
                    previousBattle = arena.BattleTime; ready = true;
                    Debug.Log("SAM_CAMERA_OBSERVATION_STARTED: normal native input remains active; no skills, health, camera or player controls were changed.");
                }
                else
                {
                    arena.automaticTick = false; view.automaticTick = false;
                    // A headless backbuffer is not a display-resolution test. Fix
                    // only the projection aspect so viewport measurements are 16:9.
                    cameraComponent.aspect = 16f / 9f;
                    Run("ignite_complete_return_and_actual_movement", IgniteComplete);
                    Run("meteor_release_player_and_dodge_window", MeteorRelease);
                    Run("meteor_release_near_warning_arc", MeteorNearArc);
                    Run("meteor_ground_cancel_exact_display_pose", () => CancelMeteor(.8f));
                    Run("meteor_air_cancel_exact_display_pose", () => CancelMeteor(4.2f));
                    Run("ignition_terminal_cancel_exact_display_pose", CancelIgnitionByDefeat);
                    Run("restart_during_ignition", () => RestartDuring(ArenaAttack.Ignite, 2.2f));
                    Run("restart_during_meteor", () => RestartDuring(ArenaAttack.Meteor, 4.2f));
                    Run("normal_input_pipeline_locked_angle_continuity", OrdinaryInputContinuity);
                    Save(true);
                    Application.Quit(rows.All(x => x.passed) ? 0 : 2);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                rows.Add(new Row { name = "setup", error = e.ToString() });
                if (output != null && !File.Exists(output)) Save(false);
                Application.Quit(2);
            }
        }

        private void Run(string name, Action action)
        {
            current = new Row { name = name }; previous = null;
            try { action(); current.passed = true; }
            catch (Exception e) { current.error = e.ToString(); }
            current.events = arena.Events.ToArray(); rows.Add(current);
        }

        private void Fixture(float playerDistance = 8)
        {
            arena.boss.decisionEnabled = false; arena.boss.useDashSampleAI = false;
            arena.bossStart = new Vector3(0, .025f, 0); arena.playerStart = new Vector3(0, .05f, playerDistance);
            arena.Restart(true); arena.boss.ResetBoss(arena.bossStart, Quaternion.identity);
            for (int i = 0; i < 16; i++) Tick(default);
            view.TickLook(Vector2.zero, true); view.TickCamera(0, true); Physics.SyncTransforms();
            previous = Capture("fixture", 0);
        }

        private void Begin(ArenaAttack skill)
        { Require(arena.boss.TrySkill(skill), "Actual Brain refused " + skill); Require(view.CinematicActive, "Skill did not acquire the real camera."); }

        private void IgniteComplete()
        {
            Fixture(6); Begin(ArenaAttack.Ignite);
            var motor = arena.boss.IgniteMotor; Vector3 playerStart = arena.player.transform.position;
            int limit = 0;
            while (view.CinematicActive && limit++ < 1600)
            {
                Tick(new ArenaCommand { move = Vector2.right, run = true, look = new Vector2(20, 0), dodge = true });
                Require(Vector3.ProjectOnPlane(arena.player.transform.position - playerStart, Vector3.up).magnitude < .001f, "Movie leaked player displacement.");
            }
            Require(limit < 1600 && motor.Completed, "Ignition failed to complete and return ownership.");
            Require(Mathf.Abs(current.controlRestoreTime - motor.Settings.completeSeconds) <= Step * 2, "Restored outside the imported completion boundary.");
            RequireReturnEvidence(motor.Settings.completeSeconds);
            Require(current.release.playerBodyVisible, "Player body was outside the actual camera viewport at ignition return.");
            for (int i = 0; i < 60; i++) Tick(new ArenaCommand { move = Vector2.right, run = true });
            Require(Vector3.ProjectOnPlane(arena.player.transform.position - playerStart, Vector3.up).magnitude > 1, "Movement did not resume after actual ignition completion.");
        }

        private void MeteorRelease()
        {
            Fixture(); Begin(ArenaAttack.Meteor); AdvanceUntilRelease();
            var motor = arena.boss.MeteorMotor;
            Require(motor.ControlRestored && !motor.Cancelled, "Meteor cancelled or failed to restore controls.");
            RequireReturnEvidence(motor.Settings.restoreTime);
            current.impactLead = motor.Settings.impactTime - motor.SourceTime;
            Require(current.impactLead >= 1, "Less than one second remains for visible meteor counterplay.");
            Require(current.release.playerBodyVisible, "Player feet/head/centre are outside the viewport at meteor control return.");
            Vector3 restoredPosition = arena.player.transform.position;
            Tick(new ArenaCommand { move = Vector2.right, dodge = true });
            Require(arena.player.Dodging, "Real dodge did not start immediately after restoration.");
            for (int i = 0; i < 30; i++) Tick(default);
            Require(Vector3.ProjectOnPlane(arena.player.transform.position - restoredPosition, Vector3.up).magnitude > .8f, "Restored dodge did not actually displace the player.");
        }

        private void MeteorNearArc()
        {
            Fixture(); Begin(ArenaAttack.Meteor); AdvanceUntilRelease();
            Require(!arena.boss.MeteorMotor.Cancelled, "Meteor cancelled before the visibility probe.");
            Require(current.release.warningActive, "No real warning presentation at restore.");
            Require(current.release.nearArcVisible, "Camera-nearest warning edge or its +/-12 degree neighbours are outside [0.03,0.97]; inspect recorded viewport coordinates.");
        }

        private void AdvanceUntilRelease()
        {
            Vector3 start = arena.player.transform.position;
            int limit = 0;
            while (view.CinematicActive && limit++ < 1600)
            {
                Tick(new ArenaCommand { move = Vector2.right, run = true });
                Require(Vector3.ProjectOnPlane(arena.player.transform.position - start, Vector3.up).magnitude < .001f, "Player moved before the camera owner restored input.");
            }
            Require(limit < 1600 && current.release != null, "No actual camera ownership-release sample.");
        }

        private void RequireReturnEvidence(float expectedRestore)
        {
            Require(current.release != null && current.returnSamples >= 12, "No sustained pre-return movie frames were observed.");
            Require(current.firstReturnTime < expectedRestore - .1f && current.firstReturnTime > expectedRestore - 1.1f,
                "Return was not observed during the existing final movie interval.");
            Require(current.returnTravel > .1f, "Return weight changed without measurable displayed camera travel.");
            Require(current.releasePositionStep < .35f && current.releaseAngleStep < 3 && current.releaseFovStep < .8f,
                "Actual display pose/lens made a large jump across the ownership boundary.");
        }

        private void CancelMeteor(float time)
        {
            Fixture(); Begin(ArenaAttack.Meteor);
            int count = 0; while (arena.boss.MeteorMotor.SourceTime < time && view.CinematicActive && count++ < 1000) Tick(default);
            Require(view.CinematicActive, "Movie unexpectedly ended before cancellation probe.");
            SnapshotRelease(() => arena.boss.CancelAttack());
            Require(arena.boss.MeteorMotor.Cancelled && !view.CinematicActive, "Actual meteor cancellation retained camera ownership.");
            for (int i = 0; i < 24; i++) Tick(new ArenaCommand { move = Vector2.right });
        }

        private void CancelIgnitionByDefeat()
        {
            Fixture(6); Begin(ArenaAttack.Ignite);
            for (int i = 0; i < 150; i++) Tick(default);
            SnapshotRelease(() => arena.player.fighter.Receive(new ArenaHit { source = arena.boss.fighter,
                strike = arena.NewStrike(), damage = 100000, skill = "camera_qa_terminal" }));
            Require(arena.State == ArenaRoundState.Defeat && !view.CinematicActive, "Terminal state leaked ignition camera ownership.");
        }

        private void SnapshotRelease(Action release)
        {
            Vector3 p = view.transform.position; Quaternion q = view.transform.rotation; float f = cameraComponent.fieldOfView;
            release();
            current.explicitReleasePositionStep = Vector3.Distance(p, view.transform.position);
            current.explicitReleaseAngleStep = Quaternion.Angle(q, view.transform.rotation);
            current.explicitReleaseFovStep = Mathf.Abs(f - cameraComponent.fieldOfView);
            samples.Add(Capture("immediate_cancel_before_next_camera_tick", 0));
            Require(current.explicitReleasePositionStep <= .000001f && current.explicitReleaseAngleStep <= .001f && current.explicitReleaseFovStep <= .000001f,
                "EndCinematic cancellation route wrote the displayed transform or lens.");
        }

        private void RestartDuring(ArenaAttack skill, float time)
        {
            Fixture(); Begin(skill);
            int count = 0; while (SkillTime() < time && view.CinematicActive && count++ < 1000) Tick(default);
            Require(view.CinematicActive, "Movie ended before restart probe.");
            arena.Restart(true); previous = null;
            Require(!view.CinematicActive && !view.LockedOn && !arena.boss.IgniteMotor.Active && !arena.boss.MeteorMotor.Active,
                "Restart retained a movie owner, target lock or active cinematic motor.");
            // A round restart is an intentional spawn/camera reset, not a normal
            // movie transition, so an immediate camera cut is allowed here.
            Vector3 start = arena.player.transform.position;
            for (int i = 0; i < 45; i++) Tick(new ArenaCommand { move = Vector2.right, run = true });
            Require(Vector3.Distance(start, arena.player.transform.position) > .7f, "Restart did not restore normal movement.");
            Require(samples[samples.Count - 1].playerBodyVisible, "Restarted player's body was not in the viewport.");
        }

        private void OrdinaryInputContinuity()
        {
            Fixture(6);
            // Exercise the exact elapsed-time input entry point used by Update,
            // including a long frame. This is scripted QA, not native input proof.
            for (int i = 0; i < 240; i++)
            {
                float delta = i == 110 ? .11f : i % 5 == 0 ? 1f / 45 : 1f / 60;
                Quaternion before = view.transform.rotation;
                arena.SimulateElapsed(new ArenaCommand { move = i < 120 ? Vector2.left : Vector2.right, run = true,
                    look = new Vector2(i % 30 < 15 ? 45 : -45, i % 40 < 20 ? .4f : -.4f) }, delta);
                view.TickCamera(delta);
                var s = Capture("scripted_elapsed_input", delta); samples.Add(s);
                float angle = Quaternion.Angle(before, view.transform.rotation);
                current.maxAngularSpeed = Mathf.Max(current.maxAngularSpeed, angle / delta);
                Require(angle <= view.lockCameraSpeed * delta + .05f, "Displayed locked camera exceeded its angular limit under the real elapsed-input pipeline.");
                Require(Finite(s), "Non-finite camera pose during ordinary input."); previous = s;
            }
        }

        private void Tick(ArenaCommand command)
        {
            bool owned = view.CinematicActive;
            Vector3 oldPosition = view.transform.position; Quaternion oldRotation = view.transform.rotation; float oldFov = cameraComponent.fieldOfView;
            arena.Simulate(command, Step);
            bool released = owned && !view.CinematicActive;
            var beforeCameraTick = Capture("after_simulation_before_gameplay_camera", Step);
            if (released)
            {
                current.controlRestoreTime = SkillTime();
                current.releasePositionStep = Vector3.Distance(oldPosition, view.transform.position);
                current.releaseAngleStep = Quaternion.Angle(oldRotation, view.transform.rotation);
                current.releaseFovStep = Mathf.Abs(oldFov - cameraComponent.fieldOfView);
                current.release = beforeCameraTick; samples.Add(beforeCameraTick);
            }
            view.TickCamera(Step);
            var s = Capture("headless_actual_motor_tick", Step); samples.Add(s);
            Require(Finite(s), "Non-finite real camera state.");
            if (owned && view.CinematicReturnWeight > 0 && view.CinematicReturnWeight < 1)
            {
                if (current.firstReturnTime < 0) current.firstReturnTime = SkillTime();
                current.returnSamples++; current.returnTravel += Vector3.Distance(oldPosition, view.transform.position);
            }
            previous = s;
        }

        private float SkillTime()
        {
            if (arena.boss.MeteorMotor != null && (arena.boss.MeteorMotor.Active || arena.boss.MeteorMotor.SourceTime > 0)) return arena.boss.MeteorMotor.SourceTime;
            return arena.boss.IgniteMotor != null ? arena.boss.IgniteMotor.SourceTime : 0;
        }

        private Sample Capture(string clock, float interval)
        {
            var s = new Sample { test = current != null ? current.name : "native_observation", sourceClock = clock,
                frame = Time.frameCount, wall = Time.realtimeSinceStartupAsDouble - observationStarted, interval = interval, battle = arena.BattleTime,
                state = arena.State.ToString(), action = arena.boss.ActionName, phase = arena.boss.CombatPhase, sourceTime = SkillTime(),
                fov = cameraComponent.fieldOfView, aspect = cameraComponent.aspect, owned = view.CinematicActive, locked = view.LockedOn,
                focused = Application.isFocused, automaticSession = arena.automaticTick, automaticCamera = view.automaticTick,
                worldConstrained = view.WorldConstrainedThisTick,
                position = view.transform.position, rotation = view.transform.rotation, player = arena.player.transform.position, boss = arena.boss.transform.position,
                returnWeight = view.CinematicReturnWeight, hazardWeight = view.HazardFramingWeight, groupDistance = view.GroupFramingDistance };
            if (previous != null && interval > 0)
            {
                s.positionStep = Vector3.Distance(s.position, previous.position); s.angleStep = Quaternion.Angle(s.rotation, previous.rotation);
                s.angularSpeed = s.angleStep / interval;
            }
            Bounds playerBounds = arena.player.capsule.bounds;
            s.playerFootViewport = cameraComponent.WorldToViewportPoint(new Vector3(playerBounds.center.x, playerBounds.min.y + .04f, playerBounds.center.z));
            s.playerHeadViewport = cameraComponent.WorldToViewportPoint(new Vector3(playerBounds.center.x, playerBounds.max.y, playerBounds.center.z));
            s.playerCentreViewport = cameraComponent.WorldToViewportPoint(playerBounds.center);
            s.playerHeightFraction = Mathf.Abs(s.playerHeadViewport.y - s.playerFootViewport.y);
            s.playerBodyVisible = Visible(s.playerFootViewport) && Visible(s.playerHeadViewport) && Visible(s.playerCentreViewport);
            var meteor = arena.boss.MeteorMotor;
            if (meteor != null && meteor.Active && !meteor.Cancelled && meteor.Settings != null)
            {
                s.warningActive = meteor.presentation != null && meteor.presentation.WarningVisible;
                Vector3 towardCamera = Vector3.ProjectOnPlane(view.transform.position - meteor.Centre, Vector3.up).normalized;
                if (towardCamera.sqrMagnitude < .01f) towardCamera = Vector3.back;
                Vector3 towardPlayer = Vector3.ProjectOnPlane(arena.player.transform.position - meteor.Centre, Vector3.up).normalized;
                if (towardPlayer.sqrMagnitude < .01f) towardPlayer = towardCamera;
                Vector3 centre = meteor.Centre + Vector3.up * .08f; float r = meteor.Settings.radius;
                s.nearEdgeViewport = cameraComponent.WorldToViewportPoint(centre + towardCamera * r);
                s.nearLeftViewport = cameraComponent.WorldToViewportPoint(centre + Quaternion.AngleAxis(-12, Vector3.up) * towardCamera * r);
                s.nearRightViewport = cameraComponent.WorldToViewportPoint(centre + Quaternion.AngleAxis(12, Vector3.up) * towardCamera * r);
                s.nearestEscapeViewport = cameraComponent.WorldToViewportPoint(centre + towardPlayer * r);
                s.nearArcVisible = Visible(s.nearEdgeViewport) && Visible(s.nearLeftViewport) && Visible(s.nearRightViewport);
                s.nearestEscapeVisible = Visible(s.nearestEscapeViewport);
            }
            return s;
        }

        private bool Visible(Vector3 viewport)
        { return viewport.z > cameraComponent.nearClipPlane && viewport.x >= .03f && viewport.x <= .97f && viewport.y >= .03f && viewport.y <= .97f; }

        private static bool Finite(Sample sample)
        { return IsFinite(sample.position.x) && IsFinite(sample.position.y) && IsFinite(sample.position.z) && IsFinite(sample.rotation.x)
            && IsFinite(sample.rotation.y) && IsFinite(sample.rotation.z) && IsFinite(sample.rotation.w) && IsFinite(sample.fov); }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        private void LateUpdate()
        {
            if (!observe || !ready || saved) return;
            double now = Time.realtimeSinceStartupAsDouble; float delta = (float)(now - previousWall); previousWall = now;
            var s = Capture("actual_lateupdate_wall_clock_native_driver", delta);
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (s.focused && keyboard != null)
            {
                s.keyboardMove = keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed;
                s.dodgeEdge = keyboard.spaceKey.wasPressedThisFrame; s.lockEdge = keyboard.tabKey.wasPressedThisFrame;
                s.mouseDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            }
            if (s.focused) focusedFrames++;
            if (s.keyboardMove || s.dodgeEdge || s.lockEdge || s.mouseDelta.sqrMagnitude > 0) inputFrames++;
            bool restarted = s.battle < previousBattle; previousBattle = s.battle;
            if (previous != null && !restarted && previous.locked && s.locked && !previous.owned && !s.owned && previous.focused && s.focused && delta > .000001f)
            {
                ordinaryFrames++; maxOrdinaryAngularSpeed = Mathf.Max(maxOrdinaryAngularSpeed, s.angularSpeed);
                // Monotonic display sampling can differ slightly from Time.deltaTime.
                // Warnings are diagnostic, not automatic visual-failure verdicts.
                if (s.angleStep > view.lockCameraSpeed * delta + .15f) angularWarnings++;
            }
            samples.Add(s); previous = s;
            if (restarted || seenEvents > arena.Events.Count) seenEvents = 0;
            for (; seenEvents < arena.Events.Count; seenEvents++) observedEvents.Add(arena.Events[seenEvents]);
            if (now - observationStarted >= observationSeconds || samples.Count >= 100000)
            {
                Save(now - observationStarted >= observationSeconds);
                if (quitObservation) Application.Quit(0);
            }
        }

        private void OnApplicationQuit()
        { if (observe && ready && !saved) Save(false); }

        private void Save(bool complete)
        {
            if (saved || output == null) return;
            saved = true;
            var report = new Report { scope = observe ? "READ_ONLY_NATIVE_INPUT_CAMERA_OBSERVATION_NOT_VISUAL_OR_PERFORMANCE_ACCEPTANCE"
                    : "REAL_MOTOR_AND_CAMERA_HEADLESS_CONTRACTS_NOT_NATIVE_INPUT_OR_VISUAL_OR_PERFORMANCE_ACCEPTANCE",
                build = Application.version, status = observe ? "observation_only_requires_review" : rows.All(x => x.passed) && complete ? "passed_bounded_camera_contracts" : "failed_or_incomplete",
                passed = !observe && complete && rows.All(x => x.passed), completed = complete, nativeInputObservation = observe,
                width = Screen.width, height = Screen.height, aspect = cameraComponent != null ? cameraComponent.aspect : 0,
                duration = observe ? (float)(Time.realtimeSinceStartupAsDouble - observationStarted) : 0,
                focusedFrames = focusedFrames, inputObservedFrames = inputFrames, ordinaryLockedFrames = ordinaryFrames,
                ordinaryAngularLimitWarnings = angularWarnings, maxOrdinaryAngularSpeed = maxOrdinaryAngularSpeed,
                cases = rows.ToArray(), samples = samples.ToArray(), events = observedEvents.ToArray() };
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("SAM_CAMERA_EVIDENCE_SAVED " + output);
        }

        private static void Require(bool condition, string text)
        { if (!condition) throw new InvalidOperationException(text); }
    }
}
