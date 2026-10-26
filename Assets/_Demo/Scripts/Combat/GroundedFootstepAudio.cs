using UnityEngine;
using KingOfGraphic.MechBossMigration;

[DefaultExecutionOrder(900),DisallowMultipleComponent]
public sealed class GroundedFootstepAudio:MonoBehaviour
{
    public AudioClip stoneStep,grassStep;
    public WorldFootstepSurfaces surfaces;
    public bool heavy;
    [Range(0,1)] public float volume=.18f;
    Animator animator;Transform left,right;PlayerMovement player;PlayerCombat combat;
    PlayerBattleActions actions;WorldHeroActions presentation;WorldPlayerGuard guard;
    MechSentinelBattle boss;Health health;BaselineFlow flow;AudioSource voice;
    readonly FootfallGate gate=new FootfallGate();
    Vector3 previous;bool sampled;
    readonly RaycastHit[] hits=new RaycastHit[24];
    public int StoneSteps{get;private set;}
    public int GrassSteps{get;private set;}
    public AudioClip LastClip{get;private set;}
    public int LastFoot{get;private set;}
    void Awake()
    {
        animator=GetComponentInChildren<Animator>();if(animator&&animator.isHuman){left=animator.GetBoneTransform(HumanBodyBones.LeftFoot);right=animator.GetBoneTransform(HumanBodyBones.RightFoot);}
        player=GetComponent<PlayerMovement>();combat=GetComponent<PlayerCombat>();actions=GetComponent<PlayerBattleActions>();
        presentation=GetComponent<WorldHeroActions>();guard=GetComponent<WorldPlayerGuard>();boss=GetComponent<MechSentinelBattle>();health=GetComponent<Health>();
        flow=FindFirstObjectByType<BaselineFlow>();
        gate.MinimumTravel=heavy?.18f:.12f;gate.MinimumInterval=heavy?.25f:.14f;
        var go=new GameObject(heavy?"Sentinel footsteps":"World hero footsteps");go.transform.SetParent(transform,false);
        voice=go.AddComponent<AudioSource>();voice.playOnAwake=false;voice.spatialBlend=heavy?.9f:.35f;
        voice.dopplerLevel=0;voice.minDistance=heavy?3:1.5f;voice.maxDistance=heavy?28:15;
    }
    void OnEnable(){previous=transform.position;sampled=false;gate.Reset();}
    void OnDisable(){gate.Reset();sampled=false;if(voice)voice.Stop();}
    void LateUpdate()
    {
        float distance=Vector3.ProjectOnPlane(transform.position-previous,Vector3.up).magnitude;previous=transform.position;
        if(!sampled){sampled=true;distance=0;}
        bool eligible=left&&right&&Time.timeScale>0&&(!health||!health.IsDead)&&(!flow||!flow.BlockWorldInput);
        if(player)eligible&=player.IsStableGrounded&&!player.SwimmingActive&&!player.ClimbingActive&&!player.MantlingActive&&!player.GroundJumpActive&&
            (!combat||!combat.IsAttacking)&&(!actions||(!actions.IsDodging&&!actions.IsRushing))&&(!presentation||!presentation.Busy)&&(!guard||!guard.IsGuarding);
        if(boss){
            var state=animator.GetCurrentAnimatorStateInfo(0);
            eligible&=boss.isActiveAndEnabled&&boss.Engaged&&!boss.Reacting&&!animator.IsInTransition(0)&&state.IsName("Walk");
        }
        if(!left||!right){gate.Reset();return;}
        int foot=gate.Observe(left.position.y-transform.position.y,right.position.y-transform.position.y,distance,Time.unscaledTime,eligible);
        if(foot==0)return;
        Vector3 point=foot==1?left.position:right.position;
        int count=Physics.RaycastNonAlloc(point+Vector3.up*.65f,Vector3.down,hits,1.6f,~0,QueryTriggerInteraction.Ignore);
        float nearest=float.PositiveInfinity;RaycastHit ground=default;
        for(int i=0;i<count;i++){var h=hits[i];if(!h.collider||h.transform.IsChildOf(transform)||h.normal.y<.5f||
            h.collider.GetComponentInParent<CameraCollisionPassthrough>()||h.distance>=nearest)continue;nearest=h.distance;ground=h;}
        if(nearest==float.PositiveInfinity)return;
        bool stone=heavy||(surfaces&&surfaces.IsStone(ground.point));
        var clip=stone?stoneStep:grassStep;if(!clip||!voice)return;
        voice.transform.position=ground.point;voice.pitch=Random.Range(.96f,1.04f);voice.PlayOneShot(clip,volume);
        LastClip=clip;LastFoot=foot;if(stone)StoneSteps++;else GrassSteps++;
    }
}
