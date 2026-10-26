using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using SamProduction.BossArena;

// One visit owns these temporary materials and cameras. Never edits source assets,
// disables colliders, awards progression, or changes source scene assets.
public sealed class SummitArenaTransition : IDisposable
{
    public const float DepartureSeconds = 5.0f, ArrivalSeconds = 12.4f;
    readonly BaselineSamBridge owner;
    readonly List<Surface> surfaces = new List<Surface>();
    readonly List<MeadowSurface> meadows = new List<MeadowSurface>();
    readonly List<Mote> seeds = new List<Mote>();
    readonly List<Material> temporaryMaterials = new List<Material>();
    readonly List<SkyLease> skies = new List<SkyLease>();
    readonly List<Renderer> loadingHidden = new List<Renderer>();
    readonly List<Light> loadingLights = new List<Light>();
    readonly List<ArenaEnvironmentLight> loadingAmbient = new List<ArenaEnvironmentLight>();
    readonly List<Volume> loadingVolumes = new List<Volume>();
    readonly List<Vector3> carriedMotes = new List<Vector3>();
    int activeMotes;
    bool startIsolationComplete;
    Quaternion arrivalRotation;
    float arrivalFov;
    SummitTransferEnvironment sourceEnvironment;
    readonly ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[1400];
    Shader dissolve;
    GameObject moteRoot;
    ParticleSystem particles;
    Camera worldCamera, arenaCamera;
    AudioListener arenaListener;
    bool arenaCameraEnabled, arenaListenerEnabled;
    ThirdPersonCamera worldLook;
    PlayerMovement movement;
    Transform hero;
    ArenaSession arena;
    bool oldCinematic, disposed, arrival, arenaCameraOwned;
    Vector3 initialEye, worldDirection, arrivalDirection;
    Quaternion initialRotation, initialHeroRotation;
    float initialFov, worldElapsed, arenaElapsed;
    Vector3 centre, arrivalOffset;
    SkyLease worldSky, arenaSky;
    FogState worldFog, arenaFog;
    SummitTransferEnvironment loadedEnvironment;
    public SummitTransferEnvironment ArrivalEnvironment => loadedEnvironment;
    readonly Color veil = new Color(.055f,.065f,.085f);
    bool reducedMotion, worldSnapshot, movementOwned;
    int seedSerial;

    sealed class Surface { public Renderer renderer; public Material[] original, temporary; public float delay; public bool replaced; }
    sealed class MeadowSurface { public OwnInstancedMeadow.Batch batch; public Material temporary; }
    struct Mote { public Vector3 source; public float phase, random; }
    struct FogState
    {
        public bool enabled; public FogMode mode; public Color colour; public float density,start,end;
        public static FogState Capture() => new FogState {enabled=RenderSettings.fog,mode=RenderSettings.fogMode,
            colour=RenderSettings.fogColor,density=RenderSettings.fogDensity,start=RenderSettings.fogStartDistance,end=RenderSettings.fogEndDistance};
        public void Restore(){RenderSettings.fog=enabled;RenderSettings.fogMode=mode;RenderSettings.fogColor=colour;
            RenderSettings.fogDensity=density;RenderSettings.fogStartDistance=start;RenderSettings.fogEndDistance=end;}
    }
    sealed class SkyLease
    {
        public Camera camera; public Skybox component; public bool created, enabled; public Material original, material;
        public CameraClearFlags clear; public string directionProperty; public Vector3 direction;
        public void Release()
        {
            if(camera)camera.clearFlags=clear;
            if(component){component.material=original;component.enabled=enabled;if(created)UnityEngine.Object.Destroy(component);}
            if(material)UnityEngine.Object.Destroy(material);
        }
    }

    public SummitArenaTransition(BaselineSamBridge bridge) { owner=bridge; }
    public void Begin()
    {
        var world=owner.Owner.GetComponent<BaselineWorld>();hero=world.player;
        worldCamera=Camera.main;
        if(!worldCamera || !hero)throw new InvalidOperationException("Summit transfer requires the main camera and hero.");
        dissolve=Resources.Load<Shader>("SummitTransferDissolve");
        if(!dissolve || !dissolve.isSupported)throw new InvalidOperationException("Summit dissolve shader unavailable.");
        reducedMotion=owner.Owner.TitleReducedMotion;
        initialEye=worldCamera.transform.position;initialRotation=worldCamera.transform.rotation;initialFov=worldCamera.fieldOfView;
        initialHeroRotation=hero.rotation;worldFog=FogState.Capture();worldSnapshot=true;
        sourceEnvironment=SummitTransferEnvironment.Capture();
        movement=hero.GetComponent<PlayerMovement>();if(movement){movement.SetActionInputLocked(owner,true);movementOwned=true;}
        worldLook=worldCamera.GetComponent<ThirdPersonCamera>();
        if(worldLook){oldCinematic=worldLook.CinematicControlled;worldLook.SetCinematicControl(true);}
        centre=owner.Owner.endingEntrance?owner.Owner.endingEntrance.position:hero.position;
        worldSky=AcquireSky(worldCamera,"SummitTransferWorldSky","_Direction");
        worldDirection=worldSky!=null?Vector3.ProjectOnPlane(worldSky.direction,Vector3.up).normalized:hero.forward;
        if(worldDirection.sqrMagnitude<.1f)worldDirection=Vector3.forward;
        foreach(var root in hero.gameObject.scene.GetRootGameObjects())
        {
            // Environment roots only: never pick up an enemy, NPC, quest object or the hero.
            if(!(root.name.StartsWith("Own ",StringComparison.Ordinal)||root.name=="_A2_Greybox"))continue;
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                var p=r.bounds.center;
                if(!r.enabled || p.y<centre.y-6 || Vector2.Distance(new Vector2(p.x,p.z),new Vector2(centre.x,centre.z))>31)continue;
                if(r.GetComponentInParent<Health>() || r.GetComponentInParent<PlayerMovement>())continue;
                bool floor=r.bounds.size.y<1.4f && p.y<centre.y+.9f || r.name.StartsWith("Land_",StringComparison.Ordinal);
                string n=r.name.ToLowerInvariant();
                bool plant=n.Contains("grass")||n.Contains("tree")||n.Contains("flower")||n.Contains("plant")||n.Contains("bush");
                AddSurface(r,floor?.42f:plant?0:.20f,true,floor);
            }
            foreach(var meadow in root.GetComponentsInChildren<OwnInstancedMeadow>(false))
            foreach(var batch in meadow.batches)
            {
                if(batch==null || !batch.material || batch.positions==null)continue;
                bool near=false;
                foreach(var p in batch.positions)if(InCourt(p)){near=true;break;}
                if(!near)continue;
                var material=MakeMaterial(batch.material,true,false);
                meadows.Add(new MeadowSurface{batch=batch,temporary=material});
                foreach(var p in batch.positions)if(InCourt(p)&&seeds.Count<1200)AddMote(p+Vector3.up*.25f,0);
            }
        }
        if(surfaces.Count==0)throw new InvalidOperationException("No summit environment found; transfer stopped before hiding the world.");
        CreateParticles();
        Debug.Log("SUMMIT_TRANSFER prepared summit surfaces="+surfaces.Count+" meadow batches="+meadows.Count);
    }
    bool InCourt(Vector3 p)=>p.y>centre.y-6 && Vector2.Distance(new Vector2(p.x,p.z),new Vector2(centre.x,centre.z))<31;
    Material MakeMaterial(Material source,bool local,bool floor)
    {
        var m=new Material(dissolve){name="Visit only - "+source.name,hideFlags=HideFlags.DontSave,enableInstancing=true};
        string tex=source.HasProperty("_BaseMap")?"_BaseMap":source.HasProperty("_MainTex")?"_MainTex":null;
        if(tex!=null){m.SetTexture("_BaseMap",source.GetTexture(tex));m.SetTextureScale("_BaseMap",source.GetTextureScale(tex));m.SetTextureOffset("_BaseMap",source.GetTextureOffset(tex));}
        if(source.HasProperty("_BaseColor"))m.SetColor("_BaseColor",source.GetColor("_BaseColor"));
        else if(source.HasProperty("_Color"))m.SetColor("_BaseColor",source.GetColor("_Color"));
        m.SetFloat("_Cutoff",source.HasProperty("_Cutoff")?source.GetFloat("_Cutoff"):.08f);
        m.SetFloat("_LocalMask",local?1:0);m.SetFloat("_Floor",floor?1:0);
        m.SetVector("_Centre",new Vector4(centre.x,centre.y,centre.z,31));m.SetFloat("_Progress",-.01f);
        temporaryMaterials.Add(m);return m;
    }
    void AddSurface(Renderer r,float delay,bool local,bool floor)
    {
        var originals=r.sharedMaterials;var copies=new Material[originals.Length];
        for(int i=0;i<copies.Length;i++)copies[i]=originals[i]?MakeMaterial(originals[i],local,floor):null;
        surfaces.Add(new Surface{renderer=r,original=originals,temporary=copies,delay=delay});
        var meshFilter=r.GetComponent<MeshFilter>();var mesh=meshFilter?meshFilter.sharedMesh:null;
        // Imported models may not be CPU-readable. AABB face points remain a bounded fallback.
        Vector3[] vertices=mesh && mesh.isReadable?mesh.vertices:null;
        int count=Mathf.Clamp(Mathf.RoundToInt(r.bounds.size.magnitude*1.8f),5,30);
        for(int i=0;i<count && seeds.Count<1200;i++)
        {
            float h=Hash(++seedSerial);Vector3 p;
            if(vertices!=null && vertices.Length>0)p=r.transform.TransformPoint(vertices[Mathf.Min(vertices.Length-1,(int)(h*vertices.Length))]);
            else {Vector3 a=new Vector3(Hash(seedSerial+31)*2-1,Hash(seedSerial+71)*2-1,Hash(seedSerial+97)*2-1);a[i%3]=i%2==0?1:-1;p=r.bounds.center+Vector3.Scale(r.bounds.extents,a);}
            if(!local||InCourt(p))AddMote(p,delay);
        }
    }
    void AddMote(Vector3 p,float phase){seeds.Add(new Mote{source=p,phase=phase,random=Hash(++seedSerial)});}
    static float Hash(int n)=>Mathf.Repeat(Mathf.Sin(n*12.9898f)*43758.5453f,1);
    void CreateParticles()
    {
        var shader=Resources.Load<Shader>("SummitTransferMote");if(!shader||!shader.isSupported)throw new InvalidOperationException("Transfer mote shader unavailable.");
        moteRoot=new GameObject("Summit transfer - temporary light fragments");moteRoot.transform.SetParent(owner.transform,false);
        particles=moteRoot.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=particles.main;main.playOnAwake=false;main.loop=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=particleBuffer.Length;
        var emission=particles.emission;emission.enabled=false;var shape=particles.shape;shape.enabled=false;
        var render=particles.GetComponent<ParticleSystemRenderer>();render.renderMode=ParticleSystemRenderMode.Billboard;
        render.shadowCastingMode=ShadowCastingMode.Off;render.receiveShadows=false;
        var material=new Material(shader){hideFlags=HideFlags.DontSave};temporaryMaterials.Add(material);render.sharedMaterial=material;
    }
    SkyLease AcquireSky(Camera camera,string resource,string directionProperty)
    {
        var component=camera.GetComponent<Skybox>();var original=component?component.material:null;
        var source=original?original:RenderSettings.skybox;var shader=Resources.Load<Shader>(resource);
        if(!source||!shader||!shader.isSupported)throw new InvalidOperationException("Transfer sky is unavailable: "+resource);
        var lease=new SkyLease{camera=camera,component=component,created=!component,enabled=component&&component.enabled,original=original,clear=camera.clearFlags,directionProperty=directionProperty};
        lease.material=new Material(source){name="Transfer sky - temporary",hideFlags=HideFlags.DontSave};lease.material.shader=shader;
        lease.direction=source.HasProperty(directionProperty)?(Vector3)source.GetVector(directionProperty):new Vector3(0,.24f,1);
        if(!lease.component)lease.component=camera.gameObject.AddComponent<Skybox>();
        lease.material.SetFloat("_TransferBlend",0);lease.component.enabled=true;
        lease.component.material=lease.material;camera.clearFlags=CameraClearFlags.Skybox;skies.Add(lease);return lease;
    }
    static void SkyPose(SkyLease lease,float elevation,float blend)
    {
        if(lease==null)return;Vector3 flat=Vector3.ProjectOnPlane(lease.direction,Vector3.up).normalized;
        if(flat.sqrMagnitude<.1f)flat=Vector3.forward;
        lease.material.SetVector(lease.directionProperty,flat*Mathf.Cos(elevation)+Vector3.up*Mathf.Sin(elevation));
        lease.material.SetFloat("_TransferBlend",blend);
    }
    static float Ease(float t)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(t));
    public bool AdvanceDeparture(float delta)
    {
        if(arena && arena.hud && arena.hud.Ready)
        {
            arena.hud.SetPresentationVisible(false);
            if(!startIsolationComplete)
            {
                foreach(var root in arena.gameObject.scene.GetRootGameObjects())
                    foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                        if(!r.forceRenderingOff){r.forceRenderingOff=true;loadingHidden.Add(r);}
                startIsolationComplete=true;
            }
        }
        worldElapsed+=delta;float t=Mathf.Clamp01(worldElapsed/DepartureSeconds);
        float align=Ease(t);
        hero.rotation=Quaternion.Slerp(initialHeroRotation,Quaternion.LookRotation(worldDirection),align);
        // First look at the celestial change. Only a small backward drift here;
        // the room reveal owns the larger movement after the sun has formed.
        Vector3 wanted=initialEye-(initialRotation*Vector3.forward)*(reducedMotion?.65f:1.4f)*align
            +Vector3.up*(reducedMotion?.45f:.9f)*align;
        Vector3 current=worldCamera.transform.position,step=wanted-current;
        // Sweep the camera's own flight path. A focus ray would clamp the camera
        // against the pavilion even after its visible roof has dissolved.
        Vector3 eye=wanted;
        if(step.sqrMagnitude>.000001f && Physics.SphereCast(current,.22f,step.normalized,out var hit,step.magnitude,1<<8,QueryTriggerInteraction.Ignore))
            eye=current+step.normalized*Mathf.Max(0,hit.distance-.03f);
        if(Physics.CheckSphere(eye,.20f,1<<8,QueryTriggerInteraction.Ignore))eye=current;
        var look=Quaternion.Slerp(initialRotation,Quaternion.LookRotation(worldDirection+Vector3.up*.24f),align);
        worldCamera.transform.SetPositionAndRotation(eye,look);
        worldCamera.fieldOfView=Mathf.Lerp(initialFov,reducedMotion?48:42,align);
        float dissolveTime=Mathf.Clamp01((t-.12f)/.88f);ApplySurfaces(dissolveTime,false);UpdateMotes(dissolveTime,false);
        float elevation=worldSky!=null?Mathf.Asin(worldSky.direction.normalized.y):.35f;
        SkyPose(worldSky,Mathf.Lerp(elevation,-.24f,Ease(t)),Ease((t-.66f)/.34f));
        FogVeil(Ease((t-.5f)/.5f));return t>=1;
    }
    public void HideLoadedCamera(ArenaSession session)
    {
        loadedEnvironment=SummitTransferEnvironment.Capture();arenaFog=FogState.Capture();
        arena=session;arenaCamera=session.view.GetComponent<Camera>();arenaCameraEnabled=arenaCamera.enabled;arenaCamera.enabled=false;
        arenaListener=arenaCamera.GetComponent<AudioListener>();if(arenaListener){arenaListenerEnabled=arenaListener.enabled;arenaListener.enabled=false;}
        foreach(var root in session.gameObject.scene.GetRootGameObjects())
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                if(!r.forceRenderingOff){r.forceRenderingOff=true;loadingHidden.Add(r);}
        foreach(var root in session.gameObject.scene.GetRootGameObjects())
        {
            foreach(var light in root.GetComponentsInChildren<Light>(true))if(light.enabled){loadingLights.Add(light);light.enabled=false;}
            foreach(var ambient in root.GetComponentsInChildren<ArenaEnvironmentLight>(true))if(ambient.enabled){loadingAmbient.Add(ambient);ambient.enabled=false;}
            foreach(var volume in root.GetComponentsInChildren<Volume>(true))if(volume.enabled){loadingVolumes.Add(volume);volume.enabled=false;}
        }
        sourceEnvironment.Apply();
        FogVeil(Ease((Mathf.Clamp01(worldElapsed/DepartureSeconds)-.5f)/.5f));
    }
    // Call after ArenaSession.Start, before the old scene roots are suspended.
    public void PrepareArrival()
    {
        // The bridge hides the old roots in this same frame, before another render.
        RestoreSurfaces();seeds.Clear();seedSerial=0;
        centre=arena.playerStart;loadedEnvironment.Apply();
        arenaSky=AcquireSky(arenaCamera,"SummitTransferArenaSky","_EclipseDirection");
        arrivalDirection=Vector3.ProjectOnPlane(arenaSky.direction,Vector3.up).normalized;
        if(arrivalDirection.sqrMagnitude<.1f)arrivalDirection=Vector3.forward;
        // Map the exact last source pose, including shoulder offset and rotation,
        // through the two hero frames. The first arena frame has the same projection.
        var change=Quaternion.FromToRotation(worldDirection,arrivalDirection);
        arrivalOffset=change*(worldCamera.transform.position-hero.position);
        arrivalRotation=change*worldCamera.transform.rotation;
        arrivalFov=worldCamera.fieldOfView;
        carriedMotes.Clear();
        for(int i=0;i<activeMotes && carriedMotes.Count<320;i+=3)
            carriedMotes.Add(arena.player.transform.position+change*(particleBuffer[i].position-hero.position));
        // The destination room and Boss retain their original materials. Only
        // preload visibility is leased; the room is complete when it is revealed.
        SkyPose(arenaSky,Mathf.Asin(arenaSky.direction.normalized.y),1);
        arenaSky.material.SetFloat("_Assembly",0);
        arena.hud?.SetPresentationVisible(false);
        Debug.Log("SUMMIT_TRANSFER prepared complete arena; only the black sun assembles.");
    }
    public void BeginArrival()
    {
        arrival=true;hero=arena.player.transform;
        // Seed from the actual source pose before using path collision; do not
        // sweep from the unrelated gameplay reset pose at the scene boundary.
        Vector3 seed=hero.position+arrivalOffset;
        float clearance=arena.view.collisionRadius;
        if(Physics.CheckSphere(seed,clearance,arena.view.worldMask,QueryTriggerInteraction.Ignore))
        {
            bool found=false;
            for(float lift=.5f;lift<=6;lift+=.5f)
            {
                var candidate=seed+Vector3.up*lift;
                if(Physics.CheckSphere(candidate,clearance,arena.view.worldMask,QueryTriggerInteraction.Ignore))continue;
                seed=candidate;found=true;break;
            }
            if(!found)throw new InvalidOperationException("No clear arrival camera seed; returning to the summit.");
            arrivalOffset=seed-hero.position;
            Debug.LogWarning("SUMMIT_TRANSFER lifted overlapping arrival camera before reveal.");
        }
        arenaCamera.transform.SetPositionAndRotation(seed,arrivalRotation);
        arenaCamera.fieldOfView=arrivalFov;
        arena.view.BeginCinematic(this);arenaCameraOwned=true;
        foreach(var r in loadingHidden)if(r)r.forceRenderingOff=false;loadingHidden.Clear();
        foreach(var light in loadingLights)if(light)light.enabled=true;loadingLights.Clear();
        foreach(var ambient in loadingAmbient)if(ambient)ambient.enabled=true;loadingAmbient.Clear();
        foreach(var volume in loadingVolumes)if(volume)volume.enabled=true;loadingVolumes.Clear();
        arenaCamera.enabled=arenaCameraEnabled;if(arenaListener)arenaListener.enabled=arenaListenerEnabled;
        AdvanceArrival(0);
    }
    public bool AdvanceArrival(float delta)
    {
        arenaElapsed+=delta;float t=Mathf.Clamp01(arenaElapsed/ArrivalSeconds);
        float assembly=Ease(Mathf.InverseLerp(.15f,2.8f,arenaElapsed));
        float targetElevation=Mathf.Asin(arenaSky.direction.normalized.y);
        SkyPose(arenaSky,targetElevation,1-Ease(arenaElapsed/1.1f));
        arenaSky.material.SetFloat("_Assembly",assembly);

        // 1) Focus on the forming sun; 2) slowly reveal the room; 3) return.
        // Movement, tilt and lens have staggered curves to avoid a rapid combined zoom-out.
        float pull=Ease(Mathf.InverseLerp(2.8f,8.4f,arenaElapsed));
        Vector3 roomOffset=-arrivalDirection*(reducedMotion?23:26)+Vector3.up*(reducedMotion?10:12);
        Vector3 eye=hero.position+Vector3.Lerp(arrivalOffset,roomOffset,pull);
        var sunLook=Quaternion.Slerp(arrivalRotation,Quaternion.LookRotation(arenaSky.direction),Ease(arenaElapsed/1.2f));
        var roomLook=Quaternion.LookRotation(hero.position+Vector3.up*5+arrivalDirection*8-eye);
        var look=Quaternion.Slerp(sunLook,roomLook,Ease(Mathf.InverseLerp(3.2f,8.4f,arenaElapsed)));
        float fov=Mathf.Lerp(arrivalFov,reducedMotion?68:64,Ease(Mathf.InverseLerp(3.4f,8.4f,arenaElapsed)));
        float handback=Ease((arenaElapsed-9.2f)/3.2f);
        // A finite gaze defines rotation only. Collision checks the camera flight,
        // so looking up at a sky object cannot pull the lens into a distant pillar.
        arena.view.PoseCinematic(this,eye+look*Vector3.forward*8,eye,fov,handback,true);
        UpdateMotes(assembly,true,assembly);
        if(arena.hud)arena.hud.SetPresentationVisible(false);
        return t>=1;
    }
    void ApplySurfaces(float progress,bool assembling)
    {
        foreach(var s in surfaces)
        {
            if(!s.renderer)continue;
            float p=Mathf.Clamp01((progress-s.delay)/(1-s.delay));float removed=assembling?1.01f-p*1.02f:p*1.02f-.01f;
            bool needsTemporary=assembling?p<1:p>0;
            if(needsTemporary&&!s.replaced){s.renderer.sharedMaterials=s.temporary;s.replaced=true;}
            if(assembling&&p>=1&&s.replaced){s.renderer.sharedMaterials=s.original;s.replaced=false;}
            foreach(var m in s.temporary)if(m)m.SetFloat("_Progress",removed);
        }
        foreach(var m in meadows)
        {m.batch.presentationMaterial=progress>0?m.temporary:null;m.batch.presentationRegion=new Vector4(centre.x,centre.y-6,centre.z,31);
            m.temporary.SetFloat("_Progress",progress*1.02f-.01f);}
    }
    Vector3 SunRim(Camera camera,Vector3 direction,float phase,float angularRadius)
    {
        direction.Normalize();
        Vector3 right=Vector3.Cross(Vector3.up,direction).normalized,up=Vector3.Cross(direction,right);
        float radius=Mathf.Tan(angularRadius)*120;
        return camera.transform.position+direction*120+(right*Mathf.Cos(phase)+up*Mathf.Sin(phase))*radius;
    }
    void UpdateMotes(float progress,bool assembling,float sunProgress=-1)
    {
        if(!particles)return;int count=0;
        if(!assembling)
        {
            // Every summit fragment heads directly toward the future black sun.
            // There is no above-player gathering point or upward arc waypoint.
            Vector3 direction=(worldDirection+Vector3.up*.24f).normalized;
            foreach(var s in seeds)
            {
                float u=(progress-s.phase)/(1-s.phase);
                if(u<=0)continue;u=Mathf.Clamp01(u);
                float travel=Ease(u)*.80f;
                Vector3 rim=SunRim(worldCamera,direction,s.random*Mathf.PI*2,.108f);
                float alpha=Mathf.Sin(u*Mathf.PI)*.65f+.55f*Ease((u-.55f)/.45f);
                particleBuffer[count++]=new ParticleSystem.Particle{
                    position=Vector3.Lerp(s.source,rim,travel),
                    startSize=Mathf.Lerp(.08f,.4f,travel),startColor=new Color(1,.68f+s.random*.22f,.3f+s.random*.4f,alpha),
                    remainingLifetime=2,startLifetime=2,rotation=s.random*90};
                if(count>=particleBuffer.Length-320)break;
            }
        }
        else if(carriedMotes.Count>0)
        {
            float u=sunProgress>=0?sunProgress:progress;
            // Finish the existing flight straight into the sun; never emit room
            // construction particles or create a second gathering cluster.
            if(u<1)for(int i=0;i<carriedMotes.Count && count<particleBuffer.Length;i++)
            {
                float phase=i*2.399963f;
                Vector3 rim=SunRim(arenaCamera,arenaSky.direction,phase,arenaSky.material.GetFloat("_EclipseSize"));
                particleBuffer[count++]=new ParticleSystem.Particle{
                    position=Vector3.Lerp(carriedMotes[i],rim,u),
                    startSize=Mathf.Lerp(.4f,.45f,u),startColor=new Color(1,.82f,.48f,.55f*(1-Ease((u-.72f)/.28f))),
                    remainingLifetime=2,startLifetime=2,rotation=phase*57.3f};
            }
        }
        activeMotes=count;particles.SetParticles(particleBuffer,count);
    }
    void FogVeil(float amount)
    {
        if(amount<.001f){if(arrival)arenaFog.Restore();else worldFog.Restore();return;}
        var normal=arrival?arenaFog:worldFog;
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
        RenderSettings.fogColor=Color.Lerp(normal.colour,veil,amount);
        Camera current=arrival?arenaCamera:worldCamera;
        float heroRange=current&&hero?Vector3.Distance(current.transform.position,hero.position):20;
        RenderSettings.fogStartDistance=Mathf.Lerp(90,heroRange+3,amount);
        RenderSettings.fogEndDistance=Mathf.Lerp(200,heroRange+24,amount);
    }
    void RestoreSurfaces()
    {
        foreach(var s in surfaces)if(s.renderer&&s.replaced)Cleanup(()=>s.renderer.sharedMaterials=s.original);
        surfaces.Clear();foreach(var m in meadows)m.batch.presentationMaterial=null;meadows.Clear();
    }
    static void Cleanup(Action action){try{action();}catch(Exception error){Debug.LogException(error);}}
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        RestoreSurfaces();
        foreach(var r in loadingHidden)if(r)Cleanup(()=>r.forceRenderingOff=false);loadingHidden.Clear();
        if(movement&&movementOwned)Cleanup(()=>{movement.SetActionInputLocked(owner,false);movement.transform.rotation=initialHeroRotation;});
        if(worldLook)Cleanup(()=>worldLook.SetCinematicControl(oldCinematic));
        if(worldCamera&&worldSnapshot)Cleanup(()=>{worldCamera.transform.SetPositionAndRotation(initialEye,initialRotation);worldCamera.fieldOfView=initialFov;});
        if(arenaCameraOwned && arena && arena.view)Cleanup(()=>arena.view.EndCinematic(this));
        foreach(var light in loadingLights)if(light)Cleanup(()=>light.enabled=true);loadingLights.Clear();
        foreach(var ambient in loadingAmbient)if(ambient)Cleanup(()=>ambient.enabled=true);loadingAmbient.Clear();
        foreach(var volume in loadingVolumes)if(volume)Cleanup(()=>volume.enabled=true);loadingVolumes.Clear();
        if(arenaCamera)arenaCamera.enabled=arenaCameraEnabled;
        if(arenaListener)arenaListener.enabled=arenaListenerEnabled;
        foreach(var sky in skies)Cleanup(sky.Release);skies.Clear();
        if(arrival)arenaFog.Restore();else if(worldSnapshot)worldFog.Restore();
        if(moteRoot)UnityEngine.Object.Destroy(moteRoot);
        foreach(var material in temporaryMaterials)if(material)UnityEngine.Object.Destroy(material);
        temporaryMaterials.Clear();
    }
}
