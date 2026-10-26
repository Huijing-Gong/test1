using UnityEngine;
using UnityEngine.Rendering;

// Compact, bounded visual effects. All mesh/line renderers share two resource
// materials; no Renderer.material clones, runtime lights, textures or emitters.
public sealed class BattleSpecialVfx : MonoBehaviour
{
    public const int EffectCapacity = 8;
    public static int RuntimeMaterialCount => 0;
    public bool BeamVisible {get;private set;}
    public float BeamVisualWidth {get;private set;}
    public float BeamVisualLength {get;private set;}
    public Vector3 BeamVisualOrigin {get;private set;}
    public Vector3 BeamVisualEnd {get;private set;}
    public bool HasContinuousEffect { get; private set; }
    public int ActiveEffectCount
    {
        get { int n=0; if(bursts!=null) foreach(var burst in bursts) if(burst.life>0) n++; return n; }
    }

    public static readonly Color Cyan = new Color32(132,205,196,255);
    public static readonly Color Gold = new Color32(234,190,85,255);
    public static readonly Color Ink = new Color32(19,55,49,235);
    static readonly Color Cream=new Color32(247,236,205,255);
    static readonly int Tint = Shader.PropertyToID("_Tint");
    static readonly int Window = Shader.PropertyToID("_Window");
    static readonly int Pulse = Shader.PropertyToID("_Pulse");
    static readonly int Beam = Shader.PropertyToID("_Beam");
    static readonly int Flow = Shader.PropertyToID("_Flow");
    static readonly int BeamLength = Shader.PropertyToID("_BeamLength");
    static readonly int Glow = Shader.PropertyToID("_Glow");
    static Material energyMaterial, shieldMaterial;
    static Mesh sphereMesh, starMesh, shieldMesh, shardsMesh;
    static int geometryOwners;
    bool ownsGeometry;

    Transform root, chargeRoot, shieldRoot, dashRoot, riseRoot;
    Transform beamRoot;
    MeshRenderer chargeCore, chargeOutline, shieldFill;
    LineRenderer chargeOrbit, chargeSecondOrbit, shieldEdge, shieldOutline, dashStreak, dashTrail, dashKick, dashKickOutline, riseCoil;
    readonly LineRenderer[] riseFlames=new LineRenderer[5];
    LineRenderer beamContour,beamBody,beamCore;
    LineRenderer chargeFloor;
    LineRenderer releaseRing;
    float releaseLife,releasePower;
    readonly LineRenderer[] gatherLines=new LineRenderer[5];
    readonly LineRenderer[] beamStreaks=new LineRenderer[3];
    MeshRenderer beamMuzzle,beamMuzzleDark,beamEndFlash;
    MaterialPropertyBlock properties;
    readonly Vector3[] ringPoints = new Vector3[33];
    Burst[] bursts;
    BattleSpecialMoves moves;
    BattleSpecialAudio audioFx;
    BattleSpecialKind lastKind;
    string lastPhase;
    bool chargeReadyCue;

    sealed class Burst
    {
        public Transform root;
        public MeshRenderer flash, shards, outline, core;
        public LineRenderer ring;
        public float life, duration, power, facing;
        public Vector3 position;
        public Color color;
        public bool dull;
        public int bornFrame;
    }

    internal static Material EnergyMaterial
    {
        get
        {
            if(!energyMaterial) energyMaterial=Resources.Load<Material>("SideA/SpecialEnergy");
            return energyMaterial;
        }
    }

    void Awake()
    {
        properties=new MaterialPropertyBlock();
        moves=GetComponent<BattleSpecialMoves>();
        audioFx=GetComponent<BattleSpecialAudio>();
        if(!audioFx) audioFx=gameObject.AddComponent<BattleSpecialAudio>();
        Build();
        geometryOwners++;
        ownsGeometry=true;
        Clear();
    }

    void Build()
    {
        EnsureGeometry();
        if(!shieldMaterial) shieldMaterial=Resources.Load<Material>("SideA/CounterShield");
        root=Child("Bunny K VFX",transform);
        chargeRoot=Child("Charge presentation",root);
        chargeOutline=Renderer("Charge dark outline",chargeRoot,sphereMesh,EnergyMaterial);
        chargeCore=Renderer("Charge core",chargeRoot,sphereMesh,EnergyMaterial);
        chargeOrbit=Line("Charge orbit",chargeRoot,.038f,EnergyMaterial);
        chargeSecondOrbit=Line("Charge secondary orbit",chargeRoot,.019f,EnergyMaterial);
        Circle(chargeOrbit,.26f);
        Circle(chargeSecondOrbit,.31f);
        chargeFloor=Line("Charge floor star ring",chargeRoot,.018f,EnergyMaterial);
        releaseRing=Line("Beam release pressure ring",root,.035f,EnergyMaterial);
        Circle(releaseRing,1f);releaseRing.enabled=false;
        Circle(chargeFloor,.38f);
        for(int i=0;i<gatherLines.Length;i++)
        {gatherLines[i]=Line("Inward charge stroke "+i,chargeRoot,.026f,EnergyMaterial);gatherLines[i].positionCount=9;}

        shieldRoot=Child("Counter presentation",root);
        shieldFill=Renderer("Counter shield",shieldRoot,shieldMesh,shieldMaterial);
        shieldOutline=Line("Counter dark edge",shieldRoot,.110f,EnergyMaterial);
        shieldEdge=Line("Counter edge",shieldRoot,.057f,EnergyMaterial);
        var arc=new Vector3[25];
        for(int i=0;i<arc.Length;i++)
        {
            float a=Mathf.Lerp(-68f,68f,i/(float)(arc.Length-1))*Mathf.Deg2Rad;
            arc[i]=new Vector3(.35f+Mathf.Cos(a)*.40f,Mathf.Sin(a)*.76f,-.055f);
        }
        shieldEdge.positionCount=shieldOutline.positionCount=arc.Length;
        shieldEdge.SetPositions(arc); shieldOutline.SetPositions(arc);
        shieldOutline.transform.localPosition=Vector3.forward*.014f;
        SetLine(shieldOutline,Ink);

        dashRoot=Child("Dash presentation",root);
        dashStreak=Line("Dash foot streak",dashRoot,.063f,EnergyMaterial);
        dashStreak.positionCount=3;
        dashStreak.SetPositions(new[]{new Vector3(-.40f,-.28f,0),new Vector3(.38f,-.24f,0),new Vector3(.79f,-.16f,0)});
        dashTrail=Line("Dash tight wake",dashRoot,.023f,EnergyMaterial);
        dashTrail.positionCount=4;
        dashTrail.SetPositions(new[]{new Vector3(-1.2f,-.22f,.02f),new Vector3(-.85f,-.14f,.02f),new Vector3(-.43f,-.08f,.02f),new Vector3(-.05f,-.02f,.02f)});
        SetLine(dashStreak,Cream); SetLine(dashTrail,new Color(Cyan.r,Cyan.g,Cyan.b,.68f));
        dashTrail.widthMultiplier=.30f;
        dashTrail.widthCurve=new AnimationCurve(new Keyframe(0,0),new Keyframe(.55f,.85f),new Keyframe(1,.12f));
        dashStreak.widthCurve=new AnimationCurve(new Keyframe(0,0),new Keyframe(.6f,1),new Keyframe(1,0));
        dashKickOutline=Line("Dash foot crescent dark edge",dashRoot,.091f,EnergyMaterial);
        dashKick=Line("Dash foot crescent",dashRoot,.044f,EnergyMaterial);
        var kickArc=new Vector3[12];
        for(int i=0;i<kickArc.Length;i++)
        {
            float a=Mathf.Lerp(-65f,65f,i/(float)(kickArc.Length-1))*Mathf.Deg2Rad;
            kickArc[i]=new Vector3(.71f+Mathf.Cos(a)*.26f,-.12f+Mathf.Sin(a)*.30f,-.015f);
        }
        dashKick.positionCount=dashKickOutline.positionCount=kickArc.Length;
        dashKick.SetPositions(kickArc);dashKickOutline.SetPositions(kickArc);
        dashKickOutline.transform.localPosition=Vector3.forward*.01f;
        SetLine(dashKickOutline,Ink);SetLine(dashKick,Cream);

        riseRoot=Child("Rise presentation",root);
        riseCoil=Line("Rise coil",riseRoot,.028f,EnergyMaterial);
        riseCoil.positionCount=33;
        for(int i=0;i<ringPoints.Length;i++)
        {
            float t=i/(float)(ringPoints.Length-1), a=t*Mathf.PI*6;
            ringPoints[i]=new Vector3(Mathf.Sin(a)*.22f,-.30f-t*.7f,Mathf.Cos(a)*.12f);
        }
        riseCoil.SetPositions(ringPoints); SetLine(riseCoil,Cyan);
        for(int i=0;i<riseFlames.Length;i++)
        {
            var flame=riseFlames[i]=Line("Rise tapered plume "+i,riseRoot,i==2?.24f:.16f,EnergyMaterial);
            float x=(i-2)*.17f;
            flame.positionCount=3;
            flame.SetPositions(new[]{new Vector3(x*1.7f,-1.9f+Mathf.Abs(i-2)*.19f,.02f),new Vector3(x,-.98f,-.02f),new Vector3(x*.3f,-.4f,-.03f)});
            flame.widthCurve=new AnimationCurve(new Keyframe(0,0),new Keyframe(.58f,1),new Keyframe(1,.08f));
            SetLine(flame,i==2?Cream:Cyan);
        }

        beamRoot=Child("K energy wave column",root);
        beamContour=BeamLine("Beam dark contour",beamRoot);
        beamBody=BeamLine("Beam cyan body",beamRoot);
        beamCore=BeamLine("Beam white core",beamRoot);
        beamContour.sortingOrder=10;beamBody.sortingOrder=11;beamCore.sortingOrder=12;
        for(int i=0;i<beamStreaks.Length;i++)
        {beamStreaks[i]=BeamLine("Beam speed streak "+i,beamRoot);beamStreaks[i].sortingOrder=13;}
        beamMuzzleDark=Renderer("Beam muzzle dark border",beamRoot,sphereMesh,EnergyMaterial);
        beamMuzzle=Renderer("Beam muzzle",beamRoot,sphereMesh,EnergyMaterial);
        beamEndFlash=Renderer("Beam end flash",beamRoot,sphereMesh,EnergyMaterial);
        beamMuzzleDark.sortingOrder=14;beamMuzzle.sortingOrder=15;beamEndFlash.sortingOrder=14;

        bursts=new Burst[EffectCapacity];
        for(int i=0;i<bursts.Length;i++)
        {
            var burst=new Burst {root=Child("Reusable K burst "+i,root)};
            burst.flash=Renderer("Contact star",burst.root,starMesh,EnergyMaterial);
            burst.outline=Renderer("Contact dark rim",burst.root,starMesh,EnergyMaterial);
            burst.core=Renderer("Contact cream core",burst.root,starMesh,EnergyMaterial);
            burst.outline.sortingOrder=105;burst.flash.sortingOrder=106;burst.core.sortingOrder=107;
            burst.shards=Renderer("Contact shards",burst.root,shardsMesh,EnergyMaterial);
            burst.ring=Line("Contact ripple",burst.root,.033f,EnergyMaterial);
            Circle(burst.ring,.45f);
            bursts[i]=burst;
        }
    }

    public void Tick(BattleSpecialKind kind,string phase,float charge01,float facing)
    {
        if(!root) return;
        if(Time.timeScale<=0f) return;
        root.position=transform.position;
        root.rotation=Quaternion.identity;
        float dir=facing>=0 ? 1f : -1f;
        bool charge=kind==BattleSpecialKind.Charge && (phase=="Startup" || phase=="Charging");
        bool counter=kind==BattleSpecialKind.Counter && (phase=="Startup" ||
            (phase=="Active" && (!moves || moves.IsCounterWindow)));
        bool dash=kind==BattleSpecialKind.Dash && (phase=="Startup" || phase=="Active");
        bool rise=kind==BattleSpecialKind.Rise && (phase=="Startup" || phase=="Active");
        HasContinuousEffect=charge || counter || dash || rise || BeamVisible;
        SetActive(chargeRoot,charge); SetActive(shieldRoot,counter);
        SetActive(dashRoot,dash); SetActive(riseRoot,rise);
        if(kind!=lastKind || phase!=lastPhase)
        {
            if(phase=="Startup")
                audioFx.Play(kind==BattleSpecialKind.Counter ? BattleSpecialSound.CounterReady :
                    kind==BattleSpecialKind.Charge ? BattleSpecialSound.ChargeReady : BattleSpecialSound.Ready);
            lastKind=kind; lastPhase=phase;
        }
        if(charge)
        {
            float power=Mathf.Clamp01(charge01), pulse=1f+Mathf.Sin(Time.time*(7+power*8))*.035f;
            chargeRoot.localPosition=new Vector3(.55f*dir,.19f,-.72f);
            chargeRoot.localRotation=Quaternion.identity;
            chargeRoot.localScale=Vector3.one;
            chargeOutline.transform.localScale=Vector3.one*(.42f+power*.37f)*pulse;
            chargeCore.transform.localScale=Vector3.one*(.25f+power*.25f)*pulse;
            chargeCore.transform.localPosition=Vector3.back*.025f;
            ApplyBeamMesh(chargeCore,new Color(.34f,.94f,.84f,1));
            ApplyBeamMesh(chargeOutline,new Color(.10f,.73f,.66f,.38f+power*.22f));
            chargeOrbit.transform.localRotation=Quaternion.Euler(38,24,Time.time*110);
            chargeSecondOrbit.transform.localRotation=Quaternion.Euler(-43,-30,-Time.time*155);
            chargeOrbit.transform.localScale=Vector3.one*(.75f+power*.65f);
            chargeSecondOrbit.transform.localScale=Vector3.one*(.7f+power*.6f);
            chargeOrbit.widthMultiplier=.018f+power*.016f;
            SetLine(chargeOrbit,new Color(.63f,1,.91f,.75f));
            SetLine(chargeSecondOrbit,new Color(Cream.r,Cream.g,Cream.b,.4f+power*.45f));
            chargeFloor.transform.position=transform.position+new Vector3(0,-.96f,0);
            chargeFloor.transform.rotation=Quaternion.Euler(90,0,0);
            SetLine(chargeFloor,new Color(Cyan.r,Cyan.g,Cyan.b,.18f+power*.32f));
            for(int i=0;i<gatherLines.Length;i++)
            {
                float t=Mathf.Repeat(Time.time*(.8f+power*1.1f)+i*.2f,1),angle=i*Mathf.PI*2/5+Time.time*.4f;
                for(int p=0;p<9;p++)
                {
                    float u=p/8f,s=Mathf.Clamp01(t*.78f+u*.22f);
                    float theta=angle+s*1.5f;
                    float radius=Mathf.Lerp(.78f+power*.40f,.12f,s*s);
                    gatherLines[i].SetPosition(p,new Vector3(Mathf.Cos(theta)*radius,Mathf.Sin(theta)*radius,-.04f));
                }
                gatherLines[i].startWidth=.006f;gatherLines[i].endWidth=.025f+power*.012f;
                SetLine(gatherLines[i],new Color(.58f,.96f,.82f,Mathf.Sin(t*Mathf.PI)*(.35f+power*.5f)));
            }
            audioFx.SetCharge(power);
            if(power>=.98f && !chargeReadyCue)
            {chargeReadyCue=true;audioFx.Play(BattleSpecialSound.Ready,power);}
        }
        else audioFx.StopCharge();
        if(counter)
        {
            bool valid=phase=="Active" && (!moves || moves.IsCounterWindow);
            shieldRoot.localPosition=new Vector3(.12f*dir,-.07f,-.67f);
            shieldRoot.localScale=new Vector3(dir,1,1);
            properties.Clear(); properties.SetColor(Tint,new Color(Cyan.r,Cyan.g,Cyan.b,valid?.62f:.24f));
            properties.SetFloat(Window,valid?1:0); properties.SetFloat(Pulse,Time.time);
            shieldFill.SetPropertyBlock(properties);
            SetLine(shieldEdge,valid ? Gold : Cyan);
        }
        if(dash)
        {
            dashRoot.localPosition=new Vector3(0,0,-.67f);
            dashRoot.localScale=new Vector3(dir,1,1);
            dashTrail.enabled=phase=="Active";
            dashStreak.enabled=phase=="Active";
            dashStreak.widthMultiplier=.14f;
            bool kicking=phase=="Active" && (!moves || moves.Phase01>=.40f);
            dashKick.enabled=dashKickOutline.enabled=kicking;
        }
        if(rise)
        {
            riseRoot.localPosition=new Vector3(0,0,-.63f);
            riseRoot.localScale=new Vector3(1,phase=="Active" ? 1f : .4f,1);
            riseCoil.enabled=phase=="Active";
            foreach(var flame in riseFlames)flame.enabled=phase=="Active";
            riseCoil.transform.localRotation=Quaternion.Euler(0,Time.time*220f,0);
        }
    }

    void Update()
    {
        if(bursts==null || Time.timeScale<=0f) return;
        if(releaseLife>0)
        {
            releaseLife=Mathf.Max(0,releaseLife-Mathf.Min(Time.unscaledDeltaTime,1f/30f));
            float t=1-releaseLife/.20f;
            float size=Mathf.Lerp(.16f,.55f+releasePower*.4f,t);
            releaseRing.transform.localScale=new Vector3(size*.4f,size,1);
            SetLine(releaseRing,new Color(Cream.r,Cream.g,Cream.b,(1-t)*(1-t)*.85f));
            releaseRing.enabled=releaseLife>0;
        }
        foreach(var burst in bursts)
        {
            if(burst.life<=0f || burst.bornFrame==Time.frameCount) continue;
            burst.life=Mathf.Max(0,burst.life-Mathf.Min(Time.unscaledDeltaTime,1f/30f));
            if(burst.life<=0f) {SetActive(burst.root,false); continue;}
            float t=1f-burst.life/burst.duration, fade=(1f-t)*(1f-t);
            burst.root.position=burst.position;
            burst.root.rotation=Quaternion.identity;
            burst.root.localScale=Vector3.one;
            burst.flash.transform.localScale=new Vector3(.36f+burst.power*.38f,.36f+burst.power*.28f,1f)*(1+t*.5f);
            burst.flash.transform.localRotation=Quaternion.Euler(0,0,burst.facing*18f+t*23f);
            burst.outline.transform.localScale=burst.flash.transform.localScale*1.14f;
            burst.core.transform.localScale=burst.flash.transform.localScale*.66f;
            burst.outline.transform.localRotation=burst.core.transform.localRotation=burst.flash.transform.localRotation;
            burst.shards.transform.localScale=Vector3.one*(.28f+t*.80f);
            burst.shards.transform.localRotation=Quaternion.Euler(0,0,burst.facing*t*26f);
            burst.ring.transform.localScale=Vector3.one*(.35f+t*(.95f+burst.power*.4f));
            burst.ring.widthMultiplier=.026f*(1f-t*.6f);
            Color tint=burst.color; tint.a*=fade;
            Apply(burst.flash,tint); SetLine(burst.ring,tint);
            Apply(burst.outline,new Color(Ink.r,Ink.g,Ink.b,fade*.8f));
            Apply(burst.core,new Color(Cream.r,Cream.g,Cream.b,fade));
            tint.a*=.72f; Apply(burst.shards,tint);
        }
    }

    public void PlayRelease(BattleSpecialKind kind,Vector3 position,float facing,float power)
    {
        if(kind==BattleSpecialKind.Charge)
        {
            releaseLife=.20f;releasePower=power;
            releaseRing.transform.position=position+Vector3.back*.72f;
            releaseRing.transform.localScale=new Vector3(.064f,.16f,1);
            releaseRing.enabled=true;SetLine(releaseRing,Cream);
        }
        // Release is a local launch cue, not a successful-hit star.
        Spawn(position,facing,Mathf.Clamp01(power)*.35f,Cyan,.18f,true);
        audioFx.Play(kind==BattleSpecialKind.Charge ? BattleSpecialSound.Shot :
            kind==BattleSpecialKind.Rise ? BattleSpecialSound.Rise : BattleSpecialSound.Dash,power);
    }

    public void PlayImpact(Vector3 position,float facing,float power,bool wall=false)
    {
        Spawn(position,facing,Mathf.Clamp01(power),wall ? new Color(.32f,.76f,.85f,.8f) : Gold,wall ? .20f : .30f,wall);
        audioFx.Play(wall ? BattleSpecialSound.Wall : BattleSpecialSound.Impact,power);
    }

    public void PlayCounter(bool success,Vector3 position,float facing)
    {
        // Catch and retaliatory damage are separate. Only PlayImpact is a heavy hit.
        Spawn(position,facing,success ? .22f : .08f,
            success ? Gold : new Color(Cyan.r,Cyan.g,Cyan.b,.35f),success ? .16f : .15f,!success);
        audioFx.Play(success ? BattleSpecialSound.CounterSuccess : BattleSpecialSound.CounterFail);
    }

    void Spawn(Vector3 position,float facing,float power,Color color,float duration,bool dull)
    {
        if(bursts==null) return;
        Burst selected=bursts[0];
        foreach(var burst in bursts) {if(burst.life<=0f){selected=burst;break;} if(burst.life<selected.life) selected=burst;}
        selected.position=position+Vector3.back*.66f;
        selected.facing=facing>=0 ? 1 : -1;
        selected.power=power; selected.color=color;
        selected.life=selected.duration=duration; selected.dull=dull;
        selected.bornFrame=Time.frameCount;
        selected.root.position=selected.position;
        selected.flash.enabled=!dull;
        selected.outline.enabled=selected.core.enabled=!dull;
        selected.flash.transform.localScale=new Vector3(.36f+power*.38f,.36f+power*.28f,1);
        selected.outline.transform.localScale=selected.flash.transform.localScale*1.14f;
        selected.core.transform.localScale=selected.flash.transform.localScale*.66f;
        selected.shards.transform.localScale=Vector3.one*.28f;
        selected.ring.transform.localScale=Vector3.one*.35f;
        Apply(selected.flash,color);Apply(selected.outline,Ink);Apply(selected.core,Cream);Apply(selected.shards,color);SetLine(selected.ring,color);
        selected.shards.enabled=true;
        selected.ring.enabled=!dull;
        SetActive(selected.root,true);
    }

    // End-of-move clears only the sustained cues. An emitted contact spark
    // keeps its independent, bounded short lifetime.
    public void ClearContinuous()
    {
        HasContinuousEffect=false;
        SetActive(chargeRoot,false); SetActive(shieldRoot,false);
        SetActive(dashRoot,false); SetActive(riseRoot,false);
        ClearBeam();
        lastKind=BattleSpecialKind.None; lastPhase=null;
        chargeReadyCue=false;
        if(audioFx)audioFx.StopCharge();
    }

    public void Clear()
    {
        ClearContinuous();
        releaseLife=0;if(releaseRing)releaseRing.enabled=false;
        if(bursts!=null) foreach(var burst in bursts) {burst.life=0;SetActive(burst.root,false);}
        if(audioFx) audioFx.Stop();
    }

    void OnDisable()=>Clear();
    void OnDestroy()
    {
        if(root)Destroy(root.gameObject);
        if(!ownsGeometry)return;
        ownsGeometry=false;
        geometryOwners=Mathf.Max(0,geometryOwners-1);
        // Effect objects and meshes are destroyed at the end of this frame, so no orphaned
        // DontSave meshes survive repeated scene visits/editor play sessions.
        if(geometryOwners==0)DestroySharedGeometry();
    }

    // The runtime gives the exact sphere-swept centre line after wall clipping.
    // Flat caps and clipped internal streaks cannot visually extend beyond it.
    public void SetBeam(Vector3 origin,Vector3 end,float radius,float charge)
    {
        float length=Vector3.Distance(origin,end);
        if(!beamRoot || length<.025f || radius<=0) {ClearBeam();return;}
        // ResolveBeam runs before Tick. Stabilise the parent first so world
        // muzzle/cap placements cannot inherit a second movement later in Tick.
        root.position=transform.position;root.rotation=Quaternion.identity;
        float power=Mathf.Clamp01(charge),width=radius*2f;
        Vector3 direction=(end-origin)/length;
        BeamVisible=true;HasContinuousEffect=true;
        BeamVisualWidth=width;BeamVisualLength=length;BeamVisualOrigin=origin;BeamVisualEnd=end;
        SetActive(beamRoot,true);
        Segment(beamContour,origin,end,width,new Color(.05f,.44f,.41f,.48f),2);
        Segment(beamBody,origin,end,width*.94f,new Color(.12f,.84f,.73f,.96f),1);
        Segment(beamCore,origin,end,width*.28f,new Color(.96f,1f,.90f,.90f),1);
        for(int i=0;i<beamStreaks.Length;i++)
        {
            float head=Mathf.Repeat(Time.time*3.5f+i*.33f,1f)*length;
            float tail=Mathf.Max(0,head-Mathf.Min(length*.23f,radius*2+length*.10f));
            Vector3 across=Vector3.up*((i-1)*.54f*radius);
            Segment(beamStreaks[i],origin+direction*tail+across,origin+direction*head+across,
                radius*.09f,new Color(.87f,1f,.91f,.8f),1);
        }
        beamMuzzleDark.transform.position=origin;
        beamMuzzleDark.transform.localScale=Vector3.one*(radius*1.25f);
        ApplyBeamMesh(beamMuzzleDark,new Color(.10f,.77f,.65f,.5f));
        beamMuzzle.transform.position=origin+Vector3.back*.02f;
        beamMuzzle.transform.localScale=Vector3.one*(radius*1.25f)*(1+Mathf.Sin(Time.time*27)*.035f);
        ApplyBeamMesh(beamMuzzle,new Color(.55f,1f,.87f,1));
        // A rounded converging head replaces the ruler-like square end. Its
        // entire X extent lies inside [origin,end], and its Y/Z radii stay
        // below the actual contact radius, including extremely short clips.
        float endHalfLength=Mathf.Min(radius*.8f,length*.18f);
        beamEndFlash.transform.position=end-direction*endHalfLength;
        beamEndFlash.transform.localScale=new Vector3(endHalfLength*1.2f,radius*.75f,radius*.6f);
        beamEndFlash.transform.rotation=Quaternion.identity;
        ApplyBeamMesh(beamEndFlash,new Color(Cream.r,Cream.g,Cream.b,.45f+.18f*power));
    }

    public void ClearBeam()
    {
        BeamVisible=false;BeamVisualWidth=BeamVisualLength=0;
        BeamVisualOrigin=BeamVisualEnd=Vector3.zero;
        SetActive(beamRoot,false);
        HasContinuousEffect=chargeRoot && chargeRoot.gameObject.activeSelf ||
            shieldRoot && shieldRoot.gameObject.activeSelf ||
            dashRoot && dashRoot.gameObject.activeSelf || riseRoot && riseRoot.gameObject.activeSelf;
    }

    LineRenderer BeamLine(string name,Transform parent)
    {
        var line=Line(name,parent,.1f,EnergyMaterial);
        line.positionCount=2;line.useWorldSpace=true;line.numCapVertices=0;line.numCornerVertices=0;
        line.textureMode=LineTextureMode.Stretch;return line;
    }
    void Segment(LineRenderer line,Vector3 start,Vector3 end,float width,Color color,int beamMode)
    {
        line.SetPosition(0,start);line.SetPosition(1,end);line.widthMultiplier=width;
        SetLine(line,color);
        properties.Clear();properties.SetFloat(Beam,beamMode);properties.SetFloat(Flow,Time.time);
        properties.SetFloat(BeamLength,Vector3.Distance(start,end));
        properties.SetFloat(Glow,beamMode==1?1f:.45f);
        line.SetPropertyBlock(properties);
    }
    void ApplyBeamMesh(MeshRenderer renderer,Color color)
    {
        properties.Clear();properties.SetColor(Tint,color);properties.SetFloat(Glow,1);
        properties.SetFloat(Beam,3);properties.SetFloat(Flow,Time.time);
        renderer.SetPropertyBlock(properties);
    }

    static Transform Child(string name,Transform parent)
    {
        var child=new GameObject(name).transform;
        child.SetParent(parent,false); return child;
    }

    internal static MeshRenderer Renderer(string name,Transform parent,Mesh mesh,Material material)
    {
        var child=Child(name,parent);
        child.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=child.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;
        renderer.receiveShadows=false;
        renderer.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
        return renderer;
    }

    internal static LineRenderer Line(string name,Transform parent,float width,Material material)
    {
        var line=Child(name,parent).gameObject.AddComponent<LineRenderer>();
        line.sharedMaterial=material;
        line.useWorldSpace=false; line.widthMultiplier=width;
        line.numCapVertices=2; line.numCornerVertices=2;
        line.shadowCastingMode=ShadowCastingMode.Off;
        line.receiveShadows=false; line.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
        return line;
    }

    void Circle(LineRenderer line,float radius)
    {
        line.positionCount=ringPoints.Length;
        for(int i=0;i<ringPoints.Length;i++)
        {
            float angle=i/(float)(ringPoints.Length-1)*Mathf.PI*2;
            ringPoints[i]=new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,0);
        }
        line.SetPositions(ringPoints);
    }

    internal static void SetLine(LineRenderer line,Color color) {line.startColor=color;line.endColor=color;}
    void Apply(MeshRenderer renderer,Color color)
    {
        properties.Clear();properties.SetColor(Tint,color);renderer.SetPropertyBlock(properties);
    }
    static void SetActive(Transform item,bool active)
    {
        if(item && item.gameObject.activeSelf!=active) item.gameObject.SetActive(active);
    }

    static void EnsureGeometry()
    {
        if(sphereMesh) return;
        const int sides=24,rings=12;
        var verts=new Vector3[(rings+1)*(sides+1)];
        var sphereUV=new Vector2[verts.Length];
        var sphereNormals=new Vector3[verts.Length];
        var triangles=new int[rings*sides*6];
        for(int y=0;y<=rings;y++) for(int x=0;x<=sides;x++)
        {
            float a=x/(float)sides*Mathf.PI*2, b=y/(float)rings*Mathf.PI;
            verts[y*(sides+1)+x]=new Vector3(Mathf.Sin(b)*Mathf.Cos(a),Mathf.Cos(b),Mathf.Sin(b)*Mathf.Sin(a))*.5f;
            sphereUV[y*(sides+1)+x]=new Vector2(x/(float)sides,y/(float)rings);
            sphereNormals[y*(sides+1)+x]=verts[y*(sides+1)+x].normalized;
        }
        int n=0;
        for(int y=0;y<rings;y++) for(int x=0;x<sides;x++)
        {
            int i=y*(sides+1)+x;
            triangles[n++]=i;triangles[n++]=i+sides+1;triangles[n++]=i+1;
            triangles[n++]=i+1;triangles[n++]=i+sides+1;triangles[n++]=i+sides+2;
        }
        sphereMesh=Mesh("Bunny K small core",verts,triangles);
        sphereMesh.uv=sphereUV;sphereMesh.normals=sphereNormals;

        verts=new Vector3[17]; triangles=new int[48];
        for(int i=0;i<16;i++)
        {
            float a=i*Mathf.PI/8, r=(i%2==0 ? 1 : .22f)*(i%4==0 ? 1 : .75f);
            verts[i+1]=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,0);
            triangles[i*3]=0;triangles[i*3+1]=i+1;triangles[i*3+2]=(i+1)%16+1;
        }
        starMesh=Mesh("Bunny K local contact star",verts,triangles);
        verts=new Vector3[18]; triangles=new int[18];
        for(int i=0;i<6;i++)
        {
            float a=i*Mathf.PI/3;
            Vector3 outwards=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
            Vector3 across=new Vector3(-outwards.y,outwards.x,0)*.035f;
            verts[i*3]=outwards*.68f+across;
            verts[i*3+1]=outwards*.84f;
            verts[i*3+2]=outwards*.68f-across;
            for(int j=0;j<3;j++) triangles[i*3+j]=i*3+j;
        }
        shardsMesh=Mesh("Bunny K six compact shards",verts,triangles);

        verts=new Vector3[26]; triangles=new int[72]; var uv=new Vector2[26];
        verts[0]=new Vector3(.35f,0,0); uv[0]=new Vector2(0,.5f);
        for(int i=0;i<25;i++)
        {
            float t=i/24f,a=Mathf.Lerp(-68f,68f,t)*Mathf.Deg2Rad;
            verts[i+1]=new Vector3(.35f+Mathf.Cos(a)*.40f,Mathf.Sin(a)*.76f,0);
            uv[i+1]=new Vector2(1,t);
            if(i>=24) continue;
            triangles[i*3]=0;triangles[i*3+1]=i+1;triangles[i*3+2]=i+2;
        }
        shieldMesh=Mesh("Bunny K directional shield",verts,triangles);shieldMesh.uv=uv;
    }

    static Mesh Mesh(string name,Vector3[] verts,int[] indices)
    {
        var mesh=new Mesh {name=name,hideFlags=HideFlags.DontSave};
        var colors=new Color[verts.Length];for(int i=0;i<colors.Length;i++)colors[i]=Color.white;
        mesh.vertices=verts;mesh.triangles=indices;mesh.colors=colors;
        mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSharedGeometry()
    {
        DestroySharedGeometry();
        geometryOwners=0;
        energyMaterial=shieldMaterial=null;
    }

    static void DestroySharedGeometry()
    {
        foreach(var mesh in new[]{sphereMesh,starMesh,shieldMesh,shardsMesh})
            if(mesh) Object.Destroy(mesh);
        sphereMesh=starMesh=shieldMesh=shardsMesh=null;
    }
}
