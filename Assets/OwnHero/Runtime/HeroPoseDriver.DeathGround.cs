using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        readonly RaycastHit[] deathFloorHits=new RaycastHit[24];
        public float DeathGroundOffset{get;private set;}
        // This curve measures the actual hero skin against the source clip, not
        // the standing capsule centre. Never apply corpse contact to airborne death.
        void ApplyAuthoredDeathSupport(HeroActionDefinition definition,float time)
        {
            DeathGroundOffset=0;
            if(definition==null||(definition.id!="DEATH_GROUND"&&definition.id!="DEATH_HOLD")||
                !definition.clip||definition.supportCompensationClip!=definition.clip||definition.authoredSupportOffset==null||definition.authoredSupportOffset.length<2)return;
            if(!supportHost)supportHost=GetComponentInParent<CharacterController>();
            if(!supportHost||!supportHost.enabled)return;
            if(!supportHips)supportHips=animator.GetBoneTransform(HumanBodyBones.Hips);if(!supportHips)return;
            Vector3 foot=supportHost.transform.TransformPoint(supportHost.center-Vector3.up*supportHost.height*.5f);
            int count=Physics.RaycastNonAlloc(foot+Vector3.up*.25f,Vector3.down,deathFloorHits,.65f,~0,QueryTriggerInteraction.Ignore);
            float distance=float.PositiveInfinity,ground=0;
            for(int i=0;i<count;i++){
                var hit=deathFloorHits[i];if(!hit.collider||hit.transform.IsChildOf(supportHost.transform)||hit.normal.y<.55f||hit.collider.GetComponentInParent<CharacterController>())continue;
                if(hit.distance<distance){distance=hit.distance;ground=hit.point.y;}
            }
            if(!float.IsFinite(distance)||!supportHost.isGrounded&&distance>.29f)return;
            float scale=definition.bodyEnvelopeScale.y>0?Mathf.Abs(transform.lossyScale.y)/definition.bodyEnvelopeScale.y:1;
            float min=definition.authoredSupportOffset.Evaluate((float)AuthorTime(definition,time))*scale;
            if(!float.IsFinite(min))return;
            DeathGroundOffset=Mathf.Clamp(ground+.006f-transform.position.y-min,-1.5f,.35f);
            supportHips.position+=Vector3.up*DeathGroundOffset;
        }
    }
}
