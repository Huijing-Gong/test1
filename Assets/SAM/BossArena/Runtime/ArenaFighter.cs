using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    public enum ArenaTeam { Player, Boss }

    [Serializable]
    public struct ArenaCommand
    {
        public Vector2 move, look;
        public KingOfGraphic.Hero.HeroAuthorInput author;
        public bool run, dodge, attack, attackHeld, attackReleased, toggleLock, guardHeld, guardPressed;
        public ArenaCommand WithoutEdges() => new ArenaCommand { move = move, run = run, guardHeld = guardHeld, attackHeld = attackHeld, author=author.WithoutEdges() };
    }

    public struct ArenaHit
    {
        public ArenaFighter source, target;
        public long strike;
        public float damage, push;
        public Vector3 point, direction;
        public string skill;
    }

    // Health belongs to the combatant, not an animation, particle or preview clock.
    public sealed class ArenaFighter : MonoBehaviour
    {
        public ArenaTeam team;
        public float maximumHealth = 100;
        public float Health { get; private set; }
        public bool Dead => Health <= 0;
        public bool Invulnerable { get; set; }
        public bool TrainingInvincible { get; set; }
        public int HitCount { get; private set; }
        public int DodgeCount { get; private set; }
        public int PerfectDodgeCount { get; private set; }
        public int BlockCount { get; private set; }
        public int PerfectBlockCount { get; private set; }
        public ArenaDefenseResult LastDefense { get; private set; } = ArenaDefenseResult.None;
        public Func<ArenaHit, ArenaDefenseResult> ResolveDefense { get; set; }
        public event Action<ArenaDefenseEvent> DefenseResolved;
        private long resolvingAvoidedStrike;
        public Vector3 Knockback { get; private set; }
        public event Action<ArenaHit> Damaged;
        public event Action<ArenaHit> Avoided;
        public event Action Died;
        private readonly HashSet<long> resolvedStrikes = new HashSet<long>();

        private void Awake() { ResetFighter(); }

        public void ResetFighter()
        {
            Health = maximumHealth;
            Invulnerable = false;
            TrainingInvincible = false;
            HitCount = DodgeCount = 0;
            PerfectDodgeCount = BlockCount = PerfectBlockCount = 0;
            LastDefense = ArenaDefenseResult.None; resolvingAvoidedStrike = 0;
            Knockback = Vector3.zero;
            resolvedStrikes.Clear();
        }

        public bool Receive(ArenaHit hit)
        {
            if (hit.source == null || hit.source == this || hit.source.Dead || Dead ||
                hit.source.team == team || hit.damage <= 0 || resolvedStrikes.Contains(hit.strike)) return false;
            resolvedStrikes.Add(hit.strike);
            hit.target = this;
            LastDefense = ArenaDefenseResult.None;
            if (Invulnerable)
            {
                DodgeCount++;
                LastDefense = new ArenaDefenseResult(ArenaDefenseOutcome.Dodge, 0, 0);
                resolvingAvoidedStrike = hit.strike;
                try { Avoided?.Invoke(hit); }
                finally { resolvingAvoidedStrike = 0; }
                DefenseResolved?.Invoke(new ArenaDefenseEvent(hit, LastDefense, Health, Health));
                return false;
            }
            ArenaHit incoming = hit;
            float healthBefore = Health;
            ArenaDefenseResult defense = ResolveDefense != null ? ResolveDefense(hit) : ArenaDefenseResult.None;
            if (defense.IsBlock)
            {
                LastDefense = defense;
                BlockCount++;
                if (defense.outcome == ArenaDefenseOutcome.PerfectBlock) PerfectBlockCount++;
                hit.damage *= defense.damageMultiplier;
                hit.push *= defense.pushMultiplier;
                if (hit.damage <= 0)
                {
                    DefenseResolved?.Invoke(new ArenaDefenseEvent(incoming, defense, healthBefore, Health));
                    return false;
                }
            }
            HitCount++;
            if (!TrainingInvincible) Health = Mathf.Max(0, Health - hit.damage);
            // Boss armour takes light hits without cancelling a committed attack.
            if (team == ArenaTeam.Player)
                Knockback += Vector3.ProjectOnPlane(hit.direction, Vector3.up).normalized * hit.push;
            Damaged?.Invoke(hit);
            if (defense.IsBlock)
                DefenseResolved?.Invoke(new ArenaDefenseEvent(incoming, defense, healthBefore, Health));
            if (Dead) { Invulnerable = false; Died?.Invoke(); }
            return true;
        }

        // Promotion is possible only from the synchronous real Avoided callback.
        // Neither a near miss nor a duplicate collision can award another reward.
        public bool MarkPerfectDodgeDuringAvoided(long strike)
        {
            if (resolvingAvoidedStrike != strike || strike <= 0 ||
                LastDefense.outcome != ArenaDefenseOutcome.Dodge) return false;
            LastDefense = new ArenaDefenseResult(ArenaDefenseOutcome.PerfectDodge, 0, 0);
            PerfectDodgeCount++;
            return true;
        }

        public Vector3 ConsumeKnockback(float delta)
        {
            Vector3 movement = Knockback * delta;
            Knockback = Vector3.MoveTowards(Knockback, Vector3.zero, 18 * delta);
            return movement;
        }
        public void ClearKnockback()=>Knockback=Vector3.zero;
    }
}
