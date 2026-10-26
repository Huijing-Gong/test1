using UnityEngine;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        CharacterController supportHost;
        Transform supportHips;
        float frameSupportClearance,snapshotSupportOffset;
        bool framePreserveWorldContacts,hasSupportFrame,supportContextChanged;
        Vector3 appliedSupportLocal;
        public float AppliedSupportOffset {get;private set;}
        public void ConfigureSupportHost(CharacterController host){supportHost=host;}
        // A frame owns its calibration. Historical blade queries never consult live physics.
        void FreezeSupportFrame(bool preserveWorldContacts)
        {
            supportContextChanged=hasSupportFrame&&framePreserveWorldContacts!=preserveWorldContacts;
            hasSupportFrame=true;framePreserveWorldContacts=preserveWorldContacts;
            frameSupportClearance=supportHost?Mathf.Clamp(supportHost.skinWidth*Mathf.Abs(supportHost.transform.lossyScale.y),0,.08f):0;
            if(!supportHips&&animator)supportHips=animator.GetBoneTransform(HumanBodyBones.Hips);
        }
        // Closed set: world-anchored water, traversal, seats and death clips
        // retain their existing contact space pending their own geometry acceptance.
        static bool UsesCapsuleSupport(string id)=>id!=null&&(id.StartsWith("LOC_")||id.StartsWith("COMBAT_")||id.StartsWith("ATK_")||id.StartsWith("GUARD_")||id.StartsWith("DODGE_")||id.StartsWith("PERFECT_DODGE_")||id.StartsWith("EQUIP_")||id.StartsWith("AIR_"));
        // Each explicitly paired light-hit direction uses its measured capsule-root height.
        // Only its explicitly paired, finite calibration may enter ordinary ground support.
        static bool IsLightHitAction(string id)=>id=="HIT_LIGHT_F"||id=="HIT_LIGHT_B"||id=="HIT_LIGHT_L"||id=="HIT_LIGHT_R";
        bool HasGroundHitCalibration(HeroActionDefinition def)
        {
            if(def==null||!IsLightHitAction(def.id)||!def.clip||def.supportCompensationClip!=def.clip||!Finite(def.duration)||def.duration<=0)return false;
            var curve=def.authoredSupportOffset;
            if(curve==null||curve.length<2||curve[0].time>0||curve[curve.length-1].time<def.duration)return false;
            for(int i=0;i<curve.length;i++){var k=curve[i];if(!Finite(k.time)||!Finite(k.value))return false;}
            return PoseCorrectionReady(def);
        }
        float RawSupport(HeroActionDefinition def,float time)
        {
            if(framePreserveWorldContacts||frameSupportClearance<=0||def==null||!(UsesCapsuleSupport(def.id)||HasGroundHitCalibration(def)||(def.id=="RESPAWN_RECOVER"&&PoseCorrectionReady(def))))return 0;
            float authored=def.clip&&def.clip==def.supportCompensationClip&&def.authoredSupportOffset!=null?def.authoredSupportOffset.Evaluate(ClipTime(def,time)):0;
            return -frameSupportClearance-authored;
        }
        float EvaluateSupport(HeroActionDefinition def,float time,float oldTime,float weight)
        {
            if(frameSupportClearance<=0)return 0;
            float target=RawSupport(def,time);
            // A completed fade must not rebind correction data from its retired clip.
            if(weight>=1)return target;
            // Landing lower body deliberately bypasses the previous airborne pose.
            if(landingContactTransition&&snapshotTransition)return target;
            if(snapshotTransition&&!snapshotUpperOnly)return Mathf.Lerp(snapshotSupportOffset,target,weight);
            return previousDefinition!=null?Mathf.Lerp(RawSupport(previousDefinition,oldTime),target,weight):target;
        }
        void ApplySupport(float offset,bool commit=false)
        {
            if(!supportHips)return;
            Vector3 prior=supportHips.localPosition;supportHips.position+=Vector3.up*offset;
            if(commit){AppliedSupportOffset=offset;appliedSupportLocal=supportHips.localPosition-prior;}
        }
        void CaptureSupportSnapshot()
        {
            snapshotSupportOffset=AppliedSupportOffset;
            // Committed poses include support. Snapshots store the animation base so
            // rebuilding a fade cannot accumulate the same host offset a second time.
            if(!supportHips||poseBones==null)return;
            for(int i=0;i<poseBones.Length;i++)if(poseBones[i]==supportHips){transitionPositions[i]-=appliedSupportLocal;break;}
        }
        void ResetSupportPresentation(){AppliedSupportOffset=snapshotSupportOffset=frameSupportClearance=0;appliedSupportLocal=Vector3.zero;framePreserveWorldContacts=hasSupportFrame=supportContextChanged=false;}
    }
}
