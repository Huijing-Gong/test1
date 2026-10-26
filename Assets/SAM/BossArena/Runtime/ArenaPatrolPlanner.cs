using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace SamProduction.BossArena
{
    // Endpoint history is physical history. A prediction never changes the
    // remembered endpoint, and a partial approach never toggles A/B.
    public sealed class ArenaPatrolPlanner : MonoBehaviour
    {
        [Serializable] public sealed class SearchTiming
        {
            public double realtime, milliseconds;
            public bool accepted, checkWorld, exhaustive;
            public Vector3 origin, player, endpoint;
            public string clip;
            public float prefixYaw, suffixYaw;
        }
        private static readonly ProfilerMarker SearchMarker = new ProfilerMarker("SAM.PatrolCandidate");
        private readonly List<SearchTiming> slowSearchTimings = new List<SearchTiming>();
        public IReadOnlyList<SearchTiming> SlowSearchTimings => slowSearchTimings;
        public int SearchCount { get; private set; }
        public double SearchMilliseconds { get; private set; }
        public double MaximumSearchMilliseconds { get; private set; }
        [Serializable] public struct Operation { public bool walk, right; public int value; public float duration; }
        [Serializable] public sealed class Plan
        {
            public Vector3 origin, endpoint, target, returnPoint;
            public float duration, finalYaw, targetProgress, pathLength;
            public int targetIndex, returnIndex;
            public bool includesReturn;
            public Operation[] operations = Array.Empty<Operation>();
            public ArenaPatrolPath path;
        }
        [Serializable] public sealed class PatrolEvent
        {
            public string kind, detail;
            public string clipId;
            public float duration;
            public int stepCount, attemptId, nextEndpointIndex;
            public double battleTime;
            public float elapsed, remaining, actualPathLength, headingYaw;
            public int operation, targetIndex, actualEndpointIndex;
            public Vector3 actor, target, expectedEndpoint;
        }
        public Vector3 pointA = new Vector3(0, 0, 4), pointB = new Vector3(3, 0, 4);
        [Tooltip("Numerical collision tolerance at an exact endpoint, not an approach radius.")]
        public float endpointTolerance = .005f;
        public float maximumRouteDistance = 2.5f;
        public float finalFacingTolerance = 12;
        public bool Available => motor != null && motor.Available;
        public string UnavailableReason => motor == null ? "Patrol18 motor and independent source are not bound." : motor.Failure;
        public bool Active { get; private set; }
        public bool Complete { get; private set; }
        public bool Invalid { get; private set; }
        public string Failure { get; private set; } = "";
        public string LastPredictionFailure { get; private set; } = "";
        public float Elapsed { get; private set; }
        public float Remaining => Mathf.Max(0,6-Elapsed);
        public float PlannedRemaining => CurrentPlan == null ? 0 : Mathf.Max(0,CurrentPlan.duration-Elapsed);
        public double StartBattleTime { get; private set; }
        public double EndBattleTime { get; private set; }
        public Vector3 ActualStart { get; private set; }
        public Vector3 ActualEnd { get; private set; }
        public float ActualPathLength { get; private set; }
        public bool TargetReached { get; private set; }
        public int LastActualEndpointIndex { get; private set; } = 0;
        public int CompletedIntervals { get; private set; }
        public int AttemptId { get; private set; }
        public int InvalidAttempts { get; private set; }
        public int Reconsiderations { get; private set; }
        public int TargetIndex => 1-LastActualEndpointIndex;
        public Vector3 Target => TargetIndex == 0 ? pointA : pointB;
        public Plan CurrentPlan { get; private set; }
        public IReadOnlyList<PatrolEvent> Events => events;
        public ArenaPatrolMotor Motor => motor;
        private ArenaBossBrain owner;
        private ArenaFoundationMotor foundation;
        private ArenaPatrolMotor motor;
        private bool startedClock, sawReturn, sawFinal, prefixActive;
        private double prefixElapsed, suffixElapsed;
        private bool suffixActive, suffixFinished;
        private bool initialEndpointVerified, invalidCounted;
        private double lastReconsiderTime;
        private Vector3 lastReconsiderPlayer;
        private Quaternion lastReconsiderHeading;
        private Vector3 lastRejectedOrigin, lastRejectedPlayer;
        private Quaternion lastRejectedHeading;
        private bool hasRejectedSnapshot;
        public bool RecoveryActive => recoveryStage != 0;
        public int RecoverySteps { get; private set; }
        public string RecoveryFailure { get; private set; } = "";
        private int recoveryStage, recoveryStepsThisAttempt;
        private double recoveryStarted, recoveryStageStarted;
        private Vector3 recoveryExpectedEnd;
        private Vector3 recoveryBudgetPlayer;
        private static readonly bool detailedFailureDiagnostics=Array.IndexOf(Environment.GetCommandLineArgs(),"--sam-patrol-failure-details")>=0;
        private string lastReconsiderFailure=null;
        private float recentStopFraction = -1;
        private bool recentThroughEndpoint;
        private readonly List<PatrolEvent> events = new List<PatrolEvent>();
        public void Initialize(ArenaBossBrain boss)
        {
            owner=boss;foundation=boss.FoundationMotor;motor=GetComponent<ArenaPatrolMotor>();
            motor?.Initialize(boss);
        }
        public void ResetPlanner()
        {
            Active=Complete=Invalid=TargetReached=startedClock=sawReturn=sawFinal=prefixActive=false;
            prefixElapsed=suffixElapsed=0;suffixActive=suffixFinished=false;
            Elapsed=ActualPathLength=0;StartBattleTime=EndBattleTime=0;CurrentPlan=null;Failure="";
            ActualStart=ActualEnd=owner.transform.position;LastActualEndpointIndex=0;CompletedIntervals=0;
            AttemptId=InvalidAttempts=Reconsiderations=0;initialEndpointVerified=invalidCounted=false;
            lastReconsiderTime=double.NegativeInfinity;lastReconsiderPlayer=Vector3.positiveInfinity;lastReconsiderHeading=owner.transform.rotation;
            events.Clear();motor?.ResetMotor();
            recentStopFraction=-1; recentThroughEndpoint=false;lastReconsiderFailure=null;
            hasRejectedSnapshot=false;
            recoveryStage=recoveryStepsThisAttempt=RecoverySteps=0;RecoveryFailure="";
            SearchCount=0;SearchMilliseconds=MaximumSearchMilliseconds=0;slowSearchTimings.Clear();
        }
        public float DistanceFromRoute(Vector3 position)
        {
            Vector3 a=Vector3.ProjectOnPlane(pointA,Vector3.up),line=Vector3.ProjectOnPlane(pointB-pointA,Vector3.up),p=Vector3.ProjectOnPlane(position,Vector3.up);
            float t=line.sqrMagnitude>.0001f?Mathf.Clamp01(Vector3.Dot(p-a,line)/line.sqrMagnitude):0;
            return Vector3.Distance(p,a+line*t);
        }
        public bool CanPlan(Vector3 origin,Quaternion heading,Vector3 player,bool checkWorld=true,bool exhaustive=true)
        {
            bool result=Candidate(origin,heading,player,checkWorld,out _,out string reason,exhaustive);
            LastPredictionFailure=result?"":reason;
#if UNITY_EDITOR
            if(!result)Debug.Log("SAM_PATROL_PLAN_REJECT origin="+origin.ToString("F6")+" yaw="+heading.eulerAngles.y.ToString("F6")
                +" player="+player.ToString("F6")+" prior="+LastActualEndpointIndex+" reason="+reason);
#endif
            return result;
        }
        public bool TryPlan(Vector3 origin,Quaternion heading,Vector3 player,out Plan result,bool checkWorld=true)
        {
            result=null;
            if(!Candidate(origin,heading,player,checkWorld,out var path,out _))return false;
            result=Describe(path);return true;
        }
        private bool Candidate(Vector3 origin,Quaternion heading,Vector3 player,bool checkWorld,out ArenaPatrolPath path,out string reason,bool exhaustive=true)
        {
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            bool ok;
            using(SearchMarker.Auto()) ok=CandidateCore(origin,heading,player,checkWorld,out path,out reason,exhaustive);
            double milliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency;
            SearchCount++;SearchMilliseconds+=milliseconds;MaximumSearchMilliseconds=Math.Max(MaximumSearchMilliseconds,milliseconds);
            // A bounded diagnostic record, never a time budget or an early return.
            // Keep all geometry, collision and source-selection results identical.
            if(milliseconds>8 && slowSearchTimings.Count<2048)
                slowSearchTimings.Add(new SearchTiming{realtime=Time.realtimeSinceStartupAsDouble,milliseconds=milliseconds,
                    accepted=ok,checkWorld=checkWorld,exhaustive=exhaustive,origin=origin,player=player,endpoint=path.endpoint,
                    clip=path.clipId,prefixYaw=path.prefixYaw,suffixYaw=path.suffixYaw});
            return ok;
        }
        private bool CandidateCore(Vector3 origin,Quaternion heading,Vector3 player,bool checkWorld,out ArenaPatrolPath path,out string reason,bool exhaustive)
        {
            path=default;reason="";
            if(!Available){reason=UnavailableReason;return false;}
            Vector3 prior=LastActualEndpointIndex==0?pointA:pointB,next=LastActualEndpointIndex==0?pointB:pointA;
            float routeDistance=DistanceFromRoute(origin);
            if(routeDistance>maximumRouteDistance){reason="Actual root lies beyond the forward return-to-route trial domain.";return false;}
            Vector3 line=next-prior;
            float progress=Mathf.Clamp01(Vector3.Dot(origin-prior,line)/line.sqrMagnitude);
            string failures="";
            // A large heading change near the remembered endpoint usually needs
            // a complete turn on each side of the short forward walk. Try the
            // geometry-informed order before enumerating longer walking curves.
            // These are fresh full contact/world preflights, never cached passes.
            float routeYaw=Vector3.SignedAngle(heading*Vector3.forward,
                Vector3.ProjectOnPlane(next-origin,Vector3.up),Vector3.up);
            if(owner.ProductionAI && progress<.15f && Mathf.Abs(routeYaw)>60
                && motor.clips.HasVariant(ArenaPatrolClips.FourShortId))
            {
                int sign=routeYaw>0?1:-1;
                foreach(float fraction in new[]{.4f,.6f,.8f,1f})
                {
                    int before=45*sign,after=-90*sign;
                    float first=foundation.TurnDuration(45,sign>0);
                    float last=foundation.TurnDuration(90,sign<0);
                    if(first<=0 || last<=0 || first+last+2.4f>6.00001f
                        || first+last+2.4f<3.99999f)continue;
                    Vector3 stop=Vector3.Lerp(prior,next,Mathf.Lerp(progress,1,fraction));
                    Vector3 direction=Vector3.ProjectOnPlane(player-stop,Vector3.up);
                    if(!WorthTryingStop(origin,stop,next) || direction.sqrMagnitude<.001f)continue;
                    Quaternion walkEnd=Quaternion.LookRotation(direction)*Quaternion.Euler(0,-after,0);
                    Vector3 orientationPoint=stop+walkEnd*Vector3.forward*5;
                    if(motor.TryPath(origin,heading*Quaternion.Euler(0,before,0),prior,stop,orientationPoint,
                        LastActualEndpointIndex,1-LastActualEndpointIndex,false,out path,out reason,
                        checkWorld,false,ArenaPatrolClips.FourShortId))
                    {path.prefixYaw=before;path.prefixDuration=first;path.suffixYaw=after;path.suffixDuration=last;return true;}
                }
            }
            // Nearby tracking predictions usually need the same stop fraction.
            // This changes search order only: recompute the current endpoint,
            // all contacts, reachability and current world collisions every time.
            if(recentStopFraction>0)
            {
                Vector3 stop=recentThroughEndpoint?Vector3.Lerp(next,prior,recentStopFraction)
                    :Vector3.Lerp(prior,next,Mathf.Lerp(progress,1,recentStopFraction));
                if((recentThroughEndpoint||WorthTryingStop(origin,stop,next))
                    &&motor.TryPath(origin,heading,recentThroughEndpoint?next:prior,stop,player,
                        recentThroughEndpoint?1-LastActualEndpointIndex:LastActualEndpointIndex,
                        recentThroughEndpoint?LastActualEndpointIndex:1-LastActualEndpointIndex,
                        recentThroughEndpoint,out path,out reason,checkWorld,
                        recentThroughEndpoint||CompletedIntervals>0||routeDistance>endpointTolerance))return true;
            }
            // Q15: retain the target endpoint across attacks. A complete 4-6s
            // action may finish partway along AB; it need not revisit the prior
            // endpoint or traverse all of AB in every attack interval.
            foreach(float fraction in new[]{1f,.8f,.6f,.4f})
            {
                Vector3 stop=Vector3.Lerp(prior,next,Mathf.Lerp(progress,1,fraction));
                if(!WorthTryingStop(origin,stop,next))continue;
                bool eight=CompletedIntervals>0||routeDistance>endpointTolerance;
                if(motor.TryPath(origin,heading,prior,stop,player,LastActualEndpointIndex,1-LastActualEndpointIndex,
                    false,out path,out reason,checkWorld,eight))
                {recentStopFraction=fraction;recentThroughEndpoint=false;return true;}
                failures+="stop fraction "+fraction+": "+reason+"; ";
            }
            // When close to the target endpoint, use it as a real mid-action
            // waypoint and continue the return leg within the same time budget.
            // This avoids inventing a tiny eight-step shuffle near an endpoint.
            foreach(float fraction in new[]{.4f,.6f,.8f})
            {
                Vector3 stop=Vector3.Lerp(next,prior,fraction);
                if(motor.TryPath(origin,heading,next,stop,player,1-LastActualEndpointIndex,LastActualEndpointIndex,
                    true,out path,out reason,checkWorld,true))
                {recentStopFraction=fraction;recentThroughEndpoint=true;return true;}
                failures+="through endpoint, return fraction "+fraction+": "+reason+"; ";
            }
            // Landing proposals need a conservative quick feasibility answer.
            // Reject a proposal rather than exhaust all turn pairs inside an
            // airborne display frame. Accepted candidates retain full checks.
            if(!exhaustive){reason=failures;return false;}
            if(owner.ProductionAI&&motor.clips.HasVariant(ArenaPatrolClips.FourShortId))
            {
                foreach(int yaw in new[]{90,-90,45,-45,180,-180,15,-15})
                {
                    float turnDuration=foundation.TurnDuration(Mathf.Abs(yaw),yaw>0);
                    if(turnDuration<=0||turnDuration+2.4f<4||turnDuration+2.4f>6.00001f)continue;
                    Quaternion after=heading*Quaternion.Euler(0,yaw,0);
                    foreach(float fraction in new[]{1f,.8f,.6f,.4f})
                    {
                        Vector3 stop=Vector3.Lerp(prior,next,Mathf.Lerp(progress,1,fraction));
                        if(!WorthTryingStop(origin,stop,next))continue;
                        if(motor.TryPath(origin,after,prior,stop,player,LastActualEndpointIndex,1-LastActualEndpointIndex,
                            false,out path,out reason,checkWorld,false,ArenaPatrolClips.FourShortId))
                        {path.prefixYaw=yaw;path.prefixDuration=turnDuration;return true;}
                    }
                }
            }
            if(owner.ProductionAI&&motor.clips.HasVariant(ArenaPatrolClips.FourShortId))
            {
                foreach(int before in new[]{0,15,-15,45,-45,90,-90})
                foreach(int after in new[]{15,-15,45,-45,90,-90})
                {
                    float first=before==0?0:foundation.TurnDuration(Mathf.Abs(before),before>0);
                    float last=foundation.TurnDuration(Mathf.Abs(after),after>0);
                    if(first+last+2.4f<4-.00001f||first+last+2.4f>6+.00001f)continue;
                    foreach(float fraction in new[]{1f,.8f,.6f,.4f})
                    {
                        Vector3 stop=Vector3.Lerp(prior,next,Mathf.Lerp(progress,1,fraction));
                        if(!WorthTryingStop(origin,stop,next))continue;
                        Vector3 direction=Vector3.ProjectOnPlane(player-stop,Vector3.up);
                        if(direction.sqrMagnitude<.001f)continue;
                        Quaternion walkEnd=Quaternion.LookRotation(direction)*Quaternion.Euler(0,-after,0);
                        // This point specifies only the walking segment's final heading;
                        // all collision checks still use the actual physics world.
                        Vector3 orientationPoint=stop+walkEnd*Vector3.forward*5;
                        if(motor.TryPath(origin,heading*Quaternion.Euler(0,before,0),prior,stop,orientationPoint,
                            LastActualEndpointIndex,1-LastActualEndpointIndex,false,out path,out reason,checkWorld,false,ArenaPatrolClips.FourShortId))
                        {path.prefixYaw=before;path.prefixDuration=first;path.suffixYaw=after;path.suffixDuration=last;return true;}
                    }
                }
            }
            reason=failures+"; complete composed four-step fallback: "+reason;return false;
        }
        // A short straight-line distance is not a short authored walk: a full
        // forward arc may return to a nearby endpoint after several large steps.
        // Only the exact outstanding fixed endpoint gets this search exception.
        // Mid-route stops keep the original 0.4 m prune; TryPath still enforces
        // every original path length, curvature, foot/leg and collision gate.
        private bool WorthTryingStop(Vector3 origin,Vector3 stop,Vector3 next)
        {
            float distance=Vector3.Distance(stop,origin);
            return distance>=.4f || (owner.ProductionAI && distance>endpointTolerance
                && (stop-next).sqrMagnitude<1e-10f);
        }
        private static Plan Describe(ArenaPatrolPath path) => new Plan
        {
            origin=path.origin,endpoint=path.endpoint,target=path.endpoint,returnPoint=path.returnPoint,
            duration=path.duration+path.prefixDuration+path.suffixDuration,finalYaw=path.finalHeading.eulerAngles.y,pathLength=path.length,
            targetProgress=Vector3.Distance(path.origin,path.endpoint),targetIndex=path.targetIndex,returnIndex=path.returnIndex,
            includesReturn=path.hasReturn,path=path
        };
        public bool Begin(ArenaSession arena) => BeginPrepared(arena,false,default);
        private bool BeginPrepared(ArenaSession arena,bool prepared,ArenaPatrolPath path)
        {
            Active=Complete=Invalid=TargetReached=startedClock=sawReturn=sawFinal=prefixActive=false;
            prefixElapsed=suffixElapsed=0;suffixActive=suffixFinished=false;
            AttemptId++;invalidCounted=false;lastReconsiderFailure=null;
            Failure="";Elapsed=ActualPathLength=0;ActualStart=ActualEnd=owner.transform.position;
            StartBattleTime=arena.BattleTime;EndBattleTime=0;CurrentPlan=null;
            // The first interval really starts at A. Subsequent intervals must
            // return to their remembered reached endpoint before crossing AB.
            if(!initialEndpointVerified&&Vector3.ProjectOnPlane(ActualStart-pointA,Vector3.up).magnitude>endpointTolerance)
            {Invalidate(arena,"Initial patrol does not actually start at fixed A.");return false;}
            initialEndpointVerified=true;
            // A reconsideration has just completed the same full synchronous
            // world/contact preflight. No simulation or transform mutation occurs
            // before this call, so consume that result instead of solving twice.
            if(!prepared&&!Candidate(ActualStart,owner.transform.rotation,arena.player.transform.position,true,out path,out string reason,!owner.ProductionAI))
            {
                // No path or motion was started: preserve the rejection as a
                // planning failure, rather than report a failed executed interval.
                Invalid=true;Complete=Active=false;Failure=reason;
                recoveryStage=recoveryStepsThisAttempt=0;RecoveryFailure="";recoveryStarted=arena.BattleTime;
                recoveryBudgetPlayer=arena.player.transform.position;
                hasRejectedSnapshot=true;lastRejectedOrigin=ActualStart;lastRejectedPlayer=arena.player.transform.position;
                lastRejectedHeading=owner.transform.rotation;lastReconsiderFailure=reason;
                lastReconsiderTime=arena.BattleTime;
                Record(arena,"boss_patrol_plan_unavailable",reason);return false;
            }
            CurrentPlan=Describe(path);
            if(path.prefixDuration>0)
            {
                if(!foundation.TryTurn(Mathf.Abs(path.prefixYaw),path.prefixYaw>0)){Invalidate(arena,"Complete prefix turn refused");return false;}
                prefixActive=true;
            }
            else if(!motor.Begin(path,arena)){Invalidate(arena,motor.Failure);Active=motor.Active;return false;}
            Active=true;
            if(Vector3.ProjectOnPlane(ActualStart-(LastActualEndpointIndex==0?pointA:pointB),Vector3.up).magnitude<=endpointTolerance)
            {
                Record(arena,"boss_patrol_endpoint_reached","Actual interval start is the exact prior fixed endpoint.",LastActualEndpointIndex);
            }
            Record(arena,"boss_patrol_started",path.clipId+" complete "+path.stepCount+"-step / "+path.duration+"s path including actual return and final facing.");
            return true;
        }
        public void Tick(float delta,ArenaSession arena)
        {
            if(!Active)return;
            if(!startedClock){StartBattleTime=arena.BattleTime-delta;startedClock=true;}
            if(prefixActive)
            {
                float part=(float)Math.Min(delta,Math.Max(0,CurrentPlan.path.prefixDuration-prefixElapsed));
                foundation.Tick(part,arena);prefixElapsed+=part;Elapsed=(float)prefixElapsed;delta-=part;
                if(prefixElapsed<CurrentPlan.path.prefixDuration-.000001f)return;
                prefixActive=false;
                if(Invalid){Active=false;return;}
                if(!foundation.IsGuard||Quaternion.Angle(owner.transform.rotation,CurrentPlan.path.initialHeading)>.05f)
                {Invalidate(arena,"Complete prefix did not reach authored guard/heading");return;}
                if(!motor.Begin(CurrentPlan.path,arena)){Invalidate(arena,motor.Failure);return;}
                Record(arena,"boss_patrol_prefix_complete","Full authored turn completed; begin full four-step source without retiming.");
            }
            if(suffixActive)
            {
                float part=(float)Math.Min(delta,Math.Max(0,CurrentPlan.path.suffixDuration-suffixElapsed));
                foundation.Tick(part,arena);suffixElapsed+=part;
                Elapsed=(float)(prefixElapsed+motor.Elapsed+suffixElapsed);
                if(suffixElapsed<CurrentPlan.path.suffixDuration-.000001f)return;
                suffixActive=false;suffixFinished=true;
                if(Invalid){Active=false;return;}
                if(!foundation.IsGuard){Invalidate(arena,"Final full turn did not reach guard");return;}
                Record(arena,"boss_patrol_suffix_complete","Complete authored final turn remains inside the same interval.");
            }
            else motor.Tick(delta,arena);
            if(motor.Active&&CurrentPlan.path.forwardCurve!=motor.Path.forwardCurve)CurrentPlan=Describe(motor.Path);
            Elapsed=(float)(prefixElapsed+motor.Elapsed+suffixElapsed);ActualEnd=owner.transform.position;ActualPathLength=motor.ActualPathLength;
            if(motor.ReturnEndpointReached&&!sawReturn)
            {
                sawReturn=true;
                LastActualEndpointIndex=CurrentPlan.returnIndex;
                Record(arena,"boss_patrol_endpoint_reached","Actual intermediate fixed endpoint reached; direction reverses within this complete interval.",CurrentPlan.returnIndex);
            }
            if(motor.FinalEndpointReached&&!sawFinal)
            {
                sawFinal=TargetReached=true;
                // Physical endpoint history survives a later target-facing or
                // duration failure. It is never inferred from a prediction.
                Vector3 fixedTarget=CurrentPlan.targetIndex==0?pointA:pointB;
                if(Vector3.ProjectOnPlane(ActualEnd-fixedTarget,Vector3.up).magnitude<=endpointTolerance)
                {
                    LastActualEndpointIndex=CurrentPlan.targetIndex;
                    Record(arena,"boss_patrol_endpoint_reached","Actual fixed endpoint reached; next interval reverses patrol direction.",CurrentPlan.targetIndex);
                }
                else Record(arena,"boss_patrol_midroute_stop","Complete forward steps stopped on AB; retain the same endpoint target for the next interval.");
            }
            if(motor.State==ArenaPatrolMotionState.Settling)
            {
                if(!Invalid){Invalid=true;Failure=motor.Failure;CountInvalid();Record(arena,"boss_patrol_invalid",Failure);}
                return;
            }
            if(motor.Invalid){Invalidate(arena,motor.Failure);return;}
            if(!motor.Complete)return;
            if(CurrentPlan.path.suffixDuration>0&&!suffixFinished)
            {
                if(!foundation.TryTurn(Mathf.Abs(CurrentPlan.path.suffixYaw),CurrentPlan.path.suffixYaw>0))
                {Invalidate(arena,"Complete suffix turn refused");return;}
                suffixActive=true;return;
            }
            if(Elapsed<4-.00001f||Elapsed>6+.00001f||!TargetReached)
            {Invalidate(arena,"Actual complete-source interval failed duration or mandatory endpoint coverage.");return;}
            // The live target can move after the final foot placements have
            // committed. Record that error honestly; never rotate planted boots.
            float liveFacing=Vector3.Angle(owner.transform.forward,Vector3.ProjectOnPlane(arena.player.transform.position-owner.transform.position,Vector3.up));
            if(liveFacing>finalFacingTolerance && !owner.ProductionAI)
            {Invalidate(arena,"Live target moved outside the completed final-foot facing tolerance: "+liveFacing);return;}
            if(liveFacing>finalFacingTolerance)
                Record(arena,"boss_patrol_target_moved","Complete planted route retained; live target error="+liveFacing+". Production skill tracks only in its authored mobile phase, without another patrol turn.");
            CompletedIntervals++;
            Complete=true;Active=false;EndBattleTime=arena.BattleTime;
            Record(arena,"boss_patrol_completed","Actual forward route/turn completed inside 4-6s; endpoint target persists at a midroute stop.");
        }
        public void Cancel(ArenaSession arena,string reason,bool dead=false)
        {
            if(!Active||(Invalid&&!dead))return;
            if(prefixActive||suffixActive)
            {
                if(dead){foundation.BeginDeath();prefixActive=suffixActive=false;Active=false;}
                else foundation.RequestStop();
            }
            else motor?.Cancel(reason,dead);Invalid=true;Failure=reason;CountInvalid();Record(arena,"boss_patrol_invalid",reason);
        }
        public void Invalidate(ArenaSession arena,string reason)
        {
            recoveryStarted=arena.BattleTime;recoveryStage=recoveryStepsThisAttempt=0;RecoveryFailure="";
            recoveryBudgetPlayer=arena.player.transform.position;
            Invalid=true;Complete=false;Failure=reason;EndBattleTime=arena.BattleTime;ActualEnd=owner.transform.position;
            CountInvalid();
            Active=motor!=null&&motor.Active;
            if(Active)motor.Cancel(reason,owner.fighter.Dead);else foundation.RequestStop();
            Record(arena,"boss_patrol_invalid",reason);
        }
        private void CountInvalid(){if(!invalidCounted){invalidCounted=true;InvalidAttempts++;}}
        // Recheck a changing target or removed obstruction. A failed attempt's
        // elapsed time, events and counter remain evidence; retrying never
        // converts its unfinished path into a completed interval.
        public bool TryReconsider(ArenaSession arena)
        {
            if(!Invalid||Active||!Available||!foundation.IsGuard||!arena.Active||owner.fighter.Dead)return false;
            if(recoveryStage==1)
            {
                recoveryStage=0;
                float distance=foundation.StraightSegmentDistance(0);
                Vector3 proposed=owner.transform.position+owner.transform.forward*distance;
                if(DistanceFromRoute(proposed)<=maximumRouteDistance && foundation.BeginWalkSegment(0))
                {
                    recoveryStage=2;recoveryStageStarted=arena.BattleTime;
                    recoveryExpectedEnd=proposed;
                    Record(arena,"boss_patrol_recovery_walk_started","Full authored start/stop egress, outside any patrol interval; route and endpoint history are unchanged.");
                    return false;
                }
                RecoveryFailure="Reserved recovery walk obstructed or outside route domain.";
                Record(arena,"boss_patrol_recovery_walk_rejected",RecoveryFailure);
            }
            else if(recoveryStage==2)
            {
                recoveryStage=0;lastReconsiderTime=double.NegativeInfinity;hasRejectedSnapshot=false;
                bool full=Vector3.ProjectOnPlane(owner.transform.position-recoveryExpectedEnd,Vector3.up).magnitude<.02f;
                Record(arena,full?"boss_patrol_recovery_walk_complete":"boss_patrol_recovery_walk_interrupted",
                    full?"Actual authored walk reached guard; re-evaluate a fresh full-contact patrol from this real root.":"Actual collision stopped the reserved egress; Foundation settled before a fresh path check. No complete egress or endpoint is inferred.");
            }
            else if(recoveryStage==3)
            {recoveryStage=0;Record(arena,"boss_patrol_recovery_failed",RecoveryFailure);return false;}
            Vector3 player=arena.player.transform.position;
            if((recoveryStepsThisAttempt>=3||arena.BattleTime-recoveryStarted>15)
                && (player-recoveryBudgetPlayer).sqrMagnitude>.0625f)
            {recoveryStepsThisAttempt=0;recoveryStarted=arena.BattleTime;recoveryBudgetPlayer=player;RecoveryFailure="";}
            // Changing player input cannot trigger an exhaustive search every
            // 120Hz substep. Recovery stays on the authored guard/turn motor.
            if(arena.BattleTime-lastReconsiderTime<1)return false;
            lastReconsiderTime=arena.BattleTime;lastReconsiderPlayer=player;lastReconsiderHeading=owner.transform.rotation;Reconsiderations++;
            Vector3 direction=Vector3.ProjectOnPlane(player-owner.transform.position,Vector3.up);
            float angle=direction.sqrMagnitude>.001f?Vector3.SignedAngle(owner.transform.forward,direction,Vector3.up):0;
            if(TargetReached&&motor.Complete)
            {
                if(Mathf.Abs(angle)<=finalFacingTolerance)
                {
                    Invalid=false;Complete=true;
                    Record(arena,"boss_patrol_endpoint_reconsidered","Retry charge from the same physically completed endpoint; previous invalid attempt remains recorded.");
                    return true;
                }
            }
            else
            {
                bool sameRejected=hasRejectedSnapshot
                    && (owner.transform.position-lastRejectedOrigin).sqrMagnitude<.0001f
                    && (player-lastRejectedPlayer).sqrMagnitude<.0625f
                    && Quaternion.Angle(owner.transform.rotation,lastRejectedHeading)<3;
                ArenaPatrolPath retryPath=default;
                string reason=lastReconsiderFailure;
                if(!sameRejected && Candidate(owner.transform.position,owner.transform.rotation,player,true,out retryPath,out reason,!owner.ProductionAI))
                {
                    hasRejectedSnapshot=false;
                    Record(arena,"boss_patrol_retry_started","Start a new complete attempt after the prior invalid attempt; its elapsed record is retained.");
                    return BeginPrepared(arena,true,retryPath);
                }
                if(!sameRejected)
                {
                    hasRejectedSnapshot=true;lastRejectedOrigin=owner.transform.position;
                    lastRejectedPlayer=player;lastRejectedHeading=owner.transform.rotation;
                }
                ObserveRetryRejection(arena,reason);
                // A close stationary target can defeat every complete return
                // curve without requiring a yaw correction. Leave that failed
                // snapshot with an ordinary full authored turn and gait, never
                // a root snap, partial clip, scaled boot or inferred endpoint.
                if(owner.ProductionAI)
                {
                    if(TryRecoveryStep(arena,player))return false;
                    if(recoveryStepsThisAttempt>=3||arena.BattleTime-recoveryStarted>15)return false;
                }
            }
            // A player behind the guard cannot strand AI forever. Any full
            // authored reorientation after the interval is explicitly recovery
            // outside that failed budget, never a successful 4-6s patrol.
            float absolute=Mathf.Abs(angle);
            if(absolute>finalFacingTolerance)
            {
                int turn=absolute>135?180:absolute>67.5f?90:absolute>30?45:15;
                bool right=angle>0;
                if(owner.ProductionAI&&arena.player.PlanarVelocity.sqrMagnitude>.04f)
                {
                    // Use measured movement, never the player's future input or
                    // test route. Score each complete source at its own end time
                    // so a moving player cannot perpetually outrun a stale aim.
                    float best=float.PositiveInfinity;
                    foreach(int candidate in new[]{15,-15,45,-45,90,-90,180,-180})
                    {
                        float duration=foundation.TurnDuration(Mathf.Abs(candidate),candidate>0);
                        if(duration<=0)continue;
                        Vector3 predicted=Vector3.ProjectOnPlane(player+arena.player.PlanarVelocity*duration-owner.transform.position,Vector3.up);
                        if(predicted.sqrMagnitude<.001f)continue;
                        Vector3 final=(owner.transform.rotation*Quaternion.Euler(0,candidate,0))*Vector3.forward;
                        float score=Vector3.Angle(final,predicted)+duration;
                        if(score<best){best=score;turn=Mathf.Abs(candidate);right=candidate>0;}
                    }
                }
                if(foundation.TryTurn(turn,right))
                    Record(arena,"boss_patrol_invalid_reorientation","Full Foundation turn "+turn+" degrees after a failed interval; outside its movement budget, not a patrol pass.");
            }
            return false;
        }
        private bool TryRecoveryStep(ArenaSession arena,Vector3 player)
        {
            if(recoveryStepsThisAttempt>=3||arena.BattleTime-recoveryStarted>15)
            {
                if(RecoveryFailure!="Recovery budget exhausted; waiting for changed target/world.")
                {RecoveryFailure="Recovery budget exhausted; waiting for changed target/world.";Record(arena,"boss_patrol_recovery_failed",RecoveryFailure);}
                return false;
            }
            float distance=foundation.StraightSegmentDistance(0),best=float.PositiveInfinity;
            int selected=0;
            foreach(int yaw in new[]{90,-90,45,-45,180})
            {
                if(foundation.TurnDuration(Mathf.Abs(yaw),yaw>0)<=0)continue;
                Vector3 end=owner.transform.position+(owner.transform.rotation*Quaternion.Euler(0,yaw,0))*Vector3.forward*distance;
                float route=DistanceFromRoute(end);if(route>maximumRouteDistance)continue;
                float clearance=Vector3.ProjectOnPlane(end-player,Vector3.up).magnitude;
                if(clearance<owner.capsule.radius+arena.player.capsule.radius+.15f)continue;
                float score=route+.2f*Vector3.Distance(end,Target)-.15f*clearance;
                if(score<best){best=score;selected=yaw;}
            }
            if(selected==0||!foundation.TryTurn(Mathf.Abs(selected),selected>0))return false;
            RecoveryFailure="";recoveryStage=1;recoveryStageStarted=arena.BattleTime;
            recoveryStepsThisAttempt++;RecoverySteps++;
            Record(arena,"boss_patrol_recovery_turn_started","Full authored "+selected+" degree turn before a reserved egress; failed interval remains failed and no fixed endpoint is claimed.");
            return true;
        }
        public void TickRecoveryWatchdog(ArenaSession arena)
        {
            if((recoveryStage==1||recoveryStage==2)&&arena.BattleTime-recoveryStageStarted>6)
            {
                RecoveryFailure="Recovery source failed to reach guard within six seconds; full source stop requested.";
                recoveryStage=3;foundation.RequestStop();
                Record(arena,"boss_patrol_recovery_timeout",RecoveryFailure);
            }
        }
        internal void ObserveRetryRejection(ArenaSession arena,string reason)
        {
            bool changed=!string.Equals(reason,lastReconsiderFailure,StringComparison.Ordinal);
            lastReconsiderFailure=reason;
            if(detailedFailureDiagnostics&&changed)
                Record(arena,"boss_patrol_retry_rejected",reason);
        }

        private void Record(ArenaSession arena,string kind,string detail,int actualIndex=-1)
        {
            events.Add(new PatrolEvent{kind=kind,detail=detail,battleTime=arena.BattleTime,elapsed=Elapsed,remaining=Remaining,
                clipId=CurrentPlan==null?"":CurrentPlan.path.clipId,duration=CurrentPlan==null?0:CurrentPlan.duration,
                stepCount=CurrentPlan==null?0:CurrentPlan.path.stepCount,attemptId=AttemptId,
                nextEndpointIndex=TargetIndex,
                actualPathLength=ActualPathLength,headingYaw=owner.transform.eulerAngles.y,operation=motor==null?-1:motor.StepIndex,
                targetIndex=CurrentPlan==null?TargetIndex:CurrentPlan.targetIndex,actualEndpointIndex=actualIndex,
                actor=owner.transform.position,target=CurrentPlan==null?Target:CurrentPlan.endpoint,
                expectedEndpoint=actualIndex==0?pointA:actualIndex==1?pointB:CurrentPlan==null?owner.transform.position:CurrentPlan.endpoint});
            arena.Record(kind,"fixed_two_points",owner.fighter,0,owner.transform.position);
            if(detailedFailureDiagnostics&&(kind=="boss_patrol_invalid"||kind=="boss_patrol_retry_rejected"))
                arena.Record("boss_patrol_failure_detail",detail
                    +" | attempt="+AttemptId+" reconsider="+Reconsiderations
                    +" prior="+LastActualEndpointIndex+" target="+TargetIndex
                    +" actor="+owner.transform.position.ToString("F6")+" yaw="+owner.transform.eulerAngles.y.ToString("F6")
                    +" player="+arena.player.transform.position.ToString("F6")
                    +" motor="+(motor==null?"missing":motor.State.ToString())+" motorFailure="+(motor==null?"missing":motor.Failure)
                    +" available="+(motor!=null&&motor.Available)+" bound="+(motor!=null&&motor.Bound)+" guard="+(foundation!=null&&foundation.IsGuard),
                    owner.fighter,0,owner.transform.position);
        }
    }
}
