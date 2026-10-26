using UnityEngine;

public readonly struct BattleComboHit
{
    public BattleComboHit(
        bool usesWeapon,
        float damage,
        float knockbackMultiplier,
        float hitstunDuration,
        float startupDuration,
        float activeDuration,
        float recoveryDuration,
        float animationSpeed,
        float forwardSpeed
    )
    {
        UsesWeapon = usesWeapon;
        Damage = damage;
        KnockbackMultiplier = knockbackMultiplier;
        HitstunDuration = hitstunDuration;
        StartupDuration = startupDuration;
        ActiveDuration = activeDuration;
        RecoveryDuration = recoveryDuration;
        AnimationSpeed = animationSpeed;
        ForwardSpeed = forwardSpeed;
    }

    public bool UsesWeapon { get; }
    public float Damage { get; }
    public float KnockbackMultiplier { get; }
    public float HitstunDuration { get; }
    public float StartupDuration { get; }
    public float ActiveDuration { get; }
    public float RecoveryDuration { get; }
    public float AnimationSpeed { get; }
    public float ForwardSpeed { get; }
}

public static class BattleFourHitCombo
{
    public const int HitCount = 3;
    public const float PlayerInputWindow = 0.5f;
    public const float RangeBonus = 0.3f;
    public const float FinisherRangeBonus = 0.25f;
    public const float HorizontalKnockbackWeight = 0.68f;
    public const float StandaloneVerticalKnockback = 0.82f;
    public const float UppercutDamage = 7f;
    public const float UppercutKnockbackMultiplier = 0.96f;
    public const float UppercutHitstunDuration = 0.48f;
    public const float UppercutHorizontalKnockbackWeight = 0.28f;
    public const float UppercutVerticalKnockback = 1.42f;

    private static readonly BattleComboHit[] Hits =
    {
        new BattleComboHit(
            false,
            3f,
            0.46f,
            0.46f,
            0.11f,
            0.07f,
            0.15f,
            1.03f,
            0.65f
        ),
        new BattleComboHit(
            true,
            4f,
            0.52f,
            0.48f,
            0.13f,
            0.07f,
            0.16f,
            0.97f,
            0.75f
        ),
        new BattleComboHit(
            false,
            6f,
            0.86f,
            0.42f,
            0.19f,
            0.09f,
            0.37f,
            0.82f,
            0.45f
        )
    };

    public static BattleComboHit GetHit(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0)
            return Hits[0];

        if (zeroBasedIndex >= Hits.Length)
            return Hits[Hits.Length - 1];

        return Hits[zeroBasedIndex];
    }

    public static float GetVerticalKnockback(int zeroBasedIndex)
    {
        switch (Mathf.Clamp(zeroBasedIndex, 0, HitCount - 1))
        {
            case 0:
                return 0.58f;
            case 1:
                return 0.70f;
            default:
                return 0.92f;
        }
    }

    public static float GetRangeBonus(int zeroBasedIndex)
    {
        return zeroBasedIndex == HitCount - 1
            ? FinisherRangeBonus
            : 0f;
    }

    public static float GetHitAnimationSpeed(int zeroBasedIndex)
    {
        switch (Mathf.Clamp(zeroBasedIndex, 0, HitCount - 1))
        {
            case 0:
                return 1.15f;
            case 1:
                return 1.02f;
            default:
                return 0.82f;
        }
    }

    public static float GetMovementControl(int zeroBasedIndex)
    {
        switch (Mathf.Clamp(zeroBasedIndex, 0, HitCount - 1))
        {
            case 0:
                return 0.58f;
            case 1:
                return 0.50f;
            default:
                return 0.25f;
        }
    }
}
