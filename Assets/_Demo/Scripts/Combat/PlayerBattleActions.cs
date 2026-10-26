using UnityEngine;
using UnityEngine.InputSystem;

// All dodge timing uses real seconds. Environmental damage never counts as an enemy attack.
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class PlayerBattleActions : MonoBehaviour
{
    public const float DodgeDuration = .60f;
    float dodgeDuration=.60f, dodgeProgress;
    public float ActiveDodgeDuration => dodgeDuration;
    static readonly AnimationCurve DodgeTravel = new AnimationCurve(new Keyframe(0,0),new Keyframe(.12f,0),new Keyframe(.32f,.36f),new Keyframe(.70f,.88f),new Keyframe(.86f,1),new Keyframe(1,1));
    // Forgiving anticipation window, measured in real time even during slow motion.
    const float PerfectWindow = .34f, InvulnerableEnd = .40f, FocusDuration = 4.2f;
    KingOfGraphic.Hero.HeroWorldAdapter authoredHero;
    bool UsesAuthoredHero=>authoredHero&&authoredHero.isActiveAndEnabled;
    PlayerMovement movement; PlayerCombat combat; PlayerVitals vitals; Health health;
    CharacterController controller; Animator animator; ThirdPersonCamera cameraRig; BaselineFlow flow;
    Vector3 dodgeOrigin, dodgeDirection; float dodgeStart, dodgeCooldown, focusEnd, combatUntil;
    float previousScale = 1, previousFixed; AnimatorUpdateMode previousMode; float previousAnimatorSpeed;
    bool ownsFocus, consumedPerfect;
    PlayerPerfectDodgeFeedback focusFeedback;
    Health focusTarget;
    bool rushLock, approachDone;
    public bool CounterSequenceActive { get; private set; }
    public bool CounterPromptReady => UsesAuthoredHero?authoredHero.FocusPromptReady:ownsFocus && approachDone && !CounterSequenceActive;

    float rushStarted, rushTravel, rushStuck, bufferedDodgeUntil, rushRetryUntil;
    Vector3 bufferedDodge;
    public bool IsRushing { get; private set; }
    public bool HitStopActive => combat && combat.IsApplyingHitStop;
    public string FocusHint => UsesAuthoredHero?authoredHero.FocusHint:"HOLD LMB TO ATTACK";
    public bool IsCountering => UsesAuthoredHero?authoredHero.FocusSequenceActive:ownsFocus && (IsRushing || approachDone || combat.IsFlurryAttacking);
    public Health FocusTarget => focusTarget && !focusTarget.IsDead && focusTarget.isActiveAndEnabled ? focusTarget : null;
    public float FocusRemaining => ownsFocus ? Mathf.Max(0, focusEnd-CombatClock.UnscaledTime) : 0;
    public float FocusFraction => FocusRemaining / (UsesAuthoredHero?KingOfGraphic.Hero.HeroFocusSequence.MaximumSeconds:FocusDuration);
    public int RushCount { get; private set; }
    public bool FinishDodgeForAttack() { if (!IsDodging) return true; if (Age < .43f) return false; EndDodge(); return true; }
    public bool QueueDodge(Vector3 direction) { if (TryDodge(direction)) return true; if (!IsDodging && combat.IsAttacking) { bufferedDodge=direction; bufferedDodgeUntil=CombatClock.UnscaledTime+.2f; } return false; }

    public bool IsDodging { get; private set; }
    public bool FocusActive => ownsFocus;
    public int PerfectDodgeCount { get; private set; }
    public int DodgeCount { get; private set; }
    float FocusScale=>UsesAuthoredHero?KingOfGraphic.Hero.HeroFocusSequence.EnemyScale:.32f;
    public float DesiredTimeScale => ownsFocus ? FocusScale : previousScale;
    public bool Invulnerable => (UsesAuthoredHero&&authoredHero.FocusSequenceActive)||(IsDodging && Age >= .015f && Age <= InvulnerableEnd) || (ownsFocus && (IsRushing || combat.IsFlurryAttacking));
    float Age => CombatClock.UnscaledTime - dodgeStart;
    public float PlayerDeltaTime => ownsFocus && Time.timeScale > 0 ? CombatClock.UnscaledDeltaTime : Time.deltaTime;
    public bool Ready => animator && animator.runtimeAnimatorController && animator.HasState(0, Animator.StringToHash("DodgeBack"));

    void Awake()
    {
        authoredHero=GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
        movement=GetComponent<PlayerMovement>();combat=GetComponent<PlayerCombat>();vitals=GetComponent<PlayerVitals>();
        health=GetComponent<Health>();controller=GetComponent<CharacterController>();animator=GetComponentInChildren<Animator>(true);
        cameraRig=FindFirstObjectByType<ThirdPersonCamera>();flow=FindFirstObjectByType<BaselineFlow>();
        focusFeedback=GetComponent<PlayerPerfectDodgeFeedback>();if(!focusFeedback)focusFeedback=gameObject.AddComponent<PlayerPerfectDodgeFeedback>();
    }
    void OnEnable(){if(!health)health=GetComponent<Health>();health.Damaged+=Damaged;health.Died+=Died;}
    void OnDisable(){if(health){health.Damaged-=Damaged;health.Died-=Died;}Cancel();}
    void Died(Health h,DamageInfo d){Cancel();}
    void Damaged(Health h,DamageInfo d)
    {
        combatUntil=CombatClock.UnscaledTime+5;
        if(UsesAuthoredHero||!Ready||IsDodging||h.IsDead||movement.IsGameplayInputLocked)return;
        animator.CrossFadeInFixedTime("BattleHit",.06f);
    }
    public void MarkCombat(){combatUntil=CombatClock.UnscaledTime+6;}
    void Update()
    {
        if(!animator||!movement)return;

        if(ownsFocus)
        {
            if(health.IsDead||!isActiveAndEnabled||(flow&&flow.BlockWorldInput)||movement.SwimmingActive||movement.ClimbingActive||movement.MantlingActive) EndFocus();
            else if(Time.timeScale>0 && (CombatClock.UnscaledTime>=focusEnd || !FocusTarget)){if(UsesAuthoredHero)authoredHero.ReturnFromFocus();else EndFocus();}
            // Normal Animator update still respects pause and the existing hit stop.
            if(ownsFocus&&!UsesAuthoredHero)animator.speed=Time.timeScale>0?previousAnimatorSpeed/Time.timeScale:previousAnimatorSpeed;
        }
        bool active=vitals&&vitals.EnemyCombatActive&&Time.timeScale>0&&Cursor.lockState==CursorLockMode.Locked;
        // Manual movement owns the capsule. Automatic counter approach must not
        // pull the player forward again while they are trying to retreat/strafe.
        bool manualMove=ManualMovementRequested();
        if(!UsesAuthoredHero&&active&&ownsFocus&&!manualMove&&!approachDone&&!IsDodging&&!IsRushing&&!combat.IsAttacking)TryFlurryRush();
        if(IsRushing) { if(!active||!ownsFocus||manualMove)StopRush();else UpdateRush(); }
        if(IsDodging)
        {
            if(!active){EndDodge();return;}
            float t=Mathf.Clamp01(Age/dodgeDuration);
            // A planted anticipation, push-off, airborne travel and a planted recovery.
            float progress=Mathf.Clamp01(DodgeTravel.Evaluate(t));
            float step=Mathf.Max(0,progress-dodgeProgress)*1.8f;dodgeProgress=progress;
            controller.Move(dodgeDirection*step+Vector3.down*2*PlayerDeltaTime);
            if(t>=1)EndDodge();
        }
        else if(!UsesAuthoredHero&&active&&!IsRushing&&WorldInputBindings.Pressed("Dodge"))QueueDodge(ReadDirection());
        if(bufferedDodgeUntil>=CombatClock.UnscaledTime&&combat.CanCancelIntoDodge){var direction=bufferedDodge;bufferedDodgeUntil=0;TryDodge(direction);}
        if(!UsesAuthoredHero&&Ready){bool stance=CombatClock.UnscaledTime<combatUntil&&!movement.SwimmingActive&&!movement.ClimbingActive;animator.SetBool("BattleReady",stance);animator.SetFloat("BattleStance",stance?1:0,.18f,PlayerDeltaTime);}
    }
    static bool ManualMovementRequested()
    {
        return WorldInputBindings.Movement.sqrMagnitude>.01f;
    }
    Vector3 ReadDirection()
    {
        var k=Keyboard.current;if(k==null)return -transform.forward;
        Vector2 input=WorldInputBindings.Movement;
        if(input.sqrMagnitude<.01f)return -transform.forward;
        var c=Camera.main;Vector3 f=c?Vector3.ProjectOnPlane(c.transform.forward,Vector3.up).normalized:transform.forward;
        Vector3 r=c?Vector3.ProjectOnPlane(c.transform.right,Vector3.up).normalized:transform.right;
        return (f*input.y+r*input.x).normalized;
    }
    public bool TryDodge(Vector3 direction)
    {
        if(UsesAuthoredHero||!Ready||IsDodging||IsRushing||CombatClock.UnscaledTime<dodgeCooldown||Time.timeScale<=0||health.IsDead||!vitals||!vitals.EnemyCombatActive||Cursor.lockState!=CursorLockMode.Locked)return false;
        if(combat.IsAttacking){if(!combat.CanCancelIntoDodge)return false;combat.CancelAttackForDodge();}
        if(!movement.CanStartInteraction||!vitals.TrySpendCombatStamina(18))return false;
        dodgeDirection=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;if(dodgeDirection.sqrMagnitude<.01f)dodgeDirection=-transform.forward;
        dodgeOrigin=transform.position;dodgeStart=CombatClock.UnscaledTime;dodgeCooldown=dodgeStart+.68f;consumedPerfect=false;IsDodging=true;DodgeCount++;MarkCombat();
        var local=transform.InverseTransformDirection(dodgeDirection);
        string state=Mathf.Abs(local.x)>Mathf.Abs(local.z)?(local.x>0?"DodgeRight":"DodgeLeft"):(local.z>0?"DodgeForward":"DodgeBack");

        dodgeDuration=.60f;dodgeProgress=0;

        animator.CrossFadeInFixedTime(state,.045f,0,0);return true;
    }
    void EndDodge(){bool active=IsDodging;IsDodging=false;if(active&&animator&&!health.IsDead)animator.CrossFadeInFixedTime("Locomotion",.08f);}
    public void FinishCounterStrike(){CounterSequenceActive=false;}
    public bool ConfirmCounter(){
        if(!CounterPromptReady||IsDodging||IsRushing||!ClearRushSight())return false;
        CounterSequenceActive=true;
        if(combat.TryStartFlurryStrike())return true;
        CounterSequenceActive=false;return false;
    }
    // Called at the enemy's actual damage instant, using the position before the evasive step.
    // This also catches a successful side-hop that has already left the attack volume.
    public bool TryEvadeThreat(GameObject source,Vector3 origin,float radius,float minimumDot)
    {
        if(!Invulnerable||!EnemySource(source))return false;
        Vector3 to=dodgeOrigin-origin;if(Mathf.Abs(to.y)>2.5f)return false;to.y=0;
        if(to.magnitude>radius||Vector3.Dot(source.transform.forward,to.normalized)<minimumDot)return false;
        foreach(var hit in Physics.RaycastAll(origin+Vector3.up,dodgeOrigin+Vector3.up-(origin+Vector3.up),to.magnitude,~0,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform)&&!hit.transform.IsChildOf(source.transform))return false;
        QualifyPerfect(source);return true;
    }
    public bool TryEvade(in DamageInfo damage)
    {
        if(!Invulnerable||!EnemySource(damage.Source))return false;
        QualifyPerfect(damage.Source);return true;
    }
    bool EnemySource(GameObject source){var h=source?source.GetComponentInParent<Health>():null;return h&&h.Team==HealthTeam.Enemy&&!h.IsDead;}
    void QualifyPerfect(GameObject source)
    {
        if(consumedPerfect||Age>PerfectWindow||Vector3.ProjectOnPlane(transform.position-dodgeOrigin,Vector3.up).magnitude<.02f)return;
        consumedPerfect=true;if(ownsFocus)return;
        focusTarget=source.GetComponentInParent<Health>();
        PerfectDodgeCount++;previousScale=Time.timeScale;previousFixed=Time.fixedDeltaTime;
        previousMode=animator.updateMode;previousAnimatorSpeed=animator.speed;
        approachDone=false;CounterSequenceActive=false;ownsFocus=true;focusEnd=CombatClock.UnscaledTime+FocusDuration;Time.timeScale=.32f;Time.fixedDeltaTime=previousFixed*.32f;
        if(cameraRig)cameraRig.RequestShake(.085f,.2f);
        focusFeedback.Begin();
    }
    public void QualifyAuthoredPerfectDodge(GameObject source)
    {
        // Called only after the existing adapter rejects a real enemy damage attempt in its perfect window.
        if(!UsesAuthoredHero||ownsFocus||Time.timeScale<=0||!EnemySource(source)||!animator)return;
        focusTarget=source.GetComponentInParent<Health>();PerfectDodgeCount++;
        previousScale=Time.timeScale;previousFixed=Time.fixedDeltaTime;previousMode=animator.updateMode;previousAnimatorSpeed=animator.speed;
        approachDone=false;CounterSequenceActive=false;ownsFocus=true;focusEnd=CombatClock.UnscaledTime+KingOfGraphic.Hero.HeroFocusSequence.MaximumSeconds;
        Time.timeScale=FocusScale;Time.fixedDeltaTime=previousFixed*FocusScale;
        if(!authoredHero.BeginFocusSequence(focusTarget)){EndFocus();return;}if(cameraRig)cameraRig.RequestShake(.085f,.2f);if(focusFeedback)focusFeedback.Begin();
    }
    public void FinishAuthoredFocusSequence()=>EndFocus(false);
    void EndFocus(bool cancelHero=true)
    {
        if(!ownsFocus)return;if(cancelHero&&UsesAuthoredHero)authoredHero.CancelFocusSequence(false);ownsFocus=false;CounterSequenceActive=false;approachDone=false;StopRush();
        if(focusFeedback)focusFeedback.End();
        if(Time.timeScale>0&&(Mathf.Approximately(Time.timeScale,.32f)||Mathf.Approximately(Time.timeScale,FocusScale)))Time.timeScale=previousScale;
        Time.fixedDeltaTime=previousFixed;
        if(animator){animator.updateMode=previousMode;animator.speed=previousAnimatorSpeed;}
    }
    public void Cancel(){EndDodge();EndFocus();StopRush();bufferedDodgeUntil=0;combatUntil=0;if(focusFeedback)focusFeedback.Clear();}
    public bool TryFlurryRush()
    {
        if(!ownsFocus||!FocusTarget||Time.timeScale<=0||CombatClock.UnscaledTime<rushRetryUntil||combat.IsAttacking||health.IsDead||!Ready)return false;
        if(IsRushing)return true;
        if(IsDodging){if(Age<dodgeDuration)return false;EndDodge();}
        if(!movement.CanStartInteraction||Vector3.Distance(transform.position,focusTarget.transform.position)>5.5f||Mathf.Abs(transform.position.y-focusTarget.transform.position.y)>1.4f)return false;
        if(!ClearRushSight())return false;
        rushLock=!movement.IsGameplayInputLocked;if(!rushLock)return false;
        movement.SetActionInputLocked(this,true);IsRushing=true;rushStarted=CombatClock.UnscaledTime;rushTravel=0;rushStuck=0;RushCount++;
        animator.CrossFadeInFixedTime("BattleRush",.08f);focusFeedback.Rush();return true;
    }
    bool ClearRushSight()
    {
        if(!FocusTarget)return false;
        Vector3 origin=transform.position+Vector3.up,delta=focusTarget.transform.position+Vector3.up-origin;
        foreach(var hit in Physics.RaycastAll(origin,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform)&&!hit.transform.IsChildOf(focusTarget.transform))return false;
        return true;
    }
    float RushStopDistance()
    {
        var capsule=FocusTarget?focusTarget.GetComponent<CapsuleCollider>():null;
        float radius=capsule?capsule.radius*Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x),Mathf.Abs(capsule.transform.lossyScale.z)):.45f;
        return Mathf.Clamp(controller.radius+radius+.18f,.8f,1.5f);
    }
    public void FaceFocusTarget(float delta)
    {
        if(!FocusTarget)return;Vector3 to=focusTarget.transform.position-transform.position;to.y=0;
        if(to.sqrMagnitude>.001f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(to),1-Mathf.Exp(-32*delta));
    }
    void UpdateRush()
    {
        if(!FocusTarget||!ClearRushSight()){StopRush();return;}
        Vector3 to=focusTarget.transform.position-transform.position;to.y=0;float gap=to.magnitude-RushStopDistance();
        FaceFocusTarget(PlayerDeltaTime);
        if(gap<=.07f){StopRush();approachDone=true;animator.CrossFadeInFixedTime("Locomotion",.1f,0);return;}
        float step=Mathf.Min(gap,14f*PlayerDeltaTime);Vector3 before=transform.position;
        controller.Move(to.normalized*step+Vector3.down*2*PlayerDeltaTime);
        float moved=Vector3.ProjectOnPlane(transform.position-before,Vector3.up).magnitude;rushTravel+=moved;rushStuck=moved<step*.15f?rushStuck+PlayerDeltaTime:0;
        if(CombatClock.UnscaledTime-rushStarted>.42f||rushTravel>3.8f||rushStuck>.09f){rushRetryUntil=CombatClock.UnscaledTime+.35f;StopRush();}
    }
    public void FollowFlurryTarget(float delta)
    {
        if(!ownsFocus||!FocusTarget)return;FaceFocusTarget(delta);
        Vector3 to=focusTarget.transform.position-transform.position;to.y=0;float gap=to.magnitude-RushStopDistance();
        if(gap>.04f&&gap<1&&ClearRushSight())controller.Move(to.normalized*Mathf.Min(gap,4*delta)+Vector3.down*2*delta);
    }
    void StopRush(){bool active=IsRushing;IsRushing=false;if(rushLock&&movement){movement.SetActionInputLocked(this,false);}rushLock=false;if(active&&animator&&!UsesAuthoredHero&&!health.IsDead&&!combat.IsAttacking)animator.CrossFadeInFixedTime("Locomotion",.08f);}

    void OnGUI()
    {
        if((Time.timeScale<=0&&!HitStopActive)||(flow&&flow.WorldHudHidden))return;
        if(CombatClock.UnscaledTime<combatUntil&&Cursor.lockState==CursorLockMode.Locked){
            var hint=new GUIStyle(GUI.skin.label){fontSize=12,alignment=TextAnchor.MiddleCenter};
            var old=GUI.color;GUI.color=new Color(.95f,.9f,.76f,.85f);GUI.Label(new Rect(0,Screen.height-47,Screen.width,22),WorldInputBindings.Label("Attack")+"  Combo    "+WorldInputBindings.Label("Guard")+"  Guard    "+WorldInputBindings.Label("Dodge")+" + direction  Dodge",hint);GUI.color=old;
        }
    }
}
