using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KingOfGraphic.Hero
{
    // Runs on each clip before blending, so an outgoing gait keeps its own axis.
    // The movement controller and attack/dodge direction remain unchanged.
    public struct HeroDirectionalStrideJob : IAnimationJob
    {
        public TransformStreamHandle hips,spine;
        public TransformSceneHandle root;
        public float yaw;
        public bool commonStance;
        public Vector3 leftAnchor,rightAnchor;
        public void ProcessRootMotion(AnimationStream stream) {}
        public void ProcessAnimation(AnimationStream stream)
        {
            if(Mathf.Abs(yaw)<.00001f||!hips.IsValid(stream)||!spine.IsValid(stream)||!root.IsValid(stream))return;
            Vector3 pivot=root.GetPosition(stream);Quaternion q=Quaternion.AngleAxis(yaw,root.GetRotation(stream)*Vector3.up);
            Quaternion upper=spine.GetRotation(stream);
            // Humanoid foot IK resolves after this job. Its goals need the same
            // rotation, otherwise it moves the feet back to the old travel axis.
            if(stream.isHumanStream)
            {
                var human=stream.AsHuman();
                if(commonStance)
                {
                    Quaternion rotation=root.GetRotation(stream);
                    RotateStride(human,AvatarIKGoal.LeftFoot,AvatarIKHint.LeftKnee,pivot+rotation*leftAnchor,q);
                    RotateStride(human,AvatarIKGoal.RightFoot,AvatarIKHint.RightKnee,pivot+rotation*rightAnchor,q);
                }
                else
                {
                    RotateGoal(human,AvatarIKGoal.LeftFoot,AvatarIKHint.LeftKnee,pivot,q);
                    RotateGoal(human,AvatarIKGoal.RightFoot,AvatarIKHint.RightKnee,pivot,q);
                }
            }
            hips.SetPosition(stream,pivot+q*(hips.GetPosition(stream)-pivot));
            if(!commonStance)hips.SetRotation(stream,q*hips.GetRotation(stream));
            spine.SetRotation(stream,upper);
        }
        static void RotateStride(AnimationHumanStream human,AvatarIKGoal goal,AvatarIKHint hint,Vector3 pivot,Quaternion q)
        {
            Vector3 before=human.GetGoalPosition(goal),after=pivot+q*(before-pivot);
            human.SetGoalPosition(goal,after);human.SetHintPosition(hint,human.GetHintPosition(hint)+(after-before)*.5f);
        }
        static void RotateGoal(AnimationHumanStream human,AvatarIKGoal goal,AvatarIKHint hint,Vector3 pivot,Quaternion q)
        {
            human.SetGoalPosition(goal,pivot+q*(human.GetGoalPosition(goal)-pivot));
            human.SetGoalRotation(goal,q*human.GetGoalRotation(goal));
            human.SetHintPosition(hint,pivot+q*(human.GetHintPosition(hint)-pivot));
        }
    }
    public sealed partial class HeroPoseDriver
    {
        AnimationScriptPlayable currentStride,previousStride;
        float currentStrideYaw,frameStrideYawStart;
        static float StrideYaw(string id,Vector3? velocity)
        {
            if(!id.StartsWith("COMBAT_MOVE_")||!velocity.HasValue)return 0;
            Vector3 v=velocity.Value;if(v.x*v.x+v.z*v.z<.00000001f)return 0;
            float axis=id.EndsWith("_F")?0:id.EndsWith("_R")?90:id.EndsWith("_B")?180:-90;
            return Mathf.Clamp(Mathf.DeltaAngle(axis,Mathf.Atan2(v.x,v.z)*Mathf.Rad2Deg),-60,60);
        }
        AnimationScriptPlayable CreateStridePlayable(AnimationClipPlayable clip,HeroActionDefinition definition)
        {
            var job=new HeroDirectionalStrideJob {
                hips=animator.BindStreamTransform(animator.GetBoneTransform(HumanBodyBones.Hips)),
                spine=animator.BindStreamTransform(animator.GetBoneTransform(HumanBodyBones.Spine)),
                root=animator.BindSceneTransform(transform),
                commonStance=definition.commonCombatStance,
                leftAnchor=definition.leftStanceAnchor*(definition.stanceHumanScale>.001f?animator.humanScale/definition.stanceHumanScale:1),
                rightAnchor=definition.rightStanceAnchor*(definition.stanceHumanScale>.001f?animator.humanScale/definition.stanceHumanScale:1)
            };
            var playable=AnimationScriptPlayable.Create(graph,job,1);
            graph.Connect(clip,0,playable,0);playable.SetInputWeight(0,1);return playable;
        }
        static void SetStrideYaw(AnimationScriptPlayable playable,float yaw)
        {if(playable.IsValid()){var job=playable.GetJobData<HeroDirectionalStrideJob>();job.yaw=yaw;playable.SetJobData(job);}}
    }
}
