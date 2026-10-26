using UnityEngine;

// One floor policy for navigation and physical movers. Never warps or moves an actor.
public sealed class EnemyGroundQuery
{
    readonly Transform actor;
    readonly OverworldV2Layout layout;
    readonly RaycastHit[] hits = new RaycastHit[32];
    readonly WaterVolume[] waters;

    public EnemyGroundQuery(Transform actor)
    {
        this.actor = actor;
        foreach (var candidate in Object.FindObjectsByType<OverworldV2Layout>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == actor.gameObject.scene) { layout = candidate; break; }
        waters = Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None);
    }

    public bool IsSurface(Collider collider)
    {
        if (!collider || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy ||
            collider.gameObject.scene != actor.gameObject.scene || collider.transform.IsChildOf(actor) ||
            collider.GetComponentInParent<Health>() || collider.GetComponentInParent<WaterVolume>()) return false;
        // Legacy disposable fixtures have no authored layout; the nearest physical floor is used there.
        return !layout || layout.IsNavigationSurface(collider);
    }

    public bool TrySample(Vector3 feet, float rise, float fall, float slope, out RaycastHit floor, int mask = ~0)
    {
        floor = default;
        int count = Physics.RaycastNonAlloc(feet + Vector3.up * rise, Vector3.down, hits,
            rise + fall, mask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (!c || c.gameObject.scene != actor.gameObject.scene || c.transform.IsChildOf(actor) ||
                c.GetComponentInParent<Health>() || c.GetComponentInParent<WaterVolume>()) continue;
            if (hits[i].distance < nearest) { nearest = hits[i].distance; floor = hits[i]; }
        }
        // Do not ray through a non-walkable roof/obstacle to a floor underneath it.
        return nearest < float.PositiveInfinity && IsSurface(floor.collider) &&
            floor.normal.y >= Mathf.Cos(slope * Mathf.Deg2Rad) && !Submerged(floor.point);
    }

    public bool TryFootprint(Vector3 feet, Quaternion facing, Vector2 halfSize, float slope,
        float rise, float fall, out float highest, bool round = false, float curvedSpan = 0f)
    {
        highest = float.NegativeInfinity;
        if (!TrySample(feet, rise, fall, slope, out RaycastHit centre)) return false;
        highest = centre.point.y;
        float lowest = highest; bool planar = true;
        Vector3 right = facing * Vector3.right * halfSize.x;
        Vector3 forward = facing * Vector3.forward * halfSize.y;
        for (int i = 0; i < 8; i++)
        {
            Vector3 offset = i == 0 ? right : i == 1 ? -right : i == 2 ? forward : i == 3 ? -forward :
                i == 4 ? right + forward : i == 5 ? right - forward : i == 6 ? -right + forward : -right - forward;
            if (round && i >= 4) offset *= .7071068f;
            if (!TrySample(feet + offset, rise, fall, slope, out RaycastHit edge)) return false;
            float planeY = centre.point.y - Vector3.Dot(centre.normal, offset) / centre.normal.y;
            // Full body support, not just the centre. Small authored steps are allowed; cliffs aren't.
            if (Mathf.Abs(edge.point.y - planeY) > .15f)
            { if (curvedSpan <= 0f) return false; planar = false; }
            highest = Mathf.Max(highest, edge.point.y);
            lowest = Mathf.Min(lowest, edge.point.y);
        }
        return planar || highest - lowest <= curvedSpan;
    }

    public bool AllowsSupportContact(Collider obstacle, Vector3 feet)
    {
        // An approved terrain mesh can also have a steep side. Do not ignore the whole collider.
        if(!IsSurface(obstacle))return false;
        // ClosestPoint is unsupported for concave terrain meshes. Query the actual
        // local support face instead; never whitelist an entire terrain collider.
        if(obstacle is MeshCollider mesh&&!mesh.convex || obstacle is TerrainCollider)
            return obstacle.Raycast(new Ray(feet+Vector3.up*.13f,Vector3.down),out RaycastHit hit,.28f)&&
                hit.normal.y>=.819f&&hit.point.y<=feet.y+.045f&&hit.point.y>=feet.y-.15f;
        return obstacle.ClosestPoint(feet + Vector3.up * .1f).y <= feet.y + .045f;
    }

    bool Submerged(Vector3 point)
    {
        foreach (var water in waters)
        {
            if (!water || !water.isActiveAndEnabled || water.gameObject.scene != actor.gameObject.scene ||
                !water.IsValid || point.y >= water.WaterSurfaceY - .08f) continue;
            Vector3 probe = new Vector3(point.x, water.WaterSurfaceY - .05f, point.z);
            if (water.VolumeCollider.bounds.Contains(probe) &&
                (water.VolumeCollider.ClosestPoint(probe) - probe).sqrMagnitude < .0001f) return true;
        }
        return false;
    }
}
