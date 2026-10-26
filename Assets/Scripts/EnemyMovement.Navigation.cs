using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class EnemyMovement
{
    void NavigatePlatforms(bool isGrounded, bool landedThisFrame)
    {
        if (isDroppingThroughPlatform)
        {
            StopHorizontalMovement();
            return;
        }

        if (landedThisFrame && navigationJumpCommitted)
        {
            if (IsStandingOnNavigationTarget())
            {
                ClearNavigationTarget();
                if (NeedsPlatformNavigation())
                {
                    SelectNavigationTarget();
                }
            }
            else
            {
                ReplanNavigationAfterFailure();
            }
        }

        if (!hasNavigationTarget &&
            isGrounded &&
            NeedsPlatformNavigation())
        {
            SelectNavigationTarget();
        }

        if (isGrounded &&
            navigationTargetPlatform == null &&
            !navigationWantsDrop &&
            NeedsPlatformNavigation() &&
            Time.time >= nextNavigationReplanTime)
        {
            ClearNavigationTarget();
            SelectNavigationTarget();
        }

        if (navigationWantsDrop)
        {
            Collider platform = navigationTargetPlatform != null
                ? navigationTargetPlatform
                : FindOneWayPlatformBelow();
            if (platform != null)
            {
                navigationWantsDrop = false;
                hasNavigationTarget = false;
                navigationTargetPlatform = null;
                StartCoroutine(DropThroughPlatform(platform));
            }
            else
            {
                ReplanNavigationAfterFailure();
            }

            StopHorizontalMovement();
            return;
        }

        if (!hasNavigationTarget)
        {
            StopHorizontalMovement();
            return;
        }

        if (navigationTargetPlatform != null &&
            !navigationTargetPlatform.enabled)
        {
            ReplanNavigationAfterFailure();
            if (!hasNavigationTarget)
            {
                StopHorizontalMovement();
                return;
            }
        }

        RefreshNavigationTargetCoordinates();

        float horizontalOffset =
            navigationTargetX - transform.position.x;
        float direction = horizontalOffset >= 0f ? 1f : -1f;
        float distance = Mathf.Abs(horizontalOffset);
        Vector3 velocity = rb.linearVelocity;
        float desiredHorizontalVelocity =
            distance > NavigationArrivalDistance
                ? direction * moveSpeed
                : 0f;

        if (isGrounded)
        {
            velocity.x = desiredHorizontalVelocity;
        }
        else
        {
            velocity.x = Mathf.MoveTowards(
                velocity.x,
                desiredHorizontalVelocity,
                Mathf.Max(0.1f, platformAirControlAcceleration) *
                    Time.fixedDeltaTime
            );
        }

        if (Mathf.Abs(velocity.x) > 0.1f)
        {
            SetFacing(velocity.x >= 0f ? 1f : -1f);
        }

        float currentSurfaceY = GetCurrentSurfaceHeight();
        bool targetIsAbove =
            navigationTargetY > currentSurfaceY + 0.4f;
        bool atTakeoffEdge =
            isGrounded &&
            targetIsAbove &&
            !HasGroundAhead(direction);
        bool shouldJump =
            isGrounded &&
            targetIsAbove &&
            (distance <= platformJumpAlignment || atTakeoffEdge) &&
            Time.time >= nextJumpTime;

        if (atTakeoffEdge && !shouldJump)
        {
            velocity.x = 0f;
        }

        if (shouldJump)
        {
            velocity.y = Mathf.Max(jumpForce, platformJumpForce);
            nextJumpTime = Time.time + platformJumpCooldown;
            wasGrounded = false;
            navigationJumpCommitted = true;
            navigationNoProgressTimer = 0f;
            PlayJumpAnimation();
            SetGroundedAnimation(false);
        }

        velocity.z = 0f;
        rb.linearVelocity = velocity;
        TrackNavigationProgress(isGrounded && !shouldJump);
        SetMovingAnimation(
            distance > NavigationArrivalDistance &&
            isGrounded &&
            !shouldJump
        );
        SetWalkingAnimation(false);
    }

    bool NeedsPlatformNavigation()
    {
        if (player == null)
            return false;

        Collider hangingPlatform = GetHangingPlayerPlatform();
        if (hangingPlatform != null)
        {
            return !IsStandingOnPlatform(hangingPlatform);
        }

        float verticalOffset =
            player.position.y - transform.position.y;
        bool playerIsSupported = HasGroundBelow(
            player.position,
            1.35f
        );

        if (playerIsSupported &&
            Mathf.Abs(verticalOffset) > verticalNavigationThreshold)
        {
            return true;
        }

        if (!IsGrounded())
            return false;

        float horizontalOffset =
            player.position.x - transform.position.x;
        if (Mathf.Abs(horizontalOffset) <= stopDistance)
            return false;

        float direction = horizontalOffset >= 0f ? 1f : -1f;
        return !HasGroundAhead(direction);
    }

    void SelectNavigationTarget()
    {
        hasNavigationTarget = true;
        navigationJumpCommitted = false;
        navigationWantsDrop = false;
        navigationTargetPlatform = null;
        navigationTargetX = player != null
            ? player.position.x
            : arenaCenterX;
        navigationTargetY = GetCurrentSurfaceHeight();
        nextNavigationReplanTime = float.PositiveInfinity;

        ClearExpiredNavigationFailures();

        // A falling fighter's Y is not a launch surface. Selecting here could
        // choose the centre platform, then keep that unreachable target after
        // landing on the main stage. Plan only after the body is supported.
        if (!IsGrounded())
        {
            hasNavigationTarget = false;
            ResetNavigationProgress();
            return;
        }

        if (player == null)
        {
            ResetNavigationProgress();
            return;
        }

        if (TrySelectHangingNavigationTarget())
            return;

        float verticalOffset =
            player.position.y - transform.position.y;
        float directionToPlayer =
            player.position.x >= transform.position.x ? 1f : -1f;
        bool pathEndsBeforePlayer =
            !HasGroundAhead(directionToPlayer);
        Collider currentOneWayPlatform =
            FindOneWayPlatformBelow();

        if (currentOneWayPlatform != null &&
            (verticalOffset < -verticalNavigationThreshold ||
                (Mathf.Abs(verticalOffset) <=
                    verticalNavigationThreshold &&
                    pathEndsBeforePlayer)))
        {
            navigationWantsDrop = true;
            navigationTargetPlatform = currentOneWayPlatform;
            navigationTargetX = transform.position.x;
            navigationTargetY =
                currentOneWayPlatform.bounds.min.y - 0.5f;
            ResetNavigationProgress();
            return;
        }

        if (verticalOffset > verticalNavigationThreshold)
        {
            Collider platform = FindBestReachablePlatform();
            if (platform != null)
            {
                navigationTargetPlatform = platform;
                RefreshNavigationTargetCoordinates();
                RefreshNavigationTimeBudget();
                ResetNavigationProgress();
                return;
            }

            SetSafeNavigationWaitingTarget(currentOneWayPlatform);
            return;
        }

        if (pathEndsBeforePlayer && currentOneWayPlatform == null)
        {
            SetSafeNavigationWaitingTarget(null);
            return;
        }

        ResetNavigationProgress();
    }

    bool TrySelectHangingNavigationTarget()
    {
        Collider targetPlatform = GetHangingPlayerPlatform();
        if (targetPlatform == null)
            return false;

        if (IsStandingOnPlatform(targetPlatform))
        {
            hasNavigationTarget = false;
            ResetNavigationProgress();
            return true;
        }

        Collider currentPlatform = FindOneWayPlatformBelow();
        float currentSurfaceY = GetCurrentSurfaceHeight();
        float targetTop = targetPlatform.bounds.max.y;

        bool shouldDropFirst = currentPlatform != null &&
            currentPlatform != targetPlatform &&
            (targetPlatform == mainStageCollider ||
                targetTop <= currentSurfaceY + 0.4f);
        if (shouldDropFirst)
        {
            navigationWantsDrop = true;
            navigationTargetPlatform = currentPlatform;
            navigationTargetX = transform.position.x;
            navigationTargetY = currentPlatform.bounds.min.y - 0.5f;
            ResetNavigationProgress();
            return true;
        }

        if (targetPlatform != mainStageCollider &&
            IsPlatformTemporarilyFailed(targetPlatform))
        {
            SetSafeNavigationWaitingTarget(currentPlatform);
            return true;
        }

        float heightDelta = targetTop - currentSurfaceY;
        if (targetPlatform != mainStageCollider &&
            heightDelta >= 0.4f &&
            heightDelta <= GetReachableJumpHeight())
        {
            navigationTargetPlatform = targetPlatform;
            RefreshNavigationTargetCoordinates();
            RefreshNavigationTimeBudget();
            ResetNavigationProgress();
            return true;
        }

        if (heightDelta > GetReachableJumpHeight())
        {
            Collider steppingPlatform = FindBestReachablePlatform();
            if (steppingPlatform != null &&
                steppingPlatform != currentPlatform)
            {
                navigationTargetPlatform = steppingPlatform;
                RefreshNavigationTargetCoordinates();
                RefreshNavigationTimeBudget();
                ResetNavigationProgress();
                return true;
            }
        }

        if (currentPlatform != null &&
            currentPlatform != targetPlatform)
        {
            navigationWantsDrop = true;
            navigationTargetPlatform = currentPlatform;
            navigationTargetX = transform.position.x;
            navigationTargetY = currentPlatform.bounds.min.y - 0.5f;
            ResetNavigationProgress();
            return true;
        }

        if (targetPlatform == mainStageCollider)
        {
            navigationTargetPlatform = null;
            navigationTargetX = Mathf.Clamp(
                GetLedgePressureX(),
                mainStageCollider.bounds.min.x + ledgeSafeInset,
                mainStageCollider.bounds.max.x - ledgeSafeInset
            );
            navigationTargetY = mainStageCollider.bounds.max.y;
            ResetNavigationProgress();
            return true;
        }

        SetSafeNavigationWaitingTarget(currentPlatform);
        return true;
    }

    void SetSafeNavigationWaitingTarget(Collider currentPlatform)
    {
        navigationTargetPlatform = null;
        navigationWantsDrop = false;

        if (currentPlatform != null)
        {
            navigationTargetX = GetSafePlatformLandingX(currentPlatform);
            navigationTargetY = currentPlatform.bounds.max.y;
        }
        else
        {
            navigationTargetX = arenaCenterX;
            navigationTargetY = GetCurrentSurfaceHeight();
        }

        nextNavigationReplanTime = Time.time + Mathf.Max(
            0.1f,
            failedPlatformRetryDelay
        );
        ResetNavigationProgress();
    }

    void RefreshNavigationTargetCoordinates()
    {
        if (navigationTargetPlatform == null || navigationWantsDrop)
            return;

        navigationTargetX = GetSafePlatformLandingX(
            navigationTargetPlatform
        );
        navigationTargetY = navigationTargetPlatform.bounds.max.y;
    }

    float GetSafePlatformLandingX(Collider platform)
    {
        if (platform == null)
            return arenaCenterX;

        Bounds bounds = platform.bounds;
        float maximumInset = Mathf.Max(0f, bounds.extents.x - 0.15f);
        float safeInset = Mathf.Min(
            Mathf.Max(0f, platformLandingInset),
            maximumInset
        );
        float safeMinimum = bounds.min.x + safeInset;
        float safeMaximum = bounds.max.x - safeInset;

        if (safeMinimum > safeMaximum)
            return bounds.center.x;

        float desiredX = GetHangingPlayerPlatform() == platform
            ? GetLedgePressureX()
            : player != null ? player.position.x : arenaCenterX;
        return Mathf.Clamp(desiredX, safeMinimum, safeMaximum);
    }

    float GetReachableJumpHeight()
    {
        float speed = Mathf.Max(jumpForce, platformJumpForce);
        float gravity = Mathf.Abs(Physics.gravity.y) *
            Mathf.Max(1f, riseGravityMultiplier);
        // Leave clearance for discrete physics and a supported landing.
        float physicalHeight = speed * speed / (2f * Mathf.Max(.1f, gravity)) - .2f;
        return Mathf.Min(maximumReachablePlatformHeight, Mathf.Max(0f, physicalHeight));
    }

    void RefreshNavigationTimeBudget()
    {
        float walkSeconds = Mathf.Abs(navigationTargetX - transform.position.x) /
            Mathf.Max(.1f, moveSpeed);
        float airSeconds = 2f * Mathf.Max(jumpForce, platformJumpForce) /
            Mathf.Max(.1f, Mathf.Abs(Physics.gravity.y) * Mathf.Max(1f, riseGravityMultiplier));
        // Progress checks still abandon blocked routes promptly. The overall
        // deadline must not blacklist a valid platform halfway to takeoff.
        stateTimer = Mathf.Max(platformNavigationTimeout,
            walkSeconds + airSeconds + platformJumpCooldown + .75f);
    }

    bool IsStandingOnNavigationTarget()
    {
        if (navigationTargetPlatform == null)
            return false;

        return IsStandingOnPlatform(navigationTargetPlatform);
    }

    Collider GetHangingPlayerPlatform()
    {
        if (playerController == null ||
            !playerController.IsLedgeHanging)
        {
            return null;
        }

        Collider platform = playerController.HangingLedgePlatform;
        return platform != null && platform.enabled
            ? platform
            : null;
    }

    bool IsStandingOnHangingPlatform()
    {
        Collider hangingPlatform = GetHangingPlayerPlatform();
        return hangingPlatform != null &&
            IsStandingOnPlatform(hangingPlatform);
    }

    bool IsStandingOnPlatform(Collider platform)
    {
        if (platform == null || !IsGrounded())
            return false;

        RaycastHit[] hits = Physics.SphereCastAll(
            transform.position + Vector3.up * 0.08f,
            0.35f,
            Vector3.down,
            1.4f,
            GroundLayerMask,
            QueryTriggerInteraction.Ignore
        );

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == platform ||
                IsColliderPartOfPlatform(hit.collider, platform))
                return true;
        }

        return false;
    }

    bool IsColliderPartOfPlatform(
        Collider candidate,
        Collider targetPlatform
    )
    {
        if (candidate == null || targetPlatform == null)
            return false;

        if (!IsOneWayPlatform(targetPlatform))
            return false;

        Transform platformRoot = targetPlatform.transform.parent;
        return platformRoot != null &&
            candidate.transform.IsChildOf(platformRoot);
    }

    float GetLedgePressureX()
    {
        Collider platform = GetHangingPlayerPlatform();
        if (platform == null)
        {
            return player != null ? player.position.x : arenaCenterX;
        }

        Bounds bounds = platform.bounds;
        float inset = Mathf.Min(
            Mathf.Max(0.1f, ledgeSafeInset),
            Mathf.Max(0.1f, bounds.extents.x - 0.15f)
        );
        float desiredX = playerController.HangingLedgeX -
            playerController.HangingLedgeSide * inset;

        return Mathf.Clamp(
            desiredX,
            bounds.min.x + 0.15f,
            bounds.max.x - 0.15f
        );
    }

    void TrackNavigationProgress(bool canEvaluate)
    {
        if (!canEvaluate ||
            !hasNavigationTarget ||
            navigationWantsDrop ||
            navigationTargetPlatform == null)
        {
            navigationNoProgressTimer = 0f;
            return;
        }

        float currentSurfaceY = GetCurrentSurfaceHeight();
        bool waitingForJumpCooldown =
            navigationTargetY > currentSurfaceY + 0.4f &&
            Time.time < nextJumpTime;
        if (waitingForJumpCooldown)
        {
            navigationNoProgressTimer = 0f;
            return;
        }

        float distance = GetNavigationProgressDistance();
        if (distance + NavigationProgressEpsilon <
            navigationBestDistance)
        {
            navigationBestDistance = distance;
            navigationNoProgressTimer = 0f;
            return;
        }

        navigationNoProgressTimer += Time.fixedDeltaTime;
        if (navigationNoProgressTimer >=
            Mathf.Max(0.1f, navigationProgressTimeout))
        {
            ReplanNavigationAfterFailure();
        }
    }

    float GetNavigationProgressDistance()
    {
        return Mathf.Abs(navigationTargetX - transform.position.x);
    }

    void ResetNavigationProgress()
    {
        navigationBestDistance = hasNavigationTarget
            ? GetNavigationProgressDistance()
            : float.PositiveInfinity;
        navigationNoProgressTimer = 0f;
    }

    void ClearNavigationTarget()
    {
        navigationTargetPlatform = null;
        hasNavigationTarget = false;
        navigationJumpCommitted = false;
        navigationWantsDrop = false;
        navigationBestDistance = float.PositiveInfinity;
        navigationNoProgressTimer = 0f;
        nextNavigationReplanTime = float.PositiveInfinity;
    }

    void ReplanNavigationAfterFailure()
    {
        if (navigationTargetPlatform != null &&
            !navigationWantsDrop)
        {
            failedNavigationPlatforms[navigationTargetPlatform] =
                Time.time + Mathf.Max(0f, failedPlatformRetryDelay);
        }

        ClearNavigationTarget();

        if (currentState == EnemyState.PlatformNavigate && IsGrounded())
        {
            SelectNavigationTarget();
            stateTimer = Mathf.Max(stateTimer, Mathf.Max(0.2f, platformNavigationTimeout));
        }
    }

    bool IsPlatformTemporarilyFailed(Collider platform)
    {
        if (platform == null)
            return false;

        return failedNavigationPlatforms.TryGetValue(
                platform,
                out float retryTime
            ) &&
            Time.time < retryTime;
    }

    void ClearExpiredNavigationFailures()
    {
        if (failedNavigationPlatforms.Count == 0)
            return;

        List<Collider> expiredPlatforms = new List<Collider>();
        foreach (KeyValuePair<Collider, float> entry in
            failedNavigationPlatforms)
        {
            if (entry.Key == null || Time.time >= entry.Value)
            {
                expiredPlatforms.Add(entry.Key);
            }
        }

        foreach (Collider platform in expiredPlatforms)
        {
            failedNavigationPlatforms.Remove(platform);
        }
    }

    Collider FindBestReachablePlatform()
    {
        if (oneWayPlatformColliders == null)
            return null;

        float currentSurfaceY = GetCurrentSurfaceHeight();
        Collider bestPlatform = null;
        float bestScore = float.NegativeInfinity;
        Collider goalPlatform = GetHangingPlayerPlatform();
        if (goalPlatform == null && player != null && Physics.Raycast(
            player.position + Vector3.up * .1f, Vector3.down,
            out RaycastHit support, 1.5f, GroundLayerMask,
            QueryTriggerInteraction.Ignore))
        {
            goalPlatform = support.collider;
        }

        foreach (Collider platform in oneWayPlatformColliders)
        {
            if (platform == null || !platform.enabled)
                continue;

            if (IsPlatformTemporarilyFailed(platform))
            {
                continue;
            }

            float platformTop = platform.bounds.max.y;
            float heightDelta = platformTop - currentSurfaceY;
            if (heightDelta < 0.4f ||
                heightDelta > GetReachableJumpHeight())
            {
                continue;
            }

            float selfDistance = Mathf.Abs(
                platform.bounds.center.x - transform.position.x
            );
            float playerDistance = player != null
                ? Mathf.Abs(
                    platform.bounds.center.x - player.position.x
                )
                : 0f;
            float score = platformTop * 2f -
                selfDistance * 0.12f -
                playerDistance * 0.25f;

            // The player's actual supported platform wins over a merely
            // higher one; higher platforms are only stepping stones.
            if (platform == goalPlatform) score += 1000f;

            if (score > bestScore)
            {
                bestScore = score;
                bestPlatform = platform;
            }
        }

        return bestPlatform;
    }

    float GetSafeRepositionDirection()
    {
        float awayFromPlayer = player != null &&
            transform.position.x < player.position.x
                ? -1f
                : 1f;

        if (HasGroundAhead(awayFromPlayer))
            return awayFromPlayer;

        float towardArenaCenter =
            arenaCenterX >= transform.position.x ? 1f : -1f;
        return HasGroundAhead(towardArenaCenter)
            ? towardArenaCenter
            : 0f;
    }

    bool HasGroundAhead(float direction)
    {
        if (Mathf.Abs(direction) < 0.01f)
            return true;

        Vector3 origin = transform.position +
            Vector3.right *
                (direction >= 0f ? 1f : -1f) *
                edgeProbeForwardDistance +
            Vector3.up * 0.1f;

        return Physics.Raycast(
            origin,
            Vector3.down,
            edgeProbeDepth,
            GroundLayerMask,
            QueryTriggerInteraction.Ignore
        );
    }

    bool HasGroundBelow(Vector3 position, float distance)
    {
        return Physics.Raycast(
            position + Vector3.up * 0.1f,
            Vector3.down,
            Mathf.Max(0.2f, distance),
            GroundLayerMask,
            QueryTriggerInteraction.Ignore
        );
    }

    float GetCurrentSurfaceHeight()
    {
        if (Physics.Raycast(
            transform.position + Vector3.up * 0.1f,
            Vector3.down,
            out RaycastHit hit,
            1.5f,
            GroundLayerMask,
            QueryTriggerInteraction.Ignore
        ))
        {
            return hit.point.y;
        }

        return transform.position.y - 1f;
    }

    void CacheOneWayPlatforms()
    {
        BattleDropThroughPlatform[] platformMarkers =
            FindObjectsByType<BattleDropThroughPlatform>(
                FindObjectsSortMode.None
            );
        oneWayPlatformColliders =
            new Collider[platformMarkers.Length];

        for (int i = 0; i < platformMarkers.Length; i++)
        {
            oneWayPlatformColliders[i] =
                platformMarkers[i].GetComponent<Collider>();
        }
    }

    void CacheMainStageBounds()
    {
        GameObject battleGround = GameObject.Find("BattleGround");
        if (battleGround == null)
            return;

        mainStageCollider = battleGround.GetComponent<Collider>();
        if (mainStageCollider == null)
            return;

        arenaCenterX = mainStageCollider.bounds.center.x;
        mainStageHalfWidth = mainStageCollider.bounds.extents.x;
    }

    Collider FindOneWayPlatformBelow()
    {
        return BattlePlatformPhysics.FindOneWayPlatformBelow(
            transform.position + Vector3.up * 0.08f,
            0.42f,
            1.4f,
            GroundLayerMask,
            false
        );
    }

    static bool IsOneWayPlatform(Collider platformCollider)
    {
        return BattlePlatformPhysics.IsOneWayPlatform(
            platformCollider,
            false
        );
    }

    IEnumerator DropThroughPlatform(Collider platformCollider)
    {
        if (isDroppingThroughPlatform || platformCollider == null)
            yield break;

        activeDropPlatform = platformCollider;
        activeDropPlatformColliders =
            BattlePlatformPhysics.GetPlatformColliders(platformCollider);
        isDroppingThroughPlatform = true;
        SetPlatformCollisionsIgnored(
            activeDropPlatformColliders,
            true
        );

        Vector3 velocity = rb.linearVelocity;
        velocity.y = Mathf.Min(
            velocity.y,
            -Mathf.Max(0.1f, dropThroughSpeed)
        );
        rb.linearVelocity = velocity;
        stateTimer = Mathf.Max(
            stateTimer,
            dropThroughTimeout + 0.25f
        );

        float timeout = Mathf.Max(0.1f, dropThroughTimeout);
        while (timeout > 0f &&
            !IsClearBelowPlatform(platformCollider))
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
}
