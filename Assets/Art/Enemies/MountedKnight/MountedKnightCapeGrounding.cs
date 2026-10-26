using System;
using System.Linq;
using UnityEngine;

// Keeps the animated cape above the floor during collapse; authored bone poses are restored each frame.
[DefaultExecutionOrder(150)]
public sealed class MountedKnightCapeGrounding : MonoBehaviour {
 Health health;SkinnedMeshRenderer armor;Transform[] bones;int[] cape,samples;Vector3[] vertices,positions,localPose;BoneWeight[] weights;Matrix4x4[] bind,skin;float[] lift;bool captured;float floor;
 void Start(){health=GetComponent<Health>();armor=GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r=>r.name=="Warden_CraftedArmorAndCloth");if(!armor){enabled=false;return;}bones=armor.bones;var mesh=armor.sharedMesh;vertices=mesh.vertices;weights=mesh.boneWeights;bind=mesh.bindposes;cape=Enumerable.Range(0,bones.Length).Where(i=>bones[i].name.StartsWith("WardenCape_")).ToArray();var capeSet=new System.Collections.Generic.HashSet<int>(cape);samples=Enumerable.Range(0,vertices.Length).Where(i=>i%3==0&&capeSet.Contains(weights[i].boneIndex0)).ToArray();skin=new Matrix4x4[bones.Length];positions=new Vector3[bones.Length];localPose=new Vector3[bones.Length];lift=new float[bones.Length];floor=transform.position.y;
  foreach(var hit in Physics.RaycastAll(transform.position+Vector3.up*2,Vector3.down,5))if(!hit.transform.IsChildOf(transform)&&hit.normal.y>.8f)floor=hit.point.y;
 }
 void Update(){Restore();}
 void LateUpdate(){if(!health||!health.IsDead||!armor)return;
  for(int i=0;i<bones.Length;i++)skin[i]=bones[i].localToWorldMatrix*bind[i];
  foreach(int i in cape){localPose[i]=bones[i].localPosition;positions[i]=bones[i].position;skin[i]=bones[i].localToWorldMatrix*bind[i];lift[i]=0;}
  foreach(int j in samples){var w=weights[j];Vector3 p=skin[w.boneIndex0].MultiplyPoint3x4(vertices[j])*w.weight0;if(w.weight1>0)p+=skin[w.boneIndex1].MultiplyPoint3x4(vertices[j])*w.weight1;if(w.weight2>0)p+=skin[w.boneIndex2].MultiplyPoint3x4(vertices[j])*w.weight2;if(w.weight3>0)p+=skin[w.boneIndex3].MultiplyPoint3x4(vertices[j])*w.weight3;float correction=floor+.045f-p.y;if(correction<=0)continue;
   if(w.weight0>0)lift[w.boneIndex0]=Mathf.Max(lift[w.boneIndex0],correction);if(w.weight1>0)lift[w.boneIndex1]=Mathf.Max(lift[w.boneIndex1],correction);if(w.weight2>0)lift[w.boneIndex2]=Mathf.Max(lift[w.boneIndex2],correction);if(w.weight3>0)lift[w.boneIndex3]=Mathf.Max(lift[w.boneIndex3],correction);
  }
  foreach(int i in cape)bones[i].position=positions[i]+Vector3.up*lift[i];captured=true;
 }
 void Restore(){if(!captured)return;foreach(int i in cape)if(bones[i])bones[i].localPosition=localPose[i];captured=false;}
 void OnDisable(){Restore();}
}
