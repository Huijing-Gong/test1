using UnityEngine;

// Opt-in reaction for this encounter only. No attack windows, damage values or player animations are replaced.
[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public sealed class RailBeetlePlayerImpact : MonoBehaviour
{
    private const float Duration = .45f;
    private const float RecoilSpeed = 6.5f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private Health health;
    private PlayerMovement movement;
    private PlayerVitals vitals;
    private BaselineFlow flow;
    private Transform visualRoot, pivot;
    private Renderer[] renderers;
    private Color[] baseColors;
    private int[] colorIds;
    private MaterialPropertyBlock block;
    private Vector3 localDirection;
    private float remaining;
    private bool subscribed, flashed;
    public int ImpactCount { get; private set; }
    public bool IsReacting => remaining > 0f;
    public float RemainingSeconds => remaining;

    private void Awake()
    {
        health = GetComponent<Health>(); movement = GetComponent<PlayerMovement>(); vitals = GetComponent<PlayerVitals>();
        foreach (var candidate in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { flow = candidate; break; }
        Animator rig = GetComponentInChildren<Animator>(true);
        if (rig && rig.transform != transform)
        {
            visualRoot = rig.transform;
            pivot = new GameObject("Rail impact cosmetic pivot").transform;
            pivot.SetParent(visualRoot.parent, false);
            visualRoot.SetParent(pivot, false); // Identity parent preserves every animation/bone reference and rest pose.
        }
        // Flash the character skin, not the attached sword or its differently coloured submaterials.
        renderers = rig ? rig.GetComponentsInChildren<SkinnedMeshRenderer>(true) : System.Array.Empty<Renderer>();
        baseColors = new Color[renderers.Length]; colorIds = new int[renderers.Length];
        block = new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i].sharedMaterial;
            if (!material) continue;
            int id = material.HasProperty(BaseColorId) ? BaseColorId : material.HasProperty(ColorId) ? ColorId : 0;
            colorIds[i] = id;
            if (id == 0) continue;
            renderers[i].GetPropertyBlock(block);
            baseColors[i] = block.HasColor(id) ? block.GetColor(id) : material.GetColor(id);
        }
    }

    private void OnEnable()
    {
        if (!health || !movement || subscribed) return;
        health.Damaged += OnDamaged; health.Died += OnDied; health.HealthChanged += OnHealthChanged;
        if (flow) flow.WorldEncountersReset += ClearReaction;
        subscribed = true;
    }

    private void OnDisable()
    {
        if (subscribed)
        {
            health.Damaged -= OnDamaged; health.Died -= OnDied; health.HealthChanged -= OnHealthChanged;
            if (flow) flow.WorldEncountersReset -= ClearReaction;
            subscribed = false;
        }
        ClearReaction();
    }

    private void OnDestroy()
    {
        if (!pivot) return;
        if (visualRoot && visualRoot.parent == pivot) visualRoot.SetParent(pivot.parent, false);
        if (Application.isPlaying) Destroy(pivot.gameObject); else DestroyImmediate(pivot.gameObject);
    }

    private void OnDamaged(Health target, DamageInfo damage)
    {
        if (target.IsDead || !damage.Source || !damage.Source.GetComponent<RailBeetleAI>() ||
            !movement || !movement.isActiveAndEnabled || (vitals && !vitals.EnemyCombatActive)) return;
        Vector3 direction = damage.HitDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude < .001f) direction = transform.position - damage.Source.transform.position;
        direction.y = 0f;
        direction = direction.sqrMagnitude > .001f ? direction.normalized : -transform.forward;
        localDirection = transform.InverseTransformDirection(direction);
        remaining = Duration;
        ImpactCount++;
        movement.ApplyImpact(direction * RecoilSpeed, Duration); // Decaying horizontal velocity gives about 1.46 m in clear space.
        Camera main = Camera.main;
        if (main && main.TryGetComponent(out ThirdPersonCamera camera)) camera.RequestShake(.13f, .18f);
        ApplyPose();
    }

    private void OnDied(Health target, DamageInfo damage) { ClearReaction(); }
    private void OnHealthChanged(Health target, int previous, int current)
    {
        // Session refill/respawn must not carry the previous impact into a fresh journey.
        if (current == target.MaxHealth && current > previous) ClearReaction();
    }

    private void LateUpdate()
    {
        if (!IsReacting) return;
        if (!movement || !movement.isActiveAndEnabled || health.IsDead ||
            (flow && (!flow.Started || flow.MenuOpen || flow.VictoryOpen))) { ClearReaction(); return; }
        remaining = Mathf.Max(0f, remaining - Time.deltaTime);
        ApplyPose();
        if (remaining <= 0f) ClearReaction();
    }

    private void ApplyPose()
    {
        float t = Mathf.Clamp01(1f - remaining / Duration);
        float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.15f));
        if (pivot)
        {
            // Lean around the hips. Only the rig's parent moves; the gameplay capsule stays upright.
            Quaternion tilt = Quaternion.Euler(localDirection.z * 20f * envelope, 0f, -localDirection.x * 20f * envelope);
            Vector3 hips = Vector3.up * .95f;
            pivot.localRotation = tilt;
            pivot.localPosition = hips - tilt * hips + Vector3.down * (.08f * envelope);
        }
        SetFlash(t < .38f); // A brief impact flash, not a tint held for the whole longer stagger.
    }

    private void SetFlash(bool active)
    {
        if (!active && !flashed) return;
        if (renderers == null || block == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i] || colorIds[i] == 0) continue;
            renderers[i].GetPropertyBlock(block);
            block.SetColor(colorIds[i], active ? new Color(1f, .78f, .64f) : baseColors[i]);
            renderers[i].SetPropertyBlock(block);
        }
        flashed = active;
    }

    public void ClearReaction()
    {
        remaining = 0f;
        if (movement) movement.ClearImpact();
        if (pivot) { pivot.localPosition = Vector3.zero; pivot.localRotation = Quaternion.identity; }
        SetFlash(false);
    }
}
