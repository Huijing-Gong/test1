using System;
using UnityEngine;
using UnityEngine.Rendering;

// One host-owned wind clock drives all plant passes. The host is suspended during
// side-world travel; the global state is cleared rather than leaking into trials.
[DisallowMultipleComponent]
[RequireComponent(typeof(BaselineWorld), typeof(BaselineFlow))]
[DefaultExecutionOrder(650)]
public sealed class WorldVegetationWind : MonoBehaviour
{
    [Header("Shared breeze")]
    public Vector2 direction = new Vector2(.83f, .56f);
    [Range(0, 1.5f)] public float strength = .7f;
    [Range(15, 120)] public float detailDistance = 65;
    [Header("Footstep wake")]
    [Range(.25f, 1.2f)] public float interactionRadius = .72f;
    [Range(.2f, 1)] public float trailRecoverySeconds = .65f;
    [Header("Sparse nearby canopy debris")]
    [Tooltip("World-space canopy centre and radius. Negative radius selects pink petals.")]
    public Vector4[] canopySources = Array.Empty<Vector4>();
    public Material driftingLeafMaterial;
    public Mesh driftingLeafMesh;
    public bool canopyDebris = true;

    public float Elapsed { get; private set; }
    public bool SimulationRunning { get; private set; }
    public float WindClock => Elapsed;
    public bool IsSuspended => !SimulationRunning;
    public int ActiveParticleCount => LiveDebrisCount;
    public Vector4 InfluencePosition { get; private set; }
    public int LiveDebrisCount => leaves ? leaves.particleCount : 0;
    public Vector4[] TrailSamples => trail;
    public float ContactMotionStrength => contactBlend;

    static readonly int DirectionId = Shader.PropertyToID("_FRWindDirection");
    static readonly int ClockId = Shader.PropertyToID("_FRWindClock");
    static readonly int PlayerId = Shader.PropertyToID("_FRWindPlayer");
    static readonly int TrailId = Shader.PropertyToID("_FRWindTrail");
    static readonly int MotionId = Shader.PropertyToID("_FRWindPlayerDirection");
    static readonly int TrailMotionId = Shader.PropertyToID("_FRWindTrailDirection");
    static readonly int CameraId = Shader.PropertyToID("_FRWindCamera");
    static readonly int InteractionRadiusId = Shader.PropertyToID("_FRWindInteractionRadius");
    const int TrailCount = 6;
    readonly Vector4[] trail = new Vector4[TrailCount];
    readonly float[] trailTimes = new float[TrailCount];
    readonly float[] trailStrengths = new float[TrailCount];
    readonly Vector4[] trailDirections = new Vector4[TrailCount];
    static readonly Vector4[] EmptyTrail = new Vector4[TrailCount];
    BaselineWorld world;
    BaselineFlow flow;
    Transform hero;
    Camera view;
    ParticleSystem leaves;
    Vector3 lastPosition;
    bool hasPosition;
    float sampleWait, debrisWait;
    float contactBlend;
    Vector2 contactDirection;
    int nextTrail, emissionSequence, canopyCursor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetGlobals()
    {
        Shader.SetGlobalVector(DirectionId, Vector4.zero);
        Shader.SetGlobalFloat(ClockId, 0);
        Shader.SetGlobalVector(PlayerId, Vector4.zero);
        Shader.SetGlobalVectorArray(TrailId, EmptyTrail);
        Shader.SetGlobalVector(MotionId, Vector4.zero);
        Shader.SetGlobalVectorArray(TrailMotionId, EmptyTrail);
        Shader.SetGlobalVector(CameraId, Vector4.zero);
        Shader.SetGlobalFloat(InteractionRadiusId, 0);
    }

    void OnValidate()
    {
        strength = Mathf.Clamp(strength, 0, 1.5f);
        detailDistance = Mathf.Clamp(detailDistance, 15, 120);
        trailRecoverySeconds = Mathf.Clamp(trailRecoverySeconds, .2f, 1);
        interactionRadius = Mathf.Clamp(interactionRadius, .25f, 1.2f);
        if (direction.sqrMagnitude < .001f) direction = new Vector2(.83f, .56f);
        if (canopySources == null) canopySources = Array.Empty<Vector4>();
    }

    void OnEnable()
    {
        ResolveHost();
        ClearTrail();
        hasPosition = false;
        ApplyGlobals();
    }

    void Start()
    {
        ResolveHost();
        CreateDebris();
        ApplyGlobals();
    }

    void ResolveHost()
    {
        world = GetComponent<BaselineWorld>();
        flow = GetComponent<BaselineFlow>();
        hero = world ? world.player : null;
        // A suspended host re-enables roots in scene order; retain its camera
        // reference while the Camera root is still inactive on this OnEnable.
        if (!view) view = Camera.main;
    }

    void LateUpdate()
    {
        // Cinematic camera moves do not require a per-frame scene-wide lookup.
        if (!hero && world) hero = world.player;
        bool travelling = SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy);
        SimulationRunning = flow && flow.Started && !flow.BlockWorldInput && !flow.MapOpen &&
            world && !world.JournalOpen && !travelling && Time.timeScale > 0;
        if (SimulationRunning)
        {
            float dt = Mathf.Min(Time.deltaTime, .1f);
            Elapsed += dt;
            UpdateWake(dt);
            TickDebris(dt);
        }
        if (leaves)
        {
            if (SimulationRunning && canopyDebris)
            {
                if (!leaves.isPlaying) leaves.Play(false);
            }
            else if (leaves.isPlaying) leaves.Pause(false);
        }
        ApplyGlobals();
    }

    void UpdateWake(float dt)
    {
        if (!hero || !hero.gameObject.activeInHierarchy)
        {
            InfluencePosition = Vector4.zero;
            ClearTrail();
            hasPosition = false;
            return;
        }
        Vector3 p = hero.position;
        bool relocated = hasPosition && (p - lastPosition).sqrMagnitude > 25;
        if (relocated)
            ClearTrail(); // Teleports never leave an invisible ribbon across the lake.
        Vector3 delta = hasPosition && !relocated ? p - lastPosition : Vector3.zero;
        delta.y = 0;
        float speed = delta.magnitude / Mathf.Max(dt, .001f);
        float moving = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.08f, 1.15f, speed));
        contactBlend = Mathf.MoveTowards(contactBlend, moving, dt * (moving > contactBlend ? 5 : 2.8f));
        if (delta.sqrMagnitude > .000004f)
        {
            Vector2 next = new Vector2(delta.x, delta.z).normalized;
            contactDirection = contactDirection.sqrMagnitude < .001f ? next :
                Vector2.Lerp(contactDirection, next, 1 - Mathf.Exp(-dt * 14)).normalized;
        }
        // No full-strength permanent repulsion halo while standing still. The
        // weak contact remnant is at most a centimetre; motion supplies the wake.
        InfluencePosition = new Vector4(p.x, p.y, p.z, .08f + .92f * contactBlend);
        sampleWait += dt;
        if (sampleWait >= .12f && delta.sqrMagnitude > .000004f)
        {
            sampleWait %= .12f;
            trail[nextTrail] = new Vector4(p.x, p.y, p.z, contactBlend);
            trailStrengths[nextTrail] = contactBlend;
            trailDirections[nextTrail] = new Vector4(contactDirection.x, contactDirection.y, 0, 0);
            trailTimes[nextTrail] = Elapsed;
            nextTrail = (nextTrail + 1) % TrailCount;
        }
        for (int i = 0; i < TrailCount; i++)
        {
            if (trail[i].w <= 0) continue;
            float age = Elapsed - trailTimes[i];
            float fade = Mathf.Exp(-age / trailRecoverySeconds);
            fade *= 1 - Mathf.SmoothStep(0, 1, (age - trailRecoverySeconds) / trailRecoverySeconds);
            fade *= trailStrengths[i];
            trail[i].w = fade < .005f ? 0 : fade;
        }
        lastPosition = p;
        hasPosition = true;
    }

    void ClearTrail()
    {
        Array.Clear(trail, 0, trail.Length);
        Array.Clear(trailTimes, 0, trailTimes.Length);
        Array.Clear(trailStrengths, 0, trailStrengths.Length);
        Array.Clear(trailDirections, 0, trailDirections.Length);
        contactBlend = 0;
        contactDirection = Vector2.zero;
        nextTrail = 0;
        sampleWait = 0;
    }

    void ApplyGlobals()
    {
        Vector2 d = direction.sqrMagnitude > .001f ? direction.normalized : Vector2.right;
        bool available = isActiveAndEnabled && flow && flow.Started;
        Shader.SetGlobalVector(DirectionId, new Vector4(d.x, d.y, strength, available ? 1 : 0));
        Shader.SetGlobalFloat(ClockId, Elapsed);
        Shader.SetGlobalVector(PlayerId, available ? InfluencePosition : Vector4.zero);
        Shader.SetGlobalVectorArray(TrailId, trail);
        Shader.SetGlobalVector(MotionId, new Vector4(contactDirection.x, contactDirection.y, 0, 0));
        Shader.SetGlobalVectorArray(TrailMotionId, trailDirections);
        Vector3 eye = view ? view.transform.position : hero ? hero.position : transform.position;
        Shader.SetGlobalVector(CameraId, new Vector4(eye.x, eye.y, eye.z, detailDistance));
        Shader.SetGlobalFloat(InteractionRadiusId, interactionRadius);
    }

    void CreateDebris()
    {
        if (leaves || !driftingLeafMaterial || canopySources.Length == 0) return;
        var child = new GameObject("Nearby wind leaves and petals (runtime)");
        child.transform.SetParent(transform, false);
        leaves = child.AddComponent<ParticleSystem>();
        leaves.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = leaves.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 3600;
        main.maxParticles = 32;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = false;
        main.gravityModifier = .025f;
        main.startSpeed = 0;
        main.startLifetime = 5.4f;
        main.startSize = .11f;
        main.startRotation3D = true;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = leaves.emission; emission.enabled = false;
        var shape = leaves.shape; shape.enabled = false;
        var spin = leaves.rotationOverLifetime;
        spin.enabled = true; spin.separateAxes = true;
        spin.x = .35f; spin.y = .65f; spin.z = .4f;
        var noise = leaves.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.strength = .075f;
        noise.frequency = .5f;
        noise.scrollSpeed = .25f;
        var size = leaves.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
            new Keyframe(0, 0), new Keyframe(.12f, 1), new Keyframe(.8f, 1), new Keyframe(1, 0)));
        var renderer = leaves.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = driftingLeafMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.renderMode = driftingLeafMesh ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        if (driftingLeafMesh) renderer.mesh = driftingLeafMesh;
    }

    void TickDebris(float dt)
    {
        if (!canopyDebris || !leaves || !hero || canopySources.Length == 0) return;
        debrisWait -= dt;
        if (debrisWait > 0) return;
        debrisWait = .5f;
        if (leaves.particleCount >= 32) return;
        Vector3 eye = view ? view.transform.position : hero.position;
        for (int tried = 0; tried < canopySources.Length; tried++)
        {
            Vector4 source = canopySources[canopyCursor++ % canopySources.Length];
            Vector3 centre = new Vector3(source.x, source.y, source.z);
            float radius = Mathf.Clamp(Mathf.Abs(source.w), .25f, 8);
            // Nearby means nearby the actual player AND the current view, so a
            // high cinematic camera cannot emit petals over the entire map.
            if ((centre - hero.position).sqrMagnitude > 35 * 35 ||
                (centre - eye).sqrMagnitude > 25 * 25) continue;
            int sequence = ++emissionSequence;
            float a = Hash(sequence * 3 + 1), b = Hash(sequence * 3 + 2), c = Hash(sequence * 3 + 3);
            Vector2 d = direction.sqrMagnitude > .001f ? direction.normalized : Vector2.right;
            var emit = new ParticleSystem.EmitParams
            {
                position = centre + new Vector3((a - .5f) * radius, (c - .5f) * radius * .45f, (b - .5f) * radius),
                velocity = new Vector3(d.x, -.14f, d.y) * (.35f + .35f * strength),
                startLifetime = 4.6f + b * 1.8f,
                startSize = source.w < 0 ? .07f + c * .045f : .1f + c * .065f,
                startColor = source.w < 0 ? Color.Lerp(new Color(.94f, .56f, .61f), new Color(1, .81f, .78f), a) :
                    Color.Lerp(new Color(.39f, .53f, .19f), new Color(.66f, .68f, .3f), a),
                rotation3D = new Vector3(a, b, c) * 360,
                randomSeed = (uint)(sequence + 1)
            };
            leaves.Emit(emit, 1);
            return;
        }
    }

    static float Hash(int value)
    {
        float v = Mathf.Sin(value * 127.1f + 31.7f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    void OnDisable()
    {
        SimulationRunning = false;
        InfluencePosition = Vector4.zero;
        ClearTrail();
        hasPosition = false;
        if (leaves) leaves.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        ResetGlobals();
    }
}
