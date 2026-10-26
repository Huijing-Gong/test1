using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroTraversal
    {
        HeroActionDefinition ledgeTopDefinition;
        Vector3 ledgeTopEntry;
        Quaternion ledgeTopFacing;
        float ledgeApproachStall;
        public HeroActionDefinition PoseDefinition=>TopRopeExitActive?ropeTopExitDefinition:GroundRopeExitActive?ropeGroundExitDefinition:phase==-3?ledgeTopDefinition:null;

        bool PrepareLedgeTopEntry(HeroTraversalSurface target)
        {
            var d=hero.Library.ledgeTopEntry;
            if(d==null||d.id!="LEDGE_GRAB"||!d.clip||d.loop||Mathf.Abs(d.duration-d.clip.length)>.002f||
                !hero.pose.PoseCorrectionReady(d)||d.traversalAscentClip!=d.clip||d.traversalAscent==null||d.traversalAscent.length<2||d.traversalAdvance==null||d.traversalAdvance.length<2)return false;
            if(Mathf.Abs(d.traversalAscent.Evaluate(0)-1)>.001f||Mathf.Abs(d.traversalAdvance.Evaluate(0)-1)>.001f||
                Mathf.Abs(d.traversalAscent.Evaluate(1))>.001f||Mathf.Abs(d.traversalAdvance.Evaluate(1))>.001f)return false;
            // This pair was measured against the existing island ledge, not arbitrary geometry.
            if(Vector3.Distance(transform.lossyScale,Vector3.one)>.001f||Mathf.Abs(capsule.height-1.8f)>.01f||Mathf.Abs(capsule.radius-.3f)>.01f)return false;
            var forward=Vector3.ProjectOnPlane(target.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.99f||Vector3.Dot(target.transform.up,Vector3.up)<.999f)return false;
            BoxCollider block=null;
            if(target.contactSolids!=null)foreach(var c in target.contactSolids)if(c&&c.enabled&&!c.isTrigger){if(block||!(c is BoxCollider))return false;block=(BoxCollider)c;}
            if(!block||Vector3.Dot(block.transform.up,Vector3.up)<.999f||Mathf.Abs(Vector3.Dot(block.transform.forward,forward))<.999f)return false;
            var size=Vector3.Scale(block.size,block.transform.lossyScale);var center=block.transform.TransformPoint(block.center);
            var front=center-forward*size.z*.5f;front.y=center.y+size.y*.5f;
            var offset=front-target.Bottom;var span=target.Top-target.Bottom;
            if(size.x<1.55f||Mathf.Abs(Vector3.Dot(offset,forward)-.4f)>.025f||Mathf.Abs(offset.y-1.68f)>.025f||
                Mathf.Abs(Vector3.Dot(span,forward)-1)>.025f||Mathf.Abs(span.y-1.75f)>.025f||Mathf.Abs(Vector3.Dot(span,target.transform.right))>.025f)return false;
            ledgeTopEntry=target.Top+forward*.65f;ledgeTopFacing=Quaternion.LookRotation(forward,Vector3.up);
            if(Mathf.Abs(transform.position.y-ledgeTopEntry.y)>.15f||Vector3.Distance(transform.position,ledgeTopEntry)>1.5f||!ClearStanding(ledgeTopEntry))return false;
            ledgeTopDefinition=d;return true;
        }

        void UpdateLedgeTopEntry(float dt,bool cancel)
        {
            if(cancel||(vitals&&!vitals.CanExert)){if(cancel)movement.ConsumeJumpInputForFrame();Cancel();return;}
            if(phase==-2)
            {
                var remaining=Vector3.ProjectOnPlane(ledgeTopEntry-transform.position,Vector3.up);float before=remaining.magnitude;
                var facing=before>.02f?Quaternion.LookRotation(remaining.normalized,Vector3.up):ledgeTopFacing;
                transform.rotation=Quaternion.RotateTowards(transform.rotation,facing,240*dt);
                var old=transform.position;var flags=capsule.Move(Vector3.ClampMagnitude(remaining,1.2f*dt)-Vector3.up*(3*dt));
                float moved=Vector3.ProjectOnPlane(transform.position-old,Vector3.up).magnitude;
                if(moved>.0001f){Action("LOC_WALK");var walk=hero.Library.Find(ActionId);ActionTime+=moved*Mathf.Max(.01f,walk.duration)/Mathf.Max(.01f,walk.cycleDistance);}
                else{Action("LOC_IDLE");ActionTime+=dt;}
                float distance=Vector3.ProjectOnPlane(ledgeTopEntry-transform.position,Vector3.up).magnitude;
                ledgeApproachStall=before>.02f&&before-distance<.0005f?ledgeApproachStall+dt:0;
                if(distance<=.002f&&Quaternion.Angle(transform.rotation,ledgeTopFacing)<1)
                {
                    if((flags&CollisionFlags.Below)==0||Mathf.Abs(transform.position.y-ledgeTopEntry.y)>.025f||!ClearStanding(ledgeTopEntry)){Cancel();return;}
                    IgnoreContactSolids();phase=-3;phaseTime=ActionTime=0;ActionId=null;Action("LEDGE_GRAB");return;
                }
                if(phaseTime>4||ledgeApproachStall>.6f)Cancel();return;
            }
            ActionTime=Mathf.Min(ActionTime+dt,Duration);
            float u=Mathf.Clamp01(ActionTime/Mathf.Max(.1f,Duration));var span=ledgeTopEntry-surface.Bottom;
            var target=surface.Bottom+span*Mathf.Clamp01(ledgeTopDefinition.traversalAdvance.Evaluate(u));
            target.y=surface.Bottom.y+span.y*Mathf.Clamp01(ledgeTopDefinition.traversalAscent.Evaluate(u));
            capsule.Move(target-transform.position);
            if(Vector3.Distance(transform.position,target)>.08f){Cancel();return;}
            if(u>=1){phase=2;phaseTime=0;along=0;ledgeTopDefinition=null;Action("WALL_LEDGE_HANG");}
        }
    }
}

