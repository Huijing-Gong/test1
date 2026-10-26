using UnityEngine;
using UnityEngine.SceneManagement;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        // The accepted contact is copied for every simulation frame. Historical blade
        // queries evaluate this value, never admit ground contacts or advance state.
        struct CrossContact
        {
            public bool valid;
            public int side;
            public string action;
            public Vector3 ankle,pole;
            public Quaternion rotation;
            public float holdEnd,releaseEnd;
            public Collider ground;
            public Vector3 groundPosition,groundScale;
            public Quaternion groundRotation;
        }
        CrossContact crossContact,crossFrame;
        float crossPhaseStart,crossPhaseEnd;
        bool frameHistoryDiscontinuous;
        Transform crossHips;
        readonly Transform[] crossThigh=new Transform[2],crossKnee=new Transform[2],crossFoot=new Transform[2];
        readonly Vector3[] crossTargets=new Vector3[2],crossPoles=new Vector3[2];
        readonly Quaternion[] crossRotations=new Quaternion[2];
        readonly float[] crossLengths=new float[2];
        readonly RaycastHit[] crossGroundHits=new RaycastHit[16];
        public bool CrossContactActive=>crossContact.valid;
        public string CrossContactFailure {get;private set;}
        public float CrossContactWeight {get;private set;}
        public Vector3 CrossContactHipOffset {get;private set;}
        public float CrossContactError {get;private set;}
        bool CrossBones()
        {
            if(crossHips)return true;
            crossHips=animator.GetBoneTransform(HumanBodyBones.Hips);
            for(int s=0;s<2;s++)
            {
                crossThigh[s]=animator.GetBoneTransform(s==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
                crossKnee[s]=animator.GetBoneTransform(s==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
                crossFoot[s]=animator.GetBoneTransform(s==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                if(!crossThigh[s]||!crossKnee[s]||!crossFoot[s]){crossHips=null;return false;}
            }
            return crossHips;
        }
        bool CrossGround(Vector3 ankle,out RaycastHit floor)
        {
            floor=default;float best=float.MaxValue;
            int n=gameObject.scene.GetPhysicsScene().Raycast(ankle+Vector3.up*.3f,Vector3.down,crossGroundHits,1.25f,~0,QueryTriggerInteraction.Ignore);
            if(n==crossGroundHits.Length)return false;
            var capsule=GetComponentInParent<CharacterController>();
            for(int i=0;i<n;i++)
            {
                var h=crossGroundHits[i];
                if(h.collider is CharacterController||h.rigidbody||h.normal.y<.99863f||h.distance>=best)continue;
                if(h.collider.transform.IsChildOf(transform)||(capsule&&h.collider.transform.IsChildOf(capsule.transform)))continue;
                best=h.distance;floor=h;
            }
            return best<float.MaxValue;
        }
        bool CrossSoleHeights(out float left,out float right)
        {
            left=right=float.MaxValue;var skin=GetComponentInChildren<SkinnedMeshRenderer>();if(!skin||!skin.sharedMesh)return false;
            var mesh=new Mesh();
            try
            {
                skin.BakeMesh(mesh,true);var v=mesh.vertices;var w=skin.sharedMesh.boneWeights;
                for(int s=0;s<2;s++)
                {
                    var toe=animator.GetBoneTransform(s==0?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);
                    int a=System.Array.IndexOf(skin.bones,crossFoot[s]),b=System.Array.IndexOf(skin.bones,toe);float low=float.MaxValue;
                    for(int i=0;i<w.Length;i++)
                    {
                        var x=w[i];float sum=((x.boneIndex0==a||x.boneIndex0==b)?x.weight0:0)+((x.boneIndex1==a||x.boneIndex1==b)?x.weight1:0)+((x.boneIndex2==a||x.boneIndex2==b)?x.weight2:0)+((x.boneIndex3==a||x.boneIndex3==b)?x.weight3:0);
                        if(sum>.75f)low=Mathf.Min(low,skin.transform.TransformPoint(v[i]).y);
                    }
                    if(s==0)left=low;else right=low;
                }
                return left<float.MaxValue&&right<float.MaxValue;
            }
            finally{if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
        }
        void PrepareCrossContact(HeroActionDefinition next,float seconds,float dt,float rate,bool changed)
        {
            CrossContactFailure=null;frameHistoryDiscontinuous=false;
            bool enabled=next.commonCombatStance||(currentDefinition!=null&&currentDefinition.commonCombatStance);
            if(!enabled){crossContact=default;return;}
            if(hasRootSample&&(Vector3.Distance(transform.position,lastRootPosition)>.45f||Mathf.Abs(transform.position.y-lastRootPosition.y)>.08f||Quaternion.Angle(transform.rotation,lastRootRotation)>40f))
            {crossContact=default;frameHistoryDiscontinuous=true;CrossContactFailure="Discontinuous root";return;}
            if(!next.commonCombatStance){crossContact=default;return;}
            var supportCapsule=GetComponentInParent<CharacterController>();
            if(supportCapsule&&!supportCapsule.isGrounded){crossContact=default;CrossContactFailure="Capsule airborne";return;}
            if(!changed)
            {
                if(crossContact.valid&&(!crossContact.ground||!crossContact.ground.enabled||!crossContact.ground.gameObject.activeInHierarchy||Vector3.Distance(crossContact.ground.transform.position,crossContact.groundPosition)>.001f||Quaternion.Angle(crossContact.ground.transform.rotation,crossContact.groundRotation)>.1f||Vector3.Distance(crossContact.ground.transform.lossyScale,crossContact.groundScale)>.001f))
                {crossContact=default;CrossContactFailure="Support removed";}
                return;
            }
            crossContact=default;
            if(!committed||!hasRootSample||poseBones==null||currentDefinition==null||!currentDefinition.commonCombatStance||!CrossBones())return;
            Vector3 position=transform.position;Quaternion rotation=transform.rotation;
            try
            {
                // Controllers already moved this frame. Rebuild the visible prior pose
                // at its prior world root before capturing a stationary support anchor.
                transform.SetPositionAndRotation(lastRootPosition,lastRootRotation);RestoreCommittedForStop();
                if(!CrossGround(crossFoot[0].position,out var l)||!CrossGround(crossFoot[1].position,out var r)||Mathf.Abs(l.point.y-r.point.y)>.03f)
                {CrossContactFailure="No shared static flat support";return;}
                if(!CrossSoleHeights(out float ly,out float ry)){CrossContactFailure="No shoe geometry";return;}
                float incoming=(seconds-dt*rate)/Mathf.Max(.001f,next.duration)-next.phaseOffset;
                float p=Mathf.Repeat(incoming,1);int side=p<.5f?0:1;
                float sole=side==0?ly:ry,floor=side==0?l.point.y:r.point.y;
                // Compare the prior visible pose in its calibrated support space.
                // Existing uncalibrated preview hosts retain their original clearance.
                if(supportCapsule)floor+=Mathf.Min(.08f,supportCapsule.skinWidth*Mathf.Abs(supportCapsule.transform.lossyScale.y))+AppliedSupportOffset;
                if(sole-floor<-.015f||sole-floor>.045f){CrossContactFailure="Support shoe not grounded";return;}
                float cycle=Mathf.Floor(incoming),end=cycle+.5f+side*.5f;
                if(incoming>=end){CrossContactFailure="Support phase expired";return;}
                crossContact=new CrossContact{valid=true,action=next.id,side=side,ankle=crossFoot[side].position,rotation=crossFoot[side].rotation,pole=(crossKnee[side].position-crossThigh[side].position).normalized,holdEnd=cycle+.25f+side*.5f,releaseEnd=end,ground=side==0?l.collider:r.collider};
                var support=crossContact.ground.transform;crossContact.groundPosition=support.position;crossContact.groundRotation=support.rotation;crossContact.groundScale=support.lossyScale;
            }
            finally{transform.SetPositionAndRotation(position,rotation);RestoreCommittedForStop();}
        }
        void FreezeCrossContact()
        {
            crossFrame=crossContact;
            if(!crossFrame.valid)return;
            crossPhaseStart=frameCurrentStart/Mathf.Max(.001f,currentDefinition.duration)-currentDefinition.phaseOffset;
            crossPhaseEnd=CurrentTime/Mathf.Max(.001f,currentDefinition.duration)-currentDefinition.phaseOffset;
            if(crossPhaseEnd<crossPhaseStart-.0001f||crossFrame.action!=CurrentAction){crossContact=crossFrame=default;return;}
            if(crossPhaseEnd>=crossFrame.releaseEnd)crossContact=default;
        }
        bool EvaluateCrossContact(float fraction,out float weight,out Vector3 shift,out float error)
        {
            weight=0;shift=Vector3.zero;error=0;
            if(!crossFrame.valid||currentDefinition==null||!currentDefinition.commonCombatStance)return true;
            float phase=Mathf.Lerp(crossPhaseStart,crossPhaseEnd,fraction);
            weight=1-StopEase((phase-crossFrame.holdEnd)/Mathf.Max(.001f,crossFrame.releaseEnd-crossFrame.holdEnd));
            if(weight<=.00001f)return true;
            for(int s=0;s<2;s++)
            {
                crossTargets[s]=crossFoot[s].position;crossRotations[s]=crossFoot[s].rotation;
                crossPoles[s]=(crossKnee[s].position-crossThigh[s].position).normalized;
                crossLengths[s]=Vector3.Distance(crossThigh[s].position,crossKnee[s].position)+Vector3.Distance(crossKnee[s].position,crossFoot[s].position)-.004f;
            }
            int side=crossFrame.side;crossTargets[side]=Vector3.Lerp(crossTargets[side],crossFrame.ankle,weight);
            crossRotations[side]=Quaternion.Slerp(crossRotations[side],crossFrame.rotation,weight);
            crossPoles[side]=Vector3.Slerp(crossPoles[side],crossFrame.pole,weight);
            for(int k=0;k<40;k++)
            {
                for(int s=0;s<2;s++){Vector3 v=crossThigh[s].position+shift-crossTargets[s];float d=v.magnitude;if(d>crossLengths[s])shift-=v*((d-crossLengths[s])/d);}
                shift.y=Mathf.Clamp(shift.y,-.045f,0);Vector2 horizontal=new Vector2(shift.x,shift.z);if(horizontal.magnitude>.08f){horizontal=horizontal.normalized*.08f;shift.x=horizontal.x;shift.z=horizontal.y;}
            }
            for(int s=0;s<2;s++)
            {
                float upper=Vector3.Distance(crossThigh[s].position,crossKnee[s].position),lower=Vector3.Distance(crossKnee[s].position,crossFoot[s].position);
                Vector3 axis=crossTargets[s]-(crossThigh[s].position+shift);float d=axis.magnitude;
                if(d>upper+lower-.00001f||d<Mathf.Abs(upper-lower)+.00001f||Vector3.ProjectOnPlane(crossPoles[s],axis).sqrMagnitude<1e-7f){shift=Vector3.zero;return false;}
            }
            crossHips.position+=shift;
            for(int s=0;s<2;s++)
            {
                var thigh=crossThigh[s];var knee=crossKnee[s];var foot=crossFoot[s];Vector3 a=thigh.position,b=knee.position,c=foot.position,axis=crossTargets[s]-a;
                float upper=Vector3.Distance(a,b),lower=Vector3.Distance(b,c),d=axis.magnitude;axis.Normalize();Vector3 bend=Vector3.ProjectOnPlane(crossPoles[s],axis).normalized;
                float along=(upper*upper-lower*lower+d*d)/(2*d);Vector3 joint=a+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
                thigh.rotation=Quaternion.FromToRotation(b-a,joint-a)*thigh.rotation;
                knee.rotation=Quaternion.FromToRotation(foot.position-knee.position,crossTargets[s]-knee.position)*knee.rotation;foot.rotation=crossRotations[s];
            }
            for(int s=0;s<2;s++)error=Mathf.Max(error,Vector3.Distance(crossFoot[s].position,crossTargets[s]));
            return true;
        }
        void ApplyCrossContactForDisplay()
        {
            bool solved=EvaluateCrossContact(1,out var weight,out var shift,out var error);
            CrossContactWeight=weight;CrossContactHipOffset=shift;CrossContactError=error;
            if(!solved){crossContact=default;CrossContactFailure="Unreachable contact";}
        }
        void ResetCrossContact(){crossContact=crossFrame=default;frameHistoryDiscontinuous=false;CrossContactFailure=null;CrossContactWeight=CrossContactError=0;CrossContactHipOffset=Vector3.zero;}
    }
}
