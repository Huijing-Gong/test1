using System.Linq;
using UnityEngine;

// Add a short, scaled-time impact ripple without replacing the authored attack/death poses.
[DefaultExecutionOrder(-120)]
public sealed class MountedKnightTailReaction : MonoBehaviour {
 Transform[] tail;Quaternion[] pose;bool captured;Health health;float age=10,side=1;
 public int Reactions {get;private set;}public float MaximumOffset {get;private set;}
 void Awake(){health=GetComponent<Health>();tail=GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("BN_Tail_")).OrderBy(t=>t.name).ToArray();pose=new Quaternion[tail.Length];}
 void OnEnable(){health.Damaged+=Hit;health.Died+=Died;}
 void Hit(Health h,DamageInfo d){if(h.IsDead)return;age=0;side=Vector3.Dot(transform.right,d.HitDirection)>=0?1:-1;Reactions++;}
 void Died(Health h,DamageInfo d){age=10;Restore();}
 void Update(){Restore();age+=Time.deltaTime;if(health.IsDead)age=10;}
 void LateUpdate(){
  if(age>.85f||health.IsDead)return;
  for(int i=0;i<tail.Length;i++){
   var bone=tail[i];pose[i]=bone.localRotation;float t=age-i*.045f;
   if(t<0)continue;
   var next=i+1<tail.Length?tail[i+1]:null;Vector3 along=next?next.localPosition.normalized:Vector3.up;
   Vector3 axis=Vector3.Cross(along,bone.InverseTransformDirection(transform.right)).normalized;if(axis.sqrMagnitude<.1f)axis=Vector3.forward;
   float angle=side*(3.5f+i*1.3f)*Mathf.Sin(t*15)*Mathf.Exp(-t*5);MaximumOffset=Mathf.Max(MaximumOffset,Mathf.Abs(angle));bone.localRotation=pose[i]*Quaternion.AngleAxis(angle,axis);
  }
  captured=true;
 }
 void Restore(){if(!captured)return;for(int i=0;i<tail.Length;i++)if(tail[i])tail[i].localRotation=pose[i];captured=false;}
 void OnDisable(){Restore();if(health){health.Damaged-=Hit;health.Died-=Died;}}
}
