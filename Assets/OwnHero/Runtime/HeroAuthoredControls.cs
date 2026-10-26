using UnityEngine;
using UnityEngine.InputSystem;
namespace KingOfGraphic.Hero
{
    public struct HeroAuthorInput
    {
        public bool jump,crouch,prone,roll,upper,skill,walk;
        public int combo,skillStep;
        public HeroAuthorInput WithoutEdges()=>new HeroAuthorInput{walk=walk};
        public static HeroAuthorInput Read(bool allowed)
        {
            var k=Keyboard.current;if(!allowed||k==null)return default;
            return new HeroAuthorInput{jump=k.spaceKey.wasPressedThisFrame,crouch=k.cKey.wasPressedThisFrame,
                prone=k.zKey.wasPressedThisFrame,roll=k.vKey.wasPressedThisFrame,upper=k.fKey.wasPressedThisFrame,
                skill=k.xKey.wasPressedThisFrame,walk=k.leftAltKey.isPressed||k.rightAltKey.isPressed,
                combo=k.digit1Key.wasPressedThisFrame?3:k.digit2Key.wasPressedThisFrame?4:k.digit3Key.wasPressedThisFrame?5:0,
                skillStep=(k.rightBracketKey.wasPressedThisFrame?1:0)-(k.leftBracketKey.wasPressedThisFrame?1:0)};
        }
    }
    // Shared choices and posture only; the existing world/arena motor owns displacement and health.
    public sealed class HeroAuthoredControls
    {
        public int Posture{get;private set;}
        public int ComboLength=3,Skill;
        public string Transition{get;private set;}
        public float TransitionAge{get;private set;}
        public float SpeedLimit=>Transition!=null&&Transition.StartsWith("AUTHOR_Prone")?0:Posture==2?.65f:Posture==1?1.15f:float.PositiveInfinity;
        readonly Collider[] headroom=new Collider[32];
        float idleAge,locomotionAge,airAge,landAge;string locomotionId,airId,landId;bool wasAir;int airJumps;
        public void ResetAir(){wasAir=false;airJumps=0;airId=landId=null;airAge=landAge=0;}
        public void Air(HeroActionLibrary library,ref string id,ref float age,bool grounded,int jumpIndex,float vertical,bool drawn,float speed,float dt)
        {
            if(!grounded){
                wasAir=true;airJumps=Mathf.Max(airJumps,jumpIndex);landId=null;
                string next=jumpIndex==2?(vertical>.1f?"AUTHOR_DoubleJump":"AUTHOR_DoubleFall"):drawn?(vertical>.1f&&jumpIndex>0?"AUTHOR_WeaponJump":"AUTHOR_WeaponFall"):(vertical>.1f&&jumpIndex>0?"AIR_TAKEOFF":"AIR_FALL");
                if(next!=airId){airId=next;airAge=0;}else airAge+=dt;id=next;age=airAge;return;
            }
            if(wasAir){wasAir=false;landAge=0;landId=id=="AIR_LAND_HEAVY"?id:airJumps>1?"AUTHOR_DoubleLand":drawn?"AUTHOR_WeaponLand":speed>3.4f?"AUTHOR_LandRun":speed>.1f?"AUTHOR_LandWalk":"AIR_LAND_LIGHT";airId=null;airJumps=0;}
            if(landId!=null){landAge+=dt;if(landAge<library.Duration(landId)&&Posture==0){id=landId;age=landAge;}else landId=null;}
        }
        public void Select(HeroAuthorInput input){if(input.combo>=3)ComboLength=input.combo;Skill=(Skill+input.skillStep+9)%9;}
        public string Attack(HeroAuthorInput input,bool airborne,bool running,bool moving)
            =>input.skill?"ATK_SKILL_"+(char)('A'+Skill):input.upper?"ATK_UPPER":airborne?"ATK_JUMP":running&&moving?"ATK_DASH":null;
        public bool SetPosture(int next,CharacterController c)
        {
            if(!c)return false;
            float height=next==2?.6f:next==1?1.15f:1.8f;
            if(height>c.height)
            {
                float radius=c.radius*.95f;
                int n=Physics.OverlapCapsuleNonAlloc(c.transform.position+Vector3.up*(radius+.035f),
                    c.transform.position+Vector3.up*(height-radius),radius,headroom,~0,QueryTriggerInteraction.Ignore);
                if(n==headroom.Length)return false;
                for(int i=0;i<n;i++)if(headroom[i]&&!headroom[i].transform.IsChildOf(c.transform))return false;
            }
            int old=Posture;Posture=next;c.height=height;c.center=Vector3.up*(height*.5f);
            Transition=next==2?"AUTHOR_ProneEnter":old==2?"AUTHOR_ProneExit":next==1?"LOC_CROUCH_ENTER":"LOC_CROUCH_EXIT";
            TransitionAge=0;return true;
        }
        public void Reset(CharacterController c)
        {ResetAir();Posture=0;Transition=null;TransitionAge=idleAge=locomotionAge=0;locomotionId=null;if(c){c.height=1.8f;c.center=Vector3.up*.9f;}}
        public void ClearTransition(){Transition=null;TransitionAge=0;}
        public void Tick(float dt,HeroActionLibrary library)
        {if(Transition!=null){TransitionAge+=dt;if(TransitionAge>=library.Duration(Transition))ClearTransition();}}
        public static string Direction(Vector3 v)
        {
            if(Mathf.Abs(v.x)<.36f*Mathf.Abs(v.z))return v.z>=0?"Forward":"Backward";
            if(Mathf.Abs(v.z)<.36f*Mathf.Abs(v.x))return v.x>=0?"Right":"Left";
            return (v.z>=0?"Forward":"Backward")+(v.x>=0?"Right":"Left");
        }
        public void Locomotion(HeroActionLibrary library,ref string id,ref float age,Vector3 local,bool drawn,bool swimming,float swimVertical,float dt)
        {
            float speed=Vector3.ProjectOnPlane(local,Vector3.up).magnitude;
            bool ordinary=id.StartsWith("LOC_")||id.StartsWith("COMBAT_")||id.StartsWith("WATER_");
            if(!ordinary){idleAge=0;return;}
            string next=null,dir=Direction(local);
            if(swimming)
            {
                if(Mathf.Abs(swimVertical)>.1f)next=swimVertical>0?"AUTHOR_SwimUp":"AUTHOR_SwimDown";
                else if(speed>.12f)next=dir=="Forward"?id=="WATER_SWIM_FAST"?id:"WATER_SWIM":"AUTHOR_Swim"+dir;
            }
            else if(Transition!=null){id=Transition;age=TransitionAge;return;}
            else if(Posture==2)next=speed<.08f?"AUTHOR_ProneIdle":local.z<-.05f?"AUTHOR_ProneBackward":"AUTHOR_ProneForward";
            else if(Posture==1)next=speed<.08f?"LOC_CROUCH_IDLE":"AUTHOR_Crouch"+dir;
            else if(drawn&&speed>3.4f&&local.z>speed*.8f)next="AUTHOR_CombatRun";
            else if(!drawn&&speed>.12f&&dir!="Forward"&&(id=="LOC_WALK"||id=="LOC_RUN"||id=="LOC_SPRINT"))next="AUTHOR_"+(speed>3.4f?"Run":"Walk")+dir;
            else if(!drawn&&speed<.08f&&id=="LOC_IDLE")
            {idleAge+=dt;if(idleAge>=7)next="AUTHOR_IdleVariation";if(idleAge>=7+library.Duration("AUTHOR_IdleVariation"))idleAge=0;}
            else idleAge=0;
            if(next==null||library.Find(next)==null)return;
            if(next!=locomotionId){locomotionId=next;locomotionAge=0;}else locomotionAge+=dt;
            id=next;age=locomotionAge;
        }
    }
}
