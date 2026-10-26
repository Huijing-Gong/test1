using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroTraversal
    {
        public bool RecoveringContact=>recoveryTime>0;
        public uint ContactRecoveryStepCount {get;private set;}
        public bool ContactRecoveryBlocked {get;private set;}
        Vector3 recoveryOutward;float recoveryTarget,recoveryTime,recoveryFace;
        public bool DeathRecoveryApplied {get;private set;}
        public float ContactRecoveryRemaining=>Mathf.Max(0,recoveryTarget-Vector3.Dot(transform.position,recoveryOutward));
        void ExtendRecoveryForDeath()
        {
            var health=GetComponent<Health>();
            if(!RecoveringContact||!health||!health.IsDead||!HeroDeathClearance.TryGetEnvelope(hero.pose,out var envelope))return;
            float extent=HeroDeathClearance.ProjectedExtent(envelope,transform.rotation,-recoveryOutward);
            recoveryTarget=Mathf.Max(recoveryTarget,recoveryFace+extent+.025f);DeathRecoveryApplied=true;
        }
        void BeginMantleRecovery()
        {
            if(RecoveringContact||!surface||surface.kind!=HeroTraversalKind.Mantle||hero.Library.Find(ActionId)?.canonicalMantleEntry!=true)return;
            // A supported top-side exit already has a clear standing pose.
            if(Physics.Raycast(transform.position+Vector3.up*.08f,Vector3.down,out var hit,.18f,~0,QueryTriggerInteraction.Ignore)
                &&hit.normal.y>.85f&&surface.contactSolids!=null&&System.Array.IndexOf(surface.contactSolids,hit.collider)>=0)return;
            recoveryOutward=-Vector3.ProjectOnPlane(surface.transform.forward,Vector3.up).normalized;
            recoveryTarget=Vector3.Dot(mantleEntry,recoveryOutward);recoveryFace=recoveryTarget-.60f;DeathRecoveryApplied=false;
            recoveryTime=.6f;ContactRecoveryBlocked=false;ExtendRecoveryForDeath();
            if(recoveryTarget-Vector3.Dot(transform.position,recoveryOutward)<=.01f){recoveryTime=0;return;}
        }
        public void ResetContactRecovery()
        {
            AbortContactRecovery();recoveryOutward=Vector3.zero;recoveryTarget=recoveryFace=0;
            ContactRecoveryBlocked=DeathRecoveryApplied=false;
        }
        public void AbortContactRecovery()
        {
            recoveryTime=0;
            if(!Active&&ownsLock&&movement){movement.SetActionInputLocked(this,false);ownsLock=false;}
        }
        void UpdateMantleRecovery(float dt)
        {
            if(!capsule||!capsule.enabled){AbortContactRecovery();return;}
            ContactRecoveryStepCount++;
            float remaining=recoveryTarget-Vector3.Dot(transform.position,recoveryOutward);
            if(remaining>.002f)
            {
                // Re-enter through the measured outside face. Move performs collision
                // sweeps; ordinary movement still owns gravity and the real hit/death pose.
                capsule.Move(recoveryOutward*Mathf.Min(remaining,3f*dt));
                remaining=recoveryTarget-Vector3.Dot(transform.position,recoveryOutward);
            }
            recoveryTime-=dt;
            if(remaining<=.002f||recoveryTime<=0)
            {
                ContactRecoveryBlocked=remaining>.002f;
                AbortContactRecovery();hero.SynchronizeAfterTraversal();
            }
        }
    }
}
