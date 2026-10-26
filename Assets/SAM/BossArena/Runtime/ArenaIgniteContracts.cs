using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace SamProduction.BossArena
{
    public sealed class ArenaIgniteContracts : MonoBehaviour
    {
        [Serializable] private sealed class Row
        {public string name,error;public bool passed;public int beginCount;public float sourceTime,bossHealth;public ArenaEventRecord[] events;}
        [Serializable] private sealed class Sample
        {public string name,clip;public float time,bossHealth,playerSpeed;public bool active,invulnerable,cameraOwned,grounded;public Vector3 boss,player,leftFoot,rightFoot,camera;public ArenaFoundationSoleProbe.Sample sole;public ArenaDashSourceSoleReference.Sample reference;}
        [Serializable] private sealed class Report
        {public string scope="ACTUAL_HEADLESS_TRANSFORMATION_CONTRACT_NOT_HALF_HP_AI_VISUAL_NATIVE_INPUT_OR_PERFORMANCE",build;public bool passed;public Row[] cases;public Sample[] samples;}
        private ArenaSession arena;private ArenaIgniteMotor motor;private new string name;
        private readonly List<Row> rows=new List<Row>();private readonly List<Sample> samples=new List<Sample>();
        private const float Step=1f/120;
        private ArenaFoundationSoleProbe soleProbe;private ArenaDashSourceSoleReference sourceReference;private ArenaMotionClip sourceMotion;private float nextSoleTime;
        [Serializable] private sealed class Identity{public string source_sha256;public ClipIdentity[] clips;}
        [Serializable] private sealed class ClipIdentity{public string fbx_sha256;}
        private Identity identity;
        [Serializable] private sealed class SamplingRow {public float time;public Vector3 playableLeft,playableRight,directLeft,directRight;public bool directSamplingRestoredSentinel;}
        [Serializable] private sealed class SamplingReport {public string scope="EXPLICIT_QA_LIVE_SAMPLER_COMPARISON_NOT_ACCEPTANCE";public SamplingRow[] samples;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--sam-ignite-contracts")<0)return;
            if(args.Any(a=>a!="--sam-ignite-contracts"&&(a.EndsWith("-contracts",StringComparison.Ordinal)||a.EndsWith("-review",StringComparison.Ordinal)||a=="--sam-benchmark")))
            {Debug.LogError("Multiple simulation owners");Application.Quit(2);return;}
            new GameObject("Ignition actual contract checks").AddComponent<ArenaIgniteContracts>();
        }
        private IEnumerator Start()
        {
            yield return null;string output=null;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");Require(at>=0&&at+1<args.Length,"Output required");
                output=Path.Combine(Path.GetFullPath(args[at+1]),"ignite_contracts.json");Require(!File.Exists(output),"Preserve prior report");Directory.CreateDirectory(Path.GetDirectoryName(output));
                arena=FindFirstObjectByType<ArenaSession>();arena.automaticTick=false;arena.view.automaticTick=false;
                if(arena.sound!=null)arena.sound.enabled=false;arena.sound=null;motor=arena.boss.IgniteMotor;Require(motor!=null,"Actual imported motor missing");
                if(Array.IndexOf(args,"--sam-ignite-sampling-diagnostic")>=0)
                {
                    Fixture();var diagnostic=new List<SamplingRow>();
                    foreach(float t in new[]{0f,1f/120,1f/60,.025f,2f/60,.05f,.125f,.5f,1f,2f})
                    {
                        arena.boss.animationDriver.PoseContactClip(motor.clip,t,0);
                        var r=new SamplingRow{time=t,playableLeft=arena.boss.Socket("socket_foot.L").position,playableRight=arena.boss.FootPosition};
                        var foot=arena.boss.GetComponentsInChildren<Transform>().Single(x=>x.name=="foot.R");var savedFoot=foot.localPosition;
                        foot.localPosition=savedFoot+Vector3.one*100;
                        motor.clip.SampleAnimation(arena.boss.visual.gameObject,t);
                        r.directSamplingRestoredSentinel=Vector3.Distance(foot.localPosition,savedFoot)<1;
                        r.directLeft=arena.boss.Socket("socket_foot.L").position;r.directRight=arena.boss.FootPosition;foot.localPosition=savedFoot;diagnostic.Add(r);
                    }
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"sampling_diagnostic.json"),JsonUtility.ToJson(new SamplingReport{samples=diagnostic.ToArray()},true));
                    arena.boss.animationDriver.ResetPose();
                }
                if(ArenaDashSourceSoleReference.Requested)
                {
                    var mesh=arena.boss.GetComponentInChildren<SkinnedMeshRenderer>();soleProbe=new ArenaFoundationSoleProbe(mesh);sourceReference=new ArenaDashSourceSoleReference(arena.boss.visual,mesh);
                    sourceMotion=ArenaMotionData.Read(motor.motionData).Get("phase_ignite");identity=JsonUtility.FromJson<Identity>(ArenaMotionEncoding.Text(motor.motionData));
                }
                Run("full_invulnerable_input_suppression_and_restore",()=>
                {
                    Fixture();var position=arena.player.transform.position;float hp=arena.boss.fighter.Health;Begin();
                    for(int i=0;i<800&&motor.Active;i++)
                    {
                        Hit();Tick(new ArenaCommand{move=Vector2.one,attack=true,dodge=true,toggleLock=true,look=new Vector2(300,50)});
                        Require(arena.boss.fighter.Health==hp,"Damage during ignition");
                        Require(Vector3.ProjectOnPlane(arena.player.transform.position-position,Vector3.up).magnitude<.001f,"Player moved during takeover");
                        if(motor.Active)Require(arena.view.CinematicActive&&arena.boss.fighter.Invulnerable&&!arena.player.Dodging&&arena.player.AttackTime<0,"Takeover incomplete");
                    }
                    Require(motor.Completed&&motor.SourceTime==4.8f&&!arena.view.CinematicActive&&!arena.boss.fighter.Invulnerable,"End restore failed");
                    Require(motor.presentation.ReleaseCount==1&&motor.presentation.Overheat,"Missing one release and sustained overheat");
                    Require(!arena.boss.TrySkill(ArenaAttack.Ignite)&&motor.BeginCount==1,"Repeated ignition allowed");
                    Hit();Require(arena.boss.fighter.Health==hp-1,"Vulnerability not restored");
                    for(int i=0;i<60;i++)Tick(new ArenaCommand{move=Vector2.right});
                    Require(Vector3.ProjectOnPlane(arena.player.transform.position-position,Vector3.up).magnitude>.4f,"Movement not restored");
                    Tick(new ArenaCommand{dodge=true});Require(arena.player.Dodging,"Dodge not restored");
                });
                Run("ordinary_cancel_cannot_interrupt",()=>{Fixture();Begin();Advance(1.7f);arena.boss.CancelAttack();Require(motor.Active&&arena.view.CinematicActive&&arena.boss.fighter.Invulnerable,"Ordinary cancellation interrupted ignition");Advance(3.2f);Require(motor.Completed&&!arena.view.CinematicActive,"Complete source did not restore");});
                foreach(float atTime in new[]{1f,3.5f})
                {
                    float time=atTime;Run("restart_at_"+time,()=>{Fixture();Begin();Advance(time);arena.Restart(true);Require(!motor.Active&&!motor.Completed&&motor.BeginCount==0&&!arena.boss.fighter.Invulnerable&&!arena.view.CinematicActive&&!motor.presentation.Overheat,"Restart leaked ownership");});
                }
                Run("player_death_releases_cinematic",()=>{Fixture();Begin();Advance(1);arena.player.fighter.Receive(new ArenaHit{source=arena.boss.fighter,strike=arena.NewStrike(),damage=100000,skill="test_terminal"});Tick(default);Require(!arena.Active&&!motor.Active&&!arena.view.CinematicActive&&!arena.boss.fighter.Invulnerable&&!motor.presentation.Overheat,"Terminal state leaked cinematic");});
                var report=new Report{build=Application.version,passed=rows.All(r=>r.passed),cases=rows.ToArray(),samples=samples.ToArray()};sourceReference?.Dispose();soleProbe?.Dispose();File.WriteAllText(output,JsonUtility.ToJson(report,true));Application.Quit(report.passed?0:2);
            }
            catch(Exception e){Debug.LogException(e);if(output!=null&&!File.Exists(output))File.WriteAllText(output,JsonUtility.ToJson(new Report{build=Application.version,cases=new[]{new Row{name="setup",error=e.ToString()}}},true));Application.Quit(2);}
        }
        private void Run(string id,Action action)
        {name=id;nextSoleTime=0;var row=new Row{name=id};try{action();row.passed=true;}catch(Exception e){row.error=e.ToString();}row.beginCount=motor.BeginCount;row.sourceTime=motor.SourceTime;row.bossHealth=arena.boss.fighter.Health;row.events=arena.Events.ToArray();rows.Add(row);}
        private void Fixture()
        {arena.boss.decisionEnabled=false;arena.boss.useDashSampleAI=false;arena.bossStart=new Vector3(0,.025f,0);arena.playerStart=new Vector3(0,.05f,6);arena.Restart(true);arena.boss.ResetBoss(arena.bossStart,Quaternion.identity);Advance(.12f);}
        private void Begin(){Require(arena.boss.TrySkill(ArenaAttack.Ignite),"Brain refused ignition");}
        private void Hit(){arena.boss.fighter.Receive(new ArenaHit{source=arena.player.fighter,strike=arena.NewStrike(),damage=1,skill="test_incoming",direction=Vector3.forward});}
        private void Advance(float seconds){for(int i=0;i<Mathf.CeilToInt(seconds/Step);i++)Tick(default);}
        private void Tick(ArenaCommand command)
        {
            arena.Simulate(command,Step);arena.view.TickCamera(Step);
            var sample=new Sample{name=name,clip=arena.boss.animationDriver.ClipName,time=motor.SourceTime,bossHealth=arena.boss.fighter.Health,playerSpeed=arena.player.Speed,
                active=motor.Active,invulnerable=arena.boss.fighter.Invulnerable,cameraOwned=arena.view.CinematicActive,grounded=arena.boss.capsule.isGrounded,
                boss=arena.boss.transform.position,player=arena.player.transform.position,leftFoot=arena.boss.Socket("socket_foot.L").position,rightFoot=arena.boss.FootPosition,camera=arena.view.transform.position};
            if(soleProbe!=null&&motor.Active&&motor.SourceTime>=nextSoleTime)
            {
                nextSoleTime=motor.SourceTime+1f/60;sample.sole=soleProbe.Capture(rows.Count,motor.SourceTime);
                Require(sample.sole.reliable,"Actual sole probe unreliable: "+sample.sole.reason);
                Require(sample.sole.leftGroundFound&&sample.sole.rightGroundFound&&Mathf.Abs(sample.sole.leftGroundClearanceM)<.005f&&Mathf.Abs(sample.sole.rightGroundClearanceM)<.005f,"Ignition boots left real floor");
                if(sample.sole.hasPreviousSameClip)
                {
                    sample.reference=sourceReference.CapturePair(motor.clip,sourceMotion,rows.Count,sample.sole.previousSourceTime,motor.SourceTime,identity.source_sha256,identity.clips[0].fbx_sha256);
                    Require(sample.reference.reliable,"Source sole reference unreliable: "+sample.reference.reason);
                    float leftExtra=Mathf.Abs(sample.sole.leftMedianPatchTravelM-sample.reference.left.medianPatchTravelM),rightExtra=Mathf.Abs(sample.sole.rightMedianPatchTravelM-sample.reference.right.medianPatchTravelM);
                    if(leftExtra>=.001f||rightExtra>=.001f)samples.Add(sample);
                    Require(leftExtra<.001f&&rightExtra<.001f,"Additional runtime sole slip: left="+leftExtra+", right="+rightExtra+", actual="+sample.sole.leftMedianPatchTravelM+"/"+sample.sole.rightMedianPatchTravelM+", source="+sample.reference.left.medianPatchTravelM+"/"+sample.reference.right.medianPatchTravelM);
                }
            }
            samples.Add(sample);
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
