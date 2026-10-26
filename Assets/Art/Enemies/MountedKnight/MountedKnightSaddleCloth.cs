using System.Linq;
using UnityEngine;
// Small, independent cloth waves layered over the authored horse animation. Upper attachment stays pinned.
public sealed class MountedKnightSaddleCloth : MonoBehaviour {
 SkinnedMeshRenderer skin;MountedKnightBrain brain;Health health;int[] indices;float phase,amplitude;
 void Start(){brain=GetComponent<MountedKnightBrain>();health=GetComponent<Health>();skin=GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(x=>x.name=="Warden_SoftSaddleBlanket");if(!skin){enabled=false;return;}indices=new[]{"SaddleSoft_Left","SaddleSoft_Right","SaddleRipple_Left","SaddleRipple_Right"}.Select(skin.sharedMesh.GetBlendShapeIndex).ToArray();}
 void LateUpdate(){if(!skin||indices==null)return;bool moving=brain.motion.ActiveClip==1||brain.motion.ActiveClip==2||brain.State==MountedKnightBrain.Mode.Charge;float target=health.IsDead?0:moving?52:26;amplitude=Mathf.Lerp(amplitude,target,1-Mathf.Exp(-Time.deltaTime*5));phase+=Time.deltaTime*(moving?5.2f:1.9f);for(int i=0;i<indices.Length;i++)if(indices[i]>=0)skin.SetBlendShapeWeight(indices[i],Mathf.Sin(phase*(i<2?1:1.37f)+(i%2)*1.1f+(i>=2?1.7f:0))*amplitude*(i<2?1:.65f));}
 void OnDisable(){if(skin&&indices!=null)foreach(int i in indices)if(i>=0)skin.SetBlendShapeWeight(i,0);}
}
