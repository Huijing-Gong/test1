using UnityEngine;
using UnityEngine.InputSystem;
using SamProduction.BossArena;

// World adapter for the final Boss room's directional guard rules. Health stays
// with the overworld owner; no arena session, duplicate HP or input loop is copied.
[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class WorldPlayerGuard : MonoBehaviour
{
    public ArenaDefenseFeedback feedback;
    readonly ArenaGuardController.Logic guard=new ArenaGuardController.Logic();
    readonly ArenaGuardController.Settings settings=ArenaGuardController.Settings.Default;
    Health health;PlayerMovement movement;PlayerCombat combat;PlayerBattleActions battle;
    PlayerEquipment equipment;PlayerVitals vitals;Animator animator;BaselineFlow flow;ThirdPersonCamera cameraRig;
    float poseAge,recoilUntil;long strike;bool ownsLock;
    public bool IsGuarding=>guard.IsGuarding;
    public bool LastBlockPerfect{get;private set;}
    public int BlockCount{get;private set;}
    public bool Ready=>animator&&animator.HasState(0,Animator.StringToHash("WorldGuardLoop"));
    void Awake()
    {
        health=GetComponent<Health>();movement=GetComponent<PlayerMovement>();combat=GetComponent<PlayerCombat>();
        battle=GetComponent<PlayerBattleActions>();equipment=GetComponent<PlayerEquipment>();vitals=GetComponent<PlayerVitals>();
        animator=GetComponentInChildren<Animator>(true);flow=FindFirstObjectByType<BaselineFlow>();cameraRig=FindFirstObjectByType<ThirdPersonCamera>();
    }
    void OnEnable(){if(!health)health=GetComponent<Health>();health.ResolvingDamage+=Resolve;}
    void OnDisable(){if(health)health.ResolvingDamage-=Resolve;Suspend();}
    void OnApplicationFocus(bool focused){if(!focused)Suspend();}
    void Update()
    {
        if(feedback)feedback.Advance(Time.deltaTime);
        var keys=Keyboard.current;
        bool evade=WorldInputBindings.Pressed("Dodge");
        bool allowed=Ready&&movement&&combat&&battle&&equipment&&equipment.CanAttack&&vitals&&vitals.CanExert&&
            !health.IsDead&&!combat.IsAttacking&&!battle.IsDodging&&!battle.IsRushing&&!battle.FocusActive&&
            !movement.SwimmingActive&&!movement.ClimbingActive&&!movement.MantlingActive&&!movement.GroundJumpActive&&
            movement.IsStableGrounded&&(!movement.IsGameplayInputLocked||ownsLock)&&
            (!flow||!flow.BlockWorldInput)&&Time.timeScale>0&&Cursor.lockState==CursorLockMode.Locked&&!evade;
        TickGuard(WorldInputBindings.Held("Guard"),WorldInputBindings.Pressed("Guard"),allowed,Time.deltaTime);
    }
    public void TickGuard(bool held,bool pressed,bool allowed,float delta)
    {
        bool previous=IsGuarding;
        guard.TickInput(held,pressed,allowed&&Ready,Mathf.Max(0,delta),settings);
        if(IsGuarding)
        {
            if(!previous){poseAge=0;ownsLock=true;movement.SetActionInputLocked(this,true);animator.CrossFadeInFixedTime("WorldGuardStart",.07f,0,0);}
            poseAge+=delta;
            if(poseAge>.32f&&Time.time>=recoilUntil&&!animator.GetCurrentAnimatorStateInfo(0).IsName("WorldGuardLoop")&&
                !(animator.IsInTransition(0)&&animator.GetNextAnimatorStateInfo(0).IsName("WorldGuardLoop")))animator.CrossFadeInFixedTime("WorldGuardLoop",.08f);
            if(cameraRig&&cameraRig.LockedEnemy){var to=Vector3.ProjectOnPlane(cameraRig.LockedEnemy.position-transform.position,Vector3.up);if(to.sqrMagnitude>.001f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(to),360*delta);}
        }
        else if(previous)ReleasePose(true);
    }
    void Resolve(ref DamageInfo damage)
    {
        var enemy=damage.Source?damage.Source.GetComponentInParent<Health>():null;
        if(!IsGuarding||!enemy||enemy.Team!=HealthTeam.Enemy||enemy.IsDead||!vitals)return;
        var result=guard.ResolveHit(new ArenaHit{damage=damage.Amount,point=damage.HitPoint,direction=damage.HitDirection},transform.forward,settings);
        if(!result.Blocked)return;
        if(!vitals.TrySpendCombatStamina(result.Perfect?5:12)){Suspend();return;}
        LastBlockPerfect=result.Perfect;BlockCount++;
        damage=new DamageInfo(Mathf.RoundToInt(result.Hit.damage),damage.Source,damage.HitPoint,damage.HitDirection);
        recoilUntil=Time.time+.27f;animator.CrossFadeInFixedTime("WorldGuardHit",.035f,0,0);
        feedback?.ShowDefense(++strike,result.Perfect?ArenaDefenseFeedback.TypePerfectBlock:ArenaDefenseFeedback.TypeBlock,damage.HitPoint,damage.HitDirection);
        GetComponent<PlayerContactFeedback>()?.Contact(damage.HitPoint,damage.HitDirection,false,true);
        cameraRig?.RequestShake(result.Perfect?.06f:.035f,.1f);
    }
    void ReleasePose(bool playExit)
    {
        if(ownsLock&&movement)movement.SetActionInputLocked(this,false);ownsLock=false;
        if(playExit&&animator&&!health.IsDead)animator.CrossFadeInFixedTime("WorldGuardExit",.08f,0,0);
    }
    public void Suspend(){guard.Suspend();ReleasePose(false);if(feedback)feedback.ResetFeedback();}
}
