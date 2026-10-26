using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private float targetHeight = 1.6f;

    [Header("Orbit")]
    [SerializeField] private float distance = 6f;
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float minimumPitch = -20f;
    [SerializeField] private float maximumPitch = 65f;

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.08f;
    [SerializeField, Min(0.01f)] private float heightSmoothTime = 0.14f;

    [SerializeField, Min(0.01f)] private float stairHeightSmoothTime = 0.30f;
    private float stairFollowRemaining;

    [Header("Collision")]
    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private float collisionBuffer = 0.15f;
    [SerializeField] private float minimumDistance = 1f;
    [SerializeField] private LayerMask collisionLayers = ~0;
    [SerializeField] private float collisionReturnSmoothTime = 0.15f;
    [SerializeField, Min(0f)] private float collisionAnticipationPadding = 0.6f;
    [SerializeField, Min(0.01f)] private float groundPitchSmoothTime = 0.2f;

    [Header("Hit Feedback")]
    [SerializeField, Min(1f)] private float shakeFrequency = 28f;

    [SerializeField] private float recenterPitch = 20f;
    private float yaw;
    private bool recentering;
    private float recenterYaw, recenterYawVelocity, recenterPitchVelocity;
    private Health lockedEnemy;
    private KingOfGraphic.Hero.HeroWorldAdapter focusHero;
    private float occludedTime;
    public Transform LockedEnemy => lockedEnemy && !lockedEnemy.IsDead && lockedEnemy.isActiveAndEnabled ? lockedEnemy.transform : null;
    private bool CombatLookAvailable => !IsLookInputLocked && !CinematicControlled && Time.timeScale > 0f &&
        Cursor.lockState == CursorLockMode.Locked && targetMovement &&
        (!targetFlow || (targetFlow.Started && !targetFlow.WorldHudHidden)) &&
        (!targetMovement.IsGameplayInputLocked || (targetCombat && targetCombat.IsAttacking) || (focusHero&&focusHero.FocusSequenceActive));

    private Vector3 EnemyAim(Health enemy)
    {
        var body = enemy.GetComponentInChildren<Collider>();
        return body && body.enabled ? body.bounds.center : enemy.transform.position + Vector3.up;
    }

    private bool EnemyVisible(Health enemy)
    {
        Vector3 start = target.position + Vector3.up * targetHeight;
        Vector3 offset = EnemyAim(enemy) - start;
        foreach (var hit in Physics.RaycastAll(start, offset.normalized, offset.magnitude, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<CameraCollisionPassthrough>() ||
                hit.transform.IsChildOf(target) || hit.collider.GetComponentInParent<Health>() == enemy) continue;
            return false;
        }
        return true;
    }

    private void UpdateLockOn()
    {
        // Target framing is temporary and belongs only to the focus sequence.
        focusHero=targetMovement?targetMovement.GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>():null;
        lockedEnemy=focusHero&&focusHero.FocusSequenceActive?focusHero.FocusEnemy:null;occludedTime=0f;
    }

    private void OnGUI()
    {
        if (!LockedEnemy || !CombatLookAvailable) return;
        Vector3 point = cachedCamera.WorldToScreenPoint(EnemyAim(lockedEnemy));
        if (point.z <= 0f) return;
        Color previous = GUI.color;
        GUI.color = new Color(1f, .85f, .42f);
        float x = point.x, y = Screen.height - point.y;
        GUI.DrawTexture(new Rect(x - 8, y - 8, 16, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 8, y + 6, 16, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 8, y - 6, 2, 12), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x + 6, y - 6, 2, 12), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private float pitch;
    private float currentCameraDistance;
    private float collisionDistanceVelocity;
    private Vector3 positionSmoothVelocity;
    private float followHeight, followHeightVelocity;
    private bool followHeightInitialized;
    private Vector3 lastTargetPosition;
    private Vector3 followFocus, followFocusVelocity;
    private Camera cachedCamera;
    private PlayerMovement targetMovement;
    private PlayerCombat targetCombat;
    private BaselineFlow targetFlow;
    private OverworldV2Layout cameraGroundLayout;
    private Renderer[] closeTargetRenderers;
    private bool[] closeTargetRenderingFlags;
    private bool closeTargetHidden;
    private float groundPitchOffset, groundPitchVelocity, heldGroundPitch, groundPitchHoldUntil;
    private readonly RaycastHit[] groundCameraHits = new RaycastHit[24];
    private readonly RaycastHit[] anticipationHits = new RaycastHit[32];
    private readonly RaycastHit[] collisionHits = new RaycastHit[16];
    private SphereCollider cameraClearanceProbe;
    private readonly Collider[] cameraClearanceOverlaps = new Collider[48];
    private float shakeAmplitude;
    private float shakeDuration;
    private float shakeTimeRemaining;
    private float shakePhase;
    private Vector3 appliedShakeOffset;
    public bool IsLookInputLocked { get; private set; }
    public bool CinematicControlled { get; private set; }
    public void SetCinematicControl(bool controlled)
    {
        if (CinematicControlled && !controlled) ResetFollowHeight();
        if (controlled) RestoreCloseTargetVisibility();
        CinematicControlled = controlled;
    }
    public bool IsShaking => shakeTimeRemaining > 0f;
    public int ShakeRequestCount { get; private set; }

    public void SetLookInputLocked(bool locked)
    {
        IsLookInputLocked = locked;
    }

    // Repositioning a new journey must also reset the old camera orbit and smoothing.
    public void ResetOrbit(float heading, float elevation)
    {
        recentering=false;lockedEnemy=null;occludedTime=0;
        RestoreCloseTargetVisibility();
        yaw = heading; pitch = Mathf.Clamp(elevation, minimumPitch, maximumPitch);
        currentCameraDistance = distance; collisionDistanceVelocity = 0;
        positionSmoothVelocity = Vector3.zero; appliedShakeOffset = Vector3.zero;
        ResetFollowHeight();
        if (!target) return;
        var rotation = Quaternion.Euler(pitch,yaw,0);
        transform.SetPositionAndRotation(target.position + Vector3.up*targetHeight - rotation*Vector3.forward*distance,rotation);
    }

    private Vector3 hitImpulseDirection;
    public void RequestHitImpulse(Vector3 attackDirection, float amplitude, float duration)
    {
        RequestShake(amplitude, duration);
        hitImpulseDirection = -attackDirection.normalized;
    }

    public void RequestShake(float amplitude, float duration)
    {
        float safeAmplitude = Mathf.Clamp(amplitude, 0f, 0.25f);
        float safeDuration = Mathf.Clamp(duration, 0f, 0.5f);
        if (safeAmplitude <= 0f || safeDuration <= 0f)
        {
            return;
        }

        ShakeRequestCount++;
        if (shakeTimeRemaining <= 0f)
        {
            shakePhase = 0f;
            hitImpulseDirection = Vector3.zero;
        }

        shakeAmplitude = Mathf.Max(shakeAmplitude, safeAmplitude);
        shakeDuration = Mathf.Max(shakeDuration, safeDuration);
        shakeTimeRemaining = Mathf.Max(shakeTimeRemaining, safeDuration);
    }

    private void OnEnable()
    {
        followHeightInitialized = false;
        IsLookInputLocked = false;
    }

    private void OnDisable()
    {
        recentering=false;lockedEnemy=null;occludedTime=0;
        RestoreCloseTargetVisibility();
        CinematicControlled = false;
        IsLookInputLocked = false;
        if (appliedShakeOffset.sqrMagnitude > 0f)
        {
            transform.position -= appliedShakeOffset;
        }
        appliedShakeOffset = Vector3.zero;
        shakeAmplitude = 0f;
        shakeDuration = 0f;
        shakeTimeRemaining = 0f;
        shakePhase = 0f;
    }

    private void OnDestroy()
    {
        if (cameraClearanceProbe) Destroy(cameraClearanceProbe.gameObject);
    }

    private void Awake()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                target = player.transform;
            }
        }

        if (target == null)
        {
            Debug.LogError("ThirdPersonCamera could not find a target. Assign the Player Transform or tag the Player GameObject as 'Player'.", this);
            enabled = false;
            return;
        }

        cachedCamera = GetComponent<Camera>();
        targetMovement = target.GetComponentInParent<PlayerMovement>();
        targetCombat = target.GetComponentInParent<PlayerCombat>();
        targetFlow = FindFirstObjectByType<BaselineFlow>();
        // Cache the authored main-world ground set once; other scenes keep their original sampling.
        foreach (var layout in FindObjectsByType<OverworldV2Layout>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (layout.gameObject.scene != target.gameObject.scene) continue;
            cameraGroundLayout = layout;
            break;
        }
        if (cameraGroundLayout)
        {
            closeTargetRenderers = target.GetComponentsInChildren<Renderer>(true);
            closeTargetRenderingFlags = new bool[closeTargetRenderers.Length];
        }
        currentCameraDistance = distance;
        InitializeOrbitAngles();
    }

    private void Start()
    {
        SetCursorLocked(true);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (!IsLookInputLocked && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
        }
        else if (!IsLookInputLocked && !WorldInputBindings.GameplaySuppressed && mouse != null && Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
        {
            SetCursorLocked(true);
        }
    }

    private void LateUpdate()
    {
        if (CinematicControlled) return;
        UpdateLockOn();
        Vector3 unshakenPosition = transform.position - appliedShakeOffset;
        appliedShakeOffset = Vector3.zero;

        bool recenter = CombatLookAvailable && Time.timeScale > 0f &&
            Cursor.lockState == CursorLockMode.Locked && target &&
            WorldInputBindings.Pressed("Recenter");
        if (recenter)
        {
            recentering = true;
            recenterYaw = target.eulerAngles.y;
            recenterYawVelocity = recenterPitchVelocity = 0f;
            groundPitchHoldUntil = 0f;
        }
        if (IsLookInputLocked || CinematicControlled || Cursor.lockState != CursorLockMode.Locked)
            recentering = false;

        if (!recenter && !LockedEnemy && !IsLookInputLocked && Cursor.lockState == CursorLockMode.Locked)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 mouseDelta = mouse.delta.ReadValue();
                if (mouseDelta.sqrMagnitude > .25f) recentering = false;
                yaw += mouseDelta.x * mouseSensitivity;
                pitch -= mouseDelta.y * mouseSensitivity;
                if (Mathf.Abs(mouseDelta.y) > 0.01f) groundPitchHoldUntil = 0f;
                pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
            }
        }

        Vector3 pivot = GetFollowPivot();
        if (recentering && !LockedEnemy)
        {
            float elevation = Mathf.Clamp(recenterPitch, minimumPitch, maximumPitch);
            yaw = Mathf.SmoothDampAngle(yaw, recenterYaw, ref recenterYawVelocity, .16f);
            pitch = Mathf.SmoothDampAngle(pitch, elevation, ref recenterPitchVelocity, .16f);
            groundPitchOffset = Mathf.Lerp(groundPitchOffset, 0f, 1f - Mathf.Exp(-12f * Time.deltaTime));
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, recenterYaw)) < .1f && Mathf.Abs(pitch - elevation) < .1f)
                recentering = false;
        }
        if (LockedEnemy && CombatLookAvailable)
        {
            recentering = false;
            Vector3 toward = EnemyAim(lockedEnemy) - pivot;
            float wantedYaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            yaw = Mathf.LerpAngle(yaw, wantedYaw, 1f - Mathf.Exp(-12f * Time.deltaTime));
            float wantedPitch = Mathf.Clamp(15f - Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg, minimumPitch, maximumPitch);
            pitch = Mathf.Lerp(pitch, wantedPitch, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }
        bool nearWater = targetMovement && targetMovement.CurrentWaterVolume && target.position.y < targetMovement.WaterSurfaceY + 0.2f;
        Quaternion orbitRotation = Quaternion.Euler(recentering || LockedEnemy ? pitch : GetGroundAwarePitch(), yaw, 0f);
        Vector3 desiredPosition = pivot - orbitRotation * Vector3.forward * distance;

        Vector3 pivotToDesired = desiredPosition - pivot;
        float desiredDistance = pivotToDesired.magnitude;
        Vector3 collisionDirection = desiredDistance > 0.0001f
            ? pivotToDesired / desiredDistance
            : -transform.forward;

        bool obstacleFound = TryGetNearestCollision(
            pivot,
            collisionDirection,
            desiredDistance,
            out RaycastHit nearestHit);

        float targetDistance = desiredDistance;
        if (obstacleFound)
        {
            float obstructionDistance = nearestHit.distance - collisionBuffer;
            // A close main-world wall may leave less room than the preferred minimum.
            // Keep the actual clearance as the hard limit so the near plane stays outside.
            float safeMinimumDistance = cameraGroundLayout
                ? Mathf.Min(minimumDistance, Mathf.Max(0.05f, obstructionDistance))
                : minimumDistance;
            targetDistance = Mathf.Clamp(
                obstructionDistance,
                safeMinimumDistance,
                desiredDistance);
        }

        float hardLimit = targetDistance;
        targetDistance = Mathf.Min(targetDistance,
            GetAnticipatedCollisionDistance(pivot, collisionDirection, desiredDistance));

        // Anticipation is a soft preference; only an actual obstruction may snap inward.
        currentCameraDistance = Mathf.SmoothDamp(
            currentCameraDistance, targetDistance, ref collisionDistanceVelocity,
            targetDistance < currentCameraDistance
                ? (cameraGroundLayout && targetMovement && targetMovement.SwimmingActive ? .12f : .04f)
                : collisionReturnSmoothTime,
            cameraGroundLayout && targetMovement && targetMovement.SwimmingActive ? 4f : Mathf.Infinity);
        if (currentCameraDistance > hardLimit)
        {
            currentCameraDistance = hardLimit;
            collisionDistanceVelocity = 0f;
        }

        Vector3 collisionAdjustedPosition = pivot + collisionDirection * currentCameraDistance;
        // Match focus and camera translation filters so stair rise cannot tilt the view.
        // Orbit motion still turns position and gaze together, keeping the actor framed.
        Vector3 smoothedFocus = Vector3.SmoothDamp(followFocus, pivot, ref followFocusVelocity, positionSmoothTime);

        Vector3 smoothedPosition = Vector3.SmoothDamp(
            unshakenPosition,
            collisionAdjustedPosition,
            ref positionSmoothVelocity,
            positionSmoothTime);

        // Do not let follow smoothing leave the near plane on the far side of a new wall.
        if (obstacleFound && Vector3.Dot(smoothedPosition - pivot, collisionDirection) > hardLimit)
        {
            smoothedPosition = pivot + collisionDirection * hardLimit;
            smoothedFocus = pivot; followFocusVelocity = Vector3.zero;
            positionSmoothVelocity = Vector3.zero;
        }

        if (nearWater)
        {
            float waterClearance = targetMovement.WaterSurfaceY + GetNearClipPlaneRadius() + 0.15f;
            if (smoothedPosition.y < waterClearance)
            {
                smoothedPosition.y = waterClearance;
                positionSmoothVelocity.y = Mathf.Max(0f, positionSmoothVelocity.y);
            }
        }
        appliedShakeOffset = CalculateShakeOffset(orbitRotation);
        Vector3 finalPosition = smoothedPosition + appliedShakeOffset;
        ResolveSwimmingCameraClearance(ref finalPosition);
        transform.position = finalPosition;

        followFocus = smoothedFocus;
        transform.LookAt(followFocus, Vector3.up);
        UpdateCloseTargetVisibility();
    }

    // The original avatar must not fill the view when a wall forces the main-world
    // camera inside its body. Mesh rendering alone is suppressed, with hysteresis.
    private void UpdateCloseTargetVisibility()
    {
        if (!cameraGroundLayout || closeTargetRenderers == null) return;
        float threshold = closeTargetHidden ? 1.1f : 0.9f;
        bool hide = (transform.position - (target.position + Vector3.up * targetHeight)).sqrMagnitude < threshold * threshold;
        if (hide == closeTargetHidden) return;
        if (!hide)
        {
            RestoreCloseTargetVisibility();
            return;
        }
        for (int i = 0; i < closeTargetRenderers.Length; i++)
        {
            var renderer = closeTargetRenderers[i];
            if (!renderer || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))) continue;
            closeTargetRenderingFlags[i] = renderer.forceRenderingOff;
            renderer.forceRenderingOff = true;
        }
        closeTargetHidden = true;
    }

    private void RestoreCloseTargetVisibility()
    {
        if (!closeTargetHidden || closeTargetRenderers == null) return;
        for (int i = 0; i < closeTargetRenderers.Length; i++)
        {
            var renderer = closeTargetRenderers[i];
            if (renderer && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                renderer.forceRenderingOff = closeTargetRenderingFlags[i];
        }
        closeTargetHidden = false;
    }

    private void ResetFollowHeight()
    {
        followHeightInitialized = target != null;
        followHeightVelocity = 0f;
        stairFollowRemaining = 0f;
        groundPitchOffset = groundPitchVelocity = heldGroundPitch = groundPitchHoldUntil = 0f;
        positionSmoothVelocity = Vector3.zero;
        if (!target) return;
        followHeight = target.position.y + targetHeight;
        followFocus = target.position + Vector3.up * targetHeight; followFocusVelocity = Vector3.zero;
        lastTargetPosition = target.position;
    }

    private Vector3 GetFollowPivot()
    {
        Vector3 pivot = target.position + Vector3.up * targetHeight;
        bool nearWater = targetMovement && targetMovement.CurrentWaterVolume && target.position.y < targetMovement.WaterSurfaceY + 0.2f;
        if (nearWater) pivot.y = Mathf.Max(pivot.y, targetMovement.WaterSurfaceY + 0.6f);
        // New journeys, respawns, and cinematic handbacks must not ease from the old location.
        if (!followHeightInitialized ||
            (target.position - lastTargetPosition).sqrMagnitude > Mathf.Max(1f, distance * distance))
            ResetFollowHeight();
        else
        {
            bool ordinaryGround = targetMovement && targetMovement.IsStableGrounded &&
                !targetMovement.GroundJumpActive && !targetMovement.ClimbingActive &&
                !targetMovement.MantlingActive && !targetMovement.SwimmingActive;
            if (ordinaryGround && targetMovement.StairSupportActive) stairFollowRemaining = .5f;
            else stairFollowRemaining = ordinaryGround ? Mathf.Max(0, stairFollowRemaining-Time.deltaTime) : 0;
            // Capsule steps are discontinuous. Filter their height for both camera
            // position and gaze, retaining the same orbit/collision and jump behavior.
            float smoothing = stairFollowRemaining > 0
                ? Mathf.Max(heightSmoothTime, stairHeightSmoothTime) : heightSmoothTime;
            followHeight = Mathf.SmoothDamp(followHeight, pivot.y, ref followHeightVelocity,
                Mathf.Max(0.01f, smoothing));
        }
        lastTargetPosition = target.position;
        pivot.y = nearWater ? Mathf.Max(followHeight, targetMovement.WaterSurfaceY + 0.6f) : followHeight;
        return pivot;
    }

    private Vector3 CalculateShakeOffset(Quaternion orbitRotation)
    {
        if (shakeTimeRemaining <= 0f)
        {
            return Vector3.zero;
        }

        float unscaledDeltaTime = Time.unscaledDeltaTime;
        shakeTimeRemaining = Mathf.Max(0f, shakeTimeRemaining - unscaledDeltaTime);
        shakePhase += unscaledDeltaTime * Mathf.Max(1f, shakeFrequency);

        float envelope = shakeDuration > 0f ? shakeTimeRemaining / shakeDuration : 0f;
        Vector3 localOffset = new Vector3(
            Mathf.Sin(shakePhase * 1.37f),
            Mathf.Sin(shakePhase * 1.91f + 1.2f),
            0f) * (shakeAmplitude * envelope);

        if (shakeTimeRemaining <= 0f)
        {
            shakeAmplitude = 0f;
            shakeDuration = 0f;
        }

        return orbitRotation * localOffset + hitImpulseDirection * (shakeAmplitude * .7f * envelope * envelope);
    }

    private bool TryGetNearestCollision(
        Vector3 origin,
        Vector3 direction,
        float castDistance,
        out RaycastHit nearestHit, float padding = 0f)
    {
        float effectiveRadius = Mathf.Max(collisionRadius, GetNearClipPlaneRadius());
        bool swimFocus = cameraGroundLayout && targetMovement && targetMovement.SwimmingActive;
        int segments = swimFocus ? 8 : 1;
        nearestHit = default;
        float nearestDistance = float.PositiveInfinity;
        for (int segment = 0; segment < segments; segment++)
        {
            float start = castDistance * segment / segments;
            float end = castDistance * (segment + 1) / segments;
            float radius = swimFocus
                ? Mathf.Lerp(Mathf.Min(.08f, effectiveRadius), effectiveRadius + padding, (segment + 1f) / segments)
                : effectiveRadius;
            int hitCount = Physics.SphereCastNonAlloc(
                origin + direction * start, radius, direction, collisionHits,
                end - start, collisionLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                var hit = collisionHits[i];
                if (!hit.collider || hit.collider.GetComponentInParent<CameraCollisionPassthrough>() || hit.collider.GetComponentInParent<GelBlobAI>()) continue;
                var hitTransform = hit.collider.transform;
                if (hitTransform == target || hitTransform.IsChildOf(target)) continue;
                float distanceFromFocus = start + hit.distance;
                if (distanceFromFocus < nearestDistance)
                {
                    nearestDistance = distanceFromFocus;
                    hit.distance = distanceFromFocus;
                    nearestHit = hit;
                }
            }
        }
        return nearestDistance < float.PositiveInfinity;
    }

    // When the boom points uphill, lift it above the supporting slope instead of
    // repeatedly shortening it against the stairs. The player's chosen pitch
    // remains unchanged and is restored as the ground levels out.
    private float GetGroundAwarePitch()
    {
        float targetOffset = 0f;
        if (targetMovement && !targetMovement.SwimmingActive &&
            TrySampleCameraGround(target.position, out float groundY))
        {
            Vector3 behind = -(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
            // Sample across several steps, rather than following a noisy capsule contact normal.
            float uphillAngle = 0f;
            for (int i = 1; i <= 2; i++)
            {
                float span = i * 1.5f;
                if (TrySampleCameraGround(target.position + behind * span, out float height))
                    uphillAngle = Mathf.Max(uphillAngle, Mathf.Atan2(height - groundY, span) * Mathf.Rad2Deg);
            }
            if (uphillAngle > 8f)
                targetOffset = Mathf.Max(0f, Mathf.Min(maximumPitch, uphillAngle + 15f) - pitch);
        }
        // Short stair landings must not cause repeated pitch down/up cycles.
        if (targetMovement && targetMovement.SwimmingActive)
        {
            heldGroundPitch = 0f;
            groundPitchHoldUntil = 0f;
        }
        else if (targetOffset >= heldGroundPitch - 2f)
        {
            heldGroundPitch = Mathf.Max(heldGroundPitch, targetOffset);
            groundPitchHoldUntil = Time.time + 2f;
        }
        else if (Time.time >= groundPitchHoldUntil)
        {
            heldGroundPitch = targetOffset;
        }
        targetOffset = heldGroundPitch;
        groundPitchOffset = Mathf.SmoothDamp(groundPitchOffset, targetOffset,
            ref groundPitchVelocity, targetOffset > groundPitchOffset
                ? Mathf.Max(0.25f, groundPitchSmoothTime) : 1f);
        float result = pitch + groundPitchOffset;
        if (targetMovement && targetMovement.SwimmingActive) result = Mathf.Max(10f, result);
        return Mathf.Clamp(result, minimumPitch, maximumPitch);
    }

    private bool TrySampleCameraGround(Vector3 point, out float height)
    {
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 4f, Vector3.down,
            groundCameraHits, 8f, collisionLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        height = point.y;
        for (int i = 0; i < count; i++)
        {
            var hit = groundCameraHits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<CameraCollisionPassthrough>() || hit.collider.GetComponentInParent<GelBlobAI>() || hit.normal.y < 0.6f ||
                hit.collider.transform == target || hit.collider.transform.IsChildOf(target)) continue;
            // Door lintels and furniture are obstacles, not the supporting slope.
            // Ordinary camera collision casts still test every original collider.
            if (cameraGroundLayout && !cameraGroundLayout.IsNavigationSurface(hit.collider)) continue;
            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                height = hit.point.y;
            }
        }
        return nearest < float.PositiveInfinity;
    }

    // Ease toward a nearby occluder before it enters the camera's actual clearance
    // radius. The ordinary cast still owns the hard limit against clipping.
    private float GetAnticipatedCollisionDistance(Vector3 origin, Vector3 direction, float desiredDistance)
    {
        // Anticipate with the same focus taper rather than a uniformly expanded sphere.
        if (cameraGroundLayout && targetMovement && targetMovement.SwimmingActive)
        {
            float extra = Mathf.Max(0f, collisionAnticipationPadding) * .75f;
            return TryGetNearestCollision(origin, direction, desiredDistance, out var predicted, extra)
                ? Mathf.Clamp(predicted.distance - collisionBuffer, minimumDistance, desiredDistance)
                : desiredDistance;
        }
        float padding = Mathf.Max(0f, collisionAnticipationPadding);
        if (padding <= 0f) return desiredDistance;
        float radius = Mathf.Max(collisionRadius, GetNearClipPlaneRadius());
        // Start before the pivot so the padded sweep can see a nearby arch or post
        // even when its larger sphere already overlaps at the normal boom origin.
        Vector3 castOrigin = cameraGroundLayout ? origin - direction * padding : origin;
        float castDistance = desiredDistance + (cameraGroundLayout ? padding : 0f);
        int count = Physics.SphereCastNonAlloc(castOrigin, radius + padding, direction,
            anticipationHits, castDistance, collisionLayers, QueryTriggerInteraction.Ignore);
        float distance = desiredDistance;
        for (int i = 0; i < count; i++)
        {
            var hit = anticipationHits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<CameraCollisionPassthrough>() || hit.collider.GetComponentInParent<GelBlobAI>() || hit.distance <= 0.0001f) continue;
            // Supporting ground must not repeatedly shorten and release the soft boom.
            // Hard collision still tests it, and ground-aware pitch provides slope clearance.
            if (cameraGroundLayout && hit.normal.y >= 0.6f && cameraGroundLayout.IsNavigationSurface(hit.collider)) continue;
            var hitTransform = hit.collider.transform;
            if (hitTransform == target || hitTransform.IsChildOf(target)) continue;
            Vector3 offset = hit.point - origin;
            float along = Vector3.Dot(offset, direction);
            if (along <= 0f || along > desiredDistance + padding) continue;
            float clearance = (offset - direction * along).magnitude;
            float blend = 1f - Mathf.InverseLerp(radius, radius + padding, clearance);
            blend = Mathf.SmoothStep(0f, 1f, blend);
            float limit = Mathf.Clamp(along - collisionBuffer, minimumDistance, desiredDistance);
            distance = Mathf.Min(distance, Mathf.Lerp(desiredDistance, limit, blend));
        }
        return distance;
    }


    // Resolve the actual camera volume after follow smoothing. The focus-side
    // taper prevents false zero-distance collapse, while this sphere keeps the
    // complete near-plane clearance at the final camera position.
    private void ResolveSwimmingCameraClearance(ref Vector3 position)
    {
        if (!cameraGroundLayout || !targetMovement || !targetMovement.SwimmingActive) return;
        if (!cameraClearanceProbe)
        {
            var queryObject = new GameObject("Runtime swimming camera clearance query");
            queryObject.hideFlags = HideFlags.HideAndDontSave;
            queryObject.transform.SetParent(transform, false);
            cameraClearanceProbe = queryObject.AddComponent<SphereCollider>();
            cameraClearanceProbe.isTrigger = true;
            var queryBody = queryObject.AddComponent<Rigidbody>();
            queryBody.isKinematic = true;
            queryBody.detectCollisions = false;
        }
        float radius = Mathf.Max(collisionRadius, GetNearClipPlaneRadius()) + .01f;
        cameraClearanceProbe.radius = radius;
        cameraClearanceProbe.center = Vector3.zero;
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            int count = Physics.OverlapSphereNonAlloc(position, radius,
                cameraClearanceOverlaps, collisionLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = cameraClearanceOverlaps[i];
                if (!collider || collider.GetComponentInParent<CameraCollisionPassthrough>() || collider.GetComponentInParent<GelBlobAI>() || collider == cameraClearanceProbe ||
                    collider.transform == target || collider.transform.IsChildOf(target)) continue;
                if (Physics.ComputePenetration(cameraClearanceProbe, position, Quaternion.identity,
                    collider, collider.transform.position, collider.transform.rotation,
                    out var direction, out float depth) && depth > .00001f)
                {
                    position += direction * (depth + .003f);
                    if (Vector3.Dot(positionSmoothVelocity, direction) < 0f)
                        positionSmoothVelocity = Vector3.ProjectOnPlane(positionSmoothVelocity, direction);
                    moved = true;
                }
            }
            if (!moved) break;
        }
    }


    private float GetNearClipPlaneRadius()
    {
        if (cachedCamera == null)
        {
            return collisionRadius;
        }

        float halfHeight = cachedCamera.nearClipPlane
            * Mathf.Tan(cachedCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * cachedCamera.aspect;
        return Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);
    }

    private void InitializeOrbitAngles()
    {
        Vector3 pivot = target.position + Vector3.up * targetHeight;
        Vector3 lookDirection = pivot - transform.position;

        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            yaw = target.eulerAngles.y;
            pitch = 20f;
            return;
        }

        Vector3 euler = Quaternion.LookRotation(lookDirection.normalized, Vector3.up).eulerAngles;
        yaw = euler.y;
        pitch = NormalizeAngle(euler.x);
        pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
