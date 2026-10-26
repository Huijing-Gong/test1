using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SamProduction.BossArena
{
    public sealed class ArenaHistoricalRecoveryChecks : MonoBehaviour
    {
        [Serializable] private sealed class Frame
        {public double battle;public Vector3 boss,player;public float yaw,sourceTime;public string state,source;public bool invalid,recovery;public int searches,reconsider,completed;}
        [Serializable] private sealed class Report
        {public string scope="CONTROLLED_HISTORICAL_A32_ROOT_YAW_PLAYER_AND_OWN_CLASS_ENDPOINT_HISTORY_FIXTURE_REAL_DEFAULT_AI_NOT_NATIVE",build,error;
         public bool passed,initialPlanAccepted;public int searches,reconsiderations,recoverySteps,completed,releaseChecks,abortChecks;
         public double searchMilliseconds,maxSearchMilliseconds;public Frame[] frames;public ArenaEventRecord[] events;public ArenaPatrolPlanner.PatrolEvent[] patrol;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--sam-historical-recovery-checks")>=0)new GameObject("Historical real source recovery fixture").AddComponent<ArenaHistoricalRecoveryChecks>();}
        private IEnumerator Start()
        {
            yield return null;
            ArenaSession arena=null;string output=null,error=null;bool initial=false,passed=false;var frames=new List<Frame>();
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--sam-output");if(at<0)throw new Exception("Output required");
                output=Path.Combine(args[at+1],"historical_recovery.json");if(File.Exists(output))throw new IOException("Preserve existing evidence");Directory.CreateDirectory(args[at+1]);
                arena=FindFirstObjectByType<ArenaSession>();arena.automaticTick=false;arena.view.automaticTick=false;arena.Restart(true);
                Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;
                arena.boss.ResetBoss(new Vector3(.353439f,0,5.094340f),Quaternion.Euler(0,197.8989f,0));
                arena.player.ResetPlayer(new Vector3(.050612f,.015f,4.126653f),Quaternion.identity);
                arena.player.view=null;arena.player.fighter.TrainingInvincible=true;
                // Own-project fixture metadata only, exactly as reported in the
                // handoff: physically visited A, next fixed target B. No private
                // Unity API, source clock, health or transform overrides follow.
                Set(arena.boss.PatrolPlanner,"initialEndpointVerified",true);
                Set(arena.boss.PatrolPlanner,"<CompletedIntervals>k__BackingField",1);
                Set(arena.boss,"needsPatrol",false);Physics.SyncTransforms();
                initial=arena.boss.PatrolPlanner.Begin(arena);
            }
            catch(Exception e){error=e.ToString();}
            if(error==null)
            {
                for(int i=0;i<2700;i++)
                {
                    try
                    {
                        arena.Simulate(default,1f/60);arena.view.TickCamera(1f/60);
                        var p=arena.boss.PatrolPlanner;var f=arena.boss.FoundationMotor;
                        frames.Add(new Frame{battle=arena.BattleTime,boss=arena.boss.transform.position,player=arena.player.transform.position,yaw=arena.boss.transform.eulerAngles.y,
                            sourceTime=f.StateTime,state=arena.boss.StateName,source=f.ClipId,invalid=p.Invalid,recovery=p.RecoveryActive,searches=p.SearchCount,reconsider=p.Reconsiderations,completed=p.CompletedIntervals});
                        if(p.CompletedIntervals>1&&arena.Events.Exists(e=>e.kind=="ai_skill_selected")) {passed=true;break;}
                    }
                    catch(Exception e){error=e.ToString();break;}
                    yield return null;
                }
            }
            var planner=arena!=null?arena.boss.PatrolPlanner:null;var dash=arena!=null?arena.boss.GetComponent<ArenaDashMotor>():null;
            if(output!=null)File.WriteAllText(output,JsonUtility.ToJson(new Report{build=Application.version,error=error,passed=passed&&error==null,initialPlanAccepted=initial,
                searches=planner!=null?planner.SearchCount:0,reconsiderations=planner!=null?planner.Reconsiderations:0,recoverySteps=planner!=null?planner.RecoverySteps:0,
                completed=planner!=null?planner.CompletedIntervals:0,searchMilliseconds=planner!=null?planner.SearchMilliseconds:0,maxSearchMilliseconds=planner!=null?planner.MaximumSearchMilliseconds:0,
                releaseChecks=dash!=null?dash.ReleaseRecoveryChecks:0,abortChecks=dash!=null?dash.AbortRecoveryChecks:0,frames=frames.ToArray(),events=arena!=null?arena.Events.ToArray():null,patrol=planner!=null?new List<ArenaPatrolPlanner.PatrolEvent>(planner.Events).ToArray():null},true));
            Debug.Log("SAM_HISTORICAL_RECOVERY "+(passed&&error==null?"PASS":"FAIL"));Application.Quit(passed&&error==null?0:2);
        }
        private static void Set(object target,string name,object value)
        {var field=target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);if(field==null)throw new Exception("Own fixture field missing: "+name);field.SetValue(target,value);}
    }
}
