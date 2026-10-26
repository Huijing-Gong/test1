using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class CombatFeedback : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Visuals")]
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private ParticleSystem hitParticles;
    [SerializeField] private ParticleSystem deathParticles;
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.72f, 0.68f, 1f);
    [SerializeField] private Color deathFlashColor = new Color(1f, 0.3f, 0.2f, 1f);
    [SerializeField, Range(0.01f, 0.5f)] private float hitFlashDuration = 0.08f;
    [SerializeField, Range(0.01f, 0.5f)] private float deathFlashDuration = 0.16f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private AudioClip deathClip;
    [SerializeField, Range(0f, 1f)] private float hitVolume = 0.45f;
    [SerializeField, Range(0f, 1f)] private float deathVolume = 0.6f;

    private Health health;
    private MaterialPropertyBlock propertyBlock;
    private int[] colorPropertyIds;
    private Color[] originalColors;
    private float flashTimeRemaining;
    private bool eventsSubscribed;
    private bool visualCacheReady;
    // The five overworld enemies own a small, spatial hit overlay instead of tinting
    // the whole body. Keep the existing player/Boss feedback and audio untouched.
    public bool UsesLocalizedEnemyVisuals { get; private set; }
    public bool UsesEnemySoundLibrary { get; private set; }
    public void UseEnemySoundLibrary() => UsesEnemySoundLibrary = true;
    public void UseLocalizedEnemyVisuals()
    {
        ClearFeedback();
        UsesLocalizedEnemyVisuals = true;
    }

    public bool IsFlashing => flashTimeRemaining > 0f;
    public bool HasConfiguredAudio => UsesEnemySoundLibrary || hitClip != null || deathClip != null;
    public int HitFeedbackCount { get; private set; }
    public int DeathFeedbackCount { get; private set; }

    private void Awake()
    {
        CacheReferences();
        CacheVisualState();
    }

    private void OnEnable()
    {
        CacheReferences();
        CacheVisualState();
        SubscribeToHealth();
    }

    private void Update()
    {
        if (flashTimeRemaining <= 0f)
        {
            return;
        }

        flashTimeRemaining -= Time.unscaledDeltaTime;
        if (flashTimeRemaining <= 0f)
        {
            flashTimeRemaining = 0f;
            RestoreRendererColors();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromHealth();
        ClearFeedback();
    }

    public void ClearFeedback()
    {
        GetComponent<EnemyImpactVfx>()?.Clear();
        flashTimeRemaining = 0f;
        RestoreRendererColors();
        StopParticles(hitParticles);
        StopParticles(deathParticles);
        if (audioSource != null)
        {
            audioSource.Stop();
        }
    }

    private void CacheReferences()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }

        if ((targetRenderers == null || targetRenderers.Length == 0))
        {
            targetRenderers = GetComponentsInChildren<Renderer>(true);
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    private void CacheVisualState()
    {
        if (visualCacheReady || targetRenderers == null)
        {
            return;
        }

        propertyBlock = new MaterialPropertyBlock();
        colorPropertyIds = new int[targetRenderers.Length];
        originalColors = new Color[targetRenderers.Length];

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer targetRenderer = targetRenderers[i];
            Material material = targetRenderer != null ? targetRenderer.sharedMaterial : null;
            if (material == null)
            {
                continue;
            }

            int propertyId = material.HasProperty(BaseColorId)
                ? BaseColorId
                : material.HasProperty(ColorId) ? ColorId : 0;
            colorPropertyIds[i] = propertyId;
            if (propertyId == 0)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(propertyBlock);
            originalColors[i] = propertyBlock.HasColor(propertyId)
                ? propertyBlock.GetColor(propertyId)
                : material.GetColor(propertyId);
        }

        visualCacheReady = true;
    }

    private void SubscribeToHealth()
    {
        if (eventsSubscribed || health == null)
        {
            return;
        }

        health.Damaged += OnDamaged;
        health.Died += OnDied;
        eventsSubscribed = true;
    }

    private void UnsubscribeFromHealth()
    {
        if (!eventsSubscribed || health == null)
        {
            return;
        }

        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
        eventsSubscribed = false;
    }

    private void OnDamaged(Health damagedHealth, DamageInfo damage)
    {
        if (damagedHealth.IsDead)
        {
            return;
        }

        HitFeedbackCount++;
        if (UsesLocalizedEnemyVisuals) flashTimeRemaining = hitFlashDuration;
        else BeginFlash(hitFlashColor, hitFlashDuration);
        PlayParticles(hitParticles, damage.HitPoint);
        PlayClip(hitClip, hitVolume);
    }

    private void OnDied(Health deadHealth, DamageInfo damage)
    {
        DeathFeedbackCount++;
        if (!UsesLocalizedEnemyVisuals) BeginFlash(deathFlashColor, deathFlashDuration);
        else flashTimeRemaining = 0f;
        PlayParticles(deathParticles != null ? deathParticles : hitParticles, damage.HitPoint);
        PlayClip(deathClip != null ? deathClip : hitClip, deathVolume);
    }

    private void BeginFlash(Color color, float duration)
    {
        if (!visualCacheReady || propertyBlock == null)
        {
            return;
        }

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer targetRenderer = targetRenderers[i];
            int propertyId = colorPropertyIds[i];
            if (targetRenderer == null || propertyId == 0)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(propertyId, color);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        flashTimeRemaining = Mathf.Max(flashTimeRemaining, Mathf.Max(0.01f, duration));
    }

    private void RestoreRendererColors()
    {
        if (UsesLocalizedEnemyVisuals) return;
        if (!visualCacheReady || propertyBlock == null)
        {
            return;
        }

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer targetRenderer = targetRenderers[i];
            int propertyId = colorPropertyIds[i];
            if (targetRenderer == null || propertyId == 0)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(propertyId, originalColors[i]);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    private static void PlayParticles(ParticleSystem particles, Vector3 hitPoint)
    {
        if (particles == null)
        {
            return;
        }

        particles.transform.position = hitPoint;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.Play(true);
    }

    private void PlayClip(AudioClip clip, float volume)
    {
        if (!UsesEnemySoundLibrary && audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }

    private static void StopParticles(ParticleSystem particles)
    {
        if (particles != null)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
