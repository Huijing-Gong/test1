using UnityEngine;
namespace KingOfGraphic.Hero
{
    // Offline full-skin bounds, valid only for the paired runtime clip and mesh.
    public static class HeroDeathClearance
    {
        public static bool TryGetEnvelope(HeroPoseDriver pose,out Bounds envelope)
        {
            envelope=default;if(!pose||!pose.library)return false;
            var skins=pose.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if(skins.Length!=1||!skins[0].sharedMesh)return false;
            if(!Valid(pose.library.Find("DEATH_GROUND"),skins[0].sharedMesh,pose.transform.lossyScale,out var ground)||!Valid(pose.library.Find("DEATH_HOLD"),skins[0].sharedMesh,pose.transform.lossyScale,out var hold))return false;
            envelope=ground;envelope.Encapsulate(hold);return true;
        }
        static bool Valid(HeroActionDefinition d,Mesh mesh,Vector3 scale,out Bounds b)
        {
            b=default;if(d==null||!d.clip||d.bodyEnvelopeClip!=d.clip||d.bodyEnvelopeMesh!=mesh||Vector3.Distance(d.bodyEnvelopeScale,scale)>.0001f)return false;
            b=d.bodyEnvelope;Vector3 min=b.min,max=b.max;
            return Finite(min)&&Finite(max)&&b.size.x>.01f&&b.size.y>.01f&&b.size.z>.01f&&b.size.magnitude<5;
        }
        static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        public static float ProjectedExtent(Bounds envelope,Quaternion facing,Vector3 direction)
        {
            float extent=float.NegativeInfinity;Vector3 min=envelope.min,max=envelope.max;
            for(int i=0;i<8;i++){var p=new Vector3((i&1)==0?min.x:max.x,(i&2)==0?min.y:max.y,(i&4)==0?min.z:max.z);extent=Mathf.Max(extent,Vector3.Dot(facing*p,direction));}
            return extent;
        }
    }
}
