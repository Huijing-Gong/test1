using UnityEngine;
using UnityEngine.InputSystem;
// Feather-wing creature. Its motion is authored here, independently of the humanoid Paimon rig.
public sealed class AmberwingCompanion:MonoBehaviour
{
 public enum Gesture { Hover,HeadTilt,FeatherPreen,HappyHop,BarrelRoll,SleepCurl,PetalLoop,PeekAtHero,Surprise,Concern }
 public Transform owner,body,leftWing,rightWing,leftArm,rightArm,leftFoot,rightFoot,leftEye,rightEye,mouth;
 public Transform[] leftFeathers,rightFeathers;
 public TrailRenderer trail; public ParticleSystem motes;
 public bool Equipped {get;private set;}
 public Gesture Action {get;private set;}
 public int ActionsStarted {get;private set;}
 public int FlightFrames {get;private set;}
 public float Visibility {get;private set;}
 public bool Moving {get;private set;}
 public bool Preview;public float PreviewClock; public bool PreviewMoving;
 float time,actionTime,wait=.8f,speed,flight,blinkAt=2,blinkTime=10,orbitTime=-1,roll,alertAt=-20,hurtAt=-20;
 int previousAction=-1; Vector3 previous,velocity,offset; Quaternion rotation; Health health;
 Quaternion lwing,rwing,larm,rarm,lfoot,rfoot;Vector3 leftEyeScale,rightEyeScale;
 public static readonly Gesture[] Ambient={Gesture.HeadTilt,Gesture.FeatherPreen,Gesture.HappyHop,Gesture.BarrelRoll,Gesture.SleepCurl,Gesture.PetalLoop,Gesture.PeekAtHero};
 public string DisplayName=>Action.ToString();
 void Awake(){CaptureRest();}
 public void CaptureRest(){lwing=leftWing.localRotation;rwing=rightWing.localRotation;larm=leftArm.localRotation;rarm=rightArm.localRotation;lfoot=leftFoot.localRotation;rfoot=rightFoot.localRotation;leftEyeScale=leftEye.localScale;rightEyeScale=rightEye.localScale;rotation=transform.rotation;}
 public void Bind(Transform target){owner=target;previous=owner.position;transform.position=owner.position+owner.right*.7f+Vector3.up*1.5f;health=owner.GetComponent<Health>();if(health)health.Damaged+=Hurt;}
 void OnDestroy(){if(health)health.Damaged-=Hurt;}
 void Hurt(Health h,DamageInfo info){if(Equipped&&Time.unscaledTime-hurtAt>6){hurtAt=Time.unscaledTime;Play(Gesture.Concern);}}
 public void SetEquipped(bool value){Equipped=value;if(value&&owner){previous=owner.position;transform.position=owner.position+owner.right*.72f+Vector3.up*1.5f;velocity=Vector3.zero;wait=.7f;Action=Gesture.Hover;actionTime=0;}if(!value&&trail)trail.emitting=false;}
 public void Play(Gesture action){Action=action;actionTime=0;ActionsStarted++;}
 public void Circle(){if(Equipped&&Visibility>.95f)orbitTime=0;}
 public static float Duration(Gesture a)=>a==Gesture.SleepCurl?5.8f:a==Gesture.BarrelRoll?5.2f:4.6f;
 static float Ease(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
 void Update(){if(Preview||!Equipped||Visibility<.95f||CompanionWardrobe.IsBagOpen)return;if(Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame&&Camera.main){var ray=Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());foreach(var r in GetComponentsInChildren<Renderer>())if(r.bounds.IntersectRay(ray)){Circle();break;}}}
 void LateUpdate(){if(Preview){Evaluate(PreviewClock,.016f,PreviewMoving);return;}if(!owner){Destroy(gameObject);return;}float dt=Time.deltaTime;if(dt<=0)return;time+=dt;Visibility=Mathf.MoveTowards(Visibility,Equipped?1:0,dt*3);body.gameObject.SetActive(Visibility>.001f);if(Visibility<=0)return;
  var v=(owner.position-previous)/Mathf.Max(dt,.001f);previous=owner.position;float s=Vector3.ProjectOnPlane(v,Vector3.up).magnitude;if(s>25)s=0;speed=Mathf.Lerp(speed,s,1-Mathf.Exp(-dt*6));Moving=speed>.2f;
  if(Action!=Gesture.Hover){actionTime+=dt;if(actionTime>Duration(Action)){Action=Gesture.Hover;actionTime=0;wait=Random.Range(.9f,1.6f);}}else if(!CompanionWardrobe.IsBagOpen){wait-=dt;if(wait<=0){int i=Random.Range(0,Ambient.Length-1);if(i>=previousAction)i++;i%=Ambient.Length;previousAction=i;Play(Ambient[i]);}}
  if(Equipped&&time>alertAt+1){alertAt=time;foreach(var enemy in FindObjectsByType<MountedKnightBrain>(FindObjectsSortMode.None))if(enemy.enabled&&Vector3.Distance(owner.position,enemy.transform.position)<enemy.attackRange&&Action!=Gesture.Concern){Play(Gesture.Surprise);alertAt=time+8;break;}}
  var local=new Vector3(.74f,1.48f,-.12f);float p=actionTime/Duration(Action),e=Ease(actionTime/.8f)*Ease((Duration(Action)-actionTime)/.9f);
  if(Action==Gesture.PetalLoop)local+=new Vector3(.36f*Mathf.Sin(p*Mathf.PI*2),.28f*Mathf.Sin(p*Mathf.PI),.25f*Mathf.Sin(p*Mathf.PI*4))*e;
  if(Action==Gesture.PeekAtHero)local+=new Vector3(-.14f,.08f,.36f)*Mathf.Sin(p*Mathf.PI)*e;
  if(Action==Gesture.HappyHop)local.y+=.19f*Mathf.Pow(Mathf.Sin(p*Mathf.PI*3),2)*e;
  if(Action==Gesture.SleepCurl)local.y-=.13f*e;
  if(orbitTime>=0){orbitTime+=dt;float q=Mathf.Clamp01(orbitTime/5.4f);float a=Mathf.PI*Mathf.Sin(q*Mathf.PI);local=new Vector3(.76f*Mathf.Cos(a),1.5f+.12f*Mathf.Sin(q*Mathf.PI*2),.76f*Mathf.Sin(a));if(q>=1)orbitTime=-1;}
  var goal=owner.position+owner.right*local.x+Vector3.up*local.y+owner.forward*local.z;if(Vector3.Distance(goal,transform.position)>8){transform.position=goal;velocity=Vector3.zero;}transform.position=Vector3.SmoothDamp(transform.position,goal,ref velocity,.2f);
  var direction=Moving?Vector3.Lerp(owner.forward,Vector3.ProjectOnPlane(v,Vector3.up).normalized,.6f):owner.forward;if(Action==Gesture.PeekAtHero)direction=Vector3.Lerp(direction,(owner.position-transform.position).normalized,e*.7f);if(direction.sqrMagnitude>.01f)rotation=Quaternion.Slerp(rotation,Quaternion.LookRotation(Vector3.ProjectOnPlane(direction,Vector3.up)),1-Mathf.Exp(-dt*5));transform.rotation=rotation;Evaluate(time,dt,Moving);
 }
 public void Evaluate(float clock,float dt,bool moving){flight=Mathf.Lerp(flight,moving?1:0,1-Mathf.Exp(-dt*5));if(moving)FlightFrames++;float e=Ease(actionTime/.8f)*Ease((Duration(Action)-actionTime)/.9f),p=Mathf.Clamp01(actionTime/Duration(Action));
  float wingBeat=Mathf.Sin(clock*Mathf.Lerp(9,16,flight)),tilt=0,bodyRoll=0,squash=0,tuck=0;float a=wingBeat*Mathf.Lerp(16,34,flight),open=0;
  switch(Action){case Gesture.HeadTilt:tilt=16*Mathf.Sin(p*Mathf.PI*2)*e;break;case Gesture.FeatherPreen:tilt=18*e;open=-18*e;break;case Gesture.HappyHop:squash=.065f*Mathf.Sin(p*Mathf.PI*6)*e;a+=11*Mathf.Sin(p*Mathf.PI*6)*e;break;case Gesture.BarrelRoll:bodyRoll=360*Ease((p-.16f)/.68f);open=-14*e;break;case Gesture.SleepCurl:tuck=1*e;open=-30*e;tilt=12*e;a*=1-.8f*e;break;case Gesture.PetalLoop:tilt=16*Mathf.Sin(p*Mathf.PI*2)*e;break;case Gesture.PeekAtHero:tilt=-12*e;break;case Gesture.Surprise:squash=.07f*e;open=18*e;a+=8*e;break;case Gesture.Concern:tuck=.45f*e;tilt=-9*e;open=-10*e;break;}
  float goalRoll=bodyRoll+tilt+Mathf.Sin(clock*1.8f)*2;roll=Mathf.LerpAngle(roll,goalRoll,1-Mathf.Exp(-dt*8));body.localPosition=Vector3.up*(Mathf.Sin(clock*2.6f)*.035f);body.localRotation=Quaternion.Euler(flight*17+Mathf.Sin(clock*2)*2,0,roll);float vis=Preview?1:Ease(Visibility);body.localScale=new Vector3(1-squash*.55f,1+squash,1-squash*.55f)*vis;
  leftWing.localRotation=Quaternion.Slerp(leftWing.localRotation,lwing*Quaternion.Euler(6*flight,open+a,-8*flight-a*.32f),1-Mathf.Exp(-dt*16));rightWing.localRotation=Quaternion.Slerp(rightWing.localRotation,rwing*Quaternion.Euler(6*flight,-open-a,8*flight+a*.32f),1-Mathf.Exp(-dt*16));
  for(int i=0;i<leftFeathers.Length;i++){float lag=Mathf.Sin(clock*Mathf.Lerp(9,16,flight)-i*.32f)*(3+flight*5);leftFeathers[i].localRotation=Quaternion.Euler(0,lag,0);rightFeathers[i].localRotation=Quaternion.Euler(0,-lag,0);}
  leftArm.localRotation=Quaternion.Slerp(leftArm.localRotation,larm*Quaternion.Euler(-flight*22-tuck*35,0,8*Mathf.Sin(clock*2)-tuck*22),1-Mathf.Exp(-dt*7));rightArm.localRotation=Quaternion.Slerp(rightArm.localRotation,rarm*Quaternion.Euler(-flight*22-tuck*35,0,-8*Mathf.Sin(clock*2)+tuck*22),1-Mathf.Exp(-dt*7));
  leftFoot.localRotation=lfoot*Quaternion.Euler(flight*32+tuck*40+Mathf.Sin(clock*3)*6,0,-tuck*12);rightFoot.localRotation=rfoot*Quaternion.Euler(flight*30+tuck*40-Mathf.Sin(clock*3)*6,0,tuck*12);
  if(clock>blinkAt){blinkAt=clock+Random.Range(2.2f,4.6f);blinkTime=clock;}float blink=1-Mathf.Sin(Mathf.Clamp01((clock-blinkTime)/.19f)*Mathf.PI);if(Action==Gesture.SleepCurl)blink=Mathf.Lerp(blink,.12f,e);leftEye.localScale=new Vector3(leftEyeScale.x,leftEyeScale.y*Mathf.Max(.07f,blink),leftEyeScale.z);rightEye.localScale=new Vector3(rightEyeScale.x,rightEyeScale.y*Mathf.Max(.07f,blink),rightEyeScale.z);
  if(trail)trail.emitting=Equipped&&(Action==Gesture.PetalLoop||Action==Gesture.BarrelRoll||orbitTime>=0);if(motes){var emission=motes.emission;emission.rateOverTime=Equipped?((Action==Gesture.HappyHop||Action==Gesture.PetalLoop)?18:3):0;}
 }
 public void Sample(Gesture action,float seconds,bool moving){Action=action;actionTime=seconds;Preview=true;PreviewClock=seconds;PreviewMoving=moving;Evaluate(seconds,1,moving);}
}
