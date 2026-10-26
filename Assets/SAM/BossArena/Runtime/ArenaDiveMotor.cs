using System;
using UnityEngine;
namespace SamProduction.BossArena
{
 [DisallowMultipleComponent]
 public sealed class ArenaDiveMotor:MonoBehaviour
 {
  [Serializable] public sealed class Contract {public string id,source_sha256;public float chargeSeconds,lockTime,impactTime,landingTime,completeSeconds,radius,impactRadius,waveSeconds,damage;public Vector3 localImpactFoot;}
  public AnimationClip clip;public TextAsset motionData,contractData;public ArenaDivePresentation presentation;
  public Contract Settings{get;private set;} public bool Active{get;private set;} public bool Cancelled{get;private set;} public bool Locked{get;private set;} public bool Landed{get;private set;}
  public float SourceTime{get;private set;} public float WaveRadius{get;private set;} public int ReleaseCount{get;private set;} public Vector3 Centre{get;private set;} public float ContactMismatch{get;private set;}
  public bool Telegraph=>Active&&!Cancelled&&SourceTime<Settings.impactTime;public bool Recovering=>Active&&SourceTime>=Settings.impactTime;
  private ArenaBossBrain owner;private ArenaSession arena;private ArenaMotionClip motion;private double clock;private Vector3 origin,lockPosition,endPosition;private Quaternion heading;private bool released,blocked,death;private long strike;private float[] boundaries;
  public void Validate()
  {
   if(clip==null||motionData==null||contractData==null||presentation==null)throw new InvalidOperationException("Dive source assets missing");
   Settings=JsonUtility.FromJson<Contract>(contractData.text);motion=ArenaMotionData.Read(motionData).Get("dive_kick");
   boundaries=new[]{Settings.chargeSeconds,Settings.lockTime,Settings.impactTime,Settings.impactTime+Settings.waveSeconds,Settings.landingTime};
   if(Settings.id!=motion.id||Mathf.Abs(clip.length-motion.duration)>.002f||Settings.chargeSeconds!=2||Settings.lockTime<=2||Settings.impactTime<=Settings.lockTime||Settings.landingTime<=Settings.impactTime||Settings.completeSeconds!=motion.duration||Settings.radius<=0||Settings.impactRadius<=0||Settings.impactRadius>=Settings.radius)throw new InvalidOperationException("Dive timing/source mismatch");
  }
  public void Initialize(ArenaBossBrain brain){owner=brain;Validate();}
  public void ResetMotor(){Active=Cancelled=Locked=Landed=released=blocked=death=false;clock=0;SourceTime=WaveRadius=ContactMismatch=0;ReleaseCount=0;presentation?.Clear();}
  public bool TryBegin(ArenaSession session)
  {
   if(Active||!session.Active||owner.fighter.Dead||!owner.FoundationMotor.IsGuard)return false;
   if(!ArenaAerialClearance.TakeoffClear(owner,motion,Settings.chargeSeconds,Settings.lockTime,false))
   {session.Record("dive_takeoff_obstructed","dive_kick",owner.fighter,0,owner.transform.position);return false;}
   ResetMotor();arena=session;origin=owner.transform.position;heading=owner.transform.rotation;strike=arena.NewStrike();Active=true;
   owner.FoundationMotor.SuspendForAttack();owner.animationDriver.PoseContactClip(clip,0,0);Centre=Ground(session.player.transform.position);
   presentation.Begin(owner,Centre,Settings.radius);Record("dive_started");return true;
  }
  public void Cancel(bool lethal)
  {
   if(!Active)return;death|=lethal;if(!Cancelled){Cancelled=true;presentation.Clear();Record("dive_cancelled");}
   // Airborne cancellation retains the entire authored descent and recovery.
   // Before takeoff the already-grounded foundation owns the return instead.
   if(SourceTime<Settings.chargeSeconds){Active=false;owner.OnGroundSkillFinished(death);}
  }
  public void Tick(float delta,ArenaSession session)
  {
   arena=session;if(!Active)return;if(owner.fighter.Dead||!arena.Active)Cancel(owner.fighter.Dead);if(!Active)return;
   if(delta<0||delta>.25f||float.IsNaN(delta))throw new ArgumentOutOfRangeException(nameof(delta));
   double remaining=delta;
   while(remaining>1e-10&&Active)
   {
    double next=Math.Min(Settings.completeSeconds,clock+Math.Min(remaining,1d/120));
    foreach(float edge in boundaries)if(clock<edge&&next>edge)next=edge;
    double step=next-clock;if(step<=0)throw new InvalidOperationException("Dive source clock stalled");float dt=(float)step;clock=next;remaining-=step;SourceTime=(float)clock;
    if(!Locked&&SourceTime>Settings.chargeSeconds)
    {
     var direction=Vector3.ProjectOnPlane(arena.player.transform.position-owner.transform.position,Vector3.up);
     if(direction.sqrMagnitude>.01f)heading=Quaternion.RotateTowards(heading,Quaternion.LookRotation(direction),90*dt);
     owner.transform.rotation=heading;
    }
    motion.Sample(SourceTime,out var source,out _);Vector3 desired=origin+heading*source;
    if(Locked)
    {
     motion.Sample(Settings.lockTime,out var lockedSource,out _);motion.Sample(Settings.impactTime,out var hitSource,out _);
     float progress=Mathf.Clamp01((source.z-lockedSource.z)/(hitSource.z-lockedSource.z));
     desired=Vector3.Lerp(lockPosition,endPosition,progress);desired.y=origin.y+source.y;
    }
    if(blocked){desired.x=owner.transform.position.x;desired.z=owner.transform.position.z;}
    var displacement=desired-owner.transform.position;if(SourceTime<=Settings.chargeSeconds||SourceTime>=Settings.landingTime)displacement.y-=2*dt;
    var flags=owner.capsule.Move(displacement);
    if(!blocked&&Vector3.ProjectOnPlane(desired-owner.transform.position,Vector3.up).magnitude>.06f){blocked=true;Cancel(false);Record("dive_world_blocked");}
    if(!Active)break;
    owner.animationDriver.PoseContactClip(clip,clock,dt);
    if(!Locked&&SourceTime>=Settings.lockTime)
    {
     Locked=true;lockPosition=owner.transform.position;endPosition=Ground(arena.player.transform.position)-heading*new Vector3(Settings.localImpactFoot.x,0,Settings.localImpactFoot.z);
     var planar=Vector3.ProjectOnPlane(endPosition-lockPosition,Vector3.up);if(planar.magnitude>14)endPosition=lockPosition+planar.normalized*14;
     endPosition.y=origin.y;
     if(owner.ProductionAI)
     {
      if(owner.TryRouteLanding(endPosition,origin,heading,out var landing))endPosition=landing;
      else {endPosition=origin;Cancel(false);Record("dive_return_route_unavailable");}
     }
     Centre=Ground(endPosition+heading*Settings.localImpactFoot);presentation.SetCentre(Centre);Record("dive_target_locked");
    }
    else if(!Locked&&!Cancelled){Centre=Ground(arena.player.transform.position);presentation.SetCentre(Centre);}
    if(!released&&SourceTime>=Settings.impactTime)
    {
     released=true;var actual=Ground(owner.FootPosition);ContactMismatch=Vector3.Distance(actual,Centre);
     if(ContactMismatch>.03f){Cancel(false);Record("dive_contact_mismatch");}
     if(!Cancelled){ReleaseCount++;presentation.Release();Record("dive_flame_impact");}
    }
    if(released&&!Cancelled&&SourceTime<=Settings.impactTime+Settings.waveSeconds)
    {
     float previous=WaveRadius;WaveRadius=Mathf.Lerp(Settings.impactRadius,Settings.radius,Mathf.Clamp01((SourceTime-Settings.impactTime)/Settings.waveSeconds));
     arena.StrikeRing(owner.fighter,strike,"dive_kick",Centre,WaveRadius,Mathf.Max(0,previous-.75f),Settings.damage,5);
    }
    if(SourceTime>=Settings.landingTime&&!Landed&&(owner.capsule.isGrounded||(flags&CollisionFlags.Below)!=0)){Landed=true;presentation.Land();Record("dive_landed");}
    if(!Cancelled)presentation.Tick(dt,SourceTime,Settings,WaveRadius);
    if(clock>=Settings.completeSeconds-1e-10)
    {
     Active=false;presentation.Clear();Record(Landed?"dive_recovery_complete":"dive_landing_failed");owner.OnGroundSkillFinished(death);
    }
   }
  }
  private Vector3 Ground(Vector3 p){if(Physics.Raycast(p+Vector3.up*12,Vector3.down,out var h,30,1<<8,QueryTriggerInteraction.Ignore))return h.point;return new Vector3(p.x,origin.y,p.z);}
  private void Record(string kind)=>arena.Record(kind,"dive_kick",owner.fighter,strike,owner.transform.position);
 }
}
