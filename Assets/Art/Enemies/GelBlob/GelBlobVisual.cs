using UnityEngine;
using UnityEngine.Rendering;

// One transparent hull, one brief shared-material contact trace, eight reused droplets.
[DisallowMultipleComponent]
public sealed class GelBlobVisual : MonoBehaviour
{
    [SerializeField] Transform gel, core;
    [SerializeField] Renderer gelRenderer, lens;
    [SerializeField] LineRenderer warning;
    [SerializeField] Mesh dropletMesh;
    [SerializeField] Material dropletMaterial;
    GelBlobAI ai;
    EnemyGroundQuery ground;
    MaterialPropertyBlock gelBlock, lensBlock, smearBlock, bubbleBlock;
    readonly Transform[] droplets = new Transform[8];
    readonly Vector3[] velocities = new Vector3[8];
    readonly Vector3[] ring = new Vector3[33];
    readonly Vector3[] lockOutline = new Vector3[13];
    readonly Vector3[] smearVertices = new Vector3[17];
    readonly float[] dropletSizes = new float[8];
    // One tiny texture-free flash, shared material; no new particles/lights/assets.
    static Material hitMaterial;
    static int hitMaterialOwners;
    readonly Vector3[] flashVertices = new Vector3[25];
    readonly Color[] flashColours = new Color[25];
    Mesh flashMesh;
    MeshRenderer hitFlash;
    Vector3 flashContact;
    float hitStartedAt;
    bool hitLive, ownsHitMaterial;
    Vector3[] bubbleHomes;
    Vector3 hitPoint = new Vector3(0,.4f,.45f);
    Mesh smearMesh;
    MeshRenderer smear;
    // One connected crosshair outline: a target marker, not a second damage circle.
    static readonly Vector2[] lockShape = {
        new Vector2(-.25f,-1), new Vector2(.25f,-1), new Vector2(.25f,-.25f),
        new Vector2(1,-.25f), new Vector2(1,.25f), new Vector2(.25f,.25f),
        new Vector2(.25f,1), new Vector2(-.25f,1), new Vector2(-.25f,.25f),
        new Vector2(-1,.25f), new Vector2(-1,-.25f), new Vector2(-.25f,-.25f), new Vector2(-.25f,-1)
    };
    LineRenderer lockMarker;
    Vector3 lockPoint, lockNormal = Vector3.up, irisScale;
    Vector3 hitDirection, warningPoint, wobble, wobbleVelocity, previousPosition;
    Renderer[] poweredParts, trappedBubbles;
    float hitAge = 10f, deathAge, splashAge = 10f, clock, flowClock, smearAge = 10f, impactAge = 10f, lockAge = 10f;
    float coreEnergy = 2f, deathEnergy = 1f, deathIris = 1f;
    bool dead, warningCached;
    public bool WarningEnabled => warning && warning.enabled;
    public bool LockMarkerEnabled => lockMarker && lockMarker.enabled;
    public bool SmearEnabled => smear && smear.enabled;
    public bool HitFlashEnabled => hitFlash && hitFlash.enabled;
    public int BubbleCount => trappedBubbles != null ? trappedBubbles.Length : 0;
    public int DropletCount { get { int n = 0; foreach (var d in droplets) if (d && d.gameObject.activeSelf) n++; return n; } }
    void Awake()
    {
        gelBlock = new MaterialPropertyBlock(); lensBlock = new MaterialPropertyBlock();
        smearBlock = new MaterialPropertyBlock(); bubbleBlock = new MaterialPropertyBlock();
        ai = GetComponent<GelBlobAI>(); ground = new EnemyGroundQuery(transform);
        previousPosition = transform.position;
        irisScale = lens.transform.localScale;
        poweredParts = System.Array.FindAll(core.GetComponentsInChildren<Renderer>(), r => r.sharedMaterial == lens.sharedMaterial);
        // Keep the authored bubble; two tiny opaque bubbles share the existing
        // sphere and core material. No nested transparent shell or particle system.
        var authoredBubbles = System.Array.FindAll(gel.GetComponentsInChildren<Renderer>(), r => r != gelRenderer);
        trappedBubbles = new Renderer[authoredBubbles.Length + 2];
        authoredBubbles.CopyTo(trappedBubbles, 0);
        for (int i = authoredBubbles.Length; i < trappedBubbles.Length; i++)
        {
            var bubble = new GameObject("Reusable internal bubble " + i); bubble.transform.SetParent(gel, false);
            bubble.AddComponent<MeshFilter>().sharedMesh = dropletMesh;
            var renderer = bubble.AddComponent<MeshRenderer>(); renderer.sharedMaterial = dropletMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            bubble.transform.localPosition = i == authoredBubbles.Length ? new Vector3(-.33f,.44f,.06f) : new Vector3(.08f,.27f,-.36f);
            bubble.transform.localScale = Vector3.one * .055f; trappedBubbles[i] = renderer;
        }
        bubbleHomes = new Vector3[trappedBubbles.Length];
        for (int i = 0; i < trappedBubbles.Length; i++) bubbleHomes[i] = trappedBubbles[i].transform.localPosition;
        BuildContactTrace();
        BuildHitFlash();
        // Built once per enemy, same shared material, no mesh/texture/light asset.
        lockMarker = new GameObject("Reusable landing lock crosshair").AddComponent<LineRenderer>();
        lockMarker.transform.SetParent(transform, false); lockMarker.useWorldSpace = true;
        lockMarker.sharedMaterial = warning.sharedMaterial; lockMarker.positionCount = lockOutline.Length;
        lockMarker.widthMultiplier = .03f; lockMarker.shadowCastingMode = ShadowCastingMode.Off;
        lockMarker.receiveShadows = false; lockMarker.numCornerVertices = 1; lockMarker.enabled = false;
        for (int i = 0; i < droplets.Length; i++)
        {
            var go = new GameObject("Reusable gel droplet " + i); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = dropletMesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = dropletMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; go.SetActive(false); droplets[i] = go.transform;
            dropletSizes[i] = .085f + (i % 3) * .027f;
        }
    }
    void LateUpdate()
    {
        if (!ai) return;
        bool active = ai.SimulationRunning && Time.deltaTime > 0f;
        bool details = EnemyPresentationBudget.Details(transform.position);
        float charge = !dead && ai.Phase == GelBlobAI.State.Windup ? ai.Progress : 0f;
        if (!ai.SimulationRunning)
        {
            // A real menu/transition cancels short feedback. Sword hit-stop still
            // has SimulationRunning=true and freezes it, rather than clearing it.
            hitLive = false; hitAge = smearAge = splashAge = 10f;
            foreach (var d in droplets) if (d) d.gameObject.SetActive(false);
        }
        if (active)
        {
            float dt = Mathf.Min(Time.deltaTime, .04f);
            clock += Time.deltaTime; splashAge += Time.deltaTime; impactAge += Time.deltaTime; lockAge += Time.deltaTime;
            flowClock += Time.deltaTime * (1f + charge * 1.4f); smearAge += Time.deltaTime;
            if (dead) deathAge += Time.deltaTime;
            // Bounded, damped inertia: the top lags, while the footprint stays pinned to the floor.
            Vector3 motion = transform.InverseTransformDirection((transform.position - previousPosition) / Mathf.Max(.001f, Time.deltaTime));
            Vector3 wanted = Vector3.ClampMagnitude(-new Vector3(motion.x, 0, motion.z) * .006f, .035f);
            if (hitAge < .15f) wanted += hitDirection * (.045f * Mathf.Exp(-hitAge * 15f));
            for (int sub = 0; sub < 2; sub++)
            { wobbleVelocity += ((wanted - wobble) * 180f - wobbleVelocity * 14f) * (dt * .5f); wobble += wobbleVelocity * (dt * .5f); }
            wobble = Vector3.ClampMagnitude(wobble, .05f);
        }
        // Don't subtract the entire frame that elapsed BEFORE the damage event.
        // Time.time also keeps this cue frozen during the existing sword hit-stop.
        if (hitLive) { hitAge = Mathf.Max(0f,Time.time-hitStartedAt); if (hitAge >= .34f) hitLive = false; }
        previousPosition = transform.position;
        Vector3 shape = ai.Shape;
        if (dead) shape = new Vector3(1f + Mathf.Min(deathAge, .35f), Mathf.Max(.02f, 1f - deathAge * 2f), 1f + Mathf.Min(deathAge, .35f));
        gel.localScale = shape * ai.BodyScale;
        // Shear the gel's top in the shader, never rotate its base into the terrain.
        gel.localRotation = Quaternion.identity;
        float idle = ai.Phase == GelBlobAI.State.Idle || ai.Phase == GelBlobAI.State.Approach ? Mathf.Sin(clock * 4f) * .012f : 0f;
        float coreHeight = dead ? Mathf.Lerp(.43f, .22f, Mathf.Clamp01(deathAge * 3f)) : Mathf.Lerp(.235f, .43f, Mathf.InverseLerp(.56f, 1f, shape.y)) + idle;
        core.localScale = Vector3.one * ai.BodyScale;
        core.localPosition = (Vector3.up * coreHeight + new Vector3(wobble.x, 0, wobble.z) * .35f) * ai.BodyScale;
        float flightTilt = ai.Phase == GelBlobAI.State.Flight ? Mathf.Lerp(-10f, 9f, ai.Progress) : 0f;
        core.localRotation = Quaternion.Euler(flightTilt, Mathf.Sin(clock * 1.8f) * 3f, wobble.x * -55f);
        gelRenderer.GetPropertyBlock(gelBlock);
        gelBlock.SetFloat("_Charge", charge);
        gelBlock.SetFloat("_Hit", dead ? 0f : 1f - Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.055f,.34f,hitAge)));
        gelBlock.SetVector("_HitPoint", hitPoint); gelBlock.SetFloat("_HitAge", hitAge);
        gelBlock.SetFloat("_FlowClock", flowClock); gelBlock.SetFloat("_Detail", details ? 1f : 0f);
        gelBlock.SetFloat("_GelClock", clock);
        gelBlock.SetVector("_Wobble", wobble);
        gelBlock.SetFloat("_Impact", Mathf.Exp(-impactAge * 12f));
        gelBlock.SetFloat("_Fade", dead ? 1f - Mathf.Clamp01(deathAge / .55f) : 1f);
        gelRenderer.SetPropertyBlock(gelBlock); gelRenderer.enabled = !dead || deathAge < .55f;
        UpdateHitFlash(details);
        bubbleBlock.SetColor("_EmissionColor", new Color(.13f,.62f,.52f) * (.35f + charge * .75f));
        for (int i = 0; i < trappedBubbles.Length; i++)
        {
            Renderer bubble = trappedBubbles[i]; bubble.enabled = details && gelRenderer.enabled;
            if (details)
            {
                Vector3 drift = new Vector3(Mathf.Sin(flowClock * 1.9f + i) * .012f,
                    Mathf.Sin(flowClock * 1.3f + i * 2.1f) * .032f, Mathf.Cos(flowClock * 1.5f + i) * .01f);
                bubble.transform.localPosition = bubbleHomes[i] + drift;
                bubble.SetPropertyBlock(bubbleBlock);
            }
        }
        smear.enabled = ai.SimulationRunning && details && smearAge < .36f;
        if (smear.enabled)
        {
            smearBlock.SetFloat("_Smear", 1f); smearBlock.SetFloat("_SmearFade", 1f - Mathf.SmoothStep(0f,1f,smearAge / .36f));
            smearBlock.SetFloat("_GelClock", smearAge); smear.SetPropertyBlock(smearBlock);
        }
        lens.GetPropertyBlock(lensBlock);
        // Near a contacted player we cannot safely widen the physical hull.
        // A sleepy iris communicates recovery even when that safety constraint holds.
        lens.transform.localScale = Vector3.Scale(irisScale, new Vector3(1f,
            dead ? deathIris : ai.Phase == GelBlobAI.State.Recover ? Mathf.Lerp(.35f, 1f, ai.RecoveryReadiness) : 1f, 1f));
        float energy = dead ? deathEnergy * Mathf.Max(0f, 1f - deathAge * 4f) : ai.Phase == GelBlobAI.State.Recover ?
            Mathf.Lerp(.35f, 2f, ai.RecoveryReadiness) : 2f + (ai.Phase == GelBlobAI.State.Windup ? ai.Progress * 2f : 0f);
        coreEnergy = energy;
        lensBlock.SetColor("_EmissionColor", new Color(.05f, .9f, 1f) * energy);
        foreach (Renderer part in poweredParts) part.SetPropertyBlock(lensBlock);
        bool contactFlash = impactAge < .18f && ai.Phase == GelBlobAI.State.Recover;
        if (warning) warning.enabled = ai.SimulationRunning && !dead && warningCached && (ai.WarningVisible || contactFlash);
        if (warning && warning.enabled)
        {
            warning.widthMultiplier = contactFlash ? Mathf.Lerp(.085f, .015f, impactAge / .18f) : ai.LandingLocked ? .065f : .042f;
            // A locked circle remains the SAME radius. Contact flash is not an extra damage wave.
            warning.startColor = warning.endColor = contactFlash ? new Color(.5f, 1f, 1f, 1f - impactAge / .18f) :
                ai.LandingLocked ? new Color(.3f, .95f, 1f) : new Color(.15f, .65f, .85f);
        }
        lockMarker.enabled = ai.SimulationRunning && !dead && warningCached && ai.WarningVisible && ai.LandingLocked && lockAge < 10f;
        if (lockMarker.enabled)
        {
            float shrink = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lockAge / .14f));
            float span = Mathf.Lerp(.42f, .17f, shrink);
            for (int i = 0; i < lockOutline.Length; i++)
            {
                Vector2 offset = lockShape[i] * span;
                // Cache the support plane once at lock, no per-frame ground queries.
                float y = -(lockNormal.x * offset.x + lockNormal.z * offset.y) / Mathf.Max(.1f, lockNormal.y);
                lockOutline[i] = lockPoint + new Vector3(offset.x, y + .045f, offset.y);
            }
            lockMarker.SetPositions(lockOutline);
            lockMarker.widthMultiplier = Mathf.Lerp(.045f, .03f, shrink);
            lockMarker.startColor = lockMarker.endColor = Color.Lerp(new Color(.9f, 1f, 1f), new Color(.4f, .95f, 1f), shrink);
        }
        for (int index = 0; index < droplets.Length; index++)
        {
            Transform d = droplets[index];
            if (!d || !d.gameObject.activeSelf) continue;
            if (!details) { d.gameObject.SetActive(false); continue; }
            if (splashAge >= .4f) { d.gameObject.SetActive(false); continue; }
            if (active)
            {
                velocities[index] += Vector3.down * 6f * Time.deltaTime; d.position += velocities[index] * Time.deltaTime;
                float size = dropletSizes[index] * (1f - splashAge / .4f);
                d.localScale = new Vector3(size, size * Mathf.Lerp(1.35f,.55f,splashAge / .4f), size);
                // Ground heights were sampled ONCE at emission, not every frame.
                Vector3 offset = d.position - dropletGround[index];
                Vector3 normal = dropletNormals[index];
                float floor = dropletGround[index].y - (normal.x * offset.x + normal.z * offset.z) / Mathf.Max(.1f,normal.y) + .015f;
                if (normal.y > .1f && d.position.y < floor) { d.position = new Vector3(d.position.x,floor,d.position.z); velocities[index] = Vector3.zero; }
            }
        }
    }
    public void SetLanding(Vector3 point)
    {
        if (warningCached && Vector3.Distance(warningPoint, point) < .025f) return;
        warningPoint = point; warningCached = true;
        for (int i = 0; i < ring.Length; i++)
        {
            float a = i * Mathf.PI * 2f / (ring.Length - 1);
            Vector3 p = point + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ai.LandingRadius;
            if (ground.TrySample(p, .9f, .9f, 35f, out RaycastHit floor)) p.y = floor.point.y + .035f;
            ring[i] = p;
        }
        warning.positionCount = ring.Length; warning.SetPositions(ring);
    }
    public void LockLanding()
    {
        if (!warningCached) return;
        lockAge = 0f; lockPoint = warningPoint; lockNormal = Vector3.up;
        if (ground.TrySample(warningPoint, .9f, .9f, 35f, out RaycastHit floor))
        { lockPoint.y = floor.point.y; lockNormal = floor.normal; }
    }
    public void HideWarning()
    { warningCached = false; lockAge = 10f; if (warning) warning.enabled = false; if (lockMarker) lockMarker.enabled = false; }
    public void Hit(Vector3 direction, Vector3 worldContact)
    {
        hitAge = 0f; hitStartedAt = Time.time; hitLive = true;
        hitDirection = transform.InverseTransformDirection(direction.normalized);
        hitPoint = gel.InverseTransformPoint(worldContact);
        if ((hitPoint - Vector3.up * .4f).sqrMagnitude < .01f || !float.IsFinite(hitPoint.x + hitPoint.y + hitPoint.z))
            hitPoint = Vector3.up * .4f - (hitDirection.sqrMagnitude > .01f ? hitDirection : Vector3.back) * .45f;
        hitPoint.y = Mathf.Clamp(hitPoint.y,.04f,.84f);
        Vector2 radial = Vector2.ClampMagnitude(new Vector2(hitPoint.x,hitPoint.z),.55f);
        // Sword collision uses the conservative physical box. Project its contact
        // onto the tapered visible surface, so a valid box hit has a visible patch.
        float radius = SurfaceRadius(hitPoint.y);
        if (radial.sqrMagnitude < .0001f) radial = new Vector2(-hitDirection.x,-hitDirection.z);
        if (radial.sqrMagnitude < .0001f) radial = Vector2.up;
        radial = radial.normalized * radius;
        hitPoint.x = radial.x; hitPoint.z = radial.y;
        Vector3 outward = transform.TransformDirection(new Vector3(radial.x,0,radial.y).normalized);
        Vector3 contact = gel.TransformPoint(hitPoint) + outward * .035f;
        flashContact = transform.InverseTransformPoint(contact);
        if (EnemyPresentationBudget.Details(transform.position)) EmitDroplets(contact,outward);
        UpdateHitFlash(EnemyPresentationBudget.Details(transform.position));
    }
    public void Splash()
    {
        impactAge = 0f;
        if (!EnemyPresentationBudget.Details(transform.position)) return;
        StampContactTrace();
        EmitDroplets(transform.position + Vector3.up * .15f,Vector3.zero);
    }
    void EmitDroplets(Vector3 origin, Vector3 outward)
    {
        splashAge = 0f;
        for (int i = 0; i < droplets.Length; i++)
        {
            float a = i * Mathf.PI * 2f / droplets.Length;
            droplets[i].gameObject.SetActive(true); droplets[i].localScale = Vector3.one * dropletSizes[i];
            float spread = .9f + (i % 3) * .22f;
            velocities[i] = new Vector3(Mathf.Cos(a) * spread, .95f + (i % 3) * .25f, Mathf.Sin(a) * spread) + outward * 1.15f;
            // A wet impact has a silhouette in its FIRST frozen frame, rather
            // than eight coincident spheres waiting for hit-stop to end.
            droplets[i].position = origin + (outward.sqrMagnitude > .01f ? velocities[i].normalized*(.035f+(i%3)*.025f) : Vector3.zero);
            droplets[i].rotation = outward.sqrMagnitude > .01f ? Quaternion.FromToRotation(Vector3.up,velocities[i].normalized) : Quaternion.identity;
            Vector3 end = origin + new Vector3(velocities[i].x,0,velocities[i].z) * .3f;
            bool supported = ground.TrySample(end,.3f,1.8f,35f,out RaycastHit support);
            dropletGround[i] = supported ? support.point : Vector3.zero;
            dropletNormals[i] = supported ? support.normal : Vector3.zero;
        }
    }
    public void Die()
    {
        deathEnergy = Mathf.Min(1f, coreEnergy);
        deathIris = Mathf.Clamp01(lens.transform.localScale.y / Mathf.Max(.001f, irisScale.y));
        dead = true; deathAge = 0f; HideWarning(); Splash();
    }
    public void ClearTransient()
    { hitLive = false; hitAge = smearAge = splashAge = impactAge = 10f; if (hitFlash) hitFlash.enabled = false; if (smear) smear.enabled = false; HideWarning(); foreach (var d in droplets) if (d) d.gameObject.SetActive(false); }
    public void ResetVisual()
    { dead = false; deathAge = 0f; coreEnergy = 2f; deathEnergy = deathIris = 1f; hitAge = splashAge = 10f; wobble = wobbleVelocity = Vector3.zero; previousPosition = transform.position; ClearTransient(); gelRenderer.enabled = true; gel.localScale = Vector3.one * ai.BodyScale; lens.transform.localScale = irisScale; }
    void OnDisable() => ClearTransient();
    readonly Vector3[] dropletGround = new Vector3[8], dropletNormals = new Vector3[8];
    void BuildContactTrace()
    {
        var go = new GameObject("Reusable wet contact trace"); go.transform.SetParent(transform,false);
        smearMesh = new Mesh { name = "Gel contact fan (16 triangles)" }; smearMesh.MarkDynamic();
        var indices = new int[48]; var uv = new Vector2[17]; var colours = new Color[17];
        colours[0] = Color.white;
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16;
            uv[i+1] = new Vector2(Mathf.Cos(a),Mathf.Sin(a)); colours[i+1] = new Color(1,1,1,0);
            indices[i*3] = 0; indices[i*3+1] = (i+1)%16+1; indices[i*3+2] = i+1;
        }
        smearMesh.vertices = smearVertices; smearMesh.uv = uv; smearMesh.colors = colours; smearMesh.triangles = indices;
        go.AddComponent<MeshFilter>().sharedMesh = smearMesh;
        smear = go.AddComponent<MeshRenderer>(); smear.sharedMaterial = gelRenderer.sharedMaterial;
        smear.shadowCastingMode = ShadowCastingMode.Off; smear.receiveShadows = false; smear.enabled = false;
    }
    void StampContactTrace()
    {
        smearAge = 10f;
        if (!ground.TrySample(transform.position,.1f,.16f,35f,out RaycastHit centre) ||
            Mathf.Abs(transform.position.y - centre.point.y) > .15f) return;
        Vector3 origin = centre.point + Vector3.up * .015f;
        smear.transform.SetPositionAndRotation(origin,Quaternion.identity);
        smearVertices[0] = Vector3.zero;
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16;
            float radius = (.56f + Mathf.Sin(a*3+.6f)*.06f) * ai.BodyScale;
            Vector3 point = centre.point + new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)) * radius;
            if (!ground.TrySample(point,.5f,.5f,35f,out RaycastHit floor) || Mathf.Abs(floor.point.y-centre.point.y) > .45f) return;
            smearVertices[i+1] = smear.transform.InverseTransformPoint(floor.point + Vector3.up * .015f);
        }
        smearMesh.vertices = smearVertices; smearMesh.RecalculateNormals(); smearMesh.RecalculateBounds(); smearAge = 0f;
    }
    static float SurfaceRadius(float y)
    {
        // Same profile as the authored mesh, including its bottom skirt and cap.
        if (y < .15f) return Mathf.Lerp(.51f,.53f,Mathf.InverseLerp(.035f,.15f,y));
        if (y < .33f) return Mathf.Lerp(.53f,.50f,Mathf.InverseLerp(.15f,.33f,y));
        if (y < .54f) return Mathf.Lerp(.50f,.42f,Mathf.InverseLerp(.33f,.54f,y));
        if (y < .73f) return Mathf.Lerp(.42f,.31f,Mathf.InverseLerp(.54f,.73f,y));
        return Mathf.Lerp(.31f,.18f,Mathf.InverseLerp(.73f,.86f,y));
    }
    void BuildHitFlash()
    {
        if (!hitMaterial)
        {
            Shader shader = Resources.Load<Shader>("EnemyLocalImpact");
            if (!shader) { Debug.LogError("Gel hit cue requires existing EnemyLocalImpact shader.",this); return; }
            hitMaterial = new Material(shader) { name="Shared gel wet-hit flash", hideFlags=HideFlags.DontSave };
        }
        hitMaterialOwners++; ownsHitMaterial = true;
        var go = new GameObject("Reusable wet sword impact"); go.transform.SetParent(transform,false);
        flashMesh = new Mesh { name="Gel wet-hit flash (36 triangles)" }; flashMesh.MarkDynamic();
        var indices = new int[108];
        for (int i=0; i<12; i++)
        {
            int next=(i+1)%12;
            indices[i*9]=0; indices[i*9+1]=next+1; indices[i*9+2]=i+1;
            indices[i*9+3]=i+1; indices[i*9+4]=next+13; indices[i*9+5]=i+13;
            indices[i*9+6]=i+1; indices[i*9+7]=next+1; indices[i*9+8]=next+13;
        }
        var normals=new Vector3[25]; for (int i=0;i<normals.Length;i++) normals[i]=Vector3.back;
        flashMesh.vertices=flashVertices; flashMesh.normals=normals; flashMesh.colors=flashColours; flashMesh.triangles=indices;
        go.AddComponent<MeshFilter>().sharedMesh=flashMesh;
        hitFlash=go.AddComponent<MeshRenderer>(); hitFlash.sharedMaterial=hitMaterial;
        hitFlash.shadowCastingMode=ShadowCastingMode.Off; hitFlash.receiveShadows=false;
        hitFlash.lightProbeUsage=LightProbeUsage.Off; hitFlash.reflectionProbeUsage=ReflectionProbeUsage.Off;
        var block=new MaterialPropertyBlock(); block.SetFloat("_ImpactRadius",0); hitFlash.SetPropertyBlock(block);
        hitFlash.enabled=false;
    }
    void UpdateHitFlash(bool details)
    {
        if (!hitFlash) return;
        hitFlash.enabled=ai.SimulationRunning && details && hitLive && hitAge < .14f;
        if (!hitFlash.enabled) return;
        Camera view=Camera.main;
        if (view) hitFlash.transform.rotation=view.transform.rotation;
        hitFlash.transform.position=transform.TransformPoint(flashContact);
        float grow=Mathf.SmoothStep(0,1,Mathf.Clamp01(hitAge/.035f));
        float radius=Mathf.Lerp(.29f,.35f,grow)*ai.BodyScale;
        float opacity=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.07f,.14f,hitAge));
        flashColours[0]=new Color(1,.97f,.80f,opacity);
        for (int i=0;i<12;i++)
        {
            float a=i*Mathf.PI/6+.3f;
            float length=radius*(i%2==0 ? 1 : .35f)*(1+(i%3-1)*.12f);
            Vector3 p=new Vector3(Mathf.Cos(a)*length,Mathf.Sin(a)*length,0);
            flashVertices[i+1]=p*.82f; flashVertices[i+13]=p;
            flashColours[i+1]=new Color(.10f,.95f,.80f,opacity);
            flashColours[i+13]=new Color(.012f,.08f,.09f,opacity*.95f);
        }
        flashMesh.SetVertices(flashVertices); flashMesh.SetColors(flashColours);
        flashMesh.bounds=new Bounds(Vector3.zero,Vector3.one*(radius*2.2f));
    }
    void OnDestroy()
    {
        if (smearMesh) Destroy(smearMesh); if (flashMesh) Destroy(flashMesh);
        if (ownsHitMaterial && --hitMaterialOwners==0 && hitMaterial) { Destroy(hitMaterial); hitMaterial=null; }
    }
}
