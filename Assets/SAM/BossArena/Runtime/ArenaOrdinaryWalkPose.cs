using System;
using System.Linq;
using UnityEngine;

namespace SamProduction.BossArena
{
    // A visual layer for existing walking clips. Never edits pelvis, legs, root or clocks.
    [RequireComponent(typeof(ArenaAnimation))]
    public sealed class ArenaOrdinaryWalkPose : MonoBehaviour
    {
        public AnimationClip start,loop,stop;
        public AnimationClip[] patrol=Array.Empty<AnimationClip>();
        public float Weight { get; private set; }
        public bool Ready { get; private set; }
        ArenaAnimation driver;Transform actor,leftFoot,rightFoot,chest,head; ArenaPatrolMotor soleMotor;
        Arm left,right;
        void OnEnable()
        {
            driver=GetComponent<ArenaAnimation>();driver.Evaluated+=Apply;
        }
        void OnDisable(){if(driver!=null)driver.Evaluated-=Apply;Weight=0;}
        void Bind()
        {
            actor=GetComponentInParent<ArenaBossBrain>().transform;
            var bones=GetComponentsInChildren<Transform>(true);
            Transform Bone(string n)=>bones.Single(t=>t.name==n);
            leftFoot=Bone("foot.L");rightFoot=Bone("foot.R");chest=Bone("chest");head=Bone("head");
            soleMotor=GetComponentInParent<ArenaBossBrain>().PatrolPlanner.Motor;
            left=new Arm(Bone("upper_arm.L"),Bone("forearm.L"),Bone("hand.L"),actor);
            right=new Arm(Bone("upper_arm.R"),Bone("forearm.R"),Bone("hand.R"),actor);
            Ready=true;
        }
        void Apply(AnimationClip clip,float time)
        {
            Weight=0;
            if(!isActiveAndEnabled)return;
            if(clip==start)Weight=Ease(time/.45f);
            else if(clip==loop)Weight=1;
            else if(clip==stop)Weight=Ease((clip.length-time)/.45f);
            else if(patrol.Contains(clip))Weight=Ease(time/.45f)*Ease((clip.length-time)/.45f);
            if(Weight<=0)return;
            if(!Ready)Bind();
            // The existing feet, sampled from the same authored frame, supply the gait phase.
            float separation=actor.InverseTransformPoint(leftFoot.position).z-actor.InverseTransformPoint(rightFoot.position).z;
            // A continuous bounded response keeps the authored foot phase without
            // flat target plateaus at the old +/-0.75m hard clamp. The 0.27m
            // asymptote keeps the measured 1.19m source-stride peak near 0.23m.
            float swing=.27f*separation/Mathf.Sqrt(separation*separation+.75f*.75f);
            // Gentle upper-body response to the calibrated sole points in this
            // authored pose. These are sampled before the final Patrol18 foot
            // solve: a gait cue, not a claim to simulate the physical COM.
            float lift=soleMotor.RightSole.y-soleMotor.LeftSole.y;
            float lean=2f*lift/Mathf.Sqrt(lift*lift+.1f*.1f)*Weight;
            var sourceHead=head.rotation;
            chest.rotation=Quaternion.AngleAxis(lean,actor.forward)*chest.rotation;
            // Keep the gaze quieter than the chest without freezing the head.
            head.rotation=Quaternion.AngleAxis(lean*.35f,actor.forward)*sourceHead;
            left.Pose(actor,-swing,Weight);right.Pose(actor,swing,Weight);
        }
        static float Ease(float t)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(t));
        sealed class Arm
        {
            readonly Transform upper,lower,hand;readonly float a,b,side;
            public Arm(Transform u,Transform l,Transform h,Transform actor)
            {upper=u;lower=l;hand=h;a=Vector3.Distance(u.position,l.position);b=Vector3.Distance(l.position,h.position);side=Mathf.Sign(actor.InverseTransformPoint(u.position).x);}
            public void Pose(Transform actor,float swing,float weight)
            {
                var sourceUpper=upper.localRotation;var sourceLower=lower.localRotation;
                var sourceHand=hand.localRotation;
                Vector3 shoulder=upper.position;
                Vector3 target=shoulder+actor.up*(-.88f*(a+b))+actor.right*(side*.13f)+actor.forward*(.08f+swing);
                Vector3 direction=target-shoulder;
                float d=Mathf.Clamp(direction.magnitude,Mathf.Abs(a-b)+.001f,a+b-.008f);
                direction.Normalize();target=shoulder+direction*d;
                Vector3 pole=actor.right*(side*.32f)-actor.up*.3f-actor.forward*.12f;
                Vector3 bend=Vector3.ProjectOnPlane(pole,direction).normalized;
                float along=(a*a-b*b+d*d)/(2*d),height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
                Vector3 elbow=shoulder+direction*along+bend*height;
                upper.rotation=Quaternion.FromToRotation(lower.position-shoulder,elbow-shoulder)*upper.rotation;
                lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,target-lower.position)*lower.rotation;
                var relaxedUpper=upper.localRotation;var relaxedLower=lower.localRotation;
                upper.localRotation=Quaternion.Slerp(sourceUpper,relaxedUpper,weight);
                lower.localRotation=Quaternion.Slerp(sourceLower,relaxedLower,weight);
                hand.localRotation=sourceHand;
            }
        }
    }
}
