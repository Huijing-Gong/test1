using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Opt-in reproduction of an observed live failure. Reflection establishes
    // historical endpoint state ONLY in this fixture, before normal simulation.
    public sealed class ArenaPatrolNearEndpointContracts : MonoBehaviour
    {
        private const string Flag="--sam-patrol-near-endpoint-contracts";
        private const float Delta=1f/120;
        [Serializable] private sealed class SoleRow
        {
            public double battle;
            public string clip;
            public float sourceTime,elapsed,ankleError,boneLengthError;
            public bool leftSupported,rightSupported;
            public Vector3 root,leftAnchor,rightAnchor;
            public ArenaFoundationSoleProbe.Sample mesh;
        }
        [Serializable] private sealed class Frame
        {
            public double battle;
            public Vector3 boss,player;
            public float elapsed,sourceTime,distanceToA;
            public string state,clip;
            public bool bossGrounded,playerGrounded;
        }
        [Serializable] private sealed class Row
        {
            public string name,error,plannerFailure,fixture="TEST ONLY: ResetBoss/ResetPlayer plus reflected prior endpoint=1, initialEndpointVerified=true, completedIntervals=1. No transforms, endpoint history, health, contacts or motion state are changed after Begin.";
            public bool passed,accepted,visitedA;
            public float initialYaw,elapsed,minimumActualDistanceToA;
            public Vector3 initialBoss,initialPlayer;
            public int completedBefore,completedAfter;
            public ArenaPatrolPlanner.Plan plan;
            public ArenaEventRecord[] events;
            public ArenaPatrolPlanner.PatrolEvent[] patrol;
            public SoleRow[] sole;
            public Frame[] frames;
        }
        [Serializable] private sealed class Report
        {
            public string scope="REAL_NEAR_ENDPOINT_PATH_CHARACTER_CONTROLLERS_AND_BAKED_SOLES_HEADLESS_NOT_VISUAL_OR_PERFORMANCE",build;
            public bool passed,completed;
            public Row[] cases;
        }
        private ArenaSession arena;
        private ArenaFoundationSoleProbe probe;
        private readonly List<Row> rows=new List<Row>();
        private readonly List<SoleRow> soles=new List<SoleRow>();
        private readonly List<Frame> frames=new List<Frame>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,Flag)<0)return;
            if(args.Any(x=>x!=Flag&&(x.EndsWith("-contracts")||x.EndsWith("-review")||x=="--sam-benchmark")))
            {Application.Quit(2);return;}
            new GameObject("Patrol observed near endpoint reproduction").AddComponent<ArenaPatrolNearEndpointContracts>();
        }
        private IEnumerator Start()
        {
            yield return null;string output=null;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");
                Require(at>=0&&at+1<args.Length,"Output directory required.");
                output=Path.Combine(Path.GetFullPath(args[at+1]),"patrol_near_endpoint_contracts.json");
                Require(!File.Exists(output),"Preserve existing evidence.");Directory.CreateDirectory(Path.GetDirectoryName(output));
                arena=FindFirstObjectByType<ArenaSession>();Require(arena!=null,"Saved arena missing.");
                arena.automaticTick=false;arena.view.automaticTick=false;
                Require(arena.boss.useTwoPhaseAI&&!arena.boss.useDashSampleAI,"Saved default must use production AI.");
                probe=new ArenaFoundationSoleProbe(arena.boss.GetComponentInChildren<SkinnedMeshRenderer>());
                Run("observed_initial_99deg_near_A",99.631210f,new Vector3(-.391548f,.015f,2.787548f));
                Run("observed_after_90deg_turn_near_A",189.631210f,new Vector3(-.391548f,.015f,2.787548f));
                Run("observed_final_204deg_near_A",204.631210f,new Vector3(-.285249f,.015f,3.038826f));
                bool passed=rows.All(r=>r.passed);
                File.WriteAllText(output,JsonUtility.ToJson(new Report{build=Application.version,passed=passed,completed=true,cases=rows.ToArray()},true));
                Application.Quit(passed?0:2);
            }
            catch(Exception e)
            {
                Debug.LogException(e);
                if(output!=null&&!File.Exists(output))File.WriteAllText(output,e.ToString());
                Application.Quit(2);
            }
            finally{probe?.Dispose();}
        }
        private void Run(string name,float yaw,Vector3 player)
        {
            var row=new Row{name=name,initialYaw=yaw,initialBoss=new Vector3(0,0,3.713118f),initialPlayer=player,minimumActualDistanceToA=.286882f};
            soles.Clear();frames.Clear();probe.ResetHistory();
            try{Exercise(row);row.passed=true;}catch(Exception e){row.error=e.ToString();}
            var p=arena.boss.PatrolPlanner;
            row.elapsed=p.Elapsed;row.completedAfter=p.CompletedIntervals;row.plannerFailure=p.Failure;
            row.plan=p.CurrentPlan;row.events=arena.Events.ToArray();row.patrol=p.Events.ToArray();
            row.sole=soles.ToArray();row.frames=frames.ToArray();rows.Add(row);
        }
        private void Exercise(Row row)
        {
            arena.boss.decisionEnabled=true;arena.Restart(true);arena.player.view=null;
            arena.boss.ResetBoss(row.initialBoss,Quaternion.Euler(0,row.initialYaw,0));
            arena.player.ResetPlayer(row.initialPlayer,Quaternion.identity);
            arena.boss.capsule.Move(Vector3.down*.05f);arena.player.capsule.Move(Vector3.down*.05f);
            Physics.SyncTransforms();
            var p=arena.boss.PatrolPlanner;var m=p.Motor;
            Require(p.Available,"Patrol assets unavailable: "+p.UnavailableReason);
            Require(Vector3.ProjectOnPlane(p.pointA-new Vector3(0,0,4),Vector3.up).magnitude<.0001f
                &&Vector3.ProjectOnPlane(p.pointB,Vector3.up).magnitude<.0001f,"Fixture requires the saved A=(0,0,4), B=(0,0,0) route.");
            SetFixtureField(p,"<LastActualEndpointIndex>k__BackingField",1);
            SetFixtureField(p,"<CompletedIntervals>k__BackingField",1);
            SetFixtureField(p,"initialEndpointVerified",true);
            row.completedBefore=p.CompletedIntervals;
            Require(p.TargetIndex==0,"Fixture endpoint history must target A.");
            Require(arena.boss.capsule.isGrounded&&arena.player.capsule.isGrounded,"Fixture bodies must actually settle on the floor.");
            Require(!arena.player.fighter.Invulnerable&&!arena.player.fighter.TrainingInvincible,"No player immunity in fixture.");
            float playerHealth=arena.player.fighter.Health,bossHealth=arena.boss.fighter.Health;
            arena.Record("qa_near_endpoint_fixture","Explicit historical prior B; observed root 0.287m before A; no subsequent fixture mutation.",arena.boss.fighter,0,arena.boss.transform.position);
            row.accepted=p.Begin(arena);
            Require(row.accepted,"Observed near-endpoint position has no complete route: "+p.Failure);
            for(int tick=0;tick<900&&p.CompletedIntervals==row.completedBefore;tick++)
            {
                arena.Simulate(default,Delta);
                float distance=Vector3.ProjectOnPlane(arena.boss.transform.position-p.pointA,Vector3.up).magnitude;
                row.minimumActualDistanceToA=Mathf.Min(row.minimumActualDistanceToA,distance);
                if(p.LastActualEndpointIndex!=1)
                    Require(p.Events.Any(e=>e.kind=="boss_patrol_endpoint_reached"&&e.actualEndpointIndex==0
                        &&Vector3.ProjectOnPlane(e.actor-p.pointA,Vector3.up).magnitude<=p.endpointTolerance+.0001f),
                        "Endpoint history changed before physically reaching A.");
                frames.Add(new Frame{battle=arena.BattleTime,boss=arena.boss.transform.position,player=arena.player.transform.position,
                    elapsed=p.Elapsed,sourceTime=m.SourceTime,distanceToA=distance,state=m.State.ToString(),clip=m.ClipId,
                    bossGrounded=arena.boss.capsule.isGrounded,playerGrounded=arena.player.capsule.isGrounded});
                Require(arena.Active&&!arena.view.CinematicActive,"Route changed combat or camera ownership.");
                Require(arena.player.fighter.Health==playerHealth&&arena.boss.fighter.Health==bossHealth,"Route fixture must not damage either body.");
                if(m.Active&&tick%3==0)
                    soles.Add(new SoleRow{battle=arena.BattleTime,clip=m.ClipId,sourceTime=m.SourceTime,elapsed=p.Elapsed,
                        ankleError=m.MaximumAnkleError,boneLengthError=m.MaximumBoneLengthError,
                        leftSupported=m.LeftSupported,rightSupported=m.RightSupported,leftAnchor=m.LeftSole,rightAnchor=m.RightSole,
                        root=arena.boss.transform.position,mesh=probe.Capture(1,m.SourceTime)});
                Require(!p.Invalid,"Route invalid during real movement: "+p.Failure);
            }
            row.visitedA=p.Events.Any(e=>e.kind=="boss_patrol_endpoint_reached"&&e.actualEndpointIndex==0
                &&Vector3.ProjectOnPlane(e.actor-p.pointA,Vector3.up).magnitude<=p.endpointTolerance+.0001f);
            Require(p.Complete&&m.Complete&&p.CompletedIntervals==row.completedBefore+1,"One real complete interval was not completed.");
            Require(p.Elapsed>=4-.02f&&p.Elapsed<=6+.02f,"Near-endpoint route violated the original 4-6 second interval.");
            Require(row.visitedA,"A was not physically reached; predicted waypoint cannot update endpoint history.");
            Require(m.LeftSupported&&m.RightSupported,"Completion left a boot airborne.");
            Require(!arena.Events.Any(e=>e.kind=="boss_patrol_invalid"),"Success must not hide a failed interval/reorientation.");
            ValidateSoles();
        }
        private void ValidateSoles()
        {
            Require(soles.Count>5,"Insufficient baked mesh samples.");SoleRow previous=null;
            foreach(var s in soles)
            {
                var mesh=s.mesh;
                Require(mesh.reliable,"Unreliable baked sole: "+mesh.reason);
                Require(mesh.leftGroundFound&&mesh.rightGroundFound&&mesh.leftGroundClearanceM>=-.005f&&mesh.rightGroundClearanceM>=-.005f,"Boot mesh penetrated or lost floor.");
                Require(s.ankleError<=.003f&&s.boneLengthError<=.001f,"Original ankle/bone tolerances failed.");
                if(s.leftSupported)Require(Mathf.Abs(mesh.leftGroundClearanceM)<=.005f,"Left support exceeds 5mm floor gate.");
                if(s.rightSupported)Require(Mathf.Abs(mesh.rightGroundClearanceM)<=.005f,"Right support exceeds 5mm floor gate.");
                if(previous!=null&&previous.clip==s.clip&&mesh.hasPreviousSameClip&&s.sourceTime>previous.sourceTime+.000001f)
                {
                    if(s.leftSupported&&previous.leftSupported)Require(mesh.leftMedianPatchTravelM<=.002f,"Left support exceeds 2mm travel gate.");
                    if(s.rightSupported&&previous.rightSupported)Require(mesh.rightMedianPatchTravelM<=.002f,"Right support exceeds 2mm travel gate.");
                }
                previous=s;
            }
        }
        private static void SetFixtureField(object target,string name,object value)
        {
            var field=target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
            Require(field!=null,"Explicit fixture field missing: "+name);field.SetValue(target,value);
        }
        private static void Require(bool result,string reason){if(!result)throw new InvalidOperationException(reason);}
    }
}
