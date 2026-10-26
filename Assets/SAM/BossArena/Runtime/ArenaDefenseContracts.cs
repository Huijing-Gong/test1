using System;

namespace SamProduction.BossArena
{
    public enum ArenaDefenseOutcome
    {
        None, Dodge, PerfectDodge, Block, PerfectBlock, GuardBreak
    }

    // A decision over the existing ArenaHit. Only ArenaFighter commits health,
    // knockback and per-strike resolution; state modules do not damage actors.
    public readonly struct ArenaDefenseResult
    {
        public readonly ArenaDefenseOutcome outcome;
        public readonly float damageMultiplier;
        public readonly float pushMultiplier;

        public ArenaDefenseResult(ArenaDefenseOutcome outcome, float damageMultiplier, float pushMultiplier)
        {
            if (float.IsNaN(damageMultiplier) || float.IsInfinity(damageMultiplier) ||
                float.IsNaN(pushMultiplier) || float.IsInfinity(pushMultiplier) ||
                damageMultiplier < 0 || damageMultiplier > 1 || pushMultiplier < 0 || pushMultiplier > 1)
                throw new ArgumentOutOfRangeException(nameof(damageMultiplier));
            this.outcome = outcome;
            this.damageMultiplier = damageMultiplier;
            this.pushMultiplier = pushMultiplier;
        }

        public static ArenaDefenseResult None => new ArenaDefenseResult(ArenaDefenseOutcome.None, 1, 1);
        public bool IsDodge => outcome == ArenaDefenseOutcome.Dodge || outcome == ArenaDefenseOutcome.PerfectDodge;
        public bool IsBlock => outcome == ArenaDefenseOutcome.Block || outcome == ArenaDefenseOutcome.PerfectBlock;
        public bool IsPerfect => outcome == ArenaDefenseOutcome.PerfectDodge || outcome == ArenaDefenseOutcome.PerfectBlock;
    }

    public readonly struct ArenaDefenseEvent
    {
        public readonly ArenaHit hit;
        public readonly ArenaDefenseResult result;
        public readonly float healthBefore;
        public readonly float healthAfter;

        public ArenaDefenseEvent(ArenaHit hit, ArenaDefenseResult result, float healthBefore, float healthAfter)
        {
            this.hit = hit;
            this.result = result;
            this.healthBefore = healthBefore;
            this.healthAfter = healthAfter;
        }
    }
}
