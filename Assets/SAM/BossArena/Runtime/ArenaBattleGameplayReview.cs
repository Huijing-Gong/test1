using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace SamProduction.BossArena
{
    // Normal-clock default AI. This labelled stationary training fixture keeps
    // damage callbacks/collision live, and supplies one explicit half-health hit.
    // It is not a native-player victory or an uncapped performance measurement.
    public sealed class ArenaBattleGameplayReview : MonoBehaviour
    {
        [Serializable] private sealed class Frame
        { public double wall,simulated,raw,battle;public int phase;public string state,skill;public float bossHealth,playerHealth,sourceTime,patrolElapsed;public bool cameraOwned,invulnerable,patrolInvalid;public Vector3 boss,player;
          public Vector3 cameraPosition,playerViewport,bossViewport,hazardViewport;public Quaternion cameraRotation;
          public float cameraFov,returnBlend,hazardFraming,groupDistance;public int width,height; }
        [Serializable] private sealed class Report
        { public string scope,build,error;public bool complete,cadencePassed;public double simulated,maxInterval;public Frame[] frames;public ArenaEventRecord[] events;public ArenaPatrolPlanner.PatrolEvent[] patrol;public ArenaPatrolPlanner.SearchTiming[] slowPatrolSearches; }
        private ArenaSession arena;private string output,error;private bool ready,halfHit,finished,saved,duringPatrol;private double previous,started,simulated,maxInterval,finishAt=-1;
        private readonly List<Frame> frames=new List<Frame>();private GUIStyle style;
        private bool movingPlayer;private int waypoint;
        private readonly Vector3[] route={new Vector3(-4,0,-3),new Vector3(4,0,-3),new Vector3(4,0,7),new Vector3(-4,0,7)};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--sam-battle-review")<0)return;
            if(args.Any(x=>x!="--sam-battle-review"&&(x.EndsWith("-contracts")||x.EndsWith("-review")||x=="--sam-benchmark"))){Application.Quit(2);return;}
            new GameObject("Default two-phase normal clock review").AddComponent<ArenaBattleGameplayReview>();
        }
        private IEnumerator Start()
        {
            yield return null;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");Require(at>=0,"Output required");output=Path.GetFullPath(args[at+1]);
                duringPatrol=Array.IndexOf(args,"--sam-half-during-patrol")>=0;
                movingPlayer=Array.IndexOf(args,"--sam-moving-player")>=0;
                Require(!File.Exists(Path.Combine(output,"realtime_battle.json")),"Preserve prior recording");Directory.CreateDirectory(output);
                arena=FindFirstObjectByType<ArenaSession>();Require(arena.boss.ProductionAI,"Saved scene must use default two-phase AI");
                arena.automaticTick=false;arena.view.automaticTick=false;arena.Restart(false);
                Application.runInBackground=true;Time.captureFramerate=0;Time.timeScale=1;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
                Screen.SetResolution(1920,1080,FullScreenMode.Windowed);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            }
            catch(Exception e){Fail(e);yield break;}
            yield return new WaitForSecondsRealtime(1.2f);yield return new WaitForEndOfFrame();
            arena.Restart(true);arena.player.fighter.TrainingInvincible=true;arena.player.view=null;arena.view.TickLook(Vector2.zero,true);
            if(movingPlayer){arena.player.ResetPlayer(new Vector3(-4,.05f,-3),Quaternion.identity);Physics.SyncTransforms();}
            started=previous=Time.realtimeSinceStartupAsDouble;ready=true;
        }
        private void Update()
        {
            if(!ready||saved)return;
            try
            {
                Require(Time.captureFramerate==0&&Time.timeScale==1,"Normal clock changed");
                double now=Time.realtimeSinceStartupAsDouble,raw=now-previous;previous=now;maxInterval=Math.Max(maxInterval,raw);Require(raw>=0&&raw<=5,"Display interval exceeded review bounds");
                double remaining=raw;
                while(remaining>1e-8)
                {
                    float step=(float)Math.Min(remaining,1d/120);remaining-=step;simulated+=step;
                    var boss=arena.boss;
                    if(!halfHit&&(duringPatrol ? boss.PatrolPlanner.Active&&boss.PatrolPlanner.Elapsed>2 : arena.Events.Count(e=>e.kind=="ai_skill_selected")>=3&&boss.Attacking&&boss.SkillTime>.5f))
                    {
                        halfHit=true;boss.fighter.Receive(new ArenaHit{source=arena.player.fighter,strike=arena.NewStrike(),damage=boss.fighter.maximumHealth*.51f,skill="qa_labelled_half_health_hit"});
                    }
                    ArenaCommand command=default;
                    if(movingPlayer)
                    {
                        Vector3 direction=Vector3.ProjectOnPlane(route[waypoint]-arena.player.transform.position,Vector3.up);
                        if(direction.magnitude<.3f){waypoint=(waypoint+1)%route.Length;direction=Vector3.ProjectOnPlane(route[waypoint]-arena.player.transform.position,Vector3.up);}
                        direction.Normalize();command.move=new Vector2(direction.x,direction.z);
                    }
                    arena.Simulate(command,step);
                    if(!finished&&halfHit&&boss.CombatPhase==2&&arena.Events.Count(e=>e.kind=="ai_skill_selected")>=(duringPatrol?5:7)&&!boss.Attacking)
                    {finished=true;finishAt=simulated+.8;boss.decisionEnabled=false;}
                }
                arena.view.TickCamera((float)raw);
                var b=arena.boss;
                var camera=arena.view.GetComponent<Camera>();
                frames.Add(new Frame{wall=now-started,simulated=simulated,raw=raw,battle=arena.BattleTime,phase=b.CombatPhase,state=b.StateName,skill=b.ActionName,bossHealth=b.fighter.Health,playerHealth=arena.player.fighter.Health,sourceTime=b.SkillTime,patrolElapsed=b.PatrolPlanner.Elapsed,cameraOwned=arena.view.CinematicActive,invulnerable=b.fighter.Invulnerable,patrolInvalid=b.PatrolPlanner.Invalid,boss=b.transform.position,player=arena.player.transform.position,
                    cameraPosition=camera.transform.position,cameraRotation=camera.transform.rotation,cameraFov=camera.fieldOfView,
                    playerViewport=camera.WorldToViewportPoint(arena.player.transform.position+Vector3.up),
                    bossViewport=camera.WorldToViewportPoint(b.transform.position+Vector3.up*1.6f),
                    hazardViewport=camera.WorldToViewportPoint(b.GroundCentre),returnBlend=arena.view.CinematicReturnWeight,
                    hazardFraming=arena.view.HazardFramingWeight,groupDistance=arena.view.GroupFramingDistance,width=Screen.width,height=Screen.height});
                if(finished&&simulated>=finishAt){Save();Application.Quit(0);}
                if(simulated>125)throw new InvalidOperationException("Default two-phase pool did not finish: "+b.StateName);
            }
            catch(Exception e){Fail(e);}
        }
        private void OnGUI()
        {
            if(style==null)style=new GUIStyle(GUI.skin.label){fontSize=17,normal={textColor=Color.white}};
            GUI.Label(new Rect(24,230,1600,88),"DEFAULT TWO-PHASE BOSS AI / normal wall clock\n"+(movingPlayer?"Moving rectangle training player":"Stationary training player")+"; actual collisions; one labelled QA half-health hit.\nNative play, patrol cadence and uncapped performance are separate checks.",style);
        }
        private void Fail(Exception e){error=e.ToString();Debug.LogException(e);Save();Application.Quit(2);}
        private void Save()
        {
            if(saved||output==null)return;saved=true;
            File.WriteAllText(Path.Combine(output,"realtime_battle.json"),JsonUtility.ToJson(new Report{scope="NORMAL_CLOCK_DEFAULT_AI_"+(movingPlayer?"MOVING_RECTANGLE":"STATIONARY")+"_TRAINING_EXPLICIT_HALF_HIT_NOT_NATIVE_OR_PERFORMANCE",build=Application.version,error=error,complete=finished&&string.IsNullOrEmpty(error),cadencePassed=finished&&string.IsNullOrEmpty(error)&&!arena.Events.Any(e=>e.kind=="boss_patrol_invalid"),simulated=simulated,maxInterval=maxInterval,frames=frames.ToArray(),events=arena?.Events.ToArray(),patrol=arena?.boss.PatrolPlanner.Events.ToArray(),slowPatrolSearches=arena?.boss.PatrolPlanner.SlowSearchTimings.ToArray()},true));
        }
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
