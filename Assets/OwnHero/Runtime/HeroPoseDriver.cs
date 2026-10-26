using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KingOfGraphic.Hero
{
    // Presentation only. Owners explicitly sample before testing the actual held blade.
    [DefaultExecutionOrder(2000),DisallowMultipleComponent] public sealed partial class HeroPoseDriver : MonoBehaviour
    {
        public HeroActionLibrary library;
        public Animator animator;
        public Transform sword, bladeBase, bladeTip, grip;
        public string CurrentAction {get;private set;}
        public long CurrentActionInstance {get;private set;}
        
        HeroGroundTurnFrame groundTurnFrame;
        HeroRopeContactFrame ropeContactFrame;
        public float RopeContactReachError {get;private set;}
        public float GroundTurnReachError {get;private set;}
        public float CurrentTime {get;private set;}
        public bool Ready=>library&&animator&&animator.avatar&&animator.avatar.isValid&&animator.avatar.isHuman;
        public Vector3 WeaponBase=>bladeBase?bladeBase.position:grip?grip.position:transform.position;
        public Vector3 WeaponTip=>bladeTip?bladeTip.position:WeaponBase;
        public static string DirectionSuffix(Vector3 local)=>Mathf.Abs(local.x)>Mathf.Abs(local.z)?local.x>0?"R":"L":local.z>=0?"F":"B";
        public bool WeaponReady=>bladeBase&&bladeTip;
        public bool RightHandGripActive=>graph.IsValid()&&layers.GetInputWeight(1)>.5f;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationLayerMixerPlayable layers;
        AnimationClipPlayable current,previous,fingers,overlay;
        AvatarMask overlayMask;string overlayId;float overlayTime,overlayDuration,overlayRate;
        public string CurrentOverlay=>overlayId;
        public const float SheathHideFraction=.7f;
        public float CurrentSheathTime=>overlayId=="EQUIP_SHEATHE"?overlayTime:-1;
        bool frameSheathOverlay;
        static float OverlayWeight(float time,float duration,bool sheath)=>Mathf.Clamp01((duration-time)/.08f)*(sheath?Mathf.Clamp01(time/.1f):1);
        // The owner supplies one clock: hands put away the sword while the existing gait owns legs/root.
        void PrepareSheathOverlay(float time)
        {
            if(time<0){if(overlayId=="EQUIP_SHEATHE")overlayId=null;return;}
            if(overlayId!="EQUIP_SHEATHE")BeginOverlay("EQUIP_SHEATHE",library.Duration("EQUIP_SHEATHE"));
            overlayTime=Mathf.Max(0,time);overlayRate=1;
        }
        public void BeginOverlay(string id,float duration,bool leftOnly=false)
        {
            if(library&&library.authoredControls)return;
            EnsureGraph();if(!graph.IsValid()||library.Find(id)==null)return;
            if(overlay.IsValid()){graph.Disconnect(layers,2);graph.DestroyPlayable(overlay);}
            if(overlayMask)Destroy(overlayMask);overlayMask=new AvatarMask();
            for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)overlayMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
            overlayMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm,true);overlayMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers,true);
            if(!leftOnly){overlayMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm,true);overlayMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers,true);}
            overlay=AnimationClipPlayable.Create(graph,library.Find(id).clip);overlay.SetSpeed(0);graph.Connect(overlay,0,layers,2);layers.SetLayerMaskFromAvatarMask(2,overlayMask);
            overlayId=id;overlayTime=0;overlayDuration=Mathf.Max(.01f,duration);overlayRate=library.Duration(id)/overlayDuration;
        }
        float previousTime,blend;
        string restartAction;bool freezePrevious;
        bool reversePreviousCombatGait,landingContactTransition;
        Vector3 basePosition;
        Quaternion baseRotation;
        bool capturedRoot;
        Transform[] poseBones;Vector3[] posePositions,poseScales;Quaternion[] poseRotations;bool committed;
        // Arena simulation samples in Update; Unity's Animator controller evaluates later.
        // Retain the exact collision pose for rendering after that evaluation, without
        // advancing clocks, changing root movement, or overwriting committed contact IK.
        public void CommitPose()
        {
            ApplyFastBackDraw();
            if(!animator)return;
            if(poseBones==null){poseBones=animator.GetComponentsInChildren<Transform>(true);posePositions=new Vector3[poseBones.Length];poseRotations=new Quaternion[poseBones.Length];poseScales=new Vector3[poseBones.Length];}
            for(int i=0;i<poseBones.Length;i++)if(poseBones[i]){posePositions[i]=poseBones[i].localPosition;poseRotations[i]=poseBones[i].localRotation;poseScales[i]=poseBones[i].localScale;}
            committed=true;
        }
        void LateUpdate()
        {
            if(!committed||(!graph.IsValid()&&!authoredGraph.IsValid())){UpdateBackSword();return;}
            for(int i=0;i<poseBones.Length;i++)if(poseBones[i]){poseBones[i].localPosition=posePositions[i];poseBones[i].localRotation=poseRotations[i];poseBones[i].localScale=poseScales[i];}
            UpdateBackSword();
        }
                public bool CanBeginGroundTurn=>CurrentAction=="LOC_IDLE"&&blend>=.999f&&CurrentOverlay==null&&landingAge<0&&!stopContactActive&&!crossContact.valid;
        void PreserveGroundTurnHandoff()
        {
            if(!committed||!hasRootSample||poseBones==null)return;
            Vector3 position=transform.position;Quaternion rotation=transform.rotation;
            if(Vector3.Distance(position,lastRootPosition)>.45f)return;
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            transform.SetPositionAndRotation(lastRootPosition,lastRootRotation);RestoreCommittedForStop();
            var worldPosition=hips.position;var worldRotation=hips.rotation;
            transform.SetPositionAndRotation(position,rotation);RestoreCommittedForStop();
            hips.SetPositionAndRotation(worldPosition,worldRotation);CommitPose();
        }
        void Awake(){if(!animator)animator=GetComponentInChildren<Animator>(true);CaptureRoot();}
        void CaptureRoot(){basePosition=transform.localPosition;baseRotation=transform.localRotation;capturedRoot=true;}
        void EnsureGraph()
        {
            if(graph.IsValid()||!Ready)return;
            if(!capturedRoot)CaptureRoot();
            CapturePoseCorrectionDefaults();
            graph=PlayableGraph.Create("Hero authored pose");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,2);
            layers=AnimationLayerMixerPlayable.Create(graph,3);
            graph.Connect(mixer,0,layers,0);layers.SetInputWeight(0,1);
            var hand=library.Find("HAND_GRIP_R");
            if(hand!=null&&hand.clip&&library.rightFingers)
            {
                fingers=AnimationClipPlayable.Create(graph,hand.clip);fingers.SetSpeed(0);fingers.SetTime(.3);
                graph.Connect(fingers,0,layers,1);layers.SetLayerMaskFromAvatarMask(1,library.rightFingers);
            }
            var output=AnimationPlayableOutput.Create(graph,"Hero",animator);output.SetSourcePlayable(layers);
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=true;
            graph.Play();
        }
        /// <summary>Replay the current action on its next successful sample, retaining its outgoing pose during the fade.</summary>
        public void RestartAction(string id){restartAction=string.IsNullOrEmpty(id)?null:id;}
        public void Sample(string id,float seconds,float delta,bool heldSword=false,float fade=-1,float playbackRate=1,float frameStartTime=float.NaN,Vector3? locomotionVelocity=null,HeroActionDefinition definitionOverride=null,float movingLandingAge=-1,bool heavyLanding=false,bool preserveWorldContacts=false,HeroGroundTurnFrame groundTurn=default,HeroRopeContactFrame ropeContact=default,float sheathTime=-1,long actionInstance=0)
        {
            var authoredDefinition=definitionOverride??library?.Find(id);
            if(authoredDefinition!=null&&authoredDefinition.authoredMotion){SampleAuthored(authoredDefinition,seconds,delta,fade,playbackRate,frameStartTime,actionInstance);return;}
            if(authoredGraph.IsValid())ResetPresentation();
            EnsureGraph();if(!graph.IsValid()){preserveRootOnTransition=false;return;}
            var def=definitionOverride??library.Find(id);if(def==null||def.id!=id||!def.clip){preserveRootOnTransition=false;return;}
            float dt=Mathf.Max(0,delta);PrepareSheathOverlay(sheathTime);
            float turnStart=!float.IsNaN(frameStartTime)?frameStartTime:CurrentAction!=id||currentDefinition!=def||!current.IsValid()?seconds-dt*Mathf.Max(0,playbackRate):CurrentTime;
            groundTurn.Validate(animator,def,seconds,Mathf.Max(0,turnStart),dt,Mathf.Max(0,playbackRate));
            ropeContact.Validate(animator,def,seconds,Mathf.Max(0,turnStart),dt,Mathf.Max(0,playbackRate));
            var priorRopeContact=ropeContactFrame;
            bool leavingGroundTurn=groundTurnFrame.Pose!=null&&!object.ReferenceEquals(groundTurnFrame.Pose,groundTurn.Pose);
            if(leavingGroundTurn)PreserveGroundTurnHandoff();
            FreezeSupportFrame(preserveWorldContacts);
            if(transitionGroundActionFeet&&(!GroundActionFootFrameAllowed(def)||!snapshotTransition||blend>=1)){transitionFeet=transitionGroundActionFeet=transitionFeetPreserveRoot=false;TransitionFootResidual=0;}
            if(transitionCombatStopFeet&&(!GroundCombatFootFrameAllowed(def)||!snapshotTransition||blend>=1)){transitionFeet=transitionCombatStopFeet=transitionFeetPreserveRoot=false;TransitionFootResidual=0;}
            // An explicit combat instance distinguishes a fresh attack from the same clip's last pose.
            // Clocks may wrap for loops or be queried at zero delta without starting a new action.
            bool replay=restartAction==id&&CurrentAction==id&&current.IsValid();restartAction=null;
            bool actionRestart=replay||(CurrentAction==id&&actionInstance!=0&&CurrentActionInstance!=0&&actionInstance!=CurrentActionInstance);
            bool changed=actionRestart||CurrentAction!=id||currentDefinition!=def||!current.IsValid()||supportContextChanged;
            float priorCurrentTime=CurrentTime;
            bool hadCrossContact=crossContact.valid;
            float requestedFade=fade<0?def.blendDuration:fade;
            bool hadUpperBlend=upperBlendEnabled&&upperBlendAge<upperBlendDuration;
            PrepareCrossContact(def,seconds,dt,Mathf.Max(0,playbackRate),changed);
            ropeContactFrame=ropeContact;
            // PrepareCrossContact resets the frame flag; apply rope boundaries afterwards.
            if(!object.ReferenceEquals(priorRopeContact.Pose,ropeContact.Pose)||(ropeContact.IsActive&&(ropeContact.ClockChanged||Mathf.Abs(priorRopeContact.End.Phase-ropeContact.Start.Phase)>.0001f)))frameHistoryDiscontinuous=true;
            var priorTurn=groundTurnFrame;
            groundTurnFrame=groundTurn.Matches(animator,def)?groundTurn:default;
            // Crossing a turn-session boundary is not interpolated by a historical query.
            if(!object.ReferenceEquals(priorTurn.Pose,groundTurnFrame.Pose)||(groundTurnFrame.Pose!=null&&Mathf.Abs(priorTurn.End-groundTurnFrame.Start)>.0001f))frameHistoryDiscontinuous=true;
            if(changed)
            {
                freezePrevious=replay;
                landingContactTransition=(id=="AIR_LAND_LIGHT"||id=="AIR_LAND_HEAVY")&&HasPairedCorrection(def);
                bool ordinaryStop=def.groundStop&&HeroGaitClock.IsOrdinaryCycle(CurrentAction);
                bool firstSwordAnticipation=id=="ATK_A1"&&(CurrentAction!="ATK_A1"||actionRestart);
                bool nextUpperBlend=requestedFade>0&&currentDefinition!=null&&(firstSwordAnticipation||landingContactTransition||ordinaryStop||((currentDefinition.clip!=def.clip||hadUpperBlend)&&HeroGaitClock.IsOrdinaryCycle(CurrentAction)&&HeroGaitClock.IsOrdinaryCycle(id)));
                reversePreviousCombatGait=HeroGaitClock.IsReverseSwordPair(CurrentAction,id,library);
                // Entering idle fades from the actual last pose instead of advancing the old stride.
                // This preserves the stop pose; foot planting still needs contact-specific validation.
                bool stoppedGroundCycle=(id=="LOC_IDLE"||id=="COMBAT_IDLE")&&
                    !string.IsNullOrEmpty(CurrentAction)&&CurrentAction!="LOC_IDLE"&&CurrentAction!="COMBAT_IDLE"&&(HeroGaitClock.IsGroundCycle(CurrentAction)||CurrentAction=="LOC_START");
                bool combatFootHandoff=CanBridgeCombatStop(def);
                bool needsBodySnapshot=actionRestart||combatFootHandoff||preserveRootOnTransition||leavingGroundTurn||supportContextChanged||(landingAge>=0&&!HeroGaitClock.IsOrdinaryCycle(id))||blend<1||stoppedGroundCycle||stopContactActive||hadCrossContact||def.groundStop||PoseCorrectionReady(currentDefinition)||PoseCorrectionReady(def);
                if(committed&&(needsBodySnapshot||hadUpperBlend))CaptureTransitionSnapshot(landingContactTransition||(!needsBodySnapshot&&nextUpperBlend),CanBridgeGroundAction(def),combatFootHandoff);else snapshotTransition=false;
                if(previous.IsValid()){graph.Disconnect(mixer,0);graph.DestroyPlayable(previousStride);graph.DestroyPlayable(previous);}
                previous=current;previousStride=currentStride;previousTime=CurrentTime;previousDefinition=currentDefinition;previousRate=currentRate;
                if(previous.IsValid()){graph.Disconnect(mixer,1);graph.Connect(previousStride,0,mixer,0);}
                current=AnimationClipPlayable.Create(graph,def.clip);current.SetApplyFootIK(HeroGaitClock.IsGroundCycle(id)||id=="LOC_START"||def.groundStop);current.SetApplyPlayableIK(false);current.SetSpeed(0);
                currentStride=CreateStridePlayable(current,def);graph.Connect(currentStride,0,mixer,1);CurrentAction=id;blend=previous.IsValid()?0:1;
                if(def.groundStop)CaptureStopContact(def);else stopContactActive=false;
                // Ease the first sword preparation without delaying its action or hit clock.
                // Ground stops also let the upper body settle after the first foot lands.
                // The existing lower-body fade and contact clock remain unchanged.
                upperBlendEnabled=nextUpperBlend;upperBlendAge=0;upperBlendDuration=Mathf.Max(firstSwordAnticipation?.20f:ordinaryStop?(id=="LOC_RUN_STOP"?.32f:.20f):.15f,requestedFade);
            }
            // A handoff request belongs to this sample only, including unchanged or superseded transitions.
            preserveRootOnTransition=false;
            if(requestedFade<=0)upperBlendEnabled=false;
            if(!changed&&blend>=1&&!(upperBlendEnabled&&upperBlendAge<upperBlendDuration))snapshotTransition=false;
            frameUpperBlendStart=upperBlendAge;if(upperBlendEnabled)upperBlendAge+=dt;
            CurrentActionInstance=actionInstance;currentDefinition=def;currentRate=Mathf.Max(0,playbackRate);
            float requestedYaw=StrideYaw(id,locomotionVelocity);frameStrideYawStart=changed?requestedYaw:currentStrideYaw;currentStrideYaw=requestedYaw;SetStrideYaw(currentStride,currentStrideYaw);
            landingAge=movingLandingAge;landingHeavy=heavyLanding;frameLandingStart=Mathf.Max(0,movingLandingAge-dt);
            frameDelta=dt;frameCurrentStart=!float.IsNaN(frameStartTime)?frameStartTime:changed?seconds-dt*currentRate:priorCurrentTime;
            framePreviousStart=previousTime;frameBlendStart=blend;frameOverlayStart=sheathTime>=0?Mathf.Max(0,sheathTime-dt):overlayTime;frameHasOverlay=overlay.IsValid()&&overlayId!=null;frameSheathOverlay=overlayId=="EQUIP_SHEATHE";
            frameRootStart=hasRootSample&&!frameHistoryDiscontinuous?lastRootPosition:transform.position;frameRootRotationStart=hasRootSample&&!frameHistoryDiscontinuous?lastRootRotation:transform.rotation;
            lastRootPosition=transform.position;lastRootRotation=transform.rotation;hasRootSample=true;
            CurrentTime=Mathf.Max(0,seconds);FreezeCrossContact();
            current.SetTime(ClipTime(def,CurrentTime));
            if(!freezePrevious)previousTime+=dt*(reversePreviousCombatGait&&previousDefinition!=null?-currentRate*previousDefinition.duration/Mathf.Max(.001f,def.duration):previousRate);if(previous.IsValid()&&previousDefinition!=null)previous.SetTime(ClipTime(previousDefinition,previousTime));
            if(fade<0)fade=def.blendDuration;frameBlendRate=fade<=0?float.PositiveInfinity:1/fade;blend=fade<=0?1:Mathf.Min(1,blend+dt/fade);
            mixer.SetInputWeight(0,snapshotTransition&&(!snapshotUpperOnly||landingContactTransition)?0:1-blend);mixer.SetInputWeight(1,snapshotTransition&&(!snapshotUpperOnly||landingContactTransition)?1:blend);
            bool fingerOverlay=heldSword&&(id.StartsWith("COMBAT_MOVE")||id=="COMBAT_IDLE"||id=="LOC_WALK"||id=="LOC_RUN"||id=="AIR_LAND_LIGHT"||id=="AIR_LAND_HEAVY");
            layers.SetInputWeight(1,fingerOverlay?1:0);
            if(overlay.IsValid()&&overlayId!=null){if(sheathTime<0)overlayTime+=Mathf.Max(0,delta);overlay.SetTime(Mathf.Min(overlayTime*overlayRate,library.Duration(overlayId)));layers.SetInputWeight(2,OverlayWeight(overlayTime,overlayDuration,frameSheathOverlay));if(overlayTime>=overlayDuration)overlayId=null;}else layers.SetInputWeight(2,0);
            float sheathWeight=CaptureSheathArmTarget();PreparePoseCorrectionEvaluation();graph.Evaluate(0);ApplyPoseCorrection(def,CurrentTime);ApplyTransitionSnapshot(blend);
            ApplyUpperGaitBlend(def,CurrentTime,upperBlendAge);ApplySheathArmTarget(sheathWeight);
            // Game controllers alone translate/rotate the root; authored hip motion remains intact.
            transform.localPosition=basePosition;transform.localRotation=baseRotation;ApplySupport(EvaluateSupport(def,CurrentTime,previousTime,blend),true);EvaluateStopContact(CurrentTime,true);ApplyCrossContactForDisplay();ApplyMovingLanding(landingAge,landingHeavy,true);GroundTurnReachError=groundTurnFrame.Apply(1);RopeContactReachError=ropeContactFrame.Apply(1);CommitPose();
        }
        public void SetSwordVisible(bool visible){SetBackSwordDrawn(visible);if(sword&&sword.gameObject.activeSelf!=visible)sword.gameObject.SetActive(visible);}
        public void ResetPresentation()
        {
            restartAction=null;freezePrevious=false;previousTime=0;
            ResetAuthoredPresentation();sheathBones=null;frameSheathOverlay=false;transitionFeet=transitionGroundActionFeet=transitionCombatStopFeet=transitionFeetPreserveRoot=false;TransitionFootResidual=0;preserveRootOnTransition=false;ropeContactFrame=default;RopeContactReachError=0;groundTurnFrame=default;GroundTurnReachError=0;ResetSupportPresentation();landingContactTransition=false;landingAge=-1;LandingFootResidual=LandingAppliedDrop=0;ResetPoseCorrection();ResetCrossContact();upperBlendEnabled=false;snapshotUpperOnly=false;upperGaitBones=null;upperBlendAge=frameUpperBlendStart=0;committed=false;stopContactActive=false;reversePreviousCombatGait=false;if(graph.IsValid())graph.Destroy();current=previous=default;currentStride=previousStride=default;currentStrideYaw=frameStrideYawStart=0;fingers=overlay=default;overlayId=null;if(overlayMask){if(Application.isPlaying)Destroy(overlayMask);else DestroyImmediate(overlayMask);overlayMask=null;}CurrentAction=null;CurrentActionInstance=0;CurrentTime=blend=0;currentDefinition=previousDefinition=null;currentRate=previousRate=1;frameDelta=0;hasRootSample=false;snapshotTransition=false;
        }
        void OnDisable(){ResetPresentation();}
        void OnDestroy(){ResetPresentation();}
    }
}


