using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Synchronous scene regression, after Start. Only actual skill/input execution can
    // cause damage here: there are no synthetic Receive calls or replacement hitboxes.
    public static class ArenaCombatChecks
    {
        public static ArenaRuntimeCheckReport Run(ArenaSession arena)
        {
            if (arena == null) throw new ArgumentNullException(nameof(arena));
            return new Runner(arena).Run();
        }

        private sealed class Runner
        {
            private const float Step = 1f / 120f;
            private static readonly float[] Opens = { .43f, 1.016667f, 1.766667f };
            private static readonly float[] Closes = { .58f, 1.116667f, 1.916667f };
            private static readonly string[] Skills = { "drive_punch", "cross_punch", "hammer_punch" };
            private readonly ArenaSession arena;
            private readonly List<ArenaCheckCase> cases = new List<ArenaCheckCase>();
            private readonly Dictionary<ArenaAttack, ContactSetup> successful = new Dictionary<ArenaAttack, ContactSetup>();
            private readonly Vector3 playerStart, bossStart;
            private readonly bool automatic, decisions, playerTraining, bossTraining;
            private readonly ArenaAudio backend;
            private readonly bool audioEnabled;
            private readonly float playerMaximum, bossMaximum;
            private readonly ArenaTeam playerTeam, bossTeam;

            private sealed class ContactSetup
            {
                public Vector3 start;
                public Vector3[] targets;
                public bool follow;
                public float firstContact;
            }

            public Runner(ArenaSession value)
            {
                arena = value;
                Require(arena.player != null && arena.boss != null && arena.effects != null && arena.view != null,
                    "Combat regression requires the real scene controllers, effects and camera.");
                playerStart = arena.playerStart; bossStart = arena.bossStart;
                automatic = arena.automaticTick; decisions = arena.boss.decisionEnabled;
                playerMaximum = arena.player.fighter.maximumHealth; bossMaximum = arena.boss.fighter.maximumHealth;
                playerTeam = arena.player.fighter.team; bossTeam = arena.boss.fighter.team;
                playerTraining = arena.player.fighter.TrainingInvincible; bossTraining = arena.boss.fighter.TrainingInvincible;
                backend = arena.sound; audioEnabled = backend != null && backend.enabled;
                Require(backend == null || !backend.CaptureClockActive,
                    "Synchronous combat checks cannot run inside an active native audio capture.");
            }

            public ArenaRuntimeCheckReport Run()
            {
                try
                {
                    // Disabling alone does not stop Session.Simulate calling Tick/DSP.
                    // Detaching alone does not stop the backend's fighter event handlers.
                    if (backend != null) backend.enabled = false;
                    arena.sound = null; arena.automaticTick = false;
                    Check("combo two: two actual independent fist hits and complete recovery", () => ComboHits(ArenaAttack.ComboTwo));
                    Check("combo three: three actual fist hits, heavy finisher and complete recovery", () => ComboHits(ArenaAttack.ComboThree));
                    Check("combo two: ordinary lateral input makes both punches miss", () => LateralWhiff(ArenaAttack.ComboTwo));
                    Check("combo three: ordinary lateral input makes all three punches miss", () => LateralWhiff(ArenaAttack.ComboThree));
                    Check("combo three: a real dodge overlaps the first strike and escapes follow-ups", DodgeCombo);
                    Check("combo cancellation: later fists cannot strike after cancellation", CancelCombo);
                    Check("stomp: actual foot contact, visible wave origin, real damage and recovery", StompContact);
                    Check("stomp cancellation: no delayed ground wave or damage", CancelStomp);
                    Check("restart: cancels charged finisher and clears pending contacts", RestartCombo);
                }
                finally
                {
                    RestoreSettings(); arena.Restart(false);
                    arena.player.fighter.TrainingInvincible = playerTraining;
                    arena.boss.fighter.TrainingInvincible = bossTraining;
                    arena.sound = backend;
                    if (backend != null) backend.enabled = audioEnabled;
                    arena.automaticTick = automatic;
                    arena.hud?.Refresh();
                }
                bool passed = true;
                foreach (var result in cases) passed &= result.passed;
                return new ArenaRuntimeCheckReport { passed = passed, cases = cases.ToArray() };
            }

            private void Check(string name, Action body)
            {
                var result = new ArenaCheckCase { name = name, error = "", passed = false };
                try { body(); result.passed = true; }
                catch (Exception exception) { result.error = exception.ToString(); }
                finally
                {
                    try { RestoreSettings(); arena.Restart(false); }
                    catch (Exception exception) { result.passed = false; result.error += "\nCleanup: " + exception; }
                    cases.Add(result);
                }
            }

            private void RestoreSettings()
            {
                arena.automaticTick = false;
                arena.boss.decisionEnabled = decisions;
                arena.playerStart = playerStart; arena.bossStart = bossStart;
                arena.player.fighter.maximumHealth = playerMaximum; arena.boss.fighter.maximumHealth = bossMaximum;
                arena.player.fighter.team = playerTeam; arena.boss.fighter.team = bossTeam;
            }

            private void Fixture(Vector3 playerPosition)
            {
                RestoreSettings(); arena.boss.decisionEnabled = false;
                playerPosition.y = .05f;
                arena.playerStart = playerPosition; arena.bossStart = new Vector3(0, .05f, 0);
                arena.Restart(true);
                arena.boss.ResetBoss(arena.bossStart, Quaternion.identity);
                Vector3 facing = -Flat(playerPosition);
                arena.player.ResetPlayer(playerPosition, facing.sqrMagnitude > .001f ? Quaternion.LookRotation(facing) : Quaternion.identity);
                arena.view.ResetCamera();
                Advance(.075f);
                Require(arena.Active && !arena.player.fighter.TrainingInvincible && !arena.boss.fighter.TrainingInvincible,
                    "Fixture is not an ordinary live fight.");
            }

            private void Advance(float seconds, ArenaCommand command = default, HitTrace trace = null)
            {
                while (seconds > .000001f)
                {
                    float delta = Mathf.Min(Step, seconds);
                    trace?.BeforeStep(); arena.Simulate(command, delta); trace?.AfterStep();
                    seconds -= delta; command = command.WithoutEdges();
                }
            }

            private ArenaCommand Move(Vector3 worldDirection, bool run = true)
            {
                Vector3 forward = arena.view.PlanarForward, right = Vector3.Cross(Vector3.up, forward);
                Vector3 d = Flat(worldDirection).normalized;
                return new ArenaCommand { move = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, forward)), run = run };
            }

            private static Vector3 Flat(Vector3 p) => Vector3.ProjectOnPlane(p, Vector3.up);
            private static int Count(ArenaAttack attack) => attack == ArenaAttack.ComboThree ? 3 : 2;
            private static float Duration(ArenaAttack attack) => attack == ArenaAttack.ComboThree ? 3 : attack == ArenaAttack.ComboTwo ? 2.1f : 2.4f;
            private void Start(ArenaAttack attack) => Require(arena.boss.TrySkill(attack), "Actual TrySkill rejected " + attack);

            private Vector3[] SampleTargets(ArenaAttack attack)
            {
                Fixture(new Vector3(0, .05f, 7)); Start(attack);
                int count = Count(attack);
                var result = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    float time = (Opens[i] + Closes[i]) * .5f;
                    Advance(time - arena.boss.SkillTime);
                    result[i] = arena.boss.Socket(i == 1 ? "socket_hand.L" : "socket_hand.R").position;
                    result[i].y = .05f;
                    Require(result[i].z > .45f && Flat(result[i]).magnitude > .6f,
                        "Imported fist did not reach a plausible front contact location at " + time + ": " + result[i]);
                }
                return result;
            }

            private ContactSetup FindContact(ArenaAttack attack)
            {
                if (successful.TryGetValue(attack, out var cached)) return cached;
                Vector3[] targets = SampleTargets(attack);
                Vector3 mean = Vector3.zero;
                foreach (var target in targets) mean += target / targets.Length;
                Vector3[] starts = { mean, targets[0], (targets[0] + targets[1]) * .5f,
                    mean + Vector3.back * .12f, targets[0] + Vector3.back * .12f, mean + Vector3.forward * .12f };
                string failures = "";
                // Bounded physical placements, then real directional input between hits.
                // No teleport, disabled collider, changed push/damage or inflated health.
                for (int mode = 0; mode < 2; mode++)
                foreach (var position in starts)
                {
                    if (Flat(position).magnitude < arena.player.capsule.radius + arena.boss.capsule.radius + .05f) continue;
                    Fixture(position);
                    var setup = new ContactSetup { start = position, targets = targets, follow = mode == 1 };
                    using (var trace = new HitTrace(arena))
                    {
                        Start(attack); SimulateCombo(setup, trace, Duration(attack) + .06f);
                        if (trace.hits.Count == Count(attack))
                        {
                            ValidateHits(attack, trace);
                            setup.firstContact = trace.times[0]; successful.Add(attack, setup);
                            Debug.Log("ARENA_COMBAT_CONTACT " + attack + " start=" + position.ToString("F5") +
                                " directionalFollow=" + setup.follow + " " + trace.Describe());
                            return setup;
                        }
                        failures += " [start=" + position.ToString("F3") + " follow=" + setup.follow + " " + trace.Describe() + "]";
                    }
                }
                throw new InvalidOperationException("No real " + attack + " full-contact placement passed." + failures);
            }

            private void SimulateCombo(ContactSetup setup, HitTrace trace, float seconds)
            {
                double end = arena.BattleTime + seconds;
                while (arena.BattleTime < end - .000001 && arena.Active)
                {
                    ArenaCommand command = default;
                    int next = trace.hits.Count;
                    if (setup.follow && next > 0 && next < setup.targets.Length && arena.boss.Attacking)
                    {
                        Vector3 d = Flat(setup.targets[next] - arena.player.transform.position);
                        if (d.magnitude > .06f) command = Move(d, false);
                    }
                    Advance(Mathf.Min(Step, (float)(end - arena.BattleTime)), command, trace);
                }
            }

            private void ComboHits(ArenaAttack attack)
            {
                ContactSetup setup = FindContact(attack);
                Fixture(setup.start);
                using (var trace = new HitTrace(arena))
                {
                    Start(attack); SimulateCombo(setup, trace, Duration(attack) + .10f);
                    ValidateHits(attack, trace); CompleteRecovery(attack);
                    Debug.Log("ARENA_COMBAT_CONFIRMED " + attack + " " + trace.Describe());
                }
            }

            private void ValidateHits(ArenaAttack attack, HitTrace trace)
            {
                int count = Count(attack);
                Require(trace.hits.Count == count && arena.player.fighter.HitCount == count,
                    "Expected exactly " + count + " real contacts. " + trace.Describe());
                var ids = new HashSet<long>(); float damage = 0;
                for (int i = 0; i < count; i++)
                {
                    ArenaHit hit = trace.hits[i]; damage += hit.damage;
                    Require(hit.skill == Skills[i] && ids.Add(hit.strike), "Hand order/independent strike ID failed. " + trace.Describe());
                    Require(trace.times[i] >= Opens[i] - .001f && trace.times[i] <= Closes[i] + Step + .001f,
                        "Actual damage occurred outside its fist window. " + trace.Describe());
                    Same(hit.damage, arena.boss.jabDamage * (i == 2 ? 1.4f : 1), "Wrong actual fist damage.");
                    Same(hit.push, i == count - 1 ? 4 : 1.3f, "Wrong actual fist push.");
                }
                Same(arena.player.fighter.Health, playerMaximum - damage, "Actual health loss did not equal the independent hits.");
                Require(trace.geometryErrors.Count == 0, "A damage event had no same-hand swept contact: " + string.Join("; ", trace.geometryErrors));
                Require(trace.rootDrift < .035f && trace.rootTurn < .15f,
                    "A committed skill translated/retargeted its Boss root: drift=" + trace.rootDrift + " turn=" + trace.rootTurn);
                Require(arena.player.fighter.DodgeCount == 0 && !arena.player.fighter.Invulnerable,
                    "Positive contact fixture used invulnerability.");
                Require(Events("boss_punch_window").Count == count, "Not all independent fist windows were emitted.");
            }

            private void CompleteRecovery(ArenaAttack attack)
            {
                Require(!arena.boss.Attacking && !arena.effects.WarningVisible, "Skill did not fully release its action/warning.");
                var starts = Events("boss_attack_started"); var recoveries = Events("boss_recovery_started"); var ends = Events("boss_recovered");
                Require(starts.Count == 1 && recoveries.Count == 1 && ends.Count == 1,
                    "Expected one complete attack lifecycle; start/recovery/end=" + starts.Count + "/" + recoveries.Count + "/" + ends.Count);
                float expected = attack == ArenaAttack.Stomp ? 1.14f : attack == ArenaAttack.ComboTwo ? 1.133333f : 1.933333f;
                Near((float)(recoveries[0].time - starts[0].time), expected, Step + .002f, "Recovery did not open on its authored beat.");
                Near((float)(ends[0].time - starts[0].time), Duration(attack), Step + .003f, "Skill ended before/after the complete clip.");
            }

            private void LateralWhiff(ArenaAttack attack)
            {
                ContactSetup setup = FindContact(attack); Fixture(setup.start);
                float health = arena.player.fighter.Health; Vector3 beginning = arena.player.transform.position;
                using (var trace = new HitTrace(arena))
                {
                    Start(attack); Advance(.10f, default, trace);
                    Vector3 side = setup.start.x < 0 ? Vector3.left : Vector3.right;
                    Advance(1.05f, Move(side), trace); Advance(Duration(attack) - 1.15f + .1f, default, trace);
                    Same(arena.player.fighter.Health, health, "Readable lateral escape still took damage. " + trace.Describe());
                    Require(trace.hits.Count == 0 && arena.player.fighter.DodgeCount == 0 && !arena.player.fighter.Invulnerable,
                        "Lateral whiff relied on damage/invulnerability.");
                    Require(Flat(arena.player.transform.position - beginning).magnitude > 2,
                        "Real lateral input did not move the player clear.");
                    Require(Events("boss_punch_window").Count == Count(attack), "Whiff skipped remaining fist windows.");
                    Require(trace.rootTurn < .15f && trace.rootDrift < .035f, "Boss tracked/slid after committing to a lateral escape.");
                    CompleteRecovery(attack);
                    Debug.Log("ARENA_COMBAT_WHIFF " + attack + " travel=" + Flat(arena.player.transform.position - beginning).magnitude);
                }
            }

            private void DodgeCombo()
            {
                ContactSetup setup = FindContact(ArenaAttack.ComboThree);
                Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                float[] leads = { .06f, .08f, .10f, .12f };
                string attempts = "";
                foreach (var direction in directions)
                foreach (float lead in leads)
                {
                    Fixture(setup.start); float health = arena.player.fighter.Health;
                    Start(ArenaAttack.ComboThree); Advance(Mathf.Max(.05f, setup.firstContact - lead));
                    Vector3 before = arena.player.transform.position;
                    ArenaCommand dodge = Move(direction); dodge.dodge = true;
                    Advance(.56f, dodge);
                    Advance(3.1f - arena.boss.SkillTime, Move(direction));
                    if (arena.player.fighter.DodgeCount > 0 && arena.player.fighter.HitCount == 0)
                    {
                        Same(arena.player.fighter.Health, health, "Dodge changed actual health.");
                        Require(!arena.player.Dodging && !arena.player.fighter.Invulnerable, "Dodge did not close its invulnerability window.");
                        Require(Flat(arena.player.transform.position - before).magnitude > .5f, "Dodge did not physically move.");
                        var avoided = Events("dodge");
                        Require(avoided.Count > 0 && avoided[0].skill == Skills[0], "No real first-fist overlap was avoided.");
                        CompleteRecovery(ArenaAttack.ComboThree);
                        Debug.Log("ARENA_COMBAT_DODGE lead=" + lead + " direction=" + direction + " avoidedStrike=" + avoided[0].strike);
                        return;
                    }
                    attempts += " [lead=" + lead + " direction=" + direction + " hits=" + arena.player.fighter.HitCount + " avoids=" + arena.player.fighter.DodgeCount + "]";
                }
                throw new InvalidOperationException("No real dodge overlap followed by full combo escape passed." + attempts);
            }

            private void CancelCombo()
            {
                ContactSetup setup = FindContact(ArenaAttack.ComboThree); Fixture(setup.start);
                Start(ArenaAttack.ComboThree); Advance(.75f);
                Require(Events("boss_punch_window").Count == 1, "Cancellation fixture did not reach the first actual fist window.");
                int hits = arena.player.fighter.HitCount; float health = arena.player.fighter.Health;
                arena.boss.CancelAttack(); Advance(3.1f);
                Require(!arena.boss.Attacking && !arena.effects.WarningVisible && Events("boss_punch_window").Count == 1,
                    "Cancelled combo emitted a later fist window or kept the action/warning.");
                Require(arena.player.fighter.HitCount == hits && Events("boss_recovered").Count == 0,
                    "Cancellation permitted delayed damage or false completed recovery.");
                Same(arena.player.fighter.Health, health, "Cancellation permitted delayed health loss.");
                Start(ArenaAttack.ComboTwo); Require(arena.boss.ComboHitIndex == -1, "Restarted skill inherited the previous fist index.");
            }

            private void StompContact()
            {
                Fixture(new Vector3(0, .05f, 7)); Start(ArenaAttack.Stomp); Advance(1.01f);
                Vector3 target = arena.boss.GroundCentre + Vector3.forward * 1.6f; target.y = .05f;
                Fixture(target); float health = arena.player.fighter.Health;
                Require(arena.effects.flames != null, "The P3 real flame-wave renderer is missing.");
                Start(ArenaAttack.Stomp); double beginning = arena.BattleTime;
                float maximumFootHeight = arena.boss.FootPosition.y;
                while (arena.boss.SkillTime < .975f)
                { Advance(Step); maximumFootHeight = Mathf.Max(maximumFootHeight, arena.boss.FootPosition.y); }
                Require(Events("boss_ground_contact").Count == 0 && arena.effects.flames.LatestWaveRadius == 0,
                    "Stomp released its ground wave before the contact beat.");
                while (Events("boss_ground_contact").Count == 0 && arena.boss.SkillTime < 1.05f) Advance(Step);
                var contacts = Events("boss_ground_contact"); Require(contacts.Count == 1, "Stomp did not emit one actual foot contact.");
                Vector3 foot = arena.boss.FootPosition, ground = arena.boss.GroundCentre;
                Near((float)(contacts[0].time - beginning), 1f, Step + .002f, "Foot contact was not at 1.0 seconds.");
                Require(Flat(foot - ground).magnitude < .002f && Vector3.Distance(contacts[0].point, ground) < .002f,
                    "Ground contact was detached from the actual right-foot socket.");
                Require(Physics.Raycast(foot + Vector3.up, Vector3.down, out var floor, 2f, 1 << 8, QueryTriggerInteraction.Ignore),
                    "There is no actual floor below the planted foot.");
                Require(Mathf.Abs(foot.y - floor.point.y) < .14f && maximumFootHeight - foot.y > .08f,
                    "Stomp foot did not visibly lift and return to the real floor: peak=" + maximumFootHeight + " contact=" + foot);
                Vector3 wave = arena.effects.flames.LatestWaveCentre;
                Require(Vector3.Distance(wave, ground + Vector3.up * .018f) < .003f && arena.effects.flames.LatestWaveRadius > 0,
                    "Visible ground-wave origin is not on the same contact beat/location.");
                MeshFilter ring = null;
                foreach (var mesh in arena.effects.flames.GetComponentsInChildren<MeshFilter>())
                    if (mesh.name == "Ground heat front" && mesh.gameObject.activeInHierarchy) { ring = mesh; break; }
                Require(ring != null && ring.sharedMesh != null && ring.sharedMesh.vertexCount >= 12,
                    "No actual rendered ground-front mesh was created.");
                Require(Flat(ring.transform.TransformPoint(ring.sharedMesh.bounds.center) - wave).magnitude < .015f,
                    "Rendered ground-front mesh centre disagrees with the contact origin.");
                Advance(.42f);
                Require(Vector3.Distance(arena.effects.flames.LatestWaveCentre, wave) < .002f,
                    "Released wave followed the moving foot instead of remaining on the floor.");
                Require(arena.player.fighter.HitCount == 1, "Expanding real wave did not hit exactly once: hits=" + arena.player.fighter.HitCount);
                Same(arena.player.fighter.Health, health - arena.boss.stompDamage, "Wave did not apply the actual stomp damage.");
                Advance(2.5f - arena.boss.SkillTime); CompleteRecovery(ArenaAttack.Stomp);
                Debug.Log("ARENA_COMBAT_STOMP foot=" + foot.ToString("F5") + " wave=" + wave.ToString("F5") +
                    " contactTime=" + (contacts[0].time - beginning).ToString("F6") + " peakFootHeight=" + maximumFootHeight);
            }

            private void CancelStomp()
            {
                Fixture(new Vector3(0, .05f, 1.7f)); float health = arena.player.fighter.Health;
                Start(ArenaAttack.Stomp); Advance(.8f); arena.boss.CancelAttack(); Advance(2.6f);
                Require(Events("boss_ground_contact").Count == 0 && !arena.boss.Attacking && !arena.effects.WarningVisible,
                    "Cancelled stomp emitted a delayed contact or retained its action.");
                Require(arena.effects.flames != null && arena.effects.flames.LatestWaveRadius == 0,
                    "Cancelled stomp emitted a delayed visible ground wave.");
                Same(arena.player.fighter.Health, health, "Cancelled stomp damaged the player.");
            }

            private void RestartCombo()
            {
                Fixture(new Vector3(0, .05f, 7)); Start(ArenaAttack.ComboThree); Advance(1.4f);
                Require(arena.boss.Attacking && Events("boss_finisher_charge").Count == 1,
                    "Restart fixture did not enter the real finisher recharge.");
                arena.Restart(true); Advance(3.2f);
                Require(!arena.boss.Attacking && Events("boss_punch_window").Count == 0 && Events("boss_finisher_charge").Count == 0,
                    "Restart retained a pending finisher or old attack event.");
                Require(arena.player.fighter.HitCount == 0 && arena.effects.ActiveCount == 0 && !arena.effects.WarningVisible,
                    "Restart retained old damage/effects/warning.");
                Same(arena.player.fighter.Health, playerMaximum, "Restart did not restore the normal health maximum.");
            }

            private List<ArenaEventRecord> Events(string kind)
            {
                var result = new List<ArenaEventRecord>();
                foreach (var record in arena.Events) if (record.kind == kind) result.Add(record);
                return result;
            }
            private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
            private static void Same(float actual, float expected, string reason) => Near(actual, expected, .002f, reason);
            private static void Near(float actual, float expected, float tolerance, string reason)
            { Require(Mathf.Abs(actual - expected) <= tolerance, reason + " Actual=" + actual + " expected=" + expected); }

            private sealed class HitTrace : IDisposable
            {
                private readonly ArenaSession arena;
                private readonly Vector3 root;
                private readonly Quaternion rotation;
                private Vector3 right, left;
                private int previousIndex;
                public readonly List<ArenaHit> hits = new List<ArenaHit>();
                public readonly List<float> times = new List<float>();
                public readonly List<string> geometryErrors = new List<string>();
                public float rootDrift, rootTurn;
                public HitTrace(ArenaSession value)
                {
                    arena = value; root = arena.boss.transform.position; rotation = arena.boss.transform.rotation;
                    arena.player.fighter.Damaged += Hit;
                }
                public void BeforeStep()
                {
                    right = arena.boss.Socket("socket_hand.R").position; left = arena.boss.Socket("socket_hand.L").position;
                    previousIndex = arena.boss.ComboHitIndex;
                }
                public void AfterStep()
                {
                    if (!arena.boss.Attacking) return;
                    rootDrift = Mathf.Max(rootDrift, Flat(arena.boss.transform.position - root).magnitude);
                    rootTurn = Mathf.Max(rootTurn, Quaternion.Angle(rotation, arena.boss.transform.rotation));
                }
                private void Hit(ArenaHit hit)
                {
                    if (hit.source != arena.boss.fighter) return;
                    hits.Add(hit); times.Add(arena.boss.SkillTime);
                    int index = Array.IndexOf(Skills, hit.skill);
                    if (index < 0) { geometryErrors.Add("Unexpected damaging skill " + hit.skill); return; }
                    Vector3 current = arena.boss.Socket(index == 1 ? "socket_hand.L" : "socket_hand.R").position;
                    Vector3 previous = previousIndex == index ? index == 1 ? left : right : current;
                    float radius = arena.boss.jabRadius + (index == 2 ? .06f : 0);
                    if (!ArenaContactGeometry.Intersects(previous, current, radius, arena.player.capsule))
                        geometryErrors.Add(hit.skill + " t=" + arena.boss.SkillTime + " from=" + previous + " to=" + current);
                }
                public string Describe()
                {
                    string value = "hits=" + hits.Count;
                    for (int i = 0; i < hits.Count; i++) value += " {" + hits[i].skill + " id=" + hits[i].strike + " t=" + times[i].ToString("F6") + " damage=" + hits[i].damage + "}";
                    return value;
                }
                public void Dispose() { arena.player.fighter.Damaged -= Hit; }
            }
        }
    }
}
