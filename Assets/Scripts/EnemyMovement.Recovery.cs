using UnityEngine;

public partial class EnemyMovement
{
bool ShouldStartOffstageRecovery()
    {
        if (rb == null || IsGrounded())
            return false;

        float stageMinimumX =
            arenaCenterX - mainStageHalfWidth;
        float stageMaximumX =
            arenaCenterX + mainStageHalfWidth;
        bool outsideStageWidth =
            transform.position.x < stageMinimumX - 0.15f ||
            transform.position.x > stageMaximumX + 0.15f;
        bool belowMainStage =
            transform.position.y <= offstageRecoveryHeight;

        return belowMainStage ||
            (outsideStageWidth && rb.linearVelocity.y <= 0.25f);
    }

    void RecoverToStage()
    {
        if (recoveryTargetPlatform == null)
        {
            SelectRecoveryTarget();
        }
        else
        {
            RefreshRecoveryTargetCoordinates();
        }

        float horizontalOffset =
            recoveryTargetX - transform.position.x;
        float direction = Mathf.Abs(horizontalOffset) <= 0.15f
            ? 0f
            : horizontalOffset > 0f
                ? 1f
                : -1f;

        Vector3 velocity = rb.linearVelocity;
        float desiredHorizontalSpeed =
            direction * offstageRecoverySpeed;
        velocity.x = Mathf.MoveTowards(
            velocity.x,
            desiredHorizontalSpeed,
            recoveryHorizontalAcceleration * Time.fixedDeltaTime
        );
        velocity.z = 0f;

        bool nearApex = Mathf.Abs(velocity.y) <=
            Mathf.Max(0.05f, recoveryApexVelocity);
        bool belowRecoverySurface =
            transform.position.y < recoveryTargetY - 0.15f;
        bool emergencyFall = velocity.y <=
            -Mathf.Max(0.1f, recoveryEmergencyFallSpeed) ||
            (belowRecoverySurface && velocity.y < -1f);
        if (!recoveryBoostUsed && (nearApex || emergencyFall))
        {
            velocity.y = Mathf.Max(
                velocity.y,
                offstageRecoveryVerticalSpeed
            );
            if (Mathf.Abs(direction) > 0.01f)
            {
                velocity.x = direction * Mathf.Max(
                    Mathf.Abs(velocity.x),
                    offstageRecoverySpeed
                );
            }
            recoveryBoostUsed = true;
            PlayJumpAnimation();
        }

        rb.linearVelocity = velocity;
        if (Mathf.Abs(direction) > 0.01f)
        {
            SetFacing(direction);
        }
    }

    void SelectRecoveryTarget()
    {
        ClearRecoveryTarget();
        float bestScore = float.PositiveInfinity;

        EvaluateRecoveryTarget(mainStageCollider, ref bestScore);
        if (oneWayPlatformColliders != null)
        {
            foreach (Collider platform in oneWayPlatformColliders)
            {
                EvaluateRecoveryTarget(platform, ref bestScore);
            }
        }

        if (recoveryTargetPlatform != null)
        {
            RefreshRecoveryTargetCoordinates();
            return;
        }

        float safeHalfWidth = Mathf.Max(
            0.5f,
            mainStageHalfWidth - recoveryTargetInset
        );
        recoveryTargetX = Mathf.Clamp(
            transform.position.x,
            arenaCenterX - safeHalfWidth,
            arenaCenterX + safeHalfWidth
        );
        recoveryTargetY = offstageRecoveryHeight + 1f;
    }

    void EvaluateRecoveryTarget(
        Collider platform,
        ref float bestScore
    )
    {
        if (platform == null || !platform.enabled)
            return;

        Bounds bounds = platform.bounds;
        float maximumInset = Mathf.Max(0f, bounds.extents.x - 0.15f);
        if (maximumInset <= 0f)
            return;

        float requestedInset = platform == mainStageCollider
            ? recoveryTargetInset
            : platformLandingInset;
        float safeInset = Mathf.Min(
            Mathf.Max(0.1f, requestedInset),
            maximumInset
        );
        float safeX = Mathf.Clamp(
            transform.position.x,
            bounds.min.x + safeInset,
            bounds.max.x - safeInset
        );
        float safeY = bounds.max.y + 0.08f;
        float heightDelta = safeY - transform.position.y;
        if (heightDelta > recoveryMaximumTargetHeight)
            return;

        float horizontalDistance = Mathf.Abs(
            safeX - transform.position.x
        );
        float verticalDistance = Mathf.Abs(heightDelta);
        float score = Mathf.Sqrt(
            horizontalDistance * horizontalDistance +
            verticalDistance * verticalDistance
        ) + Mathf.Max(0f, heightDelta) * 0.25f;

        if (score >= bestScore)
            return;

        bestScore = score;
        recoveryTargetPlatform = platform;
        recoveryTargetX = safeX;
        recoveryTargetY = safeY;
    }

    void RefreshRecoveryTargetCoordinates()
    {
        if (recoveryTargetPlatform == null)
            return;

        Bounds bounds = recoveryTargetPlatform.bounds;
        float maximumInset = Mathf.Max(0f, bounds.extents.x - 0.15f);
        float requestedInset = recoveryTargetPlatform == mainStageCollider
            ? recoveryTargetInset
            : platformLandingInset;
        float safeInset = Mathf.Min(
            Mathf.Max(0.1f, requestedInset),
            maximumInset
        );
        recoveryTargetX = Mathf.Clamp(
            recoveryTargetX,
            bounds.min.x + safeInset,
            bounds.max.x - safeInset
        );
        recoveryTargetY = bounds.max.y + 0.08f;
    }

    void ClearRecoveryTarget()
    {
        recoveryTargetPlatform = null;
        recoveryTargetX = arenaCenterX;
        recoveryTargetY = offstageRecoveryHeight + 1f;
    }
}
