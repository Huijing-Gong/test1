using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(Animator))]
public sealed class PlayerController2D : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float groundSprintMultiplier = 1.3f;
    [SerializeField] private float jumpSpeed = 9f;
    [SerializeField] private float dashSpeed = 8.5f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private LayerMask groundMask = 1;

    private Rigidbody2D body;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private BoxCollider2D bodyCollider;
    private float moveInput;
    private string currentAnimation;
    private int jumpsUsed;
    private bool isGrounded;
    private Vector3 spawnPoint;
    private float attackLockUntil;
    private int airDashesUsed;
    private bool isDashing;
    private float dashEndTime;
    private float dashDirection;
    private float normalGravityScale;
    private float knockbackUntil;
    private bool groundSprintHeld;
    private Sprite dashWindSprite;
    private GameObject dashWindObject;
    private float tripleJumpUntil;
    private Object movingPlatformSource;
    private Vector2 movingPlatformVelocity;
    private string attackAnimationName = "skill3";
    private Collider2D groundSurface;
    private float nextFootstepTime;
    private PhysicsMaterial2D edgeSlideMaterial;
    private BoxCollider2D[] archerPlatformColliders;
    private float fallLimitY = -7f;

    public float TutorialDashSpeed => dashSpeed;
    public float TutorialDashDuration => dashDuration;
    public float TutorialJumpSpeed => jumpSpeed;

    public float FacingDirection => spriteRenderer != null && spriteRenderer.flipX ? -1f : 1f;
    public float TripleJumpSecondsRemaining => Mathf.Max(0f, tripleJumpUntil - Time.time);
    public bool HasTripleJump => TripleJumpSecondsRemaining > 0f;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<BoxCollider2D>();
        RepairMisplacedWalkableTopCollider();
        // Zero friction prevents the player from hanging on the vertical edge of a platform.
        edgeSlideMaterial = new PhysicsMaterial2D("Player Edge Slide")
        {
            friction = 0f,
            bounciness = 0f
        };
        bodyCollider.sharedMaterial = edgeSlideMaterial;
        archerPlatformColliders = ConfigureArcherPlatformsAsOneWay();
        fallLimitY = CalculateFallLimitY();
        spawnPoint = transform.position;
        normalGravityScale = body.gravityScale;
        dashWindSprite = Resources.Load<Sprite>("DashWind");
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) { moveInput = 0f; return; }
        if (isDashing && Time.time >= dashEndTime)
            EndDash();

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            moveInput = 0f;
            return;
        }

        moveInput = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            moveInput -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            moveInput += 1f;

        if (moveInput != 0f)
            spriteRenderer.flipX = moveInput < 0f;

        isGrounded = CheckGrounded();
        if (isGrounded && body.linearVelocity.y <= 0.05f)
        {
            jumpsUsed = 0;
            airDashesUsed = 0;
        }

        bool jumpPressed = keyboard.spaceKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame ||
                           keyboard.upArrowKey.wasPressedThisFrame;
        if (jumpPressed && jumpsUsed < (HasTripleJump ? 3 : 2))
        {
            GameAudio.Play(GameSound.Jump);
            body.linearVelocity = new Vector2(body.linearVelocity.x, jumpSpeed);
            jumpsUsed++;
            isGrounded = false;
        }

        bool dashPressed = keyboard.leftShiftKey.wasPressedThisFrame ||
                           keyboard.rightShiftKey.wasPressedThisFrame;
        groundSprintHeld = isGrounded &&
            (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        int maximumAirDashes = HasTripleJump ? 3 : 2;
        if (dashPressed && !isGrounded && !isDashing &&
            airDashesUsed < jumpsUsed && airDashesUsed < maximumAirDashes)
            BeginDash();

        UpdateAnimation();
        UpdateFootsteps();

        if (transform.position.y < fallLimitY)
            HandleFall();
    }

    private void FixedUpdate()
    {
        UpdateArcherPlatformCollision();
        if (isDashing)
        {
            body.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);
            return;
        }
        if (Time.time < knockbackUntil)
            return;
        float currentMoveSpeed = moveSpeed * (groundSprintHeld ? groundSprintMultiplier : 1f);
        float verticalVelocity = body.linearVelocity.y;
        if (movingPlatformSource != null && verticalVelocity <= 0.15f)
            verticalVelocity = movingPlatformVelocity.y;
        body.linearVelocity = new Vector2(moveInput * currentMoveSpeed + movingPlatformVelocity.x, verticalVelocity);
    }

    public void SetMovingPlatformMotion(Object source, Vector2 velocity, bool standing)
    {
        if (source == null) return;
        if (standing)
        {
            movingPlatformSource = source;
            movingPlatformVelocity = velocity;
        }
        else if (movingPlatformSource == source)
        {
            movingPlatformSource = null;
            movingPlatformVelocity = Vector2.zero;
        }
    }

    private bool CheckGrounded()
    {
        Bounds bounds = bodyCollider.bounds;
        Vector2 checkCenter = new(bounds.center.x, bounds.min.y - 0.04f);
        Vector2 checkSize = new(bounds.size.x * 0.75f, 0.08f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(checkCenter, checkSize, 0f, groundMask);
        foreach (Collider2D hit in hits)
        {
            if (hit != bodyCollider && !hit.isTrigger)
            {
                groundSurface = hit;
                return true;
            }
        }
        groundSurface = null;
        return false;
    }

    private void UpdateAnimation()
    {
        if (Time.time < attackLockUntil)
            return;

        string nextAnimation;
        if (!isGrounded)
            nextAnimation = jumpsUsed >= 2 ? "double jump" : "jump";
        else if (Mathf.Abs(moveInput) > 0.01f)
            nextAnimation = "run";
        else
            nextAnimation = "idle";

        if (nextAnimation == currentAnimation)
            return;

        currentAnimation = nextAnimation;
        animator.Play(currentAnimation);
    }

    public void PlayAttack(float duration)
    {
        attackLockUntil = Time.time + duration;
        currentAnimation = attackAnimationName;
        animator.Play(currentAnimation, 0, 0f);
    }

    public void SetAttackAnimationName(string stateName)
    {
        attackAnimationName = string.IsNullOrEmpty(stateName) ? "skill3" : stateName;
    }

    public void ActivateTripleJump(float duration)
    {
        float start = Mathf.Max(Time.time, tripleJumpUntil);
        tripleJumpUntil = start + duration;
    }

    public void PlayHurt(float duration)
    {
        attackLockUntil = Time.time + duration;
        currentAnimation = "hurt";
        animator.Play(currentAnimation, 0, 0f);
    }

    public void Die()
    {
        EndDash();
        moveInput = 0f;
        body.linearVelocity = Vector2.zero;
        currentAnimation = "die";
        animator.Play(currentAnimation, 0, 0f);
        enabled = false;
    }

    public void ApplyKnockback(float horizontalDirection, float horizontalForce, float verticalForce)
    {
        EndDash();
        float direction = Mathf.Sign(horizontalDirection);
        if (direction == 0f)
            direction = -FacingDirection;
        knockbackUntil = Time.time + 0.22f;
        body.linearVelocity = new Vector2(direction * horizontalForce, verticalForce);
    }

    private void BeginDash()
    {
        GameAudio.Play(GameSound.Dash);
        isDashing = true;
        airDashesUsed++;
        dashDirection = FacingDirection;
        dashEndTime = Time.time + dashDuration;
        attackLockUntil = dashEndTime;
        body.gravityScale = 0f;
        body.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);
        currentAnimation = "dash";
        animator.Play(currentAnimation, 0, 0f);
        ShowDashWind();
    }

    private void UpdateFootsteps()
    {
        if (!isGrounded || isDashing || Mathf.Abs(moveInput) < 0.1f || Time.time < nextFootstepTime)
            return;
        nextFootstepTime = Time.time + (groundSprintHeld ? 0.26f : 0.36f);
        GameAudio.PlayFootstep(groundSurface);
    }

    private void EndDash()
    {
        if (!isDashing)
            return;
        isDashing = false;
        body.gravityScale = normalGravityScale;
        if (dashWindObject != null)
            Destroy(dashWindObject);
    }

    private void ShowDashWind()
    {
        if (dashWindSprite == null)
            return;

        dashWindObject = new GameObject("Dash Wind");
        dashWindObject.transform.SetParent(transform, false);
        dashWindObject.transform.localPosition = new Vector3(-dashDirection * 0.58f, -0.02f, 0f);
        dashWindObject.transform.localScale = Vector3.one * 0.45f;
        SpriteRenderer windRenderer = dashWindObject.AddComponent<SpriteRenderer>();
        windRenderer.sprite = dashWindSprite;
        windRenderer.sortingOrder = 9;
        // The supplied PNG faces left. Flip it only when dashing right.
        windRenderer.flipX = dashDirection > 0f;
    }

    private void Respawn()
    {
        transform.position = spawnPoint;
        body.linearVelocity = Vector2.zero;
        jumpsUsed = 0;
    }

    private void OnDestroy()
    {
        if (edgeSlideMaterial != null)
            Destroy(edgeSlideMaterial);
    }

    private static BoxCollider2D[] ConfigureArcherPlatformsAsOneWay()
    {
        System.Collections.Generic.List<BoxCollider2D> configured = new();
        foreach (BoxCollider2D platform in FindObjectsByType<BoxCollider2D>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!platform.name.StartsWith("Archer Stone Platform") || platform.isTrigger)
                continue;

            PlatformEffector2D effector = platform.GetComponent<PlatformEffector2D>();
            if (effector == null)
                effector = platform.gameObject.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.useOneWayGrouping = true;
            effector.useSideFriction = false;
            effector.surfaceArc = 160f;
            platform.usedByEffector = true;
            configured.Add(platform);
        }
        return configured.ToArray();
    }

    private float CalculateFallLimitY()
    {
        float lowestPlatformPoint = float.MaxValue;
        foreach (Collider2D candidate in FindObjectsByType<Collider2D>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (candidate == null || candidate.gameObject.scene != gameObject.scene || candidate.isTrigger || !candidate.enabled)
                continue;
            string fullName = FullTransformName(candidate.transform);
            if (!fullName.Contains("Platform") && !fullName.Contains("Bridge"))
                continue;
            lowestPlatformPoint = Mathf.Min(lowestPlatformPoint, candidate.bounds.min.y);
        }
        return lowestPlatformPoint < float.MaxValue ? lowestPlatformPoint - 3f : -7f;
    }

    private static string FullTransformName(Transform item)
    {
        string value = item != null ? item.name : string.Empty;
        while (item != null && item.parent != null)
        {
            item = item.parent;
            value = item.name + "/" + value;
        }
        return value;
    }

    private static void RepairMisplacedWalkableTopCollider()
    {
        GameObject walkableObject = GameObject.Find("Walkable Top Collider");
        if (walkableObject == null || walkableObject.transform.parent == null)
            return;

        Transform visiblePlatform = walkableObject.transform.parent.Find("Static Platform And Ropes");
        if (visiblePlatform == null)
            return;

        Vector3 colliderPosition = walkableObject.transform.localPosition;
        colliderPosition.x = visiblePlatform.localPosition.x;
        colliderPosition.y = visiblePlatform.localPosition.y + 0.02f;
        walkableObject.transform.localPosition = colliderPosition;
    }

    private void UpdateArcherPlatformCollision()
    {
        if (archerPlatformColliders == null) return;
        Bounds playerBounds = bodyCollider.bounds;
        foreach (BoxCollider2D platform in archerPlatformColliders)
        {
            if (platform == null) continue;
            Bounds platformBounds = platform.bounds;
            // The platform catches a descending player only while their feet are above
            // its top. At the side or underneath it has no invisible vertical wall.
            bool feetAboveTop = playerBounds.min.y >= platformBounds.max.y - 0.04f;
            bool horizontallyOverPlatform = playerBounds.max.x > platformBounds.min.x + 0.03f &&
                                            playerBounds.min.x < platformBounds.max.x - 0.03f;
            bool shouldCollide = feetAboveTop && horizontallyOverPlatform && body.linearVelocity.y <= 0.15f;
            Physics2D.IgnoreCollision(bodyCollider, platform, !shouldCollide);
        }
    }

    private void HandleFall()
    {
        float fallX = transform.position.x;
        PlayerHealth health = GetComponent<PlayerHealth>();
        if (health != null)
        {
            health.TakeDamage(30);
            if (health.IsDead)
                return;
        }

        RespawnOnNearestPlatform(fallX);
    }

    private void RespawnOnNearestPlatform(float fallX)
    {
        Collider2D nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (Collider2D candidate in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (candidate == bodyCollider || candidate.isTrigger)
                continue;
            if (!candidate.name.Contains("Platform") && !candidate.name.Contains("Bridge"))
                continue;

            Bounds bounds = candidate.bounds;
            float closestX = Mathf.Clamp(fallX, bounds.min.x, bounds.max.x);
            float distance = Mathf.Abs(fallX - closestX);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        if (nearest == null)
        {
            Respawn();
            return;
        }

        Bounds platformBounds = nearest.bounds;
        float safeX = Mathf.Clamp(fallX, platformBounds.min.x + 0.3f, platformBounds.max.x - 0.3f);
        float feetOffset = bodyCollider.offset.y - bodyCollider.size.y * 0.5f;
        transform.position = new Vector3(safeX, platformBounds.max.y - feetOffset + 0.04f, spawnPoint.z);
        body.linearVelocity = Vector2.zero;
        jumpsUsed = 0;
    }

}
