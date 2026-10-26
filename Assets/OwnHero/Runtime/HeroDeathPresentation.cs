using UnityEngine;
namespace KingOfGraphic.Hero
{
    // Shared presentation clock; physics, damage, saves and retries stay with each owner.
    public sealed class HeroDeathPresentation
    {
        enum Stage { None, Air, Ground, Water, Hold }
        Stage stage;HeroActionLibrary library;float elapsed,supportTime,unsupportedTime;int holdFrames,lastPresentedFrame=-1;
        public bool Active=>stage!=Stage.None;
        public string Id {get;private set;}
        public float Time {get;private set;}
        public bool ReadyForUI {get;private set;}
        public float BlendDuration=>stage==Stage.Ground?.16f:stage==Stage.Hold&&Id=="DEATH_HOLD"?0:-1;
        public void Reset(){stage=Stage.None;library=null;Id=null;Time=elapsed=supportTime=unsupportedTime=0;holdFrames=0;lastPresentedFrame=-1;ReadyForUI=false;}
        public void Begin(HeroActionLibrary actions,bool inWater,bool supported)
        {
            Reset();library=actions;
            Enter(inWater?Stage.Water:supported?Stage.Ground:Stage.Air);
        }
        void Enter(Stage next)
        {
            stage=next;elapsed=Time=0;holdFrames=0;lastPresentedFrame=-1;ReadyForUI=false;
            Id=next==Stage.Air?"DEATH_AIR":next==Stage.Ground?"DEATH_GROUND":next==Stage.Water?"DEATH_WATER":Id=="DEATH_WATER"?"DEATH_WATER_HOLD":"DEATH_HOLD";
        }
        public void TickAfterPhysics(float delta,bool supported)
        {
            if(!Active||!library||delta<=0)return;
            supportTime=supported?supportTime+delta:0;unsupportedTime=supported?0:unsupportedTime+delta;elapsed+=delta;
            if((stage==Stage.Ground||stage==Stage.Hold&&Id=="DEATH_HOLD")&&!supported)
            {
                ReadyForUI=false;holdFrames=0;lastPresentedFrame=-1;
                if(unsupportedTime>=.08f){Enter(Stage.Air);return;}
            }
            float duration=Mathf.Max(.01f,library.Duration(Id));
            if(stage==Stage.Air)
            {
                // Holding the air clip's last pose is not a completed corpse pose.
                Time=Mathf.Min(elapsed,duration);
                // Physical landing may precede the end of the airborne clip.
                if(supportTime>=.06f)Enter(Stage.Ground);
            }
            else if(stage==Stage.Ground||stage==Stage.Water)
            {
                Time=Mathf.Min(elapsed,duration);
                if(elapsed>=duration&&(stage==Stage.Water||supportTime>=.06f))Enter(Stage.Hold);
            }
            else Time=elapsed;
        }
        public void MarkPresented(string id)
        {
            if(stage!=Stage.Hold||id!=Id||Id=="DEATH_HOLD"&&supportTime<.06f)return;
            if(lastPresentedFrame==UnityEngine.Time.frameCount)return;
            lastPresentedFrame=UnityEngine.Time.frameCount;holdFrames++;
            float settle=Mathf.Max(.12f,library.Find(Id)?.blendDuration??.1f);
            ReadyForUI=holdFrames>=2&&Time>=settle;
        }
    }
}
