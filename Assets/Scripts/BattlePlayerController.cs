using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class BattlePlayerController : MonoBehaviour
{
    private static readonly int IsMovingHash =
        Animator.StringToHash("IsMoving");

    private static readonly int JumpTriggerHash =
        Animator.StringToHash("Jump");

    private static readonly int IsGroundedHash =
        Animator.StringToHash("IsGrounded");

    private static readonly int IsDefendingHash =
        Animator.StringToHash("IsDefending");

    private static readonly int LedgePoseStateHash =
        Animator.StringToHash("Base Layer.CharacterArmature|Wave");

    private const int GroundLayerMask = 1 << 0;
    private const float LedgeCornerVerticalInset = 0.7f;
    private const float LedgeCornerHorizontalInset = 1.05f;

    public float moveSpeed = 7f;
    public float jumpForce = 13.5f;

    [Header("Air Steering")]
    [Range(.5f,1f)] public float airSpeedRatio = .9f;
    [Min(1f)] public float airAcceleration = 40f;
    [Min(1f)] public float airBraking = 22f;
    const float PostHitAirControlSeconds = .16f;
    float postHitAirControl;

    [Header("Jump Feel")]
    [Min(1f)]
    public float riseGravityMultiplier = 1.8f;

    [Min(1f)]
    public float fallGravityMultiplier = 3f;

    [Header("Perfect Guard")]
    [Min(0.05f)]
    public float perfectGuardWindow = 0.35f;

    [Min(0.05f)]
    public float perfectGuardActionDuration = 0.5f;

    [Header("Platform Drop")]
    [Min(0.1f)]
    public float dropThroughSpeed = 3f;

    [Min(0.1f)]
    public float dropThroughTimeout = 1f;

    [Header("Ledge Grab")]
    [Min(0.1f)]
    public float ledgeGrabHorizontalRange = 0.9f;

    [Min(0f)]
    public float ledgeGrabVerticalAbove = 0.4f;

    [Min(0.1f)]
    public float ledgeGrabVerticalBelow = 1.15f;

    [Min(0.1f)]
    public float ledgeHangHorizontalOffset = 0.72f;

    [Min(0f)]
    public float ledgeHangVerticalOffset = 1.35f;

    [Range(0f, 1f)]
    public float ledgePoseNormalisedTime = 0.35f;

    [Range(0f, 25f)]
    public float ledgeVisualTilt = 3f;

    [Range(0f, 0.1f)]
    public float ledgeHangSway = 0.015f;

    [Min(0f)]
    public float ledgeGrabCooldown = 0.55f;

    [Min(0f)]
    public float ledgeClimbHorizontalSpeed = 3.5f;

    [Min(0f)]
    public float ledgeClimbVerticalSpeed = 7f;

    public Transform attackPoint;

    private Rigidbody rb;
    private PlayerDamage playerDamage;
    private BattleAttack battleAttack;
    private BattleSpecialMoves specialMoves;
    private BattleGuardVfx guardVfx;
    private Animator animator;
    private Transform visualRoot;
    private bool hasJumpTrigger;
    private bool hasGroundedParameter;
    private bool hasDefendingParameter;
    private float moveInput;
    private bool jumpRequested;
    const float JumpBufferSeconds = .12f;
    float jumpPressUntil = -1;
    BattleCombatInput combatInput;
    private int remainingAirJumps = 1;
    private float perfectGuardTimer;
    private float perfectGuardActionTimer;
    private Collider[] fighterColliders;
    private Collider activeDropPlatform;
    private Collider[] activeDropPlatformColliders;
    private Collider contactedDropPlatform;
    private bool isDroppingThroughPlatform;
    private bool isLedgeHanging;
    private float ledgeGrabCooldownTimer;
    private float hangingLedgeX;
    private float hangingLedgeTopY;
    private float hangingLedgeSide;
    private Collider hangingLedgePlatform;
    private float hangingRootX;
    private float hangingRootY;
    private float ledgeHangTimer;
    private float animatorSpeedBeforeLedgeGrab = 1f;
    private bool gravityBeforeLedgeGrab = true;
    private bool isLedgeClimbing;
    private float climbTargetX, climbTopY, climbTimer;
    private readonly Collider[] climbObstacles = new Collider[24];

    public bool IsDefending { get; private set; }
    public bool IsLedgeHanging => isLedgeHanging;
    public bool IsLedgeClimbing => isLedgeClimbing;
    public bool IsOnGround => IsGrounded();
    // Accepted physics actions, not raw key presses. Observers can safely
    // baseline these monotonically increasing counters between lessons.
    public int JumpSerial { get; private set; }
    public int AirJumpSerial { get; private set; }
    public bool HasAirJump => remainingAirJumps > 0;
    public Collider HangingLedgePlatform =>
        isLedgeHanging ? hangingLedgePlatform : null;
    public float HangingLedgeX => hangingLedgeX;
    public float HangingLedgeTopY => hangingLedgeTopY;
    public float HangingLedgeSide => hangingLedgeSide;
    public bool IsDefenceActionLocked =>
        perfectGuardActionTimer > 0f;
    public float FacingDirection { get; private set; } = 1f;

    public bool TryGetLedgeStrikeContactPoint(
        out Vector3 contactPoint
    )
    {
        contactPoint = transform.position;
        if (!isLedgeHanging ||
            !TryGetVisibleBodyBounds(out Bounds visibleBounds))
        {
            return false;
        }

        Vector3 guidePoint = new Vector3(
            hangingLedgeX + hangingLedgeSide * 0.10f,
            hangingLedgeTopY - 0.78f,
            transform.position.z
        );
        contactPoint = visibleBounds.ClosestPoint(guidePoint);
        contactPoint.z = transform.position.z;
        return true;
    }

    public bool IsLedgeStrikeContactVisible(
        Vector3 contactPoint,
        float tolerance
    )
    {
        if (!isLedgeHanging ||
            !TryGetVisibleBodyBounds(out Bounds visibleBounds))
        {
            return false;
        }

        float safeTolerance = Mathf.Max(0f, tolerance);
        return visibleBounds.SqrDistance(contactPoint) <=
            safeTolerance * safeTolerance;
    }

    void Awake()
    {
        // Specials exist before any Start/Update ordering; the scene and prefab need no extra references.
        specialMoves = GetComponent<BattleSpecialMoves>();
        if (specialMoves == null) specialMoves = gameObject.AddComponent<BattleSpecialMoves>();
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerDamage = GetComponent<PlayerDamage>();
        battleAttack = GetComponent<BattleAttack>();
        guardVfx = GetComponent<BattleGuardVfx>();
        fighterColliders = GetComponentsInChildren<Collider>(true);

        if (guardVfx == null)
        {
            guardVfx = gameObject.AddComponent<BattleGuardVfx>();
        }

        animator = GetComponentInChildren<Animator>();

        if (animator != null)
        {
            animator.applyRootMotion = false;
            hasJumpTrigger = HasAnimatorParameter(
                JumpTriggerHash,
                AnimatorControllerParameterType.Trigger
            );
            hasGroundedParameter = HasAnimatorParameter(
                IsGroundedHash,
                AnimatorControllerParameterType.Bool
            );
            hasDefendingParameter = HasAnimatorParameter(
                IsDefendingHash,
                AnimatorControllerParameterType.Bool
            );
            visualRoot = animator.transform;

            while (visualRoot.parent != null &&
                visualRoot.parent != transform)
            {
                visualRoot = visualRoot.parent;
            }
        }

        SetFacing(1f);
        SetDefending(false);
        SetMovingAnimation(false);
        SetGroundedAnimation(IsGrounded());
    }

    void Update()
    {
        moveInput = 0f;

        if (Time.timeScale <= 0f)
        {
            jumpPressUntil = -1;
            jumpRequested = false;
            return;
        }

        if (ledgeGrabCooldownTimer > 0f)
        {
            ledgeGrabCooldownTimer -= Time.deltaTime;
        }

        if (playerDamage != null && playerDamage.IsInHitstun)
        {
            jumpPressUntil = -1;
            isLedgeClimbing = false;
            ReleaseLedge(
                rb != null ? rb.linearVelocity : Vector3.zero,
                false
            );
            CancelDefence();
            jumpRequested = false;
            SetMovingAnimation(false);
            SetGroundedAnimation(IsGrounded());
            return;
        }

        if (isLedgeClimbing)
        {
            jumpPressUntil = -1;
            jumpRequested = false;
            SetMovingAnimation(false);
            SetGroundedAnimation(false);
            return;
        }

        if (specialMoves != null && specialMoves.IsBusy)
        {
            jumpPressUntil = -1;
            jumpRequested = false;
            if (specialMoves.AllowLedgeGrab && TryGrabNearbyLedge())
            {
                specialMoves.OnLedgeGrabbed();
                return;
            }
            Keyboard specialKeyboard = Keyboard.current;
            if (specialKeyboard != null && specialKeyboard.aKey.isPressed != specialKeyboard.dKey.isPressed)
                moveInput = specialKeyboard.dKey.isPressed ? 1f : -1f;
            SetMovingAnimation(false);
            SetGroundedAnimation(IsGrounded());
            return;
        }

        if (!combatInput) combatInput = GetComponent<BattleCombatInput>();
        if (!combatInput || !combatInput.CanStartActions) jumpPressUntil = -1;
        else if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && !Keyboard.current.sKey.isPressed)
            jumpPressUntil = Time.time + JumpBufferSeconds;

        if (battleAttack != null && battleAttack.IsMovementLocked)
        {
            CancelDefence();
            jumpRequested = false;

            Keyboard keyboard = Keyboard.current;
            bool isGroundedDuringAttack = IsGrounded();
            bool canCancelIntoJump = keyboard != null &&
                jumpPressUntil >= Time.time &&
                !keyboard.sKey.isPressed &&
                (isGroundedDuringAttack || remainingAirJumps > 0);
            bool canCancelIntoDrop = keyboard != null &&
                keyboard.sKey.isPressed && keyboard.spaceKey.wasPressedThisFrame;
            bool cancelledForAction =
                (canCancelIntoJump ||
                    canCancelIntoDrop) &&
                battleAttack.TryCancelComboForAction();

            if (keyboard != null)
            {
                bool left = keyboard.aKey.isPressed, right = keyboard.dKey.isPressed;
                if (left != right) moveInput = right ? 1f : -1f;
            }

            if (Mathf.Abs(moveInput) > 0.01f &&
                battleAttack.UpdateAttackDirection(moveInput))
            {
                attackPoint.localPosition = new Vector3(
                    moveInput > 0f ? 1f : -1f,
                    0f,
                    0f
                );
                SetFacing(moveInput);
            }

            if (!cancelledForAction)
            {
                SetMovingAnimation(false);
                SetGroundedAnimation(isGroundedDuringAttack);
                return;
            }

            // Continue below so the newly pressed action is handled now.
        }

        if (Keyboard.current == null)
        {
            CancelDefence();
            jumpRequested = false;
            SetMovingAnimation(false);
            SetGroundedAnimation(IsGrounded());
            return;
        }

        if (isLedgeHanging)
        {
            jumpPressUntil = -1;
            HandleLedgeInput();
            return;
        }

        if (TryGrabNearbyLedge())
            return;

        bool isGrounded = IsGrounded();

        if (isGrounded)
        {
            remainingAirJumps = 1;
        }

        TickPerfectGuardAction();

        if (IsDefenceActionLocked)
        {
            jumpPressUntil = -1;
            jumpRequested = false;
            SetMovingAnimation(false);
            SetGroundedAnimation(isGrounded);
            return;
        }

        if (Keyboard.current.sKey.isPressed && Keyboard.current.spaceKey.wasPressedThisFrame &&
            TryStartPlatformDrop())
        {
            jumpPressUntil = -1;
            jumpRequested = false;
            SetMovingAnimation(false);
            SetGroundedAnimation(false);
            return;
        }

        bool movingLeft = Keyboard.current.aKey.isPressed;
        bool movingRight = Keyboard.current.dKey.isPressed;
        if (movingLeft != movingRight) moveInput = movingRight ? 1f : -1f;

        // 根据移动方向改变 AttackPoint
        if (moveInput > 0f)
        {
            attackPoint.localPosition = new Vector3(1f, 0f, 0f);
            SetFacing(1f);
        }
        else if (moveInput < 0f)
        {
            attackPoint.localPosition = new Vector3(-1f, 0f, 0f);
            SetFacing(-1f);
        }

        SetMovingAnimation(
            Mathf.Abs(moveInput) > 0.01f && isGrounded
        );
        SetGroundedAnimation(isGrounded);

        if (jumpPressUntil >= Time.time && !Keyboard.current.sKey.isPressed)
        {
            if (isGrounded)
            {
                jumpRequested = true;
                jumpPressUntil = -1;
                PlayJumpAnimation();
            }
            else if (remainingAirJumps > 0)
            {
                remainingAirJumps--;
                jumpRequested = true;
                jumpPressUntil = -1;
                PlayJumpAnimation();
            }
        }
    }

    void FixedUpdate()
    {
        if (isLedgeHanging)
        {
            jumpRequested = false;
            rb.linearVelocity = Vector3.zero;
            ledgeHangTimer += Time.fixedDeltaTime;

            float sway = Mathf.Sin(ledgeHangTimer * 3.2f) *
                ledgeHangSway;

            rb.MovePosition(new Vector3(
                hangingRootX,
                hangingRootY + sway,
                0f
            ));

            UpdateLedgeVisualPose(sway);
            return;
        }

        if (playerDamage != null && playerDamage.IsInHitstun)
        {
            jumpRequested = false;
            postHitAirControl = PostHitAirControlSeconds;
            ApplyJumpGravity();
            return;
        }

        if (isLedgeClimbing)
        {
            TickPhysicalLedgeClimb();
            ApplyJumpGravity();
            return;
        }

        if (specialMoves != null && specialMoves.IsBusy)
        {
            jumpRequested = false;
            specialMoves.ApplyMotion(rb, moveInput, moveSpeed);
            ApplyJumpGravity();
            return;
        }

        if (battleAttack != null && battleAttack.IsMovementLocked)
        {
            jumpRequested = false;

            Vector3 lockedVelocity = rb.linearVelocity;
            // A/D may add the existing small forward nudge, not full running
            // speed or a backwards slide mid-punch. Facing is sampled again
            // only when the next punch actually begins.
            float forwardInput = Mathf.Max(0f, moveInput * battleAttack.CommittedDirection);
            float controlledMovement = forwardInput * battleAttack.CommittedDirection * moveSpeed *
                battleAttack.AttackMovementControl;
            lockedVelocity.x = battleAttack.ConstrainComboAdvance(
                battleAttack.AttackMovementVelocity + controlledMovement,
                Time.fixedDeltaTime);
            lockedVelocity.z = 0f;
            rb.linearVelocity = lockedVelocity;

            ApplyJumpGravity();
            return;
        }

        if (IsDefenceActionLocked)
        {
            jumpRequested = false;

            Vector3 guardVelocity = rb.linearVelocity;
            guardVelocity.x = 0f;
            guardVelocity.z = 0f;
            rb.linearVelocity = guardVelocity;

            ApplyJumpGravity();
            return;
        }

        Vector3 velocity = rb.linearVelocity;

        bool grounded = IsGrounded();
        if (grounded)
        {
            // Immediate ground response is intentional: no start-up glide or
            // residual skating after releasing the direction.
            velocity.x = moveInput * moveSpeed;
            postHitAirControl = 0;
        }
        else
        {
            float control = Mathf.Lerp(.35f,1,1-postHitAirControl/PostHitAirControlSeconds);
            float target = moveInput * moveSpeed * airSpeedRatio;
            float rate = Mathf.Abs(moveInput)>.01f ? airAcceleration : airBraking;
            // Never overwrite a launch velocity with full running speed.
            velocity.x = Mathf.MoveTowards(velocity.x,target,rate*control*Time.fixedDeltaTime);
            postHitAirControl = Mathf.Max(0,postHitAirControl-Time.fixedDeltaTime);
        }
        velocity.z = 0f;

        rb.linearVelocity = velocity;

        if (jumpRequested)
        {
            JumpSerial++;
            if (remainingAirJumps == 0) AirJumpSerial++;
            GetComponent<BattleMotionPresentation>()?.Jumped(remainingAirJumps==0);
            rb.linearVelocity = new Vector3(
                rb.linearVelocity.x,
                jumpForce,
                0f
            );

            jumpRequested = false;
        }

        ApplyJumpGravity();
    }

    void ApplyJumpGravity()
    {
        BattlePlatformPhysics.ApplyExtraGravity(
            rb,
            riseGravityMultiplier,
            fallGravityMultiplier
        );
    }

    bool IsGrounded()
    {
        return BattlePlatformPhysics.IsGrounded(
            transform,
            rb,
            isDroppingThroughPlatform || isLedgeHanging || isLedgeClimbing,
            GroundLayerMask,
            1.1f
        );
    }

    bool TryGrabNearbyLedge()
    {
        if (ledgeGrabCooldownTimer > 0f ||
            isDroppingThroughPlatform ||
            isLedgeClimbing ||
            rb == null ||
            rb.linearVelocity.y > 0.1f)
        {
            return false;
        }

        float searchRadius = Mathf.Max(
            ledgeGrabHorizontalRange,
            ledgeGrabVerticalBelow
        ) + 0.6f;

        Collider[] nearbyColliders = Physics.OverlapSphere(
            transform.position,
            searchRadius,
            GroundLayerMask,
            QueryTriggerInteraction.Ignore
        );

        Collider bestPlatform = null;
        float bestEdgeX = 0f;
        float bestTopY = 0f;
        float bestSide = 0f;
        float bestDistance = float.PositiveInfinity;

        foreach (Collider platformCollider in nearbyColliders)
        {
            if (!IsLedgePlatform(platformCollider))
                continue;

            Bounds bounds = platformCollider.bounds;

            if (Mathf.Abs(transform.position.z - bounds.center.z) >
                bounds.extents.z + 0.5f)
            {
                continue;
            }

            float heightAboveLedge = transform.position.y - bounds.max.y;

            if (heightAboveLedge > ledgeGrabVerticalAbove ||
                heightAboveLedge < -ledgeGrabVerticalBelow)
            {
                continue;
            }

            EvaluateLedgeCandidate(
                platformCollider,
                bounds.min.x,
                bounds.max.y,
                -1f,
                ref bestPlatform,
                ref bestEdgeX,
                ref bestTopY,
                ref bestSide,
                ref bestDistance
            );

            EvaluateLedgeCandidate(
                platformCollider,
                bounds.max.x,
                bounds.max.y,
                1f,
                ref bestPlatform,
                ref bestEdgeX,
                ref bestTopY,
                ref bestSide,
                ref bestDistance
            );
        }

        if (bestPlatform == null)
            return false;

        StartLedgeGrab(
            bestPlatform,
            bestEdgeX,
            bestTopY,
            bestSide
        );
        return true;
    }

    void EvaluateLedgeCandidate(
        Collider platformCollider,
        float edgeX,
        float topY,
        float side,
        ref Collider bestPlatform,
        ref float bestEdgeX,
        ref float bestTopY,
        ref float bestSide,
        ref float bestDistance
    )
    {
        float horizontalDistance = Mathf.Abs(transform.position.x - edgeX);

        if (horizontalDistance > ledgeGrabHorizontalRange)
            return;

        bool isOutsideOrAtEdge = side < 0f
            ? transform.position.x <= edgeX + 0.2f
            : transform.position.x >= edgeX - 0.2f;

        if (!isOutsideOrAtEdge)
            return;

        float verticalDistance = transform.position.y - topY;
        float sqrDistance =
            horizontalDistance * horizontalDistance +
            verticalDistance * verticalDistance;

        if (sqrDistance >= bestDistance)
            return;

        bestPlatform = platformCollider;
        bestEdgeX = edgeX;
        bestTopY = topY;
        bestSide = side;
        bestDistance = sqrDistance;
    }

    bool IsLedgePlatform(Collider platformCollider)
    {
        return platformCollider != null &&
            (platformCollider.name == "BattleGround" ||
                IsDropThroughPlatform(platformCollider));
    }

    void StartLedgeGrab(
        Collider platform,
        float edgeX,
        float topY,
        float side
    )
    {
        isLedgeHanging = true;
        hangingLedgePlatform = platform;
        hangingLedgeX = edgeX;
        hangingLedgeTopY = topY;
        hangingLedgeSide = side;
        GetComponent<BattleMotionPresentation>()?.Grabbed(new Vector3(edgeX,topY,transform.position.z));
        hangingRootX =
            edgeX + side * ledgeHangHorizontalOffset;
        hangingRootY = topY - ledgeHangVerticalOffset;
        ledgeHangTimer = 0f;
        jumpRequested = false;
        moveInput = 0f;
        CancelDefence();

        gravityBeforeLedgeGrab = rb.useGravity;
        rb.useGravity = false;
        rb.linearVelocity = Vector3.zero;
        rb.position = new Vector3(
            hangingRootX,
            hangingRootY,
            0f
        );

        SetFacing(-side);
        SetMovingAnimation(false);
        SetGroundedAnimation(false);

        if (animator != null)
        {
            animatorSpeedBeforeLedgeGrab = animator.speed;
            int hangState=animator.isHuman ? Animator.StringToHash("Base Layer.Humanoid|Idle_Loop") : LedgePoseStateHash;
            if(animator.HasState(0,hangState))animator.Play(hangState,0,animator.isHuman?0:ledgePoseNormalisedTime);
            animator.Update(0.001f);
            animator.speed = 0f;
            if(animator.isHuman && !GetComponent<BattleLedgePresentation>())gameObject.AddComponent<BattleLedgePresentation>();
        }

        UpdateLedgeVisualPose(0f);
        AlignVisibleBodyWithLedgeCorner();

        StartCoroutine(PlayLedgeGrabFlash(
            new Vector3(
                edgeX,
                topY - LedgeCornerVerticalInset,
                -0.7f
            )
        ));
    }

    void HandleLedgeInput()
    {
        jumpRequested = false;
        moveInput = 0f;
        SetMovingAnimation(false);
        SetGroundedAnimation(false);

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TryStartPhysicalLedgeClimb();
            return;
        }

        if (Keyboard.current.sKey.wasPressedThisFrame)
        {
            ReleaseLedge(
                new Vector3(0f, -dropThroughSpeed, 0f),
                true
            );
            return;
        }

        bool pressedAway =
            (hangingLedgeSide < 0f &&
                Keyboard.current.aKey.wasPressedThisFrame) ||
            (hangingLedgeSide > 0f &&
                Keyboard.current.dKey.wasPressedThisFrame);

        if (pressedAway)
        {
            ReleaseLedge(
                new Vector3(
                    hangingLedgeSide * ledgeClimbHorizontalSpeed,
                    ledgeClimbVerticalSpeed * 0.45f,
                    0f
                ),
                true
            );
            PlayJumpAnimation();
        }
    }

    void ReleaseLedge(Vector3 releaseVelocity, bool startCooldown)
    {
        if (!isLedgeHanging)
            return;

        isLedgeHanging = false;
        hangingLedgePlatform = null;
        rb.useGravity = gravityBeforeLedgeGrab;
        rb.linearVelocity = releaseVelocity;

        if (animator != null)
        {
            animator.speed = animatorSpeedBeforeLedgeGrab;
        }

        SetFacing(FacingDirection);

        if (startCooldown)
        {
            ledgeGrabCooldownTimer = Mathf.Max(0f, ledgeGrabCooldown);
        }
    }

    public void ReleaseLedgeForHit()
    {
        isLedgeClimbing = false;
        if (!isLedgeHanging)
            return;

        ReleaseLedge(
            rb != null ? rb.linearVelocity : Vector3.zero,
            true
        );
    }

    void UpdateLedgeVisualPose(float sway)
    {
        if (visualRoot == null)
            return;

        float yRotation = FacingDirection > 0f ? 90f : -90f;
        float zRotation = hangingLedgeSide *
            (ledgeVisualTilt + sway * 35f);

        visualRoot.localRotation = Quaternion.Euler(
            0f,
            yRotation,
            zRotation
        );
    }

    void AlignVisibleBodyWithLedgeCorner()
    {
        if(animator && animator.isHuman)
        {
            // The old animal bounds alignment could put a human's hands below
            // the lip. Keep the capsule outside and anchor the humanoid grip.
            hangingRootX=hangingLedgeX+hangingLedgeSide*.62f;
            hangingRootY=hangingLedgeTopY-1.20f;
            rb.position=new Vector3(hangingRootX,hangingRootY,0);
            return;
        }
        if (!TryGetVisibleBodyBounds(out Bounds visibleBounds))
            return;

        float visibleTopOffset =
            visibleBounds.max.y - transform.position.y;
        float desiredVisibleTop =
            hangingLedgeTopY - LedgeCornerVerticalInset;
        hangingRootY = desiredVisibleTop - visibleTopOffset;

        if (hangingLedgeSide < 0f)
        {
            float visibleInnerOffset =
                visibleBounds.max.x - transform.position.x;
            float desiredInnerEdge =
                hangingLedgeX + LedgeCornerHorizontalInset;
            hangingRootX = desiredInnerEdge - visibleInnerOffset;
        }
        else
        {
            float visibleInnerOffset =
                visibleBounds.min.x - transform.position.x;
            float desiredInnerEdge =
                hangingLedgeX - LedgeCornerHorizontalInset;
            hangingRootX = desiredInnerEdge - visibleInnerOffset;
        }

        rb.position = new Vector3(
            hangingRootX,
            hangingRootY,
            0f
        );
    }

    bool TryGetVisibleBodyBounds(out Bounds visibleBounds)
    {
        visibleBounds = default;
        if (visualRoot == null)
            return false;

        Renderer[] renderers =
            visualRoot.GetComponentsInChildren<Renderer>(true);
        bool foundVisibleRenderer = false;

        foreach (Renderer modelRenderer in renderers)
        {
            if (modelRenderer == null || !modelRenderer.enabled)
                continue;

            if (!foundVisibleRenderer)
            {
                visibleBounds = modelRenderer.bounds;
                foundVisibleRenderer = true;
            }
            else
            {
                visibleBounds.Encapsulate(modelRenderer.bounds);
            }
        }

        return foundVisibleRenderer;
    }

    IEnumerator PlayLedgeGrabFlash(Vector3 worldPosition)
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            yield break;

        GameObject flashObject = new GameObject("Ledge Grab Flash");
        flashObject.hideFlags = HideFlags.DontSave;
        flashObject.transform.position = worldPosition;

        Material flashMaterial = new Material(shader)
        {
            name = "Runtime Ledge Grab Flash",
            hideFlags = HideFlags.HideAndDontSave
        };

        LineRenderer line = flashObject.AddComponent<LineRenderer>();
        line.sharedMaterial = flashMaterial;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 24;
        line.startWidth = 0.07f;
        line.endWidth = 0.07f;
        line.numCapVertices = 3;
        line.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = 55;

        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i / (float)line.positionCount * Mathf.PI * 2f;
            line.SetPosition(
                i,
                new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f)
            );
        }

        const float flashDuration = 0.28f;
        float timer = 0f;

        while (timer < flashDuration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / flashDuration);
            flashObject.transform.localScale =
                Vector3.one * Mathf.Lerp(0.12f, 0.48f, progress);

            Color colour = new Color(0.55f, 0.92f, 1f, 1f - progress);
            line.startColor = colour;
            line.endColor = colour;
            yield return null;
        }

        Destroy(flashObject);
        Destroy(flashMaterial);
    }

    bool TryStartPlatformDrop()
    {
        Collider platformCollider = contactedDropPlatform;

        if (!IsDropThroughPlatform(platformCollider))
        {
            platformCollider = FindDropThroughPlatformBelow();
        }

        if (platformCollider == null)
            return false;

        StartCoroutine(DropThroughPlatform(platformCollider));
        return true;
    }

    Collider FindDropThroughPlatformBelow()
    {
        return BattlePlatformPhysics.FindOneWayPlatformBelow(
            transform.position,
            0.45f,
            1.25f,
            GroundLayerMask,
            true
        );
    }

    bool IsDropThroughPlatform(Collider platformCollider)
    {
        return BattlePlatformPhysics.IsOneWayPlatform(
            platformCollider,
            true
        );
    }

    IEnumerator DropThroughPlatform(Collider platformCollider)
    {
        GetComponent<BattleMotionPresentation>()?.Dropped(new Vector3(transform.position.x,platformCollider.bounds.max.y,transform.position.z));
        activeDropPlatform = platformCollider;
        activeDropPlatformColliders =
            BattlePlatformPhysics.GetPlatformColliders(platformCollider);
        isDroppingThroughPlatform = true;
        SetPlatformCollisionsIgnored(
            activeDropPlatformColliders,
            true
        );

        rb.position += Vector3.down * 0.12f;

        Vector3 dropVelocity = rb.linearVelocity;
        dropVelocity.y = Mathf.Min(
            dropVelocity.y,
            -Mathf.Max(0.1f, dropThroughSpeed)
        );
        rb.linearVelocity = dropVelocity;

        float timeout = Mathf.Max(0.1f, dropThroughTimeout);

        while (timeout > 0f && !IsClearBelowPlatform(platformCollider))
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        RestorePlatformCollision();
    }

    bool IsClearBelowPlatform(Collider platformCollider)
    {
        return BattlePlatformPhysics.IsClearBelowPlatform(
            platformCollider,
            fighterColliders
        );
    }

    void SetPlatformCollisionsIgnored(
        Collider[] platformColliders,
        bool shouldIgnore
    )
    {
        BattlePlatformPhysics.SetCollisionsIgnored(
            fighterColliders,
            platformColliders,
            shouldIgnore
        );
    }

    void RestorePlatformCollision()
    {
        SetPlatformCollisionsIgnored(
            activeDropPlatformColliders,
            false
        );

        activeDropPlatform = null;
        activeDropPlatformColliders = null;
        isDroppingThroughPlatform = false;
    }

    void OnCollisionStay(Collision collision)
    {
        if (isDroppingThroughPlatform ||
            !IsDropThroughPlatform(collision.collider))
        {
            return;
        }

        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.4f)
            {
                contactedDropPlatform = collision.collider;
                return;
            }
        }
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.collider == contactedDropPlatform)
        {
            contactedDropPlatform = null;
        }
    }

    public void FaceLaunchDirection(float direction)
    {
        float safeDirection = direction >= 0f ? 1f : -1f;

        if (attackPoint != null)
        {
            attackPoint.localPosition = new Vector3(
                safeDirection,
                0f,
                0f
            );
        }

        SetFacing(safeDirection);
    }

    void SetFacing(float direction)
    {
        FacingDirection = direction >= 0f ? 1f : -1f;

        if (guardVfx != null)
        {
            guardVfx.SetGuarding(
                IsDefending,
                FacingDirection
            );
        }

        if (visualRoot == null)
            return;

        float yRotation = FacingDirection > 0f ? 90f : -90f;

        visualRoot.localRotation = Quaternion.Euler(
            0f,
            yRotation,
            0f
        );
    }

    void SetMovingAnimation(bool isMoving)
    {
        if (animator == null ||
            animator.runtimeAnimatorController == null)
        {
            return;
        }

        animator.SetBool(IsMovingHash, isMoving);
    }

    public bool TryPerfectGuardHit(float horizontalDirection)
    {
        // Retained signature for old callers only: no source means no verified counter contact.
        return false;
    }

    public void CancelDefence()
    {
        perfectGuardTimer = 0f;
        perfectGuardActionTimer = 0f;
        SetDefending(false);
    }

    public void PlayBlockEffect()
    {
        if (guardVfx != null)
        {
            guardVfx.PlayBlockImpact(FacingDirection);
        }
    }

    bool CanStartPerfectGuardNow()
    {
        return false;
    }

    void StartPerfectGuard()
    {
        perfectGuardTimer = Mathf.Max(0.05f, perfectGuardWindow);
        perfectGuardActionTimer = Mathf.Max(
            perfectGuardTimer,
            perfectGuardActionDuration
        );
        SetDefending(true);
        SideAAudio.GuardRaise();
    }

    void TickPerfectGuardAction()
    {
        if (perfectGuardActionTimer > 0f)
        {
            perfectGuardActionTimer -= Time.deltaTime;
        }

        if (perfectGuardTimer <= 0f)
            return;

        perfectGuardTimer -= Time.deltaTime;

        if (perfectGuardTimer <= 0f)
        {
            perfectGuardTimer = 0f;
            SetDefending(false);
        }
    }

    void SetDefending(bool defending)
    {
        IsDefending = defending;

        if (animator != null && hasDefendingParameter)
        {
            animator.SetBool(IsDefendingHash, defending);
        }

        if (guardVfx != null)
        {
            guardVfx.SetGuarding(defending, FacingDirection);
        }
    }

    void PlayJumpAnimation()
    {
        SideAAudio.Jump();
        if (animator != null && hasJumpTrigger)
        {
            animator.SetTrigger(JumpTriggerHash);
        }
    }

    void SetGroundedAnimation(bool isGrounded)
    {
        if (animator != null && hasGroundedParameter)
        {
            animator.SetBool(IsGroundedHash, isGrounded);
        }
    }

    bool HasAnimatorParameter(
        int parameterHash,
        AnimatorControllerParameterType parameterType
    )
    {
        return BattleAnimatorUtility.HasParameter(
            animator,
            parameterHash,
            parameterType
        );
    }

    void OnDisable()
    {
        postHitAirControl = 0;
        jumpPressUntil = -1;
        isLedgeClimbing = false;
        specialMoves?.Cancel();
        ReleaseLedge(Vector3.zero, false);
        RestorePlatformCollision();
        contactedDropPlatform = null;
        CancelDefence();
    }

    // Teleporting between lessons must not retain ignored platform pairs,
    // hanging gravity, an exhausted air leap or a buffered jump/attack.
    public void ResetForTutorial()
    {
        postHitAirControl = 0;
        jumpPressUntil = -1;
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!battleAttack) battleAttack = GetComponent<BattleAttack>();
        if (!specialMoves) specialMoves = GetComponent<BattleSpecialMoves>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        StopAllCoroutines();
        isLedgeClimbing = false;
        ReleaseLedge(Vector3.zero, false);
        RestorePlatformCollision();
        contactedDropPlatform = null;
        isDroppingThroughPlatform = false;
        hangingLedgePlatform = null;
        ledgeGrabCooldownTimer = climbTimer = ledgeHangTimer = 0;
        remainingAirJumps = 1;
        jumpRequested = false;
        moveInput = 0;
        CancelDefence();
        battleAttack?.CancelForInputFocusLoss();
        if (specialMoves)
        {
            bool wasEnabled = specialMoves.enabled;
            specialMoves.enabled = false;
            specialMoves.enabled = wasEnabled;
        }
        if (rb)
        {
            rb.useGravity = true;
            rb.linearVelocity = rb.angularVelocity = Vector3.zero;
        }
        if (animator)
        {
            animator.speed = 1;
            BattleAnimatorUtility.ResetTriggers(animator);
            SetGroundedAnimation(true);
            SetMovingAnimation(false);
            int idle = Animator.StringToHash("Base Layer.CharacterArmature|Idle");
            if (animator.HasState(0, idle)) animator.Play(idle, 0, 0);
        }
    }

    // New counter uses the defending pose, but not the legacy continuous guard circle or L timer.
    public void SetSpecialDefencePose(bool defending)
    {
        IsDefending = defending;
        if (animator != null && hasDefendingParameter) animator.SetBool(IsDefendingHash, defending);
        guardVfx?.SetGuarding(false, FacingDirection);
    }

    // Rise first outside the edge, then move inward only once the capsule feet clear the platform.
    bool TryStartPhysicalLedgeClimb()
    {
        CapsuleCollider shape = GetComponent<CapsuleCollider>();
        if (!shape || !shape.enabled || !rb) return false;
        Bounds bounds = shape.bounds;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
        float feetOffset = transform.position.y - bounds.min.y;
        Vector3 destination = new Vector3(hangingLedgeX - hangingLedgeSide * (radius + .18f),
            hangingLedgeTopY + feetOffset + .08f, transform.position.z);
        Vector3 futureCenter = bounds.center + destination - transform.position;
        float segment = Mathf.Max(0f, bounds.extents.y - radius);
        int count = Physics.OverlapCapsuleNonAlloc(futureCenter + Vector3.up * segment, futureCenter - Vector3.up * segment,
            Mathf.Max(.05f, radius - .03f), climbObstacles, GroundLayerMask, QueryTriggerInteraction.Ignore);
        if (count == climbObstacles.Length) return false;
        for (int i = 0; i < count; i++)
            if (climbObstacles[i] && !climbObstacles[i].transform.IsChildOf(transform)) return false;

        climbTargetX = destination.x; climbTopY = hangingLedgeTopY; climbTimer = 1.5f;
        // A small clearance arc leaves enough time to cross the capsule width before falling again.
        float lift = Mathf.Max(.2f, destination.y - transform.position.y + 1f);
        float launch = Mathf.Max(ledgeClimbVerticalSpeed, Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * riseGravityMultiplier * lift));
        ReleaseLedge(new Vector3(0f, launch, 0f), true);
        isLedgeClimbing = true; jumpRequested = false; PlayJumpAnimation();
        return true;
    }

    void TickPhysicalLedgeClimb()
    {
        climbTimer -= Time.fixedDeltaTime;
        CapsuleCollider shape = GetComponent<CapsuleCollider>();
        bool feetClear = shape && shape.bounds.min.y >= climbTopY + .035f;
        Vector3 velocity = rb.linearVelocity;
        velocity.x = feetClear ? Mathf.Clamp((climbTargetX - rb.position.x) / Time.fixedDeltaTime,
            -Mathf.Max(6f, ledgeClimbHorizontalSpeed), Mathf.Max(6f, ledgeClimbHorizontalSpeed)) : 0f;
        velocity.z = 0f; rb.linearVelocity = velocity;
        if (feetClear && Mathf.Abs(climbTargetX - rb.position.x) < .12f)
        {
            isLedgeClimbing = false; remainingAirJumps = 1;
            specialMoves?.OnSafeLedgeClimbCompleted();
        }
        else if (climbTimer <= 0f) isLedgeClimbing = false;
    }
}
