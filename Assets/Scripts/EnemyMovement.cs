using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class EnemyMovement : MonoBehaviour
{
    public bool IsOnGroundForAudio => IsGrounded();
    public enum EnemyState
    {
        Chase = 0,
        Telegraph = 1,
        Attack = 2,
        Recover = 3,
        Defend = 4,
        Hitstun = 5,
        Showcase = 6,
        Reposition = 7,
        PlatformNavigate = 8,
        OffstageRecovery = 9,
        ThreatReact = 10,
        WhiffPunish = 11
    }

    private enum TacticalAction
    {
        None,
        Attack,
        Defend,
        Evade,
        Reposition,
        Uppercut,
        WhiffPunish
    }

    private static readonly int IsMovingHash =
        Animator.StringToHash("IsMoving");

    private static readonly int IsWalkingHash =
        Animator.StringToHash("IsWalking");

    private static readonly int JumpTriggerHash =
        Animator.StringToHash("Jump");

    private static readonly int IsGroundedHash =
        Animator.StringToHash("IsGrounded");

    private static readonly int IsDefendingHash =
        Animator.StringToHash("IsDefending");

    private static readonly int YesTriggerHash =
        Animator.StringToHash("Yes");

    private static readonly int NoTriggerHash =
        Animator.StringToHash("No");

    private const int GroundLayerMask = 1 << 0;
    private const float NavigationArrivalDistance = 0.12f;
    private const float NavigationProgressEpsilon = 0.04f;

    public float moveSpeed = 4.3f;
    public float walkSpeed = 2.6f;
    public float nearWalkDistance = 4f;
    public float stopDistance = 1.3f;

    [Min(0.1f)]
    public float tooCloseDistance = 0.95f;

    [Min(0.05f)]
    public float closeSpacingDuration = 0.18f;

    [Min(0f)]
    public float closeSpacingCooldown = 0.75f;

    public float jumpForce = 16.5f;
    public float jumpCooldown = 2f;

    [Header("Positioning")]
    [Min(0.1f)]
    public float repositionSpeed = 3.2f;

    [Min(0.05f)]
    public float repositionDuration = 0.28f;

    [Min(0.1f)]
    public float edgeProbeForwardDistance = 0.8f;

    [Min(0.2f)]
    public float edgeProbeDepth = 1.6f;

    [Header("Platform Navigation")]
    [Min(0.1f)]
    public float verticalNavigationThreshold = 1.25f;

    [Min(0.1f)]
    public float platformJumpAlignment = 1.35f;

    [Min(1f)]
    public float maximumReachablePlatformHeight = 7f;

    [Min(0.1f)]
    public float platformJumpForce = 17.4f;

    [Min(0f)]
    public float platformJumpCooldown = 0.45f;

    [Min(0.2f)]
    public float platformNavigationTimeout = 2.6f;

    [Min(0f)]
    public float platformLandingInset = 0.7f;

    [Min(0.1f)]
    public float platformAirControlAcceleration = 16f;

    [Min(0.1f)]
    public float navigationProgressTimeout = 0.6f;

    [Min(0f)]
    public float failedPlatformRetryDelay = 1.5f;

    [Min(0.1f)]
    public float dropThroughSpeed = 4.5f;

    [Min(0.1f)]
    public float dropThroughTimeout = 0.7f;

    [Header("Offstage Recovery")]
    public float arenaCenterX = -2f;

    [Min(2f)]
    public float mainStageHalfWidth = 16f;

    [Min(0.1f)]
    public float recoveryTargetInset = 2.5f;

    public float offstageRecoveryHeight = -0.15f;

    [Min(0.1f)]
    public float offstageRecoverySpeed = 6.2f;

    [Min(0.1f)]
    public float offstageRecoveryVerticalSpeed = 16f;

    [Min(0.05f)]
    public float recoveryApexVelocity = 0.65f;

    [Min(0.1f)]
    public float recoveryEmergencyFallSpeed = 3f;

    [Min(0.1f)]
    public float recoveryMaximumTargetHeight = 6f;

    [Min(0.1f)]
    public float recoveryHorizontalAcceleration = 20f;

    [Header("Threat Reaction")]
    [Min(0f)]
    public float minimumThreatReactionTime = 0.12f;

    [Min(0f)]
    public float maximumThreatReactionTime = 0.22f;

    [Min(0.1f)]
    public float threatAwarenessDistance = 2.7f;

    [Range(0f, 1f)]
    public float counterAttackChance = 0.18f;

    [Header("Combat Decision")]
    [Min(0f)]
    public float minimumReactionTime = 0.12f;

    [Min(0f)]
    public float maximumReactionTime = 0.26f;

    [Range(0f, 1f)]
    public float attackCommitChance = 0.84f;

    [Min(0.1f)]
    public float attackVerticalTolerance = 0.9f;

    [Header("Uppercut Decision")]
    [Min(0f)]
    public float uppercutMinimumHeight = 0.55f;

    [Min(0.1f)]
    public float uppercutMaximumHeight = 2.2f;

    [Min(0.1f)]
    public float uppercutHorizontalRange = 1.45f;

    [Range(0f, 1f)]
    public float uppercutCommitChance = 1f;

    [Min(0f)]
    public float uppercutCooldown = 1.1f;

    [Header("Whiff Punish")]
    [Range(0f, 1f)]
    public float whiffPunishChance = 0.7f;

    [Min(0f)]
    public float minimumWhiffPunishDelay = 0.1f;

    [Min(0f)]
    public float maximumWhiffPunishDelay = 0.18f;

    [Min(0.1f)]
    public float whiffPunishAwarenessRange = 2.5f;

    [Min(0f)]
    public float whiffPunishCooldown = 1.1f;

    [Header("Ledge Pressure")]
    [Min(0.1f)]
    public float ledgeSafeInset = 0.8f;

    [Min(0.05f)]
    public float ledgeApproachTolerance = 0.18f;

    [Range(0f, 35f)]
    public float ledgeStrikeVisualTilt = 20f;

    [Min(0.1f)]
    public float ledgeStrikePoseBlendSpeed = 12f;

    [Min(0f)]
    public float ledgeStrikeRetryCooldown = 1.1f;

    [Range(0f, 1f)]
    public float continueAfterFirstHitChance = 0.88f;

    [Range(0f, 1f)]
    public float continueIntoFinisherChance = 0.68f;

    [Range(0f, 0.5f)]
    public float highDamageComboBonus = 0.12f;

    [Header("Behaviour Memory")]
    [Min(0f)]
    public float behaviourMemoryDuration = 2.6f;

    [Range(0f, 1f)]
    public float repeatedActionChanceMultiplier = 0.45f;

    [Range(0f, 1f)]
    public float evadeChance = 0.22f;

    [Min(0.1f)]
    public float evadeTriggerDistance = 2.5f;

    [Min(0f)]
    public float evadeCooldown = 1.8f;

    [Header("Defence Decision")]
    [Range(0f, 1f)]
    public float defendChance = 0.32f;

    [Min(0.1f)]
    public float defendTriggerDistance = 2.15f;

    [Min(0.05f)]
    public float defendDuration = 0.42f;

    [Min(0f)]
    public float defendCooldown = 2.4f;

    [Min(0f)]
    public float defendReleaseAfterBlock = 0.1f;

    [Header("Jump Feel")]
    [Min(1f)]
    public float riseGravityMultiplier = 2.2f;

    [Min(1f)]
    public float fallGravityMultiplier = 5.8f;

    [Header("Animation Showcase")]
    [Min(0f)]
    public float showcaseActionDuration = 1.4f;

    [SerializeField]
    private EnemyState currentState = EnemyState.Chase;

    public EnemyState CurrentState => currentState;
    public bool IsDefending => currentState == EnemyState.Defend;
    public float FacingDirection => facingDirection;

    private Transform player;
    private Rigidbody rb;
    private EnemyDamage enemyDamage;
    private EnemyAttack enemyAttack;
    private BattleAttack playerAttack;
    private BattlePlayerController playerController;
    private PlayerDamage playerDamage;
    private BattleGuardVfx guardVfx;
    private Animator animator;
    private Transform visualRoot;
    private bool hasJumpTrigger;
    private bool hasGroundedParameter;
    private bool hasWalkingParameter;
    private bool hasDefendingParameter;
    private bool hasYesTrigger;
    private bool hasNoTrigger;
    private bool wasGrounded;
    private float facingDirection = -1f;
    private float nextJumpTime;
    private float stateTimer;
    private float defendCooldownTimer;
    private float evadeCooldownTimer;
    private float closeSpacingCooldownTimer;
    private float uppercutCooldownTimer;
    private float whiffPunishCooldownTimer;
    private float ledgeStrikeRetryCooldownTimer;
    private float decisionTimer;
    private bool wasPlayerAttacking;
    private bool targetWasAttackable;
    private Collider[] fighterColliders;
    private Collider[] oneWayPlatformColliders;
    private Collider activeDropPlatform;
    private Collider[] activeDropPlatformColliders;
    private float repositionDirection;
    private float navigationTargetX;
    private float navigationTargetY;
    private Collider navigationTargetPlatform;
    private bool hasNavigationTarget;
    private bool navigationJumpCommitted;
    private bool navigationWantsDrop;
    private float navigationBestDistance = float.PositiveInfinity;
    private float navigationNoProgressTimer;
    private readonly Dictionary<Collider, float>
        failedNavigationPlatforms = new Dictionary<Collider, float>();
    private float nextNavigationReplanTime;
    private bool isDroppingThroughPlatform;
    private bool recoveryBoostUsed;
    private Collider recoveryTargetPlatform;
    private float recoveryTargetX;
    private float recoveryTargetY;
    private Collider mainStageCollider;
    private bool ledgePressureAttack;
    private float ledgeStrikePoseBlend;
    private TacticalAction lastTacticalAction = TacticalAction.None;
    private float lastTacticalActionTime = float.NegativeInfinity;

    void Start()
    {
        GameObject playerObject =
            GameObject.FindGameObjectWithTag("Player");

        if (playerObject != null)
        {
            player = playerObject.transform;
            playerAttack = playerObject.GetComponent<BattleAttack>();
            playerController =
                playerObject.GetComponent<BattlePlayerController>();
            playerDamage = playerObject.GetComponent<PlayerDamage>();
        }

        rb = GetComponent<Rigidbody>();
        enemyDamage = GetComponent<EnemyDamage>();
        enemyAttack = GetComponent<EnemyAttack>();
        fighterColliders = GetComponentsInChildren<Collider>(true);
        CacheOneWayPlatforms();
        CacheMainStageBounds();
        guardVfx = GetComponent<BattleGuardVfx>();
        if (guardVfx == null)
        {
            guardVfx = gameObject.AddComponent<BattleGuardVfx>();
        }
        animator = GetComponentInChildren<Animator>();

        if (rb == null || enemyAttack == null)
        {
            Debug.LogError(
                "EnemyMovement requires Rigidbody and EnemyAttack."
            );
            enabled = false;
            return;
        }

        ConfigureAnimator();

        if (player != null)
        {
            FacePlayer();
        }

        wasGrounded = IsGrounded();
        SetMovingAnimation(false);
        SetWalkingAnimation(false);
        SetGroundedAnimation(wasGrounded);
        guardVfx.SetGuarding(false, facingDirection);
        EnterState(EnemyState.Chase);
    }

    void Update()
    {
        if (Time.timeScale <= 0f)
            return;

        if (defendCooldownTimer > 0f)
        {
            defendCooldownTimer -= Time.deltaTime;
        }

        if (evadeCooldownTimer > 0f)
        {
            evadeCooldownTimer -= Time.deltaTime;
        }

        if (closeSpacingCooldownTimer > 0f)
        {
            closeSpacingCooldownTimer -= Time.deltaTime;
        }

        if (uppercutCooldownTimer > 0f)
        {
            uppercutCooldownTimer -= Time.deltaTime;
        }

        if (whiffPunishCooldownTimer > 0f)
        {
            whiffPunishCooldownTimer -= Time.deltaTime;
        }

        if (ledgeStrikeRetryCooldownTimer > 0f)
        {
            ledgeStrikeRetryCooldownTimer -= Time.deltaTime;
        }

        if (decisionTimer > 0f)
        {
            decisionTimer -= Time.deltaTime;
        }

        bool playerIsAttacking =
            playerAttack != null && playerAttack.IsMovementLocked;
        bool playerStartedAttack =
            playerIsAttacking && !wasPlayerAttacking;
        bool playerFinishedAttack =
            !playerIsAttacking && wasPlayerAttacking;
        wasPlayerAttacking = playerIsAttacking;

        if (enemyDamage != null && enemyDamage.IsInHitstun)
        {
            if (currentState != EnemyState.Hitstun)
            {
                EnterState(EnemyState.Hitstun);
            }

            return;
        }

        if (currentState == EnemyState.Hitstun)
        {
            EnterState(
                ShouldStartOffstageRecovery()
                    ? EnemyState.OffstageRecovery
                    : EnemyState.Chase
            );
        }

        if (currentState != EnemyState.OffstageRecovery &&
            ShouldStartOffstageRecovery())
        {
            EnterState(EnemyState.OffstageRecovery);
        }

        if (currentState == EnemyState.OffstageRecovery)
        {
            if (IsGrounded())
            {
                EnterState(EnemyState.Chase);
            }

            return;
        }

        if (currentState == EnemyState.Showcase)
        {
            if (TickStateTimer())
            {
                EnterState(EnemyState.Chase);
            }

            return;
        }

        if (TryStartShowcaseAction())
            return;

        if (player == null)
        {
            SetMovingAnimation(false);
            SetWalkingAnimation(false);
            return;
        }

        if (playerFinishedAttack && CanStartWhiffPunish())
        {
            EnterState(EnemyState.WhiffPunish);
            return;
        }

        TickVisibleBeamReaction();

        switch (currentState)
        {
            case EnemyState.Chase:
                if (NeedsPlatformNavigation())
                {
                    EnterState(EnemyState.PlatformNavigate);
                    break;
                }

                if (playerStartedAttack && CanNoticePlayerAttack())
                {
                    EnterState(EnemyState.ThreatReact);
                    break;
                }

                if (ShouldCreateSpace())
                {
                    BeginCloseSpacing();
                    break;
                }

                bool canStartUppercut = CanStartUppercut();
                bool canStartAttack = CanStartAttack();
                if (!canStartAttack && !canStartUppercut)
                {
                    targetWasAttackable = false;
                    break;
                }

                if (!targetWasAttackable)
                {
                    targetWasAttackable = true;
                    ScheduleCombatDecision();
                    break;
                }

                if (decisionTimer > 0f)
                    break;

                targetWasAttackable = false;
                float attackChance = GetRememberedActionWeight(
                    TacticalAction.Attack,
                    attackCommitChance
                );

                if (WasRecentTacticalAction(TacticalAction.Reposition))
                {
                    attackChance = Mathf.Lerp(
                        attackChance,
                        1f,
                        0.18f
                    );
                }

                bool uppercutAvailable = CanStartUppercut();
                bool regularAttackAvailable = CanStartAttack();
                bool ledgeAttackAvailable = regularAttackAvailable &&
                    IsStandingOnHangingPlatform();
                bool commitsToUppercut = uppercutAvailable &&
                    Random.value <= Mathf.Clamp01(uppercutCommitChance);

                if (commitsToUppercut)
                {
                    enemyAttack.StartUppercut();
                    uppercutCooldownTimer = Mathf.Max(
                        uppercutCooldownTimer,
                        uppercutCooldown
                    );
                    ledgePressureAttack = false;
                    RegisterTacticalAction(TacticalAction.Uppercut);
                    EnterState(EnemyState.Telegraph);
                }
                else if (regularAttackAvailable &&
                    (ledgeAttackAvailable ||
                        Random.value <= Mathf.Clamp01(attackChance)))
                {
                    BeginRegularAttack();
                    RegisterTacticalAction(TacticalAction.Attack);
                    EnterState(EnemyState.Telegraph);
                }
                else
                {
                    RegisterTacticalAction(TacticalAction.Reposition);
                    EnterState(EnemyState.Reposition);
                }
                break;

            case EnemyState.Reposition:
                if (playerStartedAttack && CanNoticePlayerAttack())
                {
                    EnterState(EnemyState.ThreatReact);
                    break;
                }

                if (TickStateTimer())
                {
                    EnterState(
                        NeedsPlatformNavigation()
                            ? EnemyState.PlatformNavigate
                            : EnemyState.Chase
                    );
                }
                break;

            case EnemyState.PlatformNavigate:
                bool navigationIsGrounded = IsGrounded();
                if (navigationIsGrounded &&
                    !isDroppingThroughPlatform &&
                    !NeedsPlatformNavigation())
                {
                    EnterState(EnemyState.Chase);
                    break;
                }

                if (navigationIsGrounded && TickStateTimer())
                {
                    ReplanNavigationAfterFailure();
                    stateTimer = Mathf.Max(stateTimer, Mathf.Max(
                        0.2f,
                        platformNavigationTimeout
                    ));
                }
                break;

            case EnemyState.Telegraph:
                if (TickStateTimer())
                {
                    enemyAttack.ResolveAttack(facingDirection);
                    EnterState(EnemyState.Attack);
                }
                break;

            case EnemyState.Attack:
                if (TickStateTimer())
                {
                    EnterState(EnemyState.Recover);
                }
                break;

            case EnemyState.Recover:
                if (TickStateTimer())
                {
                    bool attackConnected =
                        enemyAttack.LastAttackConnected;

                    if (ledgePressureAttack && !attackConnected)
                    {
                        ledgeStrikeRetryCooldownTimer = Mathf.Max(
                            ledgeStrikeRetryCooldownTimer,
                            ledgeStrikeRetryCooldown
                        );
                    }

                    if (ShouldContinueCombo() &&
                        enemyAttack.TryAdvanceCombo(player))
                    {
                        EnterState(EnemyState.Telegraph);
                    }
                    else
                    {
                        if (!attackConnected)
                        {
                            RegisterTacticalAction(
                                TacticalAction.Reposition
                            );
                        }

                        EnterState(
                            NeedsPlatformNavigation()
                                ? EnemyState.PlatformNavigate
                                : EnemyState.Reposition
                        );
                    }
                }
                break;

            case EnemyState.ThreatReact:
                if (!playerIsAttacking || !CanNoticePlayerAttack())
                {
                    EnterState(EnemyState.Chase);
                }
                else if (TickStateTimer())
                {
                    ResolveThreatReaction();
                }
                break;

            case EnemyState.WhiffPunish:
                if (playerIsAttacking)
                {
                    EnterState(
                        CanNoticePlayerAttack()
                            ? EnemyState.ThreatReact
                            : EnemyState.Chase
                    );
                }
                else if (TickStateTimer())
                {
                    if (CanStartUppercut())
                    {
                        enemyAttack.StartUppercut();
                        uppercutCooldownTimer = Mathf.Max(
                            uppercutCooldownTimer,
                            uppercutCooldown
                        );
                        ledgePressureAttack = false;
                        RegisterTacticalAction(
                            TacticalAction.WhiffPunish
                        );
                        EnterState(EnemyState.Telegraph);
                    }
                    else if (CanStartAttack())
                    {
                        BeginRegularAttack();
                        RegisterTacticalAction(
                            TacticalAction.WhiffPunish
                        );
                        EnterState(EnemyState.Telegraph);
                    }
                    else
                    {
                        EnterState(EnemyState.Chase);
                    }
                }
                break;

            case EnemyState.Defend:
                if (TickStateTimer())
                {
                    EnterState(EnemyState.Chase);
                }
                break;
        }
    }

    void LateUpdate()
    {
        float targetBlend = ledgePressureAttack &&
            (currentState == EnemyState.Telegraph ||
                currentState == EnemyState.Attack)
                ? 1f
                : 0f;
        ledgeStrikePoseBlend = Mathf.MoveTowards(
            ledgeStrikePoseBlend,
            targetBlend,
            Mathf.Max(0.1f, ledgeStrikePoseBlendSpeed) *
                Time.deltaTime
        );
        ApplyVisualFacingRotation();
    }

    void FixedUpdate()
    {
        bool isGrounded = IsGrounded();
        bool landedThisFrame = isGrounded && !wasGrounded;

        if (landedThisFrame)
        {
            nextJumpTime = Time.time + Mathf.Min(
                jumpCooldown,
                platformJumpCooldown
            );

            if (currentState == EnemyState.PlatformNavigate)
            {
                stateTimer = Mathf.Max(
                    stateTimer,
                    platformNavigationTimeout
                );
            }
        }

        wasGrounded = isGrounded;
        SetGroundedAnimation(isGrounded);
        ApplyJumpGravity();

        if ((enemyDamage != null && enemyDamage.IsInHitstun) ||
            currentState == EnemyState.Hitstun)
        {
            SetMovingAnimation(false);
            SetWalkingAnimation(false);
            return;
        }

        if (currentState == EnemyState.OffstageRecovery)
        {
            RecoverToStage();
            SetMovingAnimation(false);
            SetWalkingAnimation(false);
            return;
        }

        if (player == null)
        {
            SetMovingAnimation(false);
            SetWalkingAnimation(false);
            return;
        }

        switch (currentState)
        {
            case EnemyState.Chase:
                ChasePlayer(isGrounded);
                break;

            case EnemyState.Reposition:
                RepositionPlayer(isGrounded);
                break;

            case EnemyState.PlatformNavigate:
                NavigatePlatforms(isGrounded, landedThisFrame);
                break;

            case EnemyState.Telegraph:
            case EnemyState.Attack:
            case EnemyState.Recover:
                ApplyAttackMovement();
                SetMovingAnimation(false);
                SetWalkingAnimation(false);
                break;

            case EnemyState.ThreatReact:
            case EnemyState.WhiffPunish:
                StopHorizontalMovement();
                FacePlayer();
                SetMovingAnimation(false);
                SetWalkingAnimation(false);
                break;

            default:
                StopHorizontalMovement();
                SetMovingAnimation(false);
                SetWalkingAnimation(false);
                break;
        }
    }

    void ApplyJumpGravity()
    {
        BattlePlatformPhysics.ApplyExtraGravity(
            rb,
            riseGravityMultiplier,
            fallGravityMultiplier
        );
    }

    void ChasePlayer(bool isGrounded)
    {
        bool pressuringLedge = IsStandingOnHangingPlatform();
        bool waitingAfterMiss = pressuringLedge &&
            ledgeStrikeRetryCooldownTimer > 0f;
        float pursuitTargetX = pressuringLedge && !waitingAfterMiss
            ? GetLedgePressureX()
            : waitingAfterMiss
                ? transform.position.x
                : player.position.x;
        float horizontalOffset =
            pursuitTargetX - transform.position.x;

        float distance = Mathf.Abs(horizontalOffset);
        float direction = Mathf.Abs(horizontalOffset) > 0.01f
            ? horizontalOffset >= 0f ? 1f : -1f
            : player.position.x >= transform.position.x ? 1f : -1f;
        float desiredStopDistance = pressuringLedge
            ? ledgeApproachTolerance
            : stopDistance;
        bool isMoving = distance > desiredStopDistance;
        bool isWalking =
            isMoving && distance <= nearWalkDistance;

        Vector3 velocity = rb.linearVelocity;

        if (isMoving && isGrounded && !HasGroundAhead(direction))
        {
            StopHorizontalMovement();
            EnterState(EnemyState.PlatformNavigate);
            return;
        }

        float selectedMoveSpeed = isWalking ? walkSpeed : moveSpeed;

        velocity.x = isMoving ? direction * selectedMoveSpeed : 0f;
        velocity.z = 0f;
        rb.linearVelocity = velocity;

        if (isMoving)
        {
            SetFacing(direction);
        }
        else
        {
            FacePlayer();
        }
        SetMovingAnimation(isMoving && isGrounded);
        SetWalkingAnimation(isWalking && isGrounded);
    }

    void RepositionPlayer(bool isGrounded)
    {
        if (!isGrounded)
        {
            StopHorizontalMovement();
            return;
        }

        float direction = repositionDirection;
        if (Mathf.Abs(direction) < 0.01f)
        {
            direction = GetSafeRepositionDirection();
            repositionDirection = direction;
        }

        if (Mathf.Abs(direction) < 0.01f ||
            !HasGroundAhead(direction))
        {
            StopHorizontalMovement();
            return;
        }

        Vector3 velocity = rb.linearVelocity;
        velocity.x = direction * repositionSpeed;
        velocity.z = 0f;
        rb.linearVelocity = velocity;

        FacePlayer();
        SetMovingAnimation(true);
        SetWalkingAnimation(true);
    }

    void EnterState(EnemyState nextState)
    {
        if ((currentState == EnemyState.Showcase ||
                currentState == EnemyState.Defend) &&
            nextState != currentState)
        {
            SetDefendingAnimation(false);
            guardVfx?.SetGuarding(false, facingDirection);
        }

        if (currentState == EnemyState.PlatformNavigate &&
            nextState != EnemyState.PlatformNavigate)
        {
            ClearNavigationTarget();
        }

        if (currentState == EnemyState.OffstageRecovery &&
            nextState != EnemyState.OffstageRecovery)
        {
            ClearRecoveryTarget();
        }

        if (nextState != EnemyState.Telegraph &&
            nextState != EnemyState.Attack &&
            nextState != EnemyState.Recover)
        {
            ledgePressureAttack = false;
        }

        targetWasAttackable = false;

        currentState = nextState;
        SetMovingAnimation(false);
        SetWalkingAnimation(false);

        switch (nextState)
        {
            case EnemyState.Chase:
                decisionTimer = 0f;
                stateTimer = 0f;
                break;

            case EnemyState.Reposition:
                enemyAttack.CancelCombo();
                repositionDirection = GetSafeRepositionDirection();
                if (player != null)
                {
                    FacePlayer();
                }
                stateTimer = Mathf.Max(0.05f, repositionDuration);
                break;

            case EnemyState.PlatformNavigate:
                enemyAttack.CancelCombo();
                ClearNavigationTarget();
                SelectNavigationTarget();
                stateTimer = Mathf.Max(stateTimer, Mathf.Max(
                    0.2f,
                    platformNavigationTimeout
                ));
                break;

            case EnemyState.Telegraph:
                if (ledgePressureAttack &&
                    playerController != null &&
                    playerController.IsLedgeHanging)
                {
                    SetFacing(playerController.HangingLedgeSide);
                }
                else
                {
                    FacePlayer();
                }
                stateTimer = enemyAttack.CurrentStartupDuration;
                enemyAttack.BeginAttackAnimation(facingDirection);
                break;

            case EnemyState.Attack:
                stateTimer = enemyAttack.CurrentActiveDuration;
                break;

            case EnemyState.Recover:
                stateTimer = enemyAttack.CurrentRecoveryDuration;
                break;

            case EnemyState.Defend:
                enemyAttack.CancelCombo();
                FacePlayer();
                StopHorizontalMovement();
                SetDefendingAnimation(true);
                SideAAudio.GuardRaise();
                guardVfx?.SetGuarding(true, facingDirection);
                stateTimer = Mathf.Max(0.05f, defendDuration);
                defendCooldownTimer = Mathf.Max(
                    defendCooldownTimer,
                    defendCooldown
                );
                break;

            case EnemyState.Hitstun:
                enemyAttack.CancelCombo();
                stateTimer = 0f;
                break;

            case EnemyState.OffstageRecovery:
                enemyAttack.CancelCombo();
                navigationWantsDrop = false;
                recoveryBoostUsed = false;
                SelectRecoveryTarget();
                stateTimer = 0f;
                break;

            case EnemyState.ThreatReact:
                enemyAttack.CancelCombo();
                FacePlayer();
                StopHorizontalMovement();
                ScheduleThreatReaction();
                break;

            case EnemyState.WhiffPunish:
                enemyAttack.CancelCombo();
                FacePlayer();
                StopHorizontalMovement();
                ScheduleWhiffPunish();
                whiffPunishCooldownTimer = Mathf.Max(
                    whiffPunishCooldownTimer,
                    whiffPunishCooldown
                );
                break;

            case EnemyState.Showcase:
                stateTimer = showcaseActionDuration;
                break;

            default:
                stateTimer = 0f;
                break;
        }
    }

    bool TickStateTimer()
    {
        stateTimer -= Time.deltaTime;
        return stateTimer <= 0f;
    }

    public void ResetForStockRespawn() { EnterState(EnemyState.Chase); }

    void OnDisable()
    {
        RestorePlatformCollision();
        ledgeStrikePoseBlend = 0f;
        ApplyVisualFacingRotation();
        SetDefendingAnimation(false);
        guardVfx?.SetGuarding(false, facingDirection);
    }
}
