using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroTraversal
    {
        Vector3 mantleEntry,mantleTop;
        Quaternion mantleFacing;
        float approachStall;
        float supportStableTime,previousSupportY,mantleSurfaceY;
        string mantleAction;
        readonly Transform[] supportUpper=new Transform[4],supportLower=new Transform[4],supportEnd=new Transform[4];
        readonly Vector3[] supportTarget=new Vector3[4],supportPole=new Vector3[4];
        readonly Quaternion[] supportRotation=new Quaternion[4];
        readonly float[] supportWeight=new float[4];
        // This correction was authored against a 1.10m high, 1.30m deep block.
        // Other facilities retain their existing path until they have their own profile.
        bool PrepareCanonicalMantle(HeroTraversalSurface target,HeroActionDefinition definition)
        {
            if(Vector3.Distance(transform.lossyScale,Vector3.one)>.001f||Vector3.Dot(transform.up,Vector3.up)<.999f||Mathf.Abs(capsule.height-1.8f)>.01f||Mathf.Abs(capsule.radius-.3f)>.01f)return false;
            if(!hero.pose.PoseCorrectionReady(definition)||definition.traversalAscentClip!=definition.clip||
                definition.traversalAscent==null||definition.traversalAscent.length<2||definition.traversalAdvance==null||definition.traversalAdvance.length<2)return false;
            BoxCollider block=null;
            if(target.contactSolids!=null)foreach(var c in target.contactSolids)if(c is BoxCollider box&&box.enabled&&!box.isTrigger){if(block)return false;block=box;}
            if(!block)return false;
            Vector3 forward=Vector3.ProjectOnPlane(target.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.99f||Vector3.Dot(block.transform.up,Vector3.up)<.999f||Mathf.Abs(Vector3.Dot(block.transform.forward,forward))<.999f)return false;
            Vector3 size=Vector3.Scale(block.size,block.transform.lossyScale);
            if(Mathf.Abs(size.y-1.10f)>.035f||Mathf.Abs(size.z-1.30f)>.035f||size.x<1.25f)return false;
            Vector3 center=block.transform.TransformPoint(block.center);
            Vector3 front=center-forward*(size.z*.5f);front.y=center.y+size.y*.5f;
            mantleEntry=front-forward*.60f;
            bool ground=false;float groundY=float.NegativeInfinity;
            foreach(var hit in Physics.RaycastAll(mantleEntry+Vector3.up*.3f,Vector3.down,2f,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider!=block&&!hit.collider.transform.IsChildOf(transform)&&hit.normal.y>.85f&&hit.point.y>groundY){ground=true;groundY=hit.point.y;}
            if(!ground||Mathf.Abs(front.y-groundY-1.10f)>.04f||Mathf.Abs(transform.position.y-groundY)>.2f)return false;
            float footOffset=capsule.center.y-capsule.height*.5f;
            mantleEntry.y=groundY+capsule.skinWidth-footOffset;mantleTop=center;mantleTop.y=front.y+capsule.skinWidth-footOffset;
            mantleSurfaceY=front.y;supportStableTime=0;previousSupportY=transform.position.y;
            mantleFacing=Quaternion.LookRotation(forward,Vector3.up);
            if(Vector3.Distance(transform.position,mantleEntry)>1.2f||!ClearStanding(mantleEntry)||!ClearStanding(mantleTop,true))return false;
            mantleAction=definition.id;
            return true;
        }
        void UpdateMantleApproach(float dt,bool cancel)
        {
            if(cancel||phaseTime>3||(vitals&&!vitals.CanExert)){if(cancel)movement.ConsumeJumpInputForFrame();Cancel();return;}
            Vector3 remaining=Vector3.ProjectOnPlane(mantleEntry-transform.position,Vector3.up);
            float before=remaining.magnitude;
            transform.rotation=Quaternion.RotateTowards(transform.rotation,mantleFacing,180*dt);
            CollisionFlags flags=CollisionFlags.None;
            if(before>.002f)
            {
                var oldPosition=transform.position;
                flags=capsule.Move(Vector3.ClampMagnitude(remaining,1.2f*dt)-Vector3.up*(3*dt));
                float moved=Vector3.ProjectOnPlane(transform.position-oldPosition,Vector3.up).magnitude;
                if(moved>.0001f){Action("LOC_WALK");var walk=hero.Library.Find("LOC_WALK");ActionTime+=moved*Mathf.Max(.01f,walk.duration)/Mathf.Max(.01f,walk.cycleDistance);}
                else{Action("LOC_IDLE");ActionTime+=dt;}
                float after=Vector3.ProjectOnPlane(mantleEntry-transform.position,Vector3.up).magnitude;
                approachStall=before-after<.0005f?approachStall+dt:0;
            }
            else{Action("LOC_IDLE");ActionTime+=dt;flags=capsule.Move(-Vector3.up*(3*dt));}
            float distance=Vector3.ProjectOnPlane(mantleEntry-transform.position,Vector3.up).magnitude;
            bool supported=(flags&CollisionFlags.Below)!=0&&Mathf.Abs(transform.position.y-previousSupportY)<.001f;
            supportStableTime=distance<=.002f&&supported?supportStableTime+dt:0;previousSupportY=transform.position.y;
            if(distance<=.002f&&Quaternion.Angle(transform.rotation,mantleFacing)<1)
            {
                if(Mathf.Abs(transform.position.y-mantleEntry.y)>.015f||!ClearStanding(mantleTop,true)){Cancel();return;}
                if(supportStableTime<.05f)return;
                start=transform.position;goal=mantleTop;phase=1;phaseTime=0;ActionId=null;Action(mantleAction);IgnoreContactSolids();return;
            }
            if(phaseTime>3||approachStall>.6f)Cancel();
        }
        bool ApplyMantleSupport(HeroPoseDriver pose)
        {
            if(phase!=1||surface.kind!=HeroTraversalKind.Mantle||ActionId!=mantleAction)return false;
            var d=hero.Library.Find(ActionId);if(d==null||!d.canonicalMantleEntry)return false;
            float t=ActionTime,u=Mathf.Clamp01(t/d.duration);
            // The saved pose uses a 1.10m support surface and 1.11m root ascent.
            // Adjust only admitted contact endpoints for measured controller skin/ground offset.
            float dy=mantleSurfaceY-1.10f+d.traversalAscent.Evaluate(u)*1.11f-transform.position.y;
            if(Mathf.Abs(dy)>.08f)return true;
            var a=pose.animator;
            float pelvisDrop=0;
            for(int k=0;k<4;k++)
            {
                bool hand=k<2,left=k==0||k==2;float w;
                if(hand)w=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.35f,t))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(left?2.05f:.85f,left?2.35f:1.15f,t)));
                else w=Mathf.SmoothStep(0,1,Mathf.InverseLerp(left?2.65f:1.65f,left?3.05f:2.05f,t))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(left?3.55f:3.15f,left?3.85f:3.6f,t)));
                supportWeight[k]=w;supportUpper[k]=supportLower[k]=supportEnd[k]=null;
                if(w<=0)continue;
                var upper=a.GetBoneTransform(hand?(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm):(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg));
                var lower=a.GetBoneTransform(hand?(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm):(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg));
                var end=a.GetBoneTransform(hand?(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand):(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot));
                if(!upper||!lower||!end)continue;
                supportUpper[k]=upper;supportLower[k]=lower;supportEnd[k]=end;
                supportTarget[k]=end.position+Vector3.up*(dy*w);supportPole[k]=lower.position;supportRotation[k]=end.rotation;
                if(!hand)
                {
                    // Preserve the authored swing and contact targets. A fixed pelvis can
                    // leave a planted foot unreachable after the controller skin offset.
                    float reach=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,end.position)-.001f;
                    Vector3 delta=supportTarget[k]-upper.position;
                    float vertical=Mathf.Sqrt(Mathf.Max(0,reach*reach-delta.x*delta.x-delta.z*delta.z));
                    pelvisDrop=Mathf.Max(pelvisDrop,upper.position.y-supportTarget[k].y-vertical);
                }
            }
            pelvisDrop=Mathf.Clamp(pelvisDrop,0,.06f);
            var hips=a.GetBoneTransform(HumanBodyBones.Hips);
            if(hips)hips.position-=Vector3.up*pelvisDrop;
            // Solve all admitted limbs against their saved world targets after moving
            // the common pelvis; already planted hands and the other foot stay put.
            for(int k=0;k<4;k++)
            {
                if(!supportEnd[k])continue;
                SolveArm(supportUpper[k],supportLower[k],supportEnd[k],supportTarget[k],supportPole[k]-Vector3.up*pelvisDrop);
                supportEnd[k].rotation=supportRotation[k];
            }
            return true;
        }
    }
}
