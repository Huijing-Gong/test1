using System.Collections.Generic;
using UnityEngine;

// The blade mesh pivots at its guard, not at the grip centre. Align the actual
// handle with the palm after animation, then retain a closed right-hand grasp.
[DefaultExecutionOrder(350)]
[DisallowMultipleComponent]
public sealed class WorldSwordGrip : MonoBehaviour
{
    public Vector3 handleCentre=new Vector3(0,-.12f,0);
    public Vector3 palmInset=new Vector3(.022f,-.008f,0);
    Animator animator;PlayerEquipment equipment;Transform hand,index,little;
    Transform[] fingers;Quaternion[] rotations;
    void LateUpdate()=>ApplyGrip();
    public void ApplyGrip()
    {
        if(!animator){animator=GetComponentInChildren<Animator>(true);equipment=GetComponent<PlayerEquipment>();}
        if(!animator||!animator.isHuman||!equipment||!equipment.PracticeSword||!equipment.PracticeSword.activeInHierarchy||!equipment.WeaponSocket)return;
        if(fingers==null)Capture();
        for(int i=0;i<fingers.Length;i++)if(fingers[i])fingers[i].localRotation=rotations[i];
        var palm=(index.position+little.position)*.5f+hand.TransformVector(palmInset);
        var sword=equipment.PracticeSword.transform;
        equipment.WeaponSocket.position+=palm-sword.TransformPoint(handleCentre);
    }
    void Capture()
    {
        hand=animator.GetBoneTransform(HumanBodyBones.RightHand);index=animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);little=animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
        var all=animator.GetComponentsInChildren<Transform>(true);var positions=new Vector3[all.Length];var poses=new Quaternion[all.Length];
        for(int i=0;i<all.Length;i++){positions[i]=all[i].localPosition;poses[i]=all[i].localRotation;}
        var list=new List<Transform>();for(int i=(int)HumanBodyBones.RightThumbProximal;i<=(int)HumanBodyBones.RightLittleDistal;i++){var bone=animator.GetBoneTransform((HumanBodyBones)i);if(bone)list.Add(bone);}
        fingers=list.ToArray();rotations=new Quaternion[fingers.Length];
        using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))try {
            var pose=new HumanPose();handler.GetHumanPose(ref pose);
            for(int i=0;i<HumanTrait.MuscleCount;i++){string n=HumanTrait.MuscleName[i];if(!n.StartsWith("Right ")||!(n.Contains("Thumb")||n.Contains("Index")||n.Contains("Middle")||n.Contains("Ring")||n.Contains("Little")))continue;pose.muscles[i]=n.Contains("Spread")?(n.Contains("Thumb")?-.25f:0):n.Contains("Thumb")?-.45f:-.7f;}
            handler.SetHumanPose(ref pose);for(int i=0;i<fingers.Length;i++)rotations[i]=fingers[i].localRotation;
        } finally{for(int i=0;i<all.Length;i++){all[i].localPosition=positions[i];all[i].localRotation=poses[i];}}
    }
}
