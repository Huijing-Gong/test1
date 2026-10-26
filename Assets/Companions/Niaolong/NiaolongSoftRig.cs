using UnityEngine;
// Weighted cap and two three-joint arms; prop motion stays in front of the body.
public sealed class NiaolongSoftRig:MonoBehaviour {
 public NiaolongCompanion creature;
 public Transform[] capBones,leftBones,rightBones;
 Vector3[] capRest,leftRest,rightRest;Quaternion[] capRot;
 void Init(){if(capRest!=null)return;capRest=new Vector3[capBones.Length];capRot=new Quaternion[capBones.Length];for(int i=0;i<capBones.Length;i++){capRest[i]=capBones[i].localPosition;capRot[i]=capBones[i].localRotation;}leftRest=Rest(leftBones);rightRest=Rest(rightBones);}
 Vector3[] Rest(Transform[] bones){var p=new Vector3[bones.Length];for(int i=0;i<p.Length;i++)p[i]=bones[i].localPosition;return p;}
 public void Apply(float clock,float blend,float phase){Init();
 // The brim stays fitted; successive cap joints lag rather than detaching the pompom.
 creature.hat.localRotation=Quaternion.identity;
 for(int i=0;i<capBones.Length;i++){float amount=i==0?0:i==1?1.2f:3.2f;capBones[i].localPosition=capRest[i];capBones[i].localRotation=capRot[i]*Quaternion.Euler(amount*Mathf.Sin(clock*2.8f-i*.5f),amount*.45f*Mathf.Sin(clock*2.1f-i*.6f),amount*Mathf.Sin(clock*2.5f-i*.6f));}
 creature.pompom.localPosition=new Vector3(0,-.022f,0);creature.pompom.localRotation=Quaternion.Euler(Mathf.Sin(clock*3.1f)*7,0,Mathf.Sin(clock*2.8f-1)*6);
 Vector3 left=new Vector3(-.195f,.317f,.332f),right=new Vector3(.195f,.317f,.332f);
 Vector3 pillow=new Vector3(0,.305f,.325f);Quaternion rotation=Quaternion.identity;
 if(creature.Action==NiaolongCompanion.Gesture.OneHandHold){float e=blend;pillow+=new Vector3(-.035f,.018f,.025f)*e;left+=new Vector3(-.035f,.018f,.025f)*e;right=Vector3.Lerp(right,new Vector3(.345f,.365f,.22f),e);rotation=Quaternion.Euler(0,0,8*e);}
 if(creature.Action==NiaolongCompanion.Gesture.PillowToss){
 // Lift the prop clear of the face before release, then catch at exactly the grip pose.
 float launch=Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-.10f)/.20f));float recover=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-.78f)/.18f));float forward=launch*recover;
 float airborne=Mathf.Clamp01((phase-.30f)/.48f);float arc=Mathf.Sin(airborne*Mathf.PI);
 pillow+=new Vector3(0,.10f*forward+.70f*arc,.17f*forward);
 float reach=Mathf.Sin(Mathf.Clamp01(phase/.3f)*Mathf.PI*.5f)*recover;
 left+=new Vector3(-.025f,.10f*reach,.12f*reach);right+=new Vector3(.025f,.10f*reach,.12f*reach);
 float release=Mathf.Sin(airborne*Mathf.PI);left.x-=.055f*release;right.x+=.055f*release;
 rotation=Quaternion.Euler(0,0,12*arc*Mathf.Sin(airborne*Mathf.PI*2));
 }
 creature.hug.localPosition=pillow;creature.hug.localRotation=rotation;
 Arm(leftBones,leftRest,left);Arm(rightBones,rightRest,right);
 }
 void Arm(Transform[] bones,Vector3[] rest,Vector3 target){for(int i=0;i<bones.Length;i++){bones[i].localPosition=rest[i];bones[i].localRotation=Quaternion.identity;}var parent=creature.body;
 Vector3 shoulder=parent.InverseTransformPoint(bones[0].position);float side=Mathf.Sign(shoulder.x);
 // Elbow remains outside the belly; palms stay ahead of its surface.
 Vector3 elbow=Vector3.Lerp(shoulder,target,.53f)+new Vector3(side*.018f,-.006f,0);
 bones[1].position=parent.TransformPoint(elbow);bones[2].position=parent.TransformPoint(target);
 }
}
