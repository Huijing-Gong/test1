using UnityEngine;
using UnityEngine.Playables;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        bool snapshotTransition,snapshotUpperOnly,preserveRootOnTransition;
        public void PreserveRootOnNextTransition(){preserveRootOnTransition=true;}
        public void CancelRootTransitionPreservation(){preserveRootOnTransition=false;}
        bool upperBlendEnabled;
        float upperBlendAge,upperBlendDuration,frameUpperBlendStart;
        bool[] upperGaitBones;
        Vector3[] upperBasePositions,upperBaseScales;
        Quaternion[] upperBaseRotations;

        // Preserve the existing lower-body fade. Only bound bones from Spine upward
        // use a longer eased transition; attachments retain their base local pose.
        // The same sampler is used by historical blade queries without advancing age.
        void ApplyUpperGaitBlend(HeroActionDefinition def,float time,float age)
        {
            if(!upperBlendEnabled||age>=upperBlendDuration||poseBones==null)return;
            if(upperGaitBones==null||upperGaitBones.Length!=poseBones.Length)
            {
                int n=poseBones.Length;upperGaitBones=new bool[n];
                upperBasePositions=new Vector3[n];upperBaseScales=new Vector3[n];upperBaseRotations=new Quaternion[n];
                var spine=animator.GetBoneTransform(HumanBodyBones.Spine);
                var skins=animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for(int i=0;i<n;i++)
                {
                    var bone=poseBones[i];if(!spine||!bone||bone==transform||!(bone==spine||bone.IsChildOf(spine)))continue;
                    foreach(var skin in skins)foreach(var bound in skin.bones)if(bound==bone)upperGaitBones[i]=true;
                    for(int j=0;j<(int)HumanBodyBones.LastBone;j++)if(animator.GetBoneTransform((HumanBodyBones)j)==bone)upperGaitBones[i]=true;
                }
            }
            for(int i=0;i<poseBones.Length;i++)if(poseBones[i])
            {upperBasePositions[i]=poseBones[i].localPosition;upperBaseRotations[i]=poseBones[i].localRotation;upperBaseScales[i]=poseBones[i].localScale;}
            float prior=mixer.GetInputWeight(0),next=mixer.GetInputWeight(1);
            float t=Mathf.Clamp01(age/upperBlendDuration),weight=t*t*(3-2*t);
            try
            {
                mixer.SetInputWeight(0,snapshotTransition?0:1-weight);mixer.SetInputWeight(1,snapshotTransition?1:weight);
                PreparePoseCorrectionEvaluation();graph.Evaluate(0);ApplyPoseCorrection(def,time);ApplyTransitionSnapshot(weight,true);
            }
            finally
            {
                mixer.SetInputWeight(0,prior);mixer.SetInputWeight(1,next);
                for(int i=0;i<poseBones.Length;i++)if(poseBones[i]&&!upperGaitBones[i])
                {poseBones[i].localPosition=upperBasePositions[i];poseBones[i].localRotation=upperBaseRotations[i];poseBones[i].localScale=upperBaseScales[i];}
            }
        }
        Vector3[] transitionPositions,transitionScales;
        Quaternion[] transitionRotations;
        // An interrupted fade starts from the visible pose, not one of its constituent clips.
        // Independent storage keeps blade queries and committed contact poses read-only.
        void CaptureTransitionSnapshot(bool upperOnly=false,bool groundActionFeet=false,bool combatStopFeet=false)
        {
            if(poseBones==null)return;
            if(transitionPositions==null||transitionPositions.Length!=poseBones.Length)
            {transitionPositions=new Vector3[poseBones.Length];transitionRotations=new Quaternion[poseBones.Length];transitionScales=new Vector3[poseBones.Length];}
            for(int i=0;i<poseBones.Length;i++)
            {transitionPositions[i]=posePositions[i];transitionRotations[i]=poseRotations[i];transitionScales[i]=poseScales[i];}
            CaptureSupportSnapshot();
            bool preserveWorldPose=preserveRootOnTransition||(transitionFeet&&transitionFeetPreserveRoot&&snapshotTransition&&blend<1);
            if(preserveWorldPose&&hasRootSample&&supportHips)
            {
                // The host may fall or settle before LateUpdate. Keep the departing pose in world space for its first blend.
                var rootDelta=lastRootRotation*Quaternion.Inverse(transform.rotation);
                for(int i=0;i<poseBones.Length;i++)if(poseBones[i]==supportHips){var parent=supportHips.parent;var world=parent.TransformPoint(transitionPositions[i]);transitionPositions[i]=parent.InverseTransformPoint(lastRootPosition+rootDelta*(world-transform.position));transitionRotations[i]=Quaternion.Inverse(parent.rotation)*rootDelta*parent.rotation*transitionRotations[i];break;}
            }
            CaptureTransitionFeet((preserveWorldPose||groundActionFeet||combatStopFeet)&&!upperOnly,preserveWorldPose,groundActionFeet&&!upperOnly,combatStopFeet&&!groundActionFeet&&!upperOnly);
            preserveRootOnTransition=false;snapshotTransition=true;snapshotUpperOnly=upperOnly;
        }
        void ApplyTransitionSnapshot(float weight,bool upperPass=false)
        {
            if(!snapshotTransition||poseBones==null||(snapshotUpperOnly&&!upperPass))return;
            if(!upperPass&&!snapshotUpperOnly)CaptureTransitionFootTargets();
            for(int i=0;i<poseBones.Length;i++)if(poseBones[i]&&poseBones[i]!=transform)
            {
                var t=poseBones[i];t.localPosition=Vector3.Lerp(transitionPositions[i],t.localPosition,weight);
                t.localRotation=Quaternion.Slerp(transitionRotations[i],t.localRotation,weight);
                t.localScale=Vector3.Lerp(transitionScales[i],t.localScale,weight);
            }
            if(!upperPass&&!snapshotUpperOnly)ApplyTransitionFeet(weight);
        }
    }
}
