using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Small, host-owned life: no colliders, navigation agents, damage or save state.
// Anchors are sampled on authored ground by the installer; flight never changes it.
[DisallowMultipleComponent, DefaultExecutionOrder(670)]
[RequireComponent(typeof(BaselineWorld), typeof(BaselineFlow))]
public sealed class WorldSmallWildlife : MonoBehaviour
{
    public Vector3[] birdAnchors = Array.Empty<Vector3>();
    public Vector3[] butterflyAnchors = Array.Empty<Vector3>();
    public Mesh birdBodyMesh, birdWingMesh, butterflyMesh;
    public Material birdMaterial, butterflyMaterial;
    [Range(15, 65)] public float activityDistance = 42;
    public event Action<Vector3> BirdStartled;
    public int ActiveBirdCount { get; private set; }
    public int ActiveButterflyCount { get; private set; }
    public int StartleCount { get; private set; }
    public float Elapsed { get; private set; }
    public bool SimulationRunning { get; private set; }
    public int BirdCount => birds.Count;
    public int ButterflyCount => butterflies.Count;

    enum BirdState { Ground, Departing, Waiting, Returning }
    sealed class Bird
    {
        public Transform root, rig, left, right;
        public Vector3 anchor, departure, exit, control;
        public float seed, stateTime, wait, groundTime;
        public BirdState state;
    }
    sealed class Butterfly
    {
        public Transform root;
        public Renderer renderer;
        public Vector3 anchor;
        public float seed, localTime, panic;
    }
    readonly List<Bird> birds = new List<Bird>(6);
    readonly List<Butterfly> butterflies = new List<Butterfly>(8);
    readonly List<UnityEngine.Object> ownedAssets = new List<UnityEngine.Object>();
    MaterialPropertyBlock block;
    static readonly int WingId = Shader.PropertyToID("_WingAngle");
    static readonly int TintId = Shader.PropertyToID("_BaseColor");
    static readonly int WingTintId = Shader.PropertyToID("_WingTint");
    Transform runtimeRoot, hero;
    BaselineWorld world;
    BaselineFlow flow;
    WorldVegetationWind wind;
    PlayerMovement movement;
    PlayerCombat combat;
    Camera view;
    readonly Plane[] frustum = new Plane[6];
    Vector3 previousHero;
    bool hasHeroPosition;

    void OnValidate()
    {
        activityDistance = Mathf.Clamp(activityDistance, 15, 65);
        if (birdAnchors == null) birdAnchors = Array.Empty<Vector3>();
        if (butterflyAnchors == null) butterflyAnchors = Array.Empty<Vector3>();
    }

    void OnEnable()
    {
        if (block == null) block = new MaterialPropertyBlock();
        world = GetComponent<BaselineWorld>(); flow = GetComponent<BaselineFlow>();
        wind = GetComponent<WorldVegetationWind>(); view = Camera.main;
        ResolveHero();
        hasHeroPosition = false;
    }

    void Start() { if (!runtimeRoot) CreateWildlife(); }

    void ResolveHero()
    {
        Transform next = world ? world.player : null;
        if (next == hero) return;
        hero = next;
        movement = hero ? hero.GetComponent<PlayerMovement>() : null;
        combat = hero ? hero.GetComponent<PlayerCombat>() : null;
        hasHeroPosition = false;
    }

    bool CanSimulate()
    {
        bool away = SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy);
        return flow && flow.Started && !flow.BlockWorldInput && !flow.MapOpen && !flow.MenuOpen &&
            world && !world.JournalOpen && !away && Time.timeScale > 0 && hero && hero.gameObject.activeInHierarchy;
    }

    void LateUpdate()
    {
        ResolveHero();
        // Start is not called again when a world root is re-enabled after travel.
        if (!runtimeRoot) CreateWildlife();
        SimulationRunning = CanSimulate();
        if (!SimulationRunning) return;
        float dt = Mathf.Min(.1f, Time.deltaTime);
        Elapsed += dt;
        if (!view || !view.isActiveAndEnabled) view = Camera.main;
        if (view) GeometryUtility.CalculateFrustumPlanes(view, frustum);
        Vector3 heroPosition = hero.position;
        bool relocated = hasHeroPosition && (heroPosition - previousHero).sqrMagnitude > 25;
        float speed = hasHeroPosition && !relocated ? Vector3.Distance(heroPosition, previousHero) / Mathf.Max(dt, .001f) : 0;
        previousHero = heroPosition; hasHeroPosition = true;
        float scareRadius = movement && movement.SprintingActive || speed > 4.6f ? 4 : 2.3f;
        // The actual PlayerCombat facade reports Hero combat. Opening J/M does not
        // count as a sword swing and there is deliberately no raw keyboard check.
        bool attacking = combat && combat.IsAttacking;
        ActiveBirdCount = ActiveButterflyCount = 0;
        foreach (var bird in birds) TickBird(bird, dt, heroPosition, scareRadius, attacking);
        foreach (var butterfly in butterflies) TickButterfly(butterfly, dt, heroPosition, relocated);
    }

    bool Nearby(Vector3 position, Vector3 heroPosition)
    {
        float range = activityDistance;
        return (position - heroPosition).sqrMagnitude < range * range &&
            (!view || (position - view.transform.position).sqrMagnitude < (range + 16) * (range + 16));
    }

    bool Visible(Vector3 position, float radius = .45f)
    {
        return view && GeometryUtility.TestPlanesAABB(frustum, new Bounds(position, Vector3.one * radius * 2));
    }

    void TickBird(Bird bird, float dt, Vector3 heroPosition, float scareRadius, bool attacking)
    {
        bool near = Nearby(bird.anchor, heroPosition);
        float distance = Vector3.Distance(bird.root.position, heroPosition);
        bool threatened = distance < scareRadius || attacking && distance < 6.2f;
        if (bird.state == BirdState.Ground)
        {
            bird.root.gameObject.SetActive(near);
            if (!near) return;
            ActiveBirdCount++;
            bird.groundTime += dt;
            if (threatened) { Startle(bird, heroPosition); return; }
            float t = bird.groundTime + bird.seed;
            // Three still beats, one tiny hop, then peck; never treadmill sliding.
            float cycle = Mathf.Repeat(t, 7.4f);
            float hop = cycle > 5.7f && cycle < 6.2f ? Mathf.Sin((cycle - 5.7f) / .5f * Mathf.PI) * .075f : 0;
            bird.root.position = bird.anchor + Vector3.up * hop;
            float peck = cycle > 1.2f && cycle < 2.1f ? Mathf.Pow(Mathf.Sin((cycle - 1.2f) / .9f * Mathf.PI), 2) : 0;
            float secondPeck = cycle > 3.1f && cycle < 3.65f ? Mathf.Sin((cycle - 3.1f) / .55f * Mathf.PI) : 0;
            float yaw = Mathf.Sin(t * .8f) * 13;
            bird.root.rotation = Quaternion.Euler(0, bird.seed * 57.3f + yaw, 0);
            bird.rig.localRotation = Quaternion.Euler(peck * 32 + secondPeck * 24, 0, 0);
            FoldWings(bird, -34);
            return;
        }
        if (bird.state == BirdState.Waiting)
        {
            bird.root.gameObject.SetActive(false);
            bird.stateTime += dt;
            // Re-enter from the off-screen sky only when the patch is quiet. Do
            // not instantiate or suddenly show a bird in front of the player.
            if (bird.stateTime < bird.wait || !near || (heroPosition - bird.anchor).sqrMagnitude < 64 ||
                Visible(bird.exit, 1) || attacking && (heroPosition - bird.anchor).sqrMagnitude < 144) return;
            bird.state = BirdState.Returning; bird.stateTime = 0;
            bird.departure = bird.exit;
            bird.control = Vector3.Lerp(bird.exit, bird.anchor, .65f) + Vector3.up * 1.7f;
            bird.root.position = bird.exit;
            bird.root.gameObject.SetActive(true);
        }
        else if (bird.state == BirdState.Returning && threatened)
        {
            Startle(bird, heroPosition);
        }
        bird.stateTime += dt;
        float duration = bird.state == BirdState.Departing ? 3.4f : 4.7f;
        float progress = Mathf.Clamp01(bird.stateTime / duration);
        Vector3 end = bird.state == BirdState.Departing ? bird.exit : bird.anchor;
        Vector3 position = Bezier(bird.departure, bird.control, end, progress);
        Vector3 tangent = Bezier(bird.departure, bird.control, end, Mathf.Min(1, progress + .012f)) - position;
        if (tangent.sqrMagnitude > .000001f) bird.root.rotation = Quaternion.LookRotation(tangent, Vector3.up);
        bird.root.position = position;
        bird.rig.localRotation = Quaternion.identity;
        float flapClock = bird.stateTime * 27 + bird.seed;
        FoldWings(bird, Mathf.Sin(flapClock) * 53);
        if (bird.root.gameObject.activeSelf) ActiveBirdCount++;
        if (progress < 1) return;
        if (bird.state == BirdState.Returning)
        {
            bird.state = BirdState.Ground; bird.groundTime = 0;
            bird.root.position = bird.anchor; bird.rig.localRotation = Quaternion.identity;
        }
        // If its exit remains in view, continue flying rather than vanishing.
        else if (!Visible(bird.root.position, .6f) || (bird.root.position - heroPosition).sqrMagnitude > 2500)
        {
            bird.state = BirdState.Waiting; bird.stateTime = 0; bird.root.gameObject.SetActive(false);
        }
        else
        {
            bird.departure = bird.root.position;
            Vector3 forward = bird.root.forward; forward.y = .35f;
            bird.exit = bird.departure + forward.normalized * 12;
            bird.control = Vector3.Lerp(bird.departure, bird.exit, .5f) + Vector3.up * .5f;
            bird.stateTime = 0;
        }
    }

    void Startle(Bird bird, Vector3 source)
    {
        bird.state = BirdState.Departing; bird.stateTime = 0;
        bird.departure = bird.root.position;
        Vector3 away = bird.root.position - source; away.y = 0;
        if (away.sqrMagnitude < .01f) away = Quaternion.Euler(0, bird.seed * 57.3f, 0) * Vector3.forward;
        away = Quaternion.Euler(0, (bird.seed % 1 - .5f) * 42, 0) * away.normalized;
        bird.exit = bird.departure + away * (11 + bird.seed % 3) + Vector3.up * (4.5f + bird.seed % 2);
        bird.control = bird.departure + away * 3 + Vector3.up * 3;
        bird.wait = 10 + bird.seed % 8;
        StartleCount++;
        BirdStartled?.Invoke(bird.departure);
    }

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1 - t; return a * (u * u) + b * (2 * u * t) + c * (t * t);
    }

    static void FoldWings(Bird bird, float angle)
    {
        bird.left.localRotation = Quaternion.Euler(0, 0, -angle);
        bird.right.localRotation = Quaternion.Euler(0, 0, angle);
    }

    void TickButterfly(Butterfly butterfly, float dt, Vector3 heroPosition, bool relocated)
    {
        bool near = Nearby(butterfly.anchor, heroPosition);
        butterfly.root.gameObject.SetActive(near);
        if (!near) return;
        ActiveButterflyCount++;
        butterfly.localTime += dt;
        float t = butterfly.localTime + butterfly.seed;
        if (!relocated && (heroPosition - butterfly.root.position).sqrMagnitude < 1.7f * 1.7f) butterfly.panic = 2.4f;
        butterfly.panic = Mathf.Max(0, butterfly.panic - dt);
        float cycle = Mathf.Repeat(t, 11);
        float flight = 1 - Mathf.SmoothStep(0, 1, (cycle - 6) / 1.4f);
        if (cycle > 9.5f) flight = Mathf.SmoothStep(0, 1, (cycle - 9.5f) / 1.5f);
        flight = Mathf.Max(flight, Mathf.Clamp01(butterfly.panic));
        float windClock = wind ? wind.WindClock : Elapsed;
        Vector2 breeze = wind ? wind.direction.normalized * wind.strength : Vector2.right * .35f;
        Vector3 hover = new Vector3(Mathf.Sin(t * .73f) * .47f, .25f + Mathf.Sin(t * 2.2f) * .09f,
            Mathf.Cos(t * .81f) * .38f) * flight;
        hover += new Vector3(breeze.x, 0, breeze.y) * (Mathf.Sin(windClock * .7f + butterfly.seed) * .1f * flight);
        hover.y += flight * Mathf.Clamp01(butterfly.panic) * .35f;
        butterfly.root.position = butterfly.anchor + hover;
        butterfly.root.rotation = Quaternion.Euler(flight * 13 * Mathf.Sin(t * 1.3f),
            butterfly.seed * 41 + Mathf.Sin(t * .8f) * flight * 60, 0);
        float flap = Mathf.Lerp(58 + Mathf.Sin(t * 2) * 8, Mathf.Sin(t * 31) * 67, flight);
        butterfly.renderer.GetPropertyBlock(block); block.SetFloat(WingId, flap);
        butterfly.renderer.SetPropertyBlock(block);
    }

    void CreateWildlife()
    {
        if (!Application.isPlaying || runtimeRoot) return;
        // Normal installed scenes use serialized mesh assets. Runtime-created
        // meshes are a safe fallback for a freshly added component, and owned.
        Mesh body = birdBodyMesh ? birdBodyMesh : Own(CreateBirdBodyMesh());
        Mesh wing = birdWingMesh ? birdWingMesh : Own(CreateBirdWingMesh());
        Mesh butterfly = butterflyMesh ? butterflyMesh : Own(CreateButterflyMesh());
        Material birdMat = birdMaterial ? birdMaterial : CreateFallbackMaterial();
        Material butterflyMat = butterflyMaterial ? butterflyMaterial : birdMat;
        var host = new GameObject("Nearby birds and butterflies (runtime)");
        runtimeRoot = host.transform; runtimeRoot.SetParent(transform, false);
        for (int i = 0; i < Mathf.Min(6, birdAnchors.Length); i++)
        {
            var root = new GameObject("Ground bird " + (i + 1)).transform;
            root.SetParent(runtimeRoot, false); root.position = birdAnchors[i];
            var rig = new GameObject("Peck and flight pose").transform; rig.SetParent(root, false);
            CreatePart("Plumage, eyes and beak", rig, body, birdMat);
            var left = CreatePart("Left feathered wing", rig, wing, birdMat);
            var right = CreatePart("Right feathered wing", rig, wing, birdMat);
            left.localPosition = right.localPosition = new Vector3(0, .15f, -.02f);
            left.localScale = new Vector3(-1, 1, 1);
            var b = new Bird { root = root, rig = rig, left = left, right = right,
                anchor = birdAnchors[i], seed = 1.373f + i * 2.171f, groundTime = i * 1.31f };
            FoldWings(b, -34); birds.Add(b);
        }
        for (int i = 0; i < Mathf.Min(8, butterflyAnchors.Length); i++)
        {
            var root = CreatePart("Flower butterfly " + (i + 1), runtimeRoot, butterfly, butterflyMat);
            root.position = butterflyAnchors[i];
            var renderer = root.GetComponent<Renderer>();
            block.Clear(); block.SetColor(TintId, Color.white);
            block.SetColor(WingTintId, i % 3 == 0 ? new Color(.95f, .71f, .32f) :
                i % 3 == 1 ? new Color(.56f, .78f, .94f) : new Color(.98f, .92f, .75f));
            renderer.SetPropertyBlock(block);
            butterflies.Add(new Butterfly { root = root, renderer = renderer, anchor = butterflyAnchors[i], seed = i * 2.73f });
        }
    }

    T Own<T>(T asset) where T : UnityEngine.Object { ownedAssets.Add(asset); return asset; }
    Material CreateFallbackMaterial()
    {
        Shader shader = Shader.Find("FoldedRealm/WildlifeWing");
        return shader ? Own(new Material(shader) { name = "Owned wildlife fallback" }) : null;
    }
    static Transform CreatePart(string name, Transform parent, Mesh mesh, Material material)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = true; r.lightProbeUsage = LightProbeUsage.BlendProbes;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go.transform;
    }

    void OnDisable()
    {
        SimulationRunning = false; ActiveBirdCount = ActiveButterflyCount = 0;
        hasHeroPosition = false;
        if (runtimeRoot) { runtimeRoot.gameObject.SetActive(false); Destroy(runtimeRoot.gameObject); runtimeRoot = null; }
        birds.Clear(); butterflies.Clear();
        foreach (var asset in ownedAssets) if (asset) Destroy(asset);
        ownedAssets.Clear();
    }

    // Public factories are also used by the editor installer to create compact,
    // reviewable mesh assets. Geometry is in metres, so no hidden giant animals.
    public static Mesh CreateBirdBodyMesh()
    {
        var b = new MeshBuilder();
        Color back = new Color(.31f, .40f, .43f), cream = new Color(.86f, .83f, .68f), dark = new Color(.055f, .075f, .07f);
        b.Ellipsoid(new Vector3(0, .13f, -.015f), new Vector3(.092f, .098f, .145f), back, cream);
        b.Ellipsoid(new Vector3(0, .221f, .091f), new Vector3(.071f, .069f, .075f), back, cream);
        b.Triangle(new Vector3(-.024f, .218f, .146f), new Vector3(.024f, .218f, .146f), new Vector3(0, .212f, .209f), new Color(.76f, .57f, .24f));
        b.Triangle(new Vector3(-.024f, .218f, .146f), new Vector3(0, .212f, .209f), new Vector3(0, .242f, .15f), new Color(.89f, .69f, .3f));
        b.Ellipsoid(new Vector3(-.062f, .236f, .125f), Vector3.one * .0105f, dark, dark, 5, 3);
        b.Ellipsoid(new Vector3(.062f, .236f, .125f), Vector3.one * .0105f, dark, dark, 5, 3);
        b.Triangle(new Vector3(-.041f, .15f, -.11f), new Vector3(-.052f, .13f, -.255f), new Vector3(.026f, .143f, -.12f), back * .85f);
        b.Triangle(new Vector3(.041f, .15f, -.11f), new Vector3(.052f, .13f, -.255f), new Vector3(-.026f, .143f, -.12f), back);
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * .041f;
            b.Triangle(new Vector3(x - .007f, .058f, .012f), new Vector3(x + .007f, .058f, .012f), new Vector3(x, .012f, .025f), new Color(.43f, .34f, .2f));
            b.Triangle(new Vector3(x - .019f, .008f, .06f), new Vector3(x + .019f, .008f, .06f), new Vector3(x, .012f, -.005f), new Color(.43f, .34f, .2f));
        }
        return b.Build("Small songbird body");
    }

    public static Mesh CreateBirdWingMesh()
    {
        var b = new MeshBuilder();
        Vector3 root = new Vector3(.042f, 0, .07f);
        Vector3[] edge = { new Vector3(.10f, .012f, .055f), new Vector3(.19f, 0, -.02f),
            new Vector3(.238f, -.006f, -.105f), new Vector3(.20f, -.007f, -.105f), new Vector3(.165f, -.004f, -.12f),
            new Vector3(.13f, -.002f, -.11f), new Vector3(.08f, 0, -.08f) };
        for (int i = 0; i < edge.Length - 1; i++) b.Triangle(root, edge[i], edge[i + 1],
            Color.Lerp(new Color(.23f, .32f, .37f), new Color(.57f, .62f, .57f), i % 2 * .3f));
        return b.Build("Small songbird feathered wing");
    }

    public static Mesh CreateButterflyMesh()
    {
        var b = new MeshBuilder();
        b.Ellipsoid(Vector3.zero, new Vector3(.008f, .008f, .041f), new Color(.19f, .16f, .12f), new Color(.25f, .2f, .12f), 5, 4);
        Vector3[] outline = { new Vector3(.008f, 0, .025f), new Vector3(.06f, 0, .07f), new Vector3(.102f, 0, .048f),
            new Vector3(.097f, 0, .015f), new Vector3(.057f, 0, -.005f), new Vector3(.076f, 0, -.036f),
            new Vector3(.047f, 0, -.067f), new Vector3(.008f, 0, -.025f) };
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 centre = new Vector3(.025f * side, 0, -.002f);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector3 a = outline[i]; a.x *= side;
                Vector3 c = outline[(i + 1) % outline.Length]; c.x *= side;
                b.ButterflyTriangle(centre, a, c);
            }
        }
        return b.Build("Small flower butterfly");
    }

    sealed class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>(), hinge = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> indices = new List<int>();
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Add(a, normal, color); Add(b, normal, color); Add(c, normal, color);
        }
        public void ButterflyTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            int first = vertices.Count;
            Triangle(a, b, c, new Color(.91f, .72f, .36f));
            for (int i = first; i < vertices.Count; i++) { hinge[i] = Vector2.right; uv[i] = new Vector2(Mathf.Abs(vertices[i].x) / .105f, (vertices[i].z + .07f) / .14f); }
        }
        void Add(Vector3 vertex, Vector3 normal, Color color)
        {
            indices.Add(vertices.Count); vertices.Add(vertex); normals.Add(normal); colors.Add(color); uv.Add(Vector2.zero); hinge.Add(Vector2.zero);
        }
        public void Ellipsoid(Vector3 centre, Vector3 radius, Color back, Color belly, int segments = 9, int rings = 6)
        {
            for (int ring = 0; ring < rings; ring++) for (int segment = 0; segment < segments; segment++)
            {
                Vector3 a = Sphere(ring, segment, rings, segments), d = Sphere(ring, segment + 1, rings, segments);
                Vector3 c = Sphere(ring + 1, segment + 1, rings, segments), e = Sphere(ring + 1, segment, rings, segments);
                Color color = Color.Lerp(back, belly, Mathf.Clamp01(-(a.y + c.y) * .65f + .2f));
                Triangle(centre + Vector3.Scale(a, radius), centre + Vector3.Scale(d, radius), centre + Vector3.Scale(c, radius), color);
                Triangle(centre + Vector3.Scale(a, radius), centre + Vector3.Scale(c, radius), centre + Vector3.Scale(e, radius), color);
            }
        }
        static Vector3 Sphere(int r, int s, int rings, int segments)
        {
            float latitude = Mathf.PI * r / rings, longitude = Mathf.PI * 2 * s / segments;
            return new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
        }
        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetUVs(0, uv); mesh.SetUVs(1, hinge); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            // Include shader wing rotation so culling never removes raised wings.
            Bounds bounds = mesh.bounds; bounds.Expand(.23f); mesh.bounds = bounds;
            return mesh;
        }
    }
}
