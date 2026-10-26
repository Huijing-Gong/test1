using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

// Cosmetic companion: follows a scene-local player, never intercepts combat or physics.
public sealed class PaimonCompanion : MonoBehaviour
{
    public Transform target, visual;
    BaselineFlow hostFlow;
    [Range(.5f,1.5f)] public float followDistance=.62f;
    [Range(.2f,2)] public float idlePause=.55f;
    public PaimonMotion.Action IdleAction { get; private set; }
    public int IdleActionsStarted { get; private set; }
    public int RandomActionsStartedWhileMoving {get;private set;}
    public float IdleActionBlend => idleBlend;
    static readonly PaimonMotion.Action[] IdleCycle={PaimonMotion.Action.JaizyOi,PaimonMotion.Action.Twirl,PaimonMotion.Action.LookAround,PaimonMotion.Action.SmileSway,PaimonMotion.Action.DrowsyRecover,PaimonMotion.Action.ArcSweep,PaimonMotion.Action.RiseDip,PaimonMotion.Action.PlayfulTurn,PaimonMotion.Action.PartnerPeek,PaimonMotion.Action.ShoulderPeek,PaimonMotion.Action.LeadAndReturn,PaimonMotion.Action.StarFigureEight,PaimonMotion.Action.MoonSwoop,PaimonMotion.Action.HeartBounce,PaimonMotion.Action.CometSpin};
    public static IReadOnlyList<PaimonMotion.Action> RandomActions => IdleCycle;
    float idleWait,idleTime,idleBlend;int idleIndex=-1;bool idlePerforming;
    readonly List<int> actionBag=new List<int>(),flourishBag=new List<int>();int ambientSelection;
    public int AmbientActionsStarted {get;private set;}public int AmbientFlourishesStarted {get;private set;}
    public static bool IsAmbientFlourish(PaimonMotion.Action action)=>action==PaimonMotion.Action.StarFigureEight||action==PaimonMotion.Action.MoonSwoop||action==PaimonMotion.Action.HeartBounce||action==PaimonMotion.Action.CometSpin;
    int NextRandomAction()
    {
        // Two effect flourishes per ordinary gesture; shuffle each bag independently.
        bool special=ambientSelection++%3!=1;var bag=special?flourishBag:actionBag;
        if(bag.Count==0){for(int i=0;i<IdleCycle.Length;i++)if(IsAmbientFlourish(IdleCycle[i])==special)bag.Add(i);for(int i=bag.Count-1;i>0;i--){int j=Random.Range(0,i+1);int swap=bag[i];bag[i]=bag[j];bag[j]=swap;}if(bag[0]==idleIndex&&bag.Count>1){int swap=bag[0];bag[0]=bag[1];bag[1]=swap;}}
        int next=bag[0];bag.RemoveAt(0);return next;
    }

    public bool Equipped { get; private set; }
    public bool Visible => visual && visual.gameObject.activeSelf;
    public bool IsMoving { get; private set; }
    public string Motion { get; private set; }
    readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
    readonly Dictionary<string, Quaternion> rest = new Dictionary<string, Quaternion>();
    Vector3 velocity, previous;
    Quaternion followRotation;
    public float GestureYaw => IdleAction==PaimonMotion.Action.Twirl?TwirlYaw(idleTime):IdleAction==PaimonMotion.Action.CometSpin?360*GestureEase((idleTime-.65f)/4f):0;
    public static float GestureEase(float p){p=Mathf.Clamp01(p);return p*p*p*(p*(p*6-15)+10);}
    public static float TwirlYaw(float time){float p=Mathf.Clamp01((time-.7f)/4.6f);return 360*p*p*p*(p*(p*6-15)+10);}
    float phase, visibility, orbit;
    PaimonMotion motion;
    PaimonOrbitInteraction circle;
    float flyBlend,flightTime,smoothedSpeed,bank,brake,previousYaw;
    public int FlightVariation {get;private set;}
    public int FlightVariationsStarted {get;private set;}
    float variationWait;
    Vector3 flightMood;
    void AdvanceFlightVariation(float dt)
    {
        if(IsMoving){variationWait-=dt;if(variationWait<=0){FlightVariation=(FlightVariation+Random.Range(1,4))%4;FlightVariationsStarted++;variationWait=Random.Range(2.4f,3.8f);}}
        else variationWait=0;
        Vector3 goal=FlightVariation==1?new Vector3(1,0,.35f):FlightVariation==2?new Vector3(.15f,1,0):FlightVariation==3?new Vector3(.7f,.15f,1):Vector3.zero;
        flightMood=Vector3.Lerp(flightMood,goal,1-Mathf.Exp(-dt*4));
    }
    Vector3 gestureDrift,gestureDriftVelocity;
    public Vector3 GestureDrift => gestureDrift;
    public static Vector3 ActionDrift(PaimonMotion.Action action,float time)
    {
        float duration=PaimonMotion.Duration(action);
        float envelope=Mathf.SmoothStep(0,1,Mathf.Clamp01(time/.9f))*Mathf.SmoothStep(0,1,Mathf.Clamp01((duration-time)/1.1f));
        float p=Mathf.Clamp01(time/duration),arc=Mathf.Sin(p*Mathf.PI);
        Vector3 offset=Vector3.zero;
        switch(action)
        {
            case PaimonMotion.Action.StarFigureEight:offset=new Vector3(.52f*Mathf.Sin(p*Mathf.PI*2),.16f*arc,.30f*Mathf.Sin(p*Mathf.PI*4));break;
            case PaimonMotion.Action.MoonSwoop:offset=new Vector3(.28f*Mathf.Sin(p*Mathf.PI*2),.48f*arc,.26f*arc);break;
            case PaimonMotion.Action.HeartBounce:offset=new Vector3(.29f*Mathf.Sin(p*Mathf.PI*3),(.16f+.18f*Mathf.Pow(Mathf.Sin(p*Mathf.PI*2),2))*arc,.14f*arc);break;
            case PaimonMotion.Action.CometSpin:offset=new Vector3(.38f*Mathf.Sin(p*Mathf.PI*2),.23f*arc,.32f*arc);break;
            case PaimonMotion.Action.Surprise:offset=new Vector3(0,.13f*arc,-.14f*arc);break;
            case PaimonMotion.Action.Joy:offset=new Vector3(.12f*Mathf.Sin(p*Mathf.PI*4),.16f*arc+.025f*Mathf.Sin(time*5)*arc,0);break;
            case PaimonMotion.Action.ArcSweep:offset=new Vector3(.44f*arc,.10f*arc,.28f*Mathf.Sin(p*Mathf.PI*2));break;
            case PaimonMotion.Action.RiseDip:offset=new Vector3(.12f*Mathf.Sin(p*Mathf.PI*2),.38f*arc-.08f*Mathf.Sin(p*Mathf.PI*2),.12f*arc);break;
            case PaimonMotion.Action.PlayfulTurn:offset=new Vector3(.30f*arc,.16f*arc,.24f*Mathf.Sin(p*Mathf.PI*2));break;
            case PaimonMotion.Action.LookAround:offset=new Vector3(.10f*Mathf.Sin(p*Mathf.PI*2),.05f*arc,0);break;
            case PaimonMotion.Action.SmileSway:offset=new Vector3(.08f*Mathf.Sin(p*Mathf.PI*2),.12f*arc,0);break;
            case PaimonMotion.Action.DrowsyRecover:offset=new Vector3(.04f*Mathf.Sin(p*Mathf.PI*2),-.07f*Mathf.Sin(p*Mathf.PI*2),0);break;
            case PaimonMotion.Action.JaizyOi:offset=new Vector3(0,.10f*arc,0);break;
            case PaimonMotion.Action.Twirl:offset=new Vector3(.16f*Mathf.Sin(p*Mathf.PI*2),.18f*arc,.12f*Mathf.Sin(p*Mathf.PI));break;
            case PaimonMotion.Action.Airplane:offset=new Vector3(.24f*Mathf.Sin(p*Mathf.PI*2),.15f*arc,.15f*arc);break;
            case PaimonMotion.Action.Celebrate:case PaimonMotion.Action.Happy:offset=new Vector3(.08f*Mathf.Sin(time*3),.25f*arc+.045f*Mathf.Sin(time*5),.06f*arc);break;
            case PaimonMotion.Action.Sway:offset=new Vector3(.22f*Mathf.Sin(time*2.2f),.09f*Mathf.Sin(time*4.4f),.07f*arc);break;
            case PaimonMotion.Action.LookBack:case PaimonMotion.Action.Curious:offset=new Vector3(.17f*arc,.12f*arc,-.16f*arc);break;
            case PaimonMotion.Action.Crown:offset=new Vector3(.08f*arc,.18f*arc,.05f*arc);break;
            case PaimonMotion.Action.Drowsy:case PaimonMotion.Action.Sleepy:offset=new Vector3(.10f*Mathf.Sin(time*1.3f),-.13f*arc,.06f*arc);break;
        }
        return offset*envelope;
    }
    public float FlightBlend => flyBlend;
    public float HorizontalTargetSpeed => smoothedSpeed;
    public static PaimonCompanion FindFor(Transform owner)
    {foreach(var c in FindObjectsByType<PaimonCompanion>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(c.target==owner)return c;return null;}
    void Start(){hostFlow=FindFirstObjectByType<BaselineFlow>(FindObjectsInactive.Include);if(!GetComponent<CompanionWardrobe>())gameObject.AddComponent<CompanionWardrobe>();if(target){transform.SetParent(null,true);previous=target.position;previousYaw=transform.eulerAngles.y;followRotation=transform.rotation;if(!GetComponent<PaimonGameplayReactions>())gameObject.AddComponent<PaimonGameplayReactions>();if(!GetComponent<PaimonEmotionEffects>())gameObject.AddComponent<PaimonEmotionEffects>();if(!target.GetComponent<PaimonHeroGlance>())target.gameObject.AddComponent<PaimonHeroGlance>();}}

    Animator heroAnimator;Transform heroHead;
    public Vector3 FacePosition => motion?motion.FacePosition:transform.position;
    public float InteractionBlend => IsPartnerAction(IdleAction)?idleBlend:0;
    public static bool IsPartnerAction(PaimonMotion.Action action)=>action==PaimonMotion.Action.PartnerPeek||action==PaimonMotion.Action.ShoulderPeek||action==PaimonMotion.Action.LeadAndReturn||action==PaimonMotion.Action.Concern;
    public Vector3 HeroEyePosition
    {
        get{if(!heroHead&&target){if(!heroAnimator)heroAnimator=target.GetComponentInChildren<Animator>();if(heroAnimator&&heroAnimator.isHuman)heroHead=heroAnimator.GetBoneTransform(HumanBodyBones.Head);}return heroHead?heroHead.position+Vector3.up*.03f:target.position+Vector3.up*1.5f;}
    }
    public bool ReactionPlaying => idlePerforming && IsReaction(IdleAction);
    public static bool IsReaction(PaimonMotion.Action action)=>action==PaimonMotion.Action.Surprise||action==PaimonMotion.Action.Joy||action==PaimonMotion.Action.Concern;
    public bool TryPlayReaction(PaimonMotion.Action action)
    {
        if(!IsReaction(action)||!Equipped||!Visible||visibility<.95f||bagOpen)return false;
        if(circle&&circle.IsOrbiting){if(action!=PaimonMotion.Action.Concern)return false;circle.Cancel();}
        motion?.BeginActionTransition(.32f);IdleAction=action;idleTime=idleBlend=0;idlePerforming=true;IdleActionsStarted++;return true;
    }
    public bool TryPlayAmbientAction(PaimonMotion.Action action)
    {
        if(System.Array.IndexOf(IdleCycle,action)<0||!Equipped||!Visible||visibility<.95f||bagOpen||(circle&&circle.IsOrbiting))return false;
        motion?.BeginActionTransition(.5f);IdleAction=action;idleTime=idleBlend=0;idlePerforming=true;IdleActionsStarted++;if(IsMoving)RandomActionsStartedWhileMoving++;return true;
    }
    public bool TryPlayInteraction(PaimonMotion.Action action)
    {
        if(!IsPartnerAction(action)||!Equipped||!Visible||bagOpen||(circle&&circle.IsOrbiting))return false;
        motion?.BeginActionTransition(.5f);IdleAction=action;idleTime=idleBlend=0;idlePerforming=true;IdleActionsStarted++;if(IsMoving)RandomActionsStartedWhileMoving++;return true;
    }
    public static Vector3 PartnerDrift(PaimonMotion.Action action,float time,bool moving,float distance)
    {
        float p=Mathf.Clamp01(time/PaimonMotion.Duration(action));float envelope=Mathf.SmoothStep(0,1,Mathf.Clamp01(time/.85f))*Mathf.SmoothStep(0,1,Mathf.Clamp01((PaimonMotion.Duration(action)-time)/.85f));
        if(action==PaimonMotion.Action.Concern)return new Vector3(.52f-distance,-.06f*Mathf.Sin(p*Mathf.PI),.23f+(moving?.18f:.14f))*envelope;
        if(action==PaimonMotion.Action.PartnerPeek)return new Vector3((moving?.57f:.54f)-distance,.04f*Mathf.Sin(p*Mathf.PI),(moving?.30f:.45f)+(moving?.18f:.14f))*envelope;
        if(action==PaimonMotion.Action.ShoulderPeek)return new Vector3(.54f-distance,.09f*Mathf.Sin(p*Mathf.PI),-.30f+(moving?.18f:.14f))*envelope;
        if(action==PaimonMotion.Action.LeadAndReturn)return new Vector3(.52f-distance,.08f*Mathf.Sin(p*Mathf.PI),.85f+(moving?.18f:.14f))*Mathf.Sin(p*Mathf.PI)*envelope;
        return Vector3.zero;
    }
    bool bagOpen;
    CursorLockMode oldLock; bool oldVisible;
    const string SaveKey = "Companions.Paimon.Equipped";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded; Attach(); }
    static void Loaded(Scene s, LoadSceneMode m) { Attach(); }
    static void Attach()
    {
        foreach(var p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (FindFor(p.transform)) continue;
            var prefab = Resources.Load<GameObject>("Companions/PaimonCompanion");
            if (!prefab) return;
            var go = Instantiate(prefab, p.transform); var c = go.GetComponent<PaimonCompanion>();
            c.target = p.transform; c.previous = p.transform.position;
            go.transform.position = p.transform.position + p.transform.right * c.followDistance-p.transform.forward*.14f + Vector3.up * 1.5f;
        }
    }
    void OnEnable()
    {
        bones.Clear(); rest.Clear();
        if(target)previous=target.position;
        Equipped = PlayerPrefs.GetInt(SaveKey, 1) != 0;
        foreach(var t in visual.GetComponentsInChildren<Transform>(true))
        { if(!bones.ContainsKey(t.name)){bones.Add(t.name,t);rest.Add(t.name,t.localRotation);} }
        motion=GetComponent<PaimonMotion>();
        if(!motion){motion=gameObject.AddComponent<PaimonMotion>();motion.visual=visual;motion.CaptureRest();}
        circle=GetComponent<PaimonOrbitInteraction>();if(!circle)circle=gameObject.AddComponent<PaimonOrbitInteraction>();
        circle.motion=motion;circle.externallyDriven=true;
        circle.CanInteract=()=>{
            var flow=hostFlow;
            return Equipped&&!bagOpen&&visibility>.95f&&target&&target.gameObject.activeInHierarchy&&(!flow||(!flow.BlockWorldInput&&!flow.WorldHudHidden&&!flow.MapOpen));
        };
        visibility = 0;ResetIdle();
    }
    public void SetEquipped(bool value)
    {
        if(!value&&circle)circle.Cancel();
        Equipped = value; PlayerPrefs.SetInt(SaveKey, value ? 1 : 0); PlayerPrefs.Save();
        ResetIdle();
        if(value && target) { transform.position=target.position+target.right*followDistance-target.forward*.14f+Vector3.up*1.5f; velocity=Vector3.zero; }
    }
    void ResetIdle()
    {
        IdleAction=PaimonMotion.Action.Float;idleWait=.6f;idleTime=idleBlend=0;idlePerforming=false;actionBag.Clear();flourishBag.Clear();ambientSelection=0;idleIndex=-1;
    }
    // Random gestures have their own clock. Locomotion never cancels or restarts them.
    public void AdvanceIdle(float dt,bool moving,bool available=true)
    {
        if(dt<=0)return;
        if(!available)
        {
            idlePerforming=false;idleWait=idlePause;
            idleBlend=Mathf.MoveTowards(idleBlend,0,dt/.4f);
            idleTime+=dt;
            if(idleBlend<=0){IdleAction=PaimonMotion.Action.Float;idleTime=0;}
            return;
        }
        if(idlePerforming)
        {
            idleTime+=dt;
            float duration=PaimonMotion.Duration(IdleAction);
            float fadeIn=IdleAction==PaimonMotion.Action.JaizyOi?.35f:1.05f,fadeOut=IdleAction==PaimonMotion.Action.JaizyOi?.45f:1.2f;
            idleBlend=GestureEase(idleTime/fadeIn)*GestureEase((duration-idleTime)/fadeOut);
            if(idleTime>=PaimonMotion.Duration(IdleAction))
            {idlePerforming=false;IdleAction=PaimonMotion.Action.Float;idleTime=idleBlend=0;idleWait=Random.Range(idlePause*.65f,idlePause*1.2f);}
        }
        else
        {
            idleBlend=Mathf.MoveTowards(idleBlend,0,dt/.4f);
            if(idleBlend>0){idleTime+=dt;return;}
            IdleAction=PaimonMotion.Action.Float;idleTime=0;
            idleWait-=dt;
            if(idleWait<=0)
            {
                motion?.BeginActionTransition(.5f);idleIndex=NextRandomAction();AmbientActionsStarted++;if(IsAmbientFlourish(IdleCycle[idleIndex]))AmbientFlourishesStarted++;IdleAction=IdleCycle[idleIndex];idleTime=idleBlend=0;idlePerforming=true;IdleActionsStarted++;if(moving)RandomActionsStartedWhileMoving++;
            }
        }
    }
    void Update()
    {
        if(!target) { Destroy(gameObject); return; }
        // The world backpack owns B when present; the sandbox has a small companion bag.
        if(!hostFlow && Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
        {
            bagOpen=!bagOpen;CompanionWardrobe.IsBagOpen=bagOpen;
            if(bagOpen){oldLock=Cursor.lockState;oldVisible=Cursor.visible;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            else RestoreCursor();
            Camera.main?.GetComponent<ThirdPersonCamera>()?.SetLookInputLocked(bagOpen);
            target.GetComponent<PlayerMovement>()?.SetGameplayInputLocked(bagOpen);
        }
    }
    void RestoreCursor(){Cursor.lockState=oldLock;Cursor.visible=oldVisible;}
    void OnDisable(){if(bagOpen){RestoreCursor();Camera.main?.GetComponent<ThirdPersonCamera>()?.SetLookInputLocked(false);if(target)target.GetComponent<PlayerMovement>()?.SetGameplayInputLocked(false);bagOpen=false;CompanionWardrobe.IsBagOpen=false;}}
    void LateUpdate()
    {
        if(!target)return;
        if(!target.gameObject.activeInHierarchy){if(visual)visual.gameObject.SetActive(false);return;}
        float dt=Time.deltaTime;if(dt<=0)return;
        phase+=dt;
        visibility=Mathf.MoveTowards(visibility,Equipped?1:0,dt*2.5f);
        visual.gameObject.SetActive(visibility>0);visual.localScale=Vector3.one*Mathf.SmoothStep(0,1,visibility);
        Vector3 targetVelocity=Vector3.ProjectOnPlane(target.position-previous,Vector3.up)/Mathf.Max(dt,.001f);previous=target.position;
        if(targetVelocity.magnitude>25)targetVelocity=Vector3.zero;
        float oldSpeed=smoothedSpeed;
        smoothedSpeed=Mathf.Lerp(smoothedSpeed,targetVelocity.magnitude,1-Mathf.Exp(-dt*10));
        brake=Mathf.Lerp(brake,Mathf.Clamp01((oldSpeed-smoothedSpeed)/Mathf.Max(.001f,dt)/8),1-Mathf.Exp(-dt*8));
        IsMoving=smoothedSpeed>(IsMoving?.12f:.22f);
        if(!Visible)return;
        if(circle){circle.center=target;if(circle.Advance(dt,phase)){visual.localScale*=Mathf.SmoothStep(0,1,visibility);velocity=Vector3.zero;if(motion)motion.SmoothCompanionPose(Time.unscaledDeltaTime);Motion="Circle";return;}}

        if(!IsMoving)orbit=Mathf.Sin(phase*.55f)*.10f;
        else orbit=Mathf.LerpAngle(orbit*Mathf.Rad2Deg,0,dt*2)*Mathf.Deg2Rad;
        AdvanceFlightVariation(dt);
        AdvanceIdle(Time.unscaledDeltaTime,IsMoving,Equipped&&!bagOpen&&visibility>.95f);
        gestureDrift=Vector3.SmoothDamp(gestureDrift,(IsPartnerAction(IdleAction)?PartnerDrift(IdleAction,idleTime,IsMoving,followDistance):ActionDrift(IdleAction,idleTime))*idleBlend,ref gestureDriftVelocity,.24f,1.4f,Time.unscaledDeltaTime);
        Vector3 radial=Quaternion.AngleAxis(orbit*Mathf.Rad2Deg,Vector3.up)*target.right;
        Vector3 desired=target.position+target.right*gestureDrift.x+Vector3.up*gestureDrift.y+target.forward*gestureDrift.z+radial*followDistance-target.forward*(IsMoving?.18f:.14f)+Vector3.up*(1.5f+Mathf.Sin(phase*2.1f)*.055f);
        // Shorten the orbit at walls; exclude the player and triggers.
        Vector3 origin=target.position+Vector3.up*1.5f;Vector3 ray=desired-origin;
        foreach(var hit in Physics.RaycastAll(origin,ray.normalized,ray.magnitude+.18f,~0,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(target) && hit.distance < Vector3.Distance(origin,desired)+.18f)desired=origin+ray.normalized*Mathf.Max(.3f,hit.distance-.18f);
        if(Vector3.Distance(transform.position,desired)>8){transform.position=desired;velocity=Vector3.zero;}
        transform.position=Vector3.SmoothDamp(transform.position,desired,ref velocity,IsMoving?.16f:.28f,12,dt);
        Vector3 facing=IsMoving?(targetVelocity.sqrMagnitude>.04f?targetVelocity:velocity):target.position+Vector3.up*1.5f-transform.position;facing.y=0;
        if(InteractionBlend>0){Vector3 partnerFacing=HeroEyePosition-transform.position;partnerFacing.y=0;float glance=IdleAction==PaimonMotion.Action.LeadAndReturn?Mathf.Sin(Mathf.Clamp01(idleTime/PaimonMotion.Duration(IdleAction))*Mathf.PI)*.9f:1;facing=Vector3.Lerp(facing.normalized,partnerFacing.normalized,InteractionBlend*glance);}
        if(facing.sqrMagnitude>.001f)followRotation=Quaternion.Slerp(followRotation,Quaternion.LookRotation(facing),1-Mathf.Exp(-dt*5));
        Quaternion rotationGoal=followRotation*Quaternion.AngleAxis(GestureYaw,Vector3.up);
        transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.Slerp(transform.rotation,rotationGoal,1-Mathf.Exp(-dt*8)),210*dt);
        float yaw=transform.eulerAngles.y;
        bank=Mathf.Lerp(bank,Mathf.Clamp(-Mathf.DeltaAngle(previousYaw,yaw)/Mathf.Max(.001f,dt)/110,-1,1),1-Mathf.Exp(-dt*6));previousYaw=yaw;
        // Gesture drift must not keep locomotion flying after the hero stops.
        float flightGoal=IsMoving?1:0;
        flyBlend=Mathf.MoveTowards(flyBlend,flightGoal,dt*(flightGoal>flyBlend?3.5f:2));
        flightTime=flyBlend>.1f?flightTime+dt:0;
        if(motion)
        {
            motion.SampleJaizyBase(phase,flyBlend);
            motion.ApplyFollowFlight(flightTime,flyBlend,bank,brake,flightMood,motion.ImportedReady);
            // Gesture poses are composed last so flight cannot overwrite the arms/head/face.
            motion.ApplyIndependentAction(phase,IdleAction,idleTime,idleBlend);
            if(InteractionBlend>0)motion.LookAtPartner(HeroEyePosition,InteractionBlend);
            motion.SmoothCompanionPose(Time.unscaledDeltaTime);
        }
        visual.localScale*=Mathf.SmoothStep(0,1,visibility);
        Motion=(IsMoving||flyBlend>.2f?"Flying":"Float")+(IdleAction==PaimonMotion.Action.Float?"":" + "+IdleAction);


    }
    void OnGUI()
    {
        if(hostFlow)return;
        if(!bagOpen){GUI.Label(new Rect(20,125,460,25),"B  Backpack / Companions · Click your friend to circle");return;}
        var wardrobe=GetComponent<CompanionWardrobe>();
        if(wardrobe)wardrobe.Draw(new Rect(Screen.width/2-260,Screen.height/2-190,520,380));

    }
}
