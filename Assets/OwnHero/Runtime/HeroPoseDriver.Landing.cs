using UnityEngine;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        float landingAge=-1,frameLandingStart;
        bool landingHeavy;
        Transform landingHips,landingSpine,landingHead;
        readonly Transform[] landingThigh=new Transform[2],landingKnee=new Transform[2],landingFoot=new Transform[2];
        readonly Vector3[] landingTargets=new Vector3[2],landingPoles=new Vector3[2];
        readonly Quaternion[] landingRotations=new Quaternion[2];
        public static float MovingLandingDuration(bool heavy)=>heavy?.52f:.32f;
        public float MovingLandingAge=>landingAge;
        public float LandingFootResidual {get;private set;}
        public float LandingAppliedDrop {get;private set;}
        static Quaternion LandingFromTo(Vector3 from,Vector3 to)
        {
            from.Normalize();to.Normalize();float w=1+Vector3.Dot(from,to);
            if(w<.000001f)return Quaternion.FromToRotation(from,to);
            Vector3 c=Vector3.Cross(from,to);return new Quaternion(c.x,c.y,c.z,w).normalized;
        }

        bool CacheLandingBones()
        {
            if(landingHips)return true;
            landingHips=animator.GetBoneTransform(HumanBodyBones.Hips);
            landingSpine=animator.GetBoneTransform(HumanBodyBones.Spine);
            landingHead=animator.GetBoneTransform(HumanBodyBones.Head);
            for(int i=0;i<2;i++)
            {
                landingThigh[i]=animator.GetBoneTransform(i==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
                landingKnee[i]=animator.GetBoneTransform(i==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
                landingFoot[i]=animator.GetBoneTransform(i==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                if(!landingThigh[i]||!landingKnee[i]||!landingFoot[i]){landingHips=null;return false;}
            }
            return landingHips;
        }
        // Reuse the moving gait's current feet, never world-space planted anchors.
        // Queries call this from their own reconstructed gait and do not advance state.
        void ApplyMovingLanding(float age,bool heavy,bool record=false)
        {
            if(record){LandingFootResidual=0;LandingAppliedDrop=0;}
            if(age<0||age>=MovingLandingDuration(heavy)||!HeroGaitClock.IsOrdinaryCycle(CurrentAction)||stopContactActive||crossContact.valid||!CacheLandingBones())return;
            float peak=heavy?.10f:.065f,end=MovingLandingDuration(heavy);
            float amount=age<peak?Mathf.SmoothStep(0,1,age/peak):1-Mathf.SmoothStep(0,1,(age-peak)/(end-peak));
            float drop=(heavy?.14f:.075f)*amount;
            for(int i=0;i<2;i++)
            {
                landingTargets[i]=landingFoot[i].position;landingRotations[i]=landingFoot[i].rotation;
                landingPoles[i]=landingKnee[i].position-landingThigh[i].position;
                float upper=Vector3.Distance(landingThigh[i].position,landingKnee[i].position),lower=Vector3.Distance(landingKnee[i].position,landingFoot[i].position);
                // Lowering shortens ordinary standing legs. Limit before the folded-leg singularity.
                Vector3 d=landingThigh[i].position-landingTargets[i];
                float minimum=Mathf.Abs(upper-lower)+.01f,horizontal=d.x*d.x+d.z*d.z;
                float height=horizontal<minimum*minimum?Mathf.Sqrt(minimum*minimum-horizontal):.01f;
                drop=Mathf.Min(drop,Mathf.Max(0,d.y-height));
            }
            landingHips.position-=Vector3.up*drop;
            if(landingSpine)landingSpine.rotation=Quaternion.AngleAxis((heavy?12:7)*amount,transform.right)*landingSpine.rotation;
            if(landingHead)landingHead.rotation=Quaternion.AngleAxis(-(heavy?5:3)*amount,transform.right)*landingHead.rotation;
            float residual=0;
            for(int i=0;i<2;i++)
            {
                Transform a=landingThigh[i],b=landingKnee[i],c=landingFoot[i];Vector3 target=landingTargets[i],axis=target-a.position;
                float upper=Vector3.Distance(a.position,b.position),lower=Vector3.Distance(b.position,c.position),distance=axis.magnitude;
                axis=distance>1e-6f?axis/distance:Vector3.down;
                float along=(upper*upper-lower*lower+distance*distance)/(2*Mathf.Max(distance,.000001f));
                Vector3 bend=Vector3.ProjectOnPlane(landingPoles[i],axis);if(bend.sqrMagnitude<1e-8f)bend=Vector3.ProjectOnPlane(transform.forward,axis);bend.Normalize();
                Vector3 knee=a.position+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
                a.rotation=LandingFromTo(b.position-a.position,knee-a.position)*a.rotation;
                b.rotation=LandingFromTo(c.position-b.position,target-b.position)*b.rotation;c.rotation=landingRotations[i];
                residual=Mathf.Max(residual,Vector3.Distance(c.position,target));
            }
            if(record){LandingFootResidual=residual;LandingAppliedDrop=drop;}
        }
    }
}
