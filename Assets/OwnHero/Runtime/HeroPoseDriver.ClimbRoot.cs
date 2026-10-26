using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        readonly Dictionary<TextAsset,Vector4[]> climbRootTracks=new Dictionary<TextAsset,Vector4[]>();
        Vector3 ClimbRootOffset(HeroActionDefinition d,float age)
        {
            if(d==null||!d.authoredClimbRoot||d.authoredClimbRootClip!=d.clip)return Vector3.zero;
            if(!climbRootTracks.TryGetValue(d.authoredClimbRoot,out var track))
            {
                var lines=d.authoredClimbRoot.text.Split('\n');var samples=new List<Vector4>();
                if(lines.Length==0||lines[0].Trim()!="HCR1")return Vector3.zero;
                foreach(var line in lines){var parts=line.Trim().Split(',');if(parts.Length!=4)continue;
                    if(float.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out float t)&&
                       float.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float x)&&
                       float.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float y)&&
                       float.TryParse(parts[3],NumberStyles.Float,CultureInfo.InvariantCulture,out float z)&&
                       !float.IsNaN(x+y+z+t)&&!float.IsInfinity(x+y+z+t))samples.Add(new Vector4(x,y,z,t));}
                track=samples.ToArray();climbRootTracks[d.authoredClimbRoot]=track;
            }
            if(track.Length<2)return Vector3.zero;
            float clock=(float)AuthorTime(d,age);int lo=0,hi=track.Length-1;
            while(hi-lo>1){int mid=(lo+hi)/2;if(track[mid].w<=clock)lo=mid;else hi=mid;}
            var a=track[lo];var b=track[hi];return Vector3.Lerp((Vector3)a,(Vector3)b,Mathf.InverseLerp(a.w,b.w,clock));
        }
        void ApplyAuthoredClimbRoot(HeroActionDefinition d,float age,HeroActionDefinition old,float oldAge,float blendWeight)
        {
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);if(!hips)return;
            // Source pelvis travel is separated from collision-resolved capsule movement.
            var offset=Vector3.Lerp(ClimbRootOffset(old,oldAge),ClimbRootOffset(d,age),blendWeight);
            hips.position+=animator.transform.TransformVector(offset);
        }
    }
}
