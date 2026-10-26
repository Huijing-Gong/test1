using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Explicit opt-in collision fixtures in the saved arena, not natural play.
    // No health injection, animation replacement or alternate Receive path.
    public sealed class ArenaDefenseIntegrationChecks : MonoBehaviour
    {
        [Serializable] private sealed class Row
        { public string name,error,outcome; public bool passed; public float health; public int hits,dodges,perfectDodges,blocks,perfectBlocks,feedback; public ArenaEventRecord[] events; }
        [Serializable] private sealed class Report
        { public string scope="SAVED_SCENE_REAL_CAPSULE_AND_RING_DEFENSE_FIXTURES_NOT_NATIVE_OR_DEFAULT_AI",unity,build,error; public bool passed; public Row[] cases; }
        private readonly List<Row> rows=new List<Row>();
        private ArenaSession arena;
        private const float Step=1f/120;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--sam-defense-integration-checks")<0)return;
            new GameObject("Opt-in real arena defense fixtures").AddComponent<ArenaDefenseIntegrationChecks>();
        }
        private IEnumerator Start()
        {
            yield return null;
            string output=null,error=null;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");
                Require(at>=0&&at+1<args.Length,"Output directory required");
                output=Path.Combine(Path.GetFullPath(args[at+1]),"defense_integration.json");
                Require(!File.Exists(output),"Preserve prior evidence");Directory.CreateDirectory(Path.GetDirectoryName(output));
                arena=FindFirstObjectByType<ArenaSession>();Require(arena!=null,"Saved scene missing");
                arena.automaticTick=false;arena.view.automaticTick=false;arena.boss.decisionEnabled=false;
                Check("Unified block feedback binds actual weapon grip",()=>{
                    Require(arena.defenseFeedback.blockContactAnchor==arena.player.avatar.DefenseContactAnchor
                        &&arena.defenseFeedback.blockContactAnchor!=null&&arena.defenseFeedback.blockContactAnchor.name=="WeaponGrip","Actual weapon grip anchor missing");
                });
                Check("Retry rejection telemetry emits only changed reasons and resets",()=>{
                    var planner=arena.boss.PatrolPlanner;
                    bool detailed=Array.IndexOf(Environment.GetCommandLineArgs(),"--sam-patrol-failure-details")>=0;
                    planner.ObserveRetryRejection(arena,"fixture reason A");
                    planner.ObserveRetryRejection(arena,"fixture reason A");
                    Require(planner.Events.Count(x=>x.kind=="boss_patrol_retry_rejected")== (detailed?1:0),"Repeated reason duplicated or first reason suppressed");
                    planner.ObserveRetryRejection(arena,"fixture reason B");
                    planner.ObserveRetryRejection(arena,"fixture reason B");
                    Require(planner.Events.Count(x=>x.kind=="boss_patrol_retry_rejected")== (detailed?2:0),"Changed reason suppressed or repeated reason emitted");
                    arena.Restart(true);
                    planner.ObserveRetryRejection(arena,"fixture reason B");
                    Require(planner.Events.Count(x=>x.kind=="boss_patrol_retry_rejected")== (detailed?1:0),"New round retained prior rejection cache");
                });
                Check("Full source turns finish at the planned clock boundary",()=>{
                    var motor=arena.boss.FoundationMotor;
                    foreach(int degrees in new[]{15,45,90,180})foreach(bool right in new[]{true,false})
                    foreach(float cadence in new[]{1f/120,1f/60,.013731f})
                    {
                        arena.boss.ResetBoss(arena.bossStart,Quaternion.Euler(0,180,0));
                        Require(motor.TryTurn(degrees,right),"Source turn refused");
                        double remaining=motor.TurnDuration(degrees,right);
                        while(remaining>1e-9){float part=(float)Math.Min(remaining,cadence);motor.Tick(part,arena);remaining-=part;}
                        Require(motor.IsGuard,"Planned clock finished before Foundation guard: "+degrees+" "+right+" "+cadence+" source="+motor.StateTime);
                        Require(Mathf.Abs(Mathf.DeltaAngle(arena.boss.transform.eulerAngles.y,180+(right?degrees:-degrees)))<.05f,"Turn end yaw missed authored angle");
                    }
                });
                Check("Perfect block consumes repeated real capsule strike",()=>{
                    Guard(.075f,true);long strike=Contact();
                    Require(F.Health==F.maximumHealth&&F.PerfectBlockCount==1,"Perfect block failed");
                    Require(arena.defenseFeedback.ActiveFeedbackCount==1,"Expected one precise block burst");
                    Guard(.3f,false);Contact(strike);
                    Require(F.Health==F.maximumHealth&&F.BlockCount==1,"Same strike damaged after perfect window");
                    Contact();Require(Near(F.Health,F.maximumHealth-3.5f)&&F.BlockCount==2,"Next real strike should chip 35%");
                    Require(arena.player.Guarding&&arena.player.ActionName=="Block","Chip damage caused full hurt cancel");
                });
                Check("Ordinary guard is distinct from dodge",()=>{
                    Guard(.25f,true);Contact();Require(Near(F.Health,F.maximumHealth-3.5f),"Front chip amount");
                    Require(F.DodgeCount==0&&F.PerfectBlockCount==0&&F.BlockCount==1,"Incorrect classification");
                });
                Check("Ring uses actual propagation from rear centre",()=>{
                    Guard(.25f,true);Vector3 centre=arena.player.transform.position-Vector3.forward;
                    arena.StrikeRing(arena.boss.fighter,arena.NewStrike(),"qa_rear_ring",centre,1.5f,0,10,1);
                    Require(Near(F.Health,F.maximumHealth-10)&&F.BlockCount==0,"Rear ring was blocked using source position");
                });
                Check("Real perfect dodge awards once and keeps strike dedup",()=>{
                    Dodge(.0833333f);long strike=Contact();
                    Require(F.PerfectDodgeCount==1&&F.DodgeCount==1&&arena.player.PerfectCounterReady,"Perfect dodge missing");
                    Contact(strike);Contact();Require(F.PerfectDodgeCount==1&&F.DodgeCount==2,"Duplicate perfect reward");
                    Require(F.Health==F.maximumHealth,"Dodged contact damaged health");
                    Tick(default,.5f);Tick(new ArenaCommand{attack=true},Step);
                    Require(!arena.player.PerfectCounterReady&&arena.Events.Exists(e=>e.kind=="perfect_counter_started"),"Accepted next sword did not consume reward");
                });
                Check("Unused perfect reward expires",()=>{
                    Dodge(.0833333f);Contact();Tick(default,1.2f);
                    Require(!arena.player.PerfectCounterReady,"Unused counter reward did not expire");
                });
                Check("Late iframe is ordinary dodge",()=>{
                    Dodge(.25f);Contact();Require(F.DodgeCount==1&&F.PerfectDodgeCount==0&&!arena.player.PerfectCounterReady,"Late dodge incorrectly rewarded");
                });
                Check("Early contact hits before iframe",()=>{
                    Dodge(.0166667f);Contact();Require(Near(F.Health,F.maximumHealth-10)&&F.DodgeCount==0,"Early contact incorrectly avoided");
                });
                Check("Outside iframe hits",()=>{
                    Dodge(.4f);Contact();Require(Near(F.Health,F.maximumHealth-10)&&F.DodgeCount==0,"Expired iframe still protects");
                });
                Check("WithoutEdges retains held guard only",()=>{
                    var c=new ArenaCommand{guardHeld=true,guardPressed=true,dodge=true,attack=true,toggleLock=true,run=true,move=Vector2.one,look=Vector2.one}.WithoutEdges();
                    Require(c.guardHeld&&c.run&&c.move==Vector2.one&&!c.guardPressed&&!c.dodge&&!c.attack&&!c.toggleLock&&c.look==Vector2.zero,"Substep command loss/repeated edge");
                    arena.SimulateElapsed(new ArenaCommand{guardHeld=true,guardPressed=true},.5);
                    Require(arena.player.Guarding&&!arena.player.guard.PerfectWindowOpen,"Long frame lost held guard or renewed perfect window");
                });
                Check("Held restart gives no new perfect window",()=>{
                    Guard(.075f,true);Contact();arena.Restart(true);
                    Guard(.075f,false);Contact();Require(F.PerfectBlockCount==0&&Near(F.Health,F.maximumHealth-3.5f),"Held reset gifted precision");
                    Require(F.DodgeCount==0&&!arena.player.PerfectCounterReady,"Restart retained reward");
                });
                Check("Cinematic suspend and return preserve held edge",()=>{
                    Guard(.075f,true);object token=new object();arena.view.BeginCinematic(token);
                    Tick(new ArenaCommand{guardHeld=true},Step);Require(!arena.player.Guarding&&!F.Invulnerable,"Cinematic left defense live");
                    arena.view.EndCinematic(token);Guard(.075f,false);Contact();
                    Require(F.PerfectBlockCount==0&&F.BlockCount==1,"Cinematic return gifted precision");
                });
                Check("Lethal actual collision then retry clears state",()=>{
                    Contact(0,F.maximumHealth+1);Require(arena.State==ArenaRoundState.Defeat&&F.Dead&&!F.Invulnerable&&!arena.player.Guarding,"Lethal collision cleanup");
                    arena.Restart(true);Require(arena.Active&&F.Health==F.maximumHealth&&F.HitCount==0&&F.BlockCount==0&&!arena.player.PerfectCounterReady,"Retry cleanup");
                    Contact();Require(Near(F.Health,F.maximumHealth-10),"New round strike ID incorrectly deduped");
                });
                Check("Feedback resets without a stale burst",()=>{
                    Guard(.075f,true);Contact();Require(arena.defenseFeedback.ActiveFeedbackCount==1,"Burst missing");
                    arena.Restart(false);Require(arena.defenseFeedback.ActiveFeedbackCount==0&&!arena.defenseFeedback.WarningVisible,"Stale feedback after reset");
                });
            }
            catch(Exception e){error=e.ToString();Debug.LogException(e);}
            bool passed=error==null&&rows.Count>0&&rows.TrueForAll(r=>r.passed);
            if(output!=null)File.WriteAllText(output,JsonUtility.ToJson(new Report{unity=Application.unityVersion,build=Application.version,error=error,passed=passed,cases=rows.ToArray()},true));
            Debug.Log("SAM_DEFENSE_INTEGRATION "+(passed?"PASS":"FAIL")+" cases="+rows.Count);
            Application.Quit(passed?0:2);
        }
        private ArenaFighter F=>arena.player.fighter;
        private void Check(string name,Action test)
        {
            arena.Restart(true);arena.boss.decisionEnabled=false;
            // Each fixture starts from a released physical guard state.
            Tick(default,Step);arena.player.guard.ResetGuard(false);
            var row=new Row{name=name};
            try{test();row.passed=true;}catch(Exception e){row.error=e.ToString();Debug.LogWarning(name+": "+e.Message);}
            row.health=F.Health;row.hits=F.HitCount;row.dodges=F.DodgeCount;row.perfectDodges=F.PerfectDodgeCount;row.blocks=F.BlockCount;row.perfectBlocks=F.PerfectBlockCount;row.outcome=F.LastDefense.outcome.ToString();row.feedback=arena.defenseFeedback.ActiveFeedbackCount;row.events=arena.Events.ToArray();rows.Add(row);
        }
        private void Tick(ArenaCommand command,float elapsed)
        {
            while(elapsed>1e-7f){float d=Mathf.Min(elapsed,Step);arena.player.Tick(command,d,arena);arena.defenseFeedback.Advance(d);elapsed-=d;command=command.WithoutEdges();}
        }
        private void Guard(float elapsed,bool press)=>Tick(new ArenaCommand{guardHeld=true,guardPressed=press},elapsed);
        private void Dodge(float elapsed)=>Tick(new ArenaCommand{dodge=true},elapsed);
        private long Contact(long strike=0,float damage=10)
        {
            if(strike==0)strike=arena.NewStrike();Vector3 p=arena.player.transform.position;
            arena.StrikeCapsule(arena.boss.fighter,strike,"qa_actual_body_capsule",p+Vector3.up*.4f,p+Vector3.up*1.25f,.25f,damage,1);
            return strike;
        }
        private static bool Near(float a,float b)=>Mathf.Abs(a-b)<.001f;
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
