using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroTraversal
    {
        bool ledgeContactRecovery;
        readonly Vector3[] ledgeRecoveryPoints=new Vector3[2];
        readonly RaycastHit[] ledgeRecoveryHits=new RaycastHit[32];
        public string LedgeRecoveryExitReason {get;private set;}
        int ledgeRecoveryPoint,ledgeRecoveryCount;
        Vector3 ledgeRecoveryForward,ledgeRecoveryOutside;
        float ledgeRecoveryElapsed;
        void RestoreContactCollisions(){foreach(var c in ignored)if(c&&capsule)Physics.IgnoreCollision(capsule,c,false);ignored.Clear();}
        bool ContactCapsuleOverlaps()
        {
            foreach(var c in ignored)if(c&&c.enabled&&Physics.ComputePenetration(capsule,transform.position,transform.rotation,c,c.transform.position,c.transform.rotation,out var direction,out var distance)&&distance>.0001f)return true;
            return false;
        }
        bool RecoveryPositionClear(Vector3 position)
        {
            float radius=capsule.radius;var center=position+transform.rotation*capsule.center;float half=Mathf.Max(0,capsule.height*.5f-radius);
            int n=Physics.OverlapCapsuleNonAlloc(center+Vector3.up*half,center-Vector3.up*half,radius,clearance,~0,QueryTriggerInteraction.Ignore);
            if(n==clearance.Length)return false;
            for(int i=0;i<n;i++)
            {
                var c=clearance[i];if(!c||c.transform.IsChildOf(transform)||ignored.Contains(c))continue;
                if(!Physics.ComputePenetration(capsule,position,transform.rotation,c,c.transform.position,c.transform.rotation,out var direction,out var depth))continue;
                if(direction.y>.85f&&depth<=capsule.skinWidth+.003f)continue;
                return false;
            }
            return true;
        }
        bool RecoverySegmentClear(Vector3 a,Vector3 b)
        {
            if(!RecoveryPositionClear(a)||!RecoveryPositionClear(b))return false;
            var delta=b-a;float length=delta.magnitude;if(length<.0001f)return true;
            var direction=delta/length;float radius=capsule.radius;var center=a+transform.rotation*capsule.center;float half=Mathf.Max(0,capsule.height*.5f-radius);
            int count=Physics.CapsuleCastNonAlloc(center+Vector3.up*half,center-Vector3.up*half,radius,direction,ledgeRecoveryHits,length,~0,QueryTriggerInteraction.Ignore);
            if(count==ledgeRecoveryHits.Length)return false;
            for(int i=0;i<count;i++){var hit=ledgeRecoveryHits[i];if(!hit.collider||hit.collider.transform.IsChildOf(transform)||ignored.Contains(hit.collider))continue;if(hit.normal.y>.85f&&Vector3.Dot(direction,hit.normal)>=-.001f)continue;return false;}
            return true;
        }
        bool PlanLedgeRecovery()
        {
            var p=transform.position;var outside=ledgeRecoveryOutside;outside.y=p.y;
            outside+=Vector3.ProjectOnPlane(p-outside,ledgeRecoveryForward);
            if(RecoverySegmentClear(p,outside)){ledgeRecoveryPoints[0]=outside;ledgeRecoveryCount=1;ledgeRecoveryPoint=0;return true;}
            var right=Vector3.Cross(Vector3.up,ledgeRecoveryForward);
            foreach(float lateral in new[]{.7f,-.7f,1.2f,-1.2f,1.7f,-1.7f})
            {
                var side=p+right*lateral;var end=outside+right*lateral;
                if(!RecoverySegmentClear(p,side)||!RecoverySegmentClear(side,end))continue;
                ledgeRecoveryPoints[0]=side;ledgeRecoveryPoints[1]=end;ledgeRecoveryCount=2;ledgeRecoveryPoint=0;return true;
            }
            return false;
        }
        void BeginLedgeContactRecovery()
        {
            if(RecoveringContact||!surface||surface.kind!=HeroTraversalKind.Ledge||phase!=-3||!ContactCapsuleOverlaps())return;
            ledgeRecoveryForward=Vector3.ProjectOnPlane(surface.transform.forward,Vector3.up).normalized;
            // The top entry is admitted only for the measured .4m front-face offset.
            ledgeRecoveryOutside=surface.Bottom+ledgeRecoveryForward*(.4f-capsule.radius-capsule.skinWidth-.02f);
            if(!PlanLedgeRecovery()){ContactRecoveryBlocked=true;LedgeRecoveryExitReason="no_route_legacy_fallback";return;}
            LedgeRecoveryExitReason="recovering";
            ledgeContactRecovery=true;ledgeRecoveryElapsed=0;recoveryTime=2;ContactRecoveryBlocked=false;DeathRecoveryApplied=false;
        }
        void UpdateLedgeContactRecovery(float dt)
        {
            if(!capsule||!capsule.enabled){AbortContactRecovery();return;}
            ContactRecoveryStepCount++;ledgeRecoveryElapsed+=dt;
            if(LedgeRecoveryClear()) { FinishLedgeRecovery();return; }
            var goal=ledgeRecoveryPoints[ledgeRecoveryPoint];goal.y=transform.position.y;
            var before=transform.position;capsule.Move(Vector3.ClampMagnitude(goal-before,3f*dt));
            if(Vector3.ProjectOnPlane(goal-transform.position,Vector3.up).sqrMagnitude<.000004f&&ledgeRecoveryPoint+1<ledgeRecoveryCount)ledgeRecoveryPoint++;
            if(LedgeRecoveryClear()){FinishLedgeRecovery();return;}
            if(ledgeRecoveryElapsed>2||(Vector3.Distance(before,transform.position)<.0001f&&!PlanLedgeRecovery()))
            {
                // Preserve the previous bounded recovery as a last-resort failure path;
                // it remains explicitly reported, never silently accepted as continuity.
                ContactRecoveryBlocked=true;LedgeRecoveryExitReason=ledgeRecoveryElapsed>2?"timeout_legacy_fallback":"blocked_legacy_fallback";AbortContactRecovery();hero.SynchronizeAfterTraversal();
            }
        }
        bool LedgeRecoveryClear()=>!ContactCapsuleOverlaps()&&Vector3.Dot(transform.position-ledgeRecoveryOutside,ledgeRecoveryForward)<=.002f;
        float LedgeRecoveryRemaining(){float d=0;var p=transform.position;for(int i=ledgeRecoveryPoint;i<ledgeRecoveryCount;i++){d+=Vector3.ProjectOnPlane(ledgeRecoveryPoints[i]-p,Vector3.up).magnitude;p=ledgeRecoveryPoints[i];}return d;}
        void FinishLedgeRecovery()
        {
            LedgeRecoveryExitReason="clear";ledgeContactRecovery=false;RestoreContactCollisions();recoveryTime=0;
            if(ownsLock&&movement){movement.SetActionInputLocked(this,false);ownsLock=false;}
            hero.SynchronizeAfterTraversal();
        }
    }
}
