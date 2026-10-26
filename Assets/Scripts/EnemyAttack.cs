using UnityEngine;

public class EnemyAttack : MonoBehaviour
{
    private const float LedgeStrikeContactRadius = 0.28f;
    private const float LedgeStrikeMinimumForwardReach = 0.05f;
    private const float LedgeStrikeMaximumForwardReach = 1.75f;
    private const float LedgeStrikeMinimumVerticalReach = -3.2f;
    private const float LedgeStrikeMaximumVerticalReach = 0.4f;

    private static readonly int AttackTriggerHash =
        Animator.StringToHash("Attack");

    private static readonly int WeaponTriggerHash =
        Animator.StringToHash("Weapon");

    public float attackRange = 1.5f;

    [Header("Combo Pursuit")]
    [Tooltip("The target can move slightly during light hits without " +
        "immediately cancelling the rest of the combo.")]
    public float comboContinueRange = 1.8f;

    [Header("Uppercut")]
    [Min(0.1f)]
    public float uppercutRange = 2.35f;

    [Min(0.1f)]
    public float uppercutHorizontalTolerance = 1.45f;

    public float CurrentStartupDuration => CurrentHit.StartupDuration;
    public float CurrentActiveDuration => CurrentHit.ActiveDuration;
    public float CurrentRecoveryDuration => CurrentHit.RecoveryDuration;
    public float CurrentForwardSpeed => comboStep >= 0
        ? currentAttackIsUppercut
            ? 0f
            : CurrentHit.ForwardSpeed
        : 0f;
    public int CurrentComboStep => comboStep;
    public bool IsUppercutAttack => currentAttackIsUppercut;
    public bool LastAttackConnected { get; private set; }

    private Animator animator;
    private EnemyDamage enemyDamage;
    private BattleAttackSweep attackSweep;
    private bool hasAttackTrigger;
    private bool hasWeaponTrigger;
    private bool currentAttackUsesWeapon;
    private bool currentAttackIsUppercut;
    private float currentAttackRangeBonus;
    private BattlePlayerController ledgeAttackTarget;
    private bool ledgeStrikePrepared;
    private Vector3 ledgeStrikeOrigin;
    private Vector3 ledgeStrikeContact;
    private float ledgeStrikeDirection = 1f;
    private Collider ledgeStrikePlatform;
    private int comboStep = -1;

    private BattleComboHit CurrentHit =>
        BattleFourHitCombo.GetHit(comboStep);

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        enemyDamage = GetComponent<EnemyDamage>();
        attackSweep = GetComponent<BattleAttackSweep>();
        if (attackSweep == null)
        {
            attackSweep = gameObject.AddComponent<BattleAttackSweep>();
        }
        attackSweep.Initialise(true);
        hasAttackTrigger = BattleAnimatorUtility.HasParameter(
            animator,
            AttackTriggerHash,
            AnimatorControllerParameterType.Trigger
        );
        hasWeaponTrigger = BattleAnimatorUtility.HasParameter(
            animator,
            WeaponTriggerHash,
            AnimatorControllerParameterType.Trigger
        );
    }

    public bool IsTargetInRange(
        Transform target,
        float rangeBonus = 0f
    )
    {
        if (target == null)
            return false;

        Vector3 offset = target.position - transform.position;
        offset.z = 0f;

        float range = attackRange + Mathf.Max(0f, rangeBonus);
        return offset.sqrMagnitude <= range * range;
    }

    public void StartCombo(float rangeBonus = 0f)
    {
        comboStep = 0;
        currentAttackIsUppercut = false;
        currentAttackRangeBonus = Mathf.Max(0f, rangeBonus);
        ledgeAttackTarget = null;
        ledgeStrikePrepared = false;
        ledgeStrikePlatform = null;
        LastAttackConnected = false;
        RestoreAnimatorSpeed();
    }

    public void StartLedgeAttack(BattlePlayerController target)
    {
        StartCombo();
        ledgeAttackTarget = target;
    }

    public void StartUppercut()
    {
        comboStep = BattleFourHitCombo.HitCount - 1;
        currentAttackIsUppercut = true;
        currentAttackRangeBonus = 0f;
        ledgeAttackTarget = null;
        ledgeStrikePrepared = false;
        ledgeStrikePlatform = null;
        LastAttackConnected = false;
        RestoreAnimatorSpeed();
    }

    public bool TryAdvanceCombo(Transform target)
    {
        if (currentAttackIsUppercut ||
            comboStep < 0 ||
            comboStep >= BattleFourHitCombo.HitCount - 1 ||
            !IsTargetInComboRange(target))
        {
            CancelCombo();
            return false;
        }

        comboStep++;
        return true;
    }

    public void CancelCombo()
    {
        comboStep = -1;
        currentAttackIsUppercut = false;
        currentAttackRangeBonus = 0f;
        ledgeAttackTarget = null;
        ledgeStrikePrepared = false;
        ledgeStrikePlatform = null;
        attackSweep?.Stop();
        if (enemyDamage == null || !enemyDamage.IsInHitstun)
        {
            RestoreAnimatorSpeed();
        }
    }

    public void BeginAttackAnimation(float attackDirection)
    {
        if (comboStep < 0)
        {
            StartCombo();
        }

        BattleComboHit hit = CurrentHit;
        bool isLedgeStrikeAttempt = ledgeAttackTarget != null;
        ledgeStrikePrepared = TryPrepareLedgeStrike();
        currentAttackUsesWeapon = ledgeStrikePrepared
            ? hasWeaponTrigger
            : hit.UsesWeapon && hasWeaponTrigger;

        if (ledgeStrikePrepared)
        {
            attackSweep?.PlayLedgeStrike(
                ledgeStrikeDirection,
                hit.StartupDuration,
                hit.ActiveDuration,
                ledgeStrikeOrigin,
                ledgeStrikeContact
            );
        }
        else if (!isLedgeStrikeAttempt)
        {
            attackSweep?.Play(
                attackDirection,
                hit.StartupDuration + hit.ActiveDuration,
                hit.UsesWeapon,
                comboStep,
                currentAttackIsUppercut
            );
        }
        else
        {
            attackSweep?.Stop();
        }

        if (isLedgeStrikeAttempt && !ledgeStrikePrepared)
            return;

        if (currentAttackUsesWeapon) SideAAudio.Weapon();
        else SideAAudio.EnemyAttack();
        if (animator == null)
            return;

        animator.speed = Mathf.Max(0.05f, hit.AnimationSpeed);

        if (currentAttackUsesWeapon)
        {
            animator.SetTrigger(WeaponTriggerHash);
        }
        else if (hasAttackTrigger)
        {
            animator.SetTrigger(AttackTriggerHash);
        }
    }

    public void ResolveAttack(float attackDirection)
    {
        LastAttackConnected = false;
        float horizontalDirection =
            attackDirection >= 0f ? 1f : -1f;

        if (ledgeAttackTarget != null)
        {
            if (!TryResolveLedgeAttack())
            {
                Debug.Log("Enemy ledge strike missed.");
            }
            return;
        }

        Vector3 attackCenter = currentAttackIsUppercut
            ? transform.position + Vector3.up * 0.85f
            : transform.position;
        float resolvedRange = currentAttackIsUppercut
            ? uppercutRange
            : attackRange +
                BattleFourHitCombo.RangeBonus +
                BattleFourHitCombo.GetRangeBonus(comboStep) +
                currentAttackRangeBonus;
        bool foundTarget = currentAttackIsUppercut
            ? BattleMeleeQuery.TryFindUpperTarget(
                attackCenter,
                resolvedRange,
                "Player",
                transform,
                0.1f,
                uppercutHorizontalTolerance,
                out PlayerDamage player
            )
            : BattleMeleeQuery.TryFindForwardTarget(
                attackCenter,
                resolvedRange,
                "Player",
                transform,
                horizontalDirection,
                out player
            );

        if (foundTarget)
        {
            ResolveHit(player, horizontalDirection);
            return;
        }

        Debug.Log("Enemy attack missed.");
    }

    bool TryPrepareLedgeStrike()
    {
        if (ledgeAttackTarget == null ||
            !ledgeAttackTarget.IsLedgeHanging)
        {
            return false;
        }

        ledgeStrikeDirection =
            ledgeAttackTarget.HangingLedgeSide >= 0f ? 1f : -1f;
        ledgeStrikePlatform = ledgeAttackTarget.HangingLedgePlatform;
        if (ledgeStrikePlatform == null ||
            !ledgeAttackTarget.TryGetLedgeStrikeContactPoint(
                out ledgeStrikeContact
            ))
        {
            return false;
        }

        ledgeStrikeOrigin = transform.TransformPoint(
            new Vector3(
                0.28f * ledgeStrikeDirection,
                0.25f,
                0f
            )
        );
        return IsLedgeStrikeReachValid(
            ledgeStrikeOrigin,
            ledgeStrikeContact,
            ledgeStrikeDirection
        );
    }

    bool TryResolveLedgeAttack()
    {
        if (ledgeAttackTarget == null ||
            !ledgeAttackTarget.IsLedgeHanging ||
            !ledgeStrikePrepared ||
            ledgeAttackTarget.HangingLedgePlatform !=
                ledgeStrikePlatform)
        {
            return false;
        }

        PlayerDamage player =
            ledgeAttackTarget.GetComponent<PlayerDamage>();
        if (player == null)
            return false;

        float outwardDirection =
            ledgeAttackTarget.HangingLedgeSide >= 0f ? 1f : -1f;
        if (!IsLedgeStrikeReachValid(
                ledgeStrikeOrigin,
                ledgeStrikeContact,
                outwardDirection
            ))
        {
            return false;
        }

        attackSweep?.SnapLedgeContact(ledgeStrikeContact);
        if (!ledgeAttackTarget.IsLedgeStrikeContactVisible(
                ledgeStrikeContact,
                LedgeStrikeContactRadius
            ))
        {
            return false;
        }

        ResolveHit(
            player,
            outwardDirection,
            ledgeStrikeContact,
            true
        );
        return true;
    }

    static bool IsLedgeStrikeReachValid(
        Vector3 origin,
        Vector3 contact,
        float horizontalDirection
    )
    {
        float direction = horizontalDirection >= 0f ? 1f : -1f;
        Vector3 offset = contact - origin;
        float forwardReach = offset.x * direction;
        return forwardReach >= LedgeStrikeMinimumForwardReach &&
            forwardReach <= LedgeStrikeMaximumForwardReach &&
            offset.y >= LedgeStrikeMinimumVerticalReach &&
            offset.y <= LedgeStrikeMaximumVerticalReach;
    }

    void ResolveHit(
        PlayerDamage player,
        float horizontalDirection,
        Vector3? impactPositionOverride = null,
        bool isLedgeStrike = false
    )
    {
        LastAttackConnected = true;

        bool wasPerfectlyGuarded = player.TakeHit(
                horizontalDirection,
                currentAttackIsUppercut
                    ? BattleFourHitCombo.UppercutDamage
                    : CurrentHit.Damage,
                currentAttackIsUppercut
                    ? BattleFourHitCombo.UppercutKnockbackMultiplier
                    : CurrentHit.KnockbackMultiplier,
                currentAttackIsUppercut
                    ? BattleFourHitCombo.UppercutHitstunDuration
                    : CurrentHit.HitstunDuration,
                comboStep,
                currentAttackIsUppercut
                    ? BattleFourHitCombo.UppercutHorizontalKnockbackWeight
                    : isLedgeStrike
                        ? 0.46f
                        : null,
                currentAttackIsUppercut
                    ? BattleFourHitCombo.UppercutVerticalKnockback
                    : isLedgeStrike
                        ? -1.05f
                        : null,
                transform
        );

        if (wasPerfectlyGuarded)
        {
            CancelCombo();
            // Down K has its own local counter strike; don't add the old two-second L parry stun.
            Debug.Log("Enemy attack was counter guarded.");
            return;
        }

        Vector3 impactPosition = impactPositionOverride ??
            (currentAttackIsUppercut
                ? player.transform.position +
                    Vector3.up * 1.15f +
                    Vector3.right * horizontalDirection * 0.25f
                : Vector3.Lerp(
                    transform.position + Vector3.up * 0.85f,
                    player.transform.position + Vector3.up * 0.85f,
                    0.55f
                ));
        BattleCombatFeedback.PlayComboHit(
            impactPosition,
            horizontalDirection,
            comboStep,
            true,
            player.damagePercent,
            currentAttackIsUppercut
        );
        Debug.Log(
            isLedgeStrike
                ? "Enemy downward ledge strike connected."
                : currentAttackIsUppercut
                ? "Enemy uppercut connected."
                : "Enemy combo hit " + (comboStep + 1) +
                    " connected."
        );
    }

    bool IsTargetInComboRange(Transform target)
    {
        if (target == null)
            return false;

        Vector3 offset = target.position - transform.position;
        offset.z = 0f;
        float range = Mathf.Max(attackRange, comboContinueRange) +
            currentAttackRangeBonus;
        return offset.sqrMagnitude <= range * range;
    }

    void RestoreAnimatorSpeed()
    {
        if (animator != null)
        {
            animator.speed = 1f;
        }
    }

    void OnDisable()
    {
        attackSweep?.Stop();
        RestoreAnimatorSpeed();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            transform.position,
            Mathf.Max(
                uppercutRange,
                attackRange +
                    BattleFourHitCombo.RangeBonus +
                    BattleFourHitCombo.FinisherRangeBonus
            )
        );
    }
}
