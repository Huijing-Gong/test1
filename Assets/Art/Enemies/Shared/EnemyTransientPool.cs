using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

// Scene-local, bounded transient effects. Nothing survives a world unload.
// ObjectPool.maxSize only caps retained objects: check CountActive BEFORE Get too.
[DisallowMultipleComponent]
public sealed class EnemyTransientPool : MonoBehaviour
{
    public const int ProjectileLimit = 24, ImpactLimit = 8;
    static readonly Dictionary<int, EnemyTransientPool> scenes = new Dictionary<int, EnemyTransientPool>();
    readonly Dictionary<GameObject, ObjectPool<DomainSeedProjectile>> darts =
        new Dictionary<GameObject, ObjectPool<DomainSeedProjectile>>();
    readonly List<DomainSeedProjectile> liveDarts = new List<DomainSeedProjectile>(ProjectileLimit);
    readonly List<Flash> liveFlashes = new List<Flash>(ImpactLimit);
    ObjectPool<Flash> flashes;
    Transform parking;
    int sceneHandle;
    public BaselineFlow Flow { get; private set; }
    public PlayerVitals Player { get; private set; }
    public int CreatedDarts { get; private set; }
    public int CreatedImpacts { get; private set; }
    public int DroppedDarts { get; private set; }
    public int ActiveDarts => liveDarts.Count;
    public int ActiveImpacts => liveFlashes.Count;
    public bool Disposing { get; private set; }
    float nextPlayerSearch;

    sealed class Flash
    {
        public GameObject go, owner;
        public ParticleSystem particles;
        public ParticleSystemRenderer renderer;
        public float ends;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => scenes.Clear();

    public static EnemyTransientPool For(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        if (scenes.TryGetValue(scene.handle, out var pool) && pool && !pool.Disposing) return pool;
        var go = new GameObject("Enemy transient pool (24 darts / 8 impacts)");
        SceneManager.MoveGameObjectToScene(go, scene);
        return go.AddComponent<EnemyTransientPool>();
    }
    public static void CancelOwned(GameObject owner)
    {
        if (owner && scenes.TryGetValue(owner.scene.handle, out var pool) && pool && !pool.Disposing)
            pool.ClearOwned(owner);
    }

    void Awake()
    {
        sceneHandle = gameObject.scene.handle; scenes[sceneHandle] = this;
        var shelf = new GameObject("Inactive projectile shelf"); shelf.transform.SetParent(transform, false);
        shelf.SetActive(false); parking = shelf.transform;
        FindContext();
        flashes = new ObjectPool<Flash>(CreateFlash, null, f => {
            f.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            f.go.SetActive(false); f.owner = null;
        }, f => { if (f.go) Destroy(f.go); }, true, 4, ImpactLimit);
    }

    void FindContext()
    {
        nextPlayerSearch = Time.unscaledTime + 1f;
        foreach (var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { Player = candidate; break; }
        BaselineFlow found = null;
        foreach (var candidate in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { found = candidate; break; }
        if (Flow != found)
        {
            if (Flow) Flow.WorldEncountersReset -= Clear;
            Flow = found;
            if (Flow) Flow.WorldEncountersReset += Clear;
        }
    }

    ObjectPool<DomainSeedProjectile> DartPool(GameObject prefab)
    {
        if (darts.TryGetValue(prefab, out var pool)) return pool;
        pool = new ObjectPool<DomainSeedProjectile>(() => {
            // Inactive parent prevents OnEnable before the scene/context/position is assigned.
            var go = Instantiate(prefab, parking); go.SetActive(false);
            var dart = go.GetComponent<DomainSeedProjectile>();
            go.transform.SetParent(transform, false); CreatedDarts++; return dart;
        }, null, p => p.gameObject.SetActive(false), p => {
            if (p) Destroy(p.gameObject);
        }, true, 4, ProjectileLimit);
        darts.Add(prefab, pool);
        return pool;
    }

    public DomainSeedProjectile Spawn(GameObject prefab, GameObject owner, Vector3 position, Vector3 aim, int damage)
    {
        if (Disposing || !isActiveAndEnabled || !prefab || !owner || owner.scene != gameObject.scene) return null;
        if (liveDarts.Count >= ProjectileLimit) { DroppedDarts++; return null; }
        if (!Player && Time.unscaledTime >= nextPlayerSearch) FindContext();
        var pool = DartPool(prefab);
        if (pool.CountInactive == 0)
        {
            int retained = 0; foreach (var item in darts.Values) retained += item.CountAll;
            // Bound total retained objects too, even if a future enemy uses a second dart prefab.
            if (retained >= ProjectileLimit) { DroppedDarts++; return null; }
        }
        var dart = pool.Get();
        dart.AttachPool(this, pool); liveDarts.Add(dart);
        dart.transform.SetPositionAndRotation(position, Quaternion.LookRotation(aim.sqrMagnitude > .0001f ? aim : Vector3.forward));
        dart.gameObject.SetActive(true);
        dart.Launch(owner, aim, damage);
        return dart;
    }

    public void Return(DomainSeedProjectile dart, ObjectPool<DomainSeedProjectile> pool)
    {
        if (!liveDarts.Remove(dart)) return; // Re-entrant death/OnDisable/reset is harmless.
        dart.DetachLease();
        if (!Disposing) pool.Release(dart);
    }

    Flash CreateFlash()
    {
        var go = new GameObject("Reusable leaf dart impact"); go.transform.SetParent(transform, false);
        var particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = main.playOnAwake = false; main.duration = .16f; main.startLifetime = .16f;
        main.startSpeed = 1f; main.startSize = .055f; main.startColor = new Color(.33f, .98f, 1f);
        main.maxParticles = 8; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .025f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        go.SetActive(false); CreatedImpacts++;
        return new Flash { go = go, particles = particles, renderer = renderer };
    }

    public void Impact(GameObject owner, Vector3 point, Material material)
    {
        if (Disposing || !isActiveAndEnabled || !EnemyPresentationBudget.Details(point) || liveFlashes.Count >= ImpactLimit) return;
        var flash = flashes.Get(); flash.owner = owner; flash.ends = Time.time + .2f;
        flash.go.transform.position = point; flash.renderer.sharedMaterial = material;
        flash.go.SetActive(true); flash.particles.Clear(); flash.particles.Play(); flash.particles.Emit(7);
        liveFlashes.Add(flash);
    }

    void Update()
    {
        if (Player && !Player.EnemyCombatActive)
        {
            if(EnemyAwareness.CanRetainWorld(Player,Flow))return; // Freeze, don't delete in-flight attacks on J/M/Esc.
            Clear(); return;
        }
        for (int i = liveFlashes.Count - 1; i >= 0; i--)
        {
            Flash flash = liveFlashes[i];
            if (flash.owner && flash.owner.activeInHierarchy && Time.time < flash.ends &&
                EnemyPresentationBudget.Details(flash.go.transform.position)) continue;
            liveFlashes.RemoveAt(i); flashes.Release(flash);
        }
    }

    public void ClearOwned(GameObject owner)
    {
        for (int i = liveDarts.Count - 1; i >= 0; i--)
            if (liveDarts[i].Owner == owner) liveDarts[i].Retire();
        for (int i = liveFlashes.Count - 1; i >= 0; i--)
            if (liveFlashes[i].owner == owner)
            { var flash = liveFlashes[i]; liveFlashes.RemoveAt(i); flashes.Release(flash); }
    }

    public void Clear()
    {
        while (liveDarts.Count > 0)
        {
            var dart = liveDarts[liveDarts.Count - 1];
            if (dart) dart.Retire(); else liveDarts.RemoveAt(liveDarts.Count - 1);
        }
        for (int i = liveFlashes.Count - 1; i >= 0; i--) flashes.Release(liveFlashes[i]);
        liveFlashes.Clear();
    }

    void OnDisable() { if (!Disposing) Clear(); }
    void OnDestroy()
    {
        Disposing = true;
        if (Flow) Flow.WorldEncountersReset -= Clear;
        if (scenes.TryGetValue(sceneHandle, out var pool) && pool == this) scenes.Remove(sceneHandle);
        foreach (var item in darts.Values) item.Dispose();
        flashes?.Dispose();
        liveDarts.Clear(); liveFlashes.Clear();
    }
}
