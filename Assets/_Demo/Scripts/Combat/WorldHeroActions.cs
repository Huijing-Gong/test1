using System;
using UnityEngine;

// World-only presentation layered around the sandbox combat owner.
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class WorldHeroActions : MonoBehaviour
{
    Animator animator;PlayerMovement movement;PlayerCombat combat;PlayerBattleActions battle;
    PlayerEquipment equipment;Health health;BaselineFlow flow;
    string action;float age,duration;Action completed;bool dying,deathFinished,wasGrounded,wasMoving;
    float peak,previousYaw,stillTime;Vector3 previousPosition;
    string locomotion;float locomotionAge,locomotionDuration,lastSpeed;ThirdPersonCamera cameraRig;
    Vector3 visualOffset;float actionFloor;bool groundAction;
    Mesh contactMesh;readonly System.Collections.Generic.List<Vector3> vertices=new System.Collections.Generic.List<Vector3>();
    string equipmentPose;
    public void QueueEquipmentPresentation(bool equipped){if(flow&&flow.InventoryOpen)equipmentPose=equipped?"DrawSword":"SheatheSword";}
    public string CurrentAction=>action;
    public bool Busy=>action!=null;
    public bool Dying=>dying;
    public bool DeathAnimationFinished=>deathFinished;
    public bool Ready=>animator&&animator.HasState(0,Animator.StringToHash("Death"));
    public float Duration(string name)
    {
        foreach(var clip in animator.runtimeAnimatorController.animationClips)if(clip.name==name)return clip.length;
        return .7f;
    }
    void Awake()
    {
        animator=GetComponentInChildren<Animator>();movement=GetComponent<PlayerMovement>();combat=GetComponent<PlayerCombat>();
        battle=GetComponent<PlayerBattleActions>();equipment=GetComponent<PlayerEquipment>();health=GetComponent<Health>();flow=FindFirstObjectByType<BaselineFlow>();
        previousPosition=transform.position;peak=transform.position.y;previousYaw=transform.eulerAngles.y;
        cameraRig=FindFirstObjectByType<ThirdPersonCamera>();
    }
    void OnEnable(){if(!health)health=GetComponent<Health>();health.Died+=Died;health.Damaged+=Damaged;}
    void OnDisable(){if(health){health.Died-=Died;health.Damaged-=Damaged;}Cancel();}
    public bool TryPlay(string name,Action onComplete=null)
    {
        if(!Ready||Busy||health.IsDead||!movement.CanStartInteraction||!movement.IsStableGrounded||Time.timeScale<=0||
            (flow&&flow.BlockWorldInput)||combat.IsAttacking||battle.IsDodging||battle.IsRushing||!animator.HasState(0,Animator.StringToHash(name)))return false;
        Begin(name,onComplete);return true;
    }
    void Begin(string name,Action onComplete=null)
    {
        RestoreVisualOffset();
        action=name;age=0;duration=Duration(name);completed=onComplete;locomotion=null;
        groundAction=name!="Drown"&&name!="Drowned";
        // NavigationGround includes a navigation safety offset, not the visible floor.
        actionFloor=transform.position.y-.05f;
        float nearest=float.PositiveInfinity;
        foreach(var hit in Physics.RaycastAll(transform.position+Vector3.up*.5f,Vector3.down,2f,~0,QueryTriggerInteraction.Ignore))
        {
            if(hit.transform.IsChildOf(transform)||hit.normal.y<.5f||hit.distance>=nearest)continue;
            nearest=hit.distance;actionFloor=hit.point.y;
        }
        movement.SetActionInputLocked(this,true);
        animator.SetFloat("MoveAmount",0);animator.SetBool("IsSwimming",false);animator.SetBool("IsClimbing",false);
        animator.CrossFadeInFixedTime(name,.1f,0,0);
    }
    public bool TryRest(Action onComplete)
    {
        return TryPlay("RestBegin",()=>{Begin("RestLoop",()=>Begin("RestEnd",onComplete));duration=1.2f;});
    }
    public bool TryDraw(Action onComplete)
    {
        return TryPlay("DrawSword",()=>{equipment.SetWorldSwordDrawn(true);onComplete?.Invoke();});
    }
    public bool TrySheathe()=>TryPlay("SheatheSword",()=>equipment.SetWorldSwordDrawn(false));
    void Damaged(Health h,DamageInfo damage){if(!h.IsDead&&!dying&&Busy){Cancel();animator.CrossFadeInFixedTime("BattleHit",.08f);}}
    void Died(Health h,DamageInfo damage)
    {
        bool water=movement.SwimmingActive;
        Cancel();combat.CancelForTransition();dying=true;deathFinished=false;
        equipment.SetWorldSwordDrawn(false);Begin(water?"Drown":"Death");
    }
    public void ResetForRespawn()
    {
        dying=false;deathFinished=false;equipmentPose=null;Cancel();peak=transform.position.y;wasGrounded=false;wasMoving=false;
        previousPosition=transform.position;locomotion=null;
    }
    public void Cancel()
    {
        if(dying)return;
        RestoreVisualOffset();
        bool active=Busy;action=null;completed=null;movement?.SetActionInputLocked(this,false);
        if(active&&animator&&animator.isActiveAndEnabled)animator.CrossFadeInFixedTime("Locomotion",.1f);
        locomotion=null;
    }
    void Update()
    {
        if(!Ready)return;
        RestoreVisualOffset();
        if(dying&&!health.IsDead){ResetForRespawn();return;}
        if(Busy)
        {
            if(!dying && ((flow&&flow.BlockWorldInput)||movement.SwimmingActive||movement.ClimbingActive||movement.MantlingActive)){Cancel();return;}
            age+=Time.deltaTime;
            if(action=="DrawSword"&&age>=duration*.48f)equipment.SetWorldSwordDrawn(true);
            if(action=="SheatheSword"&&age>=duration*.65f)equipment.SetWorldSwordDrawn(false);
            if(age>=duration)
            {
                if(dying){if(action=="Drown"){action="Drowned";animator.CrossFadeInFixedTime("Drowned",.15f);}deathFinished=true;return;}
                var callback=completed;action=null;completed=null;movement.SetActionInputLocked(this,false);
                animator.CrossFadeInFixedTime("Locomotion",.12f);callback?.Invoke();
            }
            previousPosition=transform.position;return;
        }
        if(dying||Time.timeScale<=0)return;
        if(equipmentPose!=null && movement.CanStartInteraction && (!flow||!flow.BlockWorldInput))
        {
            var pose=equipmentPose;equipmentPose=null;
            if(TryPlay(pose,()=>equipment.SetWorldSwordDrawn(equipment.IsSwordEquipped))){if(pose=="DrawSword")equipment.SetWorldSwordDrawn(false);return;}
        }
        if(movement.IsGameplayInputLocked||combat.IsAttacking||battle.IsDodging||battle.IsRushing||movement.SwimmingActive||movement.ClimbingActive||movement.MantlingActive)
        {locomotion=null;previousPosition=transform.position;peak=transform.position.y;wasGrounded=false;wasMoving=false;return;}
        bool grounded=movement.IsStableGrounded;
        if(!grounded){peak=Mathf.Max(peak,transform.position.y);locomotion=null;}
        else
        {
            if(!wasGrounded&&peak-transform.position.y>3f){TryPlay("HeavyLand");peak=transform.position.y;wasGrounded=true;return;}
            peak=transform.position.y;
        }
        float speed=Vector3.ProjectOnPlane(transform.position-previousPosition,Vector3.up).magnitude/Mathf.Max(.001f,battle.PlayerDeltaTime);
        bool moving=speed>.15f;float yaw=Mathf.DeltaAngle(previousYaw,transform.eulerAngles.y);previousYaw=transform.eulerAngles.y;
        if(grounded&&!movement.GroundJumpActive)
        {
            // Ordinary movement stays in the source locomotion blend. The imported
            // travelling start/stop/turn clips have no matching controller displacement;
            // crossfading to them after the capsule stops causes visible foot sliding.
            bool strafing=false;
            if(cameraRig&&cameraRig.LockedEnemy&&moving)
            {
                var local=transform.InverseTransformDirection(transform.position-previousPosition);
                string pose=Mathf.Abs(local.x)>Mathf.Abs(local.z)*.7f?(local.x>0?"StrafeRight":"StrafeLeft"):local.z<0?"WalkBack":null;
                if(pose!=null){strafing=true;
                    bool playing=animator.GetCurrentAnimatorStateInfo(0).IsName(pose)||(animator.IsInTransition(0)&&animator.GetNextAnimatorStateInfo(0).IsName(pose));
                    if(locomotion!=pose||!playing)Pose(pose);
                    animator.SetFloat("CombatStepRate",Mathf.Clamp(speed/2f,.4f,1.6f));
                }
            }
            if(locomotion!=null&&!strafing)
            {
                locomotionAge+=Time.deltaTime;
                if((locomotion=="Sprint"?(!movement.SprintingActive||!moving):locomotion.StartsWith("Strafe")||locomotion=="WalkBack"||locomotionAge>=locomotionDuration))
                {locomotion=null;animator.CrossFadeInFixedTime("Locomotion",.12f);}
            }
        }
        lastSpeed=speed;wasMoving=moving;wasGrounded=grounded;previousPosition=transform.position;
    }
    void Pose(string name){locomotion=name;locomotionAge=0;locomotionDuration=Duration(name);animator.CrossFadeInFixedTime(name,.12f);}
    void RestoreVisualOffset(){if(animator)animator.transform.position-=visualOffset;visualOffset=Vector3.zero;}
    void LateUpdate()
    {
        if(!Busy||!groundAction||!animator)return;
        if(action!="DrawSword"&&action!="SheatheSword"&&equipment.PracticeSword)equipment.PracticeSword.SetActive(false);
        if(!contactMesh)contactMesh=new Mesh();float min=float.PositiveInfinity;
        foreach(var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if(!skin.enabled)continue;skin.BakeMesh(contactMesh);contactMesh.GetVertices(vertices);
            foreach(var v in vertices)min=Mathf.Min(min,skin.transform.TransformPoint(v).y);
        }
        // Only supplementary grounded actions are fitted; source locomotion/combat are untouched.
        visualOffset=Vector3.up*Mathf.Clamp(actionFloor+.006f-min,action=="HeavyLand"?0:-.12f,.25f);animator.transform.position+=visualOffset;
    }
    void OnDestroy(){if(contactMesh)Destroy(contactMesh);}
}
