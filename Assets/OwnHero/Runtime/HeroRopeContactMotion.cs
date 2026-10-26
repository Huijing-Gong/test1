using System;
using UnityEngine;
namespace KingOfGraphic.Hero
{
    // Online history for the measured .624m / .96s upright rope cycle.
    // A queued event is consumed only after actual travel crosses its phase.
    public sealed class HeroRopeContactMotion
    {
        public const float Rise=.624f, Cycle=.96f, CloseTime=.30f;
        internal struct Contact
        {
            internal float height,opening,stopOpening,target,deadline;
            internal Plan plan;
        }
        internal struct Plan
        {
            internal float start,end,height,opening,target;
            internal Contact At(float phase,Contact previous)
            {
                float u=Mathf.Clamp01((phase-start)/Mathf.Max(.00001f,end-start));
                float e=Smooth(u),b=opening*(1-e),arc=Mathf.Sin(Mathf.PI*u);
                previous.height=Mathf.LerpUnclamped(height,target,e);
                previous.opening=b+(1-b)*(Mathf.Abs(target-height)>.00001f?1:0)*arc*arc;
                return previous;
            }
        }
        public struct State
        {
            internal float phase,stopAge,leftFingerSpan,rightFingerSpan;
            internal bool valid,stopping,reversing,bottomAlign;
            internal int bottomChannel;internal float bottomAge,bottomTarget;
            internal int active,nextEvent,direction,clockOffset;
            internal Contact left,right,feet;
            public float Phase=>phase;
            public int Direction=>direction;
            public float Clock=>(direction*phase+clockOffset)*Cycle;
            public bool Reversing=>reversing;
            public bool EndpointAligned=>bottomAlign&&bottomChannel>=3;
            public bool BottomAligned=>EndpointAligned&&Mathf.Abs(bottomTarget)<.000001f;
            public bool Valid=>valid;
            public bool Stopping=>stopping;
            public int ActiveChannel=>active;
            public int NextEvent=>nextEvent;
            public float SettleAge=>stopAge;
            public float Height(int channel)=>Get(channel).height;
            public float Opening(int channel)=>Get(channel).opening;
            public bool Gripping(int channel)=>active!=channel||(stopping&&stopAge>=CloseTime);
            internal Contact Get(int channel)=>channel==0?left:channel==1?right:feet;
            internal void Put(int channel,Contact value){if(channel==0)left=value;else if(channel==1)right=value;else feet=value;}
        }
        State state;
        readonly HeroRopeContactPose pose;
        public State Current=>state;
        public HeroRopeContactMotion(HeroRopeContactPose pose,int initialDirection=1)
        {
            this.pose=pose??throw new ArgumentNullException(nameof(pose));
            if(!pose.SupportsDirection(initialDirection))throw new ArgumentException("Unpaired rope direction");
            state=new State{valid=true,active=-1,direction=initialDirection,leftFingerSpan=pose.FingerSpan(0),rightFingerSpan=pose.FingerSpan(1)};
        }
        // Intent is separate from collision-resolved travel: blocked, stopped and reversing
        // all have zero displacement, but only a reversal changes the event sequence.
        public bool CanMove(int intent)=>state.valid&&!state.bottomAlign&&!state.reversing&&intent==state.direction&&pose.SupportsDirection(intent);
        public HeroRopeContactFrame Advance(float actualRise,float delta)=>Advance(actualRise,delta,actualRise>.0000001f?1:0);
        public HeroRopeContactFrame Advance(float signedTravel,float delta,int intent)
        {
            var before=state;
            if(state.bottomAlign||!Finite(signedTravel)||!Finite(delta)||delta<0||Mathf.Abs(signedTravel)>.65f*delta+.001f||
               intent < -1 || intent > 1 || (intent!=0&&!pose.SupportsDirection(intent))||
               (Mathf.Abs(signedTravel)>.0000001f&&(!CanMove(intent)||Mathf.Sign(signedTravel)!=state.direction)))
                state.valid=false;
            else state=AdvanceState(state,signedTravel/Rise,delta,intent);
            return new HeroRopeContactFrame(pose,before,state,signedTravel/Rise,delta,intent);
        }

        // Endpoint transfer has its own elapsed clock. It never invents vertical travel.
        public HeroRopeContactFrame AlignAtBottom(float delta,bool advance=true)=>AlignAtEndpoint(0,delta,advance);
        public HeroRopeContactFrame AlignAtEndpoint(float height,float delta,bool advance=true)
        {
            var before=state;
            if(!Finite(height)||!Finite(delta)||delta<0||Mathf.Abs(state.phase*Rise-height)>.00312f||
               (state.bottomAlign&&height!=state.bottomTarget))state.valid=false;
            else if(advance)state=AdvanceEndpointState(state,height,delta);
            return new HeroRopeContactFrame(pose,before,state,0,delta,0,true,!advance,height);
        }
        internal static State AdvanceBottomState(State s,float delta)=>AdvanceEndpointState(s,0,delta);
        internal static State AdvanceEndpointState(State s,float height,float delta)
        {
            if(!s.valid||delta<=0||delta>.1f)return s;
            if(!Finite(height)||(s.bottomAlign&&height!=s.bottomTarget)){s.valid=false;return s;}
            if(!s.bottomAlign){s.bottomAlign=true;s.bottomTarget=height;s.bottomChannel=-2;s.reversing=false;}
            if(s.bottomChannel==-2)
            {
                bool closed=s.stopping&&s.stopAge>=CloseTime;
                s=Close(s,delta);
                if(closed){s.bottomChannel=0;s.active=-1;s.bottomAge=0;}
                return s;
            }
            if(s.bottomChannel>=3)return s;
            int channel=s.bottomChannel;var c=s.Get(channel);
            if(s.active!=channel)
            {
                if(Mathf.Abs(c.height-s.bottomTarget)<.000001f&&c.opening==0){s.bottomChannel++;return s;}
                c.target=s.bottomTarget;c.deadline=.45f/Cycle;
                c.plan=MakePlan(channel,0,c,channel==0?s.leftFingerSpan:channel==1?s.rightFingerSpan:0,out bool ok);
                if(!ok){s.valid=false;return s;}
                s.Put(channel,c);s.active=channel;s.bottomAge=0;s.stopping=false;
                return s;
            }
            s.bottomAge=Mathf.Min(c.plan.end*Cycle,s.bottomAge+delta);
            c=c.plan.At(s.bottomAge/Cycle,c);s.Put(channel,c);
            if(s.bottomAge>=c.plan.end*Cycle-.000001f)
            {c.height=s.bottomTarget;c.opening=0;s.Put(channel,c);s.active=-1;s.bottomChannel++;}
            return s;
        }

        internal static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        internal static float Smooth(float u){u=Mathf.Clamp01(u);return u*u*u*(10+u*(-15+6*u));}
        internal static float Derivative(float u){u=Mathf.Clamp01(u);return 30*u*u*(1-u)*(1-u);}
        internal static float Release(int channel)=>channel==0?.62f:channel==1?.32f:.04f;
        internal static float End(int channel)=>channel==0?.88f:channel==1?.58f:.29f;
        internal static int EventCycle(int index)=>Mathf.FloorToInt(index/3f);
        internal static int Channel(int index,int direction)
        {
            int slot=index-3*EventCycle(index);
            return direction>0?(slot==0?2:slot==1?1:0):(slot==0?0:slot==1?1:2);
        }
        static float Release(int channel,int direction)=>direction>0?Release(channel):1-End(channel);
        static float End(int channel,int direction)=>direction>0?End(channel):1-Release(channel);
        static State Close(State s,float delta)
        {
            if(!s.stopping){s.stopping=true;s.stopAge=0;for(int i=0;i<3;i++){var c=s.Get(i);c.stopOpening=c.opening;s.Put(i,c);}}
            s.stopAge=Mathf.Min(CloseTime,s.stopAge+delta);float e=Smooth(s.stopAge/CloseTime);
            for(int i=0;i<3;i++){var c=s.Get(i);c.opening=c.stopOpening*(1-e);s.Put(i,c);}
            return s;
        }
        static State CommitDirection(State s,int direction)
        {
            s.direction=direction;s.active=-1;
            float q=direction*s.phase;
            s.clockOffset=Mathf.Max(0,1-Mathf.FloorToInt(q));
            int cycle=Mathf.FloorToInt(q),slot=0;
            // Include an already-open transfer window in the new direction.
            while(slot<3&&cycle+End(Channel(cycle*3+slot,direction),direction)<=q+.0000001f)slot++;
            s.nextEvent=cycle*3+slot;
            return s;
        }
        internal static float CanonicalHeight(int channel,float phase)
        {
            float cycle=Mathf.Floor(phase),u=phase-cycle;
            return Rise*(cycle+Smooth((u-Release(channel))/(End(channel)-Release(channel))));
        }
        internal static float CanonicalOpening(int channel,float phase)
        {
            float u=Mathf.Repeat(phase,1);u=Mathf.Clamp01((u-Release(channel))/(End(channel)-Release(channel)));
            float s=Mathf.Sin(Mathf.PI*u);return s*s;
        }
        static Plan MakePlan(int channel,float phase,Contact c,float fingerSpan,out bool valid)
        {
            var p=new Plan{start=phase,end=Mathf.Max(c.deadline,phase+.04f),height=c.height,opening=c.opening,target=c.target};
            float radius=channel==2?.06566f:.306f,limit=channel==2?6.5f:5.2f;
            valid=false;
            for(int attempt=0;attempt<12;attempt++)
            {
                float duration=(p.end-p.start)*Cycle,peak=0,fingerPeak=0;
                for(int i=0;i<=64;i++)
                {
                    float u=i/64f,e=Smooth(u),ed=Derivative(u)/duration;
                    float b=c.opening*(1-e),bd=-c.opening*ed;
                    float a=Mathf.Abs(c.target-c.height)>.00001f?1:0;
                    float arc=Mathf.Sin(Mathf.PI*u);float od=bd*(1-a*arc*arc)+(1-b)*a*Mathf.PI*Mathf.Sin(2*Mathf.PI*u)/duration;
                    float hd=(c.target-c.height)*ed;
                    peak=Mathf.Max(peak,Mathf.Sqrt(hd*hd+radius*radius*od*od));fingerPeak=Mathf.Max(fingerPeak,Mathf.Abs(od)*fingerSpan);
                }
                float ratio=Mathf.Max(peak/limit,fingerPeak/1100f);
                if(ratio<=1.00001f){valid=true;return p;}
                p.end=p.start+(p.end-p.start)*ratio*1.002f;
                if(p.end-p.start>.75f)return p;
            }
            return p;
        }
        // Value-only replay: never captures a pose, reads physics or changes its input.
        internal static State AdvanceState(State s,float signedPhase,float delta,int intent)
        {
            if(!s.valid||delta<=0)return s;
            if(intent!=0&&intent!=s.direction)s.reversing=true;
            if(s.reversing)
            {
                if(Mathf.Abs(signedPhase)>.0000001f){s.valid=false;return s;}
                // Commit only after a fully closed pose has been displayed for one frame.
                // The final closure step must not share a frame with changing the clip clock.
                bool alreadyClosed=s.stopping&&s.stopAge>=CloseTime;
                s=Close(s,delta);
                if(alreadyClosed&&intent!=0)
                {
                    if(intent!=s.direction)s=CommitDirection(s,intent);
                    s.reversing=false;
                }
                return s;
            }
            if(Mathf.Abs(signedPhase)<=.0000001f)return Close(s,delta);
            float progress=s.direction*s.phase,travel=Mathf.Abs(signedPhase);
            if(s.stopping&&s.active>=0)
            {
                var c=s.Get(s.active);c.plan=MakePlan(s.active,progress,c,s.active==0?s.leftFingerSpan:s.active==1?s.rightFingerSpan:0,out bool ok);s.Put(s.active,c);
                if(!ok){s.valid=false;return s;}
            }
            s.stopping=false;s.stopAge=0;
            float end=progress+travel;
            if(!Finite(end)||Mathf.Abs(end)>10000){s.valid=false;return s;}
            for(int guard=0;guard<128;guard++)
            {
                if(s.active>=0)
                {
                    var c=s.Get(s.active);float next=Mathf.Min(end,c.plan.end);
                    c=c.plan.At(next,c);s.Put(s.active,c);progress=next;s.phase=s.direction*progress;
                    if(next>=c.plan.end-.0000001f){c.height=c.target;c.opening=0;s.Put(s.active,c);s.active=-1;}
                    if(progress>=end-.0000001f)return s;
                    continue;
                }
                int channel=Channel(s.nextEvent,s.direction),cycle=EventCycle(s.nextEvent);
                float when=cycle+Release(channel,s.direction);
                if(when>end){s.phase=s.direction*end;return s;}
                progress=Mathf.Max(progress,when);s.phase=s.direction*progress;
                var contact=s.Get(channel);contact.target=s.direction*(cycle+1)*Rise;contact.deadline=cycle+End(channel,s.direction);
                contact.plan=MakePlan(channel,progress,contact,channel==0?s.leftFingerSpan:channel==1?s.rightFingerSpan:0,out bool admitted);
                if(!admitted){s.valid=false;return s;}
                s.Put(channel,contact);s.active=channel;s.nextEvent++;
                if(progress>=end-.0000001f)return s;
            }
            s.valid=false;return s;
        }
    }

    // Paired authored UP defines both the changing finger pose and grip orientation.
    // Apply only the difference from its canonical contact path, not wrist-to-axis IK.
    public sealed class HeroRopeContactPose
    {
        readonly Animator animator;readonly HeroActionDefinition action,down;readonly AnimationClip clip,downClip;readonly TextAsset correction,downCorrection;readonly Quaternion heading;readonly HeroPoseDriver.RopeFingerPose fingers,downFingers;
        public HeroRopeContactPose(HeroPoseDriver driver,HeroActionDefinition definition,Quaternion heading):this(driver,definition,null,heading){}
        public HeroRopeContactPose(HeroPoseDriver driver,HeroActionDefinition definition,HeroActionDefinition down,Quaternion heading)
        {
            if(!driver||!driver.Ready||definition==null||!definition.canonicalRopeUp||!driver.PoseCorrectionReady(definition))throw new ArgumentException("Paired rope pose required");
            animator=driver.animator;action=definition;clip=definition.clip;correction=definition.poseCorrections;this.heading=heading;fingers=driver.BindRopeFingers(definition);
            if(down!=null)
            {
                if(!down.canonicalRopeDown||down.ropeDownSourceUpClip!=clip||down.ropeDownClip!=down.clip||!driver.PoseCorrectionReady(down))throw new ArgumentException("Paired DOWN pose required");
                this.down=down;downClip=down.clip;downCorrection=down.poseCorrections;downFingers=driver.BindRopeFingers(down,true);
            }
        }
        public bool SupportsDirection(int direction)=>direction==1||(direction==-1&&downFingers!=null);
        internal float FingerSpan(int side)=>Mathf.Max(fingers.Span(side),downFingers==null?0:downFingers.Span(side));
        internal bool Matches(Animator target,HeroActionDefinition definition,int direction)
        {
            if(!animator||animator!=target||!fingers.Valid||!action.canonicalRopeUp||action.clip!=clip||action.poseCorrectionClip!=clip||action.poseCorrections!=correction)return false;
            if(direction>0)return ReferenceEquals(action,definition);
            return downFingers!=null&&downFingers.Valid&&ReferenceEquals(down,definition)&&down.canonicalRopeDown&&down.clip==downClip&&down.poseCorrectionClip==downClip&&down.poseCorrections==downCorrection&&down.ropeDownClip==downClip&&down.ropeDownSourceUpClip==clip;
        }
        static Quaternion Rotation(Vector3 a,Vector3 b)
        {
            a.Normalize();b.Normalize();float w=1+Vector3.Dot(a,b);
            if(w<.000001f)return Quaternion.FromToRotation(a,b);
            var v=Vector3.Cross(a,b);return new Quaternion(v.x,v.y,v.z,w).normalized;
        }
        static float Solve(Transform upper,Transform lower,Transform end,Vector3 target)
        {
            var root=upper.position;var axis=target-root;
            float a=Vector3.Distance(root,lower.position),b=Vector3.Distance(lower.position,end.position);
            float length=Mathf.Clamp(axis.magnitude,Mathf.Abs(a-b)+.00001f,a+b-.00001f);
            axis.Normalize();var bend=Vector3.ProjectOnPlane(lower.position-root,axis).normalized;
            if(bend.sqrMagnitude<.000001f)bend=Vector3.ProjectOnPlane(upper.forward,axis).normalized;
            float along=(a*a-b*b+length*length)/(2*length);
            var middle=root+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            var orientation=end.rotation;
            upper.rotation=Rotation(lower.position-root,middle-root)*upper.rotation;
            lower.rotation=Rotation(end.position-lower.position,root+axis*length-lower.position)*lower.rotation;
            end.rotation=orientation;return Vector3.Distance(end.position,target);
        }
        internal float Apply(HeroRopeContactMotion.State state)
        {
            if(!state.Valid||!animator)return 0;
            float error=0;
            for(int limb=0;limb<4;limb++)
            {
                int channel=limb<2?limb:2;bool left=limb%2==0,hand=limb<2;
                float height=state.Height(channel)-HeroRopeContactMotion.CanonicalHeight(channel,state.Phase);
                float opening=state.Opening(channel)-HeroRopeContactMotion.CanonicalOpening(channel,state.Phase);
                var offset=Vector3.up*height+heading*new Vector3((left?-1:1)*(hand?.195011f:.064994f)*opening,0,-(hand?.046431f:.009264f)*opening);
                // Loosen the fingers while backing the palm clear of the rope. The
                // 6cm arc was selected from measured skinned-hand clearance sweeps.
                if(hand)offset-=heading*Vector3.forward*(.06f*Mathf.Sin(Mathf.PI*state.Opening(channel)));
                if(offset.sqrMagnitude<1e-12f)continue;
                var upper=animator.GetBoneTransform(hand?(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm):(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg));
                var lower=animator.GetBoneTransform(hand?(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm):(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg));
                var end=animator.GetBoneTransform(hand?(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand):(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot));
                if(!upper||!lower||!end)return float.PositiveInfinity;
                error=Mathf.Max(error,Solve(upper,lower,end,end.position+offset));
            }
            (state.Direction>0?fingers:downFingers).Apply(state.Phase,state.Opening(0),state.Opening(1));
            return error;
        }
    }
    public readonly struct HeroRopeContactFrame
    {
        internal readonly HeroRopeContactPose Pose;
        readonly HeroRopeContactMotion.State start,end;readonly float phaseDelta,delta,alignmentHeight;readonly int intent;readonly bool bottom,frozen;
        public bool IsActive=>Pose!=null&&end.Valid;
        public HeroRopeContactMotion.State Start=>start;
        public HeroRopeContactMotion.State End=>end;
        public bool ClockChanged=>start.Direction!=end.Direction||start.clockOffset!=end.clockOffset;
        public float StartTime=>(end.Direction*start.Phase+end.clockOffset)*HeroRopeContactMotion.Cycle;
        public float EndTime=>end.Clock;
        public float PlaybackRate=>delta>0?Mathf.Abs(phaseDelta)*HeroRopeContactMotion.Cycle/delta:0;
        internal HeroRopeContactFrame(HeroRopeContactPose pose,HeroRopeContactMotion.State start,HeroRopeContactMotion.State end,float phaseDelta,float delta,int intent,bool bottom=false,bool frozen=false,float alignmentHeight=0){this.alignmentHeight=alignmentHeight;this.bottom=bottom;this.frozen=frozen;Pose=pose;this.start=start;this.end=end;this.phaseDelta=phaseDelta;this.delta=delta;this.intent=intent;}
        public HeroRopeContactMotion.State Evaluate(float fraction)=>frozen?start:bottom?(delta>.1f?start:HeroRopeContactMotion.AdvanceEndpointState(start,alignmentHeight,delta*Mathf.Clamp01(fraction))):HeroRopeContactMotion.AdvanceState(start,phaseDelta*Mathf.Clamp01(fraction),delta*Mathf.Clamp01(fraction),intent);
        internal void Validate(Animator animator,HeroActionDefinition definition,float seconds,float frameStart,float dt,float rate)
        {
            if(Pose==null)return;
            if(!IsActive||!Pose.Matches(animator,definition,end.Direction)||Mathf.Abs(seconds-EndTime)>.0002f||Mathf.Abs(frameStart-StartTime)>.0002f||Mathf.Abs(dt-delta)>.0002f||Mathf.Abs(rate-PlaybackRate)>.0002f)throw new ArgumentException("Rope contact frame and authored clock differ");
        }
        internal float Apply(float fraction)=>!IsActive?0:Pose.Apply(fraction>=1?end:Evaluate(fraction));
    }
}



