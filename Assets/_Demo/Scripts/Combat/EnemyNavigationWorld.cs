using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Query-only navigation: never moves an actor or relaxes its physical collision checks.
[DefaultExecutionOrder(-500)]
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class EnemyNavigationWorld : MonoBehaviour
{
    public enum Profile { StoneScout, LeafWisp, RuinPrism, RailBeetle }
    [SerializeField] private NavMeshData[] bakedData = new NavMeshData[4];
    [SerializeField] private int[] agentTypes = new int[4];
    private readonly NavMeshDataInstance[] instances = new NavMeshDataInstance[4];
    public bool Ready(Profile profile) => isActiveAndEnabled && bakedData != null &&
        bakedData.Length > (int)profile && bakedData[(int)profile] && instances[(int)profile].valid;

    private void OnEnable()
    {
        for (int i = 0; i < instances.Length; i++)
            if (bakedData != null && i < bakedData.Length && bakedData[i])
                instances[i] = NavMesh.AddNavMeshData(bakedData[i]);
    }

    private void OnDisable()
    {
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i].valid) instances[i].Remove();
            instances[i] = default;
        }
    }

    public static EnemyNavigationWorld Find(Scene scene)
    {
        foreach (var item in FindObjectsByType<EnemyNavigationWorld>(FindObjectsSortMode.None))
            if (item.gameObject.scene == scene && item.isActiveAndEnabled) return item;
        return null;
    }

    public bool Sample(Profile profile, Vector3 position, float range, out Vector3 point)
    {
        point = default;
        if (!Ready(profile)) return false;
        var filter = new NavMeshQueryFilter { agentTypeID = agentTypes[(int)profile], areaMask = 1 };
        if (!NavMesh.SamplePosition(position, out NavMeshHit hit, range, filter)) return false;
        point = hit.position;
        return true;
    }

    public bool Calculate(Profile profile, Vector3 start, Vector3 goal, NavMeshPath path)
    {
        if (!Ready(profile)) return false;
        var filter = new NavMeshQueryFilter { agentTypeID = agentTypes[(int)profile], areaMask = 1 };
        return NavMesh.CalculatePath(start, goal, filter, path) && path.status != NavMeshPathStatus.PathInvalid;
    }
}

// One small, reusable route per enemy. Replans are bounded and staggered, not per physics tick.
public sealed class EnemyNavigationRoute
{
    private NavMeshPath path, onward;
    private readonly Vector3[] corners = new Vector3[64], trial = new Vector3[64], tail = new Vector3[64];
    private EnemyNavigationWorld world;
    private Vector3 goal, lastPosition, rejectedPoint;
    private float clock, nextPlan, stalled, rejectionUntil;
    private int count, index, sceneHandle;
    private bool initialized, rejected;
    public bool Available { get; private set; }
    public bool Complete { get; private set; }
    public bool Direct => count == 2 && !rejected;
    public bool WaitingAtEnd { get; private set; }

    public bool Direction(Scene scene, EnemyNavigationWorld.Profile profile, Vector3 position,
        Vector3 destination, float dt, out Vector3 direction)
    {
        direction = Vector3.zero; clock += Mathf.Max(0f, dt);
        // Native path handles must be created on the main thread, never in an actor's field initializer.
        if (path == null) { path = new NavMeshPath(); onward = new NavMeshPath(); }
        if (!world || sceneHandle != scene.handle)
        { world = EnemyNavigationWorld.Find(scene); sceneHandle = scene.handle; }
        Available = world && world.Ready(profile);
        if (!Available) return false; // Legacy disposable fixtures still use their existing safe local mover.
        if (initialized && !WaitingAtEnd)
        {
            if (Flat(position - lastPosition) > .08f) { stalled = 0f; lastPosition = position; }
            else stalled += Mathf.Max(0f, dt);
            if (stalled >= 1.4f) { Reject(position + directionToNext(position) * .9f); stalled = 0f; }
        }
        if (!initialized) lastPosition = position;
        bool movedGoal = Flat(destination - goal) > 1f;
        if (!initialized || clock >= nextPlan || (movedGoal && clock >= nextPlan - .45f))
        {
            goal = destination; initialized = true;
            Plan(profile, position, destination);
            nextPlan = clock + .8f + Mathf.Abs(sceneHandle % 7) * .015f;
        }
        WaitingAtEnd = false;
        while (index < count && Flat(corners[index] - position) < .12f) index++;
        if (index >= count) { WaitingAtEnd = count > 0; return false; }
        direction = directionToNext(position);
        return direction.sqrMagnitude > .001f;
    }

    private Vector3 directionToNext(Vector3 position)
    {
        Vector3 result = index < count ? corners[index] - position : goal - position;
        result.y = 0f;
        return result.sqrMagnitude > .001f ? result.normalized : Vector3.zero;
    }

    public void Blocked(Vector3 position, Vector3 direction)
    {
        // Don't switch sides each frame. A rejected region is remembered for several seconds.
        if (rejected && clock < nextPlan) return;
        Reject(position + direction.normalized * .9f);
    }

    private void Reject(Vector3 point)
    {
        rejected = true; rejectedPoint = point; rejectionUntil = clock + 4f;
        nextPlan = clock; Complete = false;
    }

    private void Plan(EnemyNavigationWorld.Profile profile, Vector3 position, Vector3 destination)
    {
        count = index = 0; Complete = false;
        if (rejected && clock >= rejectionUntil) rejected = false;
        float heightTolerance = profile == EnemyNavigationWorld.Profile.RailBeetle ? 1f :
            profile == EnemyNavigationWorld.Profile.RuinPrism ? .8f : .4f;
        if (!world.Sample(profile, position, 2f, out Vector3 start) || Flat(start - position) > 1.6f ||
            Mathf.Abs(start.y - position.y) > heightTolerance ||
            !world.Sample(profile, destination, 2.5f, out Vector3 end) || Flat(end - destination) > 1.6f ||
            Mathf.Abs(end.y - destination.y) > heightTolerance ||
            !world.Calculate(profile, start, end, path)) return;
        int n = path.GetCornersNonAlloc(trial);
        if (n < 2 || n >= trial.Length) return;
        Complete = path.status == NavMeshPathStatus.PathComplete && Flat(end - destination) < .8f;
        System.Array.Copy(trial, corners, n); count = n;
        if (rejected)
        {
            float best = Cost(trial, n, position);
            Vector3 forward = destination - position; forward.y = 0f;
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            forward.Normalize();
            // Few bounded alternative waypoints. Physical movers still validate each actual step.
            for (int i = 0; i < 8; i++)
            {
                float angle = i % 4 < 2 ? (i % 2 == 0 ? 90f : -90f) : (i % 2 == 0 ? 150f : -150f);
                Vector3 candidate = start + Quaternion.Euler(0f, angle, 0f) * forward * (i < 4 ? 2.4f : 4f);
                if (!world.Sample(profile, candidate, 1f, out Vector3 via) || Flat(via - candidate) > .7f ||
                    !world.Calculate(profile, start, via, path) || path.status != NavMeshPathStatus.PathComplete ||
                    !world.Calculate(profile, via, end, onward)) continue;
                int a = path.GetCornersNonAlloc(trial), b = onward.GetCornersNonAlloc(tail);
                if (a < 2 || b < 2 || a + b - 1 >= corners.Length) continue;
                float cost = Cost(trial, a, position) + Cost(tail, b, via);
                if (cost >= best) continue;
                best = cost; System.Array.Copy(trial, corners, a);
                System.Array.Copy(tail, 1, corners, a, b - 1); count = a + b - 1;
                Complete = onward.status == NavMeshPathStatus.PathComplete && Flat(end - destination) < .8f;
            }
        }
        // Do not skip the entry point if the body starts near, but not on, the mesh.
        index = Flat(corners[0] - position) < .12f ? 1 : 0;
    }

    private float Cost(Vector3[] points, int length, Vector3 from)
    {
        float cost = 0f;
        for (int i = 0; i < length; i++)
        {
            Vector3 to = points[i]; cost += Flat(to - from);
            if (rejected && i > 0)
            {
                Vector3 a = from, b = to, p = rejectedPoint; a.y = b.y = p.y = 0f;
                Vector3 line = b - a;
                float t = line.sqrMagnitude < .001f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, line) / line.sqrMagnitude);
                if (Vector3.Distance(p, a + line * t) < .85f) cost += 30f;
            }
            from = to;
        }
        return cost;
    }

    public void Clear()
    {
        count = index = 0; initialized = rejected = WaitingAtEnd = Complete = Available = false;
        clock = nextPlan = stalled = rejectionUntil = 0f;
        world = null;
    }

    private static float Flat(Vector3 value) { value.y = 0f; return value.magnitude; }
}
