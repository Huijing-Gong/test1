using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        Transform[] sheathBones;
        Vector3[] sheathPositions,sheathScales;
        Quaternion[] sheathRotations;
        // Capture the authored arm target, then let base gait/snapshots finish before applying it.
        // Hips, spine, legs and the controller root are never owned by this layer.
        float CaptureSheathArmTarget()
        {
            if(!frameSheathOverlay||!frameHasOverlay||!overlay.IsValid())return -1;
            float weight=layers.GetInputWeight(2);if(weight<=0)return -1;
            if(sheathBones==null)
            {
                var bones=new List<Transform>();
                foreach(var id in new[]{HumanBodyBones.LeftShoulder,HumanBodyBones.RightShoulder,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightHand})
                {var t=animator.GetBoneTransform(id);if(t)bones.Add(t);}
                for(int i=(int)HumanBodyBones.LeftThumbProximal;i<=(int)HumanBodyBones.RightLittleDistal;i++)
                {var t=animator.GetBoneTransform((HumanBodyBones)i);if(t)bones.Add(t);}
                sheathBones=bones.ToArray();sheathPositions=new Vector3[bones.Count];sheathScales=new Vector3[bones.Count];sheathRotations=new Quaternion[bones.Count];
            }
            layers.SetInputWeight(2,1);PreparePoseCorrectionEvaluation();graph.Evaluate(0);
            for(int i=0;i<sheathBones.Length;i++){var t=sheathBones[i];sheathPositions[i]=t.localPosition;sheathRotations[i]=t.localRotation;sheathScales[i]=t.localScale;}
            layers.SetInputWeight(2,0);
            return weight;
        }
        void ApplySheathArmTarget(float weight)
        {
            if(weight<0)return;
            for(int i=0;i<sheathBones.Length;i++)
            {
                var t=sheathBones[i];t.localPosition=Vector3.Lerp(t.localPosition,sheathPositions[i],weight);
                t.localRotation=Quaternion.Slerp(t.localRotation,sheathRotations[i],weight);t.localScale=Vector3.Lerp(t.localScale,sheathScales[i],weight);
            }
            layers.SetInputWeight(2,weight);
        }
    }
}
