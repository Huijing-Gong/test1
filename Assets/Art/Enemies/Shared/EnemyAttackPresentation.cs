using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Presentation only. All timings and contact locations come from the existing AI.
// Eight reusable cues per actor; two frozen-pose mesh buffers only for the charger.
[DisallowMultipleComponent, DefaultExecutionOrder(180)]
public sealed class EnemyAttackPresentation : MonoBehaviour
{
    const int CueLimit = 8;
    static Material energy, ghostMaterial;
    static Mesh quad;
    static int owners;
    static readonly Color Mint = new Color(.18f,.85f,.76f,1);
    static readonly Color Amber = new Color(1,.58f,.09f,1);
    static readonly Color Dust = new Color(.82f,.80f,.63f,.82f);
    sealed class Cue
    {
        public MeshRenderer renderer;
        public Vector3 position, velocity;
        public float born, duration, size;
        public int mode;
        public Color color;
    }
    sealed class Ghost
    {
        public Mesh mesh;
        public MeshRenderer renderer;
        public float born = -10;
    }
    readonly Cue[] cues = new Cue[CueLimit];
    MaterialPropertyBlock block;
    readonly List<LineRenderer> rails = new List<LineRenderer>(3);
    readonly List<MeshRenderer> cores = new List<MeshRenderer>();
    readonly List<MeshFilter> ghostSources = new List<MeshFilter>();
    readonly List<int> ghostSubmeshes = new List<int>();
    Ghost[] ghosts;
    CombineInstance[] combines;
    Health health;
    PlayerVitals player;
    BaselineFlow flow;
    DomainPrototypeEnemy domain;
    RailBeetleAI beetle;
    RailBeetleVisual beetleVisual;
    RepairWispAI repair;
    RepairWispVisual repairVisual;
    GelBlobAI gel;
    StoneScoutVisualMotion scout;
    LineRenderer attackLine;
    TrailRenderer flightTrail;
    Transform muzzle;
    GameObject holder;
    Vector3 previousPosition, lastGhostPosition;
    float nextPlayerSearch, nextGhost, nextDust, lastCharge;
    int lastShots, lastLandings, ghostCursor, repairs;
    bool wasCharging, wasFlight, wasRepair, ready, owns;
    public int CueEvents { get; private set; }
    public int VisibleCueCount { get { int n=0; foreach(var c in cues) if(c != null && c.renderer && c.renderer.enabled) n++; return n; } }
    public int ActiveGhostCount { get { int n=0; if(ghosts != null) foreach(var g in ghosts) if(g.renderer && g.renderer.enabled) n++; return n; } }
    public bool ImpactVisible { get { foreach(var c in cues) if(c!=null && c.renderer && c.renderer.enabled && c.mode==1) return true; return false; } }
    public static Material EnergyMaterial
    {
        get
        {
            if (!energy)
            {
                var shader=Resources.Load<Shader>("EnemyAttackEnergy");
                if (shader) energy=new Material(shader) { name="Shared enemy attack energy", hideFlags=HideFlags.DontSave };
            }
            return energy;
        }
    }
    public static void Ensure(GameObject actor)
    {
        if (Application.isPlaying && actor && !actor.GetComponent<EnemyAttackPresentation>())
            actor.AddComponent<EnemyAttackPresentation>();
    }
    void Awake()
    {
        block=new MaterialPropertyBlock();
        health=GetComponent<Health>();
        domain=GetComponent<DomainPrototypeEnemy>(); beetle=GetComponent<RailBeetleAI>();
        beetleVisual=GetComponentInChildren<RailBeetleVisual>(); repair=GetComponent<RepairWispAI>();
        repairVisual=GetComponentInChildren<RepairWispVisual>(); gel=GetComponent<GelBlobAI>();
        scout=GetComponentInChildren<StoneScoutVisualMotion>();
        owners++; owns=true;
    }
    void OnEnable()
    {
        if (health) { health.Died+=Died; health.HealthChanged+=HealthChanged; }
        foreach(var candidate in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
            if(candidate.gameObject.scene==gameObject.scene) { flow=candidate; break; }
        if(flow) flow.WorldEncountersReset+=Clear;
    }
    void Start() => Initialize();
    void Initialize()
    {
        if(ready || !EnergyMaterial) return;
        ready=true;
        // A scene-local world-space holder keeps snapshots and impacts still as the actor moves.
        holder=new GameObject(name+" attack cues (bounded)");
        SceneManager.MoveGameObjectToScene(holder,gameObject.scene);
        if(!quad)
        {
            quad=new Mesh { name="Shared attack cue quad", hideFlags=HideFlags.DontSave };
            quad.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f),new Vector3(.5f,-.5f)};
            quad.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};
            quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            quad.triangles=new[]{0,1,2,0,2,3}; quad.RecalculateNormals(); quad.RecalculateBounds();
        }
        for(int i=0;i<CueLimit;i++) cues[i]=new Cue { renderer=Renderer("Reusable attack cue "+i,quad,energy) };
        foreach(var line in GetComponentsInChildren<LineRenderer>(true))
        {
            if(domain && domain.EnemyKind==DomainPrototypeEnemy.Kind.RuinPrism && line.positionCount>=32) attackLine=line;
            if(repair && line.name.Contains("energy")) attackLine=line;
            if(beetle && line.name.Contains("charge rail")) rails.Add(line);
        }
        if(repair) attackLine=repair.RepairLine;
        if(domain) { attackLine=domain.EnemyKind==DomainPrototypeEnemy.Kind.RuinPrism ? domain.AttackLine : null; muzzle=domain.AttackMuzzle; }
        if(attackLine) attackLine.sharedMaterial=energy;
        foreach(var renderer in GetComponentsInChildren<MeshRenderer>(true))
            if(renderer.sharedMaterial && renderer.sharedMaterial.shader.name=="Enemies/ReadableCore") cores.Add(renderer);
        if(beetle) BuildGhosts();
        if(gel)
        {
            var go=new GameObject("Short gel leap ribbon"); go.transform.SetParent(holder.transform,false);
            flightTrail=go.AddComponent<TrailRenderer>(); flightTrail.sharedMaterial=energy;
            flightTrail.time=.12f; flightTrail.minVertexDistance=.06f;
            flightTrail.startWidth=.16f; flightTrail.endWidth=0;
            flightTrail.startColor=new Color(.2f,.9f,.75f,.75f); flightTrail.endColor=new Color(.2f,.9f,.75f,0);
            flightTrail.shadowCastingMode=ShadowCastingMode.Off; flightTrail.receiveShadows=false;
            flightTrail.emitting=false; flightTrail.enabled=false;
        }
        previousPosition=transform.position; SyncCounters();
    }
    MeshRenderer Renderer(string label,Mesh mesh,Material material)
    {
        var go=new GameObject(label,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(holder.transform,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.GetComponent<MeshRenderer>(); r.sharedMaterial=material; r.enabled=false;
        r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false;
        r.lightProbeUsage=LightProbeUsage.Off; r.reflectionProbeUsage=ReflectionProbeUsage.Off;
        go.layer=gameObject.layer; return r;
    }
    void BuildGhosts()
    {
        if(!ghostMaterial)
        {
            var shader=Resources.Load<Shader>("EnemyAttackAfterimage");
            if(shader) ghostMaterial=new Material(shader) { name="Shared mint attack afterimage", hideFlags=HideFlags.DontSave };
        }
        if(!ghostMaterial) return;
        foreach(var f in GetComponentsInChildren<MeshFilter>(true))
        {
            var r=f.GetComponent<MeshRenderer>();
            if(!f.sharedMesh || !f.sharedMesh.isReadable || !r || !r.enabled || !r.sharedMaterial ||
                r.sharedMaterial.renderQueue>=2500 || f.name.Contains("patch")) continue;
            for(int sub=0;sub<f.sharedMesh.subMeshCount;sub++) { ghostSources.Add(f); ghostSubmeshes.Add(sub); }
        }
        combines=new CombineInstance[ghostSources.Count];
        ghosts=new Ghost[2];
        for(int i=0;i<ghosts.Length;i++)
        {
            var mesh=new Mesh { name="Reusable charge pose "+i, hideFlags=HideFlags.DontSave };
            mesh.MarkDynamic();
            ghosts[i]=new Ghost { mesh=mesh, renderer=Renderer("Frozen charge afterimage "+i,mesh,ghostMaterial) };
        }
    }
    void Snapshot()
    {
        if(ghosts==null || combines.Length==0) return;
        var g=ghosts[ghostCursor++%ghosts.Length];
        Vector3 origin=transform.position;
        var inverse=Matrix4x4.Translate(-origin);
        for(int i=0;i<combines.Length;i++)
        {
            var f=ghostSources[i];
            combines[i]=new CombineInstance { mesh=f.sharedMesh, subMeshIndex=ghostSubmeshes[i], transform=inverse*f.transform.localToWorldMatrix };
        }
        g.mesh.Clear(); g.mesh.CombineMeshes(combines,true,true,false);
        g.renderer.transform.SetPositionAndRotation(origin,Quaternion.identity);
        g.born=Time.time; g.renderer.enabled=true;
    }
    void LateUpdate()
    {
        if(!ready) Initialize();
        if(!ready) return;
        if(!player && Time.unscaledTime>=nextPlayerSearch)
        {
            nextPlayerSearch=Time.unscaledTime+1;
            foreach(var p in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
                if(p.gameObject.scene==gameObject.scene) { player=p; break; }
        }
        bool suspended=health && health.IsDead || player && !player.EnemyCombatActive ||
            domain && !domain.isActiveAndEnabled || beetle && !beetle.isActiveAndEnabled ||
            repair && !repair.isActiveAndEnabled || gel && !gel.isActiveAndEnabled;
        if(suspended) { Clear(); return; }
        bool details=EnemyPresentationBudget.Details(transform.position);
        if(domain) UpdateDomain(details);
        if(beetle) UpdateBeetle(details);
        if(repair) UpdateRepair(details);
        if(gel) UpdateGel(details);
        UpdateCoreCharge();
        UpdateCues(details);
        if(ghosts!=null) foreach(var g in ghosts)
        {
            float age=Time.time-g.born;
            g.renderer.enabled=details && age<.32f &&
                (g.renderer.transform.position-transform.position).sqrMagnitude>.09f;
            if(g.renderer.enabled)
            {
                block.Clear(); block.SetColor("_Tint",Mint);
                block.SetFloat("_Fade",.85f*(1-Mathf.SmoothStep(0,1,age/.32f)));
                block.SetFloat("_Clock",g.born); g.renderer.SetPropertyBlock(block);
            }
        }
        previousPosition=transform.position;
    }
    void UpdateDomain(bool details)
    {
        if(domain.EnemyKind==DomainPrototypeEnemy.Kind.LeafWisp)
        {
            if(domain.RangedShots>lastShots && details)
                Emit(muzzle ? muzzle.position : transform.position+Vector3.up,Vector3.zero,.32f,.14f,1,Mint);
            lastShots=domain.RangedShots;
            ChargeMotes(domain.RangedCharge,muzzle ? muzzle.position : transform.position+Vector3.up,details,Mint);
        }
        else
        {
            float charge=domain.AttackCharge;
            ChargeMotes(charge,transform.position+Vector3.up*.85f,details,domain.AttackTint);
            if(attackLine)
            {
                attackLine.sharedMaterial=energy;
                if(attackLine.enabled)
                {
                    attackLine.widthMultiplier=domain.PulseActive ? Mathf.Lerp(.18f,.075f,domain.PulseProgress) : .055f;
                    // Preserve AI-authored terrain/cover vertices and palette.
                    if(domain.PulseActive)
                    {
                        Color c=domain.AttackTint; c.a=Mathf.Lerp(1,.45f,domain.PulseProgress);
                        attackLine.startColor=attackLine.endColor=c;
                    }
                    SetLine(attackLine,0,1);
                }
            }
        }
    }
    void UpdateCoreCharge()
    {
        float charge=domain ? domain.AttackCharge : beetleVisual ? beetleVisual.ChargeProgress :
            repair && repair.CurrentPhase==RepairWispAI.Phase.Prepare ? repair.PhaseProgress : 0;
        foreach(var core in cores)
        {
            if(!core) continue;
            core.GetPropertyBlock(block);
            block.SetFloat("_AttackCharge",charge);
            block.SetFloat("_AttackClock",Time.time);
            core.SetPropertyBlock(block);
        }
    }
    void ChargeMotes(float charge,Vector3 centre,bool details,Color color)
    {
        if(details && charge>lastCharge && (int)(charge*4)>(int)(lastCharge*4))
        {
            float a=charge*Mathf.PI*5;
            Vector3 offset=new Vector3(Mathf.Cos(a),.3f,Mathf.Sin(a))*.32f;
            Emit(centre+offset,-offset*3,.10f,.22f,2,color);
        }
        lastCharge=charge;
    }
    void UpdateBeetle(bool details)
    {
        bool charging=beetle.IsCharging;
        float charge=beetleVisual ? beetleVisual.ChargeProgress : 0;
        ChargeMotes(charge,transform.position+Vector3.up*.5f,details,Amber);
        if(charging && details)
        {
            if(!wasCharging) { Snapshot(); lastGhostPosition=transform.position; nextGhost=Time.time+.07f; nextDust=Time.time; }
            if(Time.deltaTime>0 && Time.time>=nextGhost && (transform.position-lastGhostPosition).sqrMagnitude>=.42f)
            { Snapshot(); lastGhostPosition=transform.position; nextGhost=Time.time+.07f; }
            if(Time.deltaTime>0 && Time.time>=nextDust)
            { FootDust(transform.position-transform.forward*.35f,2); nextDust=Time.time+.11f; }
        }
        if(wasCharging && !charging && details && (beetle.CurrentState==RailBeetleAI.BehaviourState.Recover || beetle.CurrentState==RailBeetleAI.BehaviourState.WallStun))
        {
            FootDust(transform.position,3);
            if(beetle.CurrentState==RailBeetleAI.BehaviourState.WallStun)
                Emit(transform.position+transform.forward*.55f+Vector3.up*.3f,Vector3.zero,.48f,.18f,1,Amber);
        }
        foreach(var rail in rails)
        {
            if(!rail) continue;
            rail.sharedMaterial=energy;
            if(rail.enabled) { rail.widthMultiplier=rail==rails[0] ? .105f : .055f; SetLine(rail,0,1); }
        }
        wasCharging=charging;
    }
    void UpdateRepair(bool details)
    {
        bool channel=repair.BeamVisible;
        if(attackLine)
        {
            attackLine.sharedMaterial=energy;
            if(channel) { attackLine.widthMultiplier=.075f; SetLine(attackLine,4,1); }
        }
        if(wasRepair && !channel && details && repair.SuccessfulRepairs==repairs)
        {
            Vector3 centre=repairVisual ? repairVisual.CorePosition : transform.position+Vector3.up*.7f;
            for(int i=0;i<3;i++) Emit(centre+Vector3.right*(i-1)*.12f,
                new Vector3((i-1)*.4f,.35f,0),.10f,.17f,2,new Color(.64f,.94f,.12f));
        }
        if(repair.SuccessfulRepairs>repairs && details && repair.RepairTarget)
            Emit(repair.RepairTarget.transform.position+Vector3.up*.85f,Vector3.up*.15f,.22f,.20f,2,new Color(.64f,.94f,.12f));
        ChargeMotes(repair.CurrentPhase==RepairWispAI.Phase.Prepare ? repair.PhaseProgress : 0,
            repairVisual ? repairVisual.CorePosition : transform.position+Vector3.up*.7f,details,new Color(.64f,.94f,.12f));
        wasRepair=channel; repairs=repair.SuccessfulRepairs;
    }
    void UpdateGel(bool details)
    {
        bool flying=gel.Phase==GelBlobAI.State.Flight && gel.WarningVisible;
        if(flightTrail)
        {
            flightTrail.transform.position=transform.position+Vector3.up*.25f;
            if(flying && !wasFlight) flightTrail.Clear();
            flightTrail.enabled=details && (flying || flightTrail.positionCount>0);
            flightTrail.emitting=details && flying;
        }
        if(gel.LandingCount>lastLandings && details)
        {
            // Compact wisps at the actual contact, never a second expanding damage disk.
            for(int i=0;i<4;i++)
            {
                float angle=i*Mathf.PI*.5f;
                Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Emit(transform.position+radial*.78f+Vector3.up*.18f,radial*.7f+Vector3.up*.35f,.34f,.28f,5,Mint);
            }
        }
        lastLandings=gel.LandingCount; wasFlight=flying;
    }
    public void PunchStart()
    {
        Initialize(); if(!ready || !EnemyPresentationBudget.Details(transform.position)) return;
        FootDust(transform.position-transform.forward*.16f,2);
    }
    public void Contact(Vector3 point,Color tint)
    {
        Initialize(); if(!ready || health && health.IsDead || !EnemyPresentationBudget.Details(point)) return;
        Emit(point,Vector3.zero,.46f,.18f,1,tint);
        for(int i=0;i<3;i++)
        {
            float a=i*2.39996f;
            Emit(point,new Vector3(Mathf.Cos(a),.8f,Mathf.Sin(a))*.85f,.075f,.22f,2,i==1?Amber:tint);
        }
    }
    void FootDust(Vector3 feet,int count)
    {
        for(int i=0;i<count;i++)
        {
            float side=i%2==0?-1:1;
            Emit(feet+transform.right*(side*.18f)+Vector3.up*.10f,
                -transform.forward*.55f+transform.right*side*.4f+Vector3.up*.35f,.25f+i*.035f,.32f,3,Dust);
        }
    }
    void Emit(Vector3 position,Vector3 velocity,float size,float duration,int mode,Color color)
    {
        foreach(var cue in cues)
        {
            if(cue==null || !cue.renderer || cue.renderer.enabled && Time.time-cue.born<cue.duration) continue;
            cue.position=position; cue.velocity=velocity; cue.size=size; cue.duration=duration;
            cue.mode=mode; cue.color=color; cue.born=Time.time; cue.renderer.enabled=true; CueEvents++; return;
        }
    }
    void UpdateCues(bool details)
    {
        Camera view=Camera.main;
        foreach(var c in cues)
        {
            if(c==null || !c.renderer || !c.renderer.enabled) continue;
            float age=Time.time-c.born, t=Mathf.Clamp01(age/c.duration);
            if(t>=1 || !details) { c.renderer.enabled=false; continue; }
            c.renderer.transform.position=c.position+c.velocity*age;
            if(view && c.mode!=3) c.renderer.transform.position+=(view.transform.position-c.renderer.transform.position).normalized*.025f;
            c.renderer.transform.rotation=view ? view.transform.rotation : Quaternion.identity;
            float size=c.size*(c.mode==3 ? 1+t*.7f : 1-t*.30f);
            c.renderer.transform.localScale=Vector3.one*size;
            block.Clear(); block.SetColor("_Tint",c.color); block.SetFloat("_Mode",c.mode);
            block.SetFloat("_Fade",1-Mathf.SmoothStep(0,1,t)); block.SetFloat("_Progress",t);
            block.SetFloat("_Clock",Time.time); c.renderer.SetPropertyBlock(block);
        }
    }
    void SetLine(LineRenderer line,float mode,float fade)
    {
        line.GetPropertyBlock(block);
        block.SetFloat("_Mode",mode); block.SetFloat("_Fade",fade);
        block.SetFloat("_Clock",Time.time); block.SetColor("_Tint",Color.white);
        block.SetFloat("_Repeat",12); line.SetPropertyBlock(block);
    }
    void SyncCounters()
    {
        lastShots=domain?domain.RangedShots:0; lastLandings=gel?gel.LandingCount:0;
        repairs=repair?repair.SuccessfulRepairs:0;
    }
    void Died(Health actor,DamageInfo info)=>Clear();
    void HealthChanged(Health actor,int before,int after) { if(before==0 && after>0) Clear(); }
    public void Clear()
    {
        if(block!=null) foreach(var core in cores)
        {
            if(!core) continue;
            core.GetPropertyBlock(block); block.SetFloat("_AttackCharge",0); core.SetPropertyBlock(block);
        }
        foreach(var c in cues) if(c!=null && c.renderer) c.renderer.enabled=false;
        if(ghosts!=null) foreach(var g in ghosts) { g.born=-10; if(g.renderer) g.renderer.enabled=false; }
        if(flightTrail) { flightTrail.emitting=false; flightTrail.Clear(); flightTrail.enabled=false; }
        wasCharging=wasFlight=wasRepair=false; lastCharge=0;
        SyncCounters();
    }
    void OnDisable()
    {
        if(health) { health.Died-=Died; health.HealthChanged-=HealthChanged; }
        if(flow) flow.WorldEncountersReset-=Clear;
        Clear();
    }
    void OnDestroy()
    {
        if(holder) Destroy(holder);
        if(ghosts!=null) foreach(var g in ghosts) if(g.mesh) Destroy(g.mesh);
        if(owns && --owners==0)
        {
            if(energy) Destroy(energy); if(ghostMaterial) Destroy(ghostMaterial); if(quad) Destroy(quad);
            energy=ghostMaterial=null; quad=null;
        }
    }
}

