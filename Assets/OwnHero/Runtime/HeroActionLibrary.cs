using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingOfGraphic.Hero
{
    [Serializable] public sealed class HeroActionDefinition
    {
        public string id, title, group;
        public AnimationClip clip;
        public bool authoredMotion, authoredFootIK=true;
        public float authoredRate=1, authoredOffset;
        // Measured source displacement removed when the capsule owns climbing travel.
        public AnimationClip authoredClimbRootClip;
        public TextAsset authoredClimbRoot;
        public int comboSet, comboIndex;
        public float damageMultiplier=1;
        public float duration, hitOpen=-1, hitClose=-1, cancelFrom, junction=-1;
        // Optional authored anticipation and swing cue; zero preserves existing attacks.
        public float aimEnd, swingCue;
        // Opt-in source phase for early release after the shared charge preparation.
        public float chargeResumeTime;
        public bool loop;
        // Metres per full cycle at the final character scale; phase offset aligns support feet.
        public float cycleDistance,phaseOffset;
        // One-shot ground transitions advance by capsule travel, then hand off to a measured cycle phase.
        public float travelDistance,exitPhase;
        public bool groundStop;
        // Opt in only for the four measured common-stance combat clips.
        public bool commonCombatStance,reversedCombatPhase;
        public float stanceHumanScale;
        public Vector3 leftStanceAnchor,rightStanceAnchor;
        // Nonzero only after this normal/recovery pair is verified in the shared Avatar.
        public float recoveryJunction;
        // Paired with the clip after its authored vertical travel has been separated.
        // Normalized time/value, monotonic linear tangents; the capsule owns this ascent.
        public AnimationClip traversalAscentClip;
        public AnimationCurve traversalAscent, traversalAdvance;
        // Corrections are valid only for this exact source clip and duration.
        public AnimationClip bodyEnvelopeClip;
        public Mesh bodyEnvelopeMesh;
        public Bounds bodyEnvelope;
        public Vector3 bodyEnvelopeScale;
        public AnimationClip supportCompensationClip;
        // Existing authored hips translation in metres, sampled in clip seconds.
        public AnimationCurve authoredSupportOffset;
        public AnimationClip poseCorrectionClip;
        public TextAsset poseCorrections;
        public bool canonicalMantleEntry;
        // Opt-in only for the measured stationary bottom-entry / hold pair.
        public bool canonicalRopeBottomEntry;
        public AnimationClip ropeBottomEntryClip,ropeBottomHoldClip;
        // Default off; only the measured .96s upward cycle paired with the entry hold.
        public bool canonicalRopeUp;
        public AnimationClip ropeUpClip,ropeUpHoldClip;
        // Paired reverse cycle; references make an accidental legacy DOWN substitution fail closed.
        public bool canonicalRopeDown;
        public AnimationClip ropeDownClip,ropeDownSourceUpClip;
        // Ground exit is a measured, opt-in pair; legacy exits cannot silently enter this host.
        public bool canonicalRopeGroundExit;
        public AnimationClip ropeGroundExitClip,ropeGroundExitHoldClip;
        // Conditional measured top transfer; never replaces the shared pole/wall CLIMB_TOP.
        public bool canonicalRopeTopExit;
        public AnimationClip ropeTopExitClip,ropeTopExitHoldClip,ropeTopExitIdleClip;
        public TextAsset ropeTopExitRootPath,ropeTopExitIdleCorrections;
        public float blendDuration=.10f;
        public string next;
        public bool TrySampleTraversalAscent(float time,out float value)
        {
            value=0;
            if(!clip||clip!=traversalAscentClip||traversalAscent==null||traversalAscent.length<2)return false;
            value=Mathf.Clamp01(traversalAscent.Evaluate(Mathf.Clamp01(time)));
            return true;
        }

    }

    public sealed class HeroActionLibrary : ScriptableObject
    {
        public HeroActionDefinition[] actions;
        public bool authoredControls;
        public bool responsiveSwordCombat;
        public float authoredWalkSpeed=2.1f, authoredRunSpeed=4.2f, authoredSprintSpeed=6f;
        // Conditional top-entry presentation, outside the 120 gameplay action IDs.
        public HeroActionDefinition ledgeTopEntry;
        public HeroActionDefinition ropeTopExit;
        public AvatarMask rightFingers;
        public bool stationaryTurns;
        Dictionary<string,HeroActionDefinition> lookup;
        public HeroActionDefinition Find(string id)
        {
            if(lookup==null){lookup=new Dictionary<string,HeroActionDefinition>();if(actions!=null)foreach(var a in actions)if(a!=null&&!string.IsNullOrEmpty(a.id))lookup[a.id]=a;}
            return id!=null&&lookup.TryGetValue(id,out var value)?value:null;
        }
        public float Duration(string id)=>Find(id)?.duration??.3f;
        void OnEnable(){lookup=null;}
    }

    // A single gameplay clock for both worlds. Animation never independently deals damage.
    public sealed class HeroCombatClock
    {
        public HeroActionLibrary library;
        public string Id {get;private set;}
        public float Time {get;private set;}
        public float PreviousTime {get;private set;}
        public long Sequence {get;private set;}
        public int Combo {get;private set;}
        public bool Active=>!string.IsNullOrEmpty(Id);
        public bool Charging=>Id=="ATK_CHARGE_START"||Id=="ATK_CHARGE_HOLD";
        public int SelectedCombo=3;
        public string FirstAttack=>SelectedCombo==4?"ATK_COMBO4_1":SelectedCombo==5?"ATK_COMBO5_1":"ATK_A1";
        public bool Responsive=>library&&library.responsiveSwordCombat;
        public bool HasQueuedCombo=>queued||pending!=null;
        public bool Light=>Id=="ATK_A1"||Id=="ATK_A2"||Id=="ATK_A3"||(Definition?.comboSet??0)>0;
        public HeroActionDefinition Definition=>library?library.Find(Id):null;
        public float Duration=>Definition?.duration??.7f;
        public float Open=>Definition?.hitOpen??-1;
        public float Close=>Definition?.hitClose??-1;
        public float AimEnd=>Definition!=null&&Definition.aimEnd>0?Definition.aimEnd:Open;
        public float SwingCue=>Definition!=null&&Definition.swingCue>0?Definition.swingCue:.15f;
        // Only an attack with a damage window can emit a swing, using its actual source phase.
        public bool CrossedSwingCue=>Active&&Open>=0&&Close>=Open&&PreviousTime<SwingCue&&Time>=SwingCue;
        public bool HitWindow=>Active&&Open>=0&&Time>=Open&&PreviousTime<=Close;
        public bool CanCancel=>!Active||Charging||Time>=(Definition?.cancelFrom??Duration);
        // Responsive basic cuts commit their active swing; startup and recovery can dodge.
        public bool CanCancelIntoDodge=>CanCancel||(Light&&(!Responsive||Time<Open||Time>=Close));
        public float Multiplier=>Id=="ATK_HEAVY"?1.5f:Id=="ATK_CHARGE_RELEASE"?2f:Id!=null&&Id.StartsWith("ATK_BOOST")?1.3f:Definition?.damageMultiplier??1f;
        float heldTime;
        string pending;float pendingTime;bool pendingEnd;
        bool queued,chargeWanted,released,fullCharge;
        public void Begin(string id="ATK_A1")
        {
            heldTime=0;pending=null;pendingTime=0;pendingEnd=false;queued=chargeWanted=released=fullCharge=false;Set(id);
        }
        void Set(string id,float startTime=0)
        {
            Id=id;Time=PreviousTime=Mathf.Clamp(startTime,0,library?library.Duration(id):0);Sequence++;
            if(id=="ATK_A1")Combo=1;else if(id=="ATK_A2")Combo=2;else if(id=="ATK_A3")Combo=3;else Combo=Definition?.comboIndex??0;
        }
        public void Cancel(){pending=null;pendingTime=0;pendingEnd=false;Id=null;Time=PreviousTime=heldTime=0;queued=chargeWanted=released=fullCharge=false;}
        // The prior tick already displayed and sampled the terminal pose.
        // Hosts retire it before admitting this frame's fresh input.
        public bool FinishCompletedAction(){if(!pendingEnd)return false;Cancel();return true;}
        public void Tick(float delta,bool pressed,bool held,bool release)
        {
            if(delta<=0||!Active)return;
            if(pendingEnd){Cancel();return;}
            if(pending!=null){var nextId=pending;float nextTime=pendingTime;pending=null;pendingTime=0;Set(nextId,nextTime);}
            PreviousTime=Time;Time=Mathf.Min(Time+delta,Charging?float.MaxValue:Duration);
            if(pressed){heldTime=0;released=false;}
            if(held)heldTime+=delta;
            if(pressed&&Light&&PreviousTime>(Responsive?.025f:.08f))queued=true;
            if(Light&&heldTime>=.3f){chargeWanted=true;queued=false;}
            if(release)released=true;
            if(Light&&chargeWanted&&Time>=(Definition?.cancelFrom??Duration))
            {pending="ATK_CHARGE_START";return;}
            if(Id=="ATK_CHARGE_START"&&Time>=Duration)
            {pending=released?"ATK_HEAVY":"ATK_CHARGE_HOLD";pendingTime=released?(library?.Find("ATK_HEAVY")?.chargeResumeTime??0):0;return;}
            if(Id=="ATK_CHARGE_HOLD")
            {
                fullCharge|=Time>=1f;
                if(release||released||!held){pending=fullCharge?"ATK_CHARGE_RELEASE":"ATK_HEAVY";pendingTime=fullCharge?0:(library?.Find("ATK_HEAVY")?.chargeResumeTime??0);}
                return;
            }
            if(Light&&queued&&Time>=(Definition?.junction??Duration))
            {var next=Definition?.next;heldTime=0;queued=chargeWanted=released=false;pending=string.IsNullOrEmpty(next)?"ATK_A1":next;return;}
            if(Time>=Duration)pendingEnd=true;
        }
    }
}


