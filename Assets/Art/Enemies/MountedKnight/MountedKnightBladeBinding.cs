using UnityEngine;
/// <summary>Samples two skinned blade vertices; avoids scale-dependent baked-mesh hand offsets.</summary>
public sealed class MountedKnightBladeBinding
{
    SkinnedMeshRenderer skin; Transform[] bones; Matrix4x4[] bind; Vector3 tipVertex,guardVertex; BoneWeight tipWeight,guardWeight;
    Vector3 Skin(Vector3 v,BoneWeight w){Vector3 r=Vector3.zero;Add(ref r,v,w.boneIndex0,w.weight0);Add(ref r,v,w.boneIndex1,w.weight1);Add(ref r,v,w.boneIndex2,w.weight2);Add(ref r,v,w.boneIndex3,w.weight3);return r;}
    void Add(ref Vector3 result,Vector3 v,int index,float weight){if(weight>0&&index<bones.Length&&bones[index])result+=bones[index].TransformPoint(bind[index].MultiplyPoint3x4(v))*weight;}
    public bool Initialize(Transform root){
        Transform hand=null;foreach(var t in root.GetComponentsInChildren<Transform>())if(t.name=="Bip01_R_Hand"){hand=t;break;}if(!hand)return false;
        foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>()){
            var mesh=r.sharedMesh;if(!mesh||!mesh.isReadable)continue;
            for(int sub=0;sub<r.sharedMaterials.Length;sub++){
                if(!r.sharedMaterials[sub]||!r.sharedMaterials[sub].name.StartsWith("Warden_BladeSilver"))continue;
                skin=r;bones=r.bones;bind=mesh.bindposes;var vertices=mesh.vertices;var weights=mesh.boneWeights;float near=float.MaxValue,far=-1;
                foreach(int i in mesh.GetIndices(sub)){float distance=(Skin(vertices[i],weights[i])-hand.position).sqrMagnitude;if(distance<near){near=distance;guardVertex=vertices[i];guardWeight=weights[i];}if(distance>far){far=distance;tipVertex=vertices[i];tipWeight=weights[i];}}
                return far>near;
            }
        }return false;
    }
    public void Sample(Transform tip,Transform guard){if(!skin)return;tip.position=Skin(tipVertex,tipWeight);guard.position=Skin(guardVertex,guardWeight);}
}
