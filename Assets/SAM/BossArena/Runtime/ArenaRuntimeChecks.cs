using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SamProduction.BossArena
{
    [Serializable]
    public sealed class ArenaRuntimeCheckReport
    {
        public bool passed;
        public ArenaCheckCase[] cases;
    }

    [Serializable]
    public sealed class ArenaCheckCase
    {
        public string name, error;
        public bool passed;
    }

    // Run only after the scene's Start methods. These fixtures use its real controllers,
    // animation sockets and Physics colliders. They never synthesize a successful Receive.
    public static class ArenaRuntimeChecks
    {
        public static ArenaRuntimeCheckReport Run(ArenaSession arena)
        {
            if (arena == null) throw new ArgumentNullException(nameof(arena));
            return new Runner(arena).Run();
        }

        private sealed class Runner
        {
            private const float Step = 1f / 120;
            private readonly ArenaSession arena;
            private readonly List<ArenaCheckCase> cases = new List<ArenaCheckCase>();
            private readonly float playerMaximum, bossMaximum;
            private readonly ArenaTeam playerTeam, bossTeam;
            private readonly bool automatic, decisions;
            private readonly Vector3 playerStart, bossStart;
            private readonly bool playerTraining, bossTraining;
            private Vector3 punchTarget;
            private float punchContactTime;
            private bool punchCalibrated;
            private ReapproachTrace reapproachTrace;

            public Runner(ArenaSession value)
            {
                arena = value;
                Require(arena.player != null && arena.boss != null && arena.view != null && arena.effects != null,
                    "The real arena must have both fighters, camera and effects before checks run.");
                playerMaximum = arena.player.fighter.maximumHealth;
                bossMaximum = arena.boss.fighter.maximumHealth;
                playerTeam = arena.player.fighter.team; bossTeam = arena.boss.fighter.team;
                playerTraining = arena.player.fighter.TrainingInvincible;
                bossTraining = arena.boss.fighter.TrainingInvincible;
                automatic = arena.automaticTick; decisions = arena.boss.decisionEnabled;
                playerStart = arena.playerStart; bossStart = arena.bossStart;
            }

            public ArenaRuntimeCheckReport Run()
            {
                try
                {
                    Check("collision contract: allies and self cannot damage", TeamContract);
                    Check("collision contract: dead source and dead target reject strikes", DeathContract);
                    Check("collision contract: one strike damages only once", StrikeDeduplication);
                    Check("collision contract: swept middle hits but outside and overhead gaps do not", CapsuleSweepBoundary);
                    Check("collision contract: hollow wave includes a capsule crossing its inner edge", RingBoundary);
                    Check("player input: movement and release deceleration", PlayerMovement);
                    Check("player input: scene boundary blocks movement", WallCollision);
                    Check("default AI: approaches from scene spawns and its first combo fist really hits", DefaultAiJab);
                    Check("default AI: follows a lateral escape and autonomously hits again", DefaultAiReapproach);
                    Check("default AI: a player obstructing its stopping step cannot freeze combat", BlockedStopRecovery);
                    Check("default AI: close side target cannot permanently block combat", CloseSideAi);
                    Check("boss input: imported jab socket really hits once", JabHit);
                    Check("player input: lateral movement creates a real jab whiff", JabWhiff);
                    Check("player input: dodge overlaps a real strike without losing health", DodgeWindow);
                    Check("player sword input: actual sweep kills the boss and enters victory", SwordVictory);
                    Check("boss jab input: actual hit kills the player and enters defeat", JabDefeat);
                    Check("restart: clears live effects, warning, health, events and pending action", RestartState);
                }
                finally
                {
                    RestoreSettings();
                    arena.Restart(false);
                    arena.player.fighter.TrainingInvincible = playerTraining;
                    arena.boss.fighter.TrainingInvincible = bossTraining;
                    arena.hud?.Refresh();
                }
                bool passed = true;
                foreach (ArenaCheckCase result in cases) passed &= result.passed;
                return new ArenaRuntimeCheckReport { passed = passed, cases = cases.ToArray() };
            }

            private void Check(string name, Action action)
            {
                var result = new ArenaCheckCase { name = name, passed = false, error = "" };
                try { action(); result.passed = true; }
                catch (Exception exception) { result.error = exception.ToString(); }
                finally
                {
                    try { RestoreSettings(); arena.Restart(false); }
                    catch (Exception exception)
                    {
                        result.passed = false;
                        result.error += "\nFixture cleanup: " + exception;
                    }
                    cases.Add(result);
                }
            }

            private void RestoreSettings()
            {
                arena.automaticTick = automatic;
                arena.boss.decisionEnabled = decisions;
                arena.player.fighter.maximumHealth = playerMaximum;
                arena.boss.fighter.maximumHealth = bossMaximum;
                arena.player.fighter.team = playerTeam;
                arena.boss.fighter.team = bossTeam;
                arena.playerStart = playerStart; arena.bossStart = bossStart;
            }

            private void Fixture(Vector3 playerPosition, Vector3 bossPosition, Quaternion bossRotation,
                float playerHealth = -1, float bossHealth = -1)
            {
                RestoreSettings();
                arena.automaticTick = false;
                arena.boss.decisionEnabled = false;
                if (playerHealth >= 0) arena.player.fighter.maximumHealth = playerHealth;
                if (bossHealth >= 0) arena.boss.fighter.maximumHealth = bossHealth;
                arena.playerStart = playerPosition; arena.bossStart = bossPosition;
                arena.Restart(true);
                Vector3 facing = Vector3.ProjectOnPlane(bossPosition - playerPosition, Vector3.up);
                arena.player.ResetPlayer(playerPosition, facing.sqrMagnitude > .01f ? Quaternion.LookRotation(facing) : Quaternion.identity);
                arena.boss.ResetBoss(bossPosition, bossRotation);
                arena.view.ResetCamera();
                // Supplies the session to BossBrain and settles the actual controllers on the floor.
                Advance(.075f);
                Require(arena.Active, "Fixture did not enter the real Fighting state.");
                Require(!arena.player.fighter.TrainingInvincible && !arena.boss.fighter.TrainingInvincible,
                    "Training invincibility leaked into the fixture.");
            }

            private void DefaultFixture()
            { Fixture(new Vector3(0, .05f, -5), new Vector3(0, .05f, 4), Quaternion.Euler(0, 180, 0)); }

            private void NaturalAiFixture()
            {
                RestoreSettings();
                arena.automaticTick = false;
                arena.boss.decisionEnabled = true;
                // Use the actual serialized scene spawns and the normal Restart headings.
                // No socket target, forced skill, retuned range, speed or health is used here.
                arena.Restart(true);
                Require(arena.Active && !arena.player.fighter.TrainingInvincible && !arena.boss.fighter.TrainingInvincible,
                    "Default AI fixture is not an ordinary live fight.");
            }

            private void Advance(float seconds, ArenaCommand command = default, EscapeTrace trace = null)
            {
                float remaining = seconds;
                while (remaining > .000001f)
                {
                    float delta = Mathf.Min(Step, remaining);
                    trace?.BeforeStep(command);
                    arena.Simulate(command, delta);
                    trace?.AfterStep();
                    reapproachTrace?.AfterStep(command);
                    remaining -= delta;
                    command = command.WithoutEdges();
                }
            }

            private ArenaCommand MoveToward(Vector3 worldDirection, bool run = false)
            {
                Vector3 forward = arena.view.PlanarForward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 direction = Vector3.ProjectOnPlane(worldDirection, Vector3.up).normalized;
                return new ArenaCommand { move = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)), run = run };
            }

            private int DirectPlayerCapsule(ArenaFighter source, long strike, float damage, string name)
            {
                Bounds bounds = arena.player.capsule.bounds;
                return arena.StrikeCapsule(source, strike, name, bounds.center - Vector3.up * .25f,
                    bounds.center + Vector3.up * .25f, .12f, damage, 0);
            }

            private void TeamContract()
            {
                DefaultFixture();
                float health = arena.player.fighter.Health;
                arena.boss.fighter.team = arena.player.fighter.team;
                Require(DirectPlayerCapsule(arena.boss.fighter, arena.NewStrike(), 5, "contract_ally") == 0,
                    "An allied source counted a collision as damage.");
                Require(DirectPlayerCapsule(arena.player.fighter, arena.NewStrike(), 5, "contract_self") == 0,
                    "A fighter damaged itself.");
                Same(arena.player.fighter.Health, health, "Allied/self collision changed actual health.");
                Require(arena.player.fighter.HitCount == 0 && CountEvents("hit") == 0,
                    "An allied/self collision generated a hit event.");
            }

            private void DeathContract()
            {
                DefaultFixture();
                // These two setup mutations explicitly test the collision contract, not a death animation.
                arena.boss.fighter.maximumHealth = 0; arena.boss.fighter.ResetFighter();
                float health = arena.player.fighter.Health;
                Require(DirectPlayerCapsule(arena.boss.fighter, arena.NewStrike(), 5, "contract_dead_source") == 0,
                    "A dead source damaged the live target.");
                Same(arena.player.fighter.Health, health, "Dead source changed health.");
                DefaultFixture();
                arena.player.fighter.maximumHealth = 0; arena.player.fighter.ResetFighter();
                Require(DirectPlayerCapsule(arena.boss.fighter, arena.NewStrike(), 5, "contract_dead_target") == 0,
                    "A dead target accepted a new hit.");
                Require(arena.player.fighter.HitCount == 0 && CountEvents("hit") == 0,
                    "Dead target produced a new hit event.");
            }

            private void StrikeDeduplication()
            {
                DefaultFixture();
                float health = arena.player.fighter.Health;
                long strike = arena.NewStrike();
                Require(DirectPlayerCapsule(arena.boss.fighter, strike, 5, "contract_dedup") == 1,
                    "Actual overlapping colliders did not register their first strike.");
                for (int i = 0; i < 4; i++)
                    Require(DirectPlayerCapsule(arena.boss.fighter, strike, 5, "contract_dedup") == 0,
                        "Repeated collision samples reapplied the same strike.");
                Same(arena.player.fighter.Health, health - 5, "Duplicate strike damage accumulated.");
                Require(DirectPlayerCapsule(arena.boss.fighter, arena.NewStrike(), 5, "contract_dedup_next") == 1,
                    "A new strike was incorrectly suppressed.");
                Same(arena.player.fighter.Health, health - 10, "New strike failed to change health.");
                Require(arena.player.fighter.HitCount == 2 && CountEvents("hit") == 2,
                    "Collision/event deduplication disagree.");
            }

            private void CapsuleSweepBoundary()
            {
                DefaultFixture();
                Bounds body = arena.player.capsule.bounds;
                const float radius = .12f, gap = .12f, damage = 3;
                float reach = Mathf.Max(body.extents.x + radius + 1, 2);
                Vector3 halfSweep = Vector3.right * reach;
                float health = arena.player.fighter.Health;
                long strike = arena.NewStrike();
                // Exercise real queries and real health. Neither miss may resolve the
                // strike; its later middle crossing must still be accepted once.
                Vector3 outside = body.center + Vector3.forward * (body.extents.z + radius + gap);
                int sideHits = arena.StrikeCapsule(arena.boss.fighter, strike, "contract_sweep_side_gap",
                    outside - halfSweep, outside + halfSweep, radius, damage, 0);
                Require(sideHits == 0, "A horizontal sweep with a 0.12 m side gap damaged the player.");
                Same(arena.player.fighter.Health, health, "Outside sweep changed actual health.");
                Vector3 overhead = new Vector3(body.center.x, body.max.y + radius + gap, body.center.z);
                int overheadHits = arena.StrikeCapsule(arena.boss.fighter, strike, "contract_sweep_overhead_gap",
                    overhead - halfSweep, overhead + halfSweep, radius, damage, 0);
                Require(overheadHits == 0, "A sweep 0.12 m above the head damaged the player.");
                Same(arena.player.fighter.Health, health, "Overhead sweep changed actual health.");
                Require(arena.player.fighter.HitCount == 0 && CountEvents("hit") == 0,
                    "Geometric misses emitted a damage event.");
                Vector3 a = body.center - halfSweep, b = body.center + halfSweep;
                Require(a.x < body.min.x - radius - .9f && b.x > body.max.x + radius + .9f,
                    "Sweep endpoints must both be well outside the actual player bounds.");
                Require(arena.StrikeCapsule(arena.boss.fighter, strike, "contract_sweep_middle",
                    a, b, radius, damage, 0) == 1,
                    "The middle of a real swept capsule crossed the player but did not hit once.");
                Require(arena.StrikeCapsule(arena.boss.fighter, strike, "contract_sweep_middle",
                    a, b, radius, damage, 0) == 0, "The same swept strike applied damage twice.");
                Same(arena.player.fighter.Health, health - damage, "Swept-middle damage was not exactly one hit.");
                Require(arena.player.fighter.HitCount == 1 && CountEvents("hit") == 1,
                    "Swept-middle collision and damage events disagree.");
                Debug.Log("ARENA_CHECK_CAPSULE_SWEEP sideHits=" + sideHits + " overheadHits=" + overheadHits +
                    " middleHits=1 gap=" + gap + " body=" + body + " a=" + a + " b=" + b);
            }

            private void RingBoundary()
            {
                RingAt(2.10f, true, "crossing_inner");
                RingAt(.90f, false, "inside_hollow");
                RingAt(2.65f, true, "crossing_outer");
                RingAt(3.10f, false, "outside_wave");
            }

            private void RingAt(float distance, bool expected, string name)
            {
                Fixture(new Vector3(distance, .05f, 0), new Vector3(0, .05f, 0), Quaternion.identity);
                float health = arena.player.fighter.Health;
                int hits = arena.StrikeRing(arena.boss.fighter, arena.NewStrike(), "contract_ring_" + name,
                    new Vector3(0, arena.boss.transform.position.y, 0), 2.5f, 2, 3, 0);
                Require(hits == (expected ? 1 : 0), "Hollow wave " + name + " returned " + hits + " actual hits.");
                Same(arena.player.fighter.Health, health - (expected ? 3 : 0), "Hollow wave health mismatch: " + name);
            }

            private void PlayerMovement()
            {
                DefaultFixture();
                Vector3 before = arena.player.transform.position;
                Vector3 forward = arena.view.PlanarForward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Advance(.8f, new ArenaCommand { move = Vector2.up });
                Vector3 travelled = Vector3.ProjectOnPlane(arena.player.transform.position - before, Vector3.up);
                float forwardTravel = Vector3.Dot(travelled, forward);
                float lateralDrift = Vector3.Dot(travelled, right);
                Require(forwardTravel > 1.8f && Mathf.Abs(lateralDrift) < .04f,
                    "W input did not follow the starting camera heading. worldTravel=" + travelled +
                    ", cameraForward=" + forward + ", along=" + forwardTravel.ToString("F5") +
                    ", lateral=" + lateralDrift.ToString("F5"));
                before = arena.player.transform.position;
                Advance(.3f);
                Require(arena.player.Speed < .035f, "Player did not stop after movement input release.");
                Require(Vector3.ProjectOnPlane(arena.player.transform.position - before, Vector3.up).magnitude < .30f,
                    "Player kept drifting after release.");
                Require(arena.player.transform.position.y > -.06f && arena.player.transform.position.y < .09f,
                    "Controller did not remain on the actual floor.");
                Debug.Log("ARENA_CHECK_PLAYER_MOVEMENT cameraForward=" + forward + " worldTravel=" + travelled +
                    " along=" + forwardTravel.ToString("F5") + " lateral=" + lateralDrift.ToString("F5") +
                    " stoppedSpeed=" + arena.player.Speed.ToString("F5"));
            }

            private void WallCollision()
            {
                DefaultFixture();
                Vector3 origin = new Vector3(0, .9f, -6);
                Require(Physics.Raycast(origin, Vector3.right, out RaycastHit wall, 40, 1 << 8, QueryTriggerInteraction.Ignore),
                    "The playable scene has no solid world boundary to test.");
                Vector3 start = origin + Vector3.right * Mathf.Max(0, wall.distance - 2);
                start.y = .05f;
                Fixture(start, new Vector3(0, .05f, 4), Quaternion.identity);
                ArenaCommand move = MoveToward(Vector3.right, true);
                Advance(.85f, move);
                Vector3 blocked = arena.player.transform.position;
                Advance(.35f, move);
                Require(blocked.x - start.x > 1.0f, "Wall fixture did not first travel toward the boundary.");
                Require(arena.player.transform.position.x <= wall.point.x - arena.player.capsule.radius + .055f,
                    "The player crossed the real scene boundary.");
                Require(Vector3.ProjectOnPlane(arena.player.transform.position - blocked, Vector3.up).magnitude < .025f,
                    "The blocked controller continued translating.");
            }

            private void DefaultAiJab()
            {
                NaturalAiFixture();
                double beginning = arena.BattleTime;
                float initialDistance = FighterDistance();
                float initialHealth = arena.player.fighter.Health;
                Vector3 bossBeginning = arena.boss.transform.position;
                ArenaEventRecord attack = null;
                float launchDistance = -1, launchAngle = -1, closestFist = float.PositiveInfinity;
                while (arena.Active && arena.BattleTime - beginning < 20)
                {
                    Advance(Step);
                    if (attack == null)
                    {
                        attack = FindEvent("boss_attack_started", "ComboTwo");
                        if (attack != null) { launchDistance = FighterDistance(); launchAngle = FacingAngle(); }
                    }
                    if (attack == null) continue;
                    if (arena.boss.Attacking && arena.boss.CurrentAttack == ArenaAttack.ComboTwo &&
                        arena.boss.SkillTime >= .40f && arena.boss.SkillTime <= .65f)
                    {
                        Vector3 fist = arena.boss.FistPosition;
                        closestFist = Mathf.Min(closestFist, Vector3.Distance(fist, arena.player.capsule.ClosestPoint(fist)));
                    }
                    ArenaEventRecord hit = FindStrikeEvent("hit", "drive_punch", attack.strike);
                    if (hit != null)
                    {
                        Require(hit.source == ArenaTeam.Boss.ToString() && hit.target == ArenaTeam.Player.ToString() &&
                            hit.health < initialHealth && arena.player.fighter.Health < initialHealth,
                            "Default jab event did not correspond to real player health loss. " + AiDiagnostic());
                        float travelled = Vector3.ProjectOnPlane(arena.boss.transform.position - bossBeginning, Vector3.up).magnitude;
                        Require(travelled > 1 && launchDistance < initialDistance - .8f,
                            "Default AI hit without first closing the initial spawn separation. " + AiDiagnostic());
                        Require(arena.player.fighter.DodgeCount == 0 && !arena.player.fighter.Invulnerable,
                            "Stationary-input AI fixture used dodge/invulnerability.");
                        Debug.Log("ARENA_CHECK_DEFAULT_AI_JAB spawnGap=" + initialDistance.ToString("F4") +
                            " launchGap=" + launchDistance.ToString("F4") + " launchAngle=" + launchAngle.ToString("F2") +
                            " closestFist=" + closestFist.ToString("F5") + " hitTime=" + hit.time.ToString("F5") +
                            " health=" + hit.health + " bossTravel=" + travelled.ToString("F4"));
                        return;
                    }
                    if (FindLifecycleEvent("boss_recovered", attack) != null)
                        throw new InvalidOperationException("Default AI's first jab fully recovered without hitting stationary input. " +
                            "launchGap=" + launchDistance.ToString("F4") + " launchAngle=" + launchAngle.ToString("F2") +
                            " closestFistToCapsule=" + closestFist.ToString("F5") + ". " + AiDiagnostic());
                }
                throw new InvalidOperationException("Default AI failed to land its first autonomous jab within 20 simulated seconds. " +
                    "firstJab=" + (attack == null ? "not_started" : attack.time.ToString("F4")) +
                    " launchGap=" + launchDistance.ToString("F4") + " closestFistToCapsule=" + closestFist.ToString("F5") +
                    ". " + AiDiagnostic());
            }

            private void DefaultAiReapproach()
            {
                NaturalAiFixture();
                using (var diagnostic = new ReapproachTrace(arena))
                {
                    reapproachTrace = diagnostic;
                    try { DefaultAiReapproachCore(); }
                    finally { reapproachTrace = null; }
                }
            }

            private void DefaultAiReapproachCore()
            {
                double beginning = arena.BattleTime;
                ArenaEventRecord firstAttack = null;
                while (arena.Active && arena.BattleTime - beginning < 20 && firstAttack == null)
                {
                    Advance(Step);
                    firstAttack = FindEvent("boss_attack_started", "ComboTwo");
                }
                Require(firstAttack != null, "Default AI never began its first jab before the lateral escape. " + AiDiagnostic());
                float launchDistance = FighterDistance();
                float health = arena.player.fighter.Health;
                Vector3 playerBeginning = arena.player.transform.position;
                Vector3 towardBoss = Vector3.ProjectOnPlane(arena.boss.transform.position - playerBeginning, Vector3.up).normalized;
                Vector3 lateral = Vector3.Cross(Vector3.up, towardBoss);
                // A normal sprint across the wind-up, then release the controls completely.
                // This is not a teleport and does not grant dodge frames or training health.
                string escapeDiagnostic;
                using (var trace = new EscapeTrace(arena, lateral, "default_ai_lateral"))
                {
                    Advance(1.05f, MoveToward(lateral, true), trace);
                    Advance(.35f, default, trace);
                    escapeDiagnostic = trace.Describe();
                }
                float escapeTravel = Vector3.ProjectOnPlane(arena.player.transform.position - playerBeginning, Vector3.up).magnitude;
                float escapedDistance = FighterDistance();
                Require(escapeTravel > 2 && escapedDistance > launchDistance + 1.5f,
                    "Lateral input failed to create a real separation for the AI to close. " + AiDiagnostic() + escapeDiagnostic);
                Same(arena.player.fighter.Health, health, "Default jab hit a player who ran out during its readable wind-up. " + AiDiagnostic() + escapeDiagnostic);
                Require(arena.player.fighter.DodgeCount == 0 && !arena.player.fighter.Invulnerable && !arena.player.Dodging,
                    "Lateral escape relied on invulnerability rather than movement.");
                double catchupStart = arena.BattleTime;
                Vector3 previousBoss = arena.boss.transform.position;
                float bossTravel = 0, minimumDistance = escapedDistance;
                while (arena.Active && arena.BattleTime - catchupStart < 20)
                {
                    Advance(Step);
                    Vector3 currentBoss = arena.boss.transform.position;
                    bossTravel += Vector3.ProjectOnPlane(currentBoss - previousBoss, Vector3.up).magnitude;
                    previousBoss = currentBoss;
                    minimumDistance = Mathf.Min(minimumDistance, FighterDistance());
                    ArenaEventRecord nextHit = null;
                    foreach (ArenaEventRecord record in arena.Events)
                        if (record.kind == "hit" && record.source == ArenaTeam.Boss.ToString() &&
                            record.target == ArenaTeam.Player.ToString() && record.strike != firstAttack.strike && record.time >= catchupStart)
                        { nextHit = record; break; }
                    if (nextHit == null) continue;
                    ArenaEventRecord recovered = FindLifecycleEvent("boss_recovered", firstAttack);
                    ArenaEventRecord nextAttack = FindOwningAttack(nextHit);
                    Require(recovered != null && nextAttack != null && nextAttack.time >= recovered.time,
                        "Follow-up damage did not come from a new autonomous attack after recovery. " + AiDiagnostic());
                    Require(bossTravel > .8f && minimumDistance < escapedDistance - .8f,
                        "The Boss failed to physically follow the escaped player before attacking again. " + AiDiagnostic());
                    Require(arena.player.fighter.Health < health && !arena.player.fighter.TrainingInvincible,
                        "Follow-up hit did not reduce real health. " + AiDiagnostic());
                    Debug.Log("ARENA_CHECK_DEFAULT_AI_REAPPROACH escapeTravel=" + escapeTravel.ToString("F4") +
                        " escapedGap=" + escapedDistance.ToString("F4") + " minimumGap=" + minimumDistance.ToString("F4") +
                        " bossTravel=" + bossTravel.ToString("F4") + " nextSkill=" + nextHit.skill +
                        " catchupSeconds=" + (nextHit.time - catchupStart).ToString("F4") + " health=" + nextHit.health);
                    return;
                }
                throw new InvalidOperationException("Default AI did not follow and land a new autonomous attack within 20 seconds after lateral escape. " +
                    "escapeTravel=" + escapeTravel.ToString("F4") + " escapedGap=" + escapedDistance.ToString("F4") +
                    " minimumGap=" + minimumDistance.ToString("F4") + " bossTravel=" + bossTravel.ToString("F4") + ". " + AiDiagnostic());
            }

            private void BlockedStopRecovery()
            {
                // A7 frame 1553, translated so the Boss root is the fixture origin.
                // The player then uses ordinary forward walking, as in the recorded
                // 27.0-28.4s approach. No imported pose, forced attack or teleport.
                Fixture(new Vector3(1.70009685f, .05f, 7.94764304f), new Vector3(0, .05f, 0),
                    Quaternion.LookRotation(new Vector3(-.858712673f, 0, .512457371f)));
                arena.boss.decisionEnabled = true;
                using (var diagnostic = new ReapproachTrace(arena, "encounter_stop_obstruction"))
                {
                    reapproachTrace = diagnostic;
                    try { BlockedStopRecoveryCore(); }
                    finally { reapproachTrace = null; }
                }
            }

            private void BlockedStopRecoveryCore()
            {
                double beginning = arena.BattleTime;
                Vector3 initialPlayer = arena.player.transform.position, initialBoss = arena.boss.transform.position;
                Vector3 previousBoss = initialBoss;
                float initialHealth = arena.player.fighter.Health, longestBlocked = 0, continuousBlocked = 0, maximumStep = 0;
                ArenaEventRecord stop = null, obstruction = null, hit = null, nextAttack = null;
                Vector3 rebalanceStart = Vector3.zero; bool wasRebalancing = false;
                // Let the actual start bridge enter locomotion before meeting it.
                Advance(1.1f);
                previousBoss = arena.boss.transform.position;
                while (arena.Active && arena.BattleTime - beginning < 12)
                {
                    ArenaCommand command = default;
                    if (stop == null && FighterDistance() > 2.3f)
                        command = MoveToward(arena.boss.transform.position - arena.player.transform.position, false);
                    Advance(Step, command);
                    Vector3 now = arena.boss.transform.position;
                    maximumStep = Mathf.Max(maximumStep, Vector3.ProjectOnPlane(now - previousBoss, Vector3.up).magnitude);
                    previousBoss = now;
                    stop = stop ?? FindEvent("boss_stop_step", "locomotion");
                    obstruction = obstruction ?? FindEvent("boss_locomotion_obstructed", "Stopping");
                    bool blocked = arena.boss.StateName == "PATH BLOCKED";
                    continuousBlocked = blocked ? continuousBlocked + Step : 0;
                    longestBlocked = Mathf.Max(longestBlocked, continuousBlocked);
                    Require(longestBlocked < 1.0f, "Stopping bridge remained continuously blocked for a second after meeting the player. " + AiDiagnostic());
                    Require(maximumStep < .10f, "Boss bypassed the blocking player by a discontinuous root jump: " + maximumStep);
                    if (arena.boss.StateName == "REBALANCING")
                    {
                        if (!wasRebalancing) rebalanceStart = now;
                        Require(!arena.boss.Attacking && Vector3.ProjectOnPlane(now - rebalanceStart, Vector3.up).magnitude < .025f,
                            "Boss moved or attacked while its obstructed step was rebalancing.");
                        wasRebalancing = true;
                    }
                    else wasRebalancing = false;
                    if (obstruction == null) continue;
                    foreach (var record in arena.Events)
                    {
                        if (record.kind == "hit" && record.time > obstruction.time && record.source == ArenaTeam.Boss.ToString()
                            && record.target == ArenaTeam.Player.ToString()) { hit = record; break; }
                    }
                    if (hit == null) continue;
                    nextAttack = FindOwningAttack(hit);
                    Require(nextAttack != null && nextAttack.time > obstruction.time,
                        "Post-obstruction damage was not from a new autonomous action.");
                    ArenaEventRecord recovered = FindLifecycleEvent("boss_recovered", nextAttack);
                    if (recovered == null) continue;
                    Require(stop != null && stop.time < obstruction.time && FindEvent("boss_start_finished", "locomotion") != null,
                        "Fixture did not establish a real start, approach and obstructed stopping bridge.");
                    float playerTravel = Vector3.ProjectOnPlane(arena.player.transform.position - initialPlayer, Vector3.up).magnitude;
                    float bossTravel = Vector3.ProjectOnPlane(arena.boss.transform.position - initialBoss, Vector3.up).magnitude;
                    Require(playerTravel > 1.5f && bossTravel > 1.5f, "Both combatants did not physically approach before the obstruction.");
                    Require(hit.health < initialHealth && arena.player.fighter.Health < initialHealth,
                        "Post-obstruction autonomous action did not reduce real health.");
                    Require(arena.player.fighter.DodgeCount == 0 && !arena.player.fighter.Invulnerable && !arena.player.fighter.TrainingInvincible,
                        "Obstruction fixture used dodge or training invulnerability.");
                    Require(!arena.boss.Attacking && !arena.effects.WarningVisible,
                        "The autonomous attack after obstruction did not completely recover.");
                    Debug.Log("ARENA_CHECK_BLOCKED_STOP_RECOVERY stop=" + (stop.time - beginning).ToString("F5") +
                        " obstructed=" + (obstruction.time - beginning).ToString("F5") + " nextAttack=" + (nextAttack.time - beginning).ToString("F5") +
                        " hit=" + (hit.time - beginning).ToString("F5") + " recovered=" + (recovered.time - beginning).ToString("F5") +
                        " longestBlocked=" + longestBlocked.ToString("F5") + " maximumRootStep=" + maximumStep.ToString("F5") +
                        " playerTravel=" + playerTravel.ToString("F4") + " bossTravel=" + bossTravel.ToString("F4") + " health=" + hit.health);
                    return;
                }
                throw new InvalidOperationException("Player approach did not produce an obstructed stop followed by a complete autonomous damaging attack. " +
                    "stop=" + (stop != null) + " obstruction=" + (obstruction != null) + " hit=" + (hit != null) +
                    " longestBlocked=" + longestBlocked.ToString("F4") + ". " + AiDiagnostic());
            }

            private void CloseSideAi()
            {
                Vector3 sidePosition = (Vector3.forward + Vector3.right).normalized * .8f;
                sidePosition.y = .05f;
                Fixture(sidePosition, new Vector3(0, .05f, 0), Quaternion.identity);
                arena.boss.decisionEnabled = true;
                float initialDistance = FighterDistance(), initialAngle = FacingAngle();
                Require(initialDistance >= .70f && initialDistance <= .85f && initialAngle >= 30 && initialAngle <= 60,
                    "Near-side fixture did not establish the intended real capsule arrangement. " + AiDiagnostic());
                float health = arena.player.fighter.Health, blockedSeconds = 0;
                double beginning = arena.BattleTime;
                while (arena.Active && arena.BattleTime - beginning < 20)
                {
                    Advance(Step);
                    if (arena.boss.StateName == "PATH BLOCKED") blockedSeconds += Step;
                    // Any normal autonomous skill may solve the close-side encounter. The
                    // test does not prescribe Stomp, change heading, or force an attack.
                    foreach (ArenaEventRecord attack in arena.Events)
                    {
                        if (attack.kind != "boss_attack_started" || attack.time < beginning) continue;
                        ArenaEventRecord hit = FindLifecycleEvent("hit", attack);
                        ArenaEventRecord recovered = FindLifecycleEvent("boss_recovered", attack);
                        if (hit == null || recovered == null) continue;
                        Require(hit.source == ArenaTeam.Boss.ToString() && hit.target == ArenaTeam.Player.ToString() &&
                            arena.player.fighter.Health < health && hit.health < health,
                            "Near-side autonomous attack did not damage the actual player. " + AiDiagnostic());
                        Require(recovered.time >= hit.time && hit.time >= attack.time,
                            "Near-side attack lifecycle events are out of order. " + AiDiagnostic());
                        Require(arena.player.fighter.DodgeCount == 0 && !arena.player.fighter.TrainingInvincible,
                            "Near-side fixture used dodge or training invincibility.");
                        Debug.Log("ARENA_CHECK_CLOSE_SIDE_AI initialGap=" + initialDistance.ToString("F4") +
                            " initialAngle=" + initialAngle.ToString("F2") + " skill=" + attack.skill +
                            " hitTime=" + hit.time.ToString("F4") + " recoveredTime=" + recovered.time.ToString("F4") +
                            " blockedSeconds=" + blockedSeconds.ToString("F4") + " health=" + hit.health);
                        return;
                    }
                }
                throw new InvalidOperationException("A stationary close-side player prevented the Boss from completing any autonomous damaging attack within 20 seconds. " +
                    "initialGap=" + initialDistance.ToString("F4") + " initialAngle=" + initialAngle.ToString("F2") +
                    " blockedSeconds=" + blockedSeconds.ToString("F4") + ". " + AiDiagnostic());
            }

            // Combo punches have separate strike IDs. Associate lifecycle records by
            // their actual ordered attack interval, never by the final fist's ID.
            private ArenaEventRecord FindLifecycleEvent(string kind, ArenaEventRecord attack)
            {
                bool inside = false;
                foreach (ArenaEventRecord record in arena.Events)
                {
                    if (ReferenceEquals(record, attack)) { inside = true; continue; }
                    if (!inside) continue;
                    if (record.kind == "boss_attack_started") break;
                    if (record.kind != kind) continue;
                    if (kind == "hit" && (record.source != ArenaTeam.Boss.ToString() || record.target != ArenaTeam.Player.ToString())) continue;
                    if (kind == "boss_recovered" && record.skill != attack.skill) continue;
                    return record;
                }
                return null;
            }

            private ArenaEventRecord FindOwningAttack(ArenaEventRecord result)
            {
                ArenaEventRecord owner = null;
                foreach (ArenaEventRecord record in arena.Events)
                {
                    if (ReferenceEquals(record, result)) return owner;
                    if (record.kind == "boss_attack_started") owner = record;
                }
                return null;
            }

            private float FighterDistance()
            { return Vector3.ProjectOnPlane(arena.player.transform.position - arena.boss.transform.position, Vector3.up).magnitude; }

            private float FacingAngle()
            { return Vector3.Angle(arena.boss.transform.forward, Vector3.ProjectOnPlane(arena.player.transform.position - arena.boss.transform.position, Vector3.up)); }

            private string AiDiagnostic()
            {
                var recent = new List<string>();
                for (int i = Mathf.Max(0, arena.Events.Count - 12); i < arena.Events.Count; i++)
                {
                    ArenaEventRecord record = arena.Events[i];
                    recent.Add(record.time.ToString("F3") + " " + record.kind + "/" + record.skill +
                        " #" + record.strike + (record.kind == "hit" ? " hp=" + record.health : ""));
                }
                return "state=" + arena.State + ", brain=" + arena.boss.StateName + ", t=" + arena.BattleTime.ToString("F3") +
                    ", gap=" + FighterDistance().ToString("F4") + ", angle=" + FacingAngle().ToString("F2") +
                    ", speed=" + arena.boss.ActualSpeed.ToString("F4") + ", jabRange=" + arena.boss.jabDistance.ToString("F4") +
                    ", jabRadius=" + arena.boss.jabRadius.ToString("F4") + ", boss=" + arena.boss.transform.position +
                    ", player=" + arena.player.transform.position + ", health=" + arena.player.fighter.Health +
                    "; recent events: " + string.Join(" | ", recent);
            }

            // Read-only observation of the existing catch-up case. Extra queries do
            // not apply damage, move a fighter, alter input, or change its deadline.
            private sealed class ReapproachTrace : IDisposable
            {
                [Serializable] private sealed class Report
                {
                    public string source = "Actual DefaultAiReapproach real-input/clip/physics trace; diagnostic only";
                    public double endTime;
                    public float health;
                    public Row[] rows;
                    public ArenaEventRecord[] events;
                }
                [Serializable] private sealed class Row
                {
                    public double time;
                    public string eventKind, eventSkill, bossState, clip, playerState;
                    public long eventStrike;
                    public float skillTime, gap, playerSpeed, playerHealth, minimumRightRadius, minimumLeftRadius, ringDistance;
                    public int fistIndex, hits, dodges;
                    public bool playerInvulnerable, training, capsuleEnabled, capsuleGrounded;
                    public Vector3 rootPosition, rootScale, visualLocalPosition, visualScale, visualPosition;
                    public Quaternion rootRotation, visualLocalRotation, visualRotation;
                    public Vector3 rightHand, leftHand, rightFoot, leftFoot, rightHandInRoot, rightFootInRoot;
                    public Vector3 playerPosition, playerScale, capsuleCentre, capsuleExtents, expectedCapsuleCentre, capsuleClosestToRight;
                    public Quaternion playerRotation;
                    public Vector3 groundCentre, waveCentre;
                    public float waveRadius, ringOuterRadius;
                    public bool rightExact, leftExact, rightQueryContainsPlayer, leftQueryContainsPlayer, ringQueryContainsPlayer;
                    public string[] rightQuery, leftQuery, ringQuery;
                    public ArenaCommand input;
                }
                private readonly ArenaSession arena;
                private readonly List<Row> rows = new List<Row>();
                private readonly string caseName;
                private int eventCursor;
                private double nextSample;
                private Vector3 previousRight, previousLeft;
                public ReapproachTrace(ArenaSession value, string name = "default_ai_reapproach")
                {
                    arena = value; caseName = name; previousRight = arena.boss.FistPosition;
                    previousLeft = arena.boss.Socket("socket_hand.L").position;
                    nextSample = arena.BattleTime;
                }
                public void AfterStep(ArenaCommand command)
                {
                    bool sampled = false;
                    while (eventCursor < arena.Events.Count)
                    {
                        var record = arena.Events[eventCursor++];
                        if (record.kind == "boss_attack_started" || record.kind == "boss_punch_window" ||
                            record.kind == "boss_ground_contact" || record.kind == "boss_recovery_started" ||
                            record.kind == "boss_recovered" || record.kind == "hit" || record.kind == "dodge" ||
                            record.kind == "boss_stop_step" || record.kind == "boss_locomotion_obstructed" ||
                            record.kind == "boss_obstructed_step_settle" || record.kind == "boss_locomotion_resumed")
                        { Sample(command, record); sampled = true; }
                    }
                    float time = arena.boss.SkillTime;
                    bool contact = arena.boss.Attacking && (arena.boss.CurrentAttack == ArenaAttack.Stomp
                        ? time >= .98f && time <= 1.42f
                        : (time >= .42f && time <= .60f) || (time >= 1f && time <= 1.14f));
                    if (!sampled && (contact || arena.BattleTime >= nextSample)) Sample(command, null);
                    if (arena.BattleTime >= nextSample) nextSample = arena.BattleTime + .25;
                    previousRight = arena.boss.FistPosition;
                    previousLeft = arena.boss.Socket("socket_hand.L").position;
                }
                private void Sample(ArenaCommand command, ArenaEventRecord record)
                {
                    var boss = arena.boss; var player = arena.player; var body = player.capsule;
                    Vector3 right = boss.FistPosition, left = boss.Socket("socket_hand.L").position;
                    Vector3 fromRight = previousRight, fromLeft = previousLeft;
                    if (record != null && record.kind == "boss_punch_window")
                    {
                        if (record.skill == "cross_punch") fromLeft = left; else fromRight = right;
                    }
                    Physics.SyncTransforms();
                    Collider[] rq = Physics.OverlapCapsule(fromRight, right, boss.jabRadius, (1 << 9) | (1 << 10), QueryTriggerInteraction.Ignore);
                    Collider[] lq = Physics.OverlapCapsule(fromLeft, left, boss.jabRadius, (1 << 9) | (1 << 10), QueryTriggerInteraction.Ignore);
                    float ringRadius = ArenaCombatFx.PulseRadius(Mathf.Max(0, boss.SkillTime - 1f), boss.stompRadius);
                    Collider[] gq = Physics.OverlapCapsule(boss.GroundCentre + Vector3.up * .1f,
                        boss.GroundCentre + Vector3.up * 1.3f, ringRadius, (1 << 9) | (1 << 10), QueryTriggerInteraction.Ignore);
                    var row = new Row
                    {
                        time = arena.BattleTime, input = command, eventKind = record?.kind ?? "sample", eventSkill = record?.skill ?? "",
                        eventStrike = record?.strike ?? 0, bossState = boss.StateName, clip = boss.animationDriver.ClipName,
                        skillTime = boss.SkillTime, fistIndex = boss.ComboHitIndex, gap = Vector3.ProjectOnPlane(player.transform.position - boss.transform.position, Vector3.up).magnitude,
                        playerState = player.ActionName, playerSpeed = player.Speed, playerHealth = player.fighter.Health,
                        hits = player.fighter.HitCount, dodges = player.fighter.DodgeCount,
                        playerInvulnerable = player.fighter.Invulnerable, training = player.fighter.TrainingInvincible,
                        rootPosition = boss.transform.position, rootRotation = boss.transform.rotation, rootScale = boss.transform.lossyScale,
                        visualLocalPosition = boss.visual.localPosition, visualLocalRotation = boss.visual.localRotation,
                        visualScale = boss.visual.lossyScale, visualPosition = boss.visual.position, visualRotation = boss.visual.rotation,
                        rightHand = right, leftHand = left, rightFoot = boss.FootPosition, leftFoot = boss.Socket("socket_foot.L").position,
                        rightHandInRoot = boss.transform.InverseTransformPoint(right), rightFootInRoot = boss.transform.InverseTransformPoint(boss.FootPosition),
                        playerPosition = player.transform.position, playerRotation = player.transform.rotation, playerScale = player.transform.lossyScale,
                        capsuleEnabled = body.enabled, capsuleGrounded = body.isGrounded,
                        capsuleCentre = body.bounds.center, capsuleExtents = body.bounds.extents,
                        expectedCapsuleCentre = body.transform.TransformPoint(body.center), capsuleClosestToRight = body.ClosestPoint(right),
                        rightExact = ArenaContactGeometry.Intersects(fromRight, right, boss.jabRadius, body),
                        leftExact = ArenaContactGeometry.Intersects(fromLeft, left, boss.jabRadius, body),
                        minimumRightRadius = MinimumRadius(fromRight, right, body), minimumLeftRadius = MinimumRadius(fromLeft, left, body),
                        rightQueryContainsPlayer = Array.IndexOf(rq, (Collider)body) >= 0, leftQueryContainsPlayer = Array.IndexOf(lq, (Collider)body) >= 0,
                        ringQueryContainsPlayer = Array.IndexOf(gq, (Collider)body) >= 0,
                        rightQuery = Describe(rq), leftQuery = Describe(lq), ringQuery = Describe(gq),
                        groundCentre = boss.GroundCentre, ringOuterRadius = ringRadius,
                        ringDistance = Vector3.ProjectOnPlane(body.transform.TransformPoint(body.center) - boss.GroundCentre, Vector3.up).magnitude,
                        waveCentre = arena.effects.flames != null ? arena.effects.flames.LatestWaveCentre : Vector3.zero,
                        waveRadius = arena.effects.flames != null ? arena.effects.flames.LatestWaveRadius : 0
                    };
                    rows.Add(row);
                }
                private static float MinimumRadius(Vector3 a, Vector3 b, CharacterController body)
                {
                    if (ArenaContactGeometry.Intersects(a, b, 0, body)) return 0;
                    float low = 0, high = 64;
                    for (int i = 0; i < 18; i++)
                    {
                        float middle = (low + high) * .5f;
                        if (ArenaContactGeometry.Intersects(a, b, middle, body)) high = middle; else low = middle;
                    }
                    return high;
                }
                private static string[] Describe(Collider[] values)
                {
                    var result = new string[values.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        var fighter = values[i].GetComponentInParent<ArenaFighter>();
                        result[i] = values[i].name + " #" + values[i].GetInstanceID() + " layer=" + values[i].gameObject.layer +
                            " team=" + (fighter != null ? fighter.team.ToString() : "none");
                    }
                    return result;
                }
                public void Dispose()
                {
                    try
                    {
                        string[] args = Environment.GetCommandLineArgs(); string output = null;
                        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--arena-output") output = args[i + 1];
                        if (string.IsNullOrEmpty(output)) { Debug.Log("ARENA_REAPPROACH_TRACE no output argument; rows=" + rows.Count); return; }
                        Directory.CreateDirectory(output);
                        string path = Path.Combine(output, caseName + "_trace.json");
                        if (File.Exists(path)) path = Path.Combine(output, caseName + "_trace_" + Guid.NewGuid().ToString("N") + ".json");
                        var report = new Report { source = "Actual " + caseName + " real-input/clip/physics trace; diagnostic only",
                            endTime = arena.BattleTime, health = arena.player.fighter.Health, rows = rows.ToArray(), events = arena.Events.ToArray() };
                        File.WriteAllText(path, JsonUtility.ToJson(report, true));
                        Debug.Log("ARENA_REAPPROACH_TRACE " + path + " rows=" + rows.Count + " health=" + report.health);
                    }
                    catch (Exception exception) { Debug.LogWarning("ARENA_REAPPROACH_TRACE_WRITE_FAILED " + exception); }
                }
            }

            private sealed class EscapeTrace : IDisposable
            {
                private readonly ArenaSession arena;
                private readonly string name;
                private readonly Vector3 origin, intendedDirection;
                private readonly double startTime;
                private readonly List<string> rows = new List<string>();
                private double nextSample = .05, beforeTime;
                private ArenaCommand input;
                private Vector3 beforePlayer, beforeFist;
                private Bounds beforeBounds;

                public EscapeTrace(ArenaSession value, Vector3 direction, string label)
                {
                    arena = value; name = label;
                    origin = arena.player.transform.position;
                    intendedDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
                    startTime = arena.BattleTime;
                    arena.player.fighter.Damaged += OnDamage;
                    rows.Add(Sample("initial"));
                }

                public void BeforeStep(ArenaCommand command)
                {
                    input = command;
                    beforeTime = arena.BattleTime;
                    beforePlayer = arena.player.transform.position;
                    beforeFist = arena.boss.FistPosition;
                    beforeBounds = arena.player.capsule.bounds;
                }

                public void AfterStep()
                {
                    if (arena.BattleTime - startTime + .000001 >= nextSample)
                    {
                        rows.Add(Sample("sample"));
                        while (nextSample <= arena.BattleTime - startTime + .000001) nextSample += .05;
                    }
                }

                private void OnDamage(ArenaHit hit)
                {
                    rows.Add("contact_previous_substep t=" + (beforeTime - startTime).ToString("F6") +
                        " player=" + V(beforePlayer) + " fist=" + V(beforeFist) +
                        " boundsCenter=" + V(beforeBounds.center) + " boundsExtents=" + V(beforeBounds.extents));
                    rows.Add(Sample("DAMAGE") + " strike=" + hit.strike + " skill=" + hit.skill +
                        " hitPoint=" + V(hit.point) + " fistStep=" + V(arena.boss.FistPosition - beforeFist));
                    int disabled = 0;
                    foreach (Collider collider in arena.player.GetComponentsInChildren<Collider>(true))
                    {
                        if (!collider.enabled || !collider.gameObject.activeInHierarchy) { disabled++; continue; }
                        Bounds bounds = collider.bounds;
                        rows.Add("enabledPlayerCollider path=" + Path(collider.transform) + " type=" + collider.GetType().Name +
                            " layer=" + collider.gameObject.layer + " trigger=" + collider.isTrigger +
                            " transform=" + V(collider.transform.position) + " boundsCenter=" + V(bounds.center) +
                            " boundsExtents=" + V(bounds.extents) + " nearestFist=" + V(collider.ClosestPoint(arena.boss.FistPosition)));
                    }
                    rows.Add("disabledPlayerColliders=" + disabled);
                }

                private string Sample(string label)
                {
                    Vector3 player = arena.player.transform.position;
                    Vector3 fist = arena.boss.FistPosition;
                    Vector3 forward = arena.view.PlanarForward;
                    Vector3 right = Vector3.Cross(Vector3.up, forward);
                    Vector3 requested = forward * input.move.y + right * input.move.x;
                    Bounds bounds = arena.player.capsule.bounds;
                    return label + " t=" + (arena.BattleTime - startTime).ToString("F6") +
                        " skillT=" + arena.boss.SkillTime.ToString("F6") + " run=" + input.run +
                        " input=(" + input.move.x.ToString("F4") + "," + input.move.y.ToString("F4") + ")" +
                        " requestedWorld=" + V(requested) + " player=" + V(player) +
                        " displacement=" + V(player - origin) + " alongEscape=" + Vector3.Dot(player - origin, intendedDirection).ToString("F5") +
                        " speed=" + arena.player.Speed.ToString("F5") + " fist=" + V(fist) +
                        " boundsCenter=" + V(bounds.center) + " boundsExtents=" + V(bounds.extents) +
                        " fistToCapsule=" + Vector3.Distance(fist, arena.player.capsule.ClosestPoint(fist)).ToString("F5") +
                        " hp=" + arena.player.fighter.Health + " bossYaw=" + arena.boss.transform.eulerAngles.y.ToString("F3");
                }

                private string Path(Transform transform)
                {
                    string result = transform.name;
                    while (transform != arena.player.transform && transform.parent != null)
                    { transform = transform.parent; result = transform.name + "/" + result; }
                    return result;
                }

                private static string V(Vector3 value)
                { return "(" + value.x.ToString("F5") + "," + value.y.ToString("F5") + "," + value.z.ToString("F5") + ")"; }

                public string Describe()
                { return "\nEscape trace " + name + ":\n" + string.Join("\n", rows); }

                public void Dispose() { arena.player.fighter.Damaged -= OnDamage; }
            }

            private void CalibratePunch()
            {
                if (punchCalibrated) return;
                Fixture(new Vector3(0, .05f, 7), new Vector3(0, .05f, 0), Quaternion.identity);
                Require(arena.boss.TrySkill(ArenaAttack.Jab), "Boss rejected a valid forced jab request.");
                Advance(.47f);
                Vector3 socket = arena.boss.FistPosition;
                punchTarget = new Vector3(socket.x, .05f, socket.z);
                Require(Vector3.Dot(Vector3.ProjectOnPlane(socket - arena.boss.transform.position, Vector3.up), arena.boss.transform.forward) > .60f,
                    "Imported jab contact is not in front of the Boss gameplay heading: " + socket);
                Require(Vector3.ProjectOnPlane(punchTarget, Vector3.up).magnitude > .68f,
                    "Imported contact fist lies inside the combined standing capsules: " + socket);
                PunchFixture();
                double start = arena.BattleTime;
                Require(arena.boss.TrySkill(ArenaAttack.Jab), "Boss rejected the contact fixture jab.");
                while (arena.BattleTime - start < .85 && arena.player.fighter.HitCount == 0 && arena.Active)
                    Advance(Step);
                ArenaEventRecord contact = FindEvent("hit", "drive_punch");
                Require(contact != null && contact.target == ArenaTeam.Player.ToString(),
                    "Imported fist at " + socket + " never overlapped the real player in its attack window.");
                punchContactTime = (float)(contact.time - start);
                Require(punchContactTime >= .425f && punchContactTime <= .60f,
                    "Jab contact fell outside its committed strike window: " + punchContactTime);
                punchCalibrated = true;
                Debug.Log("ARENA_CHECK_PUNCH_CALIBRATION target=" + punchTarget + " contact=" + punchContactTime.ToString("F6"));
            }

            private void PunchFixture(float playerHealth = -1)
            { Fixture(punchTarget, new Vector3(0, .05f, 0), Quaternion.identity, playerHealth); }

            private void JabHit()
            {
                CalibratePunch();
                PunchFixture();
                float health = arena.player.fighter.Health;
                Require(arena.boss.TrySkill(ArenaAttack.Jab), "Boss rejected actual jab.");
                Advance(1.0f);
                Same(arena.player.fighter.Health, health - arena.boss.jabDamage, "Jab damage did not match one actual strike.");
                Require(arena.player.fighter.HitCount == 1 && CountEvents("hit", "drive_punch") == 1,
                    "Actual jab either missed or repeatedly damaged during its window.");
                Require(CountEvents("boss_attack_started", "Jab") == 1, "Actual jab start was not recorded.");
            }

            private void JabWhiff()
            {
                CalibratePunch();
                PunchFixture();
                float health = arena.player.fighter.Health;
                Vector3 before = arena.player.transform.position;
                Require(arena.boss.TrySkill(ArenaAttack.Jab), "Boss rejected whiff fixture jab.");
                Vector3 side = punchTarget.x < 0 ? Vector3.left : Vector3.right;
                string escapeDiagnostic;
                using (var trace = new EscapeTrace(arena, side, "calibrated_jab_lateral"))
                {
                    Advance(.10f, default, trace);
                    Advance(.8f, MoveToward(side, true), trace);
                    escapeDiagnostic = trace.Describe();
                }
                Advance(1.2f);
                Same(arena.player.fighter.Health, health, "A clearly lateral escape still received punch damage." + escapeDiagnostic);
                Require(arena.player.fighter.HitCount == 0 && arena.player.fighter.DodgeCount == 0,
                    "Whiff was actually a hit or invulnerable dodge.");
                Require(Vector3.ProjectOnPlane(arena.player.transform.position - before, Vector3.up).magnitude > 1.8f,
                    "Whiff fixture did not physically leave the jab path.");
                Require(CountEvents("boss_attack_started", "Jab") == 1 && CountEvents("boss_recovered", "Jab") == 1,
                    "The whiff did not run an entire real attack and recovery.");
            }

            private void DodgeWindow()
            {
                CalibratePunch();
                // Find one reproducible moving overlap inside the genuine dodge window. Each
                // attempt uses unchanged skill damage/timing and fully resets previous hits.
                var failures = new List<string>();
                foreach (float lead in new[] { .06f, .08f, .10f, .12f })
                    foreach (Vector3 direction in new[] { -punchTarget, Vector3.left, Vector3.right, punchTarget })
                    {
                        PunchFixture();
                        float health = arena.player.fighter.Health;
                        Require(arena.boss.TrySkill(ArenaAttack.Jab), "Boss rejected dodge fixture jab.");
                        Advance(Mathf.Max(0, punchContactTime - lead));
                        Vector3 before = arena.player.transform.position;
                        ArenaCommand input = MoveToward(direction);
                        input.dodge = true;
                        Advance(.65f, input);
                        float moved = Vector3.ProjectOnPlane(arena.player.transform.position - before, Vector3.up).magnitude;
                        bool passed = arena.player.fighter.DodgeCount > 0 && arena.player.fighter.HitCount == 0 &&
                            Mathf.Abs(arena.player.fighter.Health - health) < .0001f && moved > .04f;
                        if (passed)
                        {
                            Require(CountEvents("player_dodge_started", "dodge") == 1 && CountEvents("dodge", "drive_punch") == 1,
                                "Avoidance did not come from one real dodge intersecting the boss strike.");
                            Require(!arena.player.fighter.Invulnerable && !arena.player.Dodging,
                                "Dodge invulnerability remained active after the dodge ended.");
                            Debug.Log("ARENA_CHECK_DODGE lead=" + lead + " travel=" + moved.ToString("F5") + " direction=" + direction);
                            return;
                        }
                        failures.Add("lead=" + lead + " dir=" + direction + " hits=" + arena.player.fighter.HitCount +
                            " avoids=" + arena.player.fighter.DodgeCount + " moved=" + moved.ToString("F4"));
                    }
                throw new InvalidOperationException("No moving dodge actually overlapped and avoided the jab. " + string.Join("; ", failures));
            }

            private void SwordVictory()
            {
                Fixture(new Vector3(0, .05f, -1.1f), new Vector3(0, .05f, 0), Quaternion.Euler(0, 180, 0), -1, 1);
                Advance(Step, new ArenaCommand { attack = true });
                for (int i = 0; i < 100 && arena.Active; i++) Advance(Step);
                Require(arena.State == ArenaRoundState.Victory && arena.boss.fighter.Dead,
                    "The real player sword sweep did not kill the one-HP Boss.");
                ArenaEventRecord hit = FindEvent("hit", "player_sword");
                Require(hit != null && hit.source == ArenaTeam.Player.ToString() && hit.target == ArenaTeam.Boss.ToString() && hit.health == 0,
                    "Victory lacks an actual final player sword hit.");
                Require(CountEvents("victory") == 1 && !arena.boss.Attacking, "Victory transition did not occur exactly once.");
                Same(arena.boss.fighter.Health, 0, "Boss health did not clamp at zero.");
                Require(arena.effects.ActiveCount > 0, "Actual sword impact created no live response.");
                VerifyRestart();
            }

            private void JabDefeat()
            {
                CalibratePunch();
                PunchFixture(1);
                Require(arena.boss.TrySkill(ArenaAttack.Jab), "Boss rejected defeat fixture jab.");
                for (int i = 0; i < 110 && arena.Active; i++) Advance(Step);
                Require(arena.State == ArenaRoundState.Defeat && arena.player.fighter.Dead,
                    "The real boss jab did not kill the one-HP player.");
                ArenaEventRecord hit = FindEvent("hit", "drive_punch");
                Require(hit != null && hit.source == ArenaTeam.Boss.ToString() && hit.target == ArenaTeam.Player.ToString() && hit.health == 0,
                    "Defeat lacks an actual final boss jab hit.");
                Require(CountEvents("defeat") == 1 && !arena.boss.Attacking, "Defeat did not cancel its committed attack exactly once.");
                Same(arena.player.fighter.Health, 0, "Player health did not clamp at zero.");
                Require(arena.effects.ActiveCount > 0, "Actual killing punch created no live response.");
                Advance(.20f);
                Require(CountEvents("hit", "drive_punch") == 1 && CountEvents("defeat") == 1,
                    "Dead player continued receiving attack events.");
                VerifyRestart();
            }

            private void RestartState()
            {
                DefaultFixture();
                Require(arena.boss.TrySkill(ArenaAttack.Stomp), "Boss rejected pending-action restart fixture.");
                Advance(.2f);
                Require(arena.boss.Attacking && arena.effects.WarningVisible, "Real wind-up produced no visible warning.");
                arena.effects.Impact(arena.player.transform.position + Vector3.up, true, ArenaTeam.Boss);
                Require(arena.effects.ActiveCount > 0, "FX reset fixture contains no live effect.");
                VerifyRestart();
                Require(arena.NewStrike() == 1, "Restart did not reset strike numbering after clearing fighter caches.");
            }

            private void VerifyRestart()
            {
                arena.Restart(true);
                Require(arena.State == ArenaRoundState.Fighting && arena.BattleTime == 0, "Restart did not reset state/clock.");
                Same(arena.player.fighter.Health, arena.player.fighter.maximumHealth, "Restart did not restore player health.");
                Same(arena.boss.fighter.Health, arena.boss.fighter.maximumHealth, "Restart did not restore boss health.");
                Require(!arena.player.fighter.Dead && !arena.boss.fighter.Dead && !arena.player.fighter.Invulnerable &&
                    !arena.player.fighter.TrainingInvincible && !arena.boss.fighter.TrainingInvincible,
                    "Restart left a dead or training/invulnerable fighter.");
                Require(arena.player.fighter.HitCount == 0 && arena.player.fighter.DodgeCount == 0 && arena.boss.fighter.HitCount == 0,
                    "Restart left previous hit or dodge counters.");
                Require(!arena.boss.Attacking && !arena.player.Dodging && arena.player.AttackTime < 0,
                    "Restart left a pending combat action.");
                Require(arena.effects.ActiveCount == 0 && !arena.effects.WarningVisible,
                    "Restart left a burst or warning visible.");
                Require(arena.Events.Count == 1 && arena.Events[0].kind == "battle_started",
                    "Restart left prior battle events.");
                Require(!arena.view.LockedOn, "Restart left the old target lock enabled.");
            }

            private int CountEvents(string kind, string skill = null)
            {
                int count = 0;
                foreach (ArenaEventRecord record in arena.Events)
                    if (record.kind == kind && (skill == null || record.skill == skill)) count++;
                return count;
            }

            private ArenaEventRecord FindEvent(string kind, string skill)
            {
                foreach (ArenaEventRecord record in arena.Events)
                    if (record.kind == kind && record.skill == skill) return record;
                return null;
            }

            private ArenaEventRecord FindStrikeEvent(string kind, string skill, long strike)
            {
                foreach (ArenaEventRecord record in arena.Events)
                    if (record.kind == kind && (skill == null || record.skill == skill) && record.strike == strike) return record;
                return null;
            }

            private static void Same(float actual, float expected, string error)
            { Require(Mathf.Abs(actual - expected) < .0001f, error + " Actual=" + actual + ", expected=" + expected); }

            private static void Require(bool condition, string error)
            { if (!condition) throw new InvalidOperationException(error); }
        }
    }
}
