using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Visual only: no damage, movement, material clones per enemy or extra lights.
// Two reusable mesh overlays show only the small area around the real hit point.
[DisallowMultipleComponent, RequireComponent(typeof(Health)), DefaultExecutionOrder(120)]
public sealed class EnemyImpactVfx : MonoBehaviour
{
    const float HitSeconds = .18f;
    const float HitRadius = .42f; // Appearance only. Never used by damage/collision.
    static Material sharedMaterial;
    static readonly Dictionary<Material, Material> coreMaterials = new Dictionary<Material, Material>();
    static int materialOwners;
    static Mesh chipMesh;
    static readonly int Point = Shader.PropertyToID("_ImpactPoint");
    static readonly int Radius = Shader.PropertyToID("_ImpactRadius");
    static readonly int Strength = Shader.PropertyToID("_ImpactStrength");
    struct Part { public MeshRenderer source; public MeshFilter filter; public Material[] slots; }
    readonly List<Part> parts = new List<Part>();
    readonly MeshRenderer[] overlays = new MeshRenderer[2];
    readonly MeshFilter[] overlayMeshes = new MeshFilter[2];
    readonly MeshRenderer[] selected = new MeshRenderer[2];
    readonly Vector3[] localContact = new Vector3[2];
    Health health;
    BaselineFlow flow;
    PlayerVitals player;
    MaterialPropertyBlock block;
    ParticleSystem chips;
    Vector3 hitPoint;
    float hitTime, hitEndsAt, landingTime, nextPlayerSearch;
    bool ownsMaterial;
    bool ranged;
    public bool HitVisible => hitTime > 0f && health && !health.IsDead && isActiveAndEnabled;
    public int VisibleOverlayCount => (overlays[0] && overlays[0].enabled ? 1 : 0) +
        (overlays[1] && overlays[1].enabled ? 1 : 0);
    public int ParticleCount => chips ? chips.particleCount : 0;
    public static Material SharedEnergyMaterial => sharedMaterial;

    public static void Ensure(GameObject actor)
    {
        if (!Application.isPlaying || !actor) return;
        if (!actor.GetComponent<EnemyImpactVfx>()) actor.AddComponent<EnemyImpactVfx>();
        actor.GetComponent<CombatFeedback>()?.UseLocalizedEnemyVisuals();
        EnemySoundEmitter.Ensure(actor);
        EnemyAttackPresentation.Ensure(actor);
    }

    // Non-uniform flicker, shared visual language for recovery/stagger (never a gameplay timer).
    public static float StunPower(float time) => .18f + .28f * Mathf.Clamp01(
        .5f + .3f * Mathf.Sin(time * 23f) + .25f * Mathf.Sin(time * 37f + .7f));

    void Awake()
    {
        health = GetComponent<Health>(); block = new MaterialPropertyBlock();
        var domain = GetComponent<DomainPrototypeEnemy>();
        ranged = domain && domain.EnemyKind == DomainPrototypeEnemy.Kind.LeafWisp;
        if (!sharedMaterial)
        {
            Shader shader = Resources.Load<Shader>("EnemyLocalImpact");
            if (!shader) { Debug.LogError("Enemy local impact shader missing.", this); return; }
            sharedMaterial = new Material(shader) { name = "Shared enemy local impact", hideFlags = HideFlags.DontSave };
        }
        materialOwners++; ownsMaterial = true;
        foreach (MeshRenderer source in GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh || !source.sharedMaterial || source.sharedMaterial.renderQueue >= 2500) continue;
            var slots = new Material[filter.sharedMesh.subMeshCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = sharedMaterial;
            parts.Add(new Part { source = source, filter = filter, slots = slots });
        }
        PrepareReadableCores();
        for (int i = 0; i < overlays.Length; i++)
        {
            var go = new GameObject("Reusable local hit patch " + i);
            go.transform.SetParent(transform, false);
            overlayMeshes[i] = go.AddComponent<MeshFilter>();
            overlays[i] = go.AddComponent<MeshRenderer>();
            overlays[i].enabled = false;
            overlays[i].shadowCastingMode = ShadowCastingMode.Off;
            overlays[i].receiveShadows = false;
            overlays[i].lightProbeUsage = LightProbeUsage.Off;
            overlays[i].reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        CreateChips();
    }

    void PrepareReadableCores()
    {
        Shader shader = Resources.Load<Shader>("EnemyReadableCore");
        if (!shader) { Debug.LogError("Enemy readable core shader missing.",this); return; }
        foreach (Part part in parts)
        {
            Material[] slots = part.source.sharedMaterials; bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                Material source = slots[i];
                if (!source || source.shader == shader || !(source.name.Contains("CyanCircuit") ||
                    source.name.Contains("AmberLens") || source.name.Contains("Repair lime core"))) continue;
                if (!coreMaterials.TryGetValue(source,out Material readable) || !readable)
                {
                    readable = new Material(shader) { name = source.name + " readable core", hideFlags = HideFlags.DontSave };
                    readable.CopyPropertiesFromMaterial(source); coreMaterials[source] = readable;
                }
                slots[i] = readable; changed = true;
            }
            if (changed) part.source.sharedMaterials = slots;
        }
    }

    void CreateChips()
    {
        var go = new GameObject("Reusable small ceramic chips"); go.transform.SetParent(transform, false);
        chips = go.AddComponent<ParticleSystem>();
        chips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = chips.main; main.playOnAwake = false; main.loop = false; main.duration = .3f;
        main.startLifetime = .28f; main.startSize = .035f; main.startSpeed = 0f;
        main.maxParticles = 12; main.gravityModifier = .8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = chips.emission; emission.enabled = false;
        var shape = chips.shape; shape.enabled = false;
        var render = go.GetComponent<ParticleSystemRenderer>(); render.sharedMaterial = sharedMaterial;
        render.shadowCastingMode = ShadowCastingMode.Off; render.receiveShadows = false;
        if (!chipMesh)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            chipMesh = cube.GetComponent<MeshFilter>().sharedMesh; Destroy(cube);
        }
        render.renderMode = ParticleSystemRenderMode.Mesh; render.mesh = chipMesh;
    }

    void OnEnable()
    {
        health.Damaged += OnDamage; health.Died += OnDeath; health.HealthChanged += OnHealthChanged;
        foreach (BaselineFlow candidate in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { flow = candidate; break; }
        if (flow) flow.WorldEncountersReset += Clear;
    }
    void OnDisable()
    {
        if (health) { health.Damaged -= OnDamage; health.Died -= OnDeath; health.HealthChanged -= OnHealthChanged; }
        if (flow) flow.WorldEncountersReset -= Clear;
        Clear();
    }
    void OnHealthChanged(Health actor, int before, int after) { if (before == 0 && after > 0) Clear(); }
    void OnDamage(Health actor, DamageInfo damage)
    {
        if (actor.IsDead) return; // Lethal damage never competes with the extinction/collapse.
        hitPoint = damage.HitPoint; hitTime = HitSeconds; hitEndsAt = Time.time + HitSeconds;
        HidePatches();
        for (int slot = 0; slot < overlays.Length; slot++)
        {
            float best = float.PositiveInfinity; int index = -1;
            for (int i = 0; i < parts.Count; i++)
            {
                MeshRenderer source = parts[i].source;
                if (!source || !source.enabled || !source.gameObject.activeInHierarchy || source == selected[0] && slot == 1) continue;
                float distance = (source.bounds.ClosestPoint(hitPoint) - hitPoint).sqrMagnitude;
                if (distance < best) { best = distance; index = i; }
            }
            if (index < 0 || best > HitRadius * HitRadius) continue;
            Part part = parts[index]; selected[slot] = part.source;
            localContact[slot] = part.source.transform.InverseTransformPoint(hitPoint);
            Transform holder = overlays[slot].transform; holder.SetParent(part.source.transform, false);
            holder.localPosition = Vector3.zero; holder.localRotation = Quaternion.identity; holder.localScale = Vector3.one;
            overlays[slot].gameObject.layer = part.source.gameObject.layer;
            // Charger feet have a per-instance ground-fit mesh; follow that buffer too.
            overlayMeshes[slot].sharedMesh = part.filter.sharedMesh; overlays[slot].sharedMaterials = part.slots;
        }
        Burst(hitPoint, ranged ? 7 : 5, ranged ? damage.HitDirection : Vector3.zero);
    }
    void OnDeath(Health actor, DamageInfo damage)
    {
        Clear();
        // Stone Scout already owns its landing chips; do not double its burst.
        if (!ranged && !GetComponent<StoneScoutFeedback>()) landingTime = .5f;
    }
    void LateUpdate()
    {
        if (!player && Time.unscaledTime >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.unscaledTime + 1f;
            foreach (PlayerVitals candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
        }
        if (player && !player.EnemyCombatActive) { Clear(); return; }
        if (!EnemyPresentationBudget.Details(transform.position) && chips && chips.particleCount > 0)
            chips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (landingTime > 0f)
        {
            landingTime = Mathf.Max(0f, landingTime - Time.deltaTime);
            if (landingTime == 0f && health.IsDead) Burst(transform.position + Vector3.up * .12f, 7);
        }
        // Never subtract the part of a long frame that elapsed BEFORE the hit. This
        // is also idempotent when an offscreen render asks for the current visual pose.
        hitTime = health.IsDead ? 0f : Mathf.Max(0f, hitEndsAt - Time.time);
        if (hitTime <= 0f) { HidePatches(); return; }
        block.SetFloat(Radius, HitRadius);
        block.SetFloat(Strength, hitTime / HitSeconds);
        if (ranged) block.SetColor("_ImpactTint", new Color(.08f,.8f,1f,1f));
        for (int i = 0; i < overlays.Length; i++)
        {
            if (!overlays[i]) continue;
            overlays[i].enabled = selected[i] && selected[i].enabled && selected[i].gameObject.activeInHierarchy;
            if (overlays[i].enabled)
            {
                // The flash sticks to the damaged panel while its hit pose recoils/tilts.
                block.SetVector(Point, selected[i].transform.TransformPoint(localContact[i]));
                overlays[i].SetPropertyBlock(block);
            }
        }
    }
    public void EmitLanding(Vector3 point) => Burst(point, 7, Vector3.zero);
    void Burst(Vector3 point, int count, Vector3 direction = default)
    {
        if (!chips || !EnemyPresentationBudget.Details(point)) return;
        chips.Play();
        for (int i = 0; i < count; i++)
        {
            float angle = i * 2.39996f;
            Vector3 velocity = new Vector3(Mathf.Cos(angle) * .6f, .65f + (i % 3) * .12f, Mathf.Sin(angle) * .6f);
            if (ranged && direction.sqrMagnitude > .01f) velocity += direction.normalized * .9f;
            var emit = new ParticleSystem.EmitParams {
                position = point + (ranged ? velocity * .022f : Vector3.zero), velocity = velocity,
                startColor = ranged ? (i % 3 == 0 ? new Color(.15f,.85f,1f,1f) : new Color(.9f,.88f,.72f,1f)) :
                    i % 2 == 0 ? new Color(.95f,.72f,.3f,1f) : new Color(.35f,.3f,.22f,1f),
                startSize = (ranged ? .043f : .034f) + (i % 3) * .008f
            };
            chips.Emit(emit, 1);
        }
    }
    void HidePatches()
    {
        for (int i = 0; i < overlays.Length; i++) { if (overlays[i]) overlays[i].enabled = false; selected[i] = null; }
    }
    public void Clear()
    {
        hitTime = hitEndsAt = landingTime = 0f; HidePatches();
        if (chips) chips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    void OnDestroy()
    {
        // Patches can be parented to a renderer outside this component's subtree.
        foreach (MeshRenderer overlay in overlays) if (overlay) Destroy(overlay.gameObject);
        if (chips) Destroy(chips.gameObject);
        if (ownsMaterial && --materialOwners == 0 && sharedMaterial)
        {
            Destroy(sharedMaterial); sharedMaterial = null;
            foreach (Material core in coreMaterials.Values) if (core) Destroy(core);
            coreMaterials.Clear();
        }
    }
}
