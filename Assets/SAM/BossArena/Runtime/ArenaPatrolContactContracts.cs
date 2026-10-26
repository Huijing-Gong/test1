using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Explicit QA fixtures insert a grounded player into an already accepted
    // route. After that setup, the normal session and both real controllers run.
    // No private motor method is invoked and no collision/health gate is disabled.
    public sealed class ArenaPatrolContactContracts : MonoBehaviour
    {
        private const string Flag="--sam-patrol-contact-contracts";
        private const float Delta=1f/120;
        [Serializable] private sealed class SoleRow
        {
            public double battle;
            public string clip;
            public float sourceTime,elapsed,ankleError,boneLengthError,reachMargin;
            public bool walking,leftSupported,rightSupported;
            public Vector3 leftAnchor,rightAnchor,root;
            public Quaternion heading;
            public ArenaFoundationSoleProbe.Sample mesh;
        }
        [Serializable] private sealed class ContactFrame
        {
            public double battle;
            public Vector3 boss,player;
            public string motorState;
            public float sourceTime,requested,actual,requestLimit,attackTime,playerHealth,wallSeparation;
            public int steps,blockedSteps;
            public bool grounded,cinematic,dodging;
        }
        [Serializable] private sealed class Row
        {
            public string name,error,fixture="Grounded player placed without overlap AFTER route acceptance; subsequent displacement uses normal session Tick and CharacterController.Move.";
            public bool passed,wall,attackCommandAccepted;
            public double seconds;
            public int intervals,yieldSteps,blockedSteps;
            public float requestedDistance,actualDistance,maximumRequest,maximumActual,minimumWallSeparation;
            public float groundTolerance=.005f,supportTravelTolerance=.002f,ankleTolerance=.003f,boneTolerance=.001f;
            public ArenaEventRecord[] events;
            public ArenaPatrolPlanner.PatrolEvent[] patrol;
            public SoleRow[] sole;
            public ContactFrame[] frames;
        }
        [Serializable] private sealed class Report
        {
            public string scope="REAL_CHARACTER_CONTROLLER_CONTACT_AND_MESH_SOLES_HEADLESS_NOT_VISUAL_OR_PERFORMANCE",build;
            public bool passed;
            public Row[] cases;
        }
        private ArenaSession arena;
        private ArenaFoundationSoleProbe probe;
        private readonly List<Row> rows=new List<Row>();
        private readonly List<SoleRow> soles=new List<SoleRow>();
        private readonly List<ContactFrame> frames=new List<ContactFrame>();
        private int ticks;
        private float playerHealth;
        private GameObject wall;
        private Vector3 wallNearPoint,wallNormal;
        private float minimumWallSeparation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,Flag)<0)return;
            if(args.Any(x=>x!=Flag&&(x.EndsWith("-contracts")||x.EndsWith("-review")||x=="--sam-benchmark")))
            { Application.Quit(2);return; }
            new GameObject("Patrol real contact contracts").AddComponent<ArenaPatrolContactContracts>();
        }
        private IEnumerator Start()
        {
            yield return null;string output=null;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");
                Require(at>=0&&at+1<args.Length,"Output directory required.");
                output=Path.Combine(Path.GetFullPath(args[at+1]),"patrol_contact_contracts.json");
                Require(!File.Exists(output),"Preserve existing evidence.");Directory.CreateDirectory(Path.GetDirectoryName(output));
                arena=FindFirstObjectByType<ArenaSession>();Require(arena!=null,"Saved arena missing.");
                arena.automaticTick=false;arena.view.automaticTick=false;
                Require(arena.boss.useTwoPhaseAI&&!arena.boss.useDashSampleAI,"Saved default must use production AI.");
                probe=new ArenaFoundationSoleProbe(arena.boss.GetComponentInChildren<SkinnedMeshRenderer>());
                Run("late_grounded_player_yields_and_full_patrol_finishes",false);
                Run("late_player_against_real_wall_blocks_without_penetration",true);
                bool passed=rows.All(r=>r.passed);
                File.WriteAllText(output,JsonUtility.ToJson(new Report{build=Application.version,passed=passed,cases=rows.ToArray()},true));
                Application.Quit(passed?0:2);
            }
            catch(Exception e)
            {
                Debug.LogException(e);
                if(output!=null&&!File.Exists(output))File.WriteAllText(output,e.ToString());
                Application.Quit(2);
            }
            finally{RemoveWall();probe?.Dispose();}
        }
        private void Run(string name,bool withWall)
        {
            var row=new Row{name=name,wall=withWall};
            try{Exercise(row);row.passed=true;}catch(Exception e){row.error=e.ToString();}
            var p=arena.boss.PatrolPlanner;var m=p.Motor;
            row.seconds=arena.BattleTime;row.intervals=p.CompletedIntervals;
            row.yieldSteps=m.PlayerYieldSteps;row.blockedSteps=m.PlayerYieldBlockedSteps;
            row.requestedDistance=m.PlayerYieldRequestedDistance;row.actualDistance=m.PlayerYieldDistance;
            row.maximumRequest=m.MaximumPlayerYieldRequest;row.maximumActual=m.MaximumPlayerYieldStep;
            row.minimumWallSeparation=withWall?minimumWallSeparation:0;
            row.events=arena.Events.ToArray();row.patrol=p.Events.ToArray();row.sole=soles.ToArray();row.frames=frames.ToArray();
            rows.Add(row);RemoveWall();
        }
        private void Exercise(Row row)
        {
            RemoveWall();soles.Clear();frames.Clear();probe.ResetHistory();ticks=0;minimumWallSeparation=1000;
            arena.boss.decisionEnabled=true;arena.Restart(true);arena.player.view=null;
            Require(!arena.player.fighter.TrainingInvincible&&!arena.player.fighter.Invulnerable,"Fixture must retain real player damage.");
            playerHealth=arena.player.fighter.Health;Physics.SyncTransforms();
            var p=arena.boss.PatrolPlanner;var m=p.Motor;
            for(int i=0;i<180&&(!m.Active||m.SourceTime<.20f);i++)Tick();
            Require(m.State==ArenaPatrolMotionState.Moving&&m.SourceTime>=.20f,"Default AI did not enter a real walking route.");
            int initialIntervals=p.CompletedIntervals;
            var source=m.clips.SourceContract(m.ClipId);
            Vector3 placement=Vector3.zero;float placementProgress=0;bool found=false;
            for(float time=m.SourceTime+.3f;time<Mathf.Min(source.duration-.6f,2.7f);time+=.05f)
            {
                source.Sample(time,out float progress,out _,out _,out _,out _);
                Vector3 point=m.Path.Position(progress);
                float distance=Vector3.ProjectOnPlane(point-arena.boss.transform.position,Vector3.up).magnitude;
                if(distance<.85f||distance>1.1f)continue;
                placement=point;placementProgress=progress;found=true;break;
            }
            Require(found,"No non-overlapping near-future route point for the explicit late-contact fixture.");
            Vector3 forward=m.Path.forwardCurve!=null?m.Path.forwardCurve.Heading(placementProgress,m.Path.splitProgress)*Vector3.forward
                :Vector3.ProjectOnPlane(placement-arena.boss.transform.position,Vector3.up).normalized;
            placement.y=m.Path.origin.y+.01f;
            arena.player.ResetPlayer(placement,Quaternion.LookRotation(-forward));
            // Settle the fixture through the real controller, never by a second
            // position correction; gameplay from this point is normal Tick.
            arena.player.capsule.Move(Vector3.down*.05f);Physics.SyncTransforms();
            Require(arena.player.capsule.isGrounded,"Late-contact fixture player is not grounded.");
            Require(Vector3.ProjectOnPlane(arena.player.transform.position-arena.boss.transform.position,Vector3.up).magnitude
                >arena.player.capsule.radius+arena.boss.capsule.radius+.03f,"Late-contact fixture begins overlapped.");
            arena.Record("qa_late_patrol_contact_fixture",row.wall?"player_with_real_wall":"player_on_clear_floor",arena.player.fighter,0,arena.player.transform.position);
            if(row.wall)CreateWall(forward);
            bool issuedAttack=false;
            for(int i=0;i<900;i++)
            {
                bool attack=!issuedAttack&&m.PlayerYieldSteps>0;
                if(attack)issuedAttack=true;
                Tick(new ArenaCommand{attack=attack});
                if(row.wall&&m.State==ArenaPatrolMotionState.Invalid)break;
                if(!row.wall&&p.CompletedIntervals>initialIntervals)break;
            }
            Require(m.PlayerYieldSteps>0,"No real contact displacement was exercised.");
            Require(issuedAttack&&arena.Events.Count(e=>e.kind=="player_attack_started")==1,"Contact suppressed or repeated the one player sword command.");
            row.attackCommandAccepted=true;
            Require(arena.player.fighter.Health==playerHealth&&!arena.Events.Any(e=>e.kind=="hit"&&e.source=="Boss"),"Contact unexpectedly damaged the player.");
            Require(!arena.view.CinematicActive&&arena.Active,"Contact leaked control ownership or ended combat.");
            if(row.wall)
            {
                Require(m.PlayerYieldBlockedSteps>0,"Real wall did not constrain the player's Move.");
                Require(m.State==ArenaPatrolMotionState.Invalid&&!m.Active,"Blocked patrol did not finish its actual safe foot settle.");
                Require(m.LeftSupported&&m.RightSupported,"Blocked patrol relinquished airborne feet.");
                Require(p.CompletedIntervals==initialIntervals,"World-blocked interval was falsely counted complete.");
                Require(arena.Events.Any(e=>e.kind=="boss_patrol_motion_invalid")&&arena.Events.Any(e=>e.kind=="boss_patrol_invalid_settled"),"Missing truthful obstruction/settling evidence.");
                Require(minimumWallSeparation>=-.017f,"Player passed through the actual wall beyond controller skin tolerance.");
            }
            else
            {
                Require(m.PlayerYieldDistance>.01f,"Clear-floor case did not physically move the player.");
                Require(p.CompletedIntervals==initialIntervals+1&&m.Complete,"Clear-floor contact did not complete the same authored patrol.");
                Require(!arena.Events.Any(e=>e.kind=="boss_patrol_invalid"),"Clear-floor completion relied on invalid patrol recovery.");
                Require(p.Elapsed>=4-.02f&&p.Elapsed<=6+.02f,"Clear-floor patrol violated the 4-6 second interval.");
            }
            ValidateSoles();
        }
        private void CreateWall(Vector3 forward)
        {
            wallNormal=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;
            wallNearPoint=arena.player.transform.position+wallNormal*(arena.player.capsule.radius+.01f);
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="CONTRACT real wall behind late-contact player";wall.layer=8;
            wall.transform.SetPositionAndRotation(wallNearPoint+wallNormal*.06f+Vector3.up*2,Quaternion.LookRotation(wallNormal));
            wall.transform.localScale=new Vector3(2,4,.12f);Physics.SyncTransforms();
        }
        private void Tick(ArenaCommand command=default)
        {
            var m=arena.boss.PatrolPlanner.Motor;
            int beforeSteps=m.PlayerYieldSteps;float requestedBefore=m.PlayerYieldRequestedDistance;
            float actualBefore=m.PlayerYieldDistance,attackBefore=arena.player.AttackTime;
            arena.Simulate(command,Delta);ticks++;
            Require(arena.player.fighter.Health==playerHealth,"Player health changed during a contact-only interval.");
            Require(!arena.view.CinematicActive&&!arena.player.Dodging&&!arena.player.fighter.Invulnerable,"Contact changed player action or ownership state.");
            if(attackBefore>=0&&attackBefore+Delta<arena.player.attackDuration-.0001f)
                Require(arena.player.AttackTime>=attackBefore+Delta-.00001f,"Contact cancelled or froze the sword timeline.");
            int steps=m.PlayerYieldSteps-beforeSteps;
            if(steps>0)
            {
                float request=m.PlayerYieldRequestedDistance-requestedBefore;
                // The regular 120Hz session may split again at source markers.
                // Each contact request must remain within its measured local cap.
                Require(request<=steps*(m.LastPlayerYieldLimit+.00001f),"Contact requested a remote displacement beyond local movement plus skin.");
                Require(m.PlayerYieldDistance-actualBefore<=request+.002f*steps,"Controller contact travelled beyond requested displacement and numeric tolerance.");
            }
            float separation=0;
            if(wall!=null)
            {
                Vector3 centre=arena.player.capsule.transform.TransformPoint(arena.player.capsule.center);
                separation=Vector3.Dot(wallNearPoint-centre,wallNormal)-arena.player.capsule.radius;
                minimumWallSeparation=Mathf.Min(minimumWallSeparation,separation);
                Require(separation>=-.017f,"Actual player capsule crossed the wall plane.");
                Vector3 bossCentre=arena.boss.capsule.transform.TransformPoint(arena.boss.capsule.center);
                Require(Vector3.Dot(wallNearPoint-bossCentre,wallNormal)-arena.boss.capsule.radius>=-.017f,"Boss capsule crossed the wall plane.");
            }
            frames.Add(new ContactFrame{battle=arena.BattleTime,boss=arena.boss.transform.position,player=arena.player.transform.position,
                motorState=m.State.ToString(),sourceTime=m.SourceTime,requested=m.PlayerYieldRequestedDistance,actual=m.PlayerYieldDistance,
                requestLimit=m.LastPlayerYieldLimit,attackTime=arena.player.AttackTime,playerHealth=arena.player.fighter.Health,wallSeparation=separation,
                steps=m.PlayerYieldSteps,blockedSteps=m.PlayerYieldBlockedSteps,grounded=arena.player.capsule.isGrounded,
                cinematic=arena.view.CinematicActive,dodging=arena.player.Dodging});
            if(!m.Active||ticks%3!=0)return;
            soles.Add(new SoleRow{battle=arena.BattleTime,clip=m.ClipId,sourceTime=m.SourceTime,elapsed=arena.boss.PatrolPlanner.Elapsed,
                walking=true,leftSupported=m.LeftSupported,rightSupported=m.RightSupported,leftAnchor=m.LeftSole,rightAnchor=m.RightSole,
                ankleError=m.MaximumAnkleError,boneLengthError=m.MaximumBoneLengthError,reachMargin=m.MinimumReachMargin,
                root=arena.boss.transform.position,heading=arena.boss.transform.rotation,mesh=probe.Capture(1,m.SourceTime)});
        }
        private void ValidateSoles()
        {
            Require(soles.Count>5,"Insufficient actual mesh sole samples.");SoleRow previous=null;
            foreach(var s in soles)
            {
                var mesh=s.mesh;Require(mesh.reliable,"Actual baked sole measurement unreliable: "+mesh.reason);
                Require(mesh.leftGroundFound&&mesh.rightGroundFound&&mesh.leftGroundClearanceM>=-.005f&&mesh.rightGroundClearanceM>=-.005f,"Actual boot mesh penetrated or lost the floor.");
                Require(s.ankleError<=.003f&&s.boneLengthError<=.001f,"Original runtime ankle/bone tolerances failed.");
                if(s.leftSupported)Require(Mathf.Abs(mesh.leftGroundClearanceM)<=.005f,"Left supported boot gap exceeded original 5mm gate.");
                if(s.rightSupported)Require(Mathf.Abs(mesh.rightGroundClearanceM)<=.005f,"Right supported boot gap exceeded original 5mm gate.");
                if(previous!=null&&previous.clip==s.clip&&mesh.hasPreviousSameClip&&s.sourceTime>previous.sourceTime+.000001f)
                {
                    if(s.leftSupported&&previous.leftSupported)Require(mesh.leftMedianPatchTravelM<=.002f,"Left support boot exceeded original 2mm travel gate.");
                    if(s.rightSupported&&previous.rightSupported)Require(mesh.rightMedianPatchTravelM<=.002f,"Right support boot exceeded original 2mm travel gate.");
                }
                previous=s;
            }
        }
        private void RemoveWall(){if(wall!=null){DestroyImmediate(wall);wall=null;Physics.SyncTransforms();}}
        private static void Require(bool result,string reason){if(!result)throw new InvalidOperationException(reason);}
    }
}
