using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace SamProduction.BossArena
{
    // Default AI makes every attack decision. QA only supplies player commands
    // and a labelled real health hit to place the transition in a chosen state.
    public sealed class ArenaBattleContracts : MonoBehaviour
    {
        [Serializable] private sealed class Row
        { public string name,error; public bool passed; public double seconds; public int phase,intervals; public ArenaEventRecord[] events; public ArenaPatrolPlanner.PatrolEvent[] patrol; public SoleRow[] sole; }
        [Serializable] private sealed class SoleRow
        {
            public double battle; public string clip; public float sourceTime,elapsed,ankleError,boneLengthError,reachMargin;
            public bool walking,leftSupported,rightSupported; public Vector3 leftAnchor,rightAnchor,root; public Quaternion heading;
            public ArenaFoundationSoleProbe.Sample mesh;
        }
        [Serializable] private sealed class Report
        { public string scope="DEFAULT_TWO_PHASE_AI_HEADLESS_NOT_VISUAL_NATIVE_OR_PERFORMANCE",build; public bool passed; public Row[] cases; }
        private ArenaSession arena;
        private readonly List<Row> rows=new List<Row>();
        private readonly List<SoleRow> soles=new List<SoleRow>();
        private ArenaFoundationSoleProbe soleProbe;private int sampleTick,sequence,lastSoleTick;private string previousClip;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--sam-battle-contracts")<0)return;
            if(args.Any(x=>x!="--sam-battle-contracts"&&(x.EndsWith("-contracts")||x.EndsWith("-review")||x=="--sam-benchmark"))){Application.Quit(2);return;}
            new GameObject("Default battle contracts").AddComponent<ArenaBattleContracts>();
        }
        private IEnumerator Start()
        {
            yield return null;string output=null;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");Require(at>=0,"Output required");
                output=Path.Combine(Path.GetFullPath(args[at+1]),"battle_contracts.json");Require(!File.Exists(output),"Preserve evidence");Directory.CreateDirectory(Path.GetDirectoryName(output));
                arena=FindFirstObjectByType<ArenaSession>();arena.automaticTick=false;arena.view.automaticTick=false;
                if(Array.IndexOf(args,"--sam-battle-sole")>=0)soleProbe=new ArenaFoundationSoleProbe(arena.boss.GetComponentInChildren<SkinnedMeshRenderer>());
                Require(arena.boss.useTwoPhaseAI&&!arena.boss.useDashSampleAI,"Saved scene must default to production AI");
                Run("all_skills_transition_during_third_attack",()=>Battle(false));
                Run("half_health_during_patrol_uses_remaining_interval",()=>Battle(true));
                if(Array.IndexOf(args,"--sam-legacy-prefix-diagnostic")>=0)Run("diagnostic_15_degree_prefix_feasibility_only",()=>
                {
                    Fixture();int guard=0;while(!arena.boss.PatrolPlanner.Complete&&guard++<700)Tick();
                    Require(guard<700,"Initial interval missing");var p=arena.boss.PatrolPlanner;bool found=false;
                    foreach(int angle in new[]{15,-15})foreach(float fraction in new[]{1f,.8f,.6f,.4f})
                    {
                        var stop=Vector3.Lerp(p.pointB,p.pointA,fraction);
                        var heading=arena.boss.transform.rotation*Quaternion.Euler(0,angle,0);
                        bool feasible=p.Motor.TryPath(arena.boss.transform.position,heading,p.pointB,stop,arena.player.transform.position,1,0,false,out var path,out var reason,true,false,ArenaPatrolClips.DirectId);
                        arena.Record(feasible?"prefix_prediction_feasible":"prefix_prediction_rejected",angle+" deg / "+fraction,arena.boss.fighter,0,stop);found|=feasible;
                    }
                    Require(found,"Neither complete 1.2s 15-degree turn plus 4.8s six-step path was feasible; diagnostic only");
                });
                Run("death_during_composed_prefix_and_restart",()=>
                {
                    ReachCompoundTurn();Damage(100000);for(int i=0;i<600;i++)Tick();
                    Require(arena.State==ArenaRoundState.Victory&&!arena.boss.PatrolPlanner.Active&&!arena.view.CinematicActive,"Death during composed prefix leaked ownership");
                    arena.Restart(true);Require(arena.Active&&arena.boss.CombatPhase==1&&!arena.boss.PatrolPlanner.Active,"Compound death restart leaked state");
                });
                Run("defeat_during_composed_prefix_finishes_turn",()=>
                {
                    ReachCompoundTurn();arena.player.fighter.TrainingInvincible=false;
                    arena.player.fighter.Receive(new ArenaHit{source=arena.boss.fighter,strike=arena.NewStrike(),damage=100000,skill="qa_compound_player_lethal"});
                    for(int i=0;i<600;i++)Tick();Require(arena.State==ArenaRoundState.Defeat&&!arena.boss.PatrolPlanner.Active&&!arena.view.CinematicActive,"Defeat left composed motion active");
                    Require(arena.Events.Count(e=>e.kind=="boss_patrol_invalid")==1,"Defeat repeatedly cancelled the same continuing full turn");
                });
                Run("death_and_restart_release_all_owners",()=>
                {
                    Fixture();Damage(arena.boss.fighter.maximumHealth+1);
                    for(int i=0;i<600;i++)Tick();Require(arena.State==ArenaRoundState.Victory,"Boss defeat did not win");
                    arena.Restart(true);Require(arena.boss.CombatPhase==1&&!arena.boss.IgnitionPending&&!arena.view.CinematicActive&&!arena.boss.fighter.Invulnerable,"Restart leaked phase ownership");
                    arena.player.fighter.Receive(new ArenaHit{source=arena.boss.fighter,strike=arena.NewStrike(),damage=100000,skill="qa_player_lethal"});
                    Require(arena.State==ArenaRoundState.Defeat,"Player defeat missing");arena.Restart(true);Require(arena.Active&&!arena.player.fighter.Dead,"Defeat restart failed");
                });
                if(Array.IndexOf(args,"--sam-battle-boundaries")>=0)
                {
                    Run("default_AI_player_close_front",()=>SpatialBattle(new Vector3(1.4f,.05f,2.8f),false));
                    Run("default_AI_player_behind",()=>SpatialBattle(new Vector3(0,.05f,7.5f),false));
                    Run("default_AI_player_far",()=>SpatialBattle(new Vector3(9,.05f,-9),false));
                    Run("default_AI_player_moving_rectangle",()=>SpatialBattle(new Vector3(-4,.05f,-3),true));
                }
                if(Array.IndexOf(args,"--sam-battle-obstacles")>=0)
                {
                    Run("dive_low_ceiling_rejected_before_takeoff",()=>Ceiling(ArenaAttack.DiveKick,4));
                    Run("meteor_ceiling_rejected_before_camera_takeover",()=>Ceiling(ArenaAttack.Meteor,7));
                    Run("default_AI_low_ceiling_keeps_ground_attacks_available",()=>CeilingBattle());
                    Run("dive_wall_after_vertical_takeoff_aborts_and_lands",()=>TakeoffWall(ArenaAttack.DiveKick));
                    Run("meteor_side_wall_rejected_before_takeoff",()=>TakeoffWall(ArenaAttack.Meteor));
                }
                if(Array.IndexOf(args,"--sam-battle-input-clock")>=0)
                {
                    Run("elapsed_input_consumes_long_frame_without_repeating_sword_edge",()=>
                    {
                        Fixture();arena.boss.decisionEnabled=false;
                        double start=arena.BattleTime;
                        arena.SimulateElapsed(new ArenaCommand{attack=true},.917);
                        Require(Math.Abs(arena.BattleTime-start-.917)<.000001,"Actual elapsed input interval was discarded or stretched");
                        Require(arena.Events.Count(e=>e.kind=="player_attack_started")==1,"One input edge repeated attacks across internal steps");
                    });
                    Run("ignition_takeover_clears_prior_actual_knockback",()=>CinematicPush(ArenaAttack.Ignite));
                    Run("meteor_takeover_clears_prior_actual_knockback",()=>CinematicPush(ArenaAttack.Meteor));
                }
                File.WriteAllText(output,JsonUtility.ToJson(new Report{build=Application.version,passed=rows.All(x=>x.passed),cases=rows.ToArray()},true));
                Application.Quit(rows.All(x=>x.passed)?0:2);
            }
            catch(Exception e){Debug.LogException(e);if(output!=null&&!File.Exists(output))File.WriteAllText(output,e.ToString());Application.Quit(2);}
            finally{soleProbe?.Dispose();}
        }
        private void Fixture()
        {
            soles.Clear();sampleTick=sequence=0;previousClip=null;
            lastSoleTick=-100;soleProbe?.ResetHistory();
            arena.boss.decisionEnabled=true;arena.Restart(true);arena.player.fighter.TrainingInvincible=true;
            arena.player.view=null;Physics.SyncTransforms();
        }
        private void Battle(bool duringPatrol)
        {
            Fixture();bool hit=false;int intervalAtHit=-1;double queueTime=0;bool checkedInvulnerability=false;
            for(int tick=0;tick<15000;tick++)
            {
                var brain=arena.boss;var patrol=brain.PatrolPlanner;
                int selected=arena.Events.Count(e=>e.kind=="ai_skill_selected");
                if(!hit&&(duringPatrol ? patrol.Active&&patrol.Elapsed>2 : selected>=3&&brain.Attacking&&brain.SkillTime>.5f))
                {intervalAtHit=patrol.CompletedIntervals;queueTime=arena.BattleTime;Damage(brain.fighter.maximumHealth*.51f);hit=true;}
                if(brain.IgniteMotor.Active&&!checkedInvulnerability)
                {
                    float health=brain.fighter.Health;Damage(10);brain.CancelAttack();
                    Require(brain.fighter.Health==health&&brain.IgniteMotor.Active&&arena.view.CinematicActive,"Ignition invulnerability/ownership interrupted");checkedInvulnerability=true;
                    Require(patrol.CompletedIntervals==intervalAtHit+1,"Half health restarted or skipped its required interval");
                }
                Tick();
                Require(!brain.fighter.Dead&&arena.Active,"Unexpected terminal state");
                if(brain.CombatPhase==2&&arena.Events.Count(e=>e.kind=="ai_skill_selected")>=(duringPatrol?5:7)&&!brain.Attacking)break;
                if(tick==14999)throw new InvalidOperationException("Default AI failed to complete its pool within 125 simulated seconds; state="+brain.StateName+" route="+patrol.Failure);
            }
            var events=arena.Events;Require(hit&&checkedInvulnerability&&arena.boss.CombatPhase==2,"Transition never completed");
            Require(events.Count(e=>e.kind=="ignite_started")==1&&events.Count(e=>e.kind=="ignite_complete")==1,"Ignition repeated or incomplete");
            var queued=events.Single(e=>e.kind=="half_health_ignition_queued");var start=events.Single(e=>e.kind=="ignite_started");
            Require(!events.Any(e=>e.kind=="ai_skill_selected"&&e.time>queued.time&&e.time<start.time),"Ordinary skill overtook pending ignition");
            if(duringPatrol)Require(start.time-queueTime<4.05,"Remaining patrol was reset to a full interval");
            foreach(var id in new[]{"DashPunch","DiveKick","Stomp","Meteor"})Require(events.Any(e=>e.kind=="ai_skill_selected"&&e.skill==id),"Pool did not execute "+id);
            Require(events.Any(e=>e.kind=="meteor_ground_impact"),"Meteor selected but never released");
            Require(events.Any(e=>e.kind=="dive_flame_impact"),"Dive selected but never released");
            Require(!arena.boss.fighter.Invulnerable&&!arena.view.CinematicActive,"Completed battle retained invulnerability or camera");
            Require(!events.Any(e=>e.kind.EndsWith("landing_failed")),"An aerial action failed to land");
        }
        private void Damage(float amount)=>arena.boss.fighter.Receive(new ArenaHit{source=arena.player.fighter,strike=arena.NewStrike(),damage=amount,skill="qa_labelled_half_health_hit"});
        private void CinematicPush(ArenaAttack skill)
        {
            Fixture();arena.boss.decisionEnabled=false;
            Require(arena.player.fighter.Receive(new ArenaHit{source=arena.boss.fighter,strike=arena.NewStrike(),damage=1,push=7,direction=Vector3.right,skill="qa_prior_push"}),"Prior actual hit was not accepted");
            Require(arena.player.fighter.Knockback.magnitude>6,"Actual knockback was not established");
            Require(arena.boss.TrySkill(skill),"Cinematic skill refused");
            var position=arena.player.transform.position;
            for(int i=0;i<30;i++)Tick(new ArenaCommand{move=Vector2.right,attack=true,dodge=true});
            Require(Vector3.ProjectOnPlane(arena.player.transform.position-position,Vector3.up).magnitude<.001f,"Cinematic inherited a prior push");
            Require(arena.player.fighter.Knockback.sqrMagnitude<.000001f,"Cinematic retained delayed knockback");
            for(int i=0;i<1500&&arena.boss.Attacking;i++)Tick();
            Require(!arena.boss.Attacking&&!arena.view.CinematicActive,"Cinematic did not release control");
            position=arena.player.transform.position;
            for(int i=0;i<60;i++)Tick(new ArenaCommand{move=Vector2.right});
            Require(Vector3.ProjectOnPlane(arena.player.transform.position-position,Vector3.up).magnitude>1,"Restored movement remained suppressed");
        }
        private GameObject Roof(float height)
        {
            var block=GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name="CONTRACT actual ceiling";block.layer=8;
            block.transform.position=new Vector3(0,height,2);
            block.transform.localScale=new Vector3(24,.2f,24);
            Physics.SyncTransforms();return block;
        }
        private void Ceiling(ArenaAttack skill,float height)
        {
            Fixture();arena.boss.decisionEnabled=false;
            var boss=arena.boss;var position=boss.transform.position;var roof=Roof(height);
            try
            {
                Require(!boss.TrySkill(skill),"Blocked takeoff was accepted");
                Require(!boss.Attacking&&boss.FoundationMotor.IsGuard&&!arena.view.CinematicActive,"Rejected takeoff leaked ownership");
                Require(Vector3.Distance(position,boss.transform.position)<.00001f,"Rejected skill moved the actor");
                Require(arena.Events.Any(e=>e.kind.EndsWith("takeoff_obstructed")),"Missing actual obstruction evidence");
            }
            finally{DestroyImmediate(roof);Physics.SyncTransforms();}
            Require(boss.TrySkill(skill),"Removing actual ceiling did not permit takeoff");
            boss.CancelAttack();
            Require(!boss.Attacking&&!arena.view.CinematicActive,"Ground cancellation after removal leaked ownership");
        }
        private void CeilingBattle()
        {
            Fixture();var roof=Roof(4);bool hit=false;
            try
            {
                for(int i=0;i<12000;i++)
                {
                    if(!hit&&arena.BattleTime>2){Damage(arena.boss.fighter.maximumHealth*.51f);hit=true;}
                    Tick();
                    if(arena.boss.CombatPhase==2&&arena.Events.Count(e=>e.kind=="ai_skill_selected")>=3&&!arena.boss.Attacking)break;
                }
                Require(arena.boss.CombatPhase==2,"Ceiling stranded the half-health transition");
                Require(arena.Events.Any(e=>e.kind=="meteor_takeoff_obstructed"),"AI did not evaluate the blocked phase-two aerial skill");
                Require(arena.Events.Any(e=>e.kind=="ai_skill_selected"&&e.skill=="Stomp"),"AI did not continue with a ground skill");
                Require(!arena.Events.Any(e=>e.kind=="meteor_started"||e.kind=="dive_started"),"AI launched through the low ceiling");
                Require(!arena.boss.Attacking&&!arena.view.CinematicActive,"Ceiling battle left an action or cinematic active");
                Require(!arena.Events.Any(e=>e.kind=="boss_patrol_invalid"),"Ceiling battle used invalid patrol recovery");
            }
            finally{DestroyImmediate(roof);Physics.SyncTransforms();}
        }
        private void TakeoffWall(ArenaAttack skill)
        {
            Fixture();arena.boss.decisionEnabled=false;var boss=arena.boss;
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="CONTRACT actual ascent wall";wall.layer=8;
            wall.transform.position=boss.transform.position+boss.transform.forward*1.2f+Vector3.up*6;
            wall.transform.rotation=boss.transform.rotation;wall.transform.localScale=new Vector3(12,12,.2f);Physics.SyncTransforms();
            try
            {
                if(skill==ArenaAttack.DiveKick)
                {
                    // This source rises vertically until lock: the wall is in
                    // the later forward dive, not in its initial ascent.
                    Require(boss.TrySkill(skill),"Clear vertical takeoff was refused");
                    for(int i=0;i<900&&boss.DiveMotor.Active;i++)Tick();
                    Require(!boss.DiveMotor.Active&&boss.DiveMotor.Cancelled&&boss.DiveMotor.Landed,"Blocked dive failed full grounded recovery");
                    Require(boss.DiveMotor.ReleaseCount==0&&arena.Events.Any(e=>e.kind=="dive_world_blocked"),"Blocked dive released damage or lacked actual collision evidence");
                    Require(Vector3.Dot(boss.transform.position-wall.transform.position,wall.transform.forward)<-.1f,"Dive crossed the actual wall");
                }
                else Require(!boss.TrySkill(skill),"Ascent through an actual wall was accepted");
                Require(boss.FoundationMotor.IsGuard&&!boss.Attacking&&!arena.view.CinematicActive,"Wall handling leaked ownership");
            }
            finally{DestroyImmediate(wall);Physics.SyncTransforms();}
            Require(boss.TrySkill(skill),"Removed ascent wall still blocked the skill");boss.CancelAttack();
        }
        private void SpatialBattle(Vector3 playerStart,bool moving)
        {
            Fixture();arena.player.ResetPlayer(playerStart,Quaternion.identity);Physics.SyncTransforms();
            var watch=System.Diagnostics.Stopwatch.StartNew();bool hit=false;int waypoint=0;
            var route=new[]{new Vector3(-4,0,-3),new Vector3(4,0,-3),new Vector3(4,0,7),new Vector3(-4,0,7)};
            for(int tick=0;tick<18000;tick++)
            {
                var boss=arena.boss;
                if(!hit&&arena.BattleTime>2){Damage(boss.fighter.maximumHealth*.51f);hit=true;}
                ArenaCommand command=default;
                if(moving)
                {
                    Vector3 direction=Vector3.ProjectOnPlane(route[waypoint]-arena.player.transform.position,Vector3.up);
                    if(direction.magnitude<.3f){waypoint=(waypoint+1)%route.Length;direction=Vector3.ProjectOnPlane(route[waypoint]-arena.player.transform.position,Vector3.up);}
                    direction.Normalize();command.move=new Vector2(direction.x,direction.z);
                }
                Tick(command);
                Require(arena.Active&&!boss.fighter.Dead,"Spatial training fixture unexpectedly ended");
                Require(!arena.Events.Any(e=>e.kind.EndsWith("landing_failed")),"Spatial attack did not return to actual floor");
                Require(Mathf.Abs(boss.transform.position.x)<16&&Mathf.Abs(boss.transform.position.z)<16,"Boss escaped physical arena");
                if(boss.CombatPhase==2&&arena.Events.Count(e=>e.kind=="ai_skill_selected")>=5&&!boss.Attacking)
                {
                    Require(arena.Events.Count(e=>e.kind=="ignite_complete")==1,"Spatial transition missing or repeated");
                    foreach(string kind in new[]{"meteor_ground_impact","dive_flame_impact"})
                        Require(arena.Events.Any(e=>e.kind==kind),"Spatial pool never released "+kind);
                    Require(!arena.view.CinematicActive&&!boss.fighter.Invulnerable,"Spatial pool leaked ownership");
                    Require(!arena.Events.Any(e=>e.kind=="boss_patrol_invalid"),"Spatial pool completed only after invalid/over-budget patrol recovery; cadence is not passed");return;
                }
                Require(watch.Elapsed.TotalSeconds<40,"Spatial search exceeded 40-second execution watchdog at battle="+arena.BattleTime+" state="+boss.StateName+" route="+boss.PatrolPlanner.Failure);
            }
            throw new InvalidOperationException("Spatial AI failed to finish within150 simulated seconds: "+arena.boss.StateName+" route="+arena.boss.PatrolPlanner.Failure);
        }
        private void ReachCompoundTurn()
        {
            Fixture();bool hit=false;
            for(int i=0;i<2400;i++)
            {
                if(!hit&&arena.boss.PatrolPlanner.Elapsed>2){Damage(arena.boss.fighter.maximumHealth*.51f);hit=true;}
                Tick();
                if(arena.boss.PatrolPlanner.Active&&arena.boss.FoundationMotor.State==ArenaFoundationMotor.MotionState.Turning)return;
            }
            throw new InvalidOperationException("Default AI did not enter composed turn");
        }
        private void Tick(ArenaCommand command=default)
        {
            arena.Simulate(command,1f/120);sampleTick++;
            var planner=arena.boss.PatrolPlanner;var motor=planner.Motor;
            if(soleProbe==null||!planner.Active||sampleTick%3!=0)return;
            bool walking=motor.Active;string clip=walking?motor.ClipId:arena.boss.FoundationMotor.ClipId;
            float time=walking?motor.SourceTime:arena.boss.FoundationMotor.StateTime;
            bool contiguous=sampleTick-lastSoleTick<=3;
            if(!contiguous)soleProbe.ResetHistory();
            if(clip!=previousClip){sequence++;previousClip=clip;}
            soles.Add(new SoleRow{battle=arena.BattleTime,clip=clip,sourceTime=time,elapsed=planner.Elapsed,walking=walking,
                leftSupported=walking&&motor.LeftSupported,rightSupported=walking&&motor.RightSupported,
                leftAnchor=motor.LeftSole,rightAnchor=motor.RightSole,ankleError=motor.MaximumAnkleError,boneLengthError=motor.MaximumBoneLengthError,
                reachMargin=motor.MinimumReachMargin,root=arena.boss.transform.position,heading=arena.boss.transform.rotation,mesh=soleProbe.Capture(sequence,time,contiguous)});
            lastSoleTick=sampleTick;
        }
        private void Run(string name,Action action)
        {
            var row=new Row{name=name};try{action();row.passed=true;}catch(Exception e){row.error=e.ToString();}
            row.seconds=arena.BattleTime;row.phase=arena.boss.CombatPhase;row.intervals=arena.boss.PatrolPlanner.CompletedIntervals;
            row.events=arena.Events.ToArray();row.patrol=arena.boss.PatrolPlanner.Events.ToArray();row.sole=soles.ToArray();rows.Add(row);
        }
        private static void Require(bool result,string reason){if(!result)throw new InvalidOperationException(reason);}
    }
}
