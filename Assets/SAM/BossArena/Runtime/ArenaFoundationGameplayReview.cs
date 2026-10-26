using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction.BossArena
{
    // Opt-in real-wall-clock review of the ordinary session and Boss brain.
    // Fixtures and commands are scripted and visible. This is not native input,
    // the final patrol/skill AI, an artistic pass, or a performance benchmark.
    public sealed class ArenaFoundationGameplayReview : MonoBehaviour
    {
        [Serializable] private sealed class Frame
        {
            public double wallTime, rawFrameInterval, simulatedTime, battleTime, stageTime;
            public int frame, stage;
            public string stageName, roundState, bossState, motorState, clip, foundationClip, playerAction;
            public float clipTime, headingYaw, bossYaw, bossHealth, playerHealth;
            public Vector3 boss, player, leftFoot, rightFoot, cameraPosition, cameraEuler;
            public bool attacking, blocked, decisionEnabled, playerInvulnerable, lockedOn, focused;
            public bool bossInsideCameraFrustum, playerInsideCameraFrustum;
        }
        [Serializable] private sealed class Observation
        {
            public double observedWallTime, simulatedTime, stageTime;
            public int stage;
            public string kind, detail;
            public Vector3 position, size;
            public ArenaCommand command;
            public ArenaEventRecord combat;
        }
        [Serializable] private sealed class Coverage
        {
            public bool defaultAiDamagedPlayer, walkStartFinished, walkStopFinished, turnRight90Finished, turnRight180Finished;
            public bool obstructionObserved, obstructionRebalanced, playerSwordDamagedBoss, authoredHitObserved;
            public bool scriptedLockObserved, scriptedDodgeObserved, playerInvulnerableObserved;
            public bool injectedDeathObserved, deathClipCompleted, restartAliveAndGuard;
        }
        [Serializable] private sealed class Report
        {
            public int schema = 1, width, height, captureFramerate, vSyncCount, targetFrameRate;
            public string kind = "realtime_scripted_foundation_gameplay_review_not_native_input_or_performance";
            public string unity, build, graphics, view;
            public string clockPolicy = "Each displayed Update consumes its measured realtimeSinceStartupAsDouble difference through the ordinary ArenaSession.Simulate; no fixed capture clock, dropped-time clamp, prerecorded poses or offline frame stepping.";
            public string fixturePolicy = "Seven explicitly labelled fixture resets. Normal Boss decisions in approach/turn/obstruction/restart. Player action fixture disables Boss decisions. Death fixture calls one labelled direct lethal Receive for lifecycle only; it is not collision evidence.";
            public string visibilityPolicy = "Recorded camera-frustum containment only; does not certify occlusion, visual quality, or sole geometry. No BakeMesh cost is added by this driver.";
            public bool completeSequence, nativeInputVerified = false, finalAiVerified = false, performanceVerified = false, artisticPass = false;
            public double wallDuration, simulatedDuration, maximumFrameInterval;
            public Coverage observedCoverage;
            public Frame[] frames;
            public Observation[] observations;
            public string[] errors;
        }
        private static readonly string[] StageNames = {
            "DEFAULT SPAWN / ORDINARY AI APPROACH", "90 DEGREE FACING / ORDINARY AI", "180 DEGREE FACING / ORDINARY AI",
            "REAL WORLD BLOCKER / FOOT SETTLE", "SCRIPTED PLAYER ATTACK, LOCK, RUN, DODGE",
            "DIRECT LETHAL EVENT / DEATH LIFECYCLE ONLY", "RESTART / ORDINARY AI RESTORED" };
        private static readonly double[] Durations = { 9, 8, 10, 4, 6, 5, 9 };
        private static readonly double[][] Markers = {
            Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>(), new[] { .35 },
            new[] { .35, .60, 1.80, 2.80, 3.50, 3.60, 4.20 }, new[] { .50 }, new[] { .70 } };
        private ArenaSession arena;
        private ArenaFoundationMotor motor;
        private Camera camera;
        private SkinnedMeshRenderer bossMesh;
        private Vector3 originalBossStart, originalPlayerStart;
        private readonly List<Frame> frames = new List<Frame>(4096);
        private readonly List<Observation> observations = new List<Observation>(2048);
        private readonly List<string> errors = new List<string>();
        private readonly Coverage coverage = new Coverage();
        private readonly Plane[] frustum = new Plane[6];
        private string output, view;
        private int stage, marker, eventCursor;
        private double started, previousWall, stageTime, simulatedTime, maximumFrameInterval;
        private bool ready, displayReady, finishing, written, canWrite, stageEnding, attackEdge, dodgeEdge, lockEdge;
        private GameObject blocker;
        private Material blockerMaterial;
        private GUIStyle label;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--sam-foundation-gameplay-review") < 0) return;
            foreach (string other in new[] { "--sam-benchmark", "--sam-foundation-review", "--sam-foundation-contracts" })
                if (Array.IndexOf(args, other) >= 0)
                { Debug.LogError("Gameplay review cannot share its simulation with " + other); Application.Quit(2); return; }
            new GameObject("Production18 real time scripted gameplay review").AddComponent<ArenaFoundationGameplayReview>();
        }
        private static string Arg(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        private IEnumerator Start()
        {
            yield return null;
            try
            {
                output = Path.GetFullPath(Arg("--sam-output", Path.Combine(Application.persistentDataPath, "foundation_gameplay_review")));
                if (File.Exists(Path.Combine(output, "realtime_gameplay.json"))) throw new IOException("Preserve existing evidence; use a new directory.");
                Directory.CreateDirectory(output);
                canWrite = true;
                arena = FindFirstObjectByType<ArenaSession>();
                if (arena == null) throw new InvalidOperationException("Actual ArenaSession is missing.");
                motor = arena.boss.GetComponent<ArenaFoundationMotor>();
                if (motor == null || !motor.Initialized) throw new InvalidOperationException("The saved scene requires an initialized foundation motor.");
                originalBossStart = arena.bossStart; originalPlayerStart = arena.playerStart;
                view = Arg("--sam-view", "three_quarter");
                Application.runInBackground = true; Time.captureFramerate = 0; Time.timeScale = 1;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
                Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
                arena.automaticTick = false; arena.view.automaticTick = false;
                arena.boss.decisionEnabled = true;
                arena.Restart(false);
                camera = arena.view.GetComponent<Camera>(); camera.fieldOfView = 43;
                bossMesh = arena.boss.GetComponentInChildren<SkinnedMeshRenderer>();
                foreach (string id in motor.foundation.ids) { motor.foundation.Motion(id); motor.foundation.Clip(id); }
                PositionCamera();
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                displayReady = true;
            }
            catch (Exception exception) { Fail(exception); yield break; }
            // Warm the actual display, HUD and independent framebuffer recorder.
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(1.2f);
            yield return new WaitForEndOfFrame();
            try
            {
                started = previousWall = Time.realtimeSinceStartupAsDouble;
                BeginStage(0); ready = true; Capture(0);
            }
            catch (Exception exception) { Fail(exception); }
        }
        private void Update()
        {
            if (!ready || finishing || written) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { Observe("review_cancelled", "Physical Escape / incomplete review"); Save(false); Application.Quit(); return; }
            try
            {
                if (Time.captureFramerate != 0 || Mathf.Abs(Time.timeScale - 1) > .00001f)
                    throw new InvalidOperationException("The real-time simulation clock was changed.");
                double now = Time.realtimeSinceStartupAsDouble;
                double raw = now - previousWall; previousWall = now;
                if (raw < 0 || raw > 5) throw new InvalidOperationException("Runtime clock gap cannot be reviewed safely: " + raw);
                maximumFrameInterval = Math.Max(maximumFrameInterval, raw);
                Advance(raw);
                PositionCamera(); Capture(raw);
            }
            catch (Exception exception) { Fail(exception); }
        }
        private void Advance(double remaining)
        {
            while (remaining > .0000001 && !finishing)
            {
                ApplyMarkers();
                if (!stageEnding && stageTime >= Durations[stage] - .0000001)
                {
                    stageEnding = true; arena.boss.decisionEnabled = false;
                    Observe("fixture_duration_reached", "Finish the committed clip and attack before the next fixture; no pose truncation.");
                }
                bool settled = !arena.boss.Attacking && (!motor.Busy || motor.State == ArenaFoundationMotor.MotionState.DeathHold);
                if (stageEnding && settled)
                {
                    Observe("fixture_completed", StageNames[stage]);
                    if (stage + 1 == StageNames.Length)
                    { finishing = true; StartCoroutine(Finish()); return; }
                    BeginStage(stage + 1); continue;
                }
                if (stageTime > Durations[stage] + 6)
                    throw new InvalidOperationException("Fixture did not settle after its full-motion extension: " + StageNames[stage]);
                double step = Math.Min(.25, remaining);
                if (!stageEnding) step = Math.Min(step, Durations[stage] - stageTime);
                if (marker < Markers[stage].Length) step = Math.Min(step, Markers[stage][marker] - stageTime);
                if (step <= .0000001) continue;
                ArenaCommand command = default;
                if (stage == 4 && !stageEnding)
                {
                    command.attack = attackEdge; command.dodge = dodgeEdge; command.toggleLock = lockEdge;
                    if (stageTime >= 2.8 && stageTime < 3.5) { command.move = Vector2.right; command.run = true; }
                }
                if (command.attack || command.dodge || command.toggleLock)
                    observations.Add(new Observation { kind = "scripted_command_edge", detail = "ArenaCommand via ordinary Session.Simulate, not native keyboard/mouse", command = command,
                        observedWallTime = Wall, simulatedTime = simulatedTime, stageTime = stageTime, stage = stage });
                arena.Simulate(command, (float)step);
                attackEdge = dodgeEdge = lockEdge = false;
                simulatedTime += step; stageTime += step; remaining -= step;
                DrainEvents(); UpdateCoverage();
            }
        }
        private void BeginStage(int value)
        {
            DrainEvents(); RemoveBlocker(); stage = value; stageTime = 0; marker = 0; stageEnding = false;
            attackEdge = dodgeEdge = lockEdge = false;
            bool normalAi = stage != 4 && stage != 5;
            if (stage == 0 || stage == 6) { arena.bossStart = originalBossStart; arena.playerStart = originalPlayerStart; }
            else if (stage == 1) { arena.bossStart = new Vector3(0, .05f, 0); arena.playerStart = new Vector3(6, .05f, 0); }
            else if (stage == 2) { arena.bossStart = new Vector3(0, .05f, 0); arena.playerStart = new Vector3(0, .05f, -6); }
            else if (stage == 3) { arena.bossStart = new Vector3(0, .05f, -4); arena.playerStart = new Vector3(0, .05f, 5); }
            else { arena.bossStart = new Vector3(0, .05f, 0); arena.playerStart = new Vector3(0, .05f, stage == 4 ? -1.35f : -5); }
            arena.Restart(stage != 6);
            arena.boss.decisionEnabled = normalAi;
            if (stage == 1 || stage == 2 || stage == 3) arena.boss.ResetBoss(arena.bossStart, Quaternion.identity);
            Physics.SyncTransforms(); eventCursor = 0; DrainEvents();
            Observe("fixture_reset", "SCRIPTED scene fixture: " + StageNames[stage]);
            observations.Add(new Observation { kind = "fixture_positions", detail = "Explicit fixture reset, not gameplay locomotion", observedWallTime = Wall,
                simulatedTime = simulatedTime, stageTime = 0, stage = stage, position = arena.boss.transform.position, size = arena.player.transform.position });
            if (stage == 6) coverage.restartAliveAndGuard = !arena.boss.fighter.Dead && !arena.player.fighter.Dead && motor.ClipId == "guard" && !motor.Busy;
            PositionCamera();
        }
        private void ApplyMarkers()
        {
            while (marker < Markers[stage].Length && stageTime >= Markers[stage][marker] - .0000001)
            {
                int at = marker++;
                if (stage == 3) CreateBlocker();
                else if (stage == 4)
                {
                    if (at == 0) lockEdge = true;
                    else if (at == 1 || at == 2) attackEdge = true;
                    else if (at == 5) dodgeEdge = true;
                    else Observe("scripted_movement_boundary", at == 3 ? "RUN RIGHT begins" : at == 4 ? "RUN ends" : "DODGE observation hold");
                }
                else if (stage == 5)
                {
                    Observe("direct_damage_injection", "Lifecycle fixture only: one lethal ArenaFighter.Receive, NOT collision or player-attack evidence.");
                    bool received = arena.boss.fighter.Receive(new ArenaHit { source = arena.player.fighter, strike = arena.NewStrike(),
                        damage = arena.boss.fighter.maximumHealth + 1, push = 0, point = arena.boss.transform.position,
                        direction = Vector3.forward, skill = "SCRIPTED_DIRECT_LETHAL_LIFECYCLE_ONLY" });
                    coverage.injectedDeathObserved = received && arena.boss.fighter.Dead && arena.State == ArenaRoundState.Victory;
                    DrainEvents();
                }
                else if (stage == 6)
                {
                    DrainEvents(); arena.Restart(true); arena.boss.decisionEnabled = true; eventCursor = 0;
                    Observe("scripted_restart_begin", "Ordinary Restart(true) after the Ready display; native R/Enter not tested."); DrainEvents();
                }
            }
        }
        private void CreateBlocker()
        {
            blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "SCRIPTED REVIEW / actual world blocker"; blocker.layer = 8;
            blocker.transform.position = arena.boss.transform.position + arena.boss.transform.forward * 1.05f + Vector3.up * .70f;
            blocker.transform.localScale = new Vector3(2.4f, 1.4f, .25f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                blockerMaterial = new Material(shader); blockerMaterial.SetColor("_BaseColor", new Color(.32f, .36f, .40f, 1));
                blocker.GetComponent<Renderer>().sharedMaterial = blockerMaterial;
            }
            Physics.SyncTransforms();
            observations.Add(new Observation { kind = "actual_world_blocker_inserted", detail = "Layer 8 cube inserted after the committed start, real collider enabled",
                observedWallTime = Wall, simulatedTime = simulatedTime, stageTime = stageTime, stage = stage,
                position = blocker.transform.position, size = blocker.transform.localScale });
        }
        private void RemoveBlocker()
        {
            if (blocker != null) { blocker.SetActive(false); Destroy(blocker); blocker = null; }
            if (blockerMaterial != null) { Destroy(blockerMaterial); blockerMaterial = null; }
        }
        private void DrainEvents()
        {
            if (arena == null) return;
            while (eventCursor < arena.Events.Count)
            {
                var value = arena.Events[eventCursor++];
                observations.Add(new Observation { kind = "combat_event", observedWallTime = Wall, simulatedTime = simulatedTime,
                    stageTime = stageTime, stage = stage, combat = value });
                if (stage == 0 && value.kind == "hit" && value.source == "Boss" && value.target == "Player") coverage.defaultAiDamagedPlayer = true;
                if (value.kind == "boss_start_finished") coverage.walkStartFinished = true;
                if (value.kind == "boss_stop_finished") coverage.walkStopFinished = true;
                if (stage == 1 && value.kind == "boss_turn_finished" && value.skill == "turn_right_90") coverage.turnRight90Finished = true;
                if (stage == 2 && value.kind == "boss_turn_finished" && value.skill == "turn_right_180") coverage.turnRight180Finished = true;
                if (stage == 3 && value.kind == "boss_locomotion_obstructed") coverage.obstructionObserved = true;
                if (stage == 3 && value.kind == "boss_foundation_rebalanced") coverage.obstructionRebalanced = true;
                if (stage == 4 && value.kind == "hit" && value.source == "Player" && value.target == "Boss" && value.skill == "player_sword") coverage.playerSwordDamagedBoss = true;
                if (stage == 4 && value.kind == "player_dodge_started") coverage.scriptedDodgeObserved = true;
            }
        }
        private void UpdateCoverage()
        {
            if (stage == 4)
            {
                coverage.scriptedLockObserved |= arena.view.LockedOn;
                coverage.playerInvulnerableObserved |= arena.player.fighter.Invulnerable;
                coverage.authoredHitObserved |= motor.ClipId == "hit_light";
            }
            if (stage == 5) coverage.deathClipCompleted |= motor.State == ArenaFoundationMotor.MotionState.DeathHold;
        }
        private double Wall => started > 0 ? Time.realtimeSinceStartupAsDouble - started : 0;
        private void Observe(string kind, string detail)
        {
            observations.Add(new Observation { kind = kind, detail = detail, stage = stage, stageTime = stageTime,
                observedWallTime = Wall, simulatedTime = simulatedTime });
        }
        private void PositionCamera()
        {
            if (camera == null || arena == null) return;
            // Fit both real actors with room above the floor and outside the HUD.
            // The camera is a labelled review view, not the normal user camera.
            Bounds bounds = new Bounds(arena.boss.transform.position + Vector3.up * 1.7f, new Vector3(2.2f, 3.6f, 2.2f));
            bounds.Encapsulate(new Bounds(arena.player.transform.position + Vector3.up * .9f, new Vector3(1.3f, 1.9f, 1.3f)));
            Vector3 offset = view == "side" ? new Vector3(1, .30f, .13f) : view == "front" ? new Vector3(.10f, .28f, -1) : new Vector3(.85f, .30f, -.60f);
            Quaternion rotation = Quaternion.LookRotation(-offset.normalized);
            float verticalTan = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            float distance = 7;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = Quaternion.Inverse(rotation) * (point - bounds.center);
                distance = Mathf.Max(distance, Mathf.Abs(local.y) / (verticalTan * .60f) - local.z,
                    Mathf.Abs(local.x) / (verticalTan * camera.aspect * .84f) - local.z);
            }
            camera.transform.SetPositionAndRotation(bounds.center + offset.normalized * (distance + .4f), rotation);
        }
        private void Capture(double raw)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            Bounds playerBounds = arena.player.capsule.bounds;
            Bounds bossBounds = bossMesh != null ? bossMesh.bounds : arena.boss.capsule.bounds;
            frames.Add(new Frame { wallTime = Wall, rawFrameInterval = raw, simulatedTime = simulatedTime, battleTime = arena.BattleTime,
                stageTime = stageTime, frame = Time.frameCount, stage = stage, stageName = StageNames[stage], roundState = arena.State.ToString(),
                bossState = arena.boss.StateName, motorState = motor.State.ToString(), clip = arena.boss.animationDriver.ClipName,
                foundationClip = arena.boss.Attacking ? "" : motor.ClipId,
                clipTime = arena.boss.Attacking ? arena.boss.SkillTime : motor.StateTime, headingYaw = motor.HeadingYaw,
                bossYaw = arena.boss.transform.eulerAngles.y, bossHealth = arena.boss.fighter.Health, playerHealth = arena.player.fighter.Health,
                boss = arena.boss.transform.position, player = arena.player.transform.position, leftFoot = arena.boss.Socket("socket_foot.L").position,
                rightFoot = arena.boss.Socket("socket_foot.R").position, attacking = arena.boss.Attacking, blocked = motor.Blocked,
                decisionEnabled = arena.boss.decisionEnabled, playerInvulnerable = arena.player.fighter.Invulnerable, lockedOn = arena.view.LockedOn,
                playerAction = arena.player.ActionName, focused = Application.isFocused, cameraPosition = camera.transform.position,
                cameraEuler = camera.transform.eulerAngles, bossInsideCameraFrustum = FullyInsideFrustum(bossBounds), playerInsideCameraFrustum = FullyInsideFrustum(playerBounds) });
        }
        private bool FullyInsideFrustum(Bounds bounds)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                foreach (var plane in frustum) if (plane.GetDistanceToPoint(point) < 0) return false;
            }
            return true;
        }
        private IEnumerator Finish()
        {
            Observe("review_terminal_hold", "Full final action settled; hold the actual terminal view for recording.");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return new WaitForEndOfFrame(); Save(true); Application.Quit();
        }
        private void OnGUI()
        {
            if (!displayReady) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 19, normal = { textColor = Color.white }, wordWrap = true };
            float scale = Screen.width / 1920f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            GUI.Box(new Rect(24, 128, 1872, 78), GUIContent.none);
            string title = finishing ? "SCRIPTED REVIEW COMPLETE / TERMINAL HOLD" : StageNames[stage];
            string source = stage == 5 ? "DIRECT LETHAL RECEIVE: lifecycle only, NOT collision evidence" : "Scripted fixture / ArenaCommand inputs; native keyboard and mouse NOT verified";
            GUI.Label(new Rect(38, 134, 1844, 70), "FOUNDATION 18 / " + title + "\n" + source + "\nReal wall clock / ordinary Session + Brain / R1 review only / " + view, label);
        }
        private void Save(bool complete)
        {
            if (written || !canWrite || string.IsNullOrEmpty(output)) return; written = true; DrainEvents();
            var report = new Report { width = Screen.width, height = Screen.height, captureFramerate = Time.captureFramerate,
                vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate, unity = Application.unityVersion,
                build = Application.version, graphics = SystemInfo.graphicsDeviceName, view = view, completeSequence = complete,
                wallDuration = Wall, simulatedDuration = simulatedTime, maximumFrameInterval = maximumFrameInterval,
                observedCoverage = coverage, frames = frames.ToArray(), observations = observations.ToArray(), errors = errors.ToArray() };
            File.WriteAllText(Path.Combine(output, "realtime_gameplay.json"), JsonUtility.ToJson(report, true));
            Debug.Log("FOUNDATION_GAMEPLAY_REVIEW_COMPLETE sequence=" + complete + " frames=" + frames.Count + " errors=" + errors.Count);
        }
        private void Fail(Exception exception)
        {
            errors.Add(exception.ToString()); Debug.LogException(exception); ready = false; finishing = true;
            Save(false); Application.Quit(2);
        }
        private void OnApplicationQuit() { if (!written) Save(false); }
        private void OnDestroy() { RemoveBlocker(); }
    }
}
