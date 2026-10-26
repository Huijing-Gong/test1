using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction.BossArena
{
    // A visible review using the ordinary Session/Brain and actual elapsed
    // wall time. The fixtures and command edges are explicitly labelled.
    public sealed class ArenaDashGameplayReview : MonoBehaviour
    {
        [Serializable] private sealed class Frame
        {
            public int frame, stage, activeLoops, activeVoices, activeEffects, fistQueries, flameQueries;
            public double wallTime, simulatedTime, rawInterval, battleTime, stageTime;
            public string stageName, phase, clip, bossState, playerAction, actualFlameShader;
            public float sourceTime, phaseTime, bossHealth, playerHealth, yaw;
            public Vector3 boss, player, fist, leftFoot, rightFoot, velocity, lockedForward, cameraPosition, cameraEuler;
            public Bounds bossBounds, playerBounds;
            public bool locked, strikeOpen, invulnerable, focused, patrolInvalid, actualAi, insideFrustum;
            public long execution, strike;
        }
        [Serializable] private sealed class Observation
        {
            public int stage;
            public string kind, detail;
            public double wallTime, simulatedTime, stageTime;
            public ArenaCommand command;
            public ArenaEventRecord combat;
            public ArenaDashPresentation.PresentationEvent presentation;
        }
        [Serializable] private sealed class Report
        {
            public int schema = 1, width, height, captureFramerate, targetFrameRate, vSyncCount;
            public string kind = "real_wall_clock_dash_gameplay_scripted_fixture_review";
            public string unity, build, graphics, view;
            public string clockPolicy = "Each display update consumes measured real elapsed time in the ordinary Session, with 1/120 maximum substeps. No captureFramerate, fixed simulation per displayed frame or dropped-time clamp.";
            public string fixturePolicy = "First three fixtures explicitly isolate the actual skill by disabling patrol feasibility; the final fixture uses saved default spawn and normal R2 AI with that requirement enabled. --sam-dash-isolated-review omits only the labelled final fixture for interim motion/effect diagnosis.";
            public string visualScope = "Review camera frames both actual actors; frustum containment does not certify occlusion or art. Actual framebuffer/audio capture is separately timestamped. No CPU sole probe or performance acceptance is part of this review.";
            public bool completeSequence, isolatedOnly, nativeInputVerified = false, performanceVerified = false, roundPassed = false;
            public double wallDuration, simulatedDuration, maximumFrameInterval, finalUnsimulatedBoundary, startedUnityRealtime;
            public Frame[] frames;
            public Observation[] observations;
            public string[] errors;
        }
        private static readonly string[] Titles = { "STATIONARY TARGET / ONE PUNCH + FIRE", "LATE PLAYER DODGE / ACTUAL MISS", "MOVING TARGET / TRACK THEN LOCK", "DEFAULT R2 AI / COMPLETE PATROL LOOP" };
        private ArenaSession arena;
        private ArenaDashMotor dash;
        private Camera camera;
        private SkinnedMeshRenderer mesh;
        private Vector3 originalBoss, originalPlayer;
        private string output, view;
        private readonly List<Frame> frames = new List<Frame>(7000);
        private readonly List<Observation> observations = new List<Observation>();
        private readonly List<string> errors = new List<string>();
        private readonly Plane[] planes = new Plane[6];
        private int stage, eventCursor, presentationCursor, releaseBefore;
        private double started, previous, simulated, stageTime, maxInterval, finalBoundary, settledAt;
        private float firstHealth;
        private bool ready, displayReady, finishing, written, writable, isolated, began, dodgeIssued, stageSettled;
        private GUIStyle label;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--sam-dash-review") < 0) return;
            foreach (string flag in new[] { "--sam-benchmark", "--sam-foundation-review", "--sam-foundation-gameplay-review", "--sam-foundation-contracts", "--sam-dash-contracts" })
                if (Array.IndexOf(args, flag) >= 0) { Debug.LogError("Multiple simulation owners: " + flag); Application.Quit(2); return; }
            new GameObject("Production18 actual dash gameplay review").AddComponent<ArenaDashGameplayReview>();
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
                output = Path.GetFullPath(Arg("--sam-output", Path.Combine(Application.persistentDataPath, "dash_review")));
                if (File.Exists(Path.Combine(output, "realtime_dash.json"))) throw new IOException("Preserve old evidence; choose a new run.");
                Directory.CreateDirectory(output); writable = true;
                isolated = Array.IndexOf(Environment.GetCommandLineArgs(), "--sam-dash-isolated-review") >= 0;
                arena = FindFirstObjectByType<ArenaSession>(); dash = arena != null ? arena.boss.DashMotor : null;
                Require(dash != null && dash.Initialized && dash.presentation != null, "Actual saved dash scene is not ready.");
                originalBoss = arena.bossStart; originalPlayer = arena.playerStart;
                arena.automaticTick = false; arena.view.automaticTick = false;
                arena.boss.decisionEnabled = false; arena.Restart(false);
                view = Arg("--sam-view", "three_quarter");
                Application.runInBackground = true; Time.captureFramerate = 0; Time.timeScale = 1;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
                Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
                camera = arena.view.GetComponent<Camera>(); camera.fieldOfView = 43;
                mesh = arena.boss.GetComponentInChildren<SkinnedMeshRenderer>();
                foreach (string id in dash.clips.ids) { dash.clips.Motion(id); dash.clips.Clip(id); dash.clips.Events(id); }
                PositionCamera(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true; displayReady = true;
            }
            catch (Exception e) { Fail(e); yield break; }
            yield return null; yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(1.2f); yield return new WaitForEndOfFrame();
            try { started = previous = Time.realtimeSinceStartupAsDouble; BeginStage(0); ready = true; Capture(0); }
            catch (Exception e) { Fail(e); }
        }
        private void BeginStage(int value)
        {
            Drain(); stage = value; stageTime = 0; settledAt = -1; began = dodgeIssued = stageSettled = false;
            arena.boss.useDashSampleAI = value == 3; arena.boss.decisionEnabled = value == 3;
            dash.requirePatrolFeasibility = value == 3;
            arena.bossStart = value == 3 ? originalBoss : new Vector3(0, .025f, 0);
            arena.playerStart = value == 3 ? originalPlayer : new Vector3(0, .05f, 5);
            arena.Restart(true);
            if (value != 3) arena.boss.ResetBoss(arena.bossStart, Quaternion.identity);
            arena.player.view = value == 3 ? arena.view : null;
            firstHealth = arena.player.fighter.Health; releaseBefore = dash.presentation.ReleaseCount; eventCursor = 0;
            Observe("fixture_started", Titles[value] + (value == 3 ? "; actual saved default R2 AI" : "; isolated skill with explicit patrol gate disabled"));
            PositionCamera();
        }
        private void Update()
        {
            if (!ready || finishing || written) return;
            // Escape releases the cursor, as requested by the user; it does not
            // inject a pause into either the review or the combat simulation.
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            try
            {
                Require(Time.captureFramerate == 0 && Mathf.Abs(Time.timeScale - 1) < .00001f, "Real-time clock changed.");
                double now = Time.realtimeSinceStartupAsDouble, raw = now - previous; previous = now;
                Require(raw >= 0 && raw <= 5, "Unreviewable wall-clock interval: " + raw);
                maxInterval = Math.Max(maxInterval, raw); Advance(raw); PositionCamera(); Capture(raw);
            }
            catch (Exception e) { Fail(e); }
        }
        private void Advance(double remaining)
        {
            while (remaining > .0000001 && !finishing)
            {
                if (stage != 3 && !began && stageTime >= .25)
                {
                    Require(arena.boss.TrySkill(ArenaAttack.DashPunch), "Actual Boss rejected isolated skill start."); began = true;
                }
                if (stage == 3)
                {
                    Require(!arena.boss.PatrolPlanner.Invalid, "Actual patrol failed: " + arena.boss.PatrolPlanner.Failure);
                    if (arena.Events.Count(e => e.kind == "dash_recovery_complete") >= 3) { arena.boss.decisionEnabled = false; stageSettled = true; }
                }
                else if (began && !dash.Active) stageSettled = true;
                if (stageSettled)
                {
                    if (settledAt < 0) { settledAt = stageTime; Observe("source_recovery_settled", "Continue actual simulation through owned sound/effect tails before any reset."); }
                    bool cleared = dash.presentation.OwnedLoopHandleCount == 0 && dash.presentation.ActiveVoiceCount == 0 && dash.presentation.ActiveEffectCount == 0;
                    Require(stageTime - settledAt < 2, "Owned sound/effect tails failed to clear after complete recovery.");
                    if (stageTime - settledAt >= .65 && cleared)
                    {
                        ValidateStage(); Observe("fixture_completed", "Full source recovery and owned presentation tails completed; actual collision evidence checked.");
                        if (stage == (isolated ? 2 : 3))
                        { finalBoundary = remaining; finishing = true; StartCoroutine(Finish()); return; }
                        BeginStage(stage + 1); continue;
                    }
                }
                Require(stageTime < (stage == 3 ? 55 : 10), "Review fixture did not complete its actual full action: " + Titles[stage]);
                ArenaCommand command = default;
                if (!stageSettled && stage == 1 && dash.Phase == ArenaDashPhase.LockRelease && !dodgeIssued &&
                    dash.PhaseTime >= dash.clips.EventTime("dash_lock_release", "strike_open") - .12f)
                { command.dodge = true; command.move = Vector2.right; dodgeIssued = true; }
                if (!stageSettled && stage == 2)
                {
                    if (dash.IsSliding && dash.PhaseTime < .32f) { command.move = Vector2.right; command.run = true; }
                    else if (dash.IsLocked && dash.Phase == ArenaDashPhase.LockRelease) { command.move = Vector2.left; command.run = true; }
                }
                if (command.dodge)
                    observations.Add(new Observation { stage = stage, kind = "actual_scripted_command", detail = "Late dodge through ordinary player controller",
                        command = command, wallTime = Wall, simulatedTime = simulated, stageTime = stageTime });
                double step = Math.Min(1d / 120, remaining);
                arena.Simulate(command, (float)step); simulated += step; stageTime += step; remaining -= step;
                Drain();
            }
        }
        private void ValidateStage()
        {
            if (stage == 0)
                Require(arena.Events.Count(e => e.kind == "hit" && e.skill == "dash_punch") == 1 &&
                    Mathf.Abs(firstHealth - arena.player.fighter.Health - dash.damage) < .001f, "Stationary target did not take exactly one punch/fire hit.");
            else if (stage == 1)
                Require(dodgeIssued && dash.Released && arena.player.fighter.Health == firstHealth &&
                    !arena.Events.Any(e => e.kind == "hit" && e.skill == "dash_punch"), "Late actual dodge did not avoid the completed punch.");
            else if (stage == 2) Require(dash.Released && !dash.Aborted, "Moving-target execution did not reach its locked release.");
            else
            {
                var starts = arena.Events.Where(e => e.kind == "boss_patrol_started").ToArray();
                var ends = arena.Events.Where(e => e.kind == "boss_patrol_completed").ToArray();
                Require(starts.Length >= 3 && ends.Length >= 3 && arena.Events.Any(e => e.kind == "dash_strike_open"), "Default AI lacks its complete patrol/punch loop.");
                for (int i = 0; i < 2; i++) Require(ends[i].time - starts[i].time >= 3.991 && ends[i].time - starts[i].time <= 6.009, "Actual patrol timing violated 4–6 seconds.");
                ArenaDashReviewEvidence.ValidateExecutions(arena.Events);
            }
            if (stage != 3) Require(!dash.Aborted && dash.presentation.ReleaseCount - releaseBefore == 1,
                "An isolated complete attack omitted/duplicated its release or aborted.");
        }
        private double Wall => started > 0 ? Time.realtimeSinceStartupAsDouble - started : 0;
        private void Drain()
        {
            if (arena == null) return;
            while (eventCursor < arena.Events.Count)
                observations.Add(new Observation { stage = stage, kind = "combat_event", combat = arena.Events[eventCursor++],
                    wallTime = Wall, simulatedTime = simulated, stageTime = stageTime });
            if (dash?.presentation != null)
                while (presentationCursor < dash.presentation.Events.Count)
                    observations.Add(new Observation { stage = stage, kind = "presentation_event", presentation = dash.presentation.Events[presentationCursor++],
                        wallTime = Wall, simulatedTime = simulated, stageTime = stageTime });
        }
        private void Observe(string kind, string detail) => observations.Add(new Observation { stage = stage, kind = kind, detail = detail,
            wallTime = Wall, simulatedTime = simulated, stageTime = stageTime });
        private void PositionCamera()
        {
            if (camera == null) return;
            Bounds bounds = new Bounds(arena.boss.transform.position + Vector3.up * 1.7f, new Vector3(3, 3.7f, 4));
            bounds.Encapsulate(new Bounds(arena.player.transform.position + Vector3.up * .9f, new Vector3(1.4f, 2, 1.4f)));
            // Isolated clips face +Z, the default spawn faces -Z.
            float facing = stage == 3 ? -1 : 1;
            Vector3 offset = view == "side" ? new Vector3(1, .25f, .05f) : view == "front" ? new Vector3(.03f, .25f, facing) : new Vector3(.8f, .30f, .7f * facing);
            Quaternion rotation = Quaternion.LookRotation(-offset.normalized);
            float tangent = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad), distance = 7;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = Quaternion.Inverse(rotation) * (point - bounds.center);
                distance = Mathf.Max(distance, Mathf.Abs(local.y) / (tangent * .63f) - local.z, Mathf.Abs(local.x) / (tangent * camera.aspect * .82f) - local.z);
            }
            camera.transform.SetPositionAndRotation(bounds.center + offset.normalized * (distance + .4f), rotation);
        }
        private void Capture(double raw)
        {
            var playerBounds = arena.player.capsule.bounds; var bossBounds = mesh.bounds;
            var patrol = arena.boss.PatrolPlanner.Motor;
            bool patrolling = patrol != null && patrol.Active;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            frames.Add(new Frame { frame = Time.frameCount, stage = stage, stageName = Titles[stage], wallTime = Wall, simulatedTime = simulated,
                rawInterval = raw, battleTime = arena.BattleTime, stageTime = stageTime, phase = dash.Phase.ToString(),
                clip = dash.Active ? dash.ClipId : patrolling ? patrol.ClipId : arena.boss.FoundationMotor.ClipId,
                sourceTime = dash.Active ? dash.SourceTime : patrolling ? patrol.SourceTime : arena.boss.FoundationMotor.StateTime,
                phaseTime = dash.PhaseTime, bossState = arena.boss.StateName, playerAction = arena.player.ActionName,
                bossHealth = arena.boss.fighter.Health, playerHealth = arena.player.fighter.Health, yaw = arena.boss.transform.eulerAngles.y,
                boss = arena.boss.transform.position, player = arena.player.transform.position, fist = arena.boss.FistPosition,
                leftFoot = arena.boss.Socket("socket_foot.L").position, rightFoot = arena.boss.Socket("socket_foot.R").position,
                velocity = dash.ActualVelocity, lockedForward = dash.LockedForward, locked = dash.HeadingLocked,
                strikeOpen = dash.StrikeOpen, invulnerable = arena.player.fighter.Invulnerable, focused = Application.isFocused,
                actualFlameShader = dash.presentation.CoreShaderName,
                activeLoops = dash.presentation.ActiveLoopCount, activeVoices = dash.presentation.ActiveVoiceCount,
                activeEffects = dash.presentation.ActiveEffectCount, fistQueries = dash.FistQueryCount, flameQueries = dash.FlameQueryCount,
                cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles,
                bossBounds = bossBounds, playerBounds = playerBounds, execution = dash.ExecutionId, strike = dash.StrikeId,
                patrolInvalid = arena.boss.PatrolPlanner.Invalid, actualAi = arena.boss.decisionEnabled,
                insideFrustum = Inside(bossBounds) && Inside(playerBounds) });
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
        private IEnumerator Finish()
        {
            Observe("terminal_hold", "Full action finished; actual view held while recording.");
            yield return new WaitForSecondsRealtime(1.2f); yield return new WaitForEndOfFrame(); Save(true); Application.Quit();
        }
        private void OnGUI()
        {
            if (!displayReady) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 19, normal = { textColor = Color.white }, wordWrap = true };
            GUI.matrix = Matrix4x4.Scale(Vector3.one * (Screen.width / 1920f));
            GUI.Box(new Rect(24, 240, 1100, 96), GUIContent.none);
            GUI.Label(new Rect(38, 246, 1072, 84), "DASH 18 / " + (finishing ? (errors.Count>0 ? "REVIEW STOPPED / FAILED CHECK" : "FULL REVIEW COMPLETE") : Titles[stage]) + "\n" +
                (stage == 3 ? "Default R2 AI / actual complete patrol timing" : "Scripted fixture and player commands / isolated skill; patrol gate explicitly disabled") +
                "\nActual wall clock + framebuffer/audio / " + view + " / native input and performance validation are separate", label);
        }
        private void Save(bool complete)
        {
            if (written || !writable) return; written = true; Drain();
            File.WriteAllText(Path.Combine(output, "realtime_dash.json"), JsonUtility.ToJson(new Report {
                completeSequence = complete, isolatedOnly = isolated, width = Screen.width, height = Screen.height,
                captureFramerate = Time.captureFramerate, targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount,
                unity = Application.unityVersion, build = Application.version, graphics = SystemInfo.graphicsDeviceName, view = view,
                wallDuration = Wall, simulatedDuration = simulated, maximumFrameInterval = maxInterval, finalUnsimulatedBoundary = finalBoundary, startedUnityRealtime = started,
                frames = frames.ToArray(), observations = observations.ToArray(), errors = errors.ToArray() }, true));
            Debug.Log("DASH_GAMEPLAY_REVIEW_COMPLETE complete=" + complete + " frames=" + frames.Count + " errors=" + errors.Count);
        }
        private void Fail(Exception e) { errors.Add(e.ToString()); Debug.LogException(e); ready = false; finishing = true; Save(false); Application.Quit(2); }
        private void OnApplicationQuit() { if (!written) Save(false); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
