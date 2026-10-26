using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SamProduction.BossArena
{
    public sealed class ArenaStompContracts : MonoBehaviour
    {
        [Serializable] private sealed class Row
        { public string name,error; public bool passed;public float health,contactMismatch,maxRadius,sourceEnd;public int hits,dodges,releases;public ArenaEventRecord[] events; }
        [Serializable] private sealed class Sample
        { public string name,clip;public float time,radius,visibleRadius,health;public Vector3 actor,leftFoot,rightFoot,centre;public bool active,cancelled,grounded,warning,invulnerable;public ArenaFoundationSoleProbe.Sample sole;public ArenaDashSourceSoleReference.Sample reference; }
        [Serializable] private sealed class Report
        { public string scope="ACTUAL_HEADLESS_PHYSICS_NOT_VISUAL_AUDIO_NATIVE_INPUT_OR_PERFORMANCE",build;public bool passed;public Row[] cases;public Sample[] samples; }
        private ArenaSession arena;private ArenaStompMotor motor;private string caseName;
        private readonly List<Row> rows=new List<Row>();private readonly List<Sample> samples=new List<Sample>();
        private float maxRadius;
        private ArenaFoundationSoleProbe soleProbe;
        private ArenaDashSourceSoleReference sourceReference;
        private ArenaMotionClip sourceMotion;
        private float nextSoleTime;
        [Serializable] private sealed class Identity { public string source_sha256;public ClipIdentity[] clips; }
        [Serializable] private sealed class ClipIdentity { public string fbx_sha256; }
        private Identity identity;
        private const float Step=1f/120;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--sam-stomp-contracts")<0)return;
            foreach(string flag in new[]{"--sam-dash-contracts","--sam-benchmark","--sam-foundation-contracts","--sam-dash-review"})
                if(Array.IndexOf(args,flag)>=0){Debug.LogError("Multiple simulation owners");Application.Quit(2);return;}
            new GameObject("Stomp actual physics checks").AddComponent<ArenaStompContracts>();
        }
        private IEnumerator Start()
        {
            yield return null;
            string output=null;bool saved=false;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");Require(at>=0&&at+1<args.Length,"Output required");
                output=Path.Combine(Path.GetFullPath(args[at+1]),"stomp_contracts.json");Require(!File.Exists(output),"Preserve previous evidence");Directory.CreateDirectory(Path.GetDirectoryName(output));
                arena=FindFirstObjectByType<ArenaSession>();Require(arena!=null,"Arena missing");arena.automaticTick=false;arena.view.automaticTick=false;arena.player.view=null;
                if(arena.sound!=null)arena.sound.enabled=false;arena.sound=null;
                motor=arena.boss.StompMotor;Require(motor!=null,"Imported stomp motor missing");
                if(ArenaDashSourceSoleReference.Requested)
                {
                    var renderer=arena.boss.GetComponentInChildren<SkinnedMeshRenderer>();soleProbe=new ArenaFoundationSoleProbe(renderer);
                    sourceReference=new ArenaDashSourceSoleReference(arena.boss.visual,renderer);sourceMotion=ArenaMotionData.Read(motor.motionData).Get("furnace_stomp");
                    identity=JsonUtility.FromJson<Identity>(ArenaMotionEncoding.Text(motor.motionData));
                }
                Run("stationary_near_one_hit",()=>{Fixture(5);Begin();Advance(2.24f);Require(arena.player.fighter.HitCount==0,"Early damage");Finish();Require(arena.player.fighter.HitCount==1,"Expected one actual wave hit");Require(motor.ReleaseCount==1,"One release required");});
                Run("outer_edge_capsule_overlap",()=>{Fixture(9.9f);Begin();Finish();Require(arena.player.fighter.HitCount==1,"Wave failed to reach capsule overlapping 9.75m edge");});
                Run("irregular_wall_clock_tails",()=>{Fixture(5);Begin();float[] deltas={.01671943f,.00000017f,.02073815f,.007193f,.00000011f};for(int i=0;i<1800&&motor.Active;i++)Tick(default,deltas[i%deltas.Length]);Require(!motor.Active&&motor.SourceTime==4&&arena.player.fighter.HitCount==1,"Irregular source clock did not complete with one hit");});
                Run("outside_declared_radius",()=>{Fixture(10.5f);Begin();Finish();Require(arena.player.fighter.HitCount==0,"Damage escaped declared radius");});
                Run("late_actual_dodge",()=>{Require(rows[0].passed,"Vulnerable control must pass first");Fixture(5);Begin();Advance(2.72f);Tick(new ArenaCommand{dodge=true,move=Vector2.right});Finish();Require(arena.player.fighter.HitCount==0&&arena.player.fighter.DodgeCount==1,"Actual dodge did not resolve the same wave once");});
                Run("cancel_charge_complete_recovery",()=>{Fixture(5);Begin();Advance(.8f);arena.boss.CancelAttack();Require(motor.Active,"Ordinary cancel truncated source");Finish();Require(motor.Cancelled&&motor.SourceTime==4&&arena.player.fighter.HitCount==0&&motor.ReleaseCount==0,"Cancel lifecycle failed");});
                Run("death_while_leg_raised",()=>{Fixture(5);Begin();Advance(1.8f);arena.boss.fighter.Receive(new ArenaHit{source=arena.player.fighter,strike=arena.NewStrike(),damage=100000,skill="test_death",direction=Vector3.forward});Advance(4);Require(!motor.Active&&arena.boss.FoundationMotor.DeathStarted&&arena.boss.capsule.isGrounded,"Grounded death handoff failed");Require(arena.player.fighter.HitCount==0,"Dead boss dealt wave damage");});
                var report=new Report{build=Application.version,passed=rows.All(r=>r.passed),cases=rows.ToArray(),samples=samples.ToArray()};
                sourceReference?.Dispose();soleProbe?.Dispose();
                File.WriteAllText(output,JsonUtility.ToJson(report,true));saved=true;Application.Quit(report.passed?0:2);
            }
            catch(Exception e)
            { Debug.LogException(e);if(output!=null&&!saved&&!File.Exists(output))File.WriteAllText(output,JsonUtility.ToJson(new Report{build=Application.version,passed=false,cases=new[]{new Row{name="setup",error=e.ToString()}},samples=samples.ToArray()},true));Application.Quit(2); }
        }
        private void Run(string name,Action action)
        {
            caseName=name;maxRadius=0;nextSoleTime=0;var row=new Row{name=name};
            try{action();row.passed=true;}catch(Exception e){row.error=e.ToString();}
            row.health=arena.player.fighter.Health;row.hits=arena.player.fighter.HitCount;row.dodges=arena.player.fighter.DodgeCount;
            row.contactMismatch=motor.ContactMismatch;row.releases=motor.ReleaseCount;row.sourceEnd=motor.SourceTime;row.maxRadius=maxRadius;row.events=arena.Events.ToArray();rows.Add(row);
        }
        private void Fixture(float distance)
        {
            arena.boss.decisionEnabled=false;arena.boss.useDashSampleAI=false;arena.bossStart=new Vector3(0,.025f,0);
            arena.playerStart=new Vector3(0,.05f,15);arena.Restart(true);arena.boss.ResetBoss(arena.bossStart,Quaternion.identity);Advance(.12f);
            Vector3 centre=arena.boss.transform.TransformPoint(motor.localStampContact);
            arena.player.ResetPlayer(new Vector3(centre.x,.05f,centre.z+distance),Quaternion.identity);Physics.SyncTransforms();
        }
        private void Begin(){Require(arena.boss.TrySkill(ArenaAttack.Stomp),"Actual brain rejected stomp");}
        private void Finish()
        {
            for(int i=0;i<650&&motor.Active;i++)Tick(default);
            Require(!motor.Active,"Stomp did not finish");Require(motor.ContactMismatch<.015f,"Actual stamp differs from warning centre");
            Require(arena.boss.FoundationMotor.IsGuard,"Full recovery did not restore guard");
        }
        private void Advance(float seconds){float elapsed=0;while(elapsed<seconds-.000001f){float delta=Mathf.Min(Step,seconds-elapsed);Tick(default,delta);elapsed+=delta;}}
        private void Tick(ArenaCommand command,float delta=Step)
        {
            arena.Simulate(command,delta);maxRadius=Mathf.Max(maxRadius,motor.WaveRadius);
            var sample=new Sample{name=caseName,clip=arena.boss.animationDriver.ClipName,time=motor.SourceTime,radius=motor.WaveRadius,
                visibleRadius=motor.presentation.VisibleRadius,health=arena.player.fighter.Health,actor=arena.boss.transform.position,
                leftFoot=arena.boss.Socket("socket_foot.L").position,rightFoot=arena.boss.FootPosition,centre=motor.Centre,active=motor.Active,
                cancelled=motor.Cancelled,grounded=arena.boss.capsule.isGrounded,warning=motor.presentation.WarningVisible,invulnerable=arena.player.fighter.Invulnerable};
            if(soleProbe!=null&&motor.Active&&motor.SourceTime>=nextSoleTime)
            {
                nextSoleTime=motor.SourceTime+1f/60;sample.sole=soleProbe.Capture(rows.Count,motor.SourceTime);
                Require(sample.sole.reliable,"Actual sole probe unreliable: "+sample.sole.reason);
                Require(sample.sole.leftGroundFound&&Mathf.Abs(sample.sole.leftGroundClearanceM)<.005f,"Stomp left support left ground");
                Require(sample.sole.rightGroundClearanceM>-.005f,"Stomp right boot penetrated floor");
                if(sample.sole.hasPreviousSameClip)
                {
                    sample.reference=sourceReference.CapturePair(motor.clip,sourceMotion,rows.Count,sample.sole.previousSourceTime,motor.SourceTime,identity.source_sha256,identity.clips[0].fbx_sha256);
                    Require(sample.reference.reliable,"Source endpoint probe unreliable: "+sample.reference.reason);
                    Require(Mathf.Abs(sample.sole.leftMedianPatchTravelM-sample.reference.left.medianPatchTravelM)<.001f,"Extra runtime left support slip");
                }
            }
            samples.Add(sample);
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
