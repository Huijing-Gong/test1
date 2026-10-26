using UnityEngine;

internal static class BattleMeleeQuery
{
    public static bool TryFindPlayerJTarget(Transform attacker,float direction,BattleJMoveData data,
        BattleJMove move,Collider[] buffer,out EnemyDamage target,out Vector3 contact)
    {
        target=null; contact=default;
        Vector3 center=attacker.position+new Vector3(data.Center.x*direction,data.Center.y,0);
        int count=Physics.OverlapBoxNonAlloc(center,data.HalfExtents,buffer,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
        float closest=float.PositiveInfinity;
        for(int i=0;i<count;i++)
        {
            var hit=buffer[i]; var candidate=hit.GetComponentInParent<EnemyDamage>();
            if(!candidate || !candidate.CompareTag("Enemy") || !candidate.enabled) continue;
            Vector3 offset=candidate.transform.position-attacker.position;
            if(move==BattleJMove.Upper)
            {if(offset.y<-.25f || Mathf.Abs(offset.x)>1.35f) continue;}
            else if(offset.x*direction<=.05f) continue;
            // Low attacks are foot-level: a raised/airborne target isn't swept by a broad sphere.
            if(move==BattleJMove.Low && hit.bounds.min.y-(attacker.position.y-1f)>.65f) continue;
            Vector3 point=hit.ClosestPoint(center);
            if(Physics.Linecast(center,point,out RaycastHit obstruction,1<<0,QueryTriggerInteraction.Ignore) &&
                !obstruction.collider.transform.IsChildOf(attacker) && !obstruction.collider.transform.IsChildOf(candidate.transform)) continue;
            float distance=(point-center).sqrMagnitude;
            if(distance>=closest) continue;
            closest=distance; target=candidate;
            // Keep the original hit selection and obstruction test. Only the
            // visual mark is projected from outside onto the struck surface;
            // ClosestPoint(center) alone returns an interior point on overlap.
            Vector3 surfaceOrigin = new Vector3(direction>0 ? hit.bounds.min.x-.1f : hit.bounds.max.x+.1f,center.y,center.z);
            contact=hit.ClosestPoint(surfaceOrigin);
        }
        return target!=null;
    }

    public static bool TryFindForwardTarget<T>(
        Vector3 center,
        float range,
        string targetTag,
        Transform attacker,
        float horizontalDirection,
        out T target
    ) where T : Component
    {
        target = null;
        if (attacker == null)
            return false;

        float direction = horizontalDirection >= 0f ? 1f : -1f;
        Collider[] hitObjects = Physics.OverlapSphere(center, range);

        foreach (Collider hit in hitObjects)
        {
            if (!hit.CompareTag(targetTag))
                continue;

            T candidate = hit.GetComponentInParent<T>();
            if (candidate == null)
                continue;

            float targetOffset =
                candidate.transform.position.x - attacker.position.x;
            if (targetOffset * direction <= 0.05f)
                continue;

            target = candidate;
            return true;
        }

        return false;
    }

    public static bool TryFindUpperTarget<T>(
        Vector3 center,
        float range,
        string targetTag,
        Transform attacker,
        float minimumVerticalOffset,
        float horizontalTolerance,
        out T target
    ) where T : Component
    {
        target = null;
        if (attacker == null)
            return false;

        Collider[] hitObjects = Physics.OverlapSphere(center, range);

        foreach (Collider hit in hitObjects)
        {
            if (!hit.CompareTag(targetTag))
                continue;

            T candidate = hit.GetComponentInParent<T>();
            if (candidate == null)
                continue;

            Vector3 targetOffset =
                candidate.transform.position - attacker.position;
            targetOffset.z = 0f;
            if (targetOffset.y < minimumVerticalOffset ||
                Mathf.Abs(targetOffset.x) > horizontalTolerance)
            {
                continue;
            }

            target = candidate;
            return true;
        }

        return false;
    }
}
