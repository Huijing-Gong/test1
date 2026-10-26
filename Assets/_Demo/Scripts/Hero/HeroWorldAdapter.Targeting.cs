using UnityEngine;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroWorldAdapter
    {
        Health attackAim;
        readonly Collider[] aimCandidates=new Collider[64];
        bool ValidAim(Health target)
        {
            if(!target||target.IsDead||!target.isActiveAndEnabled||target.Team!=HealthTeam.Enemy||!target.CanReceiveDamageFrom(gameObject))return false;
            Vector3 to=target.transform.position-transform.position;
            return Vector3.ProjectOnPlane(to,Vector3.up).sqrMagnitude<=25&&Mathf.Abs(to.y)<2.5f&&
                HeroCombatTravel.ClearSight(transform,target.transform,transform.position+Vector3.up,target.transform.position+Vector3.up);
        }
        void AcquireAttackAim()
        {
            if(FocusEnemy){attackAim=FocusEnemy;return;}
            if(ValidAim(attackAim))return;
            attackAim=null;var camera=Camera.main;Vector3 forward=camera?Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized:transform.forward;
            int count=Physics.OverlapSphereNonAlloc(transform.position+Vector3.up,4.5f,aimCandidates,~0,QueryTriggerInteraction.Ignore);
            float best=float.PositiveInfinity;
            for(int i=0;i<count;i++){
                var target=aimCandidates[i]?aimCandidates[i].GetComponentInParent<Health>():null;
                if(!ValidAim(target))continue;
                Vector3 to=Vector3.ProjectOnPlane(target.transform.position-transform.position,Vector3.up);float distance=to.magnitude;
                float dot=distance>.001f?Vector3.Dot(forward,to/distance):1;
                if(dot<-.25f&&distance>2)continue;
                float score=distance+(1-dot)*1.2f;
                if(score<best){best=score;attackAim=target;}
            }
        }
        void FaceAttackAim(float delta,bool immediate=false)
        {
            if(!ValidAim(attackAim))AcquireAttackAim();
            if(attackAim)HeroCombatTravel.Face(transform,attackAim.transform.position,delta,immediate);
        }
    }
}
