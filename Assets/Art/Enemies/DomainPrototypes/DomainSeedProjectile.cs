using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

// Manual sweep avoids tunnelling and makes trees, walls and terrain real cover.
[DisallowMultipleComponent]
public sealed class DomainSeedProjectile : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float speed = 11f;
    [SerializeField, Min(0.01f)] private float radius = 0.13f;
    [SerializeField, Min(0.1f)] private float lifetime = 1.7f;
    private static Mesh dartMesh;
    private static Material trailMaterial;
    private static Material bladeMaterial;
    private static int materialOwners;
    private GameObject owner;
    private Health ownerHealth;
    private PlayerVitals player;
    private Health playerHealth;
    private EnemyTransientPool poolOwner;
    private ObjectPool<DomainSeedProjectile> pool;
    private bool leased;
    private float remainingLifetime;
    public GameObject Owner => owner;
    public float CollisionRadius => radius;
    private static readonly HashSet<DomainSeedProjectile> active = new HashSet<DomainSeedProjectile>();
    private Vector3 direction;
    private int damage;
    private bool launched;
    private BaselineFlow flow;
    private Transform dartVisual;
    private TrailRenderer trail;
    private float spin;
    private readonly RaycastHit[] sweepHits = new RaycastHit[16];
    private readonly Collider[] initialOverlaps = new Collider[16];

    private void Awake()
    {
        // Keep the collision footprint small. The old round placeholder remains in the
        // prefab for its material references, but is not rendered in play mode.
        Transform oldBody = transform.Find("Graphite seed");
        Transform oldTip = transform.Find("Cyan tip");
        Material amber = oldBody ? oldBody.GetComponent<MeshRenderer>().sharedMaterial : null;
        Material cyan = oldTip ? oldTip.GetComponent<MeshRenderer>().sharedMaterial : null;
        if (oldBody) oldBody.GetComponent<MeshRenderer>().enabled = false;
        if (oldTip) oldTip.GetComponent<MeshRenderer>().enabled = false;
        if (!dartMesh) dartMesh = BuildDartMesh();
        materialOwners++;
        if (!bladeMaterial)
        {
            Shader readable = Resources.Load<Shader>("EnemyReadableCore");
            if (readable)
            {
                bladeMaterial = new Material(readable) { name="Shared readable leaf dart", hideFlags=HideFlags.DontSave };
                bladeMaterial.SetColor("_BaseColor",new Color(.08f,.8f,1f));
                bladeMaterial.SetColor("_EmissionColor",new Color(.12f,2.4f,3f));
            }
        }
        GameObject visual = new GameObject("Compact leaf-shard dart", typeof(MeshFilter), typeof(MeshRenderer));
        visual.transform.SetParent(transform, false);
        dartVisual = visual.transform;
        visual.GetComponent<MeshFilter>().sharedMesh = dartMesh;
        MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { bladeMaterial ? bladeMaterial : cyan, amber };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.12f;
        trail.startWidth = 0.12f;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.045f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.sharedMaterial = TrailMaterial(cyan);
        trail.startColor = new Color(0.28f, 0.97f, 1f, 0.75f);
        trail.endColor = new Color(0.28f, 0.97f, 1f, 0f);
    }

    private static Mesh BuildDartMesh()
    {
        Mesh mesh = new Mesh { name = "Small angular leaf dart", hideFlags = HideFlags.DontSave };
        mesh.vertices = new[] {
            new Vector3(0, 0, .27f), new Vector3(-.105f, 0, 0),
            new Vector3(0, .075f, 0), new Vector3(.105f, 0, 0),
            new Vector3(0, -.075f, 0), new Vector3(0, 0, -.19f)
        };
        mesh.subMeshCount = 2;
        mesh.SetTriangles(new[] { 0, 3, 2, 0, 2, 1, 0, 1, 4, 0, 4, 3 }, 0);
        mesh.SetTriangles(new[] { 5, 2, 3, 5, 1, 2, 5, 4, 1, 5, 3, 4 }, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material TrailMaterial(Material fallback)
    {
        if (trailMaterial) return trailMaterial;
        Shader shader = Resources.Load<Shader>("EnemyAttackEnergy");
        if (!shader) return fallback;
        trailMaterial = new Material(shader) { name = "Leaf dart trail (runtime)", hideFlags = HideFlags.DontSave };
        trailMaterial.SetFloat("_Mode", 0f);
        trailMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return trailMaterial;
    }

    private void OnEnable()
    {
        active.Add(this);
        flow = poolOwner ? poolOwner.Flow : FindFirstObjectByType<BaselineFlow>();
        if (flow) flow.WorldEncountersReset += OnJourneyReset;
    }

    private void OnDisable()
    {
        active.Remove(this);
        launched = false;
        if (ownerHealth) ownerHealth.Died -= OnOwnerDied;
        if (trail) { trail.emitting = false; trail.Clear(); }
        if (flow) flow.WorldEncountersReset -= OnJourneyReset;
        flow = null;
        if (leased && poolOwner && !poolOwner.Disposing) poolOwner.Return(this, pool);
        owner = null; ownerHealth = null; player = null; playerHealth = null;
    }

    private void OnDestroy()
    {
        if (--materialOwners != 0) return;
        if (bladeMaterial) Destroy(bladeMaterial);
        if (trailMaterial) Destroy(trailMaterial);
        if (dartMesh) Destroy(dartMesh);
        bladeMaterial = trailMaterial = null; dartMesh = null;
    }

    private void OnJourneyReset()
    {
        Retire();
    }

    private void OnOwnerDied(Health actor, DamageInfo info) => Retire();

    public static void ClearOwned(GameObject source)
    {
        EnemyTransientPool.CancelOwned(source);
        // Only allocates when an encounter is cancelled, not during normal projectile movement.
        List<DomainSeedProjectile> remove = null;
        foreach (var projectile in active)
            if (projectile && projectile.owner == source)
            { if (remove == null) remove = new List<DomainSeedProjectile>(); remove.Add(projectile); }
        if (remove != null) foreach (var projectile in remove) projectile.Retire();
        // Impacts also disappear on death/reset, rather than lingering in the cancelled encounter.
    }

    public void AttachPool(EnemyTransientPool ownerPool, ObjectPool<DomainSeedProjectile> sourcePool)
    { poolOwner = ownerPool; pool = sourcePool; leased = true; }
    public void DetachLease() => leased = false;

    public void Retire()
    {
        launched = false;
        if (poolOwner)
        {
            // Event invocation lists can still call a listener removed by an earlier
            // death/reset listener. Never destroy an already returned pooled instance.
            if (leased && !poolOwner.Disposing) poolOwner.Return(this, pool);
            return;
        }
        gameObject.SetActive(false); // Cancels collision/trail immediately, including at timeScale=0.
        Destroy(gameObject);
    }

    public void Launch(GameObject source, Vector3 aim, int amount)
    {
        owner = source;
        ownerHealth = owner ? owner.GetComponent<Health>() : null;
        if (ownerHealth) ownerHealth.Died += OnOwnerDied;
        player = poolOwner ? poolOwner.Player : null;
        if (!player)
            foreach (var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
        playerHealth = player ? player.GetComponent<Health>() : null;
        direction = aim.normalized;
        damage = amount;
        launched = true;
        remainingLifetime = lifetime; spin = 0f;
        if (dartVisual) dartVisual.localRotation = Quaternion.identity;
        if (trail) { trail.Clear(); trail.emitting = EnemyPresentationBudget.Details(transform.position); }
    }

    private void Update()
    {
        if (!launched) return;
        if (!owner || !owner.activeInHierarchy || (ownerHealth && ownerHealth.IsDead) ||
            !player || player.gameObject.scene != gameObject.scene ||
            !playerHealth || playerHealth.IsDead) { Retire(); return; }
        if(!player.EnemyCombatActive)
        {
            var awareness=owner.GetComponent<EnemyAwareness>();
            if(awareness && awareness.CanRetainEncounter)return; // Pause existing darts, don't erase them.
            Retire();return;
        }
        if (Time.deltaTime <= 0f) return;
        remainingLifetime -= Time.deltaTime;
        if (remainingLifetime <= 0f) { Retire(); return; }
        bool detail = EnemyPresentationBudget.Details(transform.position);
        if (trail && trail.emitting != detail) { trail.emitting = detail; if (!detail) trail.Clear(); }
        Vector3 start = transform.position;
        // SphereCast omits starting overlaps. Never escape through a wall around the muzzle.
        int initial = Physics.OverlapSphereNonAlloc(start, radius, initialOverlaps, ~0,
            QueryTriggerInteraction.Ignore);
        if (initial == initialOverlaps.Length) { Retire(); return; }
        Collider initialPlayer = null;
        for (int i = 0; i < initial; i++)
        {
            Collider c = initialOverlaps[i];
            if (!c || (owner && c.transform.IsChildOf(owner.transform))) continue;
            if (c.GetComponentInParent<PlayerVitals>() == player) initialPlayer = c;
            else { Retire(); return; } // A coincident solid wall takes priority over player contact.
        }
        if (initialPlayer) { Impact(initialPlayer, start); return; }
        float step = speed * Time.deltaTime;
        int count = Physics.SphereCastNonAlloc(start, radius, direction, sweepHits, step,
            ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = count == sweepHits.Length
            ? Physics.SphereCastAll(start, radius, direction, step, ~0, QueryTriggerInteraction.Ignore)
            : sweepHits;
        if (count == sweepHits.Length) count = hits.Length;
        float nearest = float.MaxValue;
        Collider first = null;
        RaycastHit nearestHit = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (owner && hit.collider.transform.IsChildOf(owner.transform)) continue;
            bool wall = hit.collider.GetComponentInParent<PlayerVitals>() != player;
            if (hit.distance > nearest + .001f || (hit.distance >= nearest && !wall)) continue;
            nearest = hit.distance;
            first = hit.collider;
            nearestHit = hit;
        }
        if (first)
        {
            Vector3 impactPoint = nearestHit.point;
            Impact(first, impactPoint);
            return;
        }
        transform.position = start + direction * step;
        spin += 690f * Time.deltaTime;
        if (dartVisual && detail) dartVisual.localRotation = Quaternion.Euler(0, 0, spin);
    }

    private void Impact(Collider collider, Vector3 point)
    {
        if (collider.GetComponentInParent<PlayerVitals>() == player && player.EnemyCombatActive)
        {
            if (playerHealth.TryApplyDamage(new DamageInfo(damage, owner, point, direction)))
            {
                // A tiny collision-constrained nudge, not the charger's heavy stun
                // and not another camera shake. Never replace an existing impact.
                var movement = player.GetComponent<PlayerMovement>();
                if (movement && !movement.ImpactActive)
                    movement.ApplyImpact(Vector3.ProjectOnPlane(direction,Vector3.up).normalized * 1.8f, .12f);
            }
        }
        CreateImpactFlash(point);
        Retire();
    }

    private void CreateImpactFlash(Vector3 point)
    {
        if (owner) owner.GetComponent<EnemyAttackPresentation>()?.Contact(point, new Color(.12f,.85f,1f));
        var effects = poolOwner ? poolOwner : EnemyTransientPool.For(gameObject.scene);
        if (effects) effects.Impact(owner, point, trail ? trail.sharedMaterial : null);
    }
}
