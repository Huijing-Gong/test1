using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Opt-in synchronous physics/animation contracts. This never certifies visual quality,
    // native input, a displayed frame rate, or an ordinary AI combat session.
    public sealed class ArenaFoundationContracts : MonoBehaviour
    {
        [Serializable] private sealed class Trace
        {
            public string caseName, clip, motorState;
            public int sequenceIndex;
            public float clipTime, headingYaw, yaw;
            public double simulationTime;
            public bool blocked;
            public Vector3 actor, rightFoot, leftFoot;
            public ArenaFoundationSoleProbe.Sample sole;
        }
        [Serializable] private sealed class Report
        {
            public int schema = 1;
            public string kind = "headless_foundation_contracts_not_visual_or_performance";
            public string unity, build, graphicsDevice;
            public bool passed;
            public ArenaCheckCase[] cases;
            public Trace[] traces;
            public ArenaEventRecord[] defaultAiEvents;
        }
        private const float Step = 1f / 120;
        private ArenaSession arena;
        private ArenaFoundationMotor motor;
        private ArenaProductionClips library;
        private ArenaFoundationSoleProbe sole;
        private readonly List<ArenaCheckCase> cases = new List<ArenaCheckCase>();
        private readonly List<Trace> traces = new List<Trace>();
        private string caseName, previousClip;
        private float previousTime;
        private int sequence, ticks;
        private Vector3 previousActor;
        private float maximumStep;
        private double simulationTime;
        private Vector3 scenePlayerStart, sceneBossStart;
        private ArenaEventRecord[] defaultAiEvents = Array.Empty<ArenaEventRecord>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--sam-foundation-contracts") >= 0)
                new GameObject("Foundation physics and animation contracts").AddComponent<ArenaFoundationContracts>();
        }
        private IEnumerator Start()
        {
            yield return null;
            string output = Arg("--sam-output");
            bool canWrite = false;
            try
            {
                if (string.IsNullOrEmpty(output)) throw new ArgumentException("A new --sam-output directory is required.");
                output = Path.GetFullPath(output);
                if (File.Exists(Path.Combine(output, "foundation_contracts.json"))) throw new IOException("Preserve existing evidence.");
                Directory.CreateDirectory(output);
                canWrite = true;
                arena = FindFirstObjectByType<ArenaSession>();
                Require(arena != null, "Actual arena scene missing.");
                scenePlayerStart = arena.playerStart; sceneBossStart = arena.bossStart;
                arena.automaticTick = false;
                arena.view.automaticTick = false;
                arena.boss.decisionEnabled = false;
                // Synchronous contracts have no real DSP time; leave actual audio validation
                // to the separately recorded runtime. Neither clocks nor quality are benchmarked here.
                if (arena.sound != null) arena.sound.enabled = false;
                arena.sound = null;
                motor = arena.boss.GetComponent<ArenaFoundationMotor>();
                library = arena.boss.GetComponent<ArenaProductionClips>();
                Require(motor != null && library != null, "New foundation motor and clips must be saved in the scene.");
                sole = new ArenaFoundationSoleProbe(arena.boss.GetComponentInChildren<SkinnedMeshRenderer>());
                Check("foundation guard is used by the actual Boss brain", Guard);
                Check("two complete straight cycles use exact source travel", Walk);
                Check("default spawn AI completes approach and causes actual collision damage", DefaultAi);
                foreach (int angle in new[] { 15, 45, 90, 180 })
                    foreach (bool right in new[] { true, false })
                    {
                        int degrees = angle; bool side = right;
                        Check("complete " + (right ? "right " : "left ") + angle + " degree turn", () => Turn(degrees, side));
                    }
                Check("mid-step world obstruction settles without translation jump", Obstructed);
                Check("real damage triggers stationary hit and committed attack keeps its action", Hit);
                Check("real lethal damage plays source death and restart restores guard", Death);
            }
            catch (Exception exception) { cases.Add(new ArenaCheckCase { name = "contract setup", passed = false, error = exception.ToString() }); }
            finally { sole?.Dispose(); }
            bool passed = cases.Count == 14;
            foreach (var row in cases) passed &= row.passed;
            var report = new Report { passed = passed, unity = Application.unityVersion, build = Application.version,
                graphicsDevice = SystemInfo.graphicsDeviceName, cases = cases.ToArray(), traces = traces.ToArray(), defaultAiEvents = defaultAiEvents };
            if (canWrite) File.WriteAllText(Path.Combine(output, "foundation_contracts.json"), JsonUtility.ToJson(report, true));
            Debug.Log("FOUNDATION_CONTRACTS_COMPLETE passed=" + passed + " cases=" + cases.Count);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
#else
            Application.Quit(passed ? 0 : 1);
#endif
        }
        private void Check(string name, Action action)
        {
            caseName = name;
            var row = new ArenaCheckCase { name = name, error = "" };
            try { Fixture(); action(); row.passed = true; }
            catch (Exception exception) { row.error = exception.ToString(); }
            cases.Add(row);
        }
        private void Fixture()
        {
            arena.playerStart = new Vector3(10, .05f, 10);
            arena.bossStart = new Vector3(0, .025f, -6);
            arena.Restart(true);
            arena.boss.decisionEnabled = false;
            arena.boss.ResetBoss(arena.bossStart, Quaternion.identity);
            Physics.SyncTransforms();
            previousClip = null; previousTime = -1; maximumStep = 0; simulationTime = 0;
            previousActor = arena.boss.transform.position;
            Advance(.12f);
            Require(!motor.Busy, "Reset must settle to stable guard.");
        }
        private void Guard()
        {
            Advance(2.1f);
            Require(arena.boss.animationDriver.ClipName == library.Clip("guard").name, "Normal brain still uses old guard.");
            Require(!motor.Busy, "Guard must remain available.");
        }
        private void Walk()
        {
            Vector3 start = arena.boss.transform.position;
            Quaternion heading = arena.boss.transform.rotation;
            Require(motor.BeginWalkSegment(2), "Cannot start two-loop foundation walk.");
            Capture();
            float duration = RunUntilGuard(6);
            Vector3 wanted = heading * (library.Motion("walk_start").EndPosition + 2 * library.Motion("walk_loop").EndPosition + library.Motion("walk_stop").EndPosition);
            Vector3 error = Vector3.ProjectOnPlane(arena.boss.transform.position - start - wanted, Vector3.up);
            Require(error.magnitude < .003f, "Straight source root travel differs by " + error.magnitude + "m.");
            Require(Quaternion.Angle(heading, arena.boss.transform.rotation) < .01f, "Straight planted-foot heading changed.");
            Require(duration >= 4.79f && duration <= 5.05f, "Unexpected actual walk completion time: " + duration);
            Require(maximumStep < .06f, "Actor translation jumped within a single simulation interval.");
            Advance(.25f);
        }
        private void Turn(int degrees, bool right)
        {
            Vector3 start = arena.boss.transform.position;
            Quaternion heading = arena.boss.transform.rotation;
            string id = "turn_" + (right ? "right_" : "left_") + degrees;
            Require(motor.TryTurn(degrees, right), "Actual exported turn unavailable: " + id);
            Capture();
            Require(!arena.boss.TrySkill(ArenaAttack.Jab), "Attack interrupted a committed foundation turn.");
            RunUntilGuard(library.Motion(id).duration + .5f);
            float angleError = Quaternion.Angle(heading * Quaternion.Euler(0, right ? degrees : -degrees, 0), arena.boss.transform.rotation);
            Require(angleError < .02f, "Turn endpoint heading error: " + angleError);
            Require(Vector3.ProjectOnPlane(arena.boss.transform.position - start, Vector3.up).magnitude < .002f, "In-place turn moved the actor root.");
            Advance(.25f);
        }
        private void DefaultAi()
        {
            arena.playerStart = scenePlayerStart; arena.bossStart = sceneBossStart;
            arena.Restart(true); arena.boss.decisionEnabled = true;
            previousActor = arena.boss.transform.position; previousClip = null; previousTime = -1;
            try
            {
                for (int i = 0; i < 30 / Step && arena.player.fighter.HitCount == 0; i++) Tick();
                defaultAiEvents = arena.Events.ToArray();
                Require(arena.player.fighter.HitCount > 0 && arena.player.fighter.Health < arena.player.fighter.maximumHealth,
                    "Normal AI never caused real collision damage from default spawns within 30 simulated seconds.");
                Require(arena.Events.Exists(e => e.kind == "boss_start_finished") && arena.Events.Exists(e => e.kind == "boss_stop_finished"),
                    "Default AI did not complete the foundation approach.");
            }
            finally { arena.boss.decisionEnabled = false; }
        }
        private void Obstructed()
        {
            Require(motor.BeginWalkSegment(2), "Cannot start obstruction fixture.");
            Advance(.35f);
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                blocker.name = "Temporary contract wall"; blocker.layer = 8;
                blocker.transform.position = arena.boss.transform.position + Vector3.forward * 1.05f + Vector3.up * 1.5f;
                blocker.transform.localScale = new Vector3(6, 3, .2f);
                Physics.SyncTransforms();
                Vector3 before = arena.boss.transform.position;
                RunUntilGuard(3);
                Require(arena.boss.transform.position.z - before.z < .85f, "Actor passed through the actual world blocker.");
                Require(maximumStep < .06f, "Obstructed step teleported actor root.");
                Advance(.3f);
                var feet = sole.Capture(++sequence, 0);
                Require(feet.reliable && feet.leftGroundFound && feet.rightGroundFound, "Actual boot contact could not be measured after obstruction.");
                Require(Mathf.Abs(feet.leftGroundClearanceM) <= .005f && Mathf.Abs(feet.rightGroundClearanceM) <= .005f,
                    "Obstruction left a boot suspended or penetrating after guard settle.");
            }
            finally { DestroyImmediate(blocker); Physics.SyncTransforms(); }
        }
        private void Hit()
        {
            // Direct Receive here tests the damage-event-to-animation contract only.
            // Spatial collision and native input need their own gameplay cases.
            Require(arena.boss.fighter.Receive(Damage(1)), "Valid light damage rejected.");
            Advance(.05f);
            Require(motor.ClipId == "hit_light", "Damage did not enter actual authored hit clip.");
            RunUntilGuard(1.2f); Advance(.3f);
            Require(arena.boss.TrySkill(ArenaAttack.Jab), "Legacy attack API cannot start from guard.");
            Advance(.1f);
            Require(arena.boss.fighter.Receive(Damage(1)), "Valid armour hit rejected.");
            Advance(.05f);
            Require(arena.boss.Attacking && arena.boss.animationDriver.ClipName == arena.boss.animationDriver.jab.name,
                "Light damage replaced a committed attack with full-body hit.");
            arena.boss.CancelAttack(); Advance(.3f);
        }
        private void Death()
        {
            Require(arena.boss.fighter.Receive(Damage(arena.boss.fighter.maximumHealth + 1)), "Lethal event rejected.");
            Advance(library.Motion("death").duration + .25f);
            Require(arena.boss.fighter.Dead && arena.State == ArenaRoundState.Victory, "Lethal damage did not enter victory.");
            Require(arena.boss.animationDriver.ClipName == library.Clip("death").name, "Death does not use imported source motion.");
            Require(motor.StateTime >= library.Motion("death").duration - .001f, "Source death was truncated.");
            arena.Restart(true); arena.boss.decisionEnabled = false; Advance(.25f);
            Require(!arena.boss.fighter.Dead && !motor.Busy && motor.ClipId == "guard", "Restart retained a dead or busy motor.");
            Require(!arena.boss.fighter.Invulnerable, "Restart retained invulnerability.");
        }
        private ArenaHit Damage(float amount) => new ArenaHit { source = arena.player.fighter, strike = arena.NewStrike(),
            damage = amount, push = 0, point = arena.boss.transform.position, direction = Vector3.forward, skill = "foundation_contract_damage_event" };
        private float RunUntilGuard(float timeout)
        {
            float elapsed = 0;
            while (motor.Busy && elapsed < timeout) { Tick(); elapsed += Step; }
            Require(!motor.Busy, "Foundation motor remained busy beyond " + timeout + "s in " + motor.ClipId);
            return elapsed;
        }
        private void Advance(float seconds)
        {
            int count = Mathf.CeilToInt(seconds / Step);
            for (int i = 0; i < count; i++) Tick();
        }
        private void Tick()
        {
            arena.Simulate(default, Step);
            simulationTime += Step;
            maximumStep = Mathf.Max(maximumStep, Vector3.Distance(previousActor, arena.boss.transform.position));
            previousActor = arena.boss.transform.position;
            if ((++ticks & 1) == 0) Capture();
        }
        private void Capture()
        {
            // Attack poses belong to a different library and must never be labelled guard.
            if (arena.boss.Attacking || string.IsNullOrEmpty(motor.ClipId)) return;
            string clip = motor.ClipId; float time = motor.StateTime;
            if (clip != previousClip || time < previousTime - .000001f) sequence++;
            previousClip = clip; previousTime = time;
            traces.Add(new Trace { caseName = caseName, sequenceIndex = sequence, clip = clip, clipTime = time,
                simulationTime = simulationTime, blocked = motor.Blocked, motorState = motor.State.ToString(),
                actor = arena.boss.transform.position, yaw = arena.boss.transform.eulerAngles.y, headingYaw = motor.HeadingYaw,
                rightFoot = arena.boss.Socket("socket_foot.R").position, leftFoot = arena.boss.Socket("socket_foot.L").position,
                sole = sole.Capture(sequence, time) });
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static string Arg(string key) { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    }
}
