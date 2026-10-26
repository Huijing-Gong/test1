using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace KingOfGraphic.Hero
{
    // A tap is admitted on release; a hold requests sprint without an initial dodge.
    public sealed class HeroDashGesture
    {
        public const float HoldSeconds=.20f;
        bool down,blocked;float started;
        public bool Tap {get;private set;}
        public bool Sprint {get;private set;}
        public bool Released {get;private set;}
        public void Sample(bool held,bool pressed,bool released,float now,bool allowed=true)
        {
            Tap=Released=false;
            if(!allowed){down=false;blocked=held;Sprint=false;return;}
            if(blocked){if(!held)blocked=false;Sprint=false;return;}
            if(!down&&(pressed||held)){down=true;started=now;}
            if(released||down&&!held){Tap=down&&now-started<HoldSeconds;Released=down;down=false;}
            Sprint=down&&held&&now-started>=HoldSeconds;
        }
    }
    public static class HeroDashInput
    {
        static readonly HeroDashGesture gesture=new HeroDashGesture();
        // Unity Editor and Player have separate Input System states. Observe only
        // completed gameplay input updates; an editor query must never cache an
        // empty keyboard state for the subsequent player frame.
        static void AfterInputUpdate(){
            var kind=UnityEngine.InputSystem.LowLevel.InputState.currentUpdateType;
            if(kind==UnityEngine.InputSystem.LowLevel.InputUpdateType.Editor||kind==UnityEngine.InputSystem.LowLevel.InputUpdateType.BeforeRender)return;
            var k=Keyboard.current;var m=Mouse.current;
            bool held=k!=null&&(k.leftShiftKey.isPressed||k.rightShiftKey.isPressed)||m!=null&&m.rightButton.isPressed;
            bool press=k!=null&&(k.leftShiftKey.wasPressedThisFrame||k.rightShiftKey.wasPressedThisFrame)||m!=null&&m.rightButton.wasPressedThisFrame;
            bool release=!held&&(k!=null&&(k.leftShiftKey.wasReleasedThisFrame||k.rightShiftKey.wasReleasedThisFrame)||m!=null&&m.rightButton.wasReleasedThisFrame);
            gesture.Sample(held,press,release,Time.realtimeSinceStartup,Time.timeScale>0&&Cursor.lockState==CursorLockMode.Locked);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset(){InputSystem.onAfterUpdate-=AfterInputUpdate;InputSystem.onAfterUpdate+=AfterInputUpdate;gesture.Sample(false,false,false,0,false);}
        public static bool Held=>gesture.Sprint;
        public static bool Pressed=>gesture.Tap;
        public static bool Released=>gesture.Released;
    }
    public struct HeroCombatInput
    {
        public bool attack, held, released, guard, guardPressed, dodge, toggleLock, run;
        public static Func<bool,HeroCombatInput> GameplayReader;
        public static Func<string,string> BindingLabel;
        public static string Label(string action,string fallback) => BindingLabel != null ? BindingLabel(action) : fallback;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGameBinding() { GameplayReader=null; BindingLabel=null; }

        public static HeroCombatInput Read(bool allowed)
        {
            if(!allowed)return default;
            if(GameplayReader!=null)return GameplayReader(allowed);
            var k=Keyboard.current;var m=Mouse.current;
            return new HeroCombatInput {
                attack=m!=null&&m.leftButton.wasPressedThisFrame,held=m!=null&&m.leftButton.isPressed,
                released=m!=null&&m.leftButton.wasReleasedThisFrame,toggleLock=false,run=HeroDashInput.Held,
                guard=k!=null&&k.qKey.isPressed,guardPressed=k!=null&&k.qKey.wasPressedThisFrame,
                dodge=HeroDashInput.Pressed
            };
        }
    }

    // Capsule displacement is independent of clip length and identical in both worlds.
    public static class HeroDodgeMotion
    {
        public const float Distance=3.6f;
        public static float TravelAt(float age,float duration)
        {
            float u=Mathf.Clamp01(age/Mathf.Max(.01f,duration));
            // Integral of the previous 1 -> .42 deceleration, normalized to one dodge.
            return Distance*(u-.29f*u*u)/.71f;
        }
        public static float Step(float age,float delta,float duration)=>
            Mathf.Max(0,TravelAt(age+Mathf.Max(0,delta),duration)-TravelAt(age,duration));
    }

    public static class HeroDodgeCounterWindow
    {
        public const float Duration=.12f;
        public static bool Open(float age,float dodgeDuration,bool currentPerfect,bool rewardReady)
        {float remaining=dodgeDuration-age;return age>=0&&currentPerfect&&rewardReady&&remaining>0&&remaining<=Duration+.000001f;}
    }

    public struct HeroDodgeSample { public string Id; public float Time; }
    public static class HeroDodgePresentation
    {
        // Defense confirmation is gameplay state. Recovery starts at its authored
        // pose junction, with its own zero, while the capsule retains its clock.
        public static HeroDodgeSample Resolve(HeroActionLibrary library,string requested,float elapsed)
        {
            var sample=new HeroDodgeSample{Id=requested,Time=elapsed};
            if(!library||requested==null||!requested.StartsWith("PERFECT_DODGE_"))return sample;
            string ordinary=requested.Substring("PERFECT_".Length);
            float join=library.Find(ordinary)?.recoveryJunction??0;
            if(join<=0)return sample;
            sample.Id=elapsed<join?ordinary:requested;
            sample.Time=elapsed<join?elapsed:elapsed-join;
            return sample;
        }
    }

    // Both worlds sample the displayed sword and use exactly the same temporal sweep.
    // Damage targets remain scene adapters; neither animation nor VFX owns damage.
    public sealed class HeroBladeSweep
    {
        long sequence=-1;bool valid;Vector3 priorBase,priorTip;
        public void Reset(){sequence=-1;valid=false;}
        public void Sample(HeroPoseDriver pose,HeroCombatClock clock,Action<Vector3,Vector3> sweep)
        {
            if(!pose||!pose.WeaponReady||!clock.Active){Reset();return;}
            if(sequence!=clock.Sequence){sequence=clock.Sequence;valid=false;}
            if(!clock.HitWindow){valid=false;return;}
            float start=Mathf.Max(clock.PreviousTime,clock.Open),end=Mathf.Min(clock.Time,clock.Close);
            int steps=Mathf.Clamp(Mathf.CeilToInt((end-start)*120),1,24);
            for(int i=0;i<=steps;i++) {
                if(pose.CurrentAction!=clock.Id||!pose.TryQueryBladeAtActionTime(Mathf.Lerp(start,end,(float)i/steps),out Vector3 a,out Vector3 b)){valid=false;break;}
                sweep(a,b);
                if(valid){sweep(priorTip,b);sweep((priorBase+priorTip)*.5f,(a+b)*.5f);}
                priorBase=a;priorTip=b;valid=true;
            }
        }
    }

    // Ordinary and fast swimming share matching stroke phases, measured from the
    // corrected source clips. Changing speed changes the period, never restarts a stroke.
    public sealed class HeroSwimClock
    {
        float phase;
        public void Reset(){phase=0;}
        public static bool IsStroke(string id)=>id=="WATER_SWIM"||id=="WATER_SWIM_FAST";
        public float Step(string id,HeroActionLibrary library,float delta)
        {
            float duration=Mathf.Max(.01f,library.Duration(id));
            phase+=Mathf.Max(0,delta)/duration;
            return phase*duration;
        }
    }

    // Preserve normalized foot phase across walking/running and directional changes.
    public sealed class HeroGaitClock
    {
        float phase,idleTime,startDistance;
        bool startActive,startArmed,stopActive,lastOrdinaryMoving;
        float stopTime,fastMemory;string stopId;
        string combatDirection,lastCombatCycle,ordinaryCycle;
        public string PresentedAction {get;private set;}
        public bool CompletedStart {get;private set;}
        public bool Starting=>startActive;
        public bool Stopping=>stopActive;
        public float PlaybackRate {get;private set;}=1;
        public void Reset(){phase=idleTime=0;PlaybackRate=1;InterruptGroundTransitions();PresentedAction=null;}
        public void InterruptStart(){startActive=startArmed=CompletedStart=false;startDistance=0;}
        public void InterruptGroundTransitions(){ordinaryCycle=null;lastCombatCycle=null;ResetCombatDirection();InterruptStart();stopActive=lastOrdinaryMoving=false;stopTime=fastMemory=0;stopId=null;}
        public static bool IsGroundTransition(string id)=>id=="LOC_START"||id=="AUTHOR_RunStart"||id=="LOC_STOP"||id=="LOC_RUN_STOP";
        // The original controller retains displacement and immediate input ownership.
        public float StepPresented(string id,HeroActionLibrary library,float speed,float delta,bool eligible,bool? stopEligible=null)
        {
            CompletedStart=false;PresentedAction=id;float dt=Mathf.Max(0,delta);
            bool canStop=stopEligible??eligible;
            bool moving=IsOrdinaryCycle(id);
            if(!moving)ordinaryCycle=null;
            if(id=="LOC_RUN"||id=="LOC_SPRINT")fastMemory=.25f;else if(moving)fastMemory=Mathf.Max(0,fastMemory-dt);
            if(id!="LOC_IDLE")
            {
                // Movement and combat immediately interrupt a visual stop, without delaying input.
                if(stopActive){stopActive=false;InterruptStart();}
                lastOrdinaryMoving=moving&&canStop;
                if(!moving||!canStop)fastMemory=0;
            }
            else if(canStop)
            {
                if(!stopActive&&lastOrdinaryMoving)
                {
                    stopId=fastMemory>0?"LOC_RUN_STOP":"LOC_STOP";var stop=library.Find(stopId);
                    stopActive=stop!=null&&stop.clip&&stop.groundStop&&stop.duration>0;stopTime=0;lastOrdinaryMoving=false;InterruptStart();
                }
                if(stopActive)
                {
                    float duration=library.Duration(stopId);PlaybackRate=1;
                    if(stopTime<duration){stopTime=Mathf.Min(duration,stopTime+dt);PresentedAction=stopId;return stopTime;}
                    stopActive=false;fastMemory=0;
                }
            }
            else{stopActive=lastOrdinaryMoving=false;fastMemory=0;}
            if(id=="LOC_IDLE"&&!stopActive)fastMemory=0;
            string startId=library.authoredControls&&(id=="LOC_RUN"||id=="LOC_SPRINT")?"AUTHOR_RunStart":"LOC_START";var start=library.Find(startId);
            if(!eligible||start==null||!start.clip||start.travelDistance<=.01f||(id!="LOC_IDLE"&&id!="LOC_WALK"&&!(library.authoredControls&&IsOrdinaryCycle(id))))
            {InterruptStart();return Step(id,library,speed,delta);}
            if(id=="LOC_IDLE"){startActive=false;startDistance=0;startArmed=true;return Step(id,library,speed,delta);}
            if(startArmed&&!startActive){startActive=true;startArmed=false;startDistance=0;}
            if(!startActive)return Step(id,library,speed,delta);
            startDistance+=Mathf.Max(0,speed)*Mathf.Max(0,delta);
            if(startDistance>=start.travelDistance)
            {
                var cycle=library.Find(id);float distance=Mathf.Max(.01f,cycle.cycleDistance);
                phase=start.exitPhase+(startDistance-start.travelDistance)/distance;
                startActive=false;CompletedStart=true;PlaybackRate=Mathf.Max(0,speed)*cycle.duration/distance;
                return (phase+cycle.phaseOffset)*cycle.duration;
            }
            PresentedAction=startId;PlaybackRate=Mathf.Max(0,speed)*start.duration/start.travelDistance;
            return startDistance/start.travelDistance*start.duration;
        }
        // Angular speed keeps presentation eligibility independent of frame duration.
        // Thresholds preserve the previous 60 Hz behavior; movement still owns yaw.
        public static float SignedYawRate(float previousYaw,float currentYaw,float delta)
        {return delta>0?Mathf.DeltaAngle(previousYaw,currentYaw)/delta:0;}
        public static bool AllowsForwardStart(float speed,float forwardSpeed,float yawDegreesPerSecond,float groundAngle=0)
        {return groundAngle<=3f&&Mathf.Abs(yawDegreesPerSecond)<720f&&(speed<.08f||forwardSpeed>speed*.8f);}
        public static string SelectTurn(float yawDegreesPerSecond,bool swimming)
        {return Mathf.Abs(yawDegreesPerSecond)>(swimming?120f:300f)?(swimming?"WATER_TURN_":"LOC_TURN_")+(yawDegreesPerSecond>0?"R":"L"):null;}
        public static bool IsGroundCycle(string id)=>id=="LOC_IDLE"||id=="LOC_WALK"||id=="LOC_RUN"||id=="LOC_SPRINT"||id=="LOC_STAIRS_UP"||id=="LOC_STAIRS_DOWN"||id=="COMBAT_IDLE"||id.StartsWith("COMBAT_MOVE_");
        // Keep nearby diagonal input from repeatedly restarting a presentation blend.
        // This state never filters movement or the instantaneous dodge/hit direction.
        public void ResetCombatDirection(){combatDirection=null;}
        public string SelectCombatMove(Vector3 velocity)
        {
            if(velocity.x*velocity.x+velocity.z*velocity.z<.00000001f)
            {ResetCombatDirection();return "COMBAT_IDLE";}
            float angle=Mathf.Atan2(velocity.x,velocity.z)*Mathf.Rad2Deg;
            if(combatDirection!=null)
            {
                float heading=combatDirection=="F"?0:combatDirection=="R"?90:combatDirection=="B"?180:-90;
                if(Mathf.Abs(Mathf.DeltaAngle(heading,angle))<=53f)return "COMBAT_MOVE_"+combatDirection;
            }
            combatDirection=HeroPoseDriver.DirectionSuffix(velocity);
            return "COMBAT_MOVE_"+combatDirection;
        }
        public static bool IsOrdinaryCycle(string id)=>id=="LOC_WALK"||id=="LOC_RUN"||id=="LOC_SPRINT";
        // Presentation hysteresis only: physical velocity and input are untouched.
        // First selection uses the original boundaries; the band prevents blend
        // restarts when measured speed fluctuates around either boundary.
        public string SelectOrdinaryGroundCycle(Vector3 velocity)
        {
            float speed=new Vector2(velocity.x,velocity.z).magnitude;
            if(speed<.08f){ordinaryCycle=null;return "LOC_IDLE";}
            if(ordinaryCycle==null)return ordinaryCycle=SelectGroundCycle(velocity,false);
            if(ordinaryCycle=="LOC_WALK")ordinaryCycle=speed>=4.85f?"LOC_SPRINT":speed>3.5f?"LOC_RUN":"LOC_WALK";
            else if(ordinaryCycle=="LOC_RUN")ordinaryCycle=speed>=4.85f?"LOC_SPRINT":speed<=3.3f?"LOC_WALK":"LOC_RUN";
            else ordinaryCycle=speed<=3.3f?"LOC_WALK":speed<4.65f?"LOC_RUN":"LOC_SPRINT";
            return ordinaryCycle;
        }
        public static string SelectGroundCycle(Vector3 velocity,bool drawn)
        {
            float speed=new Vector2(velocity.x,velocity.z).magnitude;
            if(speed<.08f)return drawn?"COMBAT_IDLE":"LOC_IDLE";
            if(drawn)return "COMBAT_MOVE_"+HeroPoseDriver.DirectionSuffix(velocity);
            return speed>=4.75f?"LOC_SPRINT":speed>3.4f?"LOC_RUN":"LOC_WALK";
        }
        public static bool IsReverseSwordPair(string a,string b)=>a=="COMBAT_MOVE_F"&&b=="COMBAT_MOVE_B"||a=="COMBAT_MOVE_B"&&b=="COMBAT_MOVE_F";
        public static bool IsReverseSwordPair(string a,string b,HeroActionLibrary library)
        {
            if(library&&library.authoredControls)return false;
            var from=library?library.Find(a):null;var to=library?library.Find(b):null;
            if(from!=null&&to!=null&&from.commonCombatStance&&to.commonCombatStance)return from.reversedCombatPhase!=to.reversedCombatPhase;
            return IsReverseSwordPair(a,b);
        }
        public float Step(string id,HeroActionLibrary library,float actualSpeed,float delta)
        {
            string prior=lastCombatCycle;lastCombatCycle=id.StartsWith("COMBAT_MOVE_")?id:null;
            if(!id.StartsWith("COMBAT_MOVE_"))ResetCombatDirection();
            float duration=Mathf.Max(.01f,library.Duration(id));
            if(id=="LOC_IDLE"||id=="COMBAT_IDLE"){PlaybackRate=1;idleTime+=delta;return idleTime;}
            var def=library.Find(id);
            // Retreat was authored by reversing the forward cycle. Reflect its
            // phase on direction reversal so the same visible foot pose is retained.
            if(IsReverseSwordPair(prior,id,library))phase=Mathf.Floor(phase)+Mathf.Repeat(-Mathf.Repeat(phase,1)-(library.Find(prior)?.phaseOffset??0)-(def?.phaseOffset??0),1);
            float distance=def!=null&&def.cycleDistance>.01f?def.cycleDistance:(id=="LOC_RUN"||id=="LOC_SPRINT"?5.6f:3f)*duration;
            PlaybackRate=Mathf.Max(0,actualSpeed)*duration/distance;
            phase+=Mathf.Max(0,actualSpeed)*Mathf.Max(0,delta)/distance;
            return (phase+(def?.phaseOffset??0))*duration;
        }
    }
}
