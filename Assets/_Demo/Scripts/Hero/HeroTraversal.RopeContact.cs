using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroTraversal
    {
        HeroRopeContactMotion ropeContact;
        HeroRopeContactFrame ropeContactFrame;
        Vector3 ropeContactOrigin;
        public HeroRopeContactFrame RopeContactFrame=>ropeContactFrame;
        void ResetRopeContact(){ropeContact=null;ropeContactFrame=default;}
        bool UpdatePairedRopeContact(float vertical,float dt)
        {
            if(phase!=2||!canonicalRopeSession||surface.kind!=HeroTraversalKind.Rope||ropeUpDefinition==null)return false;
            int intent=vertical>.1f?1:vertical<-.1f?-1:0;
            if(ropeContact==null&&(intent==0||(intent<0&&ropeDownDefinition==null)))return false;
            if((intent<0&&ropeDownDefinition==null)||ropeEntry==null||!ropeEntry.ContactGeometryUnchanged||!RopeUpPairReady(hero.pose)||
                (ropeContact!=null&&(hero.pose.RopeContactReachError>.015f||Vector3.Distance(transform.position,ropeContactOrigin+Vector3.up*(ropeContact.Current.Phase*HeroRopeContactMotion.Rise))>.01f||Quaternion.Angle(transform.rotation,surface.transform.rotation)>.1f)))
            {Cancel(false);return true;}
            if(ropeContact==null)
            {
                ropeContactOrigin=transform.position;
                ropeContact=new HeroRopeContactMotion(new HeroRopeContactPose(hero.pose,ropeUpDefinition,ropeDownDefinition,surface.transform.rotation),intent);
            }
            int endpoint=0;float travel=0;
            // Reversal closes the released grip at a fixed body position first.
            // Only actual collision-resolved travel advances contact history.
            if(ropeContact.CanMove(intent))
            {
                var before=transform.position;
                var step=HeroClimbMotion.Move(capsule,surface.Bottom,surface.Top,intent,dt,intent>0?.00005f:.003f,intent>0);
                along=step.progress;travel=transform.position.y-before.y;endpoint=step.endpoint;
            }
            ropeContactFrame=ropeContact.Advance(travel,dt,intent);
            if(!ropeContactFrame.IsActive){Cancel(false);return true;}
            Action(ropeContact.Current.Direction>0?"ROPE_UP":"ROPE_DOWN");
            ActionTime=ropeContactFrame.EndTime;
            if(endpoint>0&&surface.topExit)BeginExit(true,false);
            else if(endpoint<0)BeginExit(false,false);
            return true;
        }
    }
}

