using UnityEngine;

namespace SamProduction.BossArena
{
    public static class ArenaSpellRules
    {
        public static bool IsSpell(ArenaAttack attack) => attack >= ArenaAttack.FireWave && attack <= ArenaAttack.DelayedBlast;
        public static bool IsMobile(ArenaAttack attack) => attack == ArenaAttack.GroundRift;
        public static bool CanSelect(ArenaAttack attack, float distance)
        {
            if (!IsSpell(attack) || float.IsNaN(distance) || float.IsInfinity(distance)) return false;
            return attack >= ArenaAttack.ThunderOrb ? distance >= 0 && distance <= 3.5f : distance >= 3.5f && distance <= 20f;
        }
        public static float Cooldown(ArenaAttack attack)
        {
            switch (attack)
            {
                case ArenaAttack.FireWave: return 8;
                case ArenaAttack.FireBird: return 10;
                case ArenaAttack.LightningCall: return 11;
                case ArenaAttack.GroundRift: return 9;
                case ArenaAttack.SwordFall: return 16;
                case ArenaAttack.ThunderOrb: return 9;
                default: return 10;
            }
        }
    }
}
