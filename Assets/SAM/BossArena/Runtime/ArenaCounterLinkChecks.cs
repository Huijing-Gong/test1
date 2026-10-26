using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Opt-in saved-scene collision fixtures. Not hardware input or natural play.
    public sealed class ArenaCounterLinkChecks : MonoBehaviour
    {
        [Serializable] private sealed class Row
        { public string name, error; public bool passed, queued, reward, blade; public int perfectDodges; public ArenaEventRecord[] events; }
        [Serializable] private sealed class Report
        { public string scope = "SAVED_SCENE_REAL_CAPSULE_COUNTER_FIXTURES_NOT_NATIVE_INPUT_OR_DEFAULT_AI", unity, error; public bool passed; public Row[] cases; }
        private ArenaSession arena;
        private readonly List<Row> rows = new List<Row>();
        private const float Step = 1f / 120;
        private ArenaPlayerController P => arena.player;
        private ArenaFighter F => P.fighter;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--sam-counter-link-checks") < 0) return;
            new GameObject("Opt-in counter link collision fixtures").AddComponent<ArenaCounterLinkChecks>();
        }
        private IEnumerator Start()
        {
            yield return null;
            string error = null;
            FileStream ownedOutput = null;
            try
            {
                var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--sam-output");
                Require(at >= 0 && at + 1 < args.Length, "Unique output directory required");
                string proposedOutput = Path.Combine(Path.GetFullPath(args[at + 1]), "counter-link-checks.json");
                Require(!File.Exists(proposedOutput), "Preserve existing result");
                Directory.CreateDirectory(Path.GetDirectoryName(proposedOutput));
                // Atomic reservation: cleanup may write only through this newly
                // created owned handle. Rejected/racing old paths are never opened.
                ownedOutput = new FileStream(proposedOutput, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                arena = FindFirstObjectByType<ArenaSession>(); Require(arena != null, "Saved arena missing");
                arena.automaticTick = false; arena.view.automaticTick = false; arena.boss.decisionEnabled = false;
                Check("late edge waits for full dodge then consumes one existing reward", () => {
                    Perfect(); ToAge(.43f); Tick(new ArenaCommand { attack = true }, Step);
                    Require(P.CounterQueued && P.Dodging && P.AttackTime < 0, "Premature attack or missing request");
                    float remaining = P.PerfectCounterRemaining; Tick(default, .13f);
                    Require(!P.CounterQueued && !P.Dodging && !P.PerfectCounterReady && Count("perfect_counter_started") == 1 && Count("player_attack_started") == 1, "Not one rewarded sword");
                    Require(remaining < P.perfectCounterSeconds, "Queue extended reward");
                    Tick(default, .17f); Require(P.CounterBladeVisible && arena.defenseFeedback.CounterBladeVisible, "Accepted sword highlight missing");
                    Tick(default, .6f); Require(!P.CounterBladeVisible && !arena.defenseFeedback.CounterBladeVisible && Count("player_attack_started") == 1, "Tail or second sword retained");
                });
                Check("early edge is lost and cannot replay at recovery", () => {
                    Perfect(); ToAge(.39f); Tick(new ArenaCommand { attack = true }, Step); Tick(default, .2f);
                    Require(!P.CounterQueued && Count("player_attack_started") == 0 && P.PerfectCounterReady, "Early edge replayed");
                });
                Check("ordinary dodge cannot use previous precise block reward", () => {
                    Tick(new ArenaCommand { guardHeld = true, guardPressed = true }, .075f); Contact();
                    Require(F.PerfectBlockCount == 1 && P.PerfectCounterReady, "Guard fixture failed");
                    Tick(new ArenaCommand { dodge = true }, Step); ToAge(.43f);
                    Tick(new ArenaCommand { attack = true }, Step); Tick(default, .15f);
                    Require(F.PerfectDodgeCount == 0 && !P.CounterQueued && Count("player_attack_started") == 0, "Ordinary dodge promoted by old guard reward");
                });
                Check("late ordinary iframe and empty dodge give no queue", () => {
                    Tick(new ArenaCommand { dodge = true }, .25f); Contact(); ToAge(.43f);
                    Tick(new ArenaCommand { attack = true }, Step); Tick(default, .15f);
                    Require(F.DodgeCount == 1 && F.PerfectDodgeCount == 0 && Count("player_attack_started") == 0, "Ordinary iframe rewarded");
                });
                Check("held input and repeated contact cannot add a second sword", () => {
                    long hit = Perfect(); float reward = P.PerfectCounterRemaining; Contact(hit); Contact();
                    Require(F.PerfectDodgeCount == 1 && P.PerfectCounterRemaining == reward, "Repeated reward stacked");
                    ToAge(.43f); var edge = new ArenaCommand { attack = true }; Tick(edge, .3f);
                    Tick(edge.WithoutEdges(), .9f);
                    Require(Count("perfect_counter_queued") == 1 && Count("perfect_counter_started") == 1 && Count("player_attack_started") == 1, "Held input chained");
                });
                Check("reward expiry discards request rather than downgrade to ordinary sword", () => {
                    float saved = P.perfectCounterSeconds;
                    try { P.perfectCounterSeconds = .39f; Perfect(); ToAge(.43f); Tick(new ArenaCommand { attack = true }, Step);
                        Require(P.CounterQueued, "Expiry fixture did not queue"); Tick(default, .2f);
                        Require(!P.CounterQueued && !P.PerfectCounterReady && Count("player_attack_started") == 0, "Expired request attacked");
                    } finally { P.perfectCounterSeconds = saved; }
                });
                Check("real incoming damage cancels pending input without replay", () => {
                    Queue(); Contact(); Require(!P.CounterQueued, "Damage retained queue"); Tick(default, .8f);
                    Require(Count("player_attack_started") == 0 && !arena.defenseFeedback.CounterBladeVisible, "Damage recovery replayed");
                });
                Check("guard takeover drops request but preserves original reward clock", () => {
                    Queue(); float reward = P.PerfectCounterRemaining; Tick(new ArenaCommand { guardHeld = true }, Step); Tick(default, .2f);
                    Require(!P.CounterQueued && Count("player_attack_started") == 0 && P.PerfectCounterRemaining < reward, "Guard replayed or prolonged reward");
                });
                Check("failed start drops request without spending or retrying reward", () => {
                    Queue(); P.capsule.enabled = false; Tick(default, .15f); P.capsule.enabled = true; Tick(default, .2f);
                    Require(!P.CounterQueued && Count("player_attack_started") == 0 && P.PerfectCounterReady, "Failed admission replayed or spent reward");
                });
                Check("suspend and cinematic recovery cannot replay pending input", () => {
                    Queue(); P.SuspendDefense(); Tick(default, .8f); Require(!P.CounterQueued && !P.PerfectCounterReady && Count("player_attack_started") == 0, "Suspend retained intent");
                    arena.Restart(true); arena.boss.decisionEnabled = false; Tick(default, Step); Queue();
                    object token = new object(); arena.view.BeginCinematic(token); Tick(default, Step); arena.view.EndCinematic(token); Tick(default, .8f);
                    Require(!P.CounterQueued && !P.PerfectCounterReady && Count("player_attack_started") == 0, "Cinematic replayed intent");
                });
                Check("disable and restart clear pending input and highlight", () => {
                    Queue(); P.enabled = false; P.enabled = true; Tick(default, .8f);
                    Require(!P.CounterQueued && !P.PerfectCounterReady && Count("player_attack_started") == 0, "Disable replayed");
                    arena.Restart(true); arena.boss.decisionEnabled = false; Tick(default, Step); Queue(); arena.Restart(true); Tick(default, .8f);
                    Require(!P.CounterQueued && !P.PerfectCounterReady && Count("player_attack_started") == 0 && !arena.defenseFeedback.CounterBladeVisible, "Restart leaked state");
                });
                Check("lethal actual collision clears queue through original round lifecycle", () => {
                    Queue(); Contact(0, F.maximumHealth + 1); Require(F.Dead && arena.State == ArenaRoundState.Defeat && !P.CounterQueued && !P.PerfectCounterReady, "Death cleanup failed");
                    arena.Restart(true); Tick(default, .8f); Require(Count("player_attack_started") == 0 && !arena.defenseFeedback.CounterBladeVisible, "Death retry replayed");
                });
            }
            catch (Exception e) { error = e.ToString(); Debug.LogException(e); }
            bool passed = error == null && rows.Count > 0 && rows.TrueForAll(r => r.passed);
            try
            {
                if (ownedOutput != null)
                    using (var writer = new StreamWriter(ownedOutput))
                        writer.Write(JsonUtility.ToJson(new Report { unity = Application.unityVersion, error = error, passed = passed, cases = rows.ToArray() }, true));
            }
            finally { ownedOutput?.Dispose(); }
            Debug.Log("SAM_COUNTER_LINK " + (passed ? "PASS" : "FAIL") + " cases=" + rows.Count);
            Application.Quit(passed ? 0 : 2);
        }
        private void Check(string name, Action test)
        {
            arena.Restart(true); arena.boss.decisionEnabled = false; Tick(default, Step); P.guard.ResetGuard(false);
            var row = new Row { name = name };
            try { test(); row.passed = true; } catch (Exception e) { row.error = e.ToString(); }
            row.queued = P.CounterQueued; row.reward = P.PerfectCounterReady; row.blade = arena.defenseFeedback.CounterBladeVisible;
            row.perfectDodges = F.PerfectDodgeCount; row.events = arena.Events.ToArray(); rows.Add(row);
            P.capsule.enabled = true; P.enabled = true;
        }
        private void Tick(ArenaCommand command, float elapsed)
        {
            while (elapsed > 1e-7f) { float part = Mathf.Min(elapsed, Step); arena.Simulate(command, part); elapsed -= part; command = command.WithoutEdges(); }
        }
        private void ToAge(float age) { if (P.DodgeState.Age < age) Tick(default, age - P.DodgeState.Age); }
        private long Perfect() { Tick(new ArenaCommand { dodge = true }, .0833333f); long strike = Contact(); Require(F.PerfectDodgeCount == 1, "Actual perfect contact fixture failed"); return strike; }
        private void Queue() { Perfect(); ToAge(.43f); Tick(new ArenaCommand { attack = true }, Step); Require(P.CounterQueued, "Request fixture failed"); }
        private long Contact(long strike = 0, float damage = 10)
        {
            if (strike == 0) strike = arena.NewStrike(); Vector3 p = P.transform.position;
            arena.StrikeCapsule(arena.boss.fighter, strike, "qa_counter_contact", p + Vector3.up * .4f, p + Vector3.up * 1.25f, .25f, damage, 1);
            return strike;
        }
        private int Count(string kind) => arena.Events.Count(e => e.kind == kind);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
