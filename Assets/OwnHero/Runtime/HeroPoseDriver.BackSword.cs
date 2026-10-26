using UnityEngine;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        Transform backSword,chest,carryRoot;
        Vector3 backBladeBase,backBladeDirection;
        Quaternion chestBasis;
        bool swordCarried=true,backDrawn;
        float drawAge=-1;
        public bool BackSwordVisible=>backSword&&backSword.gameObject.activeInHierarchy;
        public bool DrawingFromBack=>drawAge>=0&&drawAge<.12f;
        public void SetSwordCarried(bool carried){swordCarried=carried;if(!carried&&backSword)backSword.gameObject.SetActive(false);}
        void EnsureBackSword()
        {
            if(backSword||!sword||!animator||!animator.isHuman||!WeaponReady)return;
            chest=animator.GetBoneTransform(HumanBodyBones.UpperChest);if(!chest)chest=animator.GetBoneTransform(HumanBodyBones.Chest);
            if(!chest)return;
            var cc=GetComponentInParent<CharacterController>();carryRoot=cc?cc.transform:transform;
            chestBasis=Quaternion.Inverse(chest.rotation)*carryRoot.rotation;
            backBladeBase=sword.InverseTransformPoint(bladeBase.position);
            backBladeDirection=sword.InverseTransformVector(bladeTip.position-bladeBase.position).normalized;
            var copy=Instantiate(sword.gameObject,transform);copy.name="Back carried sword (visual only)";backSword=copy.transform;
            foreach(var c in copy.GetComponentsInChildren<Collider>(true))c.enabled=false;
            foreach(var c in copy.GetComponentsInChildren<MonoBehaviour>(true))c.enabled=false;
            foreach(var c in copy.GetComponentsInChildren<TrailRenderer>(true))c.enabled=false;
            foreach(var c in copy.GetComponentsInChildren<ParticleSystem>(true))c.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            Vector3 parentScale=transform.lossyScale,scale=sword.lossyScale;
            backSword.localScale=new Vector3(scale.x/parentScale.x,scale.y/parentScale.y,scale.z/parentScale.z);
            copy.SetActive(false);
        }
        void SetBackSwordDrawn(bool drawn)
        {
            if(!backDrawn&&drawn&&swordCarried)drawAge=0;
            if(!drawn)drawAge=-1;
            backDrawn=drawn;
        }
        void BackPose(out Vector3 position,out Quaternion rotation)
        {
            Quaternion basis=chest.rotation*chestBasis;
            rotation=basis*Quaternion.FromToRotation(backBladeDirection,new Vector3(-.45f,-.89f,0));
            Vector3 handle=chest.position+basis*new Vector3(.24f,.24f,-.22f);
            position=handle-rotation*Vector3.Scale(backBladeBase,backSword.lossyScale);
        }
        void ApplyFastBackDraw()
        {
            if(!DrawingFromBack||!animator||!chest)return;
            // Brief over-shoulder reach blends out before A1's existing hit window.
            var upper=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            if(!upper||!lower||!hand)return;
            Vector3 target=chest.position+(chest.rotation*chestBasis)*new Vector3(.24f,.24f,-.22f);
            float weight=1-Mathf.SmoothStep(0,1,drawAge/.12f);
            upper.rotation=Quaternion.Slerp(upper.rotation,Quaternion.FromToRotation(lower.position-upper.position,target-upper.position)*upper.rotation,weight*.8f);
            lower.rotation=Quaternion.Slerp(lower.rotation,Quaternion.FromToRotation(hand.position-lower.position,target-lower.position)*lower.rotation,weight);
        }
        void UpdateBackSword()
        {
            EnsureBackSword();if(!backSword)return;
            bool drawing=DrawingFromBack;
            backSword.gameObject.SetActive(swordCarried&&(!backDrawn||drawing)&&isActiveAndEnabled);
            if(backSword.gameObject.activeSelf){
                BackPose(out var position,out var rotation);
                float blend=drawing?Mathf.SmoothStep(0,1,drawAge/.09f):0;
                backSword.SetPositionAndRotation(Vector3.Lerp(position,sword.position,blend),Quaternion.Slerp(rotation,sword.rotation,blend));
            }
            // The original collision blade remains the sole damage source.
            if(sword&&backDrawn)sword.gameObject.SetActive(!drawing);
            if(drawAge>=0&&Time.timeScale>0)drawAge+=Time.unscaledDeltaTime;
        }
    }
}
