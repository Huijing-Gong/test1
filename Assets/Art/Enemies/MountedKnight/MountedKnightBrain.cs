using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health),typeof(Rigidbody),typeof(CapsuleCollider))]
public sealed class MountedKnightBrain : MonoBehaviour
{
    public enum Mode { Patrol, PauseAtWaypoint, Charge, Chase, Attack, Recover, Return, Dead }
    public MountedKnightTerritory territory;
    public MountedKnightMotion motion;
    public float patrolSpeed=2.1f,chaseSpeed=3.6f,chargeSpeed=8.4f;
    public float noticeRange=12,attackRange=2.65f;
    public float attackRecovery=1.25f,heavyRecovery=1.65f,chargeRecovery=1.4f;
    public float LocomotionRate {get;private set;}=1;
    float recoveryDuration,travelSpeed;
    MountedKnightEffects effects;
    public int CurrentAttack=>attackIndex;
    public bool AttackIsRadial=>attackIndex==2||attackIndex==6;
    public float TelegraphRange=>(State==Mode.Charge?2.9f:AttackIsRadial?2.65f:3f)+Mathf.Max(0,BodyRadius-.67f);
    public float NextImpact {get{foreach(float t in impacts[attackIndex])if(t>(float)motion.SourceTime)return t;return -1;}}
    public bool BladeWindow {get{if(AttackIsRadial)return false;foreach(float t in impacts[attackIndex])if(Mathf.Abs((float)motion.SourceTime-t)<.2f)return true;return false;}}
    public int chargeDamage=10,attackDamage=12;
    public Mode State {get;private set;}
    public int ChargesStarted {get;private set;}
    public int AttacksStarted {get;private set;}
    public int SuccessfulHits {get;private set;}
    public int PatrolArrivals {get;private set;}
    public int LeashReturns {get;private set;}
    public float StateTime=>clock;
    Health health,playerHealth;PlayerVitals player;Rigidbody body;CapsuleCollider capsule;BaselineFlow flow;
    Vector3 home,lockedCharge,destination;Quaternion homeRotation;
    List<Vector3> path=new List<Vector3>();int pathIndex,patrolIndex=1,patrolDirection=1,attackIndex,hitMask;float clock,repath,search,stuck,locomotionIdleTime;bool moving,chargeClosing;
    float BodyRadius=>capsule.radius*Mathf.Max(Mathf.Abs(transform.lossyScale.x),Mathf.Abs(transform.lossyScale.z));
    readonly float[] durations={2.25f,2.88f,2.3f,3.7f,2.1f,2.15f,2.4f};
    readonly float[] attackRates={1.4f,1.35f,1.45f,1.4f,1f,1f,1.1f};
    readonly float[] attackStarts={.18f,.15f,.25f,.3f,0,0,0};
    const float ChargeRate=1.4f;
    readonly float[][] impacts={new[]{1.26f},new[]{.88f,2.02f},new[]{1.65f},new[]{1.85f,3.06f},new[]{.85f},new[]{.91f},new[]{1.25f}};
    // Slot 4 is retired; preserve the remaining attack/effect IDs.
    readonly int[] attackClips={4,5,6,7,-1,10,11};
    void Awake(){if(!GetComponent<MountedKnightSaddleCloth>())gameObject.AddComponent<MountedKnightSaddleCloth>();if(!GetComponent<MountedKnightCapeGrounding>())gameObject.AddComponent<MountedKnightCapeGrounding>();if(!GetComponent<MountedKnightPixelDeath>())gameObject.AddComponent<MountedKnightPixelDeath>();if(!GetComponent<MountedKnightTailReaction>())gameObject.AddComponent<MountedKnightTailReaction>();effects=GetComponent<MountedKnightEffects>();health=GetComponent<Health>();body=GetComponent<Rigidbody>();capsule=GetComponent<CapsuleCollider>();if(!motion)motion=GetComponent<MountedKnightMotion>();home=transform.position;homeRotation=transform.rotation;if(!GetComponent<NonClimbableSurface>())gameObject.AddComponent<NonClimbableSurface>();}
    void OnEnable(){if(!health)health=GetComponent<Health>();health.Died+=Died;flow=FindFirstObjectByType<BaselineFlow>();if(flow)flow.WorldEncountersReset+=ResetEncounter;}
    void OnDisable(){if(health)health.Died-=Died;if(flow)flow.WorldEncountersReset-=ResetEncounter;}
    void Start(){if(territory)territory.ConfigureAgent(transform,BodyRadius);Set(Mode.Patrol);}
    void FixedUpdate(){Tick(Time.fixedDeltaTime);}
    public void Tick(float dt){
        if(!territory||territory.patrolPoints.Length<2||!motion||health.IsDead)return;
        if(!player||!playerHealth){search-=dt;if(search<=0){search=1;player=FindFirstObjectByType<PlayerVitals>();playerHealth=player?player.GetComponent<Health>():null;}}
        bool validPlayer=player&&playerHealth&&!playerHealth.IsDead;
        // The leash gate precedes every movement and every damage query, including active attacks.
        bool inside=validPlayer&&territory.Contains(player.transform.position)&&territory.Ground(player.transform.position,out var playerFloor)&&Mathf.Abs(player.transform.position.y-playerFloor.y)<3;
        if((State==Mode.Charge||State==Mode.Chase||State==Mode.Attack||State==Mode.Recover)&&!inside)ReturnToPatrol();
        if(!validPlayer||!player.EnemyCombatActive){motion.Suspend(true);return;}motion.Suspend(false);
        clock+=dt;repath-=dt;moving=false;travelSpeed=0;
        Vector3 target=player.transform.position;float distance=Flat(body.position,target);
        switch(State){
            case Mode.Patrol:
            case Mode.PauseAtWaypoint:
                if(inside&&distance<=noticeRange&&Sight(target)){BeginCharge(target);break;}
                if(State==Mode.PauseAtWaypoint){if(clock>=.8f){AdvancePatrol();Set(Mode.Patrol);}break;}
                if(Follow(territory.patrolPoints[patrolIndex],patrolSpeed,.55f,dt)){PatrolArrivals++;Set(Mode.PauseAtWaypoint);}break;
            case Mode.Return:
                // Re-entry cannot interrupt the return journey or restart the cancelled attack.
                if(Follow(territory.patrolPoints[patrolIndex],patrolSpeed,.6f,dt))Set(Mode.PauseAtWaypoint);
                break;
            case Mode.Charge:
                float chargeTime=(float)motion.SourceTime;
                if(chargeTime<.8f){if(chargeTime<.65f)lockedCharge=target;Face(lockedCharge,dt,360);}
                else if(!chargeClosing&&chargeTime<2.35f){
                    bool arrived=Follow(lockedCharge,chargeSpeed,2f,dt);
                    if((hitMask&1)==0&&Hit(2.3f,chargeDamage,.55f))hitMask|=1;
                    // Blend into the finishing cut on arrival, instead of galloping in place.
                    if(arrived||stuck>.3f){chargeClosing=true;motion.Play(3,ChargeRate,true,2.35f);chargeTime=2.35f;}
                }
                if(chargeTime>=2.60f&&(hitMask&2)==0){hitMask|=2;if(effects)effects.Impact(false);Hit(2.9f,attackDamage,.05f);}
                if(chargeTime>=2.98f)BeginRecovery(chargeRecovery);
                break;
            case Mode.Chase:
                if(distance<=attackRange&&Sight(target)){
                    Face(target,dt,540);
                    Vector3 aim=target-body.position;aim.y=0;
                    if(aim.sqrMagnitude<.001f||Vector3.Dot(transform.forward,aim.normalized)>.35f)BeginAttack();
                    break;
                }
                Follow(target,chaseSpeed,attackRange*.8f,dt);
                break;
            case Mode.Attack:
                float attackTime=(float)motion.SourceTime;
                if(attackTime<.6f)Face(target,dt,260);
                var times=impacts[attackIndex];for(int i=0;i<times.Length;i++)if(attackTime>=times[i]&&(hitMask&(1<<i))==0){hitMask|=1<<i;if(effects)effects.Impact(AttackIsRadial);Hit(AttackIsRadial?2.65f:3f,attackDamage,AttackIsRadial?-1:.05f);}
                if(attackTime>=durations[attackIndex])BeginRecovery(attackIndex>=2?heavyRecovery:attackRecovery);
                break;
            case Mode.Recover:
                // A committed opening: no tracking, movement or hit queries until recovery ends.
                if(clock>=recoveryDuration)ContinueCombat(target,distance);
                break;
        }
        if(State==Mode.Patrol||State==Mode.Return||State==Mode.Chase){
            if(moving){locomotionIdleTime=0;float reference=State==Mode.Chase?chaseSpeed:patrolSpeed;float desired=Mathf.Clamp(travelSpeed/Mathf.Max(.01f,reference),.35f,1.5f);LocomotionRate=Mathf.Lerp(LocomotionRate,desired,1-Mathf.Exp(-12*dt));motion.Play(State==Mode.Chase?2:1,LocomotionRate);}
            else{locomotionIdleTime+=dt;if(locomotionIdleTime>.22f)motion.Play(0);}
        }
    }
    void Set(Mode value){
        State=value;clock=0;hitMask=0;repath=0;path.Clear();pathIndex=0;stuck=0;locomotionIdleTime=0;
        if(!motion)return;
        if(effects&&value!=Mode.Attack&&value!=Mode.Charge)effects.CancelAttack();
        if(value==Mode.Patrol||value==Mode.Return)motion.Play(1);
        else if(value==Mode.PauseAtWaypoint||value==Mode.Recover)motion.Play(0);
        else if(value==Mode.Charge){chargeClosing=false;motion.Play(3,ChargeRate,true,.1f);}
        else if(value==Mode.Dead)motion.Play(8);
        // Attack owns its clip; Chase keeps the outgoing pose until actual movement.

    }
    void AdvancePatrol(){if(patrolIndex>=territory.patrolPoints.Length-1)patrolDirection=-1;else if(patrolIndex<=0)patrolDirection=1;patrolIndex+=patrolDirection;}
    void BeginCharge(Vector3 target){Set(Mode.Charge);lockedCharge=target;ChargesStarted++;}
    // Retain clip/effect indices, but omit RisingRiposte from the encounter rotation.
    static readonly int[] CombatAttackOrder={0,1,2,3,5,6};
    void BeginAttack(){attackIndex=CombatAttackOrder[AttacksStarted%CombatAttackOrder.Length];AttacksStarted++;Set(Mode.Attack);motion.Play(attackClips[attackIndex],attackRates[attackIndex],true,attackStarts[attackIndex]);}
    void BeginRecovery(float duration){Set(Mode.Recover);recoveryDuration=duration;}
    void ContinueCombat(Vector3 target,float distance){
        Vector3 aim=target-body.position;aim.y=0;
        if(distance<=attackRange&&Sight(target)&&(aim.sqrMagnitude<.001f||Vector3.Dot(transform.forward,aim.normalized)>.35f))BeginAttack();
        else Set(Mode.Chase);
    }
    void ReturnToPatrol(){LeashReturns++;float best=float.PositiveInfinity;for(int i=0;i<territory.patrolPoints.Length;i++){float d=Flat(body.position,territory.patrolPoints[i]);if(d<best){best=d;patrolIndex=i;}}Set(Mode.Return);}
    bool Follow(Vector3 target,float speed,float stop,float dt){
        if(Flat(body.position,target)<=stop)return true;
        if(repath<=0||Flat(destination,target)>1.5f){destination=target;path=territory.Path(body.position,target);pathIndex=path.Count>1&&territory.Clear(body.position,path[1],transform,player?player.transform:null)?1:0;repath=State==Mode.Chase?.65f:1.25f;}
        if(path.Count==0){stuck+=dt;return false;}
        while(pathIndex<path.Count&&Flat(body.position,path[pathIndex])<.35f)pathIndex++;
        while(pathIndex+1<path.Count&&Flat(body.position,path[pathIndex])<1.4f&&territory.Clear(body.position,path[pathIndex+1],transform,player?player.transform:null))pathIndex++;
        Vector3 next=pathIndex<path.Count?path[pathIndex]:target;
        if(!territory.Contains(next))return false;
        Vector3 flat=next-body.position;flat.y=0;
        Face(next,dt,State==Mode.Charge?360:420);
        float alignment=Vector3.Dot(transform.forward,flat.normalized);
        float step=Mathf.Min(speed*dt,flat.magnitude)*Mathf.Clamp01((alignment+.1f)/1.1f);if(step<.0001f)return false;
        Vector3 candidate=body.position+flat.normalized*step;
        if(!territory.Contains(candidate)||!territory.Ground(candidate,out var grounded)||Mathf.Abs(grounded.y-body.position.y)>.45f||!territory.Clear(body.position,grounded,transform,player?player.transform:null)){
            stuck+=dt;if(stuck>.4f)repath=0;return false;
        }
        // The player remains a solid obstacle; only contact-range damage can connect.
        var playerBody=player?player.GetComponent<CharacterController>():null;
        if(playerBody&&Flat(candidate,player.transform.position)<BodyRadius+playerBody.radius+.08f){return true;}
        stuck=0;moving=true;travelSpeed=step/Mathf.Max(.0001f,dt);body.MovePosition(grounded);return false;
    }
    void Face(Vector3 p,float dt,float degrees){var v=p-body.position;v.y=0;if(v.sqrMagnitude>.001f)body.MoveRotation(Quaternion.RotateTowards(body.rotation,Quaternion.LookRotation(v),degrees*dt));}
    bool Sight(Vector3 target){
        Vector3 a=body.position+Vector3.up*1.15f,b=target+Vector3.up*.95f;var d=b-a;
        foreach(var h in Physics.RaycastAll(a,d.normalized,d.magnitude,~0,QueryTriggerInteraction.Ignore))
            if(!h.collider.transform.IsChildOf(transform)&&!(player&&h.collider.transform.IsChildOf(player.transform)))return false;
        return true;
    }
    bool Hit(float range,int damage,float dot){
        if(!player||!playerHealth||playerHealth.IsDead||!player.EnemyCombatActive||!territory.Contains(player.transform.position)||(State!=Mode.Charge&&State!=Mode.Attack))return false;
        Vector3 to=player.transform.position-body.position;if(Mathf.Abs(to.y)>2.5f)return false;to.y=0;range+=Mathf.Max(0,BodyRadius-.67f);
        // dot > 1 requests the narrower, forward contact cone of the charge.
        float threshold=dot>1?.55f:dot;
        var evasion=player.GetComponent<PlayerBattleActions>();if(evasion&&evasion.TryEvadeThreat(gameObject,body.position,range,threshold))return false;
        if(to.magnitude>range||Vector3.Dot(transform.forward,to.normalized)<threshold||!Sight(player.transform.position))return false;
        bool accepted=playerHealth.TryApplyDamage(new DamageInfo(damage,gameObject,player.transform.position+Vector3.up,to));
        if(accepted)SuccessfulHits++;return accepted;
    }
    void Died(Health h,DamageInfo damage){Set(Mode.Dead);capsule.enabled=false;motion.Suspend(false);motion.Play(8,1,true);if(effects)effects.BeginDeath();}
    public void ResetEncounter(){
        health.RestoreFullHealth();if(effects)effects.ResetEffects();capsule.enabled=true;body.position=home;body.rotation=homeRotation;patrolIndex=1;patrolDirection=1;ChargesStarted=AttacksStarted=SuccessfulHits=PatrolArrivals=LeashReturns=0;Set(Mode.Patrol);
    }
    static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
}
