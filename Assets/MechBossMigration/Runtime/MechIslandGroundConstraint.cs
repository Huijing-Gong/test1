using UnityEngine;

namespace KingOfGraphic.MechBossMigration
{
    // Bound to the island's real ground collider, never to water or another actor.
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class MechIslandGroundConstraint : MonoBehaviour
    {
        public Transform arenaCenter;
        public Collider groundCollider;
        [Min(.5f)] public float moveRadius = 6.2f;
        [Min(.5f)] public float playerContainmentRadius = 7.7f;
        [Min(.1f)] public float footprintRadius = 1.35f;
        public float waterSurfaceHeight = 3.7f;
        [Min(0)] public float waterClearance = .4f;
        [Min(.05f)] public float maximumDrop = .4f;
        [Min(.05f)] public float maximumRise = .35f;
        [Range(0, 1)] public float minimumGroundNormal = .8f;

        Vector3 lastSafePosition;
        Health health;
        bool hasSafePosition;

        void Awake() => health = GetComponent<Health>();

        public bool CanEngagePlayer(Vector3 point)
        {
            Vector3 center = arenaCenter ? arenaCenter.position : transform.position;
            return Vector3.ProjectOnPlane(point - center, Vector3.up).magnitude <= playerContainmentRadius &&
                   Mathf.Abs(point.y - center.y) <= 1.5f && HasSupport(point);
        }

        public bool HasSupport(Vector3 point)
        {
            if (!groundCollider || !groundCollider.enabled || groundCollider.isTrigger) return false;
            float level = arenaCenter ? arenaCenter.position.y : transform.position.y;
            for (int sample = -1; sample < 8; sample++)
            {
                float angle = sample * Mathf.PI * .25f;
                Vector3 offset = sample < 0 ? Vector3.zero :
                    new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * footprintRadius;
                Vector3 origin = point + offset;
                origin.y = level + maximumRise + .05f;
                if (!groundCollider.Raycast(new Ray(origin, Vector3.down), out var hit,
                        maximumRise + maximumDrop + .1f) ||
                    hit.normal.y < minimumGroundNormal ||
                    hit.point.y <= waterSurfaceHeight + waterClearance ||
                    hit.point.y < level - maximumDrop || hit.point.y > level + maximumRise)
                    return false;
            }
            return true;
        }

        public Vector3 ConstrainDisplacement(Vector3 origin, Vector3 displacement)
        {
            if (!HasSupport(origin)) return Vector3.zero;
            Vector3 center = arenaCenter ? arenaCenter.position : transform.position;
            Vector3 target = origin + displacement;
            Vector3 radial = Vector3.ProjectOnPlane(target - center, Vector3.up);
            target = center + Vector3.ClampMagnitude(radial, moveRadius);
            target.y = center.y;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(origin, target) / .2f));
            Vector3 safe = origin;
            for (int step = 1; step <= steps; step++)
            {
                Vector3 next = Vector3.Lerp(origin, target, step / (float)steps);
                if (!HasSupport(next)) break;
                safe = next;
            }
            return safe - origin;
        }

        void LateUpdate()
        {
            // Do not pull the death animation or settled corpse back upright.
            if (health && health.IsDead) return;
            Vector3 center = arenaCenter ? arenaCenter.position : transform.position;
            Vector3 current = transform.position;
            bool contained = Vector3.ProjectOnPlane(current - center, Vector3.up).magnitude <= moveRadius + .001f;
            if (contained && Mathf.Abs(current.y - center.y) < .05f && HasSupport(current))
            {
                lastSafePosition = current;
                hasSafePosition = true;
                return;
            }
            if (!hasSafePosition && HasSupport(center))
            {
                lastSafePosition = center;
                hasSafePosition = true;
            }
            if (hasSafePosition)
            {
                lastSafePosition += ConstrainDisplacement(lastSafePosition, current - lastSafePosition);
                transform.position = lastSafePosition;
            }
        }
    }
}
