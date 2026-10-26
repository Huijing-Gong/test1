using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace SamProduction.BossArena
{
 public sealed class ArenaDiveContracts:MonoBehaviour
 {
  [Serializable] private sealed class Row{public string name,error;public bool passed,landed,cancelled;public float time,health,mismatch;public int releases;public ArenaEventRecord[] events;}
  [Serializable] private sealed class Sample{public string name;public float time,health,radius,visibleRadius;public bool active,locked,landed,grounded,cancelled;public Vector3 actor,player,foot,leftFoot,centre;public Quaternion rotation;public ArenaFoundationSoleProbe.Sample sole;public ArenaDashSourceSoleReference.Sample reference;}
  [Serializable] private sealed class Report{public string scope="ACTUAL_HEADLESS_DIVE_CONTRACTS_NOT_VISUAL_NATIVE_INPUT_OR_PERFORMANCE",build;public bool passed;public Row[] cases;public Sample[] samples;}
  private ArenaSession arena;private ArenaDiveMotor motor;private new string name;private const float Step=1f/120;private readonly List<Row> rows=new List<Row>();private readonly List<Sample> samples=new List<Sample>();
  private ArenaFoundationSoleProbe soleProbe;private ArenaDashSourceSoleReference sourceReference;private ArenaMotionClip sourceMotion;private float nextSole;
  [Serializable] private sealed class Identity{public string source_sha256;public ClipIdentity[] clips;}
  [Serializable] private sealed class ClipIdentity{public string fbx_sha256;public FootRow[] feet;}
  [Serializable] private sealed class FootRow{public FootContact L,R;}
  [Serializable] private sealed class FootContact{public bool authored_contact;}
  private Identity identity;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]private static void Install()
  {
   var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--sam-dive-contracts")<0)return;
   if(args.Any(x=>x!="--sam-dive-contracts"&&(x.EndsWith("-contracts")||x.EndsWith("-review")||x=="--sam-benchmark"))){Application.Quit(2);return;}
   new GameObject("Dive source gameplay contracts").AddComponent<ArenaDiveContracts>();
  }
  private IEnumerator Start()
  {
   yield return null;string output=null;
   try
   {
    var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");Require(at>=0,"Output required");output=Path.Combine(Path.GetFullPath(args[at+1]),"dive_contracts.json");Require(!File.Exists(output),"Preserve report");Directory.CreateDirectory(Path.GetDirectoryName(output));
    arena=FindFirstObjectByType<ArenaSession>();arena.automaticTick=false;arena.view.automaticTick=false;motor=arena.boss.DiveMotor;Require(motor!=null,"Dive motor missing");
    if(ArenaDashSourceSoleReference.Requested){var mesh=arena.boss.GetComponentInChildren<SkinnedMeshRenderer>();soleProbe=new ArenaFoundationSoleProbe(mesh);sourceReference=new ArenaDashSourceSoleReference(arena.boss.visual,mesh);sourceMotion=ArenaMotionData.Read(motor.motionData).Get("dive_kick");identity=JsonUtility.FromJson<Identity>(ArenaMotionEncoding.Text(motor.motionData));}
    Run("stationary_hit_and_complete_landing",()=>{Fixture();Begin();Complete();Require(arena.player.fighter.Health==72&&motor.ReleaseCount==1&&!motor.Cancelled&&motor.Landed,"Expected one impact and full landing");});
    Run("late_actual_dodge",()=>{Fixture();Begin();AdvanceTo(3.4f);Tick(new ArenaCommand{dodge=true});Complete();Require(arena.player.fighter.Health==100&&arena.player.fighter.DodgeCount==1,"Late dodge failed");});
    Run("move_out_after_lock",()=>{Fixture();Begin();AdvanceTo(2.8f);int count=0;while(motor.Active&&count++<1000)Tick(new ArenaCommand{move=Vector2.right,run=true});Require(count<1000&&!motor.Active,"Source recovery stalled");Require(arena.player.fighter.Health==100,"Early movement did not escape");});
    Run("locked_target_remains_fixed",()=>{Fixture();Begin();AdvanceTo(2.8f);var point=motor.Centre;for(int i=0;i<60;i++)Tick(new ArenaCommand{move=Vector2.right,run=true});Require(Vector3.Distance(point,motor.Centre)<.0001f,"Locked centre moved");Complete();});
    Run("ground_cancel",()=>{Fixture();Begin();AdvanceTo(.8f);arena.boss.CancelAttack();for(int i=0;i<90;i++)Tick(default);Require(!motor.Active&&arena.player.fighter.Health==100&&!motor.presentation.WarningVisible,"Ground cancellation leaked skill");});
    Run("air_cancel_full_descent",()=>{Fixture();Begin();AdvanceTo(2.9f);arena.boss.CancelAttack();Complete();Require(motor.Cancelled&&motor.Landed&&arena.player.fighter.Health==100,"Air cancellation failed descent");});
    Run("air_death_then_restart",()=>{Fixture();Begin();AdvanceTo(2.9f);arena.boss.fighter.Receive(new ArenaHit{source=arena.player.fighter,strike=arena.NewStrike(),damage=100000,skill="test_lethal"});Complete();Require(motor.Landed&&arena.boss.fighter.Dead&&arena.player.fighter.Health==100,"Air death did not settle");arena.Restart(true);Require(!motor.Active&&!motor.Cancelled&&!motor.presentation.WarningVisible,"Restart retained state");});
    sourceReference?.Dispose();soleProbe?.Dispose();File.WriteAllText(output,JsonUtility.ToJson(new Report{build=Application.version,passed=rows.All(x=>x.passed),cases=rows.ToArray(),samples=samples.ToArray()},true));Application.Quit(rows.All(x=>x.passed)?0:2);
   }
   catch(Exception e){Debug.LogException(e);if(output!=null&&!File.Exists(output))File.WriteAllText(output,JsonUtility.ToJson(new Report{cases=new[]{new Row{name="setup",error=e.ToString()}}},true));Application.Quit(2);}
  }
  private void Run(string id,Action action){name=id;nextSole=0;var r=new Row{name=id};try{action();r.passed=true;}catch(Exception e){r.error=e.ToString();}r.time=motor.SourceTime;r.health=arena.player.fighter.Health;r.landed=motor.Landed;r.cancelled=motor.Cancelled;r.releases=motor.ReleaseCount;r.mismatch=motor.ContactMismatch;r.events=arena.Events.ToArray();rows.Add(r);}
  private void Fixture(){arena.boss.decisionEnabled=false;arena.boss.useDashSampleAI=false;arena.bossStart=new Vector3(0,.025f,0);arena.playerStart=new Vector3(0,.05f,6);arena.Restart(true);arena.boss.ResetBoss(arena.bossStart,Quaternion.identity);for(int i=0;i<15;i++)Tick(default);}
  private void Begin()=>Require(arena.boss.TrySkill(ArenaAttack.DiveKick),"Brain refused dive");
  private void AdvanceTo(float time){int count=0;while(motor.Active&&motor.SourceTime<time&&count++<1000)Tick(default);Require(count<1000,"Source clock stalled");}
  private void Complete(){int count=0;while(motor.Active&&count++<1000)Tick(default);Require(count<1000&&!motor.Active,"Source never completed");}
  private void Tick(ArenaCommand command)
  {
   arena.Simulate(command,Step);var sample=new Sample{name=name,time=motor.SourceTime,health=arena.player.fighter.Health,radius=motor.WaveRadius,visibleRadius=motor.presentation.VisibleRadius,active=motor.Active,locked=motor.Locked,landed=motor.Landed,cancelled=motor.Cancelled,grounded=arena.boss.capsule.isGrounded,actor=arena.boss.transform.position,rotation=arena.boss.transform.rotation,player=arena.player.transform.position,foot=arena.boss.FootPosition,leftFoot=arena.boss.Socket("socket_foot.L").position,centre=motor.Centre};
   if(soleProbe!=null&&motor.Active&&motor.SourceTime>=nextSole)
   {
    nextSole=motor.SourceTime+1f/60;sample.sole=soleProbe.Capture(rows.Count,motor.SourceTime);samples.Add(sample);
    Require(sample.sole.reliable,"Actual boot probe failed: "+sample.sole.reason);
    Require(sample.sole.leftMinimumY>-.005f&&sample.sole.rightMinimumY>-.005f,"Actual boot penetrated floor");
    bool left=Support(motor.SourceTime,true),right=Support(motor.SourceTime,false);
    if(left)Require(sample.sole.leftGroundFound&&Mathf.Abs(sample.sole.leftGroundClearanceM)<.005f,"Authored supporting left boot lost floor");
    if(right)Require(sample.sole.rightGroundFound&&Mathf.Abs(sample.sole.rightGroundClearanceM)<.005f,"Authored supporting right boot lost floor");
    bool leftPair=left&&Support(sample.sole.previousSourceTime,true),rightPair=right&&Support(sample.sole.previousSourceTime,false);
    if(sample.sole.hasPreviousSameClip&&(leftPair||rightPair))
    {
     sample.reference=sourceReference.CapturePair(motor.clip,sourceMotion,rows.Count,sample.sole.previousSourceTime,motor.SourceTime,identity.source_sha256,identity.clips[0].fbx_sha256);Require(sample.reference.reliable,"Independent imported source probe failed");
     if(leftPair)Require(Mathf.Abs(sample.sole.leftMedianPatchTravelM-sample.reference.left.medianPatchTravelM)<.001f,"Additional planted left boot slip");
     if(rightPair)Require(Mathf.Abs(sample.sole.rightMedianPatchTravelM-sample.reference.right.medianPatchTravelM)<.001f,"Additional planted right boot slip");
    }
   }
   else samples.Add(sample);
  }
  private bool Support(float time,bool left)
  {
   var rows=identity.clips[0].feet;if(rows==null||rows.Length!=sourceMotion.frame_count)throw new InvalidOperationException("Missing source foot contact metadata");
   float frame=Mathf.Clamp(time*60,0,rows.Length-1);int a=Mathf.FloorToInt(frame),b=Mathf.Min(a+1,rows.Length-1);
   bool first=left?rows[a].L.authored_contact:rows[a].R.authored_contact;bool second=left?rows[b].L.authored_contact:rows[b].R.authored_contact;
   return first&&(frame-a<.0001f||second);
  }
  private static void Require(bool value,string text){if(!value)throw new InvalidOperationException(text);}
 }
}
