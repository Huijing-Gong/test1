using System;
using UnityEngine;
namespace SamProduction.BossArena
{
 [DisallowMultipleComponent]
 public sealed class ArenaMeteorMotor:MonoBehaviour
 {
  [Serializable] public sealed class Contract { public string id,source_sha256;public float chargeSeconds,mainTime,companionsTime,lockTime,restoreTime,punchTime,impactTime,landingTime,completeSeconds,radius,impactRadius,waveSeconds,damage; }
  public AnimationClip clip;public TextAsset motionData,contractData;public ArenaMeteorPresentation presentation;
  public Contract Settings{get;private set;} public bool Active{get;private set;} public bool Cancelled{get;private set;} public bool Locked{get;private set;} public bool Landed{get;private set;} public bool ControlRestored{get;private set;}
  public float SourceTime{get;private set;} public float WaveRadius{get;private set;} public int ReleaseCount{get;private set;} public Vector3 Centre{get;private set;}
  public bool Telegraph=>Active&&!Cancelled&&SourceTime<Settings.impactTime;public bool Recovering=>Active&&SourceTime>=Settings.impactTime;
  private ArenaBossBrain owner;private ArenaSession arena;private ArenaMotionClip motion;private double clock;private Vector3 origin,lockPosition,endPosition,originalCapsuleCentre,cameraStart,focusStart;private Quaternion heading;private bool released,blocked,death;private long strike;private float[] boundaries;private float fovStart;
  public void Validate()
  {
   if(clip==null||motionData==null||contractData==null||presentation==null)throw new InvalidOperationException("Meteor assets missing");
   Settings=JsonUtility.FromJson<Contract>(contractData.text);motion=ArenaMotionData.Read(motionData).Get("meteor_finale");
   boundaries=new[]{Settings.chargeSeconds,Settings.mainTime,Settings.companionsTime,Settings.lockTime,Settings.restoreTime,Settings.punchTime,Settings.impactTime,Settings.impactTime+Settings.waveSeconds,Settings.landingTime};
   if(Settings.id!=motion.id||Mathf.Abs(clip.length-motion.duration)>.002f||Settings.chargeSeconds<3||Settings.chargeSeconds>4||Settings.restoreTime!=Settings.lockTime||Settings.impactTime-Settings.restoreTime<1||Settings.landingTime<=Settings.impactTime||Mathf.Abs(Settings.completeSeconds-motion.duration)>.001f||Settings.radius<=Settings.impactRadius||Settings.impactRadius<=0)throw new InvalidOperationException("Meteor timing/source mismatch");
  }
  public void Initialize(ArenaBossBrain brain){owner=brain;Validate();originalCapsuleCentre=owner.capsule.center;}
  public void ResetMotor()
  {
   if(arena!=null)arena.view.EndCinematic(this);if(owner!=null)owner.capsule.center=originalCapsuleCentre;
   Active=Cancelled=Locked=Landed=ControlRestored=released=blocked=death=false;clock=0;SourceTime=WaveRadius=0;ReleaseCount=0;presentation?.Clear();
  }
  public bool TryBegin(ArenaSession session)
  {
   if(Active||!session.Active||owner.fighter.Dead||!owner.FoundationMotor.IsGuard)return false;
   if(!ArenaAerialClearance.TakeoffClear(owner,motion,Settings.chargeSeconds,Settings.lockTime,true))
   {session.Record("meteor_takeoff_obstructed","meteor_finale",owner.fighter,0,owner.transform.position);return false;}
   ResetMotor();arena=session;origin=owner.transform.position;heading=owner.transform.rotation;strike=arena.NewStrike();Active=true;
   cameraStart=arena.view.transform.position;focusStart=cameraStart+arena.view.transform.forward*5;fovStart=arena.view.GetComponent<Camera>().fieldOfView;
   arena.view.BeginCinematic(this);owner.FoundationMotor.SuspendForAttack();owner.animationDriver.PoseContactClip(clip,0,0);Centre=Ground(session.player.transform.position);
   presentation.Begin(owner,Centre,Settings.radius);Record("meteor_started");Record("player_control_taken");return true;
  }
  private void RestoreControl(){if(ControlRestored)return;arena.view.EndCinematic(this);ControlRestored=true;Record("player_control_restored");}
  public void Cancel(bool lethal)
  {
   if(!Active)return;death|=lethal;
   if(!Cancelled){Cancelled=true;presentation.Clear();RestoreControl();Record("meteor_cancelled");}
   if(SourceTime<Settings.chargeSeconds){Active=false;owner.capsule.center=originalCapsuleCentre;owner.OnGroundSkillFinished(death);}
  }
  public void Tick(float delta,ArenaSession session)
  {
   arena=session;if(!Active)return;if(owner.fighter.Dead||!arena.Active)Cancel(owner.fighter.Dead);if(!Active)return;
   if(delta<0||delta>.25f||float.IsNaN(delta))throw new ArgumentOutOfRangeException(nameof(delta));
   double remaining=delta;
   while(remaining>1e-10&&Active)
   {
    double next=Math.Min(Settings.completeSeconds,clock+Math.Min(remaining,1d/120));foreach(float edge in boundaries)if(clock<edge&&next>edge)next=edge;
    double step=next-clock;if(step<=0)throw new InvalidOperationException("Meteor source clock stalled");float dt=(float)step;clock=next;remaining-=step;SourceTime=(float)clock;
    motion.Sample(SourceTime,out var source,out _);Vector3 desired=origin+heading*source;
    if(Locked)
    {
     motion.Sample(Settings.lockTime,out var lockedSource,out _);motion.Sample(Settings.landingTime,out var landSource,out _);
     float progress=Mathf.SmoothStep(0,1,Mathf.InverseLerp(Settings.lockTime,Settings.landingTime,SourceTime));
     // Retarget the displacement smoothly, preserving all authored vertical pivot compensation.
     var offset=endPosition-(origin+heading*landSource);desired+=Vector3.ProjectOnPlane(offset,Vector3.up)*progress;
    }
    if(blocked){desired.x=owner.transform.position.x;desired.z=owner.transform.position.z;}
    // The imported whole-body inversion rotates the original capsule centre too.
    // Leaving it above the actor root would put collision several metres above the visible body.
    owner.capsule.center=Quaternion.AngleAxis(Pitch(SourceTime),Vector3.right)*originalCapsuleCentre;
    var displacement=desired-owner.transform.position;if(SourceTime<=Settings.chargeSeconds||SourceTime>=Settings.landingTime)displacement.y-=2*dt;
    var flags=owner.capsule.Move(displacement);
    if(!blocked&&Vector3.ProjectOnPlane(desired-owner.transform.position,Vector3.up).magnitude>.06f){blocked=true;Cancel(false);Record("meteor_world_blocked");}
    if(!Active)break;owner.animationDriver.PoseContactClip(clip,clock,dt);
    if(!Locked&&SourceTime>=Settings.lockTime)
    {
     Locked=true;lockPosition=owner.transform.position;Centre=Ground(arena.player.transform.position);endPosition=Centre-heading*new Vector3(0,0,2);endPosition.y=origin.y;
     if(owner.ProductionAI)
     {
      if(owner.TryRouteLanding(endPosition,origin,heading,out var landing))endPosition=landing;
      else {endPosition=origin;Cancel(false);Record("meteor_return_route_unavailable");}
     }
     presentation.SetCentre(Centre);Record("meteor_target_locked");
    }
    else if(!Locked&&!Cancelled){Centre=Ground(arena.player.transform.position);presentation.SetCentre(Centre);}
    if(!ControlRestored&&SourceTime>=Settings.restoreTime)RestoreControl();
    if(!ControlRestored)PoseCamera();
    if(!released&&SourceTime>=Settings.impactTime){released=true;if(!Cancelled){ReleaseCount++;presentation.Release();Record("meteor_ground_impact");}}
    if(released&&!Cancelled&&SourceTime<=Settings.impactTime+Settings.waveSeconds)
    {
     float previous=WaveRadius;WaveRadius=Mathf.Lerp(Settings.impactRadius,Settings.radius,Mathf.Clamp01((SourceTime-Settings.impactTime)/Settings.waveSeconds));
     arena.StrikeRing(owner.fighter,strike,"meteor_finale",Centre,WaveRadius,Mathf.Max(0,previous-.75f),Settings.damage,7);
    }
    if(SourceTime>=Settings.landingTime&&!Landed&&(owner.capsule.isGrounded||(flags&CollisionFlags.Below)!=0)){Landed=true;Record("meteor_landed");}
    if(!Cancelled)presentation.Tick(dt,SourceTime,Settings,WaveRadius);
    if(clock>=Settings.completeSeconds-1e-10){Active=false;owner.capsule.center=originalCapsuleCentre;presentation.Clear();RestoreControl();Record(Landed?"meteor_recovery_complete":"meteor_landing_failed");if(Landed&&!Cancelled)Record("meteor_vfx_finished");owner.OnGroundSkillFinished(death);}
   }
  }
  public static float Pitch(float t)=>t<3.7f?0:t<4.48f?180*Mathf.SmoothStep(0,1,Mathf.InverseLerp(3.7f,4.48f,t)):t<6.48f?180:t<7.18f?180+180*Mathf.SmoothStep(0,1,Mathf.InverseLerp(6.48f,7.18f,t)):360;
  private void PoseCamera()
  {
   float enter=Mathf.SmoothStep(0,1,Mathf.Clamp01(SourceTime/.5f));float wide=Mathf.SmoothStep(0,1,Mathf.InverseLerp(3.3f,4.7f,SourceTime));
   // Keep the inverted body large enough to read during the owned shot. The
   // shared camera still returns to player/ground danger before restoreTime.
   var chest=owner.Socket("chest").position;var focus=Vector3.Lerp(chest,Vector3.Lerp(chest,Centre,.20f),wide);
   var position=focus+heading*Vector3.Lerp(new Vector3(-3,1,5.8f),new Vector3(-7,2.5f,9),wide);
   arena.view.PoseCinematic(this,Vector3.Lerp(focusStart,focus,enter),Vector3.Lerp(cameraStart,position,enter),Mathf.Lerp(fovStart,48,enter));
  }
  private Vector3 Ground(Vector3 p){if(Physics.Raycast(p+Vector3.up*12,Vector3.down,out var h,30,1<<8,QueryTriggerInteraction.Ignore))return h.point;return new Vector3(p.x,origin.y,p.z);}
  private void Record(string kind)=>arena.Record(kind,"meteor_finale",owner.fighter,strike,owner.transform.position);
 }
}
