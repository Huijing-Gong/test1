using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class PlayerMovement : MonoBehaviour
{
    private static readonly int MoveAmountHash = Animator.StringToHash("MoveAmount");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");
    private static readonly int IsSwimmingHash = Animator.StringToHash("IsSwimming");
    private static readonly int SwimAmountHash = Animator.StringToHash("SwimAmount");
    private static readonly int IsClimbingHash = Animator.StringToHash("IsClimbing");
    private static readonly int ClimbAmountHash = Animator.StringToHash("ClimbAmount");
    private static readonly int ClimbTopHash = Animator.StringToHash("ClimbTop");

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Gravity")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundedVerticalVelocity = -2f;

    [Header("Jump")]
    [SerializeField] private float jumpHeight = 2f;
    [SerializeField] private float jumpAnticipationTime = 0.12f;

    [Header("Animation")]
    [SerializeField] private float animationDampTime = 0.1f;

    [Header("Respawn")]
    [SerializeField] private float respawnBelowY = -10f;

    [Header("Grounding")]
    [SerializeField, Min(0f), Tooltip("Extra distance below the CharacterController capsule used to detect walkable ground.")]
    private float groundProbeDistance = 0.18f;

    [SerializeField, Range(0.1f, 1f), Tooltip("Scales the CharacterController radius used by the ground sphere cast.")]
    private float groundProbeRadiusScale = 0.9f;

    [SerializeField, Min(0f), Tooltip("Time that ordinary movement may briefly lose ground contact before being considered airborne.")]
    private float groundedGraceTime = 0.12f;

    [SerializeField, Tooltip("Physics layers that can be treated as ground by the ground probe.")]
    private LayerMask groundLayers = ~0;

    [Header("Water")]
    [SerializeField, Range(0f, 1f), Tooltip("Horizontal movement multiplier while wading or standing in deep water.")]
    private float wadingSpeedMultiplier = 0.55f;

    [SerializeField, Min(0f), Tooltip("Minimum water depth required before the Player is considered wading.")]
    private float wadingMinimumDepth = 0.2f;

    [SerializeField, Min(0f), Tooltip("Water depth at which the Player is marked as being in deep water.")]
    private float deepWaterThreshold = 1.1f;

    [SerializeField, Min(0f), Tooltip("Horizontal movement speed while surface swimming.")]
    private float swimSpeed = 2f;

    [SerializeField, Min(0f), Tooltip("Distance from the water surface to the Player root while treading water.")]
    private float treadSurfaceOffset = 1.2f;

    [SerializeField, Min(0f), Tooltip("Distance from the water surface to the Player root while swimming forward.")]
    private float swimSurfaceOffset = 0.9f;

    [SerializeField, Min(0f), Tooltip("Strength used to follow the target water-surface height.")]
    private float surfaceFollowSpeed = 4f;

    [SerializeField, Min(0f), Tooltip("Maximum upward or downward buoyancy speed.")]
    private float maximumBuoyancySpeed = 2.5f;

    [SerializeField, Min(0f), Tooltip("Water depth below which surface swimming exits. Must remain below Deep Water Threshold.")]
    private float swimExitDepth = 0.65f;

    [SerializeField, Min(0f), Tooltip("Time used to smooth the tread-to-swim animation blend.")]
    private float swimAnimationDampTime = 0.15f;

    [Header("Climbing")]
    [SerializeField, Min(0f)] private float climbSpeed = 0.65f;
    [SerializeField, Min(0f)] private float climbProbeWaistHeight = 0.65f;
    [SerializeField, Min(0f)] private float climbProbeChestHeight = 1.2f;
    [SerializeField, Range(0.05f, 0.3f)] private float climbProbeRadius = 0.17f;
    [SerializeField, Min(0f)] private float climbDetectionDistance = 0.75f;
    [SerializeField, Min(0f)] private float climbWallDistance = 0.45f;
    [SerializeField, Range(0f, 180f)] private float climbMinimumAngle = 55f;
    [SerializeField, Range(0f, 180f)] private float climbMaximumAngle = 120f;
    [SerializeField, Range(0f, 90f)] private float climbNormalDifference = 20f;
    [SerializeField, Min(0f)] private float climbSurfaceGraceTime = 0.12f;
    [SerializeField, Min(0f)] private float climbNormalSmoothSpeed = 12f;
    [SerializeField, Min(0f)] private float climbRotationSpeed = 14f;
    [SerializeField, Min(0f)] private float climbSnapMaxSpeed = 3f;
    [SerializeField] private LayerMask climbLayers = ~0;

    [Header("Climb Top")]
    [SerializeField, Min(0f)] private float climbTopMinimumDetectionTime = 0.06f;
    [SerializeField, Min(0f)] private float climbTopProbeHeight = 2.1f;
    [SerializeField, Min(0f)] private float climbTopForwardDistance = 0.8f;
    [SerializeField, Min(0f)] private float climbTopDownDistance = 2f;
    [SerializeField, Min(0f)] private float climbTopMinimumRise = 0.35f;
    [SerializeField, Min(0f)] private float climbTopMaximumRise = 1.8f;
    [SerializeField, Range(0f, 90f)] private float climbTopMaximumSlope = 45f;
    [SerializeField, Min(0f)] private float climbTopMaximumLateralOffset = 0.5f;
    [SerializeField, Min(0f)] private float climbTopLandingInset = 0.15f;
    [SerializeField, Min(0f)] private float climbTopLandingHeight = 0.05f;
    [SerializeField, Min(0.01f)] private float climbTopDuration = 3.15f;
    [SerializeField, Range(0f, 1f)] private float climbTopMovementStart = 0.15f;
    [SerializeField, Range(0f, 1f)] private float climbTopVerticalEnd = 0.55f;
    [SerializeField, Range(0f, 1f)] private float climbTopMovementEnd = 0.9f;
    [SerializeField, Min(0f)] private float climbTopApexHeight = 0.1f;
    [SerializeField, Min(0f)] private float climbTopMaximumMoveSpeed = 3f;

    private PlayerVitals vitals;
    private CharacterController characterController;
    private KingOfGraphic.Hero.HeroWorldAdapter hero;
    private PlayerBattleActions battleActions;
    private float MotionDelta => battleActions ? battleActions.PlayerDeltaTime : Time.deltaTime;
    private Animator animator;
    private Camera mainCamera;
    private ThirdPersonCamera teamLockCamera;
    private float verticalVelocity;
    private int authorJumpIndex;private float authorJumpAge,authorSwimDepth;
    public int AuthorJumpIndex=>authorJumpIndex;
    public float AuthorJumpAge=>authorJumpAge;
    public float AuthorSwimVertical{get;private set;}
    private bool jumpPreparing;
    private bool jumpInProgress;
    private float jumpAnticipationTimer;
    private Vector3 initialWorldPosition;
    private Quaternion initialWorldRotation;
    private readonly RaycastHit[] groundProbeHits = new RaycastHit[8];
    private readonly WaterVolume[] overlappingWaterVolumes = new WaterVolume[8];
    private readonly RaycastHit[] climbWaistHits = new RaycastHit[8];
    private readonly RaycastHit[] climbChestHits = new RaycastHit[8];
    private readonly RaycastHit[] climbTopHits = new RaycastHit[8];
    private readonly Collider[] mantleClearanceHits = new Collider[16];
    private Vector3 groundNormal = Vector3.up;
    private float groundAngle;
    private float ungroundedTime;
    private bool stableGrounded;
    private WaterVolume currentWaterVolume;
    private float waterSurfaceY;
    private float waterDepth;
    private bool isInWater;
    private bool isWading;
    private bool isDeepWater;
    private bool swimmingActive;
    private float swimAmount;
    private float buoyancyVelocity;
    private bool climbingActive;
    private float climbAmount;
    private float climbLostTime;
    private float climbElapsedTime;
    private Vector3 climbSurfaceNormal = Vector3.forward;
    private Vector3 climbSurfacePoint;
    private Collider currentClimbCollider;
    private bool climbHasLeftGround;
    private bool mantlingActive;
    private float mantleElapsedTime;
    private Vector3 mantleStartPosition;
    private Vector3 mantleTargetPosition;
    private Vector3 mantleApexPosition;
    private Collider mantleLandingCollider;
    private float climbReentryCooldown;
    private bool climbHadValidSurface;
    private bool climbRequiresInputRelease;

    private const float GroundAngleTolerance = 0.5f;
    private const float ClimbReentryCooldownDuration = 0.2f;
    private const float MantleCompletionTolerance = 0.2f;

    public bool ClimbingActive => climbingActive;
    public bool LedgeHangingActive => climbingActive && ledgeHanging;
    public float LedgeHangElapsed => ledgeHangElapsed;
    private bool ledgeHanging, ledgeClimbRearmed;
    private float ledgeHangElapsed, ledgeRecatchDelay;
    private Vector3 ledgeHangStart, ledgeHangRoot;
    public bool MantlingActive => mantlingActive;
    public float MantleElapsedTime => mantleElapsedTime;
    public bool FastSwimmingActive => swimmingActive && IsRunHeld() && (!vitals || vitals.CanExert);
    public bool SprintingActive { get; private set; }
    // Presentation consumes the accepted physical jump, including its preparation phase.
    public bool GroundJumpActive => jumpPreparing || jumpInProgress;
    public bool GroundJumpStartedRunning { get; private set; }
    public bool StairSupportActive => stairSupportTimer > 0;
    private float stairSupportTimer;
    private bool walkableStepAhead;
    private const float WalkableStepHeight = .42f;
    private readonly RaycastHit[] stepSupportHits = new RaycastHit[24];
    public Vector3 GroundNormal => groundNormal;
    public float GroundAngle => groundAngle;
    public bool IsStableGrounded => stableGrounded;
    // Animation needs the latest ordinary Move contact, not the nearby-ground/grace policy.
    private int physicalGroundContactFrame = -1;
    private bool sampledPhysicalGroundContact;
    public bool HasCurrentGroundContactSample => physicalGroundContactFrame == Time.frameCount;
    public bool PhysicalGroundContact => HasCurrentGroundContactSample && sampledPhysicalGroundContact;
    private void InvalidateGroundContact() { physicalGroundContactFrame = -1; sampledPhysicalGroundContact = false; }
    public WaterVolume CurrentWaterVolume => currentWaterVolume;
    public bool IsInWater => isInWater;
    public float WaterSurfaceY => waterSurfaceY;
    public float WaterDepth => waterDepth;
    public bool IsWading => isWading;
    public bool IsDeepWater => isDeepWater;
    public bool SwimmingActive => swimmingActive;
    public float SwimAmount => swimAmount;
    private bool externalInputLock;
    private readonly System.Collections.Generic.HashSet<Object> actionLocks=new System.Collections.Generic.HashSet<Object>();
    public bool HasExternalInputLock=>externalInputLock;
    public bool IsGameplayInputLocked => externalInputLock || actionLocks.Count>0 || ImpactActive;
    public void SetActionInputLocked(Object owner,bool locked){if(!owner)return;if(locked)actionLocks.Add(owner);else actionLocks.Remove(owner);}
    private Vector3 impactVelocity;
    private float impactRemaining, impactDuration;
    public bool ImpactActive => impactRemaining > 0f;
    public bool CanStartInteraction =>
        enabled &&
        !IsGameplayInputLocked &&
        stableGrounded &&
        !swimmingActive &&
        !climbingActive &&
        !mantlingActive &&
        !jumpPreparing &&
        !jumpInProgress;

    public void SetGameplayInputLocked(bool locked)
    {
        externalInputLock = locked;

        if (locked && animator != null)
        {
            SetAnimatorFloat(MoveAmountHash, 0f, animationDampTime, MotionDelta);
        }
    }

    // Enemy impacts use the same collision-constrained CharacterController movement.
    public void ApplyImpact(Vector3 worldVelocity, float seconds)
    {
        if (!isActiveAndEnabled || !characterController || !characterController.enabled ||
            swimmingActive || climbingActive || mantlingActive) return;
        worldVelocity.y = 0f;
        impactVelocity = Vector3.ClampMagnitude(worldVelocity, 8f);
        impactDuration = impactRemaining = Mathf.Clamp(seconds, .08f, .5f);
        jumpPreparing = false;
        jumpAnticipationTimer = 0f;
    }
    public void ClearImpact()
    {
        impactRemaining = impactDuration = 0f;
        impactVelocity = Vector3.zero;
    }

    private void Awake()
    {
        hero=GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
        vitals = GetComponent<PlayerVitals>();
        characterController = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>(true);
        mainCamera = Camera.main;
        teamLockCamera=mainCamera?mainCamera.GetComponent<ThirdPersonCamera>():null;
        initialWorldPosition = transform.position;
        initialWorldRotation = transform.rotation;

        if (characterController == null)
        {
            Debug.LogError("PlayerMovement requires a CharacterController on the same GameObject.", this);
            enabled = false;
            return;
        }

        if (animator == null)
        {
            Debug.LogError("PlayerMovement could not find an Animator in the Player child hierarchy.", this);
            enabled = false;
            return;
        }

        animator.applyRootMotion = false;
        stableGrounded = characterController.isGrounded || ProbeGround(out _, out _);
        SetAnimatorBool(IsGroundedHash, stableGrounded);
        SetAnimatorFloat(VerticalSpeedHash, verticalVelocity);
        SetAnimatorBool(IsSwimmingHash, false);
        SetAnimatorFloat(SwimAmountHash, 0f);
        ResetClimbingRuntimeState(true);
        ResetMantlingRuntimeState(true);
    }

    private void Update()
    {
        if(GetComponent<WorldHeroActions>()?.Dying??false)return;
        InvalidateGroundContact();
        SprintingActive = false;
        if(hero&&hero.ExternalMovement)return;
        if (transform.position.y < respawnBelowY)
        {
            if (vitals) vitals.FellOutOfWorld(); else Respawn();
            return;
        }

        if(!battleActions)battleActions=GetComponent<PlayerBattleActions>();
        if(battleActions && (battleActions.IsDodging || battleActions.IsRushing))return;
        RefreshWaterState();
        if(hero&&hero.AuthorControls){authorJumpAge+=MotionDelta;if(stableGrounded&&!jumpInProgress||swimmingActive||climbingActive||mantlingActive)authorJumpIndex=0;if(!swimmingActive){authorSwimDepth=0;AuthorSwimVertical=0;}}

        Vector2 input = IsGameplayInputLocked ? Vector2.zero : ReadMovementInput();
        if (input.y <= 0.1f)
        {
            climbRequiresInputRelease = false;
        }

        Vector3 movementDirection = GetCameraRelativeDirection(input);
        UpdateWalkableStepProbe(movementDirection);
        bool hasMovementInput = movementDirection.sqrMagnitude > 0f;
        climbReentryCooldown = Mathf.Max(0f, climbReentryCooldown - MotionDelta);

        if (swimmingActive)
        {
            if (currentWaterVolume == null || !currentWaterVolume.IsValid || !isInWater || waterDepth <= Mathf.Max(0f, swimExitDepth))
            {
                ExitSwimming();
                return;
            }
            else
            {
                UpdateSwimming(movementDirection, hasMovementInput);
                return;
            }
        }
        else if (currentWaterVolume != null && currentWaterVolume.IsValid &&
                 waterDepth >= Mathf.Max(0f, deepWaterThreshold))
        {
            EnterSwimming();
            UpdateSwimming(movementDirection, hasMovementInput);
            return;
        }

        if (mantlingActive)
        {
            UpdateMantling();
            return;
        }

        if (climbingActive)
        {
            if (vitals && !vitals.CanExert && !IsGameplayInputLocked) { ExitClimbing(false, true); return; }
            UpdateClimbing(input);
            return;
        }

        bool climbingEntryAllowed = (!vitals || vitals.CanExert) && !walkableStepAhead && input.y > 0.1f &&
                                    !climbRequiresInputRelease &&
                                    climbReentryCooldown <= 0f &&
                                    !jumpPreparing &&
                                    !(jumpInProgress && verticalVelocity > 0f);
        if (climbingEntryAllowed && (!hero||hero.AcceptInput)&&MotionDelta>0&&
            TryFindClimbSurface(out Vector3 surfaceNormal, out Vector3 surfacePoint, out Collider surfaceCollider)&&(!hero||hero.PrepareAuthorStanding()))
        {
            EnterClimbing(surfaceNormal, surfacePoint, surfaceCollider);
            UpdateClimbing(input);
            return;
        }

        bool isRunning = hasMovementInput && IsRunHeld() && (!vitals || vitals.CanExert);
        SprintingActive = isRunning && stableGrounded;

        Vector3 facingDirection=movementDirection;
        if(!IsGameplayInputLocked && teamLockCamera && teamLockCamera.LockedEnemy)
            facingDirection=Vector3.ProjectOnPlane(teamLockCamera.LockedEnemy.position-transform.position,Vector3.up);
        if(hero)hero.ObserveGroundFacing(facingDirection,!hasMovementInput&&!isRunning&&!IsJumpPressed()&&!IsGameplayInputLocked,MotionDelta);
        if (facingDirection.sqrMagnitude>.0001f && !IsGameplayInputLocked)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
            float rotationBlend = 1f - Mathf.Exp(-rotationSpeed * MotionDelta);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationBlend);
        }

        bool controllerGroundedBeforeMove = characterController.isGrounded;
        bool probeGroundedBeforeMove = ProbeGround(out _, out _);
        bool physicalJumpInProgress = jumpInProgress && !jumpPreparing;
        bool reliableGroundBeforeMove = controllerGroundedBeforeMove ||
                                        (!physicalJumpInProgress && (probeGroundedBeforeMove || stableGrounded));
        bool authorDouble=hero&&hero.AuthorControls&&hero.AcceptInput&&MotionDelta>0&&!IsGameplayInputLocked&&!reliableGroundBeforeMove&&authorJumpIndex==1&&
            (GetComponent<PlayerEquipment>()?.SwordDrawn??false)&&IsJumpPressed()&&hero.PrepareAuthorStanding();
        if(authorDouble){authorJumpIndex=2;authorJumpAge=0;jumpPreparing=false;jumpInProgress=true;verticalVelocity=Mathf.Sqrt(jumpHeight*-2f*gravity);ConsumeJumpInputForFrame();}
        bool jumpRequested = (!hero||hero.AcceptInput)&&MotionDelta>0&&!IsGameplayInputLocked &&
                             reliableGroundBeforeMove &&
                             !jumpPreparing &&
                             verticalVelocity <= 0f &&
                             IsJumpPressed();
        if(jumpRequested&&hero&&!hero.PrepareAuthorStanding())jumpRequested=false;
        if (jumpRequested)
        {
            GroundJumpStartedRunning = isRunning;
            authorJumpIndex=1;authorJumpAge=0;
            jumpPreparing = true;
            jumpInProgress = true;
            jumpAnticipationTimer = jumpAnticipationTime;
            SetAnimatorTrigger(JumpHash);
            SetAnimatorBool(IsGroundedHash, false);
        }

        if (jumpPreparing)
        {
            if (!reliableGroundBeforeMove)
            {
                jumpPreparing = false;
                jumpAnticipationTimer = 0f;
                verticalVelocity += gravity * MotionDelta;
            }
            else
            {
                verticalVelocity = groundedVerticalVelocity;
                jumpAnticipationTimer -= MotionDelta;

                if (jumpAnticipationTimer <= 0f)
                {
                    jumpPreparing = false;
                    jumpAnticipationTimer = 0f;
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    verticalVelocity += gravity * MotionDelta;
                }
            }
        }
        else if (reliableGroundBeforeMove && verticalVelocity < 0f)
        {
            verticalVelocity = groundedVerticalVelocity;
        }
        else
        {
            verticalVelocity += gravity * MotionDelta;
        }

        float movementSpeed = (isRunning ? runSpeed : walkSpeed)*(hero?hero.SpeedMultiplier:1f);
        // Backpedalling uses a planted backward gait, rather than a forward sprint
        // translated in reverse. Keep its speed within that gait's playback range.
        if(teamLockCamera&&teamLockCamera.LockedEnemy&&GetComponent<WorldHeroActions>()&&
            Vector3.Dot(movementDirection,transform.forward)<-.25f)
            movementSpeed=Mathf.Min(movementSpeed,isRunning?3.2f:2f);
        if(hero&&hero.AuthorControls){var keys=Keyboard.current;bool slow=keys!=null&&(keys.leftAltKey.isPressed||keys.rightAltKey.isPressed);
            movementSpeed=Mathf.Min(isRunning?hero.Library.authoredSprintSpeed:slow?hero.Library.authoredWalkSpeed:hero.Library.authoredRunSpeed,hero.AuthoredSpeedLimit);}
        if (isWading || isDeepWater)
        {
            movementSpeed *= Mathf.Clamp01(wadingSpeedMultiplier);
        }

        Vector3 velocity = movementDirection * (hasMovementInput ? movementSpeed : 0f);
        if (ImpactActive)
        {
            float step = Mathf.Min(MotionDelta, impactRemaining);
            float weight = (impactRemaining - step * .5f) / Mathf.Max(.001f, impactDuration);
            velocity += impactVelocity * weight * step / Mathf.Max(.0001f, MotionDelta);
            impactRemaining = Mathf.Max(0f, impactRemaining - step);
        }
        velocity.y = verticalVelocity;
        CollisionFlags collisionFlags = characterController.Move(velocity * MotionDelta);
        if (hero && StairSupportActive && reliableGroundBeforeMove && !jumpInProgress && !jumpPreparing &&
            (collisionFlags & CollisionFlags.Below) == 0 &&
            TryStepSupport(transform.position + Vector3.up * .12f, WalkableStepHeight + .15f, out var lowerStep))
        {
            float drop = transform.position.y - lowerStep.point.y;
            if (drop > .025f && drop <= WalkableStepHeight)
                collisionFlags |= characterController.Move(Vector3.down * (drop + .025f));
        }

        if ((collisionFlags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
        {
            verticalVelocity = 0f;
        }

        bool controllerGroundedAfterMove = characterController.isGrounded;
        bool collisionBelow = (collisionFlags & CollisionFlags.Below) != 0;
        bool probeGroundedAfterMove = ProbeGround(out _, out _);
        bool physicalGroundContact = controllerGroundedAfterMove || collisionBelow;
        if (MotionDelta > 0f)
        {
            physicalGroundContactFrame = Time.frameCount;
            sampledPhysicalGroundContact = physicalGroundContact;
        }
        bool ordinaryGroundContact = physicalGroundContact || probeGroundedAfterMove;
        if (jumpInProgress && !jumpPreparing && verticalVelocity <= 0f && physicalGroundContact)
        {
            jumpInProgress = false;
        }

        bool activeJump = jumpPreparing || verticalVelocity > 0f || (jumpInProgress && !physicalGroundContact);
        UpdateStableGrounded(ordinaryGroundContact, activeJump);

        float targetMoveAmount = !hasMovementInput ? 0f : isRunning ? 1f : 0.5f;
        // Match the world hero's immediate capsule stop without extending a travelling pose.
        float moveDamp = !hasMovementInput && GetComponent<WorldHeroActions>() ? Mathf.Min(animationDampTime, .04f) : animationDampTime;
        SetAnimatorFloat(MoveAmountHash, targetMoveAmount, moveDamp, MotionDelta);
        SetAnimatorBool(IsGroundedHash, stableGrounded);
        SetAnimatorFloat(VerticalSpeedHash, stableGrounded ? groundedVerticalVelocity : jumpPreparing ? 0f : verticalVelocity);
        SetAnimatorBool(IsSwimmingHash, false);
        UpdateSwimAmount(0f);
    }

    private void EnterSwimming()
    {
        ResetMantlingRuntimeState(true);
        ResetClimbingRuntimeState(true);
        swimmingActive = true;
        buoyancyVelocity = 0f;
        verticalVelocity = 0f;
        jumpPreparing = false;
        jumpInProgress = false;
        jumpAnticipationTimer = 0f;
        ungroundedTime = 0f;
        stableGrounded = false;
        isWading = false;

        ResetAnimatorTrigger(JumpHash);
        SetAnimatorFloat(MoveAmountHash, 0f);
        SetAnimatorBool(IsGroundedHash, false);
        SetAnimatorFloat(VerticalSpeedHash, 0f);
        SetAnimatorBool(IsSwimmingHash, true);
        animator.applyRootMotion = false;
    }

    private void ExitSwimming()
    {
        swimmingActive = false;
        buoyancyVelocity = 0f;
        verticalVelocity = 0f;
        jumpPreparing = false;
        jumpInProgress = false;
        jumpAnticipationTimer = 0f;
        ungroundedTime = 0f;
        SetAnimatorBool(IsSwimmingHash, false);
        UpdateSwimAmount(0f);

        bool hasWalkableGround = characterController.isGrounded || ProbeGround(out _, out _);
        stableGrounded = hasWalkableGround;
        if (hasWalkableGround)
        {
            verticalVelocity = groundedVerticalVelocity;
        }

        SetAnimatorBool(IsGroundedHash, hasWalkableGround);
        SetAnimatorFloat(VerticalSpeedHash, hasWalkableGround ? groundedVerticalVelocity : 0f);
    }

    private bool TryStepSupport(Vector3 origin, float distance, out RaycastHit support)
    {
        support = default; float nearest = float.PositiveInfinity;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, stepSupportHits, distance, groundLayers, QueryTriggerInteraction.Ignore);
        for (int i=0;i<count;i++) {
            var hit=stepSupportHits[i];
            if (!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.collider is CharacterController || hit.distance>=nearest) continue;
            // A higher unwalkable surface must occlude the lower floor.
            nearest=hit.distance; support=hit;
        }
        return support.collider && Vector3.Angle(support.normal,Vector3.up)<=characterController.slopeLimit+GroundAngleTolerance;
    }
    private void UpdateWalkableStepProbe(Vector3 direction)
    {
        walkableStepAhead=false; stairSupportTimer=Mathf.Max(0,stairSupportTimer-MotionDelta);
        if (!hero || jumpInProgress || jumpPreparing || climbingActive || mantlingActive || swimmingActive || direction.sqrMagnitude<.01f || !(stableGrounded||characterController.isGrounded)) return;
        characterController.stepOffset=Mathf.Max(characterController.stepOffset,Mathf.Min(WalkableStepHeight,characterController.height*.3f));
        Vector3 ahead=transform.position+Vector3.ProjectOnPlane(direction,Vector3.up).normalized*.48f;
        if (!TryStepSupport(ahead+Vector3.up*(WalkableStepHeight+.08f),WalkableStepHeight*2+.16f,out var support)) return;
        if (!TryStepSupport(transform.position+Vector3.up*.15f,WalkableStepHeight+.25f,out var beneath)) return;
        float rise=support.point.y-beneath.point.y;
        if (Mathf.Abs(rise)<.035f || Mathf.Abs(rise)>WalkableStepHeight) return;
        var target=new Vector3(ahead.x,support.point.y+.04f,ahead.z);
        int overlaps=Physics.OverlapCapsuleNonAlloc(target+Vector3.up*.72f,target+Vector3.up*1.5f,.24f,mantleClearanceHits,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<overlaps;i++)if(mantleClearanceHits[i]&&!mantleClearanceHits[i].transform.IsChildOf(transform)&&mantleClearanceHits[i]!=support.collider)return;
        stairSupportTimer=.35f; walkableStepAhead=rise>0;
    }

    // Surface swimming owns both horizontal motion and buoyancy so gravity is not applied in the same frame.
    private void UpdateSwimming(Vector3 movementDirection, bool hasMovementInput)
    {
        if (hasMovementInput)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movementDirection, Vector3.up);
            float rotationBlend = 1f - Mathf.Exp(-Mathf.Max(0f, rotationSpeed) * MotionDelta);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationBlend);
        }

        UpdateSwimAmount(hasMovementInput ? 1f : 0f);
        float targetOffset = Mathf.Lerp(
            Mathf.Max(0f, treadSurfaceOffset),
            Mathf.Max(0f, swimSurfaceOffset),
            swimAmount);
        if(hero&&hero.AuthorControls&&hero.AcceptInput&&MotionDelta>0&&!IsGameplayInputLocked){var keys=Keyboard.current;AuthorSwimVertical=keys==null?0:(keys.spaceKey.isPressed?1:0)-(keys.leftCtrlKey.isPressed?1:0);authorSwimDepth=Mathf.Clamp(authorSwimDepth-AuthorSwimVertical*1.15f*MotionDelta,0,.85f);}else AuthorSwimVertical=0;
        float targetRootY = waterSurfaceY - targetOffset - authorSwimDepth;
        float heightError = targetRootY - transform.position.y;
        buoyancyVelocity = Mathf.Clamp(
            heightError * Mathf.Max(0f, surfaceFollowSpeed),
            -Mathf.Max(0f, maximumBuoyancySpeed),
            Mathf.Max(0f, maximumBuoyancySpeed));

        Vector3 finalVelocity = movementDirection * (hasMovementInput ? Mathf.Max(0f, swimSpeed) * (hero && FastSwimmingActive ? 1.35f : 1f) : 0f);
        finalVelocity.y = buoyancyVelocity;
        characterController.Move(finalVelocity * MotionDelta);

        verticalVelocity = 0f;
        stableGrounded = false;
        ungroundedTime = 0f;
        isWading = false;
        SetAnimatorBool(IsSwimmingHash, true);
        SetAnimatorFloat(MoveAmountHash, 0f);
        SetAnimatorBool(IsGroundedHash, false);
        SetAnimatorFloat(VerticalSpeedHash, 0f);
        animator.applyRootMotion = false;
    }

    // The authored playable graph owns the body; legacy parameters have no consumer there.
    void ResetAnimatorTrigger(int hash){if(UsesLegacyAnimator)animator.ResetTrigger(hash);}
    void SetAnimatorTrigger(int hash){if(UsesLegacyAnimator)animator.SetTrigger(hash);}
    bool UsesLegacyAnimator => animator && animator.runtimeAnimatorController && !(hero && hero.isActiveAndEnabled);
    void SetAnimatorFloat(int hash,float value){if(UsesLegacyAnimator)animator.SetFloat(hash,value);}
    void SetAnimatorFloat(int hash,float value,float damping,float delta){if(UsesLegacyAnimator)animator.SetFloat(hash,value,damping,delta);}
    void SetAnimatorBool(int hash,bool value){if(UsesLegacyAnimator)animator.SetBool(hash,value);}

    private void UpdateSwimAmount(float target)
    {
        float dampTime = Mathf.Max(0f, swimAnimationDampTime);
        float blend = dampTime <= 0f ? 1f : 1f - Mathf.Exp(-MotionDelta / dampTime);
        swimAmount = Mathf.Lerp(swimAmount, Mathf.Clamp01(target), blend);
        if (Mathf.Abs(swimAmount - target) < 0.0001f)
        {
            swimAmount = target;
        }

        SetAnimatorFloat(SwimAmountHash, swimAmount);
    }

    private bool TryFindClimbSurface(
        out Vector3 surfaceNormal,
        out Vector3 surfacePoint,
        out Collider surfaceCollider)
    {
        bool waistFound = ProbeClimbHeight(
            Mathf.Max(0f, climbProbeWaistHeight),
            climbWaistHits,
            out RaycastHit waistHit);
        bool chestFound = ProbeClimbHeight(
            Mathf.Max(0f, climbProbeChestHeight),
            climbChestHits,
            out RaycastHit chestHit);

        surfaceNormal = Vector3.forward;
        surfacePoint = Vector3.zero;
        surfaceCollider = null;

        if (!waistFound || !chestFound)
        {
            return false;
        }

        bool sameCollider = waistHit.collider == chestHit.collider;
        float normalDifference = Vector3.Angle(waistHit.normal, chestHit.normal);
        float distanceDifference = Mathf.Abs(waistHit.distance - chestHit.distance);
        if (!sameCollider &&
            (normalDifference > Mathf.Clamp(climbNormalDifference, 0f, 90f) || distanceDifference > 0.35f))
        {
            return false;
        }

        Vector3 averagedNormal = waistHit.normal + chestHit.normal;
        if (averagedNormal.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        surfaceNormal = averagedNormal.normalized;
        surfacePoint = (waistHit.point + chestHit.point) * 0.5f;
        surfaceCollider = sameCollider || waistHit.distance <= chestHit.distance
            ? waistHit.collider
            : chestHit.collider;
        return true;
    }

    private bool ProbeClimbHeight(float height, RaycastHit[] hitBuffer, out RaycastHit nearestHit)
    {
        Vector3 origin = transform.position + Vector3.up * height;
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            Mathf.Clamp(climbProbeRadius, 0.05f, 0.3f),
            transform.forward,
            hitBuffer,
            Mathf.Max(0f, climbDetectionDistance),
            climbLayers,
            QueryTriggerInteraction.Ignore);

        return TryNearestClimbObstacle(hitBuffer, hitCount, out nearestHit) && IsValidClimbHit(nearestHit);
    }

    // A forbidden/unsuitable foreground surface still occludes climbable geometry behind it.
    private bool TryNearestClimbObstacle(RaycastHit[] hits, int count, out RaycastHit nearest)
    {
        nearest = default;
        float distance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i]; var collider = hit.collider;
            if (!collider || !collider.enabled || collider.isTrigger || collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
            distance = hit.distance; nearest = hit; found = true;
        }
        return found;
    }

    private bool IsValidClimbHit(RaycastHit hit)
    {
        Collider hitCollider = hit.collider;
        if (hitCollider == null || !hitCollider.enabled || hitCollider.isTrigger ||
            hitCollider.transform.IsChildOf(transform) || IsNonClimbable(hitCollider))
        {
            return false;
        }

        float minimumAngle = Mathf.Clamp(climbMinimumAngle, 0f, 180f);
        float maximumAngle = Mathf.Clamp(climbMaximumAngle, minimumAngle, 180f);
        float surfaceAngle = Vector3.Angle(hit.normal, Vector3.up);
        return surfaceAngle >= minimumAngle && surfaceAngle <= maximumAngle;
    }

    private void EnterClimbing(Vector3 surfaceNormal, Vector3 surfacePoint, Collider surfaceCollider)
    {
        if (swimmingActive)
        {
            return;
        }

        climbingActive = true;
        climbHadValidSurface = true;
        climbAmount = 0f;
        climbLostTime = 0f;
        climbElapsedTime = 0f;
        climbSurfaceNormal = surfaceNormal.normalized;
        climbSurfacePoint = surfacePoint;
        currentClimbCollider = surfaceCollider;
        climbHasLeftGround = false;
        jumpPreparing = false;
        jumpInProgress = false;
        jumpAnticipationTimer = 0f;
        verticalVelocity = 0f;
        stableGrounded = false;
        ungroundedTime = 0f;
        buoyancyVelocity = 0f;
        swimAmount = 0f;

        ResetAnimatorTrigger(JumpHash);
        ResetAnimatorTrigger(ClimbTopHash);
        SetAnimatorBool(IsClimbingHash, true);
        SetAnimatorFloat(ClimbAmountHash, 0f);
        SetAnimatorBool(IsGroundedHash, false);
        SetAnimatorFloat(VerticalSpeedHash, 0f);
        SetAnimatorFloat(MoveAmountHash, 0f);
        SetAnimatorBool(IsSwimmingHash, false);
        SetAnimatorFloat(SwimAmountHash, 0f);
        animator.applyRootMotion = false;
    }

    private void UpdateClimbing(Vector2 input)
    {
        ledgeRecatchDelay = Mathf.Max(0, ledgeRecatchDelay - MotionDelta);
        if (IsJumpPressed())
        {
            ExitClimbing(false, true);
            return;
        }

        if (ledgeHanging) { UpdateLedgeHang(input); return; }
        if (hero && ledgeRecatchDelay <= 0 && input.y > .1f &&
            climbElapsedTime >= Mathf.Max(0, climbTopMinimumDetectionTime) &&
            TryFindClimbTop(out var reachableTop, out _) &&
            reachableTop.y - Mathf.Max(0, climbTopLandingHeight) - transform.position.y >= 1.45f)
        { EnterLedgeHang(reachableTop); return; }

        bool surfaceFound = TryFindClimbSurface(
            out Vector3 detectedNormal,
            out Vector3 detectedPoint,
            out Collider detectedCollider);
        if (surfaceFound)
        {
            climbHadValidSurface = true;
            climbLostTime = 0f;
            float normalBlend = 1f - Mathf.Exp(-Mathf.Max(0f, climbNormalSmoothSpeed) * MotionDelta);
            climbSurfaceNormal = Vector3.Slerp(climbSurfaceNormal, detectedNormal, normalBlend).normalized;
            climbSurfacePoint = detectedPoint;
            currentClimbCollider = detectedCollider;
        }
        else
        {
            if (IsNonClimbable(currentClimbCollider))
            {
                ExitClimbing(false, true);
                return;
            }

            climbLostTime += MotionDelta;

            bool canTryClimbTop = climbHadValidSurface &&
                                  input.y > 0.1f &&
                                  climbElapsedTime >= Mathf.Max(0f, climbTopMinimumDetectionTime) &&
                                  climbLostTime <= Mathf.Max(0f, climbSurfaceGraceTime) &&
                                  climbSurfaceNormal.sqrMagnitude > 0.0001f &&
                                  !swimmingActive;
            if (canTryClimbTop &&
                TryFindClimbTop(out Vector3 mantleTarget, out Collider landingCollider))
            {
                if (hero && ledgeRecatchDelay <= 0f) EnterLedgeHang(mantleTarget);
                else if (!hero) EnterMantling(mantleTarget, landingCollider);
                return;
            }

            if (climbLostTime > Mathf.Max(0f, climbSurfaceGraceTime))
            {
                ExitClimbing(false, true);
                return;
            }
        }

        float upInput = hero ? Mathf.Clamp(input.y,-1f,1f) : Mathf.Clamp01(input.y);
        float sideInput = hero ? Mathf.Clamp(input.x,-1f,1f) : 0f;
        Vector2 climbInput=Vector2.ClampMagnitude(new Vector2(sideInput,upInput),1f);
        upInput=climbInput.y;sideInput=climbInput.x;climbAmount = climbInput.magnitude;

        Vector3 targetForward = Vector3.ProjectOnPlane(-climbSurfaceNormal, Vector3.up);
        if (targetForward.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(targetForward.normalized, Vector3.up);
            float rotationBlend = 1f - Mathf.Exp(-Mathf.Max(0f, climbRotationSpeed) * MotionDelta);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationBlend);
        }

        float currentDistance = Vector3.Dot(transform.position - climbSurfacePoint, climbSurfaceNormal);
        float targetDistance = Mathf.Max(0f, climbWallDistance);
        float nextDistance = Mathf.MoveTowards(
            currentDistance,
            targetDistance,
            Mathf.Max(0f, climbSnapMaxSpeed) * MotionDelta);
        float correctionSpeed = MotionDelta > 0f
            ? (nextDistance - currentDistance) / MotionDelta
            : 0f;

        Vector3 finalVelocity = Vector3.up * (Mathf.Max(0f, climbSpeed) * upInput) +
                                climbSurfaceNormal * correctionSpeed +
                                Vector3.Cross(Vector3.up,-climbSurfaceNormal).normalized * (Mathf.Max(0f,climbSpeed)*sideInput);
        CollisionFlags collisionFlags = characterController.Move(finalVelocity * MotionDelta);
        climbElapsedTime += MotionDelta;

        bool collisionAbove = (collisionFlags & CollisionFlags.Above) != 0;
        bool collisionBelow = (collisionFlags & CollisionFlags.Below) != 0;
        if (!collisionBelow)
        {
            climbHasLeftGround = true;
        }
        else if (climbHasLeftGround && climbElapsedTime >= 0.1f)
        {
            ExitClimbing(true, false);
            return;
        }

        if (collisionAbove)
        {
            climbAmount = 0f;
        }

        verticalVelocity = 0f;
        stableGrounded = false;
        ungroundedTime = 0f;
        SetAnimatorBool(IsClimbingHash, true);
        SetAnimatorFloat(ClimbAmountHash, climbAmount);
        SetAnimatorBool(IsGroundedHash, false);
        SetAnimatorFloat(VerticalSpeedHash, 0f);
        SetAnimatorFloat(MoveAmountHash, 0f);
        SetAnimatorBool(IsSwimmingHash, false);
        SetAnimatorFloat(SwimAmountHash, 0f);
        animator.applyRootMotion = false;
    }

    private void EnterLedgeHang(Vector3 landingRoot)
    {
        ledgeHanging = true; ledgeHangElapsed = 0; ledgeClimbRearmed = false;
        ledgeHangStart = transform.position; ledgeHangRoot = transform.position;
        ledgeHangRoot.y = landingRoot.y - Mathf.Max(0, climbTopLandingHeight) - 1.68f;
        climbAmount = 0; verticalVelocity = 0;
    }

    private void UpdateLedgeHang(Vector2 input)
    {
        if (!currentClimbCollider || !currentClimbCollider.enabled || IsNonClimbable(currentClimbCollider))
        { ExitClimbing(false, true); return; }
        ledgeHangElapsed += MotionDelta;
        float grabDuration = hero.Library.Duration("WALL_LEDGE_GRAB");
        var desired = Vector3.Lerp(ledgeHangStart, ledgeHangRoot,
            Mathf.SmoothStep(0, 1, Mathf.Clamp01(ledgeHangElapsed / Mathf.Max(.1f, grabDuration))));
        characterController.Move(desired - transform.position);
        if (Vector3.Distance(transform.position, desired) > .12f) { ExitClimbing(false, true); return; }
        if (ledgeHangElapsed < grabDuration) return;
        if (input.y <= .1f) ledgeClimbRearmed = true;
        if (input.y < -.1f)
        { ledgeHanging = false; ledgeRecatchDelay = .4f; climbLostTime = 0; return; }
        // Require a fresh upward intent after the grab, and re-probe changed geometry.
        if (ledgeClimbRearmed && input.y > .1f &&
            TryFindClimbTop(out var target, out var landing)) EnterMantling(target, landing);
    }

    private bool TryFindClimbTop(out Vector3 targetRoot, out Collider landingCollider)
    {
        targetRoot = Vector3.zero;
        landingCollider = null;

        Vector3 wallForward = Vector3.ProjectOnPlane(-climbSurfaceNormal, Vector3.up);
        if (wallForward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        wallForward.Normalize();
        Vector3 origin = transform.position +
                         Vector3.up * Mathf.Max(0f, climbTopProbeHeight) +
                         wallForward * Mathf.Max(0f, climbTopForwardDistance);
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            climbTopHits,
            Mathf.Max(0f, climbTopDownDistance),
            climbLayers,
            QueryTriggerInteraction.Ignore);

        if (!TryNearestClimbObstacle(climbTopHits, hitCount, out var bestHit) || !IsValidClimbTopHit(bestHit, wallForward))
        {
            return false;
        }

        Vector3 landingPoint = bestHit.point + wallForward * Mathf.Max(0f, climbTopLandingInset);
        targetRoot = new Vector3(landingPoint.x, bestHit.point.y + Mathf.Max(0f, climbTopLandingHeight), landingPoint.z);
        if (!HasMantleLandingSupport(targetRoot, bestHit.point.y, wallForward) ||
            !HasMantleCapsuleClearance(targetRoot, bestHit.collider))
        {
            targetRoot = Vector3.zero;
            return false;
        }

        landingCollider = bestHit.collider;
        return true;
    }

    private bool HasMantleLandingSupport(Vector3 targetRoot, float surfaceY, Vector3 wallForward)
    {
        Vector3 scale = transform.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float footprintRadius = Mathf.Max(
            0.05f,
            characterController.radius * radiusScale - characterController.skinWidth * radiusScale);
        Vector3 wallRight = Vector3.Cross(Vector3.up, wallForward).normalized;

        for (int sampleIndex = 0; sampleIndex < 5; sampleIndex++)
        {
            Vector3 offset = Vector3.zero;
            if (sampleIndex == 1) offset = wallForward * footprintRadius;
            else if (sampleIndex == 2) offset = -wallForward * footprintRadius;
            else if (sampleIndex == 3) offset = wallRight * footprintRadius;
            else if (sampleIndex == 4) offset = -wallRight * footprintRadius;

            Vector3 origin = targetRoot + offset + Vector3.up * 0.25f;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                climbTopHits,
                0.5f,
                climbLayers,
                QueryTriggerInteraction.Ignore);
            if (!TryNearestClimbObstacle(climbTopHits, hitCount, out var support) ||
                IsNonClimbable(support.collider) ||
                Vector3.Angle(support.normal, Vector3.up) > Mathf.Clamp(climbTopMaximumSlope, 0f, 90f) ||
                Mathf.Abs(support.point.y - surfaceY) > .12f)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsValidClimbTopHit(RaycastHit hit, Vector3 wallForward)
    {
        Collider hitCollider = hit.collider;
        if (hitCollider == null || !hitCollider.enabled || hitCollider.isTrigger ||
            hitCollider.transform.IsChildOf(transform) || IsNonClimbable(hitCollider))
        {
            return false;
        }

        if (Vector3.Angle(hit.normal, Vector3.up) > Mathf.Clamp(climbTopMaximumSlope, 0f, 90f))
        {
            return false;
        }

        float minimumRise = Mathf.Max(0f, climbTopMinimumRise);
        float maximumRise = Mathf.Max(minimumRise, climbTopMaximumRise);
        float rise = hit.point.y - transform.position.y;
        if (rise < minimumRise || rise > maximumRise)
        {
            return false;
        }

        Vector3 horizontalOffset = Vector3.ProjectOnPlane(hit.point - transform.position, Vector3.up);
        float forwardOffset = Vector3.Dot(horizontalOffset, wallForward);
        if (forwardOffset <= 0f || forwardOffset > Mathf.Max(0f, climbTopForwardDistance) + 0.35f)
        {
            return false;
        }

        Vector3 wallRight = Vector3.Cross(Vector3.up, wallForward).normalized;
        float lateralOffset = Mathf.Abs(Vector3.Dot(horizontalOffset, wallRight));
        return lateralOffset <= Mathf.Max(0f, climbTopMaximumLateralOffset);
    }

    private static bool IsNonClimbable(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return false;
        }

        Transform current = hitCollider.transform;
        while (current != null)
        {
            if (current.TryGetComponent(out NonClimbableSurface _))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private bool HasMantleCapsuleClearance(Vector3 targetRoot, Collider landingCollider)
    {
        Vector3 scale = transform.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float heightScale = Mathf.Abs(scale.y);
        float radius = characterController.radius * radiusScale;
        float skinWidth = characterController.skinWidth * radiusScale;
        float clearanceRadius = Mathf.Max(0.01f, radius - skinWidth);
        float height = Mathf.Max(characterController.height * heightScale, clearanceRadius * 2f);
        Vector3 up = transform.up;
        Vector3 scaledCenter = Vector3.Scale(characterController.center, scale);
        Vector3 center = targetRoot + transform.rotation * scaledCenter;
        float halfSegment = Mathf.Max(0f, height * 0.5f - clearanceRadius);
        Vector3 bottom = center - up * halfSegment;
        Vector3 top = center + up * halfSegment;

        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            bottom,
            top,
            clearanceRadius,
            mantleClearanceHits,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider overlap = mantleClearanceHits[i];
            if (overlap == null || !overlap.enabled || overlap.isTrigger ||
                overlap == characterController || overlap.transform.IsChildOf(transform))
            {
                continue;
            }

            // The shrunken capsule may still report the supporting surface at its bottom contact.
            if (overlap == landingCollider)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private void EnterMantling(Vector3 targetPosition, Collider landingCollider)
    {
        ledgeHanging = false;
        mantlingActive = true;
        climbingActive = false;
        mantleElapsedTime = 0f;
        mantleStartPosition = transform.position;
        mantleTargetPosition = targetPosition;
        mantleApexPosition = new Vector3(
            mantleStartPosition.x,
            mantleTargetPosition.y + Mathf.Max(0f, climbTopApexHeight),
            mantleStartPosition.z);
        mantleLandingCollider = landingCollider;
        climbAmount = 0f;
        verticalVelocity = 0f;
        stableGrounded = false;
        ungroundedTime = 0f;
        jumpPreparing = false;
        jumpInProgress = false;
        jumpAnticipationTimer = 0f;

        ResetAnimatorTrigger(JumpHash);
        ResetAnimatorTrigger(ClimbTopHash);
        SetAnimatorBool(IsClimbingHash, true);
        SetAnimatorFloat(ClimbAmountHash, 0f);
        SetAnimatorBool(IsGroundedHash, false);
        SetAnimatorFloat(VerticalSpeedHash, 0f);
        SetAnimatorFloat(MoveAmountHash, 0f);
        SetAnimatorBool(IsSwimmingHash, false);
        SetAnimatorFloat(SwimAmountHash, 0f);
        SetAnimatorTrigger(ClimbTopHash);
        animator.applyRootMotion = false;
    }

    private void UpdateMantling()
    {
        if (mantleLandingCollider == null || !mantleLandingCollider.enabled || IsNonClimbable(mantleLandingCollider))
        {
            FailMantling();
            return;
        }

        if (hero && (!HasMantleLandingSupport(mantleTargetPosition,
                mantleTargetPosition.y - Mathf.Max(0, climbTopLandingHeight),
                Vector3.ProjectOnPlane(-climbSurfaceNormal, Vector3.up).normalized) ||
                !HasMantleCapsuleClearance(mantleTargetPosition, mantleLandingCollider)))
        { FailMantling(); return; }
        float duration = Mathf.Max(0.01f, hero && hero.Library ? hero.Library.Duration("WALL_LEDGE_TOP") : climbTopDuration);
        mantleElapsedTime += MotionDelta;
        float phase = Mathf.Clamp01(mantleElapsedTime / duration);
        float movementStart = Mathf.Clamp01(climbTopMovementStart);
        float verticalEnd = Mathf.Clamp(climbTopVerticalEnd, movementStart + 0.0001f, 1f);
        float movementEnd = Mathf.Clamp(climbTopMovementEnd, verticalEnd + 0.0001f, 1f);
        Vector3 desiredPosition;

        if (phase <= movementStart)
        {
            desiredPosition = mantleStartPosition;
        }
        else if (phase <= verticalEnd)
        {
            float t = Mathf.InverseLerp(movementStart, verticalEnd, phase);
            desiredPosition = Vector3.Lerp(mantleStartPosition, mantleApexPosition, Mathf.SmoothStep(0f, 1f, t));
        }
        else if (phase <= movementEnd)
        {
            float t = Mathf.InverseLerp(verticalEnd, movementEnd, phase);
            desiredPosition = Vector3.Lerp(mantleApexPosition, mantleTargetPosition, Mathf.SmoothStep(0f, 1f, t));
        }
        else
        {
            desiredPosition = mantleTargetPosition;
        }

        var motion=hero&&hero.Library?hero.Library.Find("WALL_LEDGE_TOP"):null;
        if(motion!=null&&motion.TrySampleTraversalAscent(phase,out float authoredY))
            desiredPosition.y=Mathf.Lerp(mantleStartPosition.y,mantleTargetPosition.y,authoredY);

        Vector3 delta = desiredPosition - transform.position;
        float maximumDelta = Mathf.Max(0f, climbTopMaximumMoveSpeed) * MotionDelta;
        if (maximumDelta > 0f && delta.magnitude > maximumDelta)
        {
            delta = delta.normalized * maximumDelta;
        }

        characterController.Move(delta);
        verticalVelocity = 0f;
        stableGrounded = false;
        ungroundedTime = 0f;
        SetAnimatorBool(IsClimbingHash, true);
        SetAnimatorFloat(ClimbAmountHash, 0f);
        SetAnimatorBool(IsGroundedHash, false);
        SetAnimatorFloat(VerticalSpeedHash, 0f);
        SetAnimatorFloat(MoveAmountHash, 0f);
        SetAnimatorBool(IsSwimmingHash, false);
        SetAnimatorFloat(SwimAmountHash, 0f);
        animator.applyRootMotion = false;

        if (phase < 1f)
        {
            return;
        }

        if (Vector3.Distance(transform.position, mantleTargetPosition) <= MantleCompletionTolerance)
        {
            CompleteMantling();
        }
        else
        {
            FailMantling();
        }
    }

    private void CompleteMantling()
    {
        ResetMantlingRuntimeState(false);
        climbHadValidSurface = false;
        climbRequiresInputRelease = false;
        climbReentryCooldown = ClimbReentryCooldownDuration;
        stableGrounded = true;
        ungroundedTime = 0f;
        verticalVelocity = groundedVerticalVelocity;
        SetAnimatorBool(IsClimbingHash, false);
        SetAnimatorFloat(ClimbAmountHash, 0f);
        ResetAnimatorTrigger(ClimbTopHash);
        SetAnimatorBool(IsGroundedHash, true);
        SetAnimatorFloat(VerticalSpeedHash, groundedVerticalVelocity);
        SetAnimatorFloat(MoveAmountHash, 0f);
        animator.applyRootMotion = false;
    }

    private void FailMantling()
    {
        ResetMantlingRuntimeState(false);
        climbReentryCooldown = ClimbReentryCooldownDuration;
        ExitClimbing(false, true);
    }

    private void ResetMantlingRuntimeState(bool resetAnimator)
    {
        mantlingActive = false;
        mantleElapsedTime = 0f;
        mantleStartPosition = Vector3.zero;
        mantleTargetPosition = Vector3.zero;
        mantleApexPosition = Vector3.zero;
        mantleLandingCollider = null;

        if (resetAnimator && animator != null)
        {
            ResetAnimatorTrigger(ClimbTopHash);
            SetAnimatorBool(IsClimbingHash, false);
            SetAnimatorFloat(ClimbAmountHash, 0f);
            animator.applyRootMotion = false;
        }
    }

    private void ExitClimbing(bool grounded, bool requireInputRelease)
    {
        ledgeHanging = false; ledgeHangElapsed = 0;
        climbingActive = false;
        climbHadValidSurface = false;
        climbRequiresInputRelease = requireInputRelease;
        climbAmount = 0f;
        climbLostTime = 0f;
        climbElapsedTime = 0f;
        climbSurfaceNormal = Vector3.forward;
        climbSurfacePoint = Vector3.zero;
        currentClimbCollider = null;
        climbHasLeftGround = false;
        jumpPreparing = false;
        jumpInProgress = false;
        jumpAnticipationTimer = 0f;
        ungroundedTime = 0f;
        stableGrounded = grounded;
        verticalVelocity = grounded ? groundedVerticalVelocity : 0f;

        if (animator != null)
        {
            SetAnimatorBool(IsClimbingHash, false);
            SetAnimatorFloat(ClimbAmountHash, 0f);
            SetAnimatorBool(IsGroundedHash, grounded);
            SetAnimatorFloat(VerticalSpeedHash, verticalVelocity);
            ResetAnimatorTrigger(ClimbTopHash);
            animator.applyRootMotion = false;
        }
    }

    private void ResetClimbingRuntimeState(bool resetAnimator)
    {
        authorJumpIndex=0;authorJumpAge=0;
        ledgeHanging = false; ledgeHangElapsed = 0;
        climbingActive = false;
        climbHadValidSurface = false;
        climbRequiresInputRelease = false;
        climbAmount = 0f;
        climbLostTime = 0f;
        climbElapsedTime = 0f;
        climbSurfaceNormal = Vector3.forward;
        climbSurfacePoint = Vector3.zero;
        currentClimbCollider = null;
        climbHasLeftGround = false;

        if (resetAnimator && animator != null)
        {
            SetAnimatorBool(IsClimbingHash, false);
            SetAnimatorFloat(ClimbAmountHash, 0f);
            ResetAnimatorTrigger(ClimbTopHash);
            animator.applyRootMotion = false;
        }
    }

    // Uses a cached hit buffer and rejects the Player hierarchy so the probe allocates no per-frame garbage.
    private bool ProbeGround(out Vector3 hitNormal, out float hitAngle)
    {
        Vector3 up = transform.up;
        Vector3 scale = transform.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float heightScale = Mathf.Abs(scale.y);
        float capsuleRadius = characterController.radius * radiusScale;
        float capsuleHeight = Mathf.Max(characterController.height * heightScale, capsuleRadius * 2f);
        float probeRadius = capsuleRadius * Mathf.Clamp(groundProbeRadiusScale, 0.1f, 1f);
        float originLift = characterController.skinWidth + 0.01f;
        Vector3 worldCenter = transform.TransformPoint(characterController.center);
        Vector3 bottomSphereCenter = worldCenter - up * (capsuleHeight * 0.5f - capsuleRadius);
        Vector3 origin = bottomSphereCenter + up * originLift;
        float castDistance = Mathf.Max(0f, groundProbeDistance) + originLift;

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            probeRadius,
            -up,
            groundProbeHits,
            castDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool foundGround = false;
        hitNormal = up;
        hitAngle = 0f;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundProbeHits[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            float angle = Vector3.Angle(hit.normal, up);
            if (angle > characterController.slopeLimit + GroundAngleTolerance || hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            hitNormal = hit.normal;
            hitAngle = angle;
            foundGround = true;
        }

        if (foundGround)
        {
            groundNormal = hitNormal;
            groundAngle = hitAngle;
        }

        return foundGround;
    }

    // Active jumps bypass grace; ordinary contact gaps must persist before locomotion becomes airborne.
    private void UpdateStableGrounded(bool hasGroundContact, bool activeJump)
    {
        if (activeJump)
        {
            stableGrounded = false;
            ungroundedTime = 0f;
            return;
        }

        if (hasGroundContact)
        {
            stableGrounded = true;
            ungroundedTime = 0f;
            return;
        }

        ungroundedTime += MotionDelta;
        stableGrounded = ungroundedTime < Mathf.Max(0f, groundedGraceTime);
    }

    private void Respawn()
    {
        Relocate(initialWorldPosition, initialWorldRotation);
    }

    // Save restores and room transitions reset transient movement without changing abilities.
    public void Relocate(Vector3 position, Quaternion rotation)
    {
        stairSupportTimer=0;walkableStepAhead=false;
        InvalidateGroundContact();
        if(hero)hero.CancelForTransition();
        if(battleActions)battleActions.Cancel();
        SprintingActive = false;
        externalInputLock=false;actionLocks.Clear();
        ClearImpact();
        characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);

        verticalVelocity = groundedVerticalVelocity;
        jumpPreparing = false;
        jumpInProgress = false;
        jumpAnticipationTimer = 0f;
        ungroundedTime = 0f;
        stableGrounded = true;
        groundNormal = Vector3.up;
        groundAngle = 0f;
        ClearWaterState();
        ResetSwimmingRuntimeState();
        ResetMantlingRuntimeState(true);
        ResetClimbingRuntimeState(true);
        SetAnimatorFloat(MoveAmountHash, 0f);
        SetAnimatorBool(IsSwimmingHash, false);
        SetAnimatorFloat(SwimAmountHash, 0f);
        SetAnimatorFloat(VerticalSpeedHash, verticalVelocity);
        SetAnimatorBool(IsGroundedHash, true);
        ResetAnimatorTrigger(JumpHash);
        animator.applyRootMotion = false;

        characterController.enabled = true;
        if (vitals) vitals.ResetTraversal();
        if(hero)hero.ResetLocomotionAfterRelocation();
    }

    private void OnEnable()
    {
        InvalidateGroundContact();
        externalInputLock=false;actionLocks.Clear();
        ClearImpact();
        ClearWaterState();
        ResetSwimmingRuntimeState();
        ResetMantlingRuntimeState(true);
        ResetClimbingRuntimeState(true);
    }

    private void OnDisable()
    {
        InvalidateGroundContact();
        externalInputLock=false;actionLocks.Clear();
        ClearImpact();
        ClearWaterState();
        ResetSwimmingRuntimeState();
        ResetMantlingRuntimeState(true);
        ResetClimbingRuntimeState(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterWaterVolume(other);
    }

    private void OnTriggerStay(Collider other)
    {
        RegisterWaterVolume(other);
    }

    private void OnTriggerExit(Collider other)
    {
        WaterVolume volume = other.GetComponent<WaterVolume>();
        if (volume == null)
        {
            return;
        }

        for (int i = 0; i < overlappingWaterVolumes.Length; i++)
        {
            if (overlappingWaterVolumes[i] == volume)
            {
                overlappingWaterVolumes[i] = null;
            }
        }

        RefreshWaterState();
    }

    private void RegisterWaterVolume(Collider other)
    {
        WaterVolume volume = other.GetComponent<WaterVolume>();
        if (volume == null || !volume.IsValid || volume.VolumeCollider != other)
        {
            return;
        }

        int freeIndex = -1;
        for (int i = 0; i < overlappingWaterVolumes.Length; i++)
        {
            if (overlappingWaterVolumes[i] == volume)
            {
                return;
            }

            if (freeIndex < 0 && overlappingWaterVolumes[i] == null)
            {
                freeIndex = i;
            }
        }

        if (freeIndex >= 0)
        {
            overlappingWaterVolumes[freeIndex] = volume;
        }
    }

    // Chooses the deepest valid overlap and removes disabled or destroyed volumes without allocations.
    private void RefreshWaterState()
    {
        WaterVolume bestVolume = null;
        float bestDepth = float.NegativeInfinity;

        for (int i = 0; i < overlappingWaterVolumes.Length; i++)
        {
            WaterVolume volume = overlappingWaterVolumes[i];
            if (volume == null || !volume.IsValid)
            {
                overlappingWaterVolumes[i] = null;
                continue;
            }

            if (!volume.ContainsSurfaceXZ(transform.position)) continue;
            float depth = volume.WaterSurfaceY - transform.position.y;
            if (depth > bestDepth)
            {
                bestDepth = depth;
                bestVolume = volume;
            }
        }

        if (bestVolume == null || bestDepth <= 0f)
        {
            ResetWaterRuntimeState();
            return;
        }

        currentWaterVolume = bestVolume;
        waterSurfaceY = bestVolume.WaterSurfaceY;
        waterDepth = bestDepth;
        isInWater = true;
        isDeepWater = waterDepth >= Mathf.Max(0f, deepWaterThreshold);
        isWading = waterDepth >= Mathf.Max(0f, wadingMinimumDepth) && !isDeepWater;
    }

    private void ClearWaterState()
    {
        for (int i = 0; i < overlappingWaterVolumes.Length; i++)
        {
            overlappingWaterVolumes[i] = null;
        }

        ResetWaterRuntimeState();
    }

    private void ResetWaterRuntimeState()
    {
        currentWaterVolume = null;
        waterSurfaceY = 0f;
        waterDepth = 0f;
        isInWater = false;
        isWading = false;
        isDeepWater = false;
    }

    private void ResetSwimmingRuntimeState()
    {
        authorJumpIndex=0;authorJumpAge=authorSwimDepth=AuthorSwimVertical=0;
        swimmingActive = false;
        swimAmount = 0f;
        buoyancyVelocity = 0f;

        if (animator != null)
        {
            SetAnimatorBool(IsSwimmingHash, false);
            SetAnimatorFloat(SwimAmountHash, 0f);
            animator.applyRootMotion = false;
        }
    }

    private static Vector2 ReadMovementInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (WorldInputBindings.Held("Left")) horizontal -= 1f;
        if (WorldInputBindings.Held("Right")) horizontal += 1f;
        if (WorldInputBindings.Held("Back")) vertical -= 1f;
        if (WorldInputBindings.Held("Forward")) vertical += 1f;

        return Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
    }

    private static bool IsRunHeld()
    {
        Keyboard keyboard = Keyboard.current;
        return WorldInputBindings.Held("Sprint");
    }

    // Re-enter ordinary physics without carrying pre-traversal jump/ground history.
    public bool HasImmediateFootSupport()
    {
        float bottom=characterController.center.y-characterController.height*.5f;
        Vector3 origin=transform.TransformPoint(new Vector3(characterController.center.x,bottom,characterController.center.z))+transform.up*.10f;
        int count=Physics.RaycastNonAlloc(origin,-transform.up,groundProbeHits,.22f,groundLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
        {
            var hit=groundProbeHits[i];
            if(hit.collider&&!hit.collider.transform.IsChildOf(transform)&&Vector3.Angle(hit.normal,transform.up)<=characterController.slopeLimit+GroundAngleTolerance)return true;
        }
        return false;
    }
    // Death needs current physical support, including the capsule rim at ledges.
    // Keep the center-ray policy unchanged for ordinary movement and traversal.
    public bool HasImmediateDeathSupport()
    {
        if(!characterController.enabled||!characterController.isGrounded)return false;
        float bottom=characterController.center.y-characterController.height*.5f;
        float radius=characterController.radius;
        Vector3 origin=transform.TransformPoint(new Vector3(characterController.center.x,bottom+radius,characterController.center.z))+transform.up*.10f;
        int count=Physics.SphereCastNonAlloc(origin,radius,-transform.up,groundProbeHits,.22f,groundLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
        {
            var hit=groundProbeHits[i];
            if(hit.collider&&!hit.collider.transform.IsChildOf(transform)&&Vector3.Dot(hit.normal,transform.up)>.01f)return true;
        }
        return false;
    }
    public void RefreshAfterExternalTraversal()
    {
        InvalidateGroundContact();
        // isGrounded describes the last Move, which may precede a traversal exit.
        stableGrounded=ProbeGround(out _,out _);
        ungroundedTime=stableGrounded?0:groundedGraceTime;
        verticalVelocity=groundedVerticalVelocity;jumpPreparing=jumpInProgress=false;jumpAnticipationTimer=0;
        ResetAnimatorTrigger(JumpHash);SetAnimatorBool(IsGroundedHash,stableGrounded);
        SetAnimatorFloat(VerticalSpeedHash,verticalVelocity);
    }
    private int consumedJumpFrame=-1;
    public void ConsumeJumpInputForFrame(){consumedJumpFrame=Time.frameCount;}
    private bool IsJumpPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return consumedJumpFrame!=Time.frameCount && WorldInputBindings.Pressed("Jump");
    }

    private Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;

        if (mainCamera != null)
        {
            forward = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(mainCamera.transform.right, Vector3.up).normalized;

            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        }

        Vector3 direction = forward * input.y + right * input.x;
        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }
}
