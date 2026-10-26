using UnityEngine;
using UnityEngine.Playables;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        HeroActionDefinition currentDefinition, previousDefinition;
        float currentRate=1,previousRate=1;
        float frameDelta,frameCurrentStart,framePreviousStart,frameBlendStart,frameBlendRate,frameOverlayStart;
        bool frameHasOverlay,hasRootSample;
        Vector3 frameRootStart,lastRootPosition;
        Quaternion frameRootRotationStart,lastRootRotation;
        Vector3[] queryPositions,queryScales;
        Quaternion[] queryRotations;

        static float ClipTime(HeroActionDefinition def,float seconds)
        {
            float length=Mathf.Max(.001f,def.duration);
            return def.loop?Mathf.Repeat(seconds,length):Mathf.Clamp(seconds,0,Mathf.Max(0,length-.0001f));
        }

        public bool TryQueryBladeAtActionTime(float time,out Vector3 a,out Vector3 b)
        {
            if(authoredGraph.IsValid())return QueryAuthored(time,out a,out b);
            float elapsed=currentRate>.00001f?(time-frameCurrentStart)/currentRate:frameDelta;
            return TryQueryBladeAtFrameOffset(elapsed-frameDelta,out a,out b);
        }

        // Query the same blended pose used for display, without advancing any clock,
        // consuming overlays, or replacing the post-contact pose retained for LateUpdate.
        // This is a historical query inside the most recent simulation step only.
        public bool TryQueryBladeAtFrameOffset(float seconds,out Vector3 a,out Vector3 b)
        {
            if(authoredGraph.IsValid())return QueryAuthored(authoredFrameStart+(authoredDelta+seconds)*authoredPlaybackRate,out a,out b);
            a=WeaponBase;b=WeaponTip;
            if(!WeaponReady||!graph.IsValid()||!current.IsValid()||currentDefinition==null)return false;
            if(seconds>.00001f||seconds < -frameDelta-.00001f)return false;
            if(seconds>=-.000001f||frameDelta<=0)return true;
            if(frameHistoryDiscontinuous)return false;
            float fraction=Mathf.Clamp01(1+seconds/frameDelta);
            if(poseBones==null)return false;
            if(queryPositions==null||queryPositions.Length!=poseBones.Length)
            {
                queryPositions=new Vector3[poseBones.Length];queryRotations=new Quaternion[poseBones.Length];queryScales=new Vector3[poseBones.Length];
            }
            for(int i=0;i<poseBones.Length;i++)if(poseBones[i])
            {queryPositions[i]=poseBones[i].localPosition;queryRotations[i]=poseBones[i].localRotation;queryScales[i]=poseBones[i].localScale;}
            Vector3 rootPosition=transform.position;Quaternion rootRotation=transform.rotation;
            double currentSample=current.GetTime(),previousSample=previous.IsValid()?previous.GetTime():0,overlaySample=overlay.IsValid()?overlay.GetTime():0;
            float priorWeight=mixer.GetInputWeight(0),currentWeight=mixer.GetInputWeight(1),overlayWeight=layers.GetInputWeight(2);
            float oldTransitionFootResidual=TransitionFootResidual;
            try
            {
                float actionTime=Mathf.Clamp(frameCurrentStart+frameDelta*fraction*currentRate,0,CurrentTime);
                current.SetTime(ClipTime(currentDefinition,actionTime));
                if(previous.IsValid()&&previousDefinition!=null)previous.SetTime(ClipTime(previousDefinition,Mathf.Lerp(framePreviousStart,previousTime,fraction)));
                float weight=float.IsPositiveInfinity(frameBlendRate)?1:Mathf.Min(1,frameBlendStart+frameDelta*fraction*frameBlendRate);
                mixer.SetInputWeight(0,snapshotTransition&&(!snapshotUpperOnly||landingContactTransition)?0:1-weight);mixer.SetInputWeight(1,snapshotTransition&&(!snapshotUpperOnly||landingContactTransition)?1:weight);
                if(overlay.IsValid()&&frameHasOverlay)
                {
                    float t=Mathf.Lerp(frameOverlayStart,overlayTime,fraction);
                    overlay.SetTime(Mathf.Min(t*overlayRate,overlayDuration*overlayRate));
                    layers.SetInputWeight(2,OverlayWeight(t,overlayDuration,frameSheathOverlay));
                }
                transform.SetPositionAndRotation(Vector3.Lerp(frameRootStart,lastRootPosition,fraction),Quaternion.Slerp(frameRootRotationStart,lastRootRotation,fraction));
                SetStrideYaw(currentStride,Mathf.LerpAngle(frameStrideYawStart,currentStrideYaw,fraction));
                float sheathWeight=CaptureSheathArmTarget();PreparePoseCorrectionEvaluation();graph.Evaluate(0);ApplyPoseCorrection(currentDefinition,actionTime);ApplyTransitionSnapshot(weight);
                ApplyUpperGaitBlend(currentDefinition,actionTime,frameUpperBlendStart+frameDelta*fraction);ApplySheathArmTarget(sheathWeight);
                transform.SetPositionAndRotation(Vector3.Lerp(frameRootStart,lastRootPosition,fraction),Quaternion.Slerp(frameRootRotationStart,lastRootRotation,fraction));
                ApplySupport(EvaluateSupport(currentDefinition,actionTime,Mathf.Lerp(framePreviousStart,previousTime,fraction),weight));EvaluateStopContact(actionTime);EvaluateCrossContact(fraction,out _,out _,out _);ApplyMovingLanding(landingAge<0?-1:Mathf.Lerp(frameLandingStart,landingAge,fraction),landingHeavy);groundTurnFrame.Apply(fraction);ropeContactFrame.Apply(fraction);a=WeaponBase;b=WeaponTip;
                return true;
            }
            finally
            {
                if(graph.IsValid())
                {
                    SetStrideYaw(currentStride,currentStrideYaw);
                    current.SetTime(currentSample);if(previous.IsValid())previous.SetTime(previousSample);if(overlay.IsValid())overlay.SetTime(overlaySample);
                    mixer.SetInputWeight(0,priorWeight);mixer.SetInputWeight(1,currentWeight);layers.SetInputWeight(2,overlayWeight);
                }
                TransitionFootResidual=oldTransitionFootResidual;
                transform.SetPositionAndRotation(rootPosition,rootRotation);
                for(int i=0;i<poseBones.Length;i++)if(poseBones[i])
                {poseBones[i].localPosition=queryPositions[i];poseBones[i].localRotation=queryRotations[i];poseBones[i].localScale=queryScales[i];}
            }
        }
    }
}
