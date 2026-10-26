using UnityEngine;
using UnityEngine.Rendering;

// Feedback owned by the enemy; it never changes the player's attack behaviour.
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class StoneScoutFeedback : MonoBehaviour
{
    [SerializeField] private Material glowMaterial;
    [SerializeField] private AudioClip windupClip;
    [SerializeField] private AudioClip playerHitClip;
    [SerializeField] private AudioClip stoneHitClip;

    private Health health;
    private AudioSource audioSource;
    private Transform warningOrb;
    private Transform impactSpark;
    private float sparkTime;
    private float windupElapsed, windupDuration;
    private Renderer warningRenderer;
    private MaterialPropertyBlock warningBlock;
    private LineRenderer punchStroke;
    private Vector3 previousFist;
    private float strokeTime;
    private ParticleSystem fallChips;
    private float fallCueTime;
    private static Mesh chipMesh;
    private EnemyGroundQuery landingGround;
    private StoneScoutVisualMotion visual;

    private void Awake()
    {
        health = GetComponent<Health>();
        landingGround = new EnemyGroundQuery(transform);
        visual = GetComponentInChildren<StoneScoutVisualMotion>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 12f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;

        Transform eye = null;
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name == "EyeVFXAnchor") { eye = child; break; }
        if (eye != null)
            warningOrb = CreateGlow("Punch warning", eye, 0.2f, new Color(1f, 0.62f, 0.16f));
        warningRenderer = warningOrb ? warningOrb.GetComponent<Renderer>() : null;
        warningBlock = new MaterialPropertyBlock();
        impactSpark = CreateGlow("Punch impact", transform, 0.22f, new Color(0.4f, 1f, 1f));
        var stroke = new GameObject("Short actual fist stroke"); stroke.transform.SetParent(transform, false);
        punchStroke = stroke.AddComponent<LineRenderer>(); punchStroke.sharedMaterial = glowMaterial;
        punchStroke.useWorldSpace = true; punchStroke.positionCount = 2;
        punchStroke.shadowCastingMode = ShadowCastingMode.Off; punchStroke.receiveShadows = false;
        punchStroke.enabled = false;
        var strokeTint = new MaterialPropertyBlock();
        strokeTint.SetColor("_BaseColor", new Color(.35f, .9f, 1f));
        strokeTint.SetColor("_Color", new Color(.35f, .9f, 1f)); punchStroke.SetPropertyBlock(strokeTint);
        GameObject debris = new GameObject("Small stone landing chips");
        debris.transform.SetParent(transform, false);
        fallChips = debris.AddComponent<ParticleSystem>();
        fallChips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = fallChips.main; main.playOnAwake = false; main.loop = false;
        main.duration = .3f; main.startLifetime = .3f; main.startSpeed = 1.2f;
        main.startSize = .055f; main.maxParticles = 8; main.gravityModifier = .7f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new Color(.68f, .65f, .52f);
        var emission = fallChips.emission; emission.enabled = false;
        var shape = fallChips.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = .07f;
        var renderer = debris.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = glowMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (!chipMesh)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            chipMesh = cube.GetComponent<MeshFilter>().sharedMesh; Destroy(cube);
        }
        renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = chipMesh;
        var chipTint = new MaterialPropertyBlock();
        chipTint.SetColor("_BaseColor", new Color(.56f, .52f, .43f));
        chipTint.SetColor("_Color", new Color(.56f, .52f, .43f)); renderer.SetPropertyBlock(chipTint);
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
        Clear();
    }

    private void Update()
    {
        bool details = EnemyPresentationBudget.Details(transform.position);
        if (!details && fallChips && fallChips.particleCount > 0)
            fallChips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (fallCueTime > 0f)
        {
            fallCueTime = Mathf.Max(0f, fallCueTime - Time.deltaTime);
            if (fallCueTime == 0f && health.IsDead && fallChips && details)
            {
                Vector3 point = visual ? visual.TorsoWorldPosition : transform.position;
                if (landingGround.TrySample(point, .5f, 2f, 35f, out RaycastHit floor)) point = floor.point;
                else point = transform.position;
                fallChips.transform.position = point + Vector3.up * .08f;
                fallChips.Play(); fallChips.Emit(8);
                if (!GetComponent<EnemySoundEmitter>()) Play(stoneHitClip, .3f);
            }
        }
        if (warningOrb != null && warningOrb.gameObject.activeSelf)
        {
            windupElapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(windupElapsed / Mathf.Max(.05f, windupDuration));
            warningOrb.localScale = Vector3.one * Mathf.Lerp(.075f, .16f, progress);
            if (warningRenderer)
            {
                warningRenderer.GetPropertyBlock(warningBlock);
                Color tint = new Color(1f, .48f, .09f) * Mathf.Lerp(.45f, 1.3f, progress);
                warningBlock.SetColor("_BaseColor", tint); warningBlock.SetColor("_Color", tint);
                warningRenderer.SetPropertyBlock(warningBlock);
            }
        }
        if (sparkTime <= 0f || impactSpark == null) return;
        sparkTime = Mathf.Max(0f, sparkTime - Time.deltaTime);
        impactSpark.localScale = Vector3.one * (0.08f + sparkTime * 1.4f);
        if (sparkTime == 0f) impactSpark.gameObject.SetActive(false);
    }

    public void BeginWindup(float duration = .38f)
    {
        windupElapsed = 0f; windupDuration = duration;
        if (warningOrb != null) warningOrb.gameObject.SetActive(true);
        var sound = GetComponent<EnemySoundEmitter>();
        if (sound) sound.Cue(EnemySoundCue.StoneScrape, seconds: duration);
        else Play(windupClip, 0.22f);
    }

    public void EndWindup()
    {
        GetComponent<EnemySoundEmitter>()?.StopWarning();
        strokeTime = 0f;
        if (punchStroke) punchStroke.enabled = false;
        if (visual) visual.SetChargeProgress(0f);
        if (warningOrb != null) warningOrb.gameObject.SetActive(false);
    }

    public void BeginStrike()
    {
        if (!visual || health.IsDead) return;
        if (visual is DemonKingCombatVisual)
        {
            if (warningOrb) warningOrb.gameObject.SetActive(false);
            visual.SetChargeProgress(0f);
            var bladeSound = GetComponent<EnemySoundEmitter>();
            if (bladeSound) { bladeSound.StopWarning(); bladeSound.Cue(EnemySoundCue.StonePunch,.65f); }
            return;
        }
        EnemyAttackPresentation.Ensure(gameObject);
        GetComponent<EnemyAttackPresentation>()?.PunchStart();
        if (warningOrb) warningOrb.gameObject.SetActive(false);
        visual.SetChargeProgress(0f);
        previousFist = visual.RightFistWorldPosition; strokeTime = .16f;
        var sound = GetComponent<EnemySoundEmitter>();
        if (sound) { sound.StopWarning(); sound.Cue(EnemySoundCue.StonePunch, .85f); }
    }

    private void LateUpdate()
    {
        if (!punchStroke || !visual || strokeTime <= 0f || health.IsDead) return;
        strokeTime = Mathf.Max(0f, strokeTime - Time.deltaTime);
        Vector3 fist = visual.RightFistWorldPosition;
        Vector3 step = Vector3.ClampMagnitude(fist - previousFist, .3f);
        punchStroke.enabled = strokeTime > 0f && step.sqrMagnitude > .0001f && EnemyPresentationBudget.Details(fist);
        punchStroke.SetPosition(0, fist - step); punchStroke.SetPosition(1, fist);
        if (EnemyAttackPresentation.EnergyMaterial) punchStroke.sharedMaterial = EnemyAttackPresentation.EnergyMaterial;
        punchStroke.startColor = new Color(.14f,.84f,.75f,.65f);
        punchStroke.endColor = new Color(.7f,1f,.93f,1f);
        punchStroke.startWidth = .075f * strokeTime / .16f; punchStroke.endWidth = .018f;
        previousFist = fist;
    }

    public void HitPlayer(Vector3 hitPoint)
    {
        EndWindup();
        GetComponent<EnemyAttackPresentation>()?.Contact(hitPoint, visual is DemonKingCombatVisual ? new Color(1,.25f,.05f) : new Color(.18f,.85f,.76f));
        var sound = GetComponent<EnemySoundEmitter>();
        if (sound) sound.Cue(EnemySoundCue.StoneHit, .6f, point: hitPoint);
        else Play(playerHitClip, 0.38f);
    }

    public void Clear()
    {
        GetComponent<EnemySoundEmitter>()?.StopAll();
        fallCueTime = 0f;
        if (fallChips) fallChips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        EndWindup();
        sparkTime = 0f;
        GetComponent<EnemyAttackPresentation>()?.Clear();
        if (impactSpark != null) impactSpark.gameObject.SetActive(false);
        if (audioSource != null) audioSource.Stop();
    }

    private void OnDamaged(Health target, DamageInfo damage)
    {
        if (target.IsDead) return;
        EndWindup();
        ShowSpark(damage.HitPoint);
        if (!GetComponent<EnemySoundEmitter>()) Play(stoneHitClip, 0.4f);
    }

    private void OnDied(Health target, DamageInfo damage)
    {
        fallCueTime = .65f; // Matches the existing fall, not the instant lethal hit.
        EndWindup();
        sparkTime = 0f;
        if (impactSpark) impactSpark.gameObject.SetActive(false);
        if (!GetComponent<EnemySoundEmitter>()) Play(stoneHitClip, 0.5f);
    }

    private void ShowSpark(Vector3 point)
    {
        if (impactSpark == null) return;
        impactSpark.position = point;
        sparkTime = 0.14f;
        impactSpark.localScale = Vector3.one * 0.22f;
        impactSpark.gameObject.SetActive(true);
    }

    private void Play(AudioClip clip, float volume)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip, volume);
    }

    private Transform CreateGlow(string title, Transform parent, float size, Color color)
    {
        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = title;
        glow.transform.SetParent(parent, false);
        glow.transform.localPosition = Vector3.zero;
        glow.transform.localScale = Vector3.one * size;
        Collider collider = glow.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        MeshRenderer renderer = glow.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = glowMaterial;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        glow.SetActive(false);
        return glow.transform;
    }
}
