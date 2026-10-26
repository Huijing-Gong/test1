using UnityEngine;

// Cosmetic motion only. The AI moves the root collider; these transforms never move gameplay geometry.
[DisallowMultipleComponent]
public sealed class RailBeetleVisual : MonoBehaviour
{
    [SerializeField] private Transform visualBody;
    [SerializeField] private Transform[] legs = new Transform[4];
    [SerializeField] private Transform[] shells = new Transform[2];
    [SerializeField] private Renderer[] coreRenderers;
    [SerializeField] private LineRenderer warningLine;
    [SerializeField] private LineRenderer impactLine;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip impactClip;
    [SerializeField] private AudioClip windupClip;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color IdleCore = new Color(.06f, .85f, .94f);
    // Keep green/blue below bloom's white threshold: the charging visor should read orange-red, not white.
    private static readonly Color ChargedCore = new Color(1f, .10f, .004f);
    private const float GroundSkin = .012f;
    private TransformState bodyState;
    private TransformState[] legStates, shellStates;
    private LegMesh[] legMeshes;
    private MeshFilter chassisFilter;
    private Vector3[] chassisContactVertices;
    private readonly RaycastHit[] groundHits = new RaycastHit[32];
    private float groundRefreshTime;
    private float nextDistantFeet;
    private bool deathFitReady, deathPoseComplete;
    private Vector3 deathStartPosition, deathEndPosition;
    private Quaternion deathStartRotation, deathEndRotation;
    private Color deathCoreColor;
    private float deathCoreGlow;
    private Renderer[] shellRenderers;
    private Color[] shellColors;
    private MaterialPropertyBlock block;
    private bool cached, dead, stunned, rushing;
    private float speed, charge, phase, hitTime, impactTime, deathTime;
    private Vector3 hitDirection;
    private LineRenderer[] chargeTrails;
    private readonly Vector3[][] chargePoints = { new Vector3[2], new Vector3[2], new Vector3[2] };
    private float trailFade;
    public bool ChargeTrailVisible => chargeTrails != null && chargeTrails.Length > 0 && chargeTrails[0] && chargeTrails[0].enabled;
    public float ChargeProgress => charge;
    private RailBeetleWarningArrow warningArrow;
    public RailBeetleWarningArrow WarningArrow => warningArrow;

    private sealed class LegMesh
    {
        public MeshFilter Filter;
        public Mesh Original, Runtime;
        public Vector3[] Bind, Vertices, Normals, Probes;
        public int[] Triangles;
        public float[] Weights;
        public GroundPlane[] Ground = new GroundPlane[5];
        public float Lift;
    }

    private struct GroundPlane
    {
        public bool Valid;
        public Vector3 Point, Normal;
        public float Height(Vector3 world)
        {
            return Point.y - (Normal.x * (world.x - Point.x) + Normal.z * (world.z - Point.z)) / Normal.y;
        }
    }

    private struct TransformState
    {
        public Vector3 Position, Scale;
        public Quaternion Rotation;
        public TransformState(Transform value)
        {
            Position = value ? value.localPosition : Vector3.zero;
            Rotation = value ? value.localRotation : Quaternion.identity;
            Scale = value ? value.localScale : Vector3.one;
        }
        public void Restore(Transform value)
        {
            if (!value) return;
            value.localPosition = Position;
            value.localRotation = Rotation;
            value.localScale = Scale;
        }
    }

    private void Awake() { Cache(); }
    private void OnEnable() { Cache(); ResetVisual(); }

    private void Cache()
    {
        if (cached) return;
        if (!visualBody) visualBody = transform;
        bodyState = new TransformState(visualBody);
        legs = legs ?? new Transform[0];
        shells = shells ?? new Transform[0];
        legStates = new TransformState[legs.Length];
        legMeshes = new LegMesh[legs.Length];
        shellStates = new TransformState[shells.Length];
        for (int i = 0; i < legs.Length; i++)
        {
            legStates[i] = new TransformState(legs[i]);
            MeshFilter mesh = legs[i] ? legs[i].GetComponent<MeshFilter>() : null;
            if (mesh && mesh.sharedMesh)
            {
                Mesh source = mesh.sharedMesh;
                var part = new LegMesh
                {
                    Filter = mesh, Original = source, Runtime = Instantiate(source),
                    Bind = source.vertices, Triangles = source.triangles
                };
                part.Runtime.name = source.name + " (instance ground fit)";
                part.Runtime.hideFlags = HideFlags.DontSave;
                part.Runtime.MarkDynamic();
                part.Vertices = new Vector3[part.Bind.Length];
                part.Normals = new Vector3[part.Bind.Length];
                part.Weights = new float[part.Bind.Length];
                for (int vertex = 0; vertex < part.Bind.Length; vertex++)
                {
                    // Hip stays attached; the rigid boot moves as one piece, with the two rods stretching between.
                    part.Weights[vertex] = Mathf.Clamp01((-.03f - part.Bind[vertex].y) / .31f);
                    part.Vertices[vertex] = part.Bind[vertex];
                }
                part.Probes = FootProbes(part.Bind, source.bounds.min.y);
                mesh.sharedMesh = part.Runtime;
                legMeshes[i] = part;
            }
        }
        MeshFilter[] bodyMeshes = visualBody.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < bodyMeshes.Length; i++)
        {
            if (bodyMeshes[i].name != "Graphite chassis" || !bodyMeshes[i].sharedMesh) continue;
            chassisFilter = bodyMeshes[i];
            // This is sampled only once per death, not per frame. Duplicated flat-shaded positions are harmless.
            chassisContactVertices = chassisFilter.sharedMesh.vertices;
            break;
        }
        for (int i = 0; i < shells.Length; i++) shellStates[i] = new TransformState(shells[i]);
        shellRenderers = new Renderer[shells.Length];
        shellColors = new Color[shells.Length];
        for (int i = 0; i < shells.Length; i++)
        {
            shellRenderers[i] = shells[i] ? shells[i].GetComponent<Renderer>() : null;
            Material material = shellRenderers[i] ? shellRenderers[i].sharedMaterial : null;
            shellColors[i] = material && material.HasProperty(BaseColorId)
                ? material.GetColor(BaseColorId) : new Color(.79f, .78f, .66f);
        }
        block = new MaterialPropertyBlock();
        CacheChargeTrails();
        cached = true;
    }

    private void CacheChargeTrails()
    {
        if (chargeTrails != null || !warningLine || !warningLine.sharedMaterial) return;
        chargeTrails = new LineRenderer[3];
        for (int i = 0; i < chargeTrails.Length; i++)
        {
            GameObject holder = new GameObject("Reusable blue charge rail " + i);
            holder.hideFlags = HideFlags.DontSave;
            holder.transform.SetParent(transform, false);
            var line = holder.AddComponent<LineRenderer>();
            line.sharedMaterial = warningLine.sharedMaterial;
            line.useWorldSpace = true; line.positionCount = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false; line.numCapVertices = 2;
            line.widthMultiplier = i == 0 ? .045f : .024f;
            line.enabled = false;
            chargeTrails[i] = line;
        }
    }

    public void SetChargeTrail(Vector3 start, Vector3 end, bool active)
    {
        Cache();
        if (chargeTrails == null) return;
        if (!active || dead)
        {
            trailFade = 0f;
            for (int i = 0; i < chargeTrails.Length; i++) if (chargeTrails[i]) chargeTrails[i].enabled = false;
            return;
        }
        Vector3 heading = end - start;
        heading.y = 0f;
        if (heading.sqrMagnitude < .001f) return;
        Vector3 side = Vector3.Cross(Vector3.up, heading.normalized);
        for (int i = 0; i < chargeTrails.Length; i++)
        {
            Vector3 lateral = side * (i == 0 ? 0f : i == 1 ? -.36f : .36f);
            Vector3 a = start + lateral, b = end + lateral;
            GroundPlane groundA = ProbeGround(a), groundB = ProbeGround(b);
            a.y = groundA.Valid ? groundA.Height(a) + .075f : start.y + .06f;
            b.y = groundB.Valid ? groundB.Height(b) + .075f : end.y + .06f;
            chargePoints[i][0] = a; chargePoints[i][1] = b;
            chargeTrails[i].SetPositions(chargePoints[i]);
            chargeTrails[i].startColor = new Color(.03f, .45f, 1f, .25f);
            chargeTrails[i].endColor = new Color(.13f, .9f, 1f, 1f);
            chargeTrails[i].enabled = true;
        }
        trailFade = .10f;
    }

    private static Vector3[] FootProbes(Vector3[] vertices, float soleY)
    {
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].y > soleY + .001f) continue;
            minX = Mathf.Min(minX, vertices[i].x); maxX = Mathf.Max(maxX, vertices[i].x);
            minZ = Mathf.Min(minZ, vertices[i].z); maxZ = Mathf.Max(maxZ, vertices[i].z);
        }
        float x = (minX + maxX) * .5f, z = (minZ + maxZ) * .5f;
        return new[] { new Vector3(x, soleY, z), new Vector3(minX, soleY, z),
            new Vector3(maxX, soleY, z), new Vector3(x, soleY, minZ), new Vector3(x, soleY, maxZ) };
    }

    private void OnDestroy()
    {
        if (chargeTrails != null)
            for (int i = 0; i < chargeTrails.Length; i++)
            {
                if (!chargeTrails[i]) continue;
                if (Application.isPlaying) Destroy(chargeTrails[i].gameObject);
                else DestroyImmediate(chargeTrails[i].gameObject);
            }
        if (legMeshes == null) return;
        for (int i = 0; i < legMeshes.Length; i++)
        {
            LegMesh part = legMeshes[i];
            if (part == null || !part.Runtime) continue;
            if (part.Filter && part.Filter.sharedMesh == part.Runtime) part.Filter.sharedMesh = part.Original;
            if (Application.isPlaying) Destroy(part.Runtime); else DestroyImmediate(part.Runtime);
        }
    }

    public void SetMotion(float metresPerSecond) { speed = Mathf.Max(0f, metresPerSecond); }
    public void SetAttack(float charge01, bool isRushing, float warningSeconds = .9f)
    {
        if (rushing && !isRushing) stopRecoilTime = .2f;
        var sound = Application.isPlaying ? GetComponent<EnemySoundEmitter>() : null;
        if (sound && !dead)
        {
            if (!isRushing && charge <= .001f && charge01 > .001f)
                sound.Cue(EnemySoundCue.BeetleCharge, seconds: warningSeconds);
            if (!rushing && isRushing) { sound.StopWarning(); sound.Cue(EnemySoundCue.BeetleRush); }
            if (!isRushing && charge01 <= .001f) sound.StopWarning();
        }
        // Cosmetic editor previews retain their original optional audio references.
        else if (!dead && !isRushing && charge <= .001f && charge01 > .001f && audioSource && windupClip)
        {
            audioSource.pitch = .88f;
            audioSource.PlayOneShot(windupClip, .21f);
        }
        charge = Mathf.Clamp01(charge01);
        rushing = isRushing;
    }
    private bool wallStunned;
    private float stopRecoilTime;
    public void SetStunned(bool value, bool wall = false)
    {
        if (value && !stunned && !dead)
        {
            var sound = GetComponent<EnemySoundEmitter>();
            if (sound)
            {
                sound.StopAction();
                if (wall) sound.QueueCue(EnemySoundCue.Stun, .32f, .65f);
                else sound.Cue(EnemySoundCue.Stun, .75f);
            }
        }
        stunned = value; wallStunned = value && wall;
    }

    public void SetWarningPath(Vector3[] points, int count)
    {
        if(Application.isPlaying)
        {
            if(!warningArrow) warningArrow=gameObject.GetComponent<RailBeetleWarningArrow>() ?? gameObject.AddComponent<RailBeetleWarningArrow>();
            var actor=GetComponentInParent<RailBeetleAI>();
            warningArrow.SetPath(points,dead?0:count,actor?actor.WarningHalfWidth:.55f,charge);
            if(warningLine) warningLine.enabled=false;
            return;
        }
        if (!warningLine) return;
        warningLine.enabled = !dead && count >= 2;
        if (!warningLine.enabled) return;
        warningLine.positionCount = count;
        for (int i = 0; i < count; i++) warningLine.SetPosition(i, points[i]);
        Color tint = Color.Lerp(new Color(1f, .7f, .25f, .7f), new Color(1f, .35f, .05f, 1f), charge);
        warningLine.startColor = tint; tint.a = .25f; warningLine.endColor = tint;
    }

    public void SetWarning(Vector3 start, Vector3 end, bool visible)
    {
        if(warningArrow) warningArrow.Clear();
        if (!warningLine) return;
        visible &= !dead && (end - start).sqrMagnitude > .01f;
        warningLine.enabled = visible;
        if (!visible) return;
        Vector3 delta = end - start; // Telegraph the actual full charge reach, including the run-through.
        warningLine.positionCount = 2;
        warningLine.SetPosition(0, start);
        warningLine.SetPosition(1, start + delta);
        Color tint = Color.Lerp(new Color(1f, .7f, .25f, .7f), new Color(1f, .35f, .05f, 1f), charge);
        warningLine.startColor = tint;
        warningLine.endColor = new Color(tint.r, tint.g, tint.b, .25f);
    }

    public void PlayHit(Vector3 worldPoint)
    {
        if (dead) return;
        Cache();
        hitTime = .15f;
        hitDirection = visualBody.InverseTransformDirection(transform.position - worldPoint);
        hitDirection.y = 0f;
        hitDirection = hitDirection.sqrMagnitude > .001f ? hitDirection.normalized : Vector3.back;
        Spark(worldPoint, false);
        if (!GetComponent<EnemySoundEmitter>()) PlayImpactAudio(.28f, 1.15f);
    }

    public void PlayImpact(Vector3 worldPoint, bool wall)
    {
        if (dead) return;
        Cache();
        hitTime = wall ? .25f : .12f;
        hitDirection = Vector3.back;
        Spark(worldPoint, wall);
        var sound = GetComponent<EnemySoundEmitter>();
        if (sound)
        {
            sound.StopAction();
            sound.Cue(wall ? EnemySoundCue.WallCrash : EnemySoundCue.StoneHit, wall ? 1f : .75f, point: worldPoint);
        }
        else PlayImpactAudio(wall ? .48f : .35f, wall ? .82f : 1f);
    }

    public void PlayDeath()
    {
        wallStunned = false; stopRecoilTime = 0f;
        Cache();
        if (dead) return;
        deathStartPosition = visualBody.localPosition;
        deathStartRotation = visualBody.localRotation;
        deathCoreColor = charge > .001f ? ChargedCore : IdleCore;
        deathCoreGlow = charge > .001f ? 1.5f + charge * .65f : 1.7f;
        deathFitReady = false;
        deathPoseComplete = false;
        dead = true;
        if(warningArrow) warningArrow.Clear();
        deathTime = 0f;
        speed = charge = 0f;
        rushing = stunned = false;
        if (warningLine) warningLine.enabled = false;
        SetChargeTrail(Vector3.zero, Vector3.zero, false);
        hitTime = impactTime = 0f;
        if (impactLine) impactLine.enabled = false;
        if (!GetComponent<EnemySoundEmitter>()) PlayImpactAudio(.38f, .7f);
    }

    public void ResetVisual()
    {
        if(warningArrow) warningArrow.Clear();
        GetComponent<EnemySoundEmitter>()?.ResetForJourney();
        wallStunned = false; stopRecoilTime = 0f;
        Cache();
        dead = stunned = rushing = false;
        speed = charge = phase = hitTime = impactTime = deathTime = 0f;
        groundRefreshTime = nextDistantFeet = 0f;
        deathFitReady = false;
        deathPoseComplete = false;
        bodyState.Restore(visualBody);
        for (int i = 0; i < legs.Length; i++)
        {
            legStates[i].Restore(legs[i]);
            LegMesh part = legMeshes[i];
            if (part == null) continue;
            part.Lift = 0f;
            System.Array.Copy(part.Bind, part.Vertices, part.Bind.Length);
            part.Runtime.vertices = part.Vertices;
            part.Runtime.normals = part.Original.normals;
            part.Runtime.bounds = part.Original.bounds;
            for (int probe = 0; probe < part.Ground.Length; probe++) part.Ground[probe].Valid = false;
        }
        for (int i = 0; i < shells.Length; i++) shellStates[i].Restore(shells[i]);
        if (warningLine) warningLine.enabled = false;
        if (impactLine) impactLine.enabled = false;
        if (audioSource) audioSource.Stop();
        SetChargeTrail(Vector3.zero, Vector3.zero, false);
        ApplyCore(IdleCore, 1.7f);
        FlashShell(0f);
    }

    private void LateUpdate()
    {
        TickVisual(Mathf.Min(Time.deltaTime, .05f));
    }

    private void TickVisual(float dt)
    {
        Cache();
        groundRefreshTime -= dt;
        phase += dt * (3f + Mathf.Min(speed, 9f) * 2.6f);
        hitTime = Mathf.Max(0f, hitTime - dt);
        stopRecoilTime = Mathf.Max(0f, stopRecoilTime - dt);
        impactTime = Mathf.Max(0f, impactTime - dt);
        AnimateImpact();
        if (!rushing && trailFade > 0f)
        {
            trailFade = Mathf.Max(0f, trailFade - dt);
            if (trailFade <= 0f) SetChargeTrail(Vector3.zero, Vector3.zero, false);
        }
        if (dead) { AnimateDeath(dt); return; }

        float walkAmount = Mathf.Clamp01(speed / 2.5f);
        float bob = Mathf.Sin(phase * 2f) * Mathf.Lerp(.008f, .025f, walkAmount);
        float shake = hitTime > 0f ? Mathf.Sin(hitTime * 88f) * hitTime * .3f : 0f;
        float pitch = rushing ? 9f : charge * 5f;
        if (stunned) pitch = 6f + Mathf.Sin(phase * 1.4f) * 1.5f;
        pitch += Mathf.Sin(stopRecoilTime / .2f * Mathf.PI) * .8f;
        Vector3 hitOffset = hitDirection * shake;
        // Keep the nose within its close-fitting clearance box even during hit shake.
        hitOffset.z = Mathf.Clamp(hitOffset.z, -.012f, .012f);
        Quaternion bodyRotation = bodyState.Rotation * Quaternion.Euler(pitch, 0f,
            stunned ? Mathf.Sin(phase) * 3f : shake * 70f);
        // Lean around the belly, not around the ground origin: pitching must not push the nose through its box.
        Vector3 belly = Vector3.up * .44f;
        Vector3 pivotCompensation = bodyState.Rotation * belly - bodyRotation * belly;
        visualBody.localPosition = bodyState.Position + pivotCompensation +
            Vector3.up * (bob - charge * .075f - (stunned ? .055f : 0f)) + hitOffset;
        visualBody.localRotation = bodyRotation;

        for (int i = 0; i < legs.Length; i++)
        {
            if (!legs[i]) continue;
            float side = (i & 1) == 0 ? -1f : 1f;
            float gait = Mathf.Sin(phase + ((i == 0 || i == 3) ? 0f : Mathf.PI));
            // Short insect steps keep the front claws inside the actual 1.68 m body envelope.
            float stride = walkAmount * (rushing ? 10f : 12f);
            legs[i].localRotation = legStates[i].Rotation * Quaternion.Euler(
                gait * stride - charge * 8f, side * gait * stride * .3f, side * charge * 8f);
            legs[i].localPosition = legStates[i].Position + Vector3.up * Mathf.Max(0f, gait) * walkAmount * .025f;
            if (legMeshes[i] != null) legMeshes[i].Lift = Mathf.Max(0f, gait) * walkAmount * .025f;
        }
        for (int i = 0; i < shells.Length; i++)
        {
            if (!shells[i]) continue;
            float side = i == 0 ? -1f : 1f;
            shells[i].localPosition = shellStates[i].Position + new Vector3(side * charge * .024f, 0f, -charge * .035f);
            shells[i].localRotation = shellStates[i].Rotation * Quaternion.Euler(charge * 3f, 0f, side * charge * 3f);
        }
        GroundFeet();
        float pulse = stunned ? EnemyImpactVfx.StunPower(Time.time) : 1f + .12f * Mathf.Sin(phase);
        // Do not mix cyan into the warning: the first readable cue is already orange, then grows brighter.
        bool windup = charge > .001f && !rushing;
        ApplyCore(windup ? ChargedCore : IdleCore,
            (windup ? 1.5f + charge * .65f : 1.7f) * pulse);
        // Local hit shader owns the contact patch. Do not flash both entire shell panels.
    }

    private void AnimateDeath(float dt)
    {
        if (deathPoseComplete) return;
        deathTime += dt;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(deathTime / .55f));
        if (!deathFitReady) FitDeadBody();
        // The chassis settles onto the actual slope. The root and its gameplay box never move or grow.
        Quaternion rotation = Quaternion.Slerp(deathStartRotation, deathEndRotation, t);
        Vector3 belly = Vector3.up * .44f;
        Vector3 startPivot = bodyState.Rotation * belly - deathStartRotation * belly;
        Vector3 endPivot = bodyState.Rotation * belly - deathEndRotation * belly;
        Vector3 currentPivot = bodyState.Rotation * belly - rotation * belly;
        visualBody.localPosition = Vector3.Lerp(deathStartPosition - startPivot, deathEndPosition - endPivot, t) + currentPivot;
        visualBody.localRotation = rotation;
        for (int i = 0; i < legs.Length; i++)
        {
            if (!legs[i]) continue;
            float side = (i & 1) == 0 ? -1f : 1f;
            legs[i].localPosition = legStates[i].Position;
            // Fold inwards rather than spreading outside the physical X/Z silhouette.
            legs[i].localRotation = legStates[i].Rotation * Quaternion.Euler(i < 2 ? -12f * t : 12f * t, 0f, -side * 35f * t);
            legs[i].localScale = legStates[i].Scale * Mathf.Lerp(1f, .65f, t);
            if (legMeshes[i] != null) legMeshes[i].Lift = 0f;
        }
        for (int i = 0; i < shells.Length; i++)
        {
            if (!shells[i]) continue;
            float side = i == 0 ? -1f : 1f;
            shells[i].localRotation = shellStates[i].Rotation * Quaternion.Euler(0f, side * 3f * t, side * 5f * t);
        }
        GroundFeet();
        float extinction = Mathf.Clamp01(deathTime / .12f);
        ApplyCore(Color.Lerp(deathCoreColor, new Color(.025f, .05f, .055f), extinction), deathCoreGlow * (1f - extinction));
        FlashShell(0f);
        // A settled corpse has no moving feet. Reuse its final buffers without casts or mesh uploads until reset.
        deathPoseComplete = t >= 1f;
    }

    private void GroundFeet()
    {
        // Only the cosmetic boot fit/mesh uploads are throttled. The AI's sweep,
        // ground validation, collider and blue attack rails still run normally.
        if (!dead && !EnemyPresentationBudget.Details(transform.position))
        {
            if (Time.time < nextDistantFeet) return;
            nextDistantFeet = Time.time + .12f;
        }
        else nextDistantFeet = 0f;
        bool refresh = groundRefreshTime <= 0f || dead;
        if (refresh) groundRefreshTime = speed > .1f ? .03f : .08f;
        for (int i = 0; i < legs.Length; i++)
        {
            LegMesh part = legMeshes[i];
            if (part == null || !legs[i]) continue;
            Matrix4x4 pose = legs[i].localToWorldMatrix;
            bool supported = false;
            if (refresh)
            {
                for (int probe = 0; probe < part.Probes.Length; probe++)
                    part.Ground[probe] = ProbeGround(pose.MultiplyPoint3x4(part.Probes[probe]));
            }
            for (int probe = 0; probe < part.Ground.Length; probe++) supported |= part.Ground[probe].Valid;
            float offset = float.NegativeInfinity;
            for (int vertex = 0; vertex < part.Bind.Length; vertex++)
            {
                float weight = part.Weights[vertex];
                if (weight <= .0001f) continue;
                Vector3 world = pose.MultiplyPoint3x4(part.Bind[vertex]);
                float groundY = transform.position.y;
                if (supported)
                {
                    groundY = float.NegativeInfinity;
                    for (int probe = 0; probe < part.Ground.Length; probe++)
                        if (part.Ground[probe].Valid) groundY = Mathf.Max(groundY, part.Ground[probe].Height(world));
                }
                float skin = GroundSkin + (dead ? 0f : part.Lift);
                offset = Mathf.Max(offset, (groundY + skin - world.y) / weight);
            }
            // A tucked dead foot may remain above the floor; only lift it to prevent clipping.
            if (dead) offset = Mathf.Max(0f, offset);
            offset = Mathf.Clamp(offset, -.8f, .8f);
            Vector3 localUp = legs[i].worldToLocalMatrix.MultiplyVector(Vector3.up);
            for (int vertex = 0; vertex < part.Bind.Length; vertex++)
                part.Vertices[vertex] = part.Bind[vertex] + localUp * (offset * part.Weights[vertex]);
            UpdateLegMesh(part);
        }
    }

    private GroundPlane ProbeGround(Vector3 world)
    {
        // Five tiny casts per foot, fixed reusable storage; this only alters cosmetic meshes, not navigation.
        int count = Physics.RaycastNonAlloc(world + Vector3.up * 1.5f, Vector3.down,
            groundHits, 4.5f, ~0, QueryTriggerInteraction.Ignore);
        GroundPlane best = default;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            Collider surface = hit.collider;
            if (!surface || surface.gameObject.scene != gameObject.scene || surface.transform.IsChildOf(transform) ||
                surface.GetComponentInParent<Health>() || surface.GetComponentInParent<WaterVolume>() ||
                (surface.attachedRigidbody && !surface.attachedRigidbody.isKinematic) ||
                hit.normal.y < .82f || hit.distance >= nearest) continue;
            nearest = hit.distance;
            best = new GroundPlane { Valid = true, Point = hit.point, Normal = hit.normal };
        }
        return best;
    }

    private static void UpdateLegMesh(LegMesh part)
    {
        // Flat authored faces have duplicated triangle vertices, so cached buffers avoid RecalculateNormals allocations.
        for (int i = 0; i < part.Triangles.Length; i += 3)
        {
            int a = part.Triangles[i], b = part.Triangles[i + 1], c = part.Triangles[i + 2];
            Vector3 normal = Vector3.Cross(part.Vertices[b] - part.Vertices[a], part.Vertices[c] - part.Vertices[a]).normalized;
            part.Normals[a] = part.Normals[b] = part.Normals[c] = normal;
        }
        part.Runtime.vertices = part.Vertices;
        part.Runtime.normals = part.Normals;
        part.Runtime.RecalculateBounds();
    }

    private void FitDeadBody()
    {
        Vector3 previousPosition = visualBody.localPosition;
        Quaternion previousRotation = visualBody.localRotation;
        visualBody.localPosition = bodyState.Position;
        visualBody.localRotation = bodyState.Rotation;
        Vector3 centre = chassisFilter && chassisFilter.sharedMesh
            ? chassisFilter.transform.TransformPoint(chassisFilter.sharedMesh.bounds.center)
            : visualBody.TransformPoint(Vector3.up * .44f);
        GroundPlane support = ProbeGround(centre);
        deathEndRotation = bodyState.Rotation;
        if (support.Valid)
        {
            Vector3 localNormal = visualBody.parent
                ? visualBody.parent.InverseTransformDirection(support.Normal) : support.Normal;
            Quaternion slope = Quaternion.FromToRotation(bodyState.Rotation * Vector3.up, localNormal) * bodyState.Rotation;
            // Tilt the corpse onto its supporting slope, around the belly rather than the ground origin.
            // A 22 degree cap and inward-folded legs retain the original checked X/Z physical silhouette.
            deathEndRotation = Quaternion.RotateTowards(bodyState.Rotation, slope, 22f);
        }
        Vector3 belly = Vector3.up * .44f;
        Vector3 pivot = bodyState.Rotation * belly - deathEndRotation * belly;
        visualBody.localPosition = bodyState.Position + pivot;
        visualBody.localRotation = deathEndRotation;
        float drop = float.PositiveInfinity;
        if (chassisFilter && chassisContactVertices != null)
        {
            Matrix4x4 pose = chassisFilter.transform.localToWorldMatrix;
            for (int i = 0; i < chassisContactVertices.Length; i++)
            {
                Vector3 world = pose.MultiplyPoint3x4(chassisContactVertices[i]);
                GroundPlane ground = ProbeGround(world);
                if (ground.Valid) drop = Mathf.Min(drop, world.y - ground.Height(world) - GroundSkin);
            }
        }
        // Empty preview scenes still get the original visible collapse; never move the gameplay root.
        if (float.IsPositiveInfinity(drop)) drop = .21f;
        Vector3 parentDown = visualBody.parent
            ? visualBody.parent.worldToLocalMatrix.MultiplyVector(Vector3.down) : Vector3.down;
        deathEndPosition = bodyState.Position + pivot + parentDown * Mathf.Clamp(drop, -.1f, 1.2f);
        visualBody.localPosition = previousPosition;
        visualBody.localRotation = previousRotation;
        deathFitReady = true;
    }

#if UNITY_EDITOR
    public void SamplePoseForChecks(float dt) { TickVisual(Mathf.Max(0f, dt)); }

    public void RefreshGroundingForChecks()
    {
        Cache();
        groundRefreshTime = 0f;
        if (dead) deathFitReady = deathPoseComplete = false;
        GroundFeet();
    }
#endif

    private void ApplyCore(Color color, float glow)
    {
        if (coreRenderers == null) return;
        for (int i = 0; i < coreRenderers.Length; i++)
        {
            Renderer target = coreRenderers[i];
            if (!target) continue;
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            block.SetColor(EmissionId, color * glow);
            target.SetPropertyBlock(block);
        }
    }

    private void FlashShell(float amount)
    {
        for (int i = 0; i < shellRenderers.Length; i++)
        {
            Renderer target = shellRenderers[i];
            if (!target) continue;
            // Only the ivory submaterial flashes; moss and graphite remain legible.
            target.GetPropertyBlock(block, 0);
            Color tint = Color.Lerp(shellColors[i], new Color(1f, .77f, .54f), amount);
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            target.SetPropertyBlock(block, 0);
        }
    }

    private void Spark(Vector3 worldPoint, bool wall)
    {
        if (!impactLine) return;
        impactTime = wall ? .19f : .12f;
        impactLine.transform.position = worldPoint;
        impactLine.transform.rotation = Quaternion.identity;
        impactLine.startColor = impactLine.endColor = wall ? new Color(1f, .65f, .12f) : new Color(.25f, .95f, 1f);
        impactLine.enabled = true;
    }

    private void AnimateImpact()
    {
        if (!impactLine) return;
        if (stunned && impactTime <= 0f && !dead)
        {
            // Reuse the impact line for a tiny orbiting stun glyph; no lights or spawned stars.
            Vector3 centre = transform.position + Vector3.up * (1.05f * Mathf.Abs(transform.lossyScale.y)) +
                new Vector3(Mathf.Cos(phase * 2f), 0f, Mathf.Sin(phase * 2f)) * .14f;
            impactLine.positionCount = 5;
            impactLine.SetPosition(0, centre + Vector3.left * .045f);
            impactLine.SetPosition(1, centre + Vector3.up * .045f);
            impactLine.SetPosition(2, centre + Vector3.right * .045f);
            impactLine.SetPosition(3, centre + Vector3.down * .045f);
            impactLine.SetPosition(4, centre + Vector3.left * .045f);
            impactLine.startColor = impactLine.endColor = wallStunned ? new Color(1f, .7f, .16f) : new Color(.25f, .9f, 1f);
            impactLine.widthMultiplier = wallStunned ? .018f : .012f; impactLine.enabled = true;
            return;
        }
        if (impactTime <= 0f) { impactLine.enabled = false; return; }
        // One reusable six-point diamond burst, no per-hit objects, textures or particle systems.
        float radius = .035f + (.2f - impactTime) * .8f;
        Vector3 origin = impactLine.transform.position;
        impactLine.positionCount = 6;
        impactLine.SetPosition(0, origin + Vector3.left * radius);
        impactLine.SetPosition(1, origin + Vector3.up * radius);
        impactLine.SetPosition(2, origin + Vector3.right * radius);
        impactLine.SetPosition(3, origin + Vector3.down * radius);
        impactLine.SetPosition(4, origin + Vector3.left * radius);
        impactLine.SetPosition(5, origin + Vector3.forward * radius);
        impactLine.widthMultiplier = .018f * Mathf.Clamp01(impactTime / .12f);
    }

    private void PlayImpactAudio(float volume, float pitch)
    {
        if (!audioSource || !impactClip) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(impactClip, volume);
    }
}
