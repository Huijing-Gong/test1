using UnityEngine;
namespace KingOfGraphic.Hero
{
    // Presentation only: never moves the capsule or changes jump/climb controls.
    [DefaultExecutionOrder(80),DisallowMultipleComponent]
    public sealed class HeroStairFootPlacement:MonoBehaviour
    {
        HeroWorldAdapter hero;PlayerMovement movement;HeroPoseDriver pose;Animator animator;
        readonly RaycastHit[] hits=new RaycastHit[24];float weight,filteredY,velocity;bool initialized,rememberStairs;Vector3 lastStairPosition;
        void Awake(){hero=GetComponent<HeroWorldAdapter>();movement=GetComponent<PlayerMovement>();pose=GetComponentInChildren<HeroPoseDriver>();animator=pose?pose.animator:null;}
        void LateUpdate(){
            if(!hero||!movement||!pose||!animator||Time.deltaTime<=0)return;
            bool eligible=movement.IsStableGrounded&&!movement.ClimbingActive&&!movement.MantlingActive&&!movement.SwimmingActive&&!hero.Busy;
            if(!eligible){weight=0;initialized=false;rememberStairs=false;return;}
            if(movement.StairSupportActive){rememberStairs=true;lastStairPosition=transform.position;}
            float target=movement.StairSupportActive||(rememberStairs&&Vector3.Distance(transform.position,lastStairPosition)<.18f)?1:0;
            weight=Mathf.MoveTowards(weight,target,Time.deltaTime*10);
            float rootY=transform.position.y;
            if(!initialized||!eligible||Mathf.Abs(filteredY-rootY)>.55f){filteredY=rootY;velocity=0;initialized=true;}
            filteredY=Mathf.SmoothDamp(filteredY,rootY,ref velocity,.065f,8,Time.deltaTime);
            if(weight<=0)return;
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);if(!hips)return;
            var leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);var rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if(!leftFoot||!rightFoot)return;
            float leftHeight=leftFoot.position.y-rootY,rightHeight=rightFoot.position.y-rootY,low=Mathf.Min(leftHeight,rightHeight);
            hips.position+=Vector3.up*(Mathf.Clamp(filteredY-rootY,-.12f,0)*weight);
            Place(true,rootY,leftHeight,low);Place(false,rootY,rightHeight,low);pose.CommitPose();
        }
        void Place(bool left,float rootY,float sourceHeight,float lowestHeight){
            var upper=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
            var lower=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
            var foot=animator.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);if(!upper||!lower||!foot)return;
            int count=Physics.RaycastNonAlloc(foot.position+Vector3.up*.45f,Vector3.down,hits,.9f,~0,QueryTriggerInteraction.Ignore);
            float best=float.PositiveInfinity;RaycastHit ground=default;
            for(int i=0;i<count;i++)if(hits[i].collider&&!hits[i].transform.IsChildOf(transform)&&!(hits[i].collider is CharacterController)&&hits[i].distance<best){ground=hits[i];best=hits[i].distance;}
            if(!ground.collider||ground.normal.y<.707f||Mathf.Abs(ground.point.y-rootY)>.44f)return;
            // The lower authored foot is the stance foot. A root-height change must
            // not reduce its planting weight and leave both feet floating downhill.
            float contact=Mathf.Clamp01(1-(sourceHeight-lowestHeight)/.18f)*weight;
            Vector3 goal=foot.position;goal.y=ground.point.y+.085f;
            goal=Vector3.Lerp(foot.position,goal,contact);
            if(Vector3.Distance(goal,foot.position)>.43f)return;
            Quaternion rotation=foot.rotation;
            Solve(upper,lower,foot,goal,upper.position+transform.forward*.65f+transform.right*(left?-.2f:.2f));foot.rotation=rotation;
        }
        static void Solve(Transform upper,Transform lower,Transform end,Vector3 goal,Vector3 pole){
            Vector3 root=upper.position,axis=goal-root;float a=Vector3.Distance(root,lower.position),b=Vector3.Distance(lower.position,end.position);
            float distance=Mathf.Clamp(axis.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);axis=axis.sqrMagnitude>.000001f?axis.normalized:Vector3.down;
            Vector3 bend=Vector3.ProjectOnPlane(pole-root,axis).normalized;if(bend.sqrMagnitude<.000001f)return;
            float along=(a*a-b*b+distance*distance)/(2*distance);Vector3 knee=root+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(lower.position-root,knee-root)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(end.position-lower.position,root+axis*distance-lower.position)*lower.rotation;
        }
    }
}
