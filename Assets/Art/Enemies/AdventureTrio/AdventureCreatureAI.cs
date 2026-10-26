using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Shared physical/perception/lifecycle services only. Each creature owns its own
// decision graph in a separate component; animation and VFX never apply damage.
[RequireComponent(typeof(Health),typeof(Rigidbody),typeof(CapsuleCollider))]
public abstract class AdventureCreatureAI : MonoBehaviour
{
    public Animator animator;
    public Transform weapon;
    public Transform bodyContact;
    public float bodyContactRadius=.28f;
    public Vector3 weaponBaseLocal,weaponTipLocal;
    public Material impactMaterial;
    public Color warningColor=new Color(.25f,.8f,.85f);
    public float noticeRange=7f,leashRange=9f,walkSpeed=1.8f;
    protected Health health,playerHealth;
    protected Rigidbody body;
    protected CapsuleCollider shape;
    protected EnemyAwareness awareness;
    protected PlayerVitals player;
    protected CharacterController playerBody;
    protected Vector3 home;
    protected Quaternion homeFacing;
    BaselineFlow flow;
    EnemyGroundQuery ground;
    EnemySoundEmitter sounds;
    readonly EnemyNavigationRoute route=new EnemyNavigationRoute();
    readonly RaycastHit[] casts=new RaycastHit[48];
    readonly Collider[] overlaps=new Collider[32];
    readonly Dictionary<string,float> durations=new Dictionary<string,float>();
    float playerSearch,playRate=1,patrolAge,patrolWait=.8f;
    Vector3 patrolPoint,lastA,lastB;
    bool started,patrolGoal,hadSample,strikeSpent=true,strikeOpen,usesWeapon;
    float strikeBegin,strikeEnd,strikeRadius,strikeAge,charge,flash,blockFlash;
    int strikeDamage;
    Vector3 impactPoint;
    string currentAction;
    // Ambient poses share this brain's clock. Perception and accepted hits always
    // interrupt them before any patrol decision; they never create a strike.
    string ambientAction,ambientRest;
    float ambientWait,ambientAge,ambientLength;
    int ambientCycle;
    LineRenderer warning,blockMark;
    SkinnedMeshRenderer hitOverlay;
    MaterialPropertyBlock hitBlock;
    readonly Vector3[] markerPoints=new Vector3[5];
    public abstract string StateName {get;}
    public bool IsDead=>health&&health.IsDead;
    public Vector3 Home=>home;
    public string CurrentAction=>currentAction;
    public bool AmbientIdleActive=>ambientAction!=null;
    public string AmbientIdleAction=>ambientAction;
    public float AmbientIdleAge=>ambientAge;
    public int AmbientIdleCount {get;private set;}
    public int AmbientIdleCompleted {get;private set;}
    public int AmbientIdleInterrupted {get;private set;}
    public float CurrentActionTime=>animator&&animator.isActiveAndEnabled?animator.GetCurrentAnimatorStateInfo(0).normalizedTime:0;
    public int Swings {get;private set;}
    public int Hits {get;private set;}
    public int Contacts {get;private set;}
    public bool HitWindow=>strikeOpen&&!strikeSpent&&CombatActive;
    public bool WarningVisible=>warning&&warning.enabled;
    public float Anticipation01=>Mathf.Clamp01(charge);
    public float VoiceAttackDelay=>Mathf.Max(0,strikeBegin-.08f);
    public EnemyAwareness Awareness=>awareness;
    protected bool Visible=>awareness&&awareness.Visible;
    protected bool Engaged=>awareness&&awareness.Engaged;
    protected Vector3 Target=>Visible&&player?player.transform.position:awareness?awareness.InterestPosition:home;
    protected float Distance=>player?Flat(player.transform.position-body.position):float.PositiveInfinity;
    protected bool CanAcquire=>awareness&&awareness.Ready&&Visible&&WithinHome(Target);
    protected bool CombatActive=>isActiveAndEnabled&&!IsDead&&WorldActive;
    bool WorldActive=>player&&playerHealth&&!playerHealth.IsDead&&player.gameObject.scene==gameObject.scene&&player.EnemyCombatActive;
    public virtual bool ResolveIncoming(ref DamageInfo damage)=>true;
    protected abstract void TickBrain(float dt);
    protected virtual void ResetBrain(){}
    protected virtual void ReceiveHit(DamageInfo damage){}
    protected virtual bool AcceptContact(Vector3 point)=>true;
    protected virtual bool InterruptOnHit(DamageInfo damage)=>true;

    protected virtual void Awake()
    {
        health=GetComponent<Health>();body=GetComponent<Rigidbody>();shape=GetComponent<CapsuleCollider>();hitBlock=new MaterialPropertyBlock();
        body.isKinematic=true;body.useGravity=false;ground=new EnemyGroundQuery(transform);
        awareness=EnemyAwareness.Ensure(this);home=body.position;homeFacing=body.rotation;
        ResetAmbientIdle();
        if(!animator)animator=GetComponentInChildren<Animator>();
        if(animator){animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach(var clip in animator.runtimeAnimatorController.animationClips)durations[clip.name]=clip.length;}
        foreach(var host in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))if(host.gameObject.scene==gameObject.scene){flow=host;break;}
        sounds=EnemySoundEmitter.Ensure(gameObject);GetComponent<CombatFeedback>()?.UseLocalizedEnemyVisuals();
        PrepareCues();FindPlayer();AdventureCreatureVoice.Ensure(gameObject);
    }
    protected virtual void OnEnable()
    {health.Damaged+=Damaged;health.Died+=Died;if(flow)flow.WorldEncountersReset+=ResetForJourney;}
    protected virtual void Start(){started=true;ResetBrain();}
    protected virtual void OnDisable()
    {if(health){health.Damaged-=Damaged;health.Died-=Died;}if(flow)flow.WorldEncountersReset-=ResetForJourney;CancelStrike();if(awareness)awareness.Clear();if(animator)animator.speed=0;if(warning)warning.enabled=false;if(blockMark)blockMark.enabled=false;if(hitOverlay)hitOverlay.enabled=false;if(sounds)sounds.StopAll();}
    void FindPlayer()
    {
        playerSearch=Time.unscaledTime+1f;
        foreach(var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))if(candidate.gameObject.scene==gameObject.scene){player=candidate;break;}
        playerHealth=player?player.GetComponent<Health>():null;playerBody=player?player.GetComponent<CharacterController>():null;
    }
    void Update()
    {
        if(!player&&Time.unscaledTime>=playerSearch)FindPlayer();
        if(animator)animator.speed=WorldActive?playRate:0;
        if(!WorldActive)
        {
            if(awareness&&awareness.CanRetainEncounter)awareness.Suspend();
            else {if(awareness)awareness.Clear();CancelStrike();}
            hadSample=false;if(warning)warning.enabled=false;if(blockMark)blockMark.enabled=false;
        }
    }
    void FixedUpdate()
    {
        if(!started||!CombatActive)return;
        bool canSee=Distance<=noticeRange*1.45f&&WithinHome(player.transform.position)&&HasSight();
        awareness.Tick(player,canSee,noticeRange,Time.fixedDeltaTime);
        if(Visible||Engaged||awareness.Curious)EndAmbientIdle(false,true);
        TickBrain(Time.fixedDeltaTime);
    }
    void LateUpdate()
    {
        if(!WorldActive||Time.deltaTime<=0){hadSample=false;return;}
        Vector3 toward=player?Vector3.ProjectOnPlane(player.transform.position-transform.position,Vector3.up):Vector3.zero;
        bool inFront=toward.sqrMagnitude<.001f||Vector3.Dot(transform.forward,toward.normalized)>.15f;
        if(!IsDead&&strikeOpen&&!strikeSpent&&playerBody&&inFront&&HasSight()&&Contact(out Vector3 point)&&AcceptContact(point))
        {
            strikeSpent=true;Contacts++;
            Vector3 direction=player.transform.position-transform.position;direction.y=0;
            if(playerHealth.TryApplyDamage(new DamageInfo(strikeDamage,gameObject,point,direction))){Hits++;Sound(EnemySoundCue.StoneHit,.65f);}
        }
        lastA=BladeBase;lastB=BladeTip;hadSample=!IsDead;
        flash=Mathf.Max(0,flash-Time.deltaTime);blockFlash=Mathf.Max(0,blockFlash-Time.deltaTime);
        if(hitOverlay){hitOverlay.enabled=!IsDead&&flash>0;hitBlock.SetVector("_ImpactPoint",impactPoint);hitBlock.SetFloat("_ImpactRadius",.6f);hitBlock.SetFloat("_ImpactStrength",flash/.18f);hitBlock.SetColor("_ImpactTint",new Color(1,.83f,.44f));hitOverlay.SetPropertyBlock(hitBlock);}
        if(warning)
        {
            warning.enabled=!IsDead&&charge>.02f&&EnemyPresentationBudget.Details(transform.position);
            warning.startColor=warning.endColor=new Color(warningColor.r,warningColor.g,warningColor.b,charge*.8f);
            if(weapon){warning.positionCount=2;warning.SetPosition(0,BladeBase);warning.SetPosition(1,BladeTip);}
            else {warning.positionCount=25;for(int i=0;i<25;i++){float a=i/24f*Mathf.PI*2;warning.SetPosition(i,transform.position+new Vector3(Mathf.Cos(a),.06f,Mathf.Sin(a))*(shape.radius+.13f));}}
        }
        if(blockMark){blockMark.enabled=blockFlash>0&&!IsDead;Vector3 up=Vector3.up*.22f,right=transform.right*.22f;blockMark.positionCount=5;markerPoints[0]=markerPoints[4]=impactPoint+up;markerPoints[1]=impactPoint+right;markerPoints[2]=impactPoint-up;markerPoints[3]=impactPoint-right;blockMark.SetPositions(markerPoints);}
    }
    protected void Play(string action,float duration=0,bool restart=false,float fade=.10f)
    {
        if(ambientAction!=null&&(action!=ambientAction||restart))EndAmbientIdle(false,false);
        if(!animator||(!restart&&currentAction==action))return;
        if(!durations.TryGetValue(action,out float native)){Debug.LogError("Missing native creature action: "+action,this);return;}
        currentAction=action;playRate=duration>0?native/Mathf.Max(.05f,duration):1;
        animator.speed=WorldActive?playRate:0;
        animator.CrossFadeInFixedTime(action,fade,0,0);
    }
    protected void Stand(string action="IdleBattle"){Play(action);}
    float NextAmbientWait()
    {
        // Stable spatial phase: neighboring actors do not flourish in unison and
        // this does not consume gameplay's shared Random state.
        float phase=Mathf.Sin(home.x*1.731f+home.z*.927f+ambientCycle++*2.37f)*43758.547f;
        return 6f+6f*(phase-Mathf.Floor(phase));
    }
    void ResetAmbientIdle()
    {
        ambientAction=ambientRest=null;ambientAge=ambientLength=0;ambientCycle=0;
        AmbientIdleCount=AmbientIdleCompleted=AmbientIdleInterrupted=0;
        ambientWait=NextAmbientWait();playRate=1;
    }
    void EndAmbientIdle(bool completed,bool restoreRest)
    {
        if(ambientAction==null)return;
        string rest=ambientRest;
        ambientAction=ambientRest=null;ambientAge=ambientLength=0;playRate=1;
        ambientWait=NextAmbientWait();
        if(completed)AmbientIdleCompleted++;else AmbientIdleInterrupted++;
        if(restoreRest)Play(rest,0,false,.12f);
    }
    protected bool AmbientRest(float dt,string rest,string variation)
    {
        if(!CombatActive||Visible||Engaged||(awareness&&awareness.Curious))
        {EndAmbientIdle(false,true);return false;}
        if(ambientAction!=null)
        {
            ambientAge+=dt;
            if(ambientAction==rest)
                playRate=1f-.12f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(ambientAge/ambientLength));
            if(ambientAge>=ambientLength){EndAmbientIdle(true,true);return false;}
            return true;
        }
        ambientWait-=dt;
        if(ambientWait>0||!durations.ContainsKey(variation))return false;
        ambientLength=variation==rest?2.6f:Mathf.Clamp(durations[variation],2f,3f);
        // A planted mushroom keeps its native plant loop and a subtly slower
        // breathing cadence. It does not expose its battle body or change shape.
        if(variation!=rest)Play(variation,ambientLength,true,.16f);
        else Stand(rest);
        ambientAction=variation;ambientRest=rest;ambientAge=0;AmbientIdleCount++;
        return true;
    }
    protected bool WithinHome(Vector3 point)=>Flat(point-home)<=leashRange&&Mathf.Abs(point.y-home.y)<3f;
    protected void Face(Vector3 point,float degrees=300)
    {Vector3 delta=point-body.position;delta.y=0;if(delta.sqrMagnitude>.001f)body.MoveRotation(Quaternion.RotateTowards(body.rotation,Quaternion.LookRotation(delta),degrees*Time.fixedDeltaTime));}
    protected bool MoveTo(Vector3 target,float speed,float stop=0,string anim="WalkFWD")
    {
        Vector3 delta=target-body.position;delta.y=0;float distance=delta.magnitude;
        if(distance<=stop+.04f){Stand();return true;}
        Vector3 direction=delta.normalized;
        bool valid=route.Direction(gameObject.scene,EnemyNavigationWorld.Profile.StoneScout,body.position,target,Time.fixedDeltaTime,out Vector3 nav);
        if(route.Available&&!(distance<2.5f&&Visible&&HasSight())){if(!valid){Stand();return route.WaitingAtEnd;}direction=nav;}
        Face(body.position+direction);
        if(!Step(direction,Mathf.Min(speed*Time.fixedDeltaTime,distance-stop))){route.Blocked(body.position,direction);Stand();return false;}
        Play(anim);return true;
    }
    protected bool Step(Vector3 direction,float distance)
    {
        direction.y=0;if(direction.sqrMagnitude<.001f||distance<=0)return false;
        Vector3 candidate=body.position+direction.normalized*distance;
        if(!WithinHome(candidate)||!ground.TryFootprint(candidate,body.rotation,Vector2.one*(shape.radius*.9f),35,.55f,.75f,out float y,true))return false;
        candidate.y=y+.015f;if(Mathf.Abs(candidate.y-body.position.y)>.42f)return false;
        float radius=shape.radius,half=Mathf.Max(0,shape.height*.5f-radius);
        Vector3 centre=candidate+shape.center;
        int n=Physics.OverlapCapsuleNonAlloc(centre-Vector3.up*half,centre+Vector3.up*half,radius+.015f,overlaps,~0,QueryTriggerInteraction.Ignore);
        if(n==overlaps.Length)return false;
        for(int i=0;i<n;i++)if(overlaps[i]&&!overlaps[i].transform.IsChildOf(transform)&&!ground.AllowsSupportContact(overlaps[i],candidate))return false;
        Vector3 from=body.position+shape.center,travel=candidate-body.position;
        n=Physics.CapsuleCastNonAlloc(from-Vector3.up*half,from+Vector3.up*half,radius,travel.normalized,casts,travel.magnitude+.01f,~0,QueryTriggerInteraction.Ignore);
        if(n==casts.Length)return false;
        for(int i=0;i<n;i++)if(casts[i].collider&&!casts[i].collider.transform.IsChildOf(transform)&&!(ground.IsSurface(casts[i].collider)&&casts[i].normal.y>.82f))return false;
        body.MovePosition(candidate);return true;
    }
    protected void Patrol(float dt,float radius=1,string ambientVariation=null)
    {
        patrolAge+=dt;
        if(awareness.Curious){Stand();Face(awareness.InterestPosition,140);return;}
        if(patrolWait<=0&&!AmbientIdleActive&&ambientVariation!=null&&!Visible&&!Engaged)
            ambientWait-=dt;
        if(patrolWait>0||AmbientIdleActive)
        {
            if(ambientVariation!=null&&AmbientRest(dt,"IdleNormal",ambientVariation))return;
            patrolWait-=dt;Stand("IdleNormal");return;
        }
        if(!patrolGoal){float a=patrolAge*.73f+(GetInstanceID()%11);patrolPoint=home+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)) *radius;patrolGoal=true;}
        bool arrived=Flat(body.position-patrolPoint)<.15f;
        if(arrived||!MoveTo(patrolPoint,walkSpeed*.45f,.1f)||route.WaitingAtEnd)
        {
            // NavMesh destinations are safely sampled onto the reachable shore.
            // Its endpoint may differ from the original random patrol point;
            // waiting there is arrival, not an infinite IdleBattle travel leg.
            patrolGoal=false;patrolWait=1.6f;route.Clear();Stand("IdleNormal");
        }
    }
    protected bool ReturnHome(float dt)
    {
        if(Flat(body.position-home)>.15f){MoveTo(home,walkSpeed,.08f);return false;}
        awareness.Clear();CancelStrike();Face(home+homeFacing*Vector3.forward);Stand("IdleNormal");return true;
    }
    protected void SetStrike(int damage,float open,float close,bool weapon=true,float radius=.18f)
    {strikeDamage=damage;strikeBegin=open;strikeEnd=close;usesWeapon=weapon;strikeRadius=radius;strikeSpent=false;strikeOpen=false;hadSample=false;Swings++;}
    protected void TickStrike(float elapsed){strikeAge=elapsed;strikeOpen=elapsed>=strikeBegin&&elapsed<=strikeEnd&&!strikeSpent;}
    protected void CancelStrike(){EndAmbientIdle(false,false);strikeOpen=false;strikeSpent=true;hadSample=false;Cue(0);if(sounds)sounds.StopWarning();}
    protected void Cue(float amount){charge=Mathf.Clamp01(amount);}
    protected void Sound(EnemySoundCue cue,float gain=1,float seconds=0){if(sounds)sounds.Cue(cue,gain,seconds);}
    protected void BlockFeedback(Vector3 point){impactPoint=point;blockFlash=.14f;Sound(EnemySoundCue.ShellHit,.85f);}
    Vector3 BladeBase=>weapon?weapon.TransformPoint(weaponBaseLocal):bodyContact?bodyContact.position:shape.bounds.center;
    Vector3 BladeTip=>weapon?weapon.TransformPoint(weaponTipLocal):bodyContact?bodyContact.position:shape.bounds.center+transform.forward*shape.radius;
    bool Contact(out Vector3 point)
    {
        point=default;
        if(!usesWeapon)
        {
            Vector3 centre=bodyContact?bodyContact.position:shape.bounds.center;
            float radius=bodyContact?bodyContactRadius+strikeRadius:strikeRadius;
            point=playerBody.ClosestPoint(centre);
            if(bodyContact&&(point-centre).sqrMagnitude<=radius*radius&&ClearPath(centre,point))return true;
            if(!bodyContact)return (shape.ClosestPoint(point)-point).sqrMagnitude<=radius*radius;
            Vector3 travel=centre-lastA;
            if(!hadSample||travel.sqrMagnitude<.000001f)return false;
            int n=Physics.SphereCastNonAlloc(lastA,radius,travel.normalized,casts,travel.magnitude,~0,QueryTriggerInteraction.Ignore);
            if(n==casts.Length)return false;
            float nearest=float.PositiveInfinity;Collider first=null;
            for(int i=0;i<n;i++)if(casts[i].collider&&!casts[i].collider.transform.IsChildOf(transform)&&casts[i].distance<nearest)
            {nearest=casts[i].distance;first=casts[i].collider;point=casts[i].point;}
            return first&&first.transform.IsChildOf(player.transform)&&ClearPath(lastA,point);
        }
        if(!weapon)return false;
        for(int i=0;i<=10;i++)
        {
            float t=i/10f;Vector3 sample=Vector3.Lerp(BladeBase,BladeTip,t);Vector3 closest=playerBody.ClosestPoint(sample);
            if((sample-closest).sqrMagnitude<=strikeRadius*strikeRadius&&ClearPath(sample,closest)){point=closest;return true;}
            if(!hadSample)continue;
            Vector3 previous=Vector3.Lerp(lastA,lastB,t),travel=sample-previous;
            if(travel.sqrMagnitude<.000001f)continue;
            int n=Physics.SphereCastNonAlloc(previous,strikeRadius,travel.normalized,casts,travel.magnitude,~0,QueryTriggerInteraction.Ignore);
            if(n==casts.Length)continue;
            float near=float.PositiveInfinity;Collider first=null;Vector3 hit=default;
            for(int j=0;j<n;j++)if(casts[j].collider&&!casts[j].collider.transform.IsChildOf(transform)&&casts[j].distance<near){near=casts[j].distance;first=casts[j].collider;hit=casts[j].point;}
            if(first&&first.transform.IsChildOf(player.transform)&&ClearPath(previous,hit)){point=hit;return true;}
        }
        return false;
    }
    bool HasSight()=>playerBody&&ClearPath(shape.bounds.center,playerBody.bounds.center);
    bool ClearPath(Vector3 from,Vector3 to)
    {
        Vector3 delta=to-from;if(delta.sqrMagnitude<.000001f)return true;
        int n=Physics.RaycastNonAlloc(from,delta.normalized,casts,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
        if(n==casts.Length)return false;
        for(int i=0;i<n;i++)if(casts[i].collider&&!casts[i].collider.transform.IsChildOf(transform)&&!casts[i].collider.transform.IsChildOf(player.transform))return false;
        return true;
    }
    void Damaged(Health actor,DamageInfo damage)
    {if(actor.IsDead)return;if(InterruptOnHit(damage))CancelStrike();impactPoint=damage.HitPoint;flash=.18f;ReceiveHit(damage);}
    void Died(Health actor,DamageInfo damage)
    {CancelStrike();awareness.Clear();shape.enabled=false;flash=blockFlash=0;Play("Die",0,true,.04f);}
    public void ResetForJourney()
    {
        if(!body)return;CancelStrike();route.Clear();GetComponent<StoneScoutLootDrop>()?.ClearDrop();
        body.position=home;body.rotation=homeFacing;health.RestoreFullHealth();shape.enabled=true;flash=blockFlash=0;awareness.Clear();
        patrolGoal=false;patrolWait=.8f;currentAction=null;GetComponent<CombatFeedback>()?.ClearFeedback();ResetBrain();
        ResetAmbientIdle();
        GetComponent<AdventureCreatureStyle>()?.ResetForJourney();
    }
    void PrepareCues()
    {
        warning=Line("Native creature anticipation",warningColor,.025f);blockMark=Line("Shield contact mark",new Color(1,.78f,.25f),.025f);
        var source=GetComponentInChildren<SkinnedMeshRenderer>();
        if(source&&impactMaterial)
        {
            var go=new GameObject("Skinned creature local hit patch");go.transform.SetParent(source.transform,false);hitOverlay=go.AddComponent<SkinnedMeshRenderer>();
            hitOverlay.sharedMesh=source.sharedMesh;hitOverlay.bones=source.bones;hitOverlay.rootBone=source.rootBone;hitOverlay.localBounds=source.localBounds;
            var mats=new Material[source.sharedMesh.subMeshCount];for(int i=0;i<mats.Length;i++)mats[i]=impactMaterial;hitOverlay.sharedMaterials=mats;hitOverlay.enabled=false;hitOverlay.shadowCastingMode=ShadowCastingMode.Off;
        }
    }
    LineRenderer Line(string name,Color tint,float width)
    {
        var go=new GameObject(name);go.transform.SetParent(transform,false);var line=go.AddComponent<LineRenderer>();line.sharedMaterial=EnemyAttackPresentation.EnergyMaterial;
        line.useWorldSpace=true;line.startWidth=line.endWidth=width;line.startColor=line.endColor=tint;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;return line;
    }
    protected static float Flat(Vector3 value)=>new Vector2(value.x,value.z).magnitude;
}
