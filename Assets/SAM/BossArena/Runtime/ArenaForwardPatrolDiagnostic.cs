using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Explicit engineering search of the user's adjustable fixed endpoints.
    // This does not alter the saved layout or certify an executed patrol.
    public sealed class ArenaForwardPatrolDiagnostic : MonoBehaviour
    {
        [Serializable] private sealed class Row
        {
            public Vector3 a,b,player;
            public bool geometryFeasible,worldFeasible;
            public float length,duration;
            public string geometryReason,worldReason;
            public bool actualMotorComplete,releaseReturnFeasible,abortReturnFeasible;
            public string actualMotorFailure,releaseReturnReason,abortReturnReason;
            public float actualMaximumForwardAngle,actualMaximumAnkleError,actualMaximumBoneError;
            public Vector3 actualEnd,releaseReturnPrediction,abortReturnPrediction;
        }
        [Serializable] private sealed class Report
        {
            public string kind="actual_rig_forward_route_preflight_search_not_executed_visual_or_performance_acceptance";
            public Row[] candidates;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--sam-forward-route-probe")>=0)
                new GameObject("Forward-only patrol candidate search").AddComponent<ArenaForwardPatrolDiagnostic>();
        }
        private IEnumerator Start()
        {
            yield return null;
            int exit=0;
            try
            {
                var args=Environment.GetCommandLineArgs();int outputIndex=Array.IndexOf(args,"--sam-output");
                if(outputIndex<0)throw new InvalidOperationException("Explicit output required.");
                string output=Path.Combine(Path.GetFullPath(args[outputIndex+1]),"forward_route_probe.json");
                if(File.Exists(output))throw new IOException("Preserve earlier diagnostics.");
                var arena=FindFirstObjectByType<ArenaSession>();
                arena.automaticTick=false;arena.view.automaticTick=false;
                arena.boss.decisionEnabled=false;
                var rows=new List<Row>();
                Vector3 a=new Vector3(0,0,4);
                foreach(Vector3 b in new[]{new Vector3(3,0,4),new Vector3(0,0,1),new Vector3(0,0,0),
                    new Vector3(-2,0,1),new Vector3(2,0,1),new Vector3(0,0,-1)})
                foreach(Vector3 player in new[]{new Vector3(1.5f,.05f,1),new Vector3(2.5f,.05f,0),
                    new Vector3(-2.5f,.05f,0),new Vector3(0,.05f,-3)})
                {
                    arena.bossStart=a;arena.playerStart=player;arena.Restart(true);
                    arena.boss.decisionEnabled=false;
                    arena.boss.ResetBoss(a,Quaternion.Euler(0,180,0));
                    Physics.SyncTransforms();
                    var planner=arena.boss.PatrolPlanner;planner.pointA=a;planner.pointB=b;
                    var motor=planner.Motor;
                    var row=new Row{a=a,b=b,player=player};
                    row.geometryFeasible=motor.TryPath(a,Quaternion.Euler(0,180,0),a,b,player,0,1,false,
                        out var path,out row.geometryReason,false);
                    if(row.geometryFeasible)
                    {
                        row.length=path.length;row.duration=path.duration;
                        row.worldFeasible=motor.TryPath(a,Quaternion.Euler(0,180,0),a,b,player,0,1,false,
                            out path,out row.worldReason,true);
                        if(row.worldFeasible)
                        {
                            bool begun=planner.Begin(arena);
                            for(int tick=0;begun&&motor.Active&&tick<800;tick++)
                            {
                                Vector3 before=arena.boss.transform.position;
                                planner.Tick(1f/120,arena);
                                Vector3 delta=Vector3.ProjectOnPlane(arena.boss.transform.position-before,Vector3.up);
                                if(delta.magnitude>.0001f)row.actualMaximumForwardAngle=Mathf.Max(row.actualMaximumForwardAngle,
                                    Vector3.Angle(delta,arena.boss.transform.forward));
                            }
                            row.actualMotorComplete=motor.Complete;row.actualMotorFailure=motor.Failure;
                            row.actualMaximumAnkleError=motor.MaximumAnkleError;
                            row.actualMaximumBoneError=motor.MaximumBoneLengthError;
                            row.actualEnd=arena.boss.transform.position;
                            if(motor.Complete)
                            {
                                var heading=arena.boss.transform.rotation;var dash=arena.boss.DashMotor;
                                row.releaseReturnPrediction=row.actualEnd+heading*(dash.clips.Motion("dash_lock_release").EndPosition+dash.clips.Motion("dash_recover").EndPosition);
                                row.abortReturnPrediction=row.actualEnd+heading*dash.clips.Motion("dash_abort_recover").EndPosition;
                                row.releaseReturnFeasible=planner.CanPlan(row.releaseReturnPrediction,heading,player,true);
                                row.releaseReturnReason=planner.LastPredictionFailure;
                                row.abortReturnFeasible=planner.CanPlan(row.abortReturnPrediction,heading,player,true);
                                row.abortReturnReason=planner.LastPredictionFailure;
                            }
                        }
                    }
                    rows.Add(row);
                }
                File.WriteAllText(output,JsonUtility.ToJson(new Report{candidates=rows.ToArray()},true));
                Debug.Log("FORWARD_ROUTE_PROBE candidates="+rows.Count);
            }
            catch(Exception e){Debug.LogException(e);exit=1;}
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(exit);
#else
            Application.Quit(exit);
#endif
        }
    }
}
