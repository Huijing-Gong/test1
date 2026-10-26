using System;
using UnityEngine;

// Five reusable voices for an entire map: nearby water, regional canopy and
// three spatial bird calls. No global AudioListener pause or music changes.
[DisallowMultipleComponent]
[RequireComponent(typeof(BaselineWorld), typeof(BaselineFlow))]
[DefaultExecutionOrder(690)]
public sealed class RegionalNatureAudio : MonoBehaviour
{
    public AudioClip waterLoop, foliageLoop;
    public AudioClip[] birdCalls = Array.Empty<AudioClip>();
    [Tooltip("Actual shoreline positions, not the centre of the lake.")]
    public Vector3[] waterAnchors = Array.Empty<Vector3>();
    public Vector3[] treeAnchors = Array.Empty<Vector3>();
    [Range(0, 1)] public float waterVolume = .28f;
    [Range(0, 1)] public float foliageVolume = .24f;
    [Range(0, 1)] public float birdVolume = .24f;

    public float Elapsed { get; private set; }
    public bool IsSuspended { get; private set; } = true;
    public int BirdClipCount => birdCalls == null ? 0 : birdCalls.Length;
    public int BirdPlayCount { get; private set; }
    public Vector3 LastBirdPosition { get; private set; }
    public float LakeMix { get; private set; }
    public float ForestMix { get; private set; }
    public float CombatDuck { get; private set; } = 1;
    public int ActiveSourceCount
    {
        get
        {
            int count = 0;
            if (waterSource && waterSource.isPlaying) count++;
            if (foliageSource && foliageSource.isPlaying) count++;
            for (int i = 0; i < birdSources.Length; i++)
                if (birdSources[i] && birdSources[i].isPlaying) count++;
            return count;
        }
    }

    BaselineWorld world;
    BaselineFlow flow;
    WorldVegetationWind wind;
    WorldSmallWildlife wildlife;
    Transform hero;
    Health health;
    PlayerCombat combat;
    AudioSource waterSource, foliageSource;
    readonly AudioSource[] birdSources = new AudioSource[3];
    readonly System.Random random = new System.Random(27183);
    bool initialized, paused, subscribed, hadPosition;
    float nextBird = 4.5f, combatUntil = -1, sampleWait;
    Vector3 lastPosition, nearestWater;
    int nextVoice, nextClip;

    void OnEnable()
    {
        ResolveHost();
        Subscribe();
        hadPosition = false;
        sampleWait = 0;
    }

    void Start()
    {
        ResolveHost();
        Subscribe();
        EnsureSources();
    }

    void ResolveHost()
    {
        world = GetComponent<BaselineWorld>();
        flow = GetComponent<BaselineFlow>();
        wind = GetComponent<WorldVegetationWind>();
        wildlife = GetComponent<WorldSmallWildlife>();
        hero = world ? world.player : null;
        if (hero)
        {
            health = hero.GetComponent<Health>();
            combat = hero.GetComponent<PlayerCombat>();
        }
    }

    void Subscribe()
    {
        if (subscribed) return;
        if (health) health.Damaged += OnHeroDamaged;
        if (wildlife) wildlife.BirdStartled += OnBirdStartled;
        subscribed = health || wildlife;
    }

    void EnsureSources()
    {
        if (initialized) return;
        waterSource = MakeSource("Nearby lake lapping", true, true, waterLoop);
        waterSource.minDistance = 3;
        waterSource.maxDistance = 42;
        foliageSource = MakeSource("Regional tree breeze", true, false, foliageLoop);
        for (int i = 0; i < birdSources.Length; i++)
        {
            birdSources[i] = MakeSource("Spatial bird voice " + (i + 1), false, true, null);
            birdSources[i].minDistance = 2;
            birdSources[i].maxDistance = 30;
        }
        initialized = true;
    }

    AudioSource MakeSource(string label, bool loop, bool spatial, AudioClip clip)
    {
        var child = new GameObject(label + " (runtime)");
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.spatialBlend = spatial ? 1 : 0;
        source.dopplerLevel = 0;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.spread = loop ? 65 : 0;
        source.priority = loop ? 220 : 200;
        source.reverbZoneMix = 0;
        source.volume = 0;
        return source;
    }

    bool CanRun => isActiveAndEnabled && world && flow && flow.Started &&
        !flow.BlockWorldInput && !flow.MapOpen && !world.JournalOpen &&
        hero && hero.gameObject.activeInHierarchy && (!health || !health.IsDead) &&
        !(SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy)) &&
        Time.timeScale > 0;

    void LateUpdate()
    {
        if (!initialized) return;
        IsSuspended = !CanRun;
        if (IsSuspended)
        {
            if (!paused)
            {
                waterSource.Pause(); foliageSource.Pause();
                foreach (var source in birdSources) source.Pause();
                paused = true;
            }
            return;
        }
        if (paused)
        {
            waterSource.UnPause(); foliageSource.UnPause();
            foreach (var source in birdSources) source.UnPause();
            paused = false;
        }
        float dt = Mathf.Min(Time.deltaTime, .1f);
        Elapsed += dt;
        Vector3 position = hero.position;
        bool teleported = hadPosition && (position - lastPosition).sqrMagnitude > 100;
        if (teleported)
        {
            foreach (var source in birdSources) source.Stop();
            waterSource.volume = foliageSource.volume = 0;
            nextBird = 4.5f;
            sampleWait = 0;
        }
        lastPosition = position;
        hadPosition = true;
        sampleWait -= dt;
        if (sampleWait <= 0)
        {
            sampleWait = .25f;
            float waterDistance = Nearest(position, waterAnchors, out nearestWater);
            float treeDistance = Nearest(position, treeAnchors, out _);
            LakeMix = 1 - Mathf.SmoothStep(0, 1, (waterDistance - 3) / 34);
            ForestMix = 1 - Mathf.SmoothStep(0, 1, (treeDistance - 3) / 19);
        }
        if (teleported || !waterSource.isPlaying) waterSource.transform.position = nearestWater;
        else waterSource.transform.position = Vector3.Lerp(waterSource.transform.position, nearestWater, 1 - Mathf.Exp(-dt * 2));
        // Exact same travelling gust expression as VegetationWind.hlsl.
        float t = wind ? wind.WindClock : Elapsed;
        Vector2 dir = wind ? wind.direction.normalized : new Vector2(.83f, .56f).normalized;
        float travelling = (position.x * dir.x + position.z * dir.y) * .19f - t * .72f;
        float gust = .65f + .21f * Mathf.Sin(travelling) + .14f * Mathf.Sin(travelling * .53f + t * .11f + 1.7f);
        float strength = wind ? wind.strength : .7f;
        if (combat && combat.IsAttacking) combatUntil = Elapsed + 3;
        CombatDuck = Mathf.MoveTowards(CombatDuck, Elapsed < combatUntil ? .38f : 1, dt * .75f);
        float lakeTarget = waterVolume * LakeMix * CombatDuck;
        float treeTarget = foliageVolume * (.12f + .88f * ForestMix) *
            Mathf.Clamp01(strength) * (.35f + .65f * gust) * CombatDuck;
        waterSource.volume = Mathf.MoveTowards(waterSource.volume, lakeTarget, dt * .12f);
        foliageSource.volume = Mathf.MoveTowards(foliageSource.volume, treeTarget, dt * .1f);
        StartLoop(waterSource);
        StartLoop(foliageSource);
        nextBird -= dt;
        if (nextBird <= 0)
        {
            nextBird = 7.5f + (float)random.NextDouble() * 9;
            // Daytime calls originate at a real nearby tree, not the listener.
            if (ForestMix > .1f && Elapsed >= combatUntil && treeAnchors.Length > 0)
            {
                int start = random.Next(treeAnchors.Length);
                for (int i = 0; i < treeAnchors.Length; i++)
                {
                    Vector3 at = treeAnchors[(start + i) % treeAnchors.Length];
                    if ((at - position).sqrMagnitude > 26 * 26) continue;
                    PlayBird(at, false);
                    break;
                }
            }
        }
    }

    static void StartLoop(AudioSource source)
    {
        if (source.clip && !source.isPlaying) source.Play();
    }

    static float Nearest(Vector3 position, Vector3[] anchors, out Vector3 closest)
    {
        float nearest = float.PositiveInfinity;
        closest = position;
        if (anchors == null) return nearest;
        for (int i = 0; i < anchors.Length; i++)
        {
            float distance = (anchors[i] - position).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            closest = anchors[i];
        }
        return Mathf.Sqrt(nearest);
    }

    public void PlayBird(Vector3 position, bool startled)
    {
        if (!initialized || !CanRun || BirdClipCount == 0 || !hero ||
            (position - hero.position).sqrMagnitude > 30 * 30) return;
        // Brief cooldown avoids every member of a flock firing the same sample.
        if (Elapsed - lastBirdAt < (startled ? .16f : 1.5f)) return;
        var clip = birdCalls[nextClip++ % birdCalls.Length];
        if (!clip) return;
        var source = birdSources[nextVoice++ % birdSources.Length];
        source.Stop();
        source.transform.position = position;
        source.clip = clip;
        source.pitch = .95f + (float)random.NextDouble() * .1f;
        source.volume = birdVolume * (startled ? 1 : .7f) * CombatDuck;
        source.Play();
        LastBirdPosition = position;
        BirdPlayCount++;
        lastBirdAt = Elapsed;
    }

    float lastBirdAt = -10;
    void OnBirdStartled(Vector3 position) => PlayBird(position, true);
    void OnHeroDamaged(Health _, DamageInfo __) => combatUntil = Elapsed + 6;

    void OnDisable()
    {
        if (subscribed)
        {
            if (health) health.Damaged -= OnHeroDamaged;
            if (wildlife) wildlife.BirdStartled -= OnBirdStartled;
            subscribed = false;
        }
        IsSuspended = true;
        paused = false;
        if (!initialized) return;
        waterSource.Stop(); foliageSource.Stop();
        waterSource.volume = foliageSource.volume = 0;
        foreach (var source in birdSources) source.Stop();
        nextBird = 4.5f;
        hadPosition = false;
    }
}
