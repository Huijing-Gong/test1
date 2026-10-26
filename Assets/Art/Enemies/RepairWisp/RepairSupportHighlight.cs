using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Runtime-only overlay: shared meshes/material, no changes to the original material/property block.
[DisallowMultipleComponent, DefaultExecutionOrder(110)]
public sealed class RepairSupportHighlight : MonoBehaviour
{
    public const int MaximumOverlays = 4;
    struct Layer { public Renderer source, overlay; }
    struct Candidate { public Renderer source; public Mesh mesh; public float score; }
    readonly List<Layer> layers = new List<Layer>();
    static readonly int Strength = Shader.PropertyToID("_SupportStrength");
    static readonly int Progress = Shader.PropertyToID("_RepairProgress");
    static readonly int Origin = Shader.PropertyToID("_SupportOrigin");
    static readonly int Radius = Shader.PropertyToID("_SupportRadius");
    static readonly int ScanPhase = Shader.PropertyToID("_RepairScanPhase");
    MaterialPropertyBlock block;
    Health health;
    Collider body;
    Renderer anchor;
    Transform torso;
    EnemyImpactVfx impact;
    RepairWispSupportVfx owner;
    HealingRecipientStatus status;
    int channelFrame = -1;
    float strength, progress, completion;
    public bool Visible { get; private set; }
    public int OverlayCount => layers.Count;

    void Awake()
    {
        health = GetComponent<Health>(); body = GetComponent<CapsuleCollider>();
        impact = GetComponent<EnemyImpactVfx>(); block = new MaterialPropertyBlock();
    }
    void OnEnable() { if (health) health.Died += OnDeath; }
    void OnDisable() { if (health) health.Died -= OnDeath; Clear(); }
    public void Configure(Material material)
    {
        if (!material || layers.Count > 0) return;
        status=GetComponent<HealingRecipientStatus>();
        if(!status) status=gameObject.AddComponent<HealingRecipientStatus>();
        status.Initialize();
        // Cache once; never scan or allocate a new material in the frame loop.
        Renderer[] originals = GetComponentsInChildren<Renderer>(true);
        FindRepairAnchor(originals);
        Vector3 centre = PatchOrigin;
        float radius = PatchRadius;
        var candidates = new List<Candidate>();
        foreach (Renderer source in originals)
        {
            // Only parts intersecting the small torso repair zone, never a full-body silhouette.
            if ((source.bounds.ClosestPoint(centre) - centre).sqrMagnitude > radius * radius) continue;
            if (!(source is MeshRenderer) && !(source is SkinnedMeshRenderer)) continue;
            Material[] originalMaterials = source.sharedMaterials;
            bool opaque = false;
            foreach (Material item in originalMaterials) if (item && item.renderQueue < 2500) opaque = true;
            if (!opaque) continue; // Exclude existing projectile/warning/energy VFX.
            Mesh mesh = source is SkinnedMeshRenderer skin ? skin.sharedMesh : source.GetComponent<MeshFilter>()?.sharedMesh;
            if (!mesh) continue;
            // Prefer large ceramic armour, not tiny trims, wires or every graphite part.
            bool armour = source.sharedMaterial && (source.sharedMaterial.name.Contains("Ivory") ||
                source.sharedMaterial.name.Contains("Ceramic") || source.sharedMaterial.shader.name.Contains("StoneScout"));
            float area = source.bounds.size.sqrMagnitude;
            if (!armour && source != anchor) continue;
            candidates.Add(new Candidate { source = source, mesh = mesh,
                score = (armour ? 10f : 0f) + area - (source.bounds.center - centre).sqrMagnitude });
        }
        candidates.Sort((a,b) => b.score.CompareTo(a.score));
        for (int i = 0; i < Mathf.Min(MaximumOverlays, candidates.Count); i++)
        {
            Renderer source = candidates[i].source; Mesh mesh = candidates[i].mesh;
            GameObject go = new GameObject("Repair energy overlay");
            go.layer = source.gameObject.layer; go.transform.SetParent(source.transform, false);
            Renderer overlay;
            if (source is SkinnedMeshRenderer skinned)
            {
                var copy = go.AddComponent<SkinnedMeshRenderer>(); copy.sharedMesh = mesh;
                copy.bones = skinned.bones; copy.rootBone = skinned.rootBone;
                copy.localBounds = skinned.localBounds; copy.quality = skinned.quality;
                copy.updateWhenOffscreen = false; overlay = copy;
            }
            else { go.AddComponent<MeshFilter>().sharedMesh = mesh; overlay = go.AddComponent<MeshRenderer>(); }
            Material[] slots = new Material[mesh.subMeshCount];
            for (int slot = 0; slot < slots.Length; slot++) slots[slot] = material;
            overlay.sharedMaterials = slots; overlay.enabled = false;
            overlay.shadowCastingMode = ShadowCastingMode.Off; overlay.receiveShadows = false;
            overlay.lightProbeUsage = LightProbeUsage.Off; overlay.reflectionProbeUsage = ReflectionProbeUsage.Off;
            layers.Add(new Layer { source = source, overlay = overlay });
        }
    }
    public void Channel(RepairWispSupportVfx source, float amount)
    {
        if (!health || health.IsDead || !isActiveAndEnabled) return;
        owner = source; channelFrame = Time.frameCount; progress = Mathf.Clamp01(amount);
    }
    public void Completed(RepairWispSupportVfx source)
    { if (owner == source && health && !health.IsDead) { completion = .45f; progress = 1f; } }
    public void Release(RepairWispSupportVfx source, bool immediate)
    { if (owner == source) { channelFrame = -1; if (immediate || completion <= 0f) Clear(); } }
    void LateUpdate()
    {
        if (!health || health.IsDead || !owner || !owner.isActiveAndEnabled || !owner.CombatAvailable) { Clear(); return; }
        bool active = channelFrame == Time.frameCount;
        if (!active && completion <= 0f && strength <= 0f) return;
        completion = Mathf.Max(0, completion - Time.deltaTime);
        bool hit = impact && impact.HitVisible;
        float desired = hit ? 0f : active ? 1f : completion > 0 ? completion / .45f : 0f;
        // Death clears immediately. A real hit takes precedence for its entire short flash.
        strength = hit ? 0f : Mathf.MoveTowards(strength, desired, Time.deltaTime * 8f);
        Visible = strength > .005f && EnemyPresentationBudget.Details(PatchOrigin);
        if(status) status.Show(Visible ? strength : 0f);
        block.SetFloat(Strength, strength); block.SetFloat(Progress, progress);
        block.SetVector(Origin, PatchOrigin);
        block.SetFloat(Radius, PatchRadius);
        block.SetFloat(ScanPhase, Mathf.Repeat(Time.time * .85f, 1f));
        foreach (Layer layer in layers)
        {
            if (!layer.overlay) continue;
            layer.overlay.enabled = Visible && layer.source && layer.source.enabled && layer.source.gameObject.activeInHierarchy;
            if (layer.overlay.enabled)
            {
                if (layer.source is SkinnedMeshRenderer skin && layer.overlay is SkinnedMeshRenderer copy)
                    for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                layer.overlay.SetPropertyBlock(block);
            }
        }
    }
    float PatchRadius => body && body.enabled ? Mathf.Clamp(body.bounds.extents.magnitude, .65f, 1.8f) : .8f;
    Vector3 PatchOrigin => torso ? torso.position : anchor ? anchor.bounds.center :
        body && body.enabled ? body.bounds.center : transform.position + Vector3.up;
    void FindRepairAnchor(Renderer[] originals)
    {
        // The Prism capsule centre sits in the empty gap above its pedestal. Bind the
        // scan to an actual visible core/panel, not an unrelated collision volume.
        var scout = GetComponentInChildren<StoneScoutVisualMotion>();
        if (scout is DemonKingCombatVisual demon)
        { anchor = demon.BodyRenderer; return; }
        if (scout)
            foreach (Transform child in scout.GetComponentsInChildren<Transform>())
                if (child.name == "Torso") { torso = child; return; }
        float largest = 0f;
        foreach (Renderer source in originals)
        {
            if (!source.sharedMaterial || !(source.sharedMaterial.name.Contains("CyanCircuit") ||
                source.sharedMaterial.name.Contains("Repair lime core"))) continue;
            float size = source.bounds.size.sqrMagnitude;
            if (size > largest) { largest = size; anchor = source; }
        }
    }
    void OnDeath(Health actor, DamageInfo damage) => Clear();
    public void Clear()
    {
        Visible = false; channelFrame = -1; strength = completion = progress = 0f; owner = null;
        if(status) status.Clear();
        foreach (Layer layer in layers) if (layer.overlay) layer.overlay.enabled = false;
    }
    void OnDestroy() { foreach (Layer layer in layers) if (layer.overlay) Destroy(layer.overlay.gameObject); }
}
