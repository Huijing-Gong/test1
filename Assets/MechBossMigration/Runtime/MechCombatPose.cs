using UnityEngine;
namespace KingOfGraphic.MechBossMigration
{
    [RequireComponent(typeof(Animator))]
    public sealed class MechCombatPose:MonoBehaviour
    {
        public MechSentinelBattle battle;Animator animator;
        void Awake(){animator=GetComponent<Animator>();}
        void OnAnimatorIK(int layer)
        {
            if(!battle)return;float t=battle.AttackElapsed;
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand,0);animator.SetIKPositionWeight(AvatarIKGoal.LeftHand,0);
            if(battle.Slamming&&t>.82f&&t<1.65f){float weight=Ease(.82f,1,t)*(1-Ease(1.3f,1.65f,t));Hand(AvatarIKGoal.RightHand,battle.transform.TransformPoint(new Vector3(.12f,.32f,1.4f)),weight);}
        }
        void Hand(AvatarIKGoal hand,Vector3 target,float weight){animator.SetIKPositionWeight(hand,weight);animator.SetIKPosition(hand,target);}
        static float Ease(float a,float b,float t)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,t));
    }
}
