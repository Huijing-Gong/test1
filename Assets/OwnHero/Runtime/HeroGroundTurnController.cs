using UnityEngine;
using UnityEngine.SceneManagement;

namespace KingOfGraphic.Hero
{
    // Shared presentation admission only; movement, rotation and combat remain owned
    // by their existing controllers. Initialize once after all Awake calls.
    public sealed class HeroGroundTurnController
    {
        HeroPoseDriver pose;
        CharacterController capsule;
        HeroGroundTurnReference neutral;
        HeroGroundTurnPose turn;
        Vector3 pivot,hostScale,groundPosition,groundScale;
        Quaternion target,groundRotation;
        Collider ground;
        HeroGroundTurnSupport areaSupport,supportCache;
        float age;
        bool initialized,observed;
        readonly RaycastHit[] hits=new RaycastHit[16];
        public bool Active=>turn!=null;
        public float Age=>age;
        public int StartedCount {get;private set;}
        public string LastStopReason {get;private set;}

        public void Initialize(HeroPoseDriver actor,CharacterController host)
        {
            if(initialized)return;initialized=true;pose=actor;capsule=host;
            if(!pose||!pose.Ready||!capsule||!pose.library.stationaryTurns)return;
            hostScale=capsule.transform.lossyScale;
            if(Vector3.Distance(hostScale,Vector3.one)>.0001f||Mathf.Abs(capsule.center.y-capsule.height*.5f)>.001f){LastStopReason="Unsupported capsule scale/origin";return;}
            var left=pose.library.Find("LOC_TURN_L");var right=pose.library.Find("LOC_TURN_R");
            if(left==null||right==null||!left.clip||!right.clip||Mathf.Abs(left.duration-HeroGroundTurnPose.Duration)>.0001f||Mathf.Abs(right.duration-HeroGroundTurnPose.Duration)>.0001f){LastStopReason="Turn resources not configured";return;}
            pose.Sample("LOC_IDLE",0,0,false,fade:0);
            float clearance=Mathf.Clamp(capsule.skinWidth*Mathf.Abs(hostScale.y),0,.08f);
            neutral=new HeroGroundTurnReference(pose,capsule.transform,capsule.transform.position.y-clearance);
        }
        public void Cancel(string reason="Owner state changed"){turn=null;age=0;observed=false;ground=null;areaSupport=null;LastStopReason=reason;}
        bool FlatSupport(out Collider contact,out float floor)
        {
            contact=null;floor=0;if(!capsule||!capsule.enabled||!capsule.isGrounded)return false;
            var root=capsule.transform;float expected=root.position.y-Mathf.Clamp(capsule.skinWidth*Mathf.Abs(root.lossyScale.y),0,.08f);
            float firstHeight=0;
            for(int side=-1;side<=1;side++)
            {
                var origin=root.position+root.right*(side*.18f)+Vector3.up*.3f;
                int count=pose.gameObject.scene.GetPhysicsScene().Raycast(origin,Vector3.down,hits,.55f,~0,QueryTriggerInteraction.Ignore);
                if(count==hits.Length)return false;float nearest=float.MaxValue;RaycastHit best=default;
                for(int i=0;i<count;i++){var h=hits[i];if(h.collider is CharacterController||h.collider.transform.IsChildOf(root)||h.distance>=nearest)continue;nearest=h.distance;best=h;}
                if(nearest==float.MaxValue||best.rigidbody||best.normal.y<.99863f||Mathf.Abs(best.point.y-expected)>.065f)return false;
                if(side==-1){contact=best.collider;firstHeight=best.point.y;}
                else if(best.collider!=contact||Mathf.Abs(firstHeight-best.point.y)>.02f)return false;
            }
            floor=firstHeight;return contact;
        }
        bool Ready()=>neutral!=null&&pose&&pose.Ready&&pose.library.stationaryTurns&&capsule&&Vector3.Distance(capsule.transform.lossyScale,hostScale)<.0001f;
        public void ObserveFacing(Vector3 facing,bool allowed,float delta)
        {
            if(delta<=0)return; // A paused world keeps its entry snapshot and clock.
            observed=true;
            if(!Ready()||!allowed||facing.sqrMagnitude<.0001f||pose.CurrentOverlay!=null||!FlatSupport(out var support,out var floor)){Cancel("Facing or support unavailable");return;}
            var root=capsule.transform;facing=Vector3.ProjectOnPlane(facing,Vector3.up);
            if(facing.sqrMagnitude<.0001f){Cancel("No horizontal facing");return;}
            var wanted=Quaternion.LookRotation(facing,Vector3.up);
            if(turn!=null)
            {
                if(age>=HeroGroundTurnPose.Duration){Cancel("Completed");return;}
                if(Quaternion.Angle(target,wanted)>4||Vector3.Distance(root.position,pivot)>.03f||!GroundUnchanged()){Cancel("Retarget or support changed");return;}
                return;
            }
            if(!pose.CanBeginGroundTurn)return;
            float angle=Vector3.SignedAngle(root.forward,facing,Vector3.up);
            if(Mathf.Abs(angle)<14.9999f||Mathf.Abs(angle)>90.001f)return;
            angle=Mathf.Clamp(angle,-90,90);pivot=root.position;target=wanted;
            ground=support;groundPosition=ground.transform.position;groundRotation=ground.transform.rotation;groundScale=ground.transform.lossyScale;
            
            var candidate=new HeroGroundTurnPose(neutral,pose.library.Find(angle>0?"LOC_TURN_R":"LOC_TURN_L"),angle,pivot,root.rotation);
            if(!HeroGroundTurnSupport.TryCreate(ground,pivot,floor,candidate.SupportRadius,out areaSupport,supportCache)){Cancel("Turn footprint crosses unsupported ground");return;}
            supportCache=areaSupport;turn=candidate;age=0;StartedCount++;LastStopReason=null;
        }
        bool GroundUnchanged()=>areaSupport!=null&&areaSupport.Unchanged(false)&&ground&&ground.enabled&&ground.gameObject.activeInHierarchy&&Vector3.Distance(ground.transform.position,groundPosition)<.001f&&Quaternion.Angle(ground.transform.rotation,groundRotation)<.1f&&Vector3.Distance(ground.transform.lossyScale,groundScale)<.001f;
        public bool TryAdvance(bool allowed,float delta,out string id,out float time,out float sampleDelta,out HeroGroundTurnFrame frame)
        {
            id=null;time=sampleDelta=0;frame=default;
            if(delta<=0)return false;
            if(turn==null){observed=false;return false;}
            if(!observed||!allowed||!Ready()||!GroundUnchanged()||!areaSupport.Unchanged(true)||Vector3.Distance(capsule.transform.position,pivot)>.03f||!FlatSupport(out _,out _)){Cancel("Presentation no longer eligible");return false;}
            observed=false;float before=age;float next=before+delta;age=next>=HeroGroundTurnPose.Duration-.000001f?HeroGroundTurnPose.Duration:next;
            if(age<=before){Cancel("Completed");return false;}
            id=turn.ActionId;time=age;sampleDelta=age-before;frame=new HeroGroundTurnFrame(turn,before,age);return true;
        }
    }
}
