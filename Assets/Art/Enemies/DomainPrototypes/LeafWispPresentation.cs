using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Leaf Wisp only. Small cached vertex-colour cues; no light, texture, collider
// or per-shot GameObject. Existing generic impact owns the ceramic particles.
[DisallowMultipleComponent, DefaultExecutionOrder(130)]
public sealed class LeafWispPresentation : MonoBehaviour
{
    struct Panel { public MeshRenderer renderer; public Vector3[] vertices; public bool ceramic; }
    static readonly Dictionary<Material, Material> shellMaterials = new Dictionary<Material, Material>();
    static Mesh star, chargeBrackets, lockedBrackets;
    static int owners;
    readonly List<Panel> panels = new List<Panel>();
    MaterialPropertyBlock block;
    DomainPrototypeEnemy ai;
    Health health;
    PlayerVitals player;
    Health playerHealth;
    Transform muzzle, contactFrame;
    MeshRenderer hit, shot, aim;
    MeshFilter aimMesh;
    Vector3 localContact;
    float hitAt = -10, shotAt = -10, lockedAt = -10;
    float lastCharge=-1;
    bool lastAlive;
    bool configured;
    public bool HitVisible => hit && hit.enabled;
    public bool MuzzleVisible => shot && shot.enabled;
    public bool AimWarningVisible => aim && aim.enabled;
    public bool LockedWarningVisible => AimWarningVisible && ai && ai.RangedLocked;

    void Awake() { ai = GetComponent<DomainPrototypeEnemy>(); health = GetComponent<Health>(); block = new MaterialPropertyBlock(); }
    void OnEnable() { health.Damaged += OnHit; health.Died += OnDeath; }
    void OnDisable() { if (health) { health.Damaged -= OnHit; health.Died -= OnDeath; } Clear(); }
    public void Configure(Transform visual, Transform barrel)
    {
        if (configured || !visual) return;
        if (block == null) block = new MaterialPropertyBlock();
        configured = true; owners++; muzzle = barrel;
        Shader ceramic = Resources.Load<Shader>("LeafWispSurface");
        foreach (MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>())
        {
            Material original = renderer.sharedMaterial;
            var filter = renderer.GetComponent<MeshFilter>();
            if (!original || original.renderQueue >= 2500 || !filter || !filter.sharedMesh) continue;
            bool surface = ceramic && (original.name.Contains("IvoryCeramic") || original.name.Contains("Graphite"));
            panels.Add(new Panel { renderer = renderer, vertices = filter.sharedMesh.vertices, ceramic = surface });
            if (!surface) continue;
            if (!shellMaterials.TryGetValue(original, out Material material) || !material)
            {
                material = new Material(ceramic) { name = original.name + " Leaf Wisp surface", hideFlags = HideFlags.DontSave };
                material.CopyPropertiesFromMaterial(original); shellMaterials[original] = material;
            }
            renderer.sharedMaterial = material;
        }
        if (!star) star = BuildStar();
        if (!chargeBrackets) chargeBrackets = BuildBrackets(new Color(.12f,.84f,1f), false);
        if (!lockedBrackets) lockedBrackets = BuildBrackets(new Color(1f,.67f,.16f), true);
        hit = Cue("Reusable ceramic sword impact");
        shot = Cue("Reusable compact dart muzzle flash");
        aim = Cue("Reusable compact charge brackets");
        aimMesh = aim.GetComponent<MeshFilter>(); aimMesh.sharedMesh = chargeBrackets;
        foreach (PlayerVitals candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
        playerHealth = player ? player.GetComponent<Health>() : null;
    }
    MeshRenderer Cue(string name)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = star;
        var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = EnemyImpactVfx.SharedEnergyMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.enabled = false; return renderer;
    }
    static Mesh BuildStar()
    {
        // Eight alternating rays, 24 triangles. Pale centre, blue interior, dark edge.
        var vertices = new Vector3[17]; var colours = new Color[17]; var normals = new Vector3[17];
        var indices = new int[72]; colours[0] = new Color(1,.98f,.86f,1);
        for (int i=0; i<8; i++)
        {
            float a=i*Mathf.PI/4+.15f; float length=i%2==0 ? 1 : .38f;
            vertices[i+1]=new Vector3(Mathf.Cos(a),Mathf.Sin(a))*length*.77f;
            vertices[i+9]=vertices[i+1]/.77f;
            colours[i+1]=new Color(.08f,.83f,1,1); colours[i+9]=new Color(.015f,.055f,.08f,1);
            int n=(i+1)%8;
            indices[i*9]=0; indices[i*9+1]=n+1; indices[i*9+2]=i+1;
            indices[i*9+3]=i+1; indices[i*9+4]=n+9; indices[i*9+5]=i+9;
            indices[i*9+6]=i+1; indices[i*9+7]=n+1; indices[i*9+8]=n+9;
        }
        for (int i=0;i<normals.Length;i++) normals[i]=Vector3.back;
        var mesh=new Mesh { name="Shared Leaf Wisp flash (24 triangles)", hideFlags=HideFlags.DontSave };
        mesh.vertices=vertices; mesh.colors=colours; mesh.normals=normals; mesh.triangles=indices; mesh.RecalculateBounds(); return mesh;
    }
    static Mesh BuildBrackets(Color colour, bool locked)
    {
        // Four disconnected corners, open centre: a projectile weapon's iris,
        // not a blast-radius ring or a beam that looks like an active attack.
        var vertices=new Vector3[64]; var colours=new Color[64]; var normals=new Vector3[64];
        var triangles=new int[96];
        for(int corner=0;corner<4;corner++)
        {
            float angle=corner*Mathf.PI*.5f + (locked ? Mathf.PI*.25f : 0f);
            for(int arm=0;arm<2;arm++)
            for(int band=0;band<2;band++)
            {
                float minLong=band==0 ? .47f : .52f, maxLong=band==0 ? 1.02f : .97f;
                float minShort=band==0 ? .80f : .855f, maxShort=band==0 ? 1.02f : .965f;
                Vector2 lo=arm==0 ? new Vector2(minLong,minShort) : new Vector2(minShort,minLong);
                Vector2 hi=arm==0 ? new Vector2(maxLong,maxShort) : new Vector2(maxShort,maxLong);
                int quad=(corner*4+arm*2+band), v=quad*4, t=quad*6;
                for(int point=0;point<4;point++)
                {
                    float x=point<2 ? lo.x : hi.x, y=point==0 || point==3 ? lo.y : hi.y;
                    vertices[v+point]=new Vector3(x*Mathf.Cos(angle)-y*Mathf.Sin(angle),x*Mathf.Sin(angle)+y*Mathf.Cos(angle),band==0 ? 0f : -.001f);
                    colours[v+point]=band==0 ? new Color(.012f,.04f,.055f,1f) : colour;
                    normals[v+point]=Vector3.back;
                }
                triangles[t]=v; triangles[t+1]=v+1; triangles[t+2]=v+2;
                triangles[t+3]=v; triangles[t+4]=v+2; triangles[t+5]=v+3;
            }
        }
        var mesh=new Mesh { name=locked ? "Shared amber locked iris (32 triangles)" : "Shared cyan charging iris (32 triangles)", hideFlags=HideFlags.DontSave };
        mesh.vertices=vertices; mesh.colors=colours; mesh.normals=normals; mesh.triangles=triangles; mesh.RecalculateBounds(); return mesh;
    }
    void OnHit(Health target, DamageInfo damage)
    {
        if (target.IsDead || !configured) return;
        float best=float.PositiveInfinity; Vector3 point=damage.HitPoint;
        // Sword reports the capsule contact. Anchor to the nearest actual visible
        // mesh vertex, not inside the capsule or in empty space beside the shell.
        foreach (Panel panel in panels)
        {
            if (!panel.renderer || !panel.renderer.enabled || !panel.renderer.gameObject.activeInHierarchy) continue;
            if ((panel.renderer.bounds.ClosestPoint(damage.HitPoint)-damage.HitPoint).sqrMagnitude>best) continue;
            foreach (Vector3 vertex in panel.vertices)
            {
                Vector3 world=panel.renderer.transform.TransformPoint(vertex);
                float distance=(world-damage.HitPoint).sqrMagnitude;
                if (distance>=best) continue;
                best=distance; point=world; contactFrame=panel.renderer.transform;
            }
        }
        if (!contactFrame) return;
        localContact=contactFrame.InverseTransformPoint(point);
        hitAt=Time.time;
        Refresh(); // Immediate on the genuine hit frame, including sword hit-stop.
    }
    void OnDeath(Health target, DamageInfo damage) => Clear();
    public void Fire() { shotAt=Time.time; Refresh(); }
    public void BeginCharge() { lockedAt=-10; Refresh(); }
    public void LockAim() { lockedAt=Time.time; Refresh(); }
    public void ClearAim() { lockedAt=-10; if(aim) aim.enabled=false; }
    void LateUpdate() => Refresh();
    void Refresh()
    {
        if (!configured) return;
        bool alive=health && !health.IsDead;
        bool active=!player || player.gameObject.scene==gameObject.scene && player.EnemyCombatActive && playerHealth && !playerHealth.IsDead;
        if (!active) { Clear(); return; }
        bool details=EnemyPresentationBudget.Details(transform.position);
        Camera camera=Camera.main;
        bool warning=alive && ai.isActiveAndEnabled && ai.RangedCharging && muzzle;
        if(aimMesh) aimMesh.sharedMesh=ai.RangedLocked ? lockedBrackets : chargeBrackets;
        // Warning is gameplay-critical: never use secondary-detail distance culling.
        // The open square converges, then snaps once to a stable amber diamond.
        float irisSize=ai.RangedLocked ? .27f+.025f*(1-Mathf.Clamp01((Time.time-lockedAt)/.08f)) : Mathf.Lerp(.43f,.30f,Mathf.SmoothStep(0,1,ai.RangedCharge));
        Show(aim,warning,muzzle ? muzzle.position-transform.forward*.10f : transform.position,irisSize,0,1,camera);
        Show(hit, alive && details && contactFrame && Time.time-hitAt<.16f,
            contactFrame ? contactFrame.TransformPoint(localContact) : transform.position, .29f, Time.time-hitAt, .16f, camera);
        Show(shot, alive && details && muzzle && Time.time-shotAt<.10f,
            muzzle ? muzzle.position : transform.position, .18f, Time.time-shotAt, .10f, camera);
        float charge=ai.RangedCharge;
        if (Mathf.Abs(lastCharge-charge)<.015f && lastAlive==alive) return;
        lastCharge=charge; lastAlive=alive;
        foreach (Panel panel in panels)
        {
            if (!panel.renderer || !panel.ceramic) continue;
            panel.renderer.GetPropertyBlock(block);
            block.SetFloat("_Charge",alive ? charge : 0);
            block.SetFloat("_Alive",alive ? 1 : 0);
            panel.renderer.SetPropertyBlock(block);
        }
    }
    void Show(MeshRenderer renderer, bool visible, Vector3 position, float size, float age, float duration, Camera camera)
    {
        if (!renderer) return;
        renderer.enabled=visible;
        if (!visible) return;
        if (camera)
        {
            renderer.transform.rotation=camera.transform.rotation;
            position+=(camera.transform.position-position).normalized*.018f;
        }
        renderer.transform.position=position;
        renderer.transform.localScale=Vector3.one*size;
        renderer.GetPropertyBlock(block); block.SetFloat("_ImpactRadius",0);
        block.SetFloat("_CueOpacity",1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(duration*.4f,duration,age)));
        renderer.SetPropertyBlock(block);
    }
    public void Clear()
    {
        hitAt=shotAt=-10; ClearAim();
        if (hit) hit.enabled=false; if (shot) shot.enabled=false;
        bool alive=health && !health.IsDead;
        if (block!=null && (lastCharge!=0 || lastAlive!=alive))
            foreach (Panel panel in panels)
            {
                if (!panel.renderer || !panel.ceramic) continue;
                panel.renderer.GetPropertyBlock(block); block.SetFloat("_Charge",0); block.SetFloat("_Alive",alive ? 1 : 0);
                panel.renderer.SetPropertyBlock(block);
            }
        lastCharge=0; lastAlive=alive;
    }
    void OnDestroy()
    {
        if (!configured || --owners!=0) return;
        foreach (Material material in shellMaterials.Values) if (material) Destroy(material);
        shellMaterials.Clear(); if (star) Destroy(star); if(chargeBrackets) Destroy(chargeBrackets); if(lockedBrackets) Destroy(lockedBrackets);
        star=chargeBrackets=lockedBrackets=null;
    }
}
