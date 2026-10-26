using UnityEngine;

namespace SamProduction.BossArena
{
    // Preserve the authored bend plane and wrist orientation while keeping casting
    // hands on the near side of a close opponent. Never used to extend damage reach.
    internal static class ArenaArmContact
    {
        public static void Constrain(Animator animator, CharacterController target)
        {
            Solve(animator, target, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            Solve(animator, target, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        }
        static void Solve(Animator animator,CharacterController target,HumanBodyBones upperBone,HumanBodyBones lowerBone,HumanBodyBones handBone)
        {
            var upper=animator.GetBoneTransform(upperBone);var lower=animator.GetBoneTransform(lowerBone);var hand=animator.GetBoneTransform(handBone);
            if(!upper||!lower||!hand||!target)return;
            var centre=target.transform.TransformPoint(target.center);
            float radius=target.radius*Mathf.Max(Mathf.Abs(target.transform.lossyScale.x),Mathf.Abs(target.transform.lossyScale.z));
            float half=Mathf.Max(0,target.height*Mathf.Abs(target.transform.lossyScale.y)*.5f-radius);
            var normal=Vector3.ProjectOnPlane(upper.position-centre,Vector3.up).normalized;
            if(normal.sqrMagnitude<.5f)return;
            Vector3 start=upper.position,elbow=lower.position,end=hand.position;
            // No correction when the arm is clear of the opponent's body volume.
            bool overlaps=false;
            for(int i=0;i<=12;i++)
            {
                var p=Vector3.Lerp(elbow,end,i/12f);
                var axis=new Vector3(centre.x,Mathf.Clamp(p.y,centre.y-half,centre.y+half),centre.z);
                if((p-axis).sqrMagnitude<(radius+.10f)*(radius+.10f)){overlaps=true;break;}
            }
            if(!overlaps)return;
            var wrist=hand.rotation;
            var near=centre+normal*(radius+.12f);
            float penetration=Vector3.Dot(near-end,normal);
            var goal=end+normal*Mathf.Max(0,penetration);
            float a=Vector3.Distance(start,elbow),b=Vector3.Distance(elbow,end);
            var aim=goal-start;float distance=Mathf.Clamp(aim.magnitude,Mathf.Abs(a-b)+.01f,a+b-.015f);
            if(aim.sqrMagnitude<.0001f)return;
            var forward=aim.normalized;goal=start+forward*distance;
            var pole=Vector3.ProjectOnPlane(elbow-start,forward).normalized;
            if(pole.sqrMagnitude<.01f)pole=Vector3.ProjectOnPlane(-animator.transform.forward,forward).normalized;
            float along=(a*a-b*b+distance*distance)/(2*distance);
            var desiredElbow=start+forward*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            if(Vector3.Dot(desiredElbow-near,normal)<0)
            {
                // A low lunge may put the elbow inside even when the wrist is clear.
                // Rotate its bend plane toward the near side without changing bone lengths.
                pole=Vector3.ProjectOnPlane(normal,forward).normalized;
                desiredElbow=start+forward*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            }
            upper.rotation=Quaternion.FromToRotation(elbow-start,desiredElbow-start)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,goal-lower.position)*lower.rotation;
            hand.rotation=wrist;
        }
    }
}
