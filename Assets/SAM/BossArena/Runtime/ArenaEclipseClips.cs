using System;
using UnityEngine;
namespace SamProduction.BossArena
{
    // Serialized references to imported V13 clips; no Resources lookup or old SAM trajectory dependency.
    public sealed class ArenaEclipseClips : MonoBehaviour
    {
        public bool authoredLocomotion;
        public AnimationClip J002, WALK, RUN, J058, J048, J059, J066, J065, J045, J056, J063, J026;
        [Serializable]
        public sealed class RootMotionTrack
        {
            public AnimationClip clip;
            // Original derived .anim RootT.x/z, normalized Humanoid root units, before stripping XZ.
            public AnimationCurve x,z;
        }
        public RootMotionTrack[] rootMotionTracks=Array.Empty<RootMotionTrack>();
        public Vector3 GetDelta(AnimationClip clip,float previous,float next)
        {
            if(clip==null)throw new ArgumentNullException(nameof(clip));
            var track=FindTrack(clip);
            if(track==null||track.x==null||track.z==null)
                throw new InvalidOperationException("Missing V13 normalized root track: "+clip.name);
            previous=Mathf.Clamp(previous,0,clip.length);next=Mathf.Clamp(next,0,clip.length);
            return new Vector3(track.x.Evaluate(next)-track.x.Evaluate(previous),0,
                track.z.Evaluate(next)-track.z.Evaluate(previous));
        }
        RootMotionTrack FindTrack(AnimationClip clip)
        {
            RootMotionTrack found=null;
            if(rootMotionTracks==null)return null;
            foreach(var track in rootMotionTracks)if(track!=null&&track.clip==clip){
                if(found!=null)throw new InvalidOperationException("Duplicate V13 root track: "+clip.name);
                found=track;
            }
            return found;
        }
        public AnimationClip Attack(ArenaAttack a)
        {
            switch(a) {
                case ArenaAttack.Stomp:return J048; case ArenaAttack.ComboTwo:return J059;
                case ArenaAttack.ComboThree:return J066; case ArenaAttack.DashPunch:return J065;
                case ArenaAttack.Ignite:return J045; case ArenaAttack.DiveKick:return J056;
                case ArenaAttack.Meteor:return J063; default:return J058;
            }
        }
        public void Validate()
        {
            if(authoredLocomotion){foreach(var c in new[]{J002,WALK,RUN})if(!c||c.legacy||!c.humanMotion||c.length<=.05f)throw new InvalidOperationException("Invalid authored Boss locomotion");}
            else{Check(J002,4,"J002");Check(WALK,2,"WALK");Check(RUN,1.25f,"RUN");}Check(J026,3.5f,"J026");
            Check(J058,3.208333f,"J058");Check(J048,3.5f,"J048");Check(J059,4.5f,"J059");
            Check(J066,5.291667f,"J066");Check(J065,3.458333f,"J065");Check(J045,4.75f,"J045");
            Check(J056,3.833333f,"J056");Check(J063,5.875f,"J063");
            foreach(var clip in new[]{J058,J048,J059,J066,J065,J045,J056,J063}){
                var track=FindTrack(clip);
                if(track==null||track.x==null||track.z==null||track.x.length==0||track.z.length==0)
                    throw new InvalidOperationException("Assign both original RootT.x/z curves (explicit zero curve if stationary): "+clip.name);
            }
        }
        static void Check(AnimationClip c,float duration,string id)
        {
            if(c==null || Mathf.Abs(c.length-duration)>.03f || c.legacy)
                throw new InvalidOperationException("V13 clip missing/legacy/duration mismatch: "+id);
        }
    }
}
