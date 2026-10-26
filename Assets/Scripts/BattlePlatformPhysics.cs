using UnityEngine;

internal static class BattlePlatformPhysics
{
    public static void ApplyExtraGravity(
        Rigidbody body,
        float riseGravityMultiplier,
        float fallGravityMultiplier
    )
    {
        if (body == null)
            return;

        float gravityMultiplier = body.linearVelocity.y > 0f
            ? riseGravityMultiplier
            : fallGravityMultiplier;
        float extraGravity = Mathf.Max(1f, gravityMultiplier) - 1f;

        body.AddForce(
            Physics.gravity * extraGravity,
            ForceMode.Acceleration
        );
    }

    public static bool IsGrounded(
        Transform fighter,
        Rigidbody body,
        bool forceAirborne,
        int groundLayerMask,
        float checkDistance = 1.1f
    )
    {
        if (fighter == null || forceAirborne)
            return false;

        if (body != null && body.linearVelocity.y > 0.1f)
            return false;

        return Physics.Raycast(
            fighter.position,
            Vector3.down,
            checkDistance,
            groundLayerMask,
            QueryTriggerInteraction.Ignore
        );
    }

    public static Collider FindOneWayPlatformBelow(
        Vector3 origin,
        float sphereRadius,
        float castDistance,
        int groundLayerMask,
        bool requireNamedArenaPlatform
    )
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            sphereRadius,
            Vector3.down,
            castDistance,
            groundLayerMask,
            QueryTriggerInteraction.Ignore
        );

        Collider nearestPlatform = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.distance < nearestDistance &&
                IsOneWayPlatform(
                    hit.collider,
                    requireNamedArenaPlatform
                ))
            {
                nearestPlatform = hit.collider;
                nearestDistance = hit.distance;
            }
        }

        return nearestPlatform;
    }

    public static bool IsOneWayPlatform(
        Collider platformCollider,
        bool requireNamedArenaPlatform
    )
    {
        if (platformCollider == null)
            return false;

        if (platformCollider.GetComponent<BattleDropThroughPlatform>() !=
            null)
        {
            return true;
        }

        Transform platformRoot = platformCollider.transform.parent;
        if (platformCollider.name != "OneWayCollisionSurface" ||
            platformRoot == null)
        {
            return false;
        }

        if (!requireNamedArenaPlatform)
            return true;

        return platformRoot.name == "LeftPlatform" ||
            platformRoot.name == "CentrePlatform" ||
            platformRoot.name == "RightPlatform";
    }

    public static Collider[] GetPlatformColliders(
        Collider platformCollider
    )
    {
        if (platformCollider == null)
            return null;

        Transform platformRoot = platformCollider.transform.parent;
        return platformRoot != null
            ? platformRoot.GetComponentsInChildren<Collider>(true)
            : new[] { platformCollider };
    }

    public static bool IsClearBelowPlatform(
        Collider platformCollider,
        Collider[] fighterColliders
    )
    {
        if (platformCollider == null || fighterColliders == null)
            return true;

        float platformBottom = platformCollider.bounds.min.y - 0.05f;

        foreach (Collider fighterCollider in fighterColliders)
        {
            if (fighterCollider != null &&
                fighterCollider.enabled &&
                !fighterCollider.isTrigger &&
                fighterCollider.bounds.max.y >= platformBottom)
            {
                return false;
            }
        }

        return true;
    }

    public static void SetCollisionsIgnored(
        Collider[] fighterColliders,
        Collider[] platformColliders,
        bool shouldIgnore
    )
    {
        if (platformColliders == null || fighterColliders == null)
            return;

        foreach (Collider platformCollider in platformColliders)
        {
            if (platformCollider == null)
                continue;

            foreach (Collider fighterCollider in fighterColliders)
            {
                if (fighterCollider != null &&
                    !fighterCollider.isTrigger &&
                    fighterCollider != platformCollider)
                {
                    Physics.IgnoreCollision(
                        fighterCollider,
                        platformCollider,
                        shouldIgnore
                    );
                }
            }
        }
    }
}
