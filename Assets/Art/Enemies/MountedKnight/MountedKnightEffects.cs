using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Cosmetic timing follows the same source clock used by the damage windows.</summary>
[DisallowMultipleComponent]
public sealed class MountedKnightEffects : MonoBehaviour
{
    public Material energyMaterial;
    public Transform bladeTip,bladeGuard;
    public int ImpactBursts { get; private set; }
    public readonly HashSet<int> ObservedEffectMoves=new HashSet<int>();
    public int DeathBursts { get; private set; }
    public bool TelegraphVisible => warning && warning.enabled;
    public bool TrailVisible => trail && trail.emitting;
    MountedKnightBrain brain; MountedKnightMotion motion; Health health;
    LineRenderer warning; TrailRenderer trail; ParticleSystem motes;
    Transform[] feet; bool dying;float deathClock,hoofClock;int deathStage;
    Material ownedMaterial;
    MountedKnightBladeBinding bladeBinding;
    Mesh bladeRibbon; MeshRenderer ribbonRenderer; MountedKnightLayeredVfx layered;
    readonly List<Vector3> ribbonTips=new List<Vector3>(),ribbonGuards=new List<Vector3>();
    readonly List<float> ribbonTimes=new List<float>();
    float chargeFxClock; bool wasCharging; bool sampledSwing; int ribbonAttack=-99;
    // Cleave, combo, stomp, execution, rising cut, thrust, hoof ward, charge.
    static readonly float[] RibbonLife={.16f,.12f,0,.19f,.145f,.075f,0,.1f};
    static readonly float[] RibbonRoot={.28f,.42f,1,.22f,.37f,.80f,1,.66f};
    static readonly Color[] RibbonColors={new Color(.16f,.75f,1,.85f),new Color(.28f,.9f,1,.8f),Color.clear,new Color(1,.66f,.24f,.95f),new Color(.12f,.85f,.88f,.85f),new Color(.65f,.92f,1,.95f),Color.clear,new Color(.42f,.82f,1,.8f)};
    public int ChargeBursts { get; private set; }
    public bool RibbonVisible => ribbonRenderer && ribbonRenderer.enabled;
    readonly Color cyan=new Color(.16f,.8f,1,.75f),gold=new Color(1,.7f,.24f,.8f);
    readonly List<Pulse> pulses=new List<Pulse>();
    sealed class Pulse {public LineRenderer line;public float age,duration,radius;public Vector3 center;public Quaternion rotation;public bool ground;public float arc;public Color color;}
    void Awake(){
        brain=GetComponent<MountedKnightBrain>();motion=GetComponent<MountedKnightMotion>();health=GetComponent<Health>();
        if(!energyMaterial){var shader=Shader.Find("Warden/Boundary Energy");if(!shader){enabled=false;return;}ownedMaterial=new Material(shader);energyMaterial=ownedMaterial;}
        warning=NewLine("Attack anticipation",.035f);warning.enabled=false;
        for(int i=0;i<14;i++){var l=NewLine("Pooled boundary pulse",.04f);l.enabled=false;pulses.Add(new Pulse{line=l});}
        if(bladeTip){trail=bladeTip.gameObject.AddComponent<TrailRenderer>();trail.sharedMaterial=energyMaterial;trail.time=.065f;trail.minVertexDistance=.025f;trail.widthMultiplier=.045f;trail.widthCurve=AnimationCurve.Linear(0,1,1,0);trail.colorGradient=Gradient(cyan,new Color(1,.9f,.55f,0));trail.emitting=false;trail.shadowCastingMode=ShadowCastingMode.Off;trail.receiveShadows=false;}
        var all=GetComponentsInChildren<Transform>();var list=new List<Transform>();foreach(var t in all)if(t.name=="Bip02_L_Foot"||t.name=="Bip02_R_Foot"||t.name=="Bip02_L_Finger0"||t.name=="Bip02_R_Finger0")list.Add(t);feet=list.ToArray();
        var go=new GameObject("Boundary sparks (pooled)");go.transform.SetParent(transform,false);motes=go.AddComponent<ParticleSystem>();motes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=motes.main;main.playOnAwake=false;main.loop=false;main.maxParticles=220;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startLifetime=.6f;main.startSize=.055f;main.gravityModifier=.18f;
        var emission=motes.emission;emission.enabled=false;var shape=motes.shape;shape.enabled=false;
        var col=motes.colorOverLifetime;col.enabled=true;col.color=Gradient(Color.white,new Color(1,1,1,0));
        var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=energyMaterial;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        var ribbonObject=new GameObject("Blade swept energy ribbon");ribbonObject.transform.SetParent(transform,false);
        bladeRibbon=new Mesh{name="Warden blade sweep"};bladeRibbon.MarkDynamic();ribbonObject.AddComponent<MeshFilter>().sharedMesh=bladeRibbon;
        ribbonRenderer=ribbonObject.AddComponent<MeshRenderer>();ribbonRenderer.sharedMaterial=energyMaterial;ribbonRenderer.shadowCastingMode=ShadowCastingMode.Off;ribbonRenderer.receiveShadows=false;ribbonRenderer.enabled=false;
        layered=gameObject.AddComponent<MountedKnightLayeredVfx>();layered.Initialize();if(layered.RibbonMaterial)ribbonRenderer.sharedMaterial=layered.RibbonMaterial;
        bladeBinding=new MountedKnightBladeBinding();if(bladeTip&&bladeGuard&&bladeBinding.Initialize(transform)){bladeTip.SetParent(transform,true);bladeGuard.SetParent(transform,true);bladeBinding.Sample(bladeTip,bladeGuard);}else Debug.LogError("Warden blade binding requires readable skinned sword geometry",this);
        health.Damaged+=Damaged;
    }
    static Gradient Gradient(Color start,Color end){var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(start,0),new GradientColorKey(end,1)},new[]{new GradientAlphaKey(start.a,0),new GradientAlphaKey(end.a,1)});return g;}
    LineRenderer NewLine(string name,float width){var o=new GameObject(name);o.transform.SetParent(transform,false);var l=o.AddComponent<LineRenderer>();l.useWorldSpace=true;l.sharedMaterial=energyMaterial;l.widthMultiplier=width;l.numCapVertices=3;l.numCornerVertices=2;l.shadowCastingMode=ShadowCastingMode.Off;l.receiveShadows=false;l.textureMode=LineTextureMode.Stretch;return l;}
    void Update(){
        if(!brain||!motion)return;
        float dt=Time.deltaTime;
        foreach(var p in pulses){if(!p.line.enabled)continue;p.age+=dt;float t=p.age/p.duration;if(t>=1){p.line.enabled=false;continue;}float r=p.radius*(p.ground?Mathf.Lerp(.2f,1,t):Mathf.Lerp(.85f,1.08f,t));Draw(p.line,p.center,p.rotation,r,p.arc,p.ground,1-t);p.line.startColor=p.line.endColor=new Color(p.color.r,p.color.g,p.color.b,p.color.a*(1-t));}
        if(dying){
            deathClock+=dt;
            if(deathStage==0&&deathClock>=1.8f){deathStage++;DeathBursts++;PulseAt(transform.position+Vector3.up*.09f,3.1f,1.1f,true,360,cyan);Sparks(transform.position+Vector3.up*.55f,36,1.8f,cyan);}
            if(deathStage==1&&deathClock>=3.2f){deathStage++;DeathBursts++;PulseAt(transform.position+Vector3.up*.12f,2.1f,1.4f,true,360,gold);Sparks(transform.position+Vector3.up*.4f,24,1,gold);}
            return;
        }
        bool attack=brain.State==MountedKnightBrain.Mode.Attack,charge=brain.State==MountedKnightBrain.Mode.Charge;float time=(float)motion.SourceTime;float impact=charge?.8f:brain.NextImpact;
        bool windup=(attack||charge)&&time<impact;
        warning.enabled=windup;
        if(windup){float radius=brain.TelegraphRange;Draw(warning,transform.position+Vector3.up*.07f,transform.rotation,radius,brain.AttackIsRadial?360:174,true,.38f+.35f*Mathf.Clamp01(time/Mathf.Max(.01f,impact)));warning.startColor=warning.endColor=gold;}
        if(trail){bool slash=(attack&&brain.BladeWindow)||(charge&&time>2.36f&&time<2.83f);trail.emitting=slash;if(!slash&&motion.ActiveClip<3&&trail.positionCount>0)trail.Clear();}
        bool rushing=charge&&time>.8f&&time<2.35f;
        if(rushing){
            chargeFxClock-=dt;
            if(!wasCharging){ChargeBursts++;if(layered)layered.Dust(transform.position,14,.9f);PulseAt(transform.position+Vector3.up*.09f,1.25f,.36f,true,360,gold);Sparks(transform.position+Vector3.up*.2f,18,1.2f,cyan);}
            if(chargeFxClock<=0){chargeFxClock=.09f;for(int side=-1;side<=1;side+=2){Vector3 at=transform.position+transform.right*side*.65f+Vector3.up*.55f;Sparks(at,3,.35f,cyan);} }
        }
        wasCharging=rushing;
        if(rushing){hoofClock-=dt;if(hoofClock<=0){hoofClock=.16f;if(layered)layered.Dust(transform.position,2,.45f);foreach(var foot in feet)if(foot.position.y<transform.position.y+.45f)Sparks(foot.position,2,.45f,new Color(.44f,.8f,.83f,.4f));}}
    }
    void Draw(LineRenderer line,Vector3 center,Quaternion rotation,float radius,float arc,bool ground,float alpha){
        const int count=57;line.positionCount=count;line.startColor=line.endColor=new Color(cyan.r,cyan.g,cyan.b,Mathf.Clamp01(alpha));
        for(int i=0;i<count;i++){float angle=(-arc*.5f+arc*i/(count-1))*Mathf.Deg2Rad;Vector3 p=ground?new Vector3(Mathf.Sin(angle)*radius,0,Mathf.Cos(angle)*radius):new Vector3(Mathf.Sin(angle)*radius,Mathf.Cos(angle)*radius*.25f,Mathf.Cos(angle)*radius*.7f);line.SetPosition(i,center+rotation*p);}
    }
    void PulseAt(Vector3 center,float radius,float duration,bool ground,float arc,Color color){
        Pulse slot=null;foreach(var p in pulses)if(!p.line.enabled){slot=p;break;}if(slot==null)return;
        slot.color=color;slot.center=center;slot.rotation=transform.rotation;slot.radius=radius;slot.duration=duration;slot.age=0;slot.arc=arc;slot.ground=ground;slot.line.enabled=true;slot.line.startColor=slot.line.endColor=color;
    }
    void Sparks(Vector3 position,int count,float speed,Color color){
        if(!motes)return;motes.Play();
        for(int i=0;i<count;i++){var e=new ParticleSystem.EmitParams{position=position,velocity=(Random.insideUnitSphere+Vector3.up*.7f)*speed,startColor=color,startSize=Random.Range(.025f,.065f),startLifetime=Random.Range(.28f,.85f)};motes.Emit(e,1);}
    }
    public void Impact(bool radial){
        if(dying)return;ImpactBursts++;
        bool charge=brain.State==MountedKnightBrain.Mode.Charge;ObservedEffectMoves.Add(charge?7:brain.CurrentAttack);
        if(radial){
            // Front-most hoof contacts, projected onto the actual terrain.
            var sorted=new List<Transform>(feet);sorted.Sort((a,b)=>Vector3.Dot(b.position-transform.position,transform.forward).CompareTo(Vector3.Dot(a.position-transform.position,transform.forward)));
            int contacts=brain.CurrentAttack==2?Mathf.Min(2,sorted.Count):Mathf.Min(1,sorted.Count);
            for(int i=0;i<contacts;i++){
                Vector3 at=sorted[i].position;if(brain.territory.Ground(at,out var floor))at=floor;
                if(layered)layered.Strike(at,transform.rotation,brain.CurrentAttack,true,1);
                Sparks(at+Vector3.up*.06f,brain.CurrentAttack==2?12:7,brain.CurrentAttack==2?.9f:.55f,gold);
            }
        }else{
            if(layered)layered.Strike(bladeTip?bladeTip.position:transform.position,transform.rotation,charge?7:brain.CurrentAttack,false,0);
            // Air displacement at the actual tip; contact sparks are reserved for damage.
            if(bladeTip)Sparks(bladeTip.position,charge?3:brain.CurrentAttack==3?6:3,.35f,charge?cyan:RibbonColors[brain.CurrentAttack]);
        }
    }
    void LateUpdate(){
        if(!ribbonRenderer)return;
        if(bladeBinding!=null&&bladeTip&&bladeGuard)bladeBinding.Sample(bladeTip,bladeGuard);
        bool active=!dying&&trail&&trail.emitting&&bladeGuard&&bladeTip;
        int attack=brain.State==MountedKnightBrain.Mode.Charge?7:Mathf.Clamp(brain.CurrentAttack,0,6);
        float life=RibbonLife[attack];
        if(life<=0){ClearRibbon();sampledSwing=false;return;}
        // Reset between separate combo strokes and when the move changes.
        if(active&&(!sampledSwing||ribbonAttack!=attack)){ClearRibbon();ribbonAttack=attack;}
        sampledSwing=active;
        if(layered&&layered.RibbonMaterial)layered.RibbonMaterial.SetColor("_Tint",RibbonColors[attack]);
        while(ribbonTimes.Count>0&&(Time.time-ribbonTimes[0]>life||ribbonTimes.Count>24)){ribbonTimes.RemoveAt(0);ribbonTips.RemoveAt(0);ribbonGuards.RemoveAt(0);}
        if(active&&ribbonTips.Count>0&&Vector3.Distance(ribbonTips[ribbonTips.Count-1],bladeTip.position)>Vector3.Distance(bladeGuard.position,bladeTip.position)*2.5f)ClearRibbon();
        if(active){ribbonTimes.Add(Time.time);ribbonTips.Add(bladeTip.position);ribbonGuards.Add(Vector3.Lerp(bladeGuard.position,bladeTip.position,RibbonRoot[attack]));}
        sampledSwing=active;int count=ribbonTimes.Count;ribbonRenderer.enabled=count>1;if(count<2)return;
        var vertices=new Vector3[count*2];var colors=new Color[count*2];var uv=new Vector2[count*2];var triangles=new int[(count-1)*6];
        for(int i=0;i<count;i++){
            float fade=Mathf.Clamp01(1-(Time.time-ribbonTimes[i])/life);vertices[2*i]=transform.InverseTransformPoint(ribbonGuards[i]);vertices[2*i+1]=transform.InverseTransformPoint(ribbonTips[i]);
            colors[2*i]=new Color(cyan.r,cyan.g,cyan.b,fade*.48f);colors[2*i+1]=new Color(.45f,.92f,1,fade*.95f);uv[2*i]=new Vector2((float)i/(count-1),0);uv[2*i+1]=new Vector2((float)i/(count-1),1);
            if(i<count-1){int k=i*6,v=i*2;triangles[k]=v;triangles[k+1]=v+1;triangles[k+2]=v+2;triangles[k+3]=v+1;triangles[k+4]=v+3;triangles[k+5]=v+2;}
        }
        bladeRibbon.Clear();bladeRibbon.vertices=vertices;bladeRibbon.colors=colors;bladeRibbon.uv=uv;bladeRibbon.triangles=triangles;bladeRibbon.RecalculateBounds();
    }
    void ClearRibbon(){sampledSwing=false;ribbonTimes.Clear();ribbonTips.Clear();ribbonGuards.Clear();if(ribbonRenderer)ribbonRenderer.enabled=false;}
    void Damaged(Health h,DamageInfo damage){if(!dying)Sparks(damage.HitPoint,7,1,cyan);}
    public void BeginDeath(){if(layered)layered.Clear();ClearRibbon();dying=true;deathClock=0;deathStage=0;warning.enabled=false;if(trail){trail.emitting=false;trail.Clear();}}
    public void CancelAttack(){if(layered)layered.Clear();ClearRibbon();wasCharging=false;chargeFxClock=0;if(warning)warning.enabled=false;if(trail){trail.emitting=false;trail.Clear();}}
    public void ResetEffects(){dying=false;deathClock=0;deathStage=0;ImpactBursts=DeathBursts=ChargeBursts=0;CancelAttack();foreach(var p in pulses)p.line.enabled=false;if(motes)motes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
    void OnDisable(){CancelAttack();foreach(var p in pulses)if(p.line)p.line.enabled=false;if(motes)motes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
    void OnDestroy(){if(bladeRibbon)Destroy(bladeRibbon);if(health)health.Damaged-=Damaged;if(ownedMaterial)Destroy(ownedMaterial);}
}
