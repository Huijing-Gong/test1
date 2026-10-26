using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        // Contact exits, paired ground actions and combat stop/back-start fades use this bridge. Values are frozen
        // in world space before support; the departure contact survives host settling.
        bool transitionFeet,transitionFeetPreserveRoot,transitionGroundActionFeet,transitionCombatStopFeet;
        // Paired ground actions preserve foot trajectories without whole-body root ownership.
        static bool IsGroundDefenseAction(string id)=>id=="GUARD_ENTER"||id=="GUARD_HOLD"||id=="GUARD_EXIT"||id=="GUARD_IMPACT"||id=="GUARD_BREAK"||id=="DODGE_B"||id=="PERFECT_DODGE_B";
        static bool IsChargeFamily(string id)=>id=="ATK_HEAVY"||id=="ATK_CHARGE_START"||id=="ATK_CHARGE_HOLD"||id=="ATK_CHARGE_RELEASE";
        bool HasGroundActionCorrection(HeroActionDefinition d)=>HasGroundHitCalibration(d)||(d!=null&&(d.id=="EQUIP_SHEATHE"||d.id=="ATK_A1"||IsGroundDefenseAction(d.id)||IsChargeFamily(d.id))&&PoseCorrectionReady(d));
        static bool GroundActionEndpoint(HeroActionDefinition d)=>d!=null&&(d.id=="EQUIP_SHEATHE"||IsLightHitAction(d.id)||d.id=="LOC_IDLE"||d.id=="LOC_START"||d.id=="LOC_WALK"||d.id=="LOC_RUN"||d.groundStop||d.id=="COMBAT_IDLE"||d.id.StartsWith("COMBAT_MOVE_")||d.id.StartsWith("ATK_")||IsGroundDefenseAction(d.id));
        bool GroundActionFootFrameAllowed(HeroActionDefinition next)=>supportHost&&supportHost.isGrounded&&!framePreserveWorldContacts&&GroundActionEndpoint(next);
        bool CanBridgeGroundAction(HeroActionDefinition next)=>GroundActionFootFrameAllowed(next)&&GroundActionEndpoint(currentDefinition)&&(HasGroundActionCorrection(next)||HasGroundActionCorrection(currentDefinition)||(transitionGroundActionFeet&&snapshotTransition&&blend<1));

        static bool CombatFootEndpoint(HeroActionDefinition d)=>d!=null&&(d.id=="COMBAT_IDLE"||d.id=="COMBAT_MOVE_F"||d.id=="COMBAT_MOVE_B"||d.id=="COMBAT_MOVE_L"||d.id=="COMBAT_MOVE_R");
        // Short stop/backward-start handoffs on static near-level foot support; never capture a new
        // contact in historical sword queries or takes ownership from traversal/hits.
        bool GroundCombatFootFrameAllowed(HeroActionDefinition next)
        {
            return supportHost&&supportHost.isGrounded&&!framePreserveWorldContacts&&CombatFootEndpoint(next)&&CacheLandingBones()&&
                CrossGround(landingFoot[0].position,out var l)&&CrossGround(landingFoot[1].position,out var r)&&Mathf.Abs(l.point.y-r.point.y)<.03f;
        }
        bool CanBridgeCombatStop(HeroActionDefinition next)=>GroundCombatFootFrameAllowed(next)&&CombatFootEndpoint(currentDefinition)&&
            ((next.id=="COMBAT_IDLE"&&currentDefinition.id!="COMBAT_IDLE")||
             (currentDefinition.id=="COMBAT_IDLE"&&next.id=="COMBAT_MOVE_B")||
             (transitionCombatStopFeet&&snapshotTransition&&blend<1));
        readonly Vector3[] transitionAnkles=new Vector3[2],transitionBends=new Vector3[2];
        readonly Quaternion[] transitionAnkleRotations=new Quaternion[2];
        readonly Vector3[] targetAnkles=new Vector3[2],targetBends=new Vector3[2];
        readonly Quaternion[] targetAnkleRotations=new Quaternion[2];
        public float TransitionFootResidual {get;private set;}
        Matrix4x4 SnapshotMatrix(Transform bone)
        {
            var m=Matrix4x4.identity;
            for(var t=bone;t&&t!=transform;t=t.parent)
            {
                int i=System.Array.IndexOf(poseBones,t);
                m=Matrix4x4.TRS(i>=0?transitionPositions[i]:t.localPosition,i>=0?transitionRotations[i]:t.localRotation,i>=0?transitionScales[i]:t.localScale)*m;
            }
            return m;
        }
        void CaptureTransitionFeet(bool requested,bool preserveRoot,bool groundHit,bool combatStop)
        {
            transitionFeet=requested&&CacheLandingBones();transitionFeetPreserveRoot=transitionFeet&&preserveRoot;transitionGroundActionFeet=transitionFeet&&groundHit;transitionCombatStopFeet=transitionFeet&&combatStop;TransitionFootResidual=0;
            if(!transitionFeet)return;
            for(int i=0;i<2;i++)
            {
                var foot=SnapshotMatrix(landingFoot[i]);
                transitionAnkles[i]=transform.TransformPoint(foot.MultiplyPoint3x4(Vector3.zero));transitionAnkleRotations[i]=transform.rotation*foot.rotation;
                transitionBends[i]=transform.TransformVector(SnapshotMatrix(landingKnee[i]).MultiplyPoint3x4(Vector3.zero)-SnapshotMatrix(landingThigh[i]).MultiplyPoint3x4(Vector3.zero));
            }
        }
        void CaptureTransitionFootTargets()
        {
            if(!transitionFeet)return;
            for(int i=0;i<2;i++){targetAnkles[i]=landingFoot[i].position;targetAnkleRotations[i]=landingFoot[i].rotation;targetBends[i]=landingKnee[i].position-landingThigh[i].position;}
        }
        void ApplyTransitionFeet(float weight)
        {
            TransitionFootResidual=0;
            if(!transitionFeet||weight<=0||weight>=1)return;
            for(int i=0;i<2;i++)
            {
                var a=landingThigh[i];var b=landingKnee[i];var c=landingFoot[i];
                var goal=Vector3.Lerp(transitionAnkles[i],targetAnkles[i],weight);
                var rotation=Quaternion.Slerp(transitionAnkleRotations[i],targetAnkleRotations[i],weight);
                float upper=Vector3.Distance(a.position,b.position),lower=Vector3.Distance(b.position,c.position),distance=Vector3.Distance(a.position,goal);
                if(distance>upper+lower-.000001f||distance<Mathf.Abs(upper-lower)+.000001f){TransitionFootResidual=Mathf.Max(TransitionFootResidual,Vector3.Distance(c.position,goal));continue;}
                var axis=(goal-a.position)/distance;
                var bend=Vector3.ProjectOnPlane(Vector3.Lerp(transitionBends[i],targetBends[i],weight),axis);
                if(bend.sqrMagnitude<.00000001f){TransitionFootResidual=Mathf.Max(TransitionFootResidual,Vector3.Distance(c.position,goal));continue;}
                float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
                var knee=a.position+axis*along+bend.normalized*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
                a.rotation=LandingFromTo(b.position-a.position,knee-a.position)*a.rotation;
                b.rotation=LandingFromTo(c.position-b.position,goal-b.position)*b.rotation;c.rotation=rotation;
                TransitionFootResidual=Mathf.Max(TransitionFootResidual,Vector3.Distance(c.position,goal));
            }
        }
    }
}
