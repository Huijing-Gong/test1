using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SamProduction.BossArena
{
    [Serializable] public sealed class ArenaPatrolLegObservation
    {
        public string side;
        public Vector3 hip, knee, foot, desiredKnee, targetAnkle, targetSole, actualSole;
        public Vector3 hipLocalScale, kneeLocalScale, footLocalScale, pelvisLocalScale, pelvisLossyScale;
        public Vector3 hipLocalPosition, kneeLocalPosition, footLocalPosition;
        public float upperBind, lowerBind, upperBefore, lowerBefore, upperAfter, lowerAfter, ankleError, boneError, reachMargin, poleSquared;
        public bool swinging;
    }
    [Serializable] public sealed class ArenaPatrolSolveObservation
    {
        public string reason;
        public float sourceTime, yaw, lateralSeparation, pelvisAdjustment;
        public Vector3 actor;
        public CollisionFlags collisionFlags;
        public ArenaPatrolLegObservation left, right;
    }
    [Serializable] public sealed class ArenaPatrolBindObservation
    {
        public string side, mesh;
        public int rigidVertexCount, uniqueRigidVertexCount, rawPatch25Count, uniquePatch25Count, rawPatch50Count, uniquePatch50Count;
        public Vector3 exportedGuardSole, importedGuardSole, rawPatch25, uniquePatch25, rawPatch50, uniquePatch50;
        public Vector3 geometryMinimum, geometryMaximum, footOriginActor, footLossyScale, rendererLossyScale;
        public float importedExportedDistance, rigidBakeMaximumError, rigidBakeRmsError;
        public bool originalCentroidGatePassed;
        public int sourceUniqueVertexCount;
        public float actualToSourceMaximumError, sourceToActualMaximumError, anchorProjectionY;
        public Vector3 calibratedGuardSole;
        public bool geometryGatePassed;
    }
    [Serializable] public struct ArenaPatrolPath
    {
        public string clipId;
        public float duration, prefixDuration, suffixDuration;
        public int prefixYaw, suffixYaw;
        public int stepCount;
        public Vector3 origin, returnPoint, endpoint;
        public Quaternion initialHeading, finalHeading;
        public bool hasReturn;
        public int returnIndex, targetIndex;
        public float length, splitProgress, splitTime;
        public Vector3[] footGoals;
        public float[] footOutwardAdjustments;
        public float[] requiredPelvisLowering, pelvisEnvelope;
        public ArenaForwardPatrolCurve forwardCurve;
        public Vector3 Position(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if(forwardCurve!=null)return forwardCurve.Position(progress,splitProgress);
            if (!hasReturn) return Vector3.Lerp(origin, endpoint, progress);
            // Both legs stop at the actual intermediate endpoint. No corner is
            // cut, and no fixed root-speed assumption hides the return leg.
            return progress <= splitProgress
                ? Vector3.Lerp(origin, returnPoint, EndEase(progress / splitProgress))
                : Vector3.Lerp(returnPoint, endpoint, StartEase((progress-splitProgress)/(1-splitProgress)));
        }
        private static float EndEase(float x) { if(x<=.88f)return x;float t=(x-.88f)/.12f;return .88f+.12f*(t+t*t-t*t*t); }
        private static float StartEase(float x) { if(x>=.12f)return x;float t=x/.12f;return .12f*(2*t*t-t*t*t); }
    }
    public enum ArenaPatrolMotionState { Unavailable, Idle, Moving, Settling, Complete, Invalid }
    // The separate patrol rig is sampled at its authored rate. Only swing-foot
    // goals adapt; support boots remain fixed in the world, and root motion is
    // resolved by the same CharacterController as ordinary combat.
    public sealed class ArenaPatrolMotor : MonoBehaviour
    {
        public ArenaPatrolClips clips;
        public float minimumPathLength = 3, maximumPathLength = 5.5f, maximumTotalYaw = 75;
        public float reachMargin = .015f, endpointEpsilon = .005f;
        public float maximumPelvisAdjustment = .12f;
        public float maximumFootPlacementAdjustment = .25f;
        public float forwardMaximumPathLength = 8.5f;
        private const float MinimumSoleSeparation = .20f, PlannedSoleSeparation = .22f;
        public ArenaPatrolMotionState State { get; private set; } = ArenaPatrolMotionState.Unavailable;
        public bool Available { get; private set; }
        public bool Bound { get; private set; }
        public bool Active => State == ArenaPatrolMotionState.Moving || State == ArenaPatrolMotionState.Settling;
        public bool Complete => State == ArenaPatrolMotionState.Complete;
        public bool Invalid => State == ArenaPatrolMotionState.Invalid || State == ArenaPatrolMotionState.Unavailable;
        public string Failure { get; private set; } = "Patrol18 has not been initialized.";
        public string ClipId { get; private set; } = ArenaPatrolClips.DirectId;
        public int SourceStepCount => source == null ? 0 : source.steps.Length;
        public float SourceDuration => source == null ? 0 : source.duration;
        public float SourceTime { get; private set; }
        public float Elapsed { get; private set; }
        public float ActualPathLength { get; private set; }
        public Vector3 ActualVelocity { get; private set; }
        public bool ReturnEndpointReached { get; private set; }
        public bool FinalEndpointReached { get; private set; }
        public int StepIndex { get; private set; } = -1;
        public ArenaPatrolPath Path { get; private set; }
        public Vector3 LeftSole => legs[0] == null ? Vector3.zero : legs[0].ActualSole;
        public Vector3 RightSole => legs[1] == null ? Vector3.zero : legs[1].ActualSole;
        public bool LeftSupported => legs[0] != null && !legs[0].swinging;
        public bool RightSupported => legs[1] != null && !legs[1].swinging;
        public float MaximumAnkleError { get; private set; }
        public float MaximumBoneLengthError { get; private set; }
        public float MinimumReachMargin { get; private set; }
        public float FinalFacingError { get; private set; }
        public float CurrentPelvisAdjustment { get; private set; }
        public float MaximumPelvisAdjustment { get; private set; }
        public readonly List<ArenaPatrolBindObservation> BindObservations = new List<ArenaPatrolBindObservation>();
        public ArenaPatrolSolveObservation LastFailedSolve { get; private set; }
        public string LastSolveFailure { get; private set; } = "";
        public CollisionFlags LastCollisionFlags { get; private set; }
        public int PlayerYieldSteps { get; private set; }
        public int PlayerYieldBlockedSteps { get; private set; }
        public float PlayerYieldRequestedDistance { get; private set; }
        public float PlayerYieldDistance { get; private set; }
        public float MaximumPlayerYieldStep { get; private set; }
        public float MaximumPlayerYieldRequest { get; private set; }
        public float LastPlayerYieldLimit { get; private set; }
        private ArenaBossBrain owner;
        private ArenaSession arena;
        private ArenaAnimation driver;
        private ArenaFoundationMotor foundation;
        private ArenaPatrolSourceContract source;
        private Transform pelvis;
        private Vector3 sourcePelvisLocal;
        private float posedTime;
        private readonly Leg[] legs = new Leg[2];
        private Vector3[] trialGoals = Array.Empty<Vector3>();
        private Quaternion[] trialRotations = Array.Empty<Quaternion>();
        private Vector3[] trialOutward = Array.Empty<Vector3>();
        private readonly Dictionary<string,int> recentForwardCurve = new Dictionary<string,int>();
        private CurveGeometryMemo geometryMemo;
        private readonly Dictionary<ArenaPatrolSourceContract,PreflightSourceCache> preflightCaches = new Dictionary<ArenaPatrolSourceContract,PreflightSourceCache>();
        private string recentForwardClip = "";
        private int StepCount => source.steps.Length;
        private int FinalPair => StepCount-2;
        private int FirstStep(int side) => source.steps[0].side == (side==0?"L":"R") ? 0 : 1;
        private float vertical = -2, settleTime;
        private Quaternion stepHeadingFrom, stepHeadingTo, finalHeading;
        private bool headingFinalized, deathPending;
        private const int MovementMask = (1 << 8) | (1 << 9);

        public void Initialize(ArenaBossBrain boss)
        {
            preflightCaches.Clear();
            owner = boss; driver = boss.animationDriver; foundation = boss.FoundationMotor;
            if (clips == null) clips = GetComponent<ArenaPatrolClips>();
            if (clips == null || !clips.Configured) { Unavailable("Bind the independent Patrol18 clip and contract before integrated patrol."); return; }
            try { clips.Validate(); source = clips.Contract; Available = true; State = ArenaPatrolMotionState.Idle; Failure = ""; }
            catch (Exception e) { Unavailable(e.Message); }
        }
        private void Unavailable(string reason) { Available = Bound = false; State = ArenaPatrolMotionState.Unavailable; Failure = reason; }
        public void ResetMotor()
        {
            recentForwardCurve.Clear(); recentForwardClip="";
            SourceTime = Elapsed = ActualPathLength = 0; ActualVelocity = Vector3.zero; StepIndex = -1;
            ReturnEndpointReached = FinalEndpointReached = headingFinalized = deathPending = false;
            MaximumAnkleError = MaximumBoneLengthError = 0; MinimumReachMargin = float.PositiveInfinity;
            CurrentPelvisAdjustment = MaximumPelvisAdjustment = 0;
            LastFailedSolve=null;LastSolveFailure="";
            PlayerYieldSteps=PlayerYieldBlockedSteps=0;
            PlayerYieldRequestedDistance=PlayerYieldDistance=MaximumPlayerYieldStep=MaximumPlayerYieldRequest=LastPlayerYieldLimit=0;
            vertical = -2; settleTime = 0; LastCollisionFlags = CollisionFlags.None;
            if (Available) { State = ArenaPatrolMotionState.Idle; Failure = ""; }
        }
        public double PreparationMilliseconds { get; private set; }
        public bool Warmup()
        {
            if(!Available||Active||!foundation.IsGuard)return false;
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            var previous=source;
            try
            {
                if(!Bind())return false;
                foreach(string id in new[]{ArenaPatrolClips.DirectId,ArenaPatrolClips.ReturnId,ArenaPatrolClips.LeftId,ArenaPatrolClips.FourId,ArenaPatrolClips.FourShortId})
                    if(clips.HasVariant(id)){source=clips.SourceContract(id);PreflightCache();}
                return true;
            }
            finally
            {
                source=previous;
                PreparationMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency;
            }
        }
        private bool Bind()
        {
            if (Bound) return true;
            if (!Available || owner == null || !foundation.IsGuard || owner.Attacking) return false;
            var mesh = owner.GetComponentInChildren<SkinnedMeshRenderer>();
            if (mesh == null) { Unavailable("Preserved actual SAM mesh is missing."); return false; }
            var bones = mesh.bones; var p = new Vector3[bones.Length]; var q = new Quaternion[bones.Length];
            var shapes=new float[mesh.sharedMesh.blendShapeCount];
            for(int i=0;i<shapes.Length;i++)shapes[i]=mesh.GetBlendShapeWeight(i);
            BindObservations.Clear();
            for (int i = 0; i < bones.Length; i++) { p[i] = bones[i].localPosition; q[i] = bones[i].localRotation; }
            try
            {
                driver.PoseContactClip(foundation.foundation.Clip("guard"), 0, 0);
                foreach (var data in source.legs)
                {
                    int index = data.side == "L" ? 0 : data.side == "R" ? 1 : -1;
                    if (index < 0 || legs[index] != null) throw new InvalidOperationException("Patrol requires two distinct L/R deform legs.");
                    legs[index] = new Leg(owner, mesh, data, clips.SoleGeometry(data.side), BindObservations);
                }
                if (legs[0] == null || legs[1] == null) throw new InvalidOperationException("Incomplete actual patrol feet.");
                foreach (var observation in BindObservations)
                    if (!observation.geometryGatePassed)
                        throw new InvalidOperationException("Actual boot geometry calibration failed: " + observation.side
                            + " actualToSource=" + observation.actualToSourceMaximumError.ToString("F7")
                            + " sourceToActual=" + observation.sourceToActualMaximumError.ToString("F7")
                            + " rigidBake=" + observation.rigidBakeMaximumError.ToString("F7") + "m; inspect SAM_PATROL_BIND diagnostics.");
                pelvis=legs[0].hip.parent;
                if(pelvis==null||pelvis!=legs[1].hip.parent)throw new InvalidOperationException("Patrol requires the actual shared pelvis parent for coupled leg reach adjustment.");
                Bound = true;
            }
            catch (Exception e) { legs[0] = legs[1] = null; Unavailable(e.Message); }
            finally
            {
                for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = p[i]; bones[i].localRotation = q[i]; }
                for(int i=0;i<shapes.Length;i++)mesh.SetBlendShapeWeight(i,shapes[i]);
            }
            return Bound;
        }
        public bool TryPath(Vector3 origin, Quaternion heading, Vector3 returnPoint, Vector3 endpoint, Vector3 player,
            int returnIndex, int targetIndex, bool includeReturn, out ArenaPatrolPath path, out string reason, bool checkWorld = true, bool useEightSteps = false, string sourceOnly = null)
        {
            // A prediction has its own temporary source context. In particular
            // it cannot change the source/time metadata of a completed patrol
            // while Dash asks about hypothetical release and abort landings.
            var previousGeometry=geometryMemo;geometryMemo=null;
            var previousSource=source;var previousGoals=trialGoals;
            var previousRotations=trialRotations;var previousOutward=trialOutward;
            try
            {
                if(sourceOnly!=null)return TryPathCore(origin,heading,returnPoint,endpoint,player,returnIndex,targetIndex,includeReturn,out path,out reason,checkWorld,false,sourceOnly);
                // Reuse search order, never a stale solved path. Every query
                // recomputes all feet and checks the current world geometry.
                if(recentForwardClip==ArenaPatrolClips.LeftId&&clips!=null&&clips.HasVariant(ArenaPatrolClips.LeftId))
                    if(TryPathCore(origin,heading,returnPoint,endpoint,player,returnIndex,targetIndex,includeReturn,out path,out reason,checkWorld,true,ArenaPatrolClips.LeftId))return true;
                if(TryPathCore(origin,heading,returnPoint,endpoint,player,returnIndex,targetIndex,includeReturn,out path,out reason,checkWorld,useEightSteps))return true;
                string rightFailure=reason;
                if(clips!=null&&clips.HasVariant(ArenaPatrolClips.LeftId))
                {
                    if(TryPathCore(origin,heading,returnPoint,endpoint,player,returnIndex,targetIndex,includeReturn,out path,out reason,checkWorld,true,ArenaPatrolClips.LeftId))return true;
                    reason="Right-leading: "+rightFailure+" Left-leading: "+reason;
                }
                // A stationary ignition/stomp can start the next interval at
                // exactly the last endpoint. Try the complete six-step source
                // too; requiring eight steps here can make its final footholds
                // infeasible despite a valid 4.8-second forward route.
                if(useEightSteps&&!includeReturn)
                {
                    string priorFailure=reason;
                    if(TryPathCore(origin,heading,returnPoint,endpoint,player,returnIndex,targetIndex,false,out path,out reason,checkWorld,false))return true;
                    reason=priorFailure+" Six-step: "+reason;
                }
                return false;
            }
            finally{geometryMemo=previousGeometry;source=previousSource;trialGoals=previousGoals;trialRotations=previousRotations;trialOutward=previousOutward;}
        }
        private bool TryPathCore(Vector3 origin, Quaternion heading, Vector3 returnPoint, Vector3 endpoint, Vector3 player,
            int returnIndex, int targetIndex, bool includeReturn, out ArenaPatrolPath path, out string reason, bool checkWorld, bool useEightSteps, string forcedId = null)
        {
            path=default;reason="";
            if(Active){reason="Cannot replace an active complete patrol.";return false;}
            if(!Available||(!Bound&&!Bind())){reason=Failure;return false;}
            returnPoint.y=endpoint.y=origin.y;
            bool returning=includeReturn&&Vector3.Distance(origin,returnPoint)>endpointEpsilon;
            string id=forcedId??(returning||useEightSteps?ArenaPatrolClips.ReturnId:ArenaPatrolClips.DirectId);
            if(!clips.HasVariant(id)){reason="Missing complete forward source "+id;return false;}
            source=clips.SourceContract(id);
            trialGoals=new Vector3[StepCount];trialRotations=new Quaternion[StepCount];trialOutward=new Vector3[StepCount];
            Vector3 playerDirection=Vector3.ProjectOnPlane(player-endpoint,Vector3.up);
            Quaternion final=playerDirection.sqrMagnitude>.001f?Quaternion.LookRotation(playerDirection):heading;
            var failures=new StringBuilder();
            // Curves/curvature depend on this exact query, not gait source.
            // Share only this pure work across its right/left/six/eight variants.
            // No solved feet, world checks or results survive the query.
            if(geometryMemo==null||!geometryMemo.Matches(origin,heading,returnPoint,endpoint,final,returning,owner.ProductionAI))
            {
                var generatedCurves=new List<ArenaForwardPatrolCurve>();
                foreach(float scale in new[]{.4f,.65f,1f,1.4f})
                foreach(int joinSide in returning?new[]{1,-1}:new[]{1})
                    generatedCurves.Add(new ArenaForwardPatrolCurve(origin,heading,returnPoint,endpoint,final,returning,scale,joinSide));
                if(!returning)
                    foreach(float radius in new[]{.65f,.85f,1.05f})
                    foreach(int firstSide in new[]{1,-1})
                    foreach(int secondSide in new[]{1,-1})
                        if(ArenaForwardPatrolCurve.TryArcLineArc(origin,heading,endpoint,final,radius,firstSide,secondSide,out var curvedTurn))generatedCurves.Add(curvedTurn);
                if(!returning)
                    foreach(float tail in new[]{1f,1.5f})
                    foreach(float radius in new[]{.65f,.85f})
                    foreach(int firstSide in new[]{1,-1})foreach(int secondSide in new[]{1,-1})
                    {
                        Vector3 preStop=endpoint-final*Vector3.forward*tail;
                        if(ArenaForwardPatrolCurve.TryArcLineArc(origin,heading,preStop,final,radius,firstSide,secondSide,out var curvedTurn))
                            generatedCurves.Add(ArenaForwardPatrolCurve.WithStraightTail(curvedTurn,preStop,endpoint,final));
                    }
                if(!returning&&owner.ProductionAI)
                    foreach(float head in new[]{.5f,1f})
                    foreach(float tail in new[]{0f,1f})
                    foreach(float radius in new[]{.85f,1.05f})
                    foreach(int firstSide in new[]{1,-1})foreach(int secondSide in new[]{1,-1})
                    {
                        Vector3 afterStart=origin+heading*Vector3.forward*head;
                        Vector3 beforeStop=endpoint-final*Vector3.forward*tail;
                        if(!ArenaForwardPatrolCurve.TryArcLineArc(afterStart,heading,beforeStop,final,radius,firstSide,secondSide,out var middle))continue;
                        if(tail>0)middle=ArenaForwardPatrolCurve.WithStraightTail(middle,beforeStop,endpoint,final);
                        generatedCurves.Add(ArenaForwardPatrolCurve.WithStraightHead(middle,origin,afterStart,heading));
                    }
                if(returning)
                {
                    Vector3 routeDirection=(endpoint-returnPoint).normalized;
                    foreach(float radius in new[]{.65f,.85f})
                    foreach(int side in new[]{1,-1})
                    {
                        Quaternion joinHeading=Quaternion.LookRotation(Quaternion.AngleAxis(side*90,Vector3.up)*routeDirection);
                        ArenaForwardPatrolCurve bestFirst=null,bestSecond=null;
                        foreach(int s1 in new[]{1,-1})foreach(int s2 in new[]{1,-1})
                        {
                            if(ArenaForwardPatrolCurve.TryArcLineArc(origin,heading,returnPoint,joinHeading,radius,s1,s2,out var c1)
                                &&(bestFirst==null||c1.Length<bestFirst.Length))bestFirst=c1;
                            if(ArenaForwardPatrolCurve.TryArcLineArc(returnPoint,joinHeading,endpoint,final,radius,s1,s2,out var c2)
                                &&(bestSecond==null||c2.Length<bestSecond.Length))bestSecond=c2;
                        }
                        if(bestFirst!=null&&bestSecond!=null)generatedCurves.Add(ArenaForwardPatrolCurve.Join(bestFirst,bestSecond));
                    }
                }
                geometryMemo=new CurveGeometryMemo(origin,heading,returnPoint,endpoint,final,returning,owner.ProductionAI,generatedCurves.ToArray());
            }
            // The last feasible family is a search hint for nearby tracking
            // queries. It must pass fresh complete preflight; no validity cache.
            string searchKey=id+(returning?"/return":"/direct");
            recentForwardCurve.TryGetValue(searchKey,out int preferred);
            bool hasPreferred=recentForwardCurve.ContainsKey(searchKey);
            var originalOrder=geometryMemo.curves;
            var curves=originalOrder.OrderBy(c=>hasPreferred&&Array.IndexOf(originalOrder,c)==preferred?0:1).ThenBy(c=>c.Length).ToList();
            int curveIndex=0;
            foreach(var curve in curves)
            {
                int scale=curveIndex++;
                if(curve.Length<((id==ArenaPatrolClips.FourId||id==ArenaPatrolClips.FourShortId)?1.6f:minimumPathLength)||curve.Length>forwardMaximumPathLength)
                {failures.Append("handle ").Append(scale).Append(" length ").Append(curve.Length).Append("; ");continue;}
                float splitTime=0,split=0;
                if(returning)
                {
                    float best=float.PositiveInfinity;
                    for(int i=0;i<FinalPair;i++)
                    {
                        source.Sample(source.steps[i].land,out float p,out _,out _,out _,out _);
                        float error=Mathf.Abs(p-curve.FirstFraction);
                        if(p>.01f&&p<.95f&&error<best){best=error;split=p;splitTime=source.steps[i].land;}
                    }
                    if(split<=0)continue;
                }
                var candidate=new ArenaPatrolPath{origin=origin,returnPoint=returnPoint,endpoint=endpoint,
                    initialHeading=heading,finalHeading=final,hasReturn=returning,returnIndex=returnIndex,targetIndex=targetIndex,
                    clipId=id,duration=source.duration,stepCount=StepCount,length=curve.Length,splitProgress=split,
                    splitTime=splitTime,forwardCurve=curve};
                bool geometry;string geometryFailure;
                if(geometryMemo.checks.TryGetValue((curve,split),out var geometryResult))
                {geometry=geometryResult.passed;geometryFailure=geometryResult.reason;}
                else
                {
                    geometry=true;geometryFailure="";Vector3 previous=origin;Quaternion previousHeading=heading;
                    for(int i=1;i<=256;i++)
                    {
                        float p=i/256f;Vector3 current=curve.Position(p,split);Quaternion facing;
                        try{facing=curve.Heading(p,split);}catch(InvalidOperationException){geometry=false;geometryFailure="cusp at "+p;break;}
                        // Reject reversals/cusps and extreme curvature before any
                        // rendered foot is moved. Never substitute a sideways line.
                        float distance=Vector3.Distance(previous,current);
                        if(distance>.00001f&&Quaternion.Angle(previousHeading,facing)/distance>150)
                        {geometry=false;geometryFailure="curvature at "+p+" deg/m="+(Quaternion.Angle(previousHeading,facing)/distance);break;}
                        previous=current;previousHeading=facing;
                    }
                    geometryMemo.checks.Add((curve,split),new CurveGeometryMemo.Check{passed=geometry,reason=geometryFailure});
                }
                if(!geometry){failures.Append("handle ").Append(scale).Append(' ').Append(geometryFailure).Append("; ");continue;}
                if(!Preflight(ref candidate,false,out string rejected))
                {failures.Append("handle ").Append(scale).Append(' ').Append(rejected).Append("; ");continue;}
                // World queries cannot make an impossible leg pose feasible.
                // Run the same full collision/floor checks only after geometry.
                if(checkWorld)
                {
                    Vector3 previousRoot=origin;bool clear=true;
                    for(int i=1;i<=256;i++)
                    {
                        Vector3 currentRoot=curve.Position(i/256f,split);
                        if(!ClearRootSegment(previousRoot,currentRoot)){clear=false;break;}
                        previousRoot=currentRoot;
                    }
                    if(clear)foreach(var goal in candidate.footGoals)
                        if(!Ground(goal,out var floor)||Mathf.Abs(floor.y-origin.y)>.01f){clear=false;break;}
                    if(!clear){failures.Append("handle ").Append(scale).Append(" world collision/floor; ");continue;}
                }
                recentForwardCurve[searchKey]=Array.IndexOf(originalOrder,curve);
                recentForwardClip=id;
                path=candidate;reason="";return true;
            }
            reason=failures.ToString();return false;
        }
        // Retained only as a readable record while the forward replacement is
        // validated. No production or prediction entry calls this side gait.
        private bool LegacySidePathCore(Vector3 origin, Quaternion heading, Vector3 returnPoint, Vector3 endpoint, Vector3 player,
            int returnIndex, int targetIndex, bool includeReturn, out ArenaPatrolPath path, out string reason, bool checkWorld)
        {
            path = default; reason = "";
            if(Active){reason="Cannot replace the source of an active complete patrol.";return false;}
            if (!Available || (!Bound && !Bind())) { reason = Failure.Length > 0 ? Failure : "Actual guard-pose bind calibration is unavailable."; return false; }
            returnPoint.y = endpoint.y = origin.y;
            float back = includeReturn ? Vector3.Distance(origin, returnPoint) : 0;
            float across = Vector3.Distance(includeReturn ? returnPoint : origin, endpoint);
            bool hasReturn = includeReturn && back > endpointEpsilon;
            string selectedId=hasReturn?ArenaPatrolClips.ReturnId:ArenaPatrolClips.DirectId;
            if(!clips.HasVariant(selectedId)){reason="Complete return source is not configured: "+selectedId;return false;}
            source=clips.SourceContract(selectedId);
            trialGoals=new Vector3[StepCount];trialRotations=new Quaternion[StepCount];trialOutward=new Vector3[StepCount];
            float length = back + across;
            if (length < minimumPathLength || length > maximumPathLength)
            { reason = "Actual return + full AB length outside the complete-source trial reach domain: " + length; return false; }
            Vector3 toPlayer = Vector3.ProjectOnPlane(player-endpoint, Vector3.up);
            Quaternion facing = toPlayer.sqrMagnitude > .001f ? Quaternion.LookRotation(toPlayer) : heading;
            if (Quaternion.Angle(heading, facing) > maximumTotalYaw)
            { reason = "Final body/foot turn outside the complete-source angular trial domain."; return false; }
            float splitProgress = 0, splitTime = 0;
            if (hasReturn)
            {
                float best = float.PositiveInfinity;
                // Commit the route corner to a source landing, so a path knot
                // has an actual support change and is explicitly sampled.
                for (int i = 0; i < Mathf.Min(3,FinalPair); i++)
                {
                    source.Sample(source.steps[i].land, out float progress, out _, out _, out _, out _);
                    float score = Mathf.Abs(progress-back/length);
                    if (progress > .02f && progress < .9f && score < best) { best = score; splitProgress = progress; splitTime = source.steps[i].land; }
                }
                if (splitProgress <= 0) { reason = "No full-step support transition can contain the return corner."; return false; }
            }
            path = new ArenaPatrolPath { origin=origin, returnPoint=returnPoint, endpoint=endpoint, initialHeading=heading, finalHeading=facing,
                clipId=selectedId,duration=source.duration,stepCount=StepCount,
                hasReturn=hasReturn, returnIndex=returnIndex, targetIndex=targetIndex, length=length, splitProgress=splitProgress, splitTime=splitTime };
            if (checkWorld && (!ClearRootSegment(origin, hasReturn ? returnPoint : endpoint) || (hasReturn && !ClearRootSegment(returnPoint, endpoint))))
            { reason = "Real world/player capsule obstructs the complete proposed route."; return false; }
            if(!hasReturn)return Preflight(ref path,checkWorld,out reason);
            ArenaPatrolPath bestPath=default;float bestScore=float.PositiveInfinity;bool found=false;string failures="";
            for(int i=0;i<Mathf.Min(3,FinalPair);i++)
            {
                var step=source.steps[i];
                // Choose a specific real source time inside a full swing. This
                // lets a short reverse return cross its endpoint during one
                // meaningful step instead of inventing two tiny return steps.
                source.Sample(step.swingStart,out float startP,out _,out _,out _,out _);
                source.Sample(step.land,out float endP,out _,out _,out _,out _);
                float ideal=Mathf.Lerp(step.swingStart,step.land,Mathf.InverseLerp(startP,endP,back/length));
                for(int option=0;option<3;option++)
                {
                    float time=option==0?step.land:option==1?ideal:(step.swingStart+step.land)*.5f;
                    if(option>0&&Mathf.Abs(time-step.land)<.00001f)continue;
                    source.Sample(time,out float progress,out _,out _,out _,out _);
                    if(progress<=.005f||progress>=.9f)continue;
                    var candidate=path;candidate.splitProgress=progress;candidate.splitTime=time;
                    if(!Preflight(ref candidate,checkWorld,out reason)){failures+="corner "+i+" at "+time+": "+reason+"; ";continue;}
                    float score=Mathf.Abs(progress-back/length);
                    if(score<bestScore){bestScore=score;bestPath=candidate;found=true;}
                }
            }
            if(found){path=bestPath;reason="";}else reason=failures;return found;
        }
        private Quaternion PlannedHeading(ArenaPatrolPath path, float time)
        {
            if(path.forwardCurve!=null)
            {
                source.Sample(time,out float routeProgress,out _,out _,out _,out _);
                return path.forwardCurve.Heading(routeProgress,path.splitProgress);
            }
            int firstTurnStep=0;
            if(path.hasReturn)while(firstTurnStep<FinalPair&&source.steps[firstTurnStep].land<=path.splitTime+.000001f)firstTurnStep++;
            float progress = 0;
            for (int i = firstTurnStep; i < source.steps.Length; i++)
            {
                var step = source.steps[i];
                if (time >= step.land) progress = (i+1-firstTurnStep)/(float)(StepCount-firstTurnStep);
                else if (time >= step.swingStart) { progress = (i-firstTurnStep+Smooth((time-step.swingStart)/(step.land-step.swingStart)))/(StepCount-firstTurnStep); break; }
                else break;
            }
            return Quaternion.Slerp(path.initialHeading, path.finalHeading, progress);
        }
        private bool Preflight(ref ArenaPatrolPath path, bool checkWorld, out string reason,int frozenSteps=0,ArenaPatrolPath frozenPath=default)
        {
            reason = "";
            for (int i = 0; i < StepCount; i++)
            {
                var step = source.steps[i]; var leg = legs[step.side == "L" ? 0 : 1];
                if(i<frozenSteps)
                {
                    trialGoals[i]=frozenPath.footGoals[i];
                    Quaternion oldHeading=i>=FinalPair?frozenPath.finalHeading:PlannedHeading(frozenPath,step.land);
                    trialRotations[i]=oldHeading*leg.GuardFootRotation;
                    trialOutward[i]=Vector3.zero;continue;
                }
                Quaternion heading = i >= FinalPair ? path.finalHeading : PlannedHeading(path, step.land);
                Vector3 goal = path.Position(step.plantRootProgress) + heading * leg.GuardSoleActor;
                goal.y = path.origin.y;
                if (checkWorld && (!Ground(goal, out var floor) || Mathf.Abs(floor.y-path.origin.y) > .01f))
                { reason = "Planned boot contact has no matching actual flat floor."; return false; }
                trialGoals[i] = goal; trialRotations[i] = heading * leg.GuardFootRotation;
                trialOutward[i]=heading*Vector3.right*(step.side=="R"?1:-1);
            }
            // Solve all future contacts together. A turn can reverse the
            // projection of an earlier outward offset; greedy permanent growth
            // can therefore reject a feasible complete set of contacts.
            var adjustments=new float[StepCount];var cache=PreflightCache();
            var coefficients=cache.coefficients;var demands=cache.demands;
            // Coefficients remain fixed during projection. Their squared norms
            // are identical in every iteration; calculate once, preserving the
            // exact multiplication/addition order and all feasibility thresholds.
            var rowNorms=cache.rowNorms;
            for(int sampleIndex=0;sampleIndex<source.samples.Length;sampleIndex++)
            {
                var sample=source.samples[sampleIndex];var weights=cache.weights[sampleIndex];
                PlannedFeet(path,sample,cache.blends[sampleIndex],out var left,out var right);
                Quaternion sampleHeading=path.forwardCurve!=null
                    ?path.forwardCurve.Heading(cache.routeProgress[sampleIndex],path.splitProgress)
                    :PlannedHeading(path,sample.time);
                cache.headings[sampleIndex]=sampleHeading;
                Vector3 actorRight=sampleHeading*Vector3.right;
                var row=coefficients[sampleIndex];float possible=0;
                for(int i=0;i<FinalPair;i++)
                {
                    row[i]=weights[i]*Vector3.Dot(trialOutward[i],actorRight)*(source.steps[i].side=="R"?1:-1);
                    possible+=Mathf.Max(0,row[i])*maximumFootPlacementAdjustment;
                }
                float demand=PlannedSoleSeparation-Vector3.Dot(right-left,actorRight);
                if(demand>possible+.000001f)
                {reason="Full interval foot-placement constraints infeasible at "+sample.time+" demand="+demand+" maximum="+possible;return false;}
                float rowNorm=0;for(int i=0;i<FinalPair;i++)rowNorm+=row[i]*row[i];
                demands[sampleIndex]=demand;rowNorms[sampleIndex]=rowNorm;
            }
            bool contactsSolved=false;
            for(int iteration=0;iteration<160;iteration++)
            {
                for(int sample=0;sample<coefficients.Length;sample++)
                {
                    var row=coefficients[sample];float value=0,norm=rowNorms[sample];
                    for(int i=0;i<FinalPair;i++)value+=row[i]*adjustments[i];
                    float correction=demands[sample]-value;
                    if(correction<=0||norm<.00000001f)continue;
                    for(int i=0;i<FinalPair;i++)adjustments[i]=Mathf.Clamp(adjustments[i]+row[i]*correction/norm,0,maximumFootPlacementAdjustment);
                }
                float violation=0;
                for(int sample=0;sample<coefficients.Length;sample++)
                {
                    float value=0;for(int i=0;i<FinalPair;i++)value+=coefficients[sample][i]*adjustments[i];
                    violation=Mathf.Max(violation,demands[sample]-value);
                }
                if(violation<=.000001f){contactsSolved=true;break;}
            }
            if(!contactsSolved){reason="Whole-interval foot-placement constraints did not converge inside the unchanged 22cm/25cm domain.";return false;}
            for(int i=0;i<FinalPair;i++)trialGoals[i]+=trialOutward[i]*adjustments[i];
            path.footGoals=(Vector3[])trialGoals.Clone();path.footOutwardAdjustments=adjustments;
            for(int i=0;i<frozenSteps;i++)path.footOutwardAdjustments[i]=frozenPath.footOutwardAdjustments[i];
            if(checkWorld)foreach(var goal in trialGoals)
                if(!Ground(goal,out var floor)||Mathf.Abs(floor.y-path.origin.y)>.01f)
                {reason="Adjusted future swing contact has no actual flat floor.";return false;}
            // Pure vector reach checks use the independently exported actual hip
            // samples. They cannot certify knee appearance, twist or rendering.
            var requiredLowering=new float[source.samples.Length];
            int reachSampleIndex=0;
            foreach (var sample in source.samples)
            {
                Vector3 root = path.Position(sample.rootProgress); Quaternion heading = cache.headings[reachSampleIndex];
                float lower=0;
                for (int side = 0; side < 2; side++)
                {
                    var leg = legs[side]; Vector3 sole = path.origin + path.initialHeading * leg.GuardSoleActor; sole.y = path.origin.y;
                    Quaternion rotation = path.initialHeading * leg.GuardFootRotation;
                    for (int i = FirstStep(side); i < StepCount; i += 2)
                    {
                        var step = source.steps[i];
                        if (sample.time <= step.swingStart) break;
                        float blend = cache.blends[reachSampleIndex][i];
                        sole = Vector3.Lerp(sole, trialGoals[i], blend); rotation = Quaternion.Slerp(rotation, trialRotations[i], blend);
                        if (sample.time < step.land) { sole.y += side == 0 ? sample.leftLift : sample.rightLift; break; }
                    }
                    Vector3 hip = root + heading * ArenaPatrolSourceContract.V(side == 0 ? sample.leftHipActor : sample.rightHipActor);
                    float needed=leg.RequiredLowering(hip,sole-rotation*leg.FlatSoleLocal,reachMargin);
                    lower=Mathf.Max(lower,needed);
                    if (lower > maximumPelvisAdjustment)
                    { reason = "Complete source planned hip/ankle reach fails at source " + sample.time + " for " + (side==0?"L":"R"); return false; }
                }
                requiredLowering[reachSampleIndex++]=lower;
            }
            path.requiredPelvisLowering=requiredLowering;
            return true;
        }
        private void PlannedFeet(ArenaPatrolPath path,ArenaPatrolSourceSample sample,float[] blends,out Vector3 left,out Vector3 right)
        {
            left=right=Vector3.zero;
            for(int side=0;side<2;side++)
            {
                Vector3 sole=path.origin+path.initialHeading*legs[side].GuardSoleActor;sole.y=path.origin.y;
                for(int i=FirstStep(side);i<StepCount;i+=2)
                {
                    var step=source.steps[i];if(sample.time<=step.swingStart)break;
                    float blend=blends[i];
                    sole=Vector3.Lerp(sole,trialGoals[i],blend);
                    if(sample.time<step.land){sole.y+=side==0?sample.leftLift:sample.rightLift;break;}
                }
                if(side==0)left=sole;else right=sole;
            }
        }
        private PreflightSourceCache PreflightCache()
        {
            if(!preflightCaches.TryGetValue(source,out var cache))
            {
                cache=new PreflightSourceCache(source);preflightCaches.Add(source,cache);
            }
            return cache;
        }
        // Key by the loaded immutable contract object, not clip name: variants
        // may share an ID in separate libraries. Only source-time arithmetic is
        // cached. The remaining arrays are private, nonescaping work storage;
        // every path overwrites all rows before projection, including frozen
        // steps whose trialOutward is zero. No solved path or world query is reused.
        private sealed class CurveGeometryMemo
        {
            public struct Check { public bool passed;public string reason; }
            public readonly ArenaForwardPatrolCurve[] curves;
            // Return-waypoint reparameterization depends on the source's split;
            // equal curves with different six/eight-step splits are distinct.
            public readonly Dictionary<(ArenaForwardPatrolCurve,float),Check> checks=new Dictionary<(ArenaForwardPatrolCurve,float),Check>();
            private readonly Vector3 origin,returnPoint,endpoint;
            private readonly Quaternion heading,final;
            private readonly bool returning,production;
            public CurveGeometryMemo(Vector3 origin,Quaternion heading,Vector3 returnPoint,Vector3 endpoint,Quaternion final,bool returning,bool production,ArenaForwardPatrolCurve[] curves)
            {this.origin=origin;this.heading=heading;this.returnPoint=returnPoint;this.endpoint=endpoint;this.final=final;this.returning=returning;this.production=production;this.curves=curves;}
            public bool Matches(Vector3 o,Quaternion h,Vector3 r,Vector3 e,Quaternion f,bool ret,bool prod)
                =>origin.Equals(o)&&heading.Equals(h)&&returnPoint.Equals(r)&&endpoint.Equals(e)&&final.Equals(f)&&returning==ret&&production==prod;
        }
        private sealed class PreflightSourceCache
        {
            public readonly float[][] blends,weights,coefficients;
            public readonly float[] demands,rowNorms,routeProgress;
            public readonly Quaternion[] headings;
            public PreflightSourceCache(ArenaPatrolSourceContract contract)
            {
                int count=contract.samples.Length,steps=contract.steps.Length;
                blends=new float[count][];weights=new float[count][];coefficients=new float[count][];
                demands=new float[count];rowNorms=new float[count];routeProgress=new float[count];headings=new Quaternion[count];
                for(int sampleIndex=0;sampleIndex<count;sampleIndex++)
                {
                    var sample=contract.samples[sampleIndex];
                    // Sample the original time exactly; using sample.rootProgress
                    // directly could change float rounding at t*fps boundaries.
                    contract.Sample(sample.time,out routeProgress[sampleIndex],out _,out _,out _,out _);
                    var blendRow=new float[steps];var weightRow=new float[steps];
                    blends[sampleIndex]=blendRow;weights[sampleIndex]=weightRow;coefficients[sampleIndex]=new float[steps-2];
                    for(int side=0;side<2;side++)
                    {
                        int first=contract.steps[0].side==(side==0?"L":"R")?0:1;
                        for(int i=first;i<steps;i+=2)
                        {
                            var step=contract.steps[i];if(sample.time<=step.swingStart)break;
                            float blend=Smooth((sample.time-step.swingStart)/(step.land-step.swingStart));
                            blendRow[i]=blend;
                            // Keep the original weight update order exactly.
                            for(int j=first;j<steps;j+=2)weightRow[j]*=1-blend;
                            weightRow[i]+=blend;
                            if(sample.time<step.land)break;
                        }
                    }
                }
            }
        }
        public bool Begin(ArenaPatrolPath path, ArenaSession session)
        {
            if (!Available || Active || !foundation.IsGuard || !Bound) return false;
            if(string.IsNullOrEmpty(path.clipId)||!clips.HasVariant(path.clipId))return false;
            source=clips.SourceContract(path.clipId);
            if(path.stepCount!=StepCount||Mathf.Abs(path.duration-source.duration)>.00001f
                ||path.footGoals==null||path.footGoals.Length!=StepCount||path.footOutwardAdjustments==null||path.footOutwardAdjustments.Length!=StepCount)return false;
            arena = session; ResetMotor();
            path.pelvisEnvelope=AnticipatedPelvis(path.requiredPelvisLowering,source.fps);
            Path = path; ClipId=path.clipId; finalHeading = path.finalHeading;
            for (int i = 0; i < 2; i++) legs[i].ResetActual();
            foundation.SuspendForAttack(); State = ArenaPatrolMotionState.Moving;
            PoseSource(0,0);
            if (!ApplyFeet(0, false)) { Fail("Initial actual boot/chain cannot enter patrol."); return false; }
            Record("boss_patrol_motor_started", "Full "+SourceDuration+"s "+ClipId+"; actual return and final facing are included.");
            return true;
        }
        public void Tick(float delta, ArenaSession session)
        {
            if (!Active || delta <= 0) return;
            if (delta > .25f || float.IsNaN(delta) || float.IsInfinity(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            arena = session;
            if (owner.fighter.Dead) { deathPending = true; if (State != ArenaPatrolMotionState.Settling) Fail("boss_died_during_patrol"); }
            float remaining = delta;
            while (remaining > .000001f && Active)
            {
                if (State == ArenaPatrolMotionState.Settling) { Settle(remaining); return; }
                ProcessMarkers(); if (State != ArenaPatrolMotionState.Moving) continue;
                float next = Mathf.Min(source.duration, SourceTime+remaining);
                next = Mathf.Min(next, (Mathf.Floor(SourceTime*source.fps+.0001f)+1)/source.fps);
                foreach (var step in source.steps)
                {
                    if (step.swingStart > SourceTime+.000001f) next = Mathf.Min(next, step.swingStart);
                    if (step.land > SourceTime+.000001f) next = Mathf.Min(next, step.land);
                }
                if (Path.hasReturn && !ReturnEndpointReached && Path.splitTime > SourceTime+.000001f) next = Mathf.Min(next, Path.splitTime);
                float part = next-SourceTime;
                if (part <= 0) { Finish(); continue; }
                source.Sample(next, out float progress, out _, out _, out _, out _);
                Vector3 planned = Path.Position(progress), before = owner.transform.position;
                Vector3 request = Vector3.ProjectOnPlane(planned-before, Vector3.up);
                Quaternion beforeHeading = owner.transform.rotation;
                owner.transform.rotation = LiveHeading(next);
                PoseSource(next,part);
                EvaluateFeet(next, true);
                if (!CheckReach(request))
                {
                    owner.transform.rotation = beforeHeading; PoseSource(SourceTime,0); ApplyFeet(SourceTime,false);
                    Fail("Actual future hip/boot target exceeded the rigid two-bone reach domain."); continue;
                }
                if (owner.capsule.isGrounded && vertical < 0) vertical = -2;
                vertical = Mathf.Max(-25, vertical-24*part);
                YieldPlayerAtContact(before, request);
                LastCollisionFlags = owner.capsule.Move(request+Vector3.up*vertical*part);
                Vector3 actual = owner.transform.position-before; ActualVelocity = actual/part; ActualPathLength += Vector3.ProjectOnPlane(actual,Vector3.up).magnitude;
                SourceTime = next; Elapsed += part; remaining -= part;
                if (!ApplyFeet(SourceTime, true)) { Fail("Actual foot solve failed: "+LastSolveFailure); continue; }
                if (Vector3.ProjectOnPlane(owner.transform.position-planned,Vector3.up).magnitude > .015f || Mathf.Abs(owner.transform.position.y-Path.origin.y) > .015f)
                { Fail("Actual CharacterController was obstructed or left the flat authored floor; no time/stride compression is allowed."); continue; }
                if (Path.hasReturn && !ReturnEndpointReached && SourceTime >= Path.splitTime-.000001f)
                {
                    if (!At(Path.returnPoint)) { Fail("Actual return endpoint was not reached."); continue; }
                    ReturnEndpointReached = true; Record("boss_patrol_return_endpoint_reached", "Actual original endpoint reached by collision-resolved root.");
                }
                ProcessMarkers();
                if (SourceTime >= source.duration-.000001f) Finish();
            }
            if (!Active && remaining > .000001f) foundation.Tick(remaining, arena);
        }
        private void ProcessMarkers()
        {
            for (int i = 0; i < StepCount; i++)
            {
                var step = source.steps[i]; var leg = legs[step.side=="L"?0:1];
                if (i > StepIndex && SourceTime >= step.swingStart-.000001f)
                {
                    TryRefreshFutureFacing(i);
                    StepIndex = i; stepHeadingFrom = owner.transform.rotation;
                    Vector3 direction = Vector3.ProjectOnPlane(arena.player.transform.position-Path.endpoint, Vector3.up);
                    Quaternion desired = direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction) : finalHeading;
                    if(Path.forwardCurve!=null)desired=Path.finalHeading;
                    if (i == FinalPair) { finalHeading = desired; headingFinalized = true; }
                    float remainingTurn = Quaternion.Angle(stepHeadingFrom, i >= FinalPair ? finalHeading : desired);
                    if (Path.forwardCurve==null&&(remainingTurn > maximumTotalYaw || (i >= FinalPair && remainingTurn > 35*(StepCount-i))))
                    { Fail("Live final facing exceeds the remaining complete foot placements."); return; }
                    Quaternion goalHeading = i >= FinalPair ? finalHeading : desired;
                    // Return toward the actual prior endpoint first. Turning
                    // toward the far endpoint's player during those forward
                    // return steps needlessly sweeps the support hip sideways.
                    bool returningStep=Path.hasReturn&&step.land<=Path.splitTime+.000001f;
                    stepHeadingTo = returningStep?stepHeadingFrom:Quaternion.Slerp(stepHeadingFrom, goalHeading, 1f/(StepCount-i));
                    if (i == StepCount-1) stepHeadingTo = finalHeading;
                    Quaternion footHeading = i >= FinalPair ? finalHeading : stepHeadingTo;
                    if(Path.forwardCurve!=null)
                    {
                        stepHeadingTo=PlannedHeading(Path,step.land);
                        footHeading=i>=FinalPair?Path.finalHeading:stepHeadingTo;
                    }
                    Vector3 target = Path.Position(step.plantRootProgress)+footHeading*leg.GuardSoleActor;
                    if(i<FinalPair)target+=footHeading*Vector3.right*(step.side=="R"?1:-1)*Path.footOutwardAdjustments[i];
                    target.y = Path.origin.y;
                    if (!Ground(target, out var floor) || Mathf.Abs(floor.y-Path.origin.y) > .01f)
                    { Fail("Actual next swing target has no flat ground support."); return; }
                    leg.BeginSwing(target, footHeading*leg.GuardFootRotation);
                    Record("boss_patrol_swing_started", "step="+i+" side="+step.side+"; only this airborne foot receives a new target; outwardAdjustment="+Path.footOutwardAdjustments[i].ToString("F6"));
                }
                if (i == StepIndex && leg.swinging && SourceTime >= step.land-.000001f)
                {
                    if (Vector3.Distance(leg.ActualSole, leg.goal) > .005f || !Ground(leg.ActualSole, out var ground) || Mathf.Abs(ground.y-leg.ActualSole.y) > .005f)
                    { Fail("Actual rigid sole did not reach its planned floor contact."); return; }
                    leg.CommitPlant();
                    arena.Record("boss_foot_contact", step.side=="L"?"left_step":"right_step", owner.fighter, 0, leg.ActualSole);
                    Record("boss_patrol_foot_planted", "step="+i+" side="+step.side+"; actual rigid boot fixed in world space.");
                }
            }
        }
        private Quaternion LiveHeading(float time)
        {
            if(Path.forwardCurve!=null)return PlannedHeading(Path,time);
            if (StepIndex < 0) return Path.initialHeading;
            var step = source.steps[StepIndex];
            return Quaternion.Slerp(stepHeadingFrom, stepHeadingTo, Smooth((time-step.swingStart)/(step.land-step.swingStart)));
        }
        private void TryRefreshFutureFacing(int nextStep)
        {
            if(!owner.ProductionAI||Path.forwardCurve==null||Path.suffixDuration>0||nextStep<1||nextStep>FinalPair
                ||(Path.hasReturn&&!ReturnEndpointReached)||arena.player.PlanarVelocity.sqrMagnitude<.04f)return;
            source.Sample(SourceTime,out float cut,out _,out _,out _,out _);
            if(cut<.05f||cut>.85f)return;
            Vector3 predicted=arena.player.transform.position+arena.player.PlanarVelocity*Mathf.Min(1.5f,source.duration-SourceTime);
            Vector3 direction=Vector3.ProjectOnPlane(predicted-Path.endpoint,Vector3.up);
            if(direction.sqrMagnitude<.01f)return;
            Quaternion facing=Quaternion.LookRotation(direction);
            if(Quaternion.Angle(facing,Path.finalHeading)<4)return;
            // Offline candidate search restores its caller's scratch arrays.
            // Runtime refresh must size fresh scratch for the active source.
            trialGoals=new Vector3[StepCount];trialRotations=new Quaternion[StepCount];trialOutward=new Vector3[StepCount];
            var old=Path;Vector3 start=old.Position(cut);Quaternion startHeading=old.forwardCurve.Heading(cut,old.splitProgress);
            foreach(float radius in new[]{.65f,.85f,1.05f})
            foreach(int first in new[]{1,-1})foreach(int second in new[]{1,-1})
            {
                if(!ArenaForwardPatrolCurve.TryArcLineArc(start,startHeading,old.endpoint,facing,radius,first,second,out var tail))continue;
                var curve=ArenaForwardPatrolCurve.ReplaceFuture(old.forwardCurve,cut,old.splitProgress,tail);
                if(curve==null||curve.Length>forwardMaximumPathLength)continue;
                bool clear=true;Vector3 prior=start;Quaternion priorHeading=startHeading;
                for(int i=1;i<=128;i++)
                {
                    float p=Mathf.Lerp(cut,1,i/128f);Vector3 root=curve.Position(p,old.splitProgress);Quaternion heading=curve.Heading(p,old.splitProgress);
                    float distance=Vector3.Distance(root,prior);
                    if((distance>.00001f&&Quaternion.Angle(priorHeading,heading)/distance>150)||!ClearRootSegment(prior,root)){clear=false;break;}
                    prior=root;priorHeading=heading;
                }
                if(!clear)continue;
                var candidate=old;candidate.forwardCurve=curve;candidate.finalHeading=facing;candidate.length=curve.Length;
                if(!Preflight(ref candidate,true,out _,nextStep,old))continue;
                // All past curve samples and committed contacts remain exact.
                // Only the next airborne foot and later contacts use this plan.
                Path=candidate;finalHeading=facing;
                Record("boss_patrol_future_facing_updated","Fresh full preflight with frozen prior contacts; next step="+nextStep);
                return;
            }
        }
        private void EvaluateFeet(float time, bool useSourceTilt)
        {
            source.Sample(time, out _, out _, out _, out float leftLift, out float rightLift);
            for (int i = 0; i < 2; i++)
            {
                var leg = legs[i];
                if (!leg.swinging) { leg.EvaluatePlant(); continue; }
                var step = source.steps[StepIndex]; float blend = Smooth((time-step.swingStart)/(step.land-step.swingStart));
                Quaternion tilt = useSourceTilt ? Quaternion.Inverse(leg.GuardFootRotation)*Quaternion.Inverse(owner.transform.rotation)*leg.foot.rotation : Quaternion.identity;
                leg.EvaluateSwing(blend, i==0?leftLift:rightLift, tilt);
            }
        }
        private bool CheckReach(Vector3 rootDelta)
        {
            pelvis.localPosition=sourcePelvisLocal;float adjustment=0;
            foreach(var leg in legs)adjustment=Mathf.Max(adjustment,leg.RequiredLowering(leg.hip.position+rootDelta,leg.AnkleTarget,reachMargin));
            var envelope=Path.pelvisEnvelope;
            if(envelope!=null&&envelope.Length>1)
            {
                float sample=Mathf.Clamp(posedTime*source.fps,0,envelope.Length-1);
                int i=Mathf.FloorToInt(sample);
                adjustment=Mathf.Max(adjustment,Mathf.Lerp(envelope[i],envelope[Mathf.Min(i+1,envelope.Length-1)],sample-i));
            }
            if(adjustment>maximumPelvisAdjustment)return false;
            pelvis.position-=Vector3.up*adjustment;CurrentPelvisAdjustment=adjustment;
            MaximumPelvisAdjustment=Mathf.Max(MaximumPelvisAdjustment,adjustment);
            foreach (var leg in legs)
                if (!leg.Reachable(leg.hip.position+rootDelta, leg.AnkleTarget, reachMargin, out _)) return false;
            return true;
        }
        private void PoseSource(float time,float delta)
        {driver.PoseContactClip(clips.Clip(ClipId),time,delta);sourcePelvisLocal=pelvis.localPosition;posedTime=time;}
        private static float[] AnticipatedPelvis(float[] required,float fps)
        {
            if(required==null)return null;
            var result=new float[required.Length];
            int radius=Mathf.CeilToInt(.4f*fps);var kernel=new float[radius+1];
            for(int k=0;k<=radius;k++)kernel[k]=.5f+.5f*Mathf.Cos(Mathf.PI*k/radius);
            for(int i=0;i<result.Length;i++)
            {
                float envelope=0;
                for(int j=Mathf.Max(0,i-radius);j<=Mathf.Min(required.Length-1,i+radius);j++)
                    envelope=Mathf.Max(envelope,required[j]*kernel[Mathf.Abs(i-j)]);
                float edge=Smooth(Mathf.Min(i,required.Length-1-i)/(float)radius);
                result[i]=Mathf.Max(required[i],envelope*edge);
            }
            return result;
        }
        private bool ApplyFeet(float time, bool useSourceTilt)
        {
            EvaluateFeet(time, useSourceTilt);
            if(!CheckReach(Vector3.zero))return SolveFailed("coupled pelvis/reach check",time);
            for(int side=0;side<legs.Length;side++)
            {
                var leg=legs[side];bool solved=leg.Solve(reachMargin, out float ankleError, out float boneError, out float margin);
                MaximumAnkleError = Mathf.Max(MaximumAnkleError, ankleError); MaximumBoneLengthError = Mathf.Max(MaximumBoneLengthError,boneError);
                MinimumReachMargin = Mathf.Min(MinimumReachMargin,margin);
                if(!solved)return SolveFailed((side==0?"L":"R")+" analytic chain; ankle="+ankleError.ToString("F7")+" bone="+boneError.ToString("F7")+" margin="+margin.ToString("F7"),time);
            }
            // A positive lateral ordering is necessary but not a substitute for
            // the separate runtime mesh/twist/occlusion review.
            if (Vector3.Dot(RightSole-LeftSole, owner.transform.right) < MinimumSoleSeparation) return SolveFailed("actual sole lateral ordering below .20m",time);
            return true;
        }
        private bool SolveFailed(string reason,float time)
        {
            LastSolveFailure=reason;
            LastFailedSolve=new ArenaPatrolSolveObservation {reason=reason,sourceTime=time,yaw=owner.transform.eulerAngles.y,
                actor=owner.transform.position,collisionFlags=LastCollisionFlags,pelvisAdjustment=CurrentPelvisAdjustment,
                lateralSeparation=Vector3.Dot(RightSole-LeftSole,owner.transform.right),left=legs[0].Observe("L"),right=legs[1].Observe("R")};
            Debug.Log("SAM_PATROL_SOLVE_FAILURE "+JsonUtility.ToJson(LastFailedSolve));
            return false;
        }
        private void Finish()
        {
            if (!At(Path.endpoint) || (Path.hasReturn && !ReturnEndpointReached) || legs[0].swinging || legs[1].swinging)
            { Fail("Full source ended without both planted feet and the actual required endpoints."); return; }
            FinalFacingError = Vector3.Angle(owner.transform.forward, Vector3.ProjectOnPlane(arena.player.transform.position-owner.transform.position,Vector3.up));
            if (!headingFinalized || Quaternion.Angle(owner.transform.rotation,finalHeading) > .01f)
            { Fail("Final body turn did not finish inside every step of the selected complete source."); return; }
            FinalEndpointReached = true; State = ArenaPatrolMotionState.Complete;
            RecordPlayerYield("complete");
            Record("boss_patrol_final_endpoint_reached", "Actual final endpoint and both boot contacts reached; live target angular error="+FinalFacingError);
            foundation.ResumeGuard(0);
        }
        public void Cancel(string reason, bool dead = false)
        {
            if (!Active) return; deathPending |= dead;
            if (State != ArenaPatrolMotionState.Settling) Fail(reason);
        }
        private void Fail(string reason)
        {
            if (State == ArenaPatrolMotionState.Settling) return;
            Failure = reason; State = ArenaPatrolMotionState.Settling; settleTime = 0;
            RecordPlayerYield("interrupted");
            foreach (var leg in legs) leg.BeginEmergency(owner.transform.position.y, reachMargin);
            Record("boss_patrol_motion_invalid", reason+"; actual moving foot must settle before relinquishing the rig.");
        }
        private void Settle(float delta)
        {
            settleTime += delta; Elapsed += delta;
            if (owner.capsule.isGrounded && vertical < 0) vertical = -2;
            vertical = Mathf.Max(-25,vertical-24*delta); LastCollisionFlags = owner.capsule.Move(Vector3.up*vertical*delta);
            PoseSource(SourceTime,0);
            bool solved = true;
            foreach (var leg in legs)
            {
                leg.Emergency(Smooth(settleTime/.25f));
            }
            solved=CheckReach(Vector3.zero);
            if(solved)foreach(var leg in legs)solved &= leg.Solve(0,out _,out _,out _);
            if (settleTime >= .25f && solved && owner.capsule.isGrounded)
            {
                State = ArenaPatrolMotionState.Invalid;
                Record("boss_patrol_invalid_settled", Failure);
                if (deathPending) foundation.BeginDeath(); else foundation.ResumeGuard(.20f);
            }
            // No timeout pretends that a physically impossible settle succeeded.
        }
        private bool At(Vector3 point) => Vector3.ProjectOnPlane(owner.transform.position-point,Vector3.up).magnitude <= endpointEpsilon;
        // CharacterController.Move remains the authority for BOTH characters.
        // A late sword lunge can enter an already validated route. A massive
        // walking boss yields the light grounded player by only the impending
        // contact displacement, rather than freezing its authored airborne foot.
        // See Unity CharacterController.Move / OnControllerColliderHit examples;
        // their Rigidbody velocity example is deliberately not used for a CC.
        private void YieldPlayerAtContact(Vector3 bossBefore, Vector3 request)
        {
            if (!owner.ProductionAI || arena == null || !arena.Active || request.sqrMagnitude < 1e-10f
                || (arena.view != null && arena.view.CinematicActive)) return;
            var player=arena.player;
            if (player == null || player.fighter == null || player.fighter.Dead || player.capsule == null
                || !player.capsule.enabled || !player.capsule.isGrounded) return;
            var bossBody=owner.capsule; var playerBody=player.capsule;
            // These patrol sources require upright, grounded body capsules.
            if (Vector3.Dot(bossBody.transform.up,Vector3.up)<.999f
                || Vector3.Dot(playerBody.transform.up,Vector3.up)<.999f) return;
            Vector3 bossScale=bossBody.transform.lossyScale, playerScale=playerBody.transform.lossyScale;
            float bossRadius=bossBody.radius*Mathf.Max(Mathf.Abs(bossScale.x),Mathf.Abs(bossScale.z));
            float playerRadius=playerBody.radius*Mathf.Max(Mathf.Abs(playerScale.x),Mathf.Abs(playerScale.z));
            Vector3 bossCentre=bossBody.transform.TransformPoint(bossBody.center);
            Vector3 playerCentre=playerBody.transform.TransformPoint(playerBody.center);
            Vector3 separation=Vector3.ProjectOnPlane(playerCentre-bossCentre-request,Vector3.up);
            if (Vector3.Dot(request,separation)<=0) return;
            float bossHalf=Mathf.Max(0,bossBody.height*Mathf.Abs(bossScale.y)*.5f-bossRadius);
            float playerHalf=Mathf.Max(0,playerBody.height*Mathf.Abs(playerScale.y)*.5f-playerRadius);
            float verticalGap=Mathf.Max(0,Mathf.Abs(bossCentre.y-playerCentre.y)-bossHalf-playerHalf);
            float skin=bossBody.skinWidth+playerBody.skinWidth;
            float clearance=bossRadius+playerRadius+skin;
            if (verticalGap>=clearance) return;
            float requiredPlanar=Mathf.Sqrt(clearance*clearance-verticalGap*verticalGap);
            float distance=separation.magnitude;
            if (distance>=requiredPlanar || distance<.00001f) return;
            // Do not displace a player for a boss step already blocked by a wall.
            if (!ClearRootSegment(bossBefore,bossBefore+request,false)) return;
            LastPlayerYieldLimit=request.magnitude+skin;
            float amount=Mathf.Min(requiredPlanar-distance,LastPlayerYieldLimit);
            Vector3 direction=separation/distance, before=player.transform.position;
            CollisionFlags flags=playerBody.Move(direction*amount);
            Vector3 actual=Vector3.ProjectOnPlane(player.transform.position-before,Vector3.up);
            PlayerYieldSteps++; PlayerYieldRequestedDistance+=amount; PlayerYieldDistance+=actual.magnitude;
            MaximumPlayerYieldStep=Mathf.Max(MaximumPlayerYieldStep,actual.magnitude);
            MaximumPlayerYieldRequest=Mathf.Max(MaximumPlayerYieldRequest,amount);
            bool blocked=Vector3.Dot(actual,direction)<amount-.002f;
            if (blocked) PlayerYieldBlockedSteps++;
            if (PlayerYieldSteps==1)
                Record("boss_patrol_player_contact","Grounded player displaced through collision-constrained Move; no damage or action cancellation.");
            if (blocked && PlayerYieldBlockedSteps==1)
                Record("boss_patrol_player_contact_blocked","Player could not clear impending contact; world and boss obstruction gates remain active; flags="+flags);
        }
        private void RecordPlayerYield(string outcome)
        {
            if(PlayerYieldSteps==0)return;
            Record("boss_patrol_player_contact_summary",outcome+"; steps="+PlayerYieldSteps+" blocked="+PlayerYieldBlockedSteps
                +" requested="+PlayerYieldRequestedDistance.ToString("F6")+" actual="+PlayerYieldDistance.ToString("F6")
                +" maxRequest="+MaximumPlayerYieldRequest.ToString("F6")+" maxActual="+MaximumPlayerYieldStep.ToString("F6"));
        }
        private bool ClearRootSegment(Vector3 from, Vector3 to, bool includePlayer = true)
        {
            Vector3 delta=to-from; if(delta.sqrMagnitude<1e-10f)return true;
            var c=owner.capsule; Vector3 centre=from+owner.transform.rotation*c.center;
            float radius=c.radius-c.skinWidth*.5f, offset=Mathf.Max(0,c.height*.5f-c.radius);
            return !Physics.CapsuleCast(centre+Vector3.up*offset,centre-Vector3.up*offset,radius,delta.normalized,delta.magnitude+.015f,
                includePlayer?MovementMask:1<<8,QueryTriggerInteraction.Ignore);
        }
        private static bool Ground(Vector3 point,out Vector3 floor)
        {
            if(Physics.Raycast(point+Vector3.up*.50f,Vector3.down,out var hit,1.0f,1<<8,QueryTriggerInteraction.Ignore))
            {floor=hit.point;return hit.normal.y>.98f;}
            floor=default;return false;
        }
        private void Record(string kind,string detail) => arena?.Record(kind,detail,owner.fighter,0,owner.transform.position);
        private static float Smooth(float t) { t=Mathf.Clamp01(t);return t*t*t*(10+t*(-15+6*t)); }

        private sealed class Leg
        {
            public readonly Transform hip,knee,foot;
            public readonly Vector3 GuardSoleActor,FlatSoleLocal;
            public readonly Quaternion GuardFootRotation;
            public Vector3 goal;
            public bool swinging;
            private readonly ArenaBossBrain owner;
            private readonly float upper,lower;
            private readonly Vector3[] boot;
            private Vector3 planted,from,sole,soleLocal,emergencyFrom,emergencyTo;
            private Quaternion plantedRotation,fromRotation,goalRotation,rotation;
            private Vector3 lastDesiredKnee;
            private float upperBefore,lowerBefore,lastAnkleError,lastBoneError,lastMargin,lastPoleSquared;
            public Vector3 ActualSole => foot.TransformPoint(soleLocal);
            public Vector3 AnkleTarget => sole-rotation*soleLocal;
            public Leg(ArenaBossBrain boss,SkinnedMeshRenderer renderer,ArenaPatrolLegData data,ArenaPatrolSoleLegGeometry geometry,List<ArenaPatrolBindObservation> observations)
            {
                owner=boss; var bones=renderer.bones;
                hip=Find(bones,data.hip);knee=Find(bones,data.knee);foot=Find(bones,data.foot);
                if(knee.parent!=hip||foot.parent!=knee)throw new InvalidOperationException("Patrol IK requires the actual contiguous deform hip/knee/foot chain.");
                upper=Vector3.Distance(hip.position,knee.position);lower=Vector3.Distance(knee.position,foot.position);
                if(Mathf.Abs(upper-data.upperLength)>.002f||Mathf.Abs(lower-data.lowerLength)>.002f)throw new InvalidOperationException("Actual FBX leg lengths disagree with the authored contract.");
                var mesh=renderer.sharedMesh;
                if(!mesh.isReadable)throw new InvalidOperationException("Actual mesh must be readable for once-only rigid sole bind calibration.");
                int boneIndex=Array.IndexOf(bones,foot);var vertices=mesh.vertices;var weights=mesh.boneWeights;var bind=mesh.bindposes[boneIndex];
                var selected=new List<Vector3>(); var selectedIds=new List<int>();
                for(int i=0;i<vertices.Length;i++)
                {
                    var w=weights[i];float weight=(w.boneIndex0==boneIndex?w.weight0:0)+(w.boneIndex1==boneIndex?w.weight1:0)+(w.boneIndex2==boneIndex?w.weight2:0)+(w.boneIndex3==boneIndex?w.weight3:0);
                    if(weight>.9999f){selected.Add(bind.MultiplyPoint3x4(vertices[i]));selectedIds.Add(i);}
                }
                if(selected.Count<32)throw new InvalidOperationException("No reliable rigid foot-weighted boot geometry in the actual FBX.");
                boot=selected.ToArray();GuardFootRotation=Quaternion.Inverse(owner.transform.rotation)*foot.rotation;
                FlatSoleLocal=LowestPatch(foot.rotation);
                GuardSoleActor=owner.transform.InverseTransformPoint(foot.TransformPoint(FlatSoleLocal));
                Vector3 exported=ArenaPatrolSourceContract.V(data.guardSoleActor);
                var observation=ObserveBind(renderer,selectedIds,data.side,exported,geometry);
                if(observation.geometryGatePassed)
                {
                    // The source anchor uses original vertex multiplicities.
                    // Preserve that XZ point after proving the entire geometry;
                    // imported seam duplication must never redefine the anchor.
                    Vector3 anchor=exported;anchor.y=observation.geometryMinimum.y;
                    FlatSoleLocal=foot.InverseTransformPoint(owner.transform.TransformPoint(anchor));
                    GuardSoleActor=owner.transform.InverseTransformPoint(foot.TransformPoint(FlatSoleLocal));
                    observation.calibratedGuardSole=GuardSoleActor;
                    observation.anchorProjectionY=GuardSoleActor.y-exported.y;
                }
                observations.Add(observation);
                Debug.Log("SAM_PATROL_BIND " + JsonUtility.ToJson(observation));
                ResetActual();
            }
            private ArenaPatrolBindObservation ObserveBind(SkinnedMeshRenderer renderer,List<int> selectedIds,string side,Vector3 exported,ArenaPatrolSoleLegGeometry geometry)
            {
                var actorPoints=new Vector3[boot.Length];var unique=new List<Vector3>();var keys=new HashSet<Vector3Int>();
                Vector3 minimum=Vector3.positiveInfinity,maximum=Vector3.negativeInfinity;
                for(int i=0;i<boot.Length;i++)
                {
                    Vector3 point=owner.transform.InverseTransformPoint(foot.TransformPoint(boot[i]));actorPoints[i]=point;
                    minimum=Vector3.Min(minimum,point);maximum=Vector3.Max(maximum,point);
                    // Diagnostic deduplication only: geometric positions within a
                    // micrometre share a key; the actual runtime boot is unchanged.
                    var key=new Vector3Int(Mathf.RoundToInt(point.x*1000000),Mathf.RoundToInt(point.y*1000000),Mathf.RoundToInt(point.z*1000000));
                    if(keys.Add(key))unique.Add(point);
                }
                var row=new ArenaPatrolBindObservation {side=side,mesh=renderer.sharedMesh.name,rigidVertexCount=boot.Length,
                    uniqueRigidVertexCount=unique.Count,exportedGuardSole=exported,importedGuardSole=GuardSoleActor,
                    importedExportedDistance=Vector3.Distance(GuardSoleActor,exported),geometryMinimum=minimum,geometryMaximum=maximum,
                    footOriginActor=owner.transform.InverseTransformPoint(foot.position),footLossyScale=foot.lossyScale,rendererLossyScale=renderer.transform.lossyScale};
                row.originalCentroidGatePassed=row.importedExportedDistance<=.008f;
                row.rawPatch25=PatchCentroid(actorPoints,.0025f,out row.rawPatch25Count);
                row.rawPatch50=PatchCentroid(actorPoints,.005f,out row.rawPatch50Count);
                row.uniquePatch25=PatchCentroid(unique,.0025f,out row.uniquePatch25Count);
                row.uniquePatch50=PatchCentroid(unique,.005f,out row.uniquePatch50Count);
                var sourcePoints=new Vector3[geometry.guardVerticesActor.Length/3];
                for(int i=0;i<sourcePoints.Length;i++)sourcePoints[i]=new Vector3(geometry.guardVerticesActor[3*i],geometry.guardVerticesActor[3*i+1],geometry.guardVerticesActor[3*i+2]);
                row.sourceUniqueVertexCount=sourcePoints.Length;
                row.actualToSourceMaximumError=NearestMaximum(unique,sourcePoints);
                row.sourceToActualMaximumError=NearestMaximum(sourcePoints,unique);
                // One explicit bind-time geometry cross-check proves the actual
                // bone-local calibration against Unity skinning, not a socket.
                var baked=new Mesh {name="Patrol sole bind diagnostic"};
                try
                {
                    renderer.BakeMesh(baked,false);var bakedPoints=baked.vertices;double square=0;
                    for(int i=0;i<selectedIds.Count;i++)
                    {
                        Vector3 expected=foot.TransformPoint(boot[i]);
                        Vector3 actual=renderer.transform.TransformPoint(bakedPoints[selectedIds[i]]);float error=Vector3.Distance(expected,actual);
                        row.rigidBakeMaximumError=Mathf.Max(row.rigidBakeMaximumError,error);square+=error*error;
                    }
                    row.rigidBakeRmsError=(float)Math.Sqrt(square/selectedIds.Count);
                }
                finally {UnityEngine.Object.Destroy(baked);}
                row.geometryGatePassed=unique.Count==sourcePoints.Length&&row.actualToSourceMaximumError<=.001f&&row.sourceToActualMaximumError<=.001f
                    &&row.rigidBakeMaximumError<=.0005f&&Mathf.Abs(exported.y-minimum.y)<=.0025f
                    &&exported.x>=minimum.x&&exported.x<=maximum.x&&exported.z>=minimum.z&&exported.z<=maximum.z;
                return row;
            }
            private static float NearestMaximum(IList<Vector3> from,IList<Vector3> to)
            {
                float maximum=0;foreach(var point in from){float nearest=float.PositiveInfinity;foreach(var candidate in to)nearest=Mathf.Min(nearest,(point-candidate).sqrMagnitude);maximum=Mathf.Max(maximum,nearest);}
                return Mathf.Sqrt(maximum);
            }
            private static Vector3 PatchCentroid(IList<Vector3> points,float depth,out int count)
            {
                float minimum=float.PositiveInfinity;foreach(var point in points)minimum=Mathf.Min(minimum,point.y);
                Vector3 sum=Vector3.zero;count=0;foreach(var point in points)if(point.y<minimum+depth){sum+=point;count++;}
                return sum/count;
            }
            private static Transform Find(Transform[] bones,string name)
            {
                Transform result=null;foreach(var bone in bones)if(bone.name==name||name.EndsWith("/"+bone.name,StringComparison.Ordinal))
                {if(result!=null)throw new InvalidOperationException("Ambiguous deform bone "+name);result=bone;}
                return result!=null?result:throw new InvalidOperationException("Missing deform bone "+name);
            }
            private Vector3 LowestPatch(Quaternion worldRotation)
            {
                float minimum=float.PositiveInfinity;foreach(var p in boot)minimum=Mathf.Min(minimum,(worldRotation*p).y);
                Vector3 centre=Vector3.zero;int count=0;
                foreach(var p in boot){Vector3 q=worldRotation*p;if(q.y<=minimum+.005f){centre+=q;count++;}}
                centre/=count;centre.y=minimum;
                return Quaternion.Inverse(worldRotation)*centre;
            }
            private Vector3 RotatedSoleAnchor(Quaternion worldRotation)
            {
                float minimum=float.PositiveInfinity;foreach(var p in boot)minimum=Mathf.Min(minimum,(worldRotation*p).y);
                Vector3 anchor=worldRotation*FlatSoleLocal;anchor.y=minimum;
                return Quaternion.Inverse(worldRotation)*anchor;
            }
            public void ResetActual()
            {soleLocal=FlatSoleLocal;planted=sole=foot.TransformPoint(soleLocal);plantedRotation=rotation=foot.rotation;swinging=false;}
            public void BeginSwing(Vector3 target,Quaternion desired)
            {from=ActualSole;fromRotation=foot.rotation;goal=target;goalRotation=desired;swinging=true;}
            public void EvaluatePlant(){sole=planted;rotation=plantedRotation;soleLocal=FlatSoleLocal;}
            public void EvaluateSwing(float blend,float lift,Quaternion tilt)
            {sole=Vector3.Lerp(from,goal,blend)+Vector3.up*Mathf.Max(0,lift);rotation=Quaternion.Slerp(fromRotation,goalRotation,blend)*tilt;soleLocal=RotatedSoleAnchor(rotation);}
            public void CommitPlant(){planted=ActualSole;plantedRotation=rotation;soleLocal=FlatSoleLocal;swinging=false;}
            public bool Reachable(Vector3 h,Vector3 ankle,float reserve,out float margin)
            {
                float d=Vector3.Distance(h,ankle);margin=upper+lower-d;
                return d>Mathf.Abs(upper-lower)+.005f&&margin>=reserve;
            }
            public float RequiredLowering(Vector3 h,Vector3 ankle,float reserve)
            {
                float max=upper+lower-reserve,flat=Vector3.ProjectOnPlane(h-ankle,Vector3.up).magnitude;
                if(flat>=max||h.y<=ankle.y)return float.PositiveInfinity;
                float allowed=Mathf.Sqrt(max*max-flat*flat),needed=Mathf.Max(0,h.y-ankle.y-allowed);
                return needed>0?needed+.001f:0;
            }
            public bool Solve(float reserve,out float ankleError,out float boneError,out float margin)
            {
                ankleError=boneError=0;
                upperBefore=Vector3.Distance(hip.position,knee.position);lowerBefore=Vector3.Distance(knee.position,foot.position);
                if(!Reachable(hip.position,AnkleTarget,reserve,out margin)){lastMargin=margin;return false;}
                lastMargin=margin;
                Vector3 h=hip.position,target=AnkleTarget,line=(target-h).normalized;
                float d=Vector3.Distance(h,target),along=(upper*upper-lower*lower+d*d)/(2*d);
                Vector3 pole=Vector3.ProjectOnPlane(knee.position-h,line);
                lastPoleSquared=pole.sqrMagnitude;
                if(pole.sqrMagnitude<.0001f)pole=Vector3.ProjectOnPlane(owner.transform.forward,line);
                if(pole.sqrMagnitude<.0001f)return false;
                Vector3 desiredKnee=h+line*along+pole.normalized*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
                lastDesiredKnee=desiredKnee;
                hip.rotation=Quaternion.FromToRotation(knee.position-h,desiredKnee-h)*hip.rotation;
                knee.rotation=Quaternion.FromToRotation(foot.position-knee.position,target-knee.position)*knee.rotation;
                foot.rotation=rotation;
                ankleError=Vector3.Distance(foot.position,target);
                boneError=Mathf.Max(Mathf.Abs(Vector3.Distance(hip.position,knee.position)-upper),Mathf.Abs(Vector3.Distance(knee.position,foot.position)-lower));
                lastAnkleError=ankleError;lastBoneError=boneError;
                return ankleError<.002f&&boneError<.0005f;
            }
            public ArenaPatrolLegObservation Observe(string side)=>new ArenaPatrolLegObservation {side=side,hip=hip.position,knee=knee.position,
                foot=foot.position,desiredKnee=lastDesiredKnee,targetAnkle=AnkleTarget,targetSole=sole,actualSole=ActualSole,
                hipLocalScale=hip.localScale,kneeLocalScale=knee.localScale,footLocalScale=foot.localScale,
                pelvisLocalScale=hip.parent.localScale,pelvisLossyScale=hip.parent.lossyScale,
                hipLocalPosition=hip.localPosition,kneeLocalPosition=knee.localPosition,footLocalPosition=foot.localPosition,
                upperBind=upper,lowerBind=lower,upperBefore=upperBefore,lowerBefore=lowerBefore,
                upperAfter=Vector3.Distance(hip.position,knee.position),lowerAfter=Vector3.Distance(knee.position,foot.position),
                ankleError=lastAnkleError,boneError=lastBoneError,reachMargin=lastMargin,poleSquared=lastPoleSquared,swinging=swinging};
            public void BeginEmergency(float floor,float reserve)
            {
                emergencyFrom=ActualSole;emergencyTo=emergencyFrom;emergencyTo.y=floor;
                // Keep the exact current boot-local anchor. Replacing a tilted
                // swing anchor with its flat-pose anchor would move the measured
                // sole instantly before any physical settle takes place.
                rotation=foot.rotation;
                // Only an already airborne foot may relocate to a reachable
                // grounded emergency placement. A support boot keeps its anchor.
                if(swinging)
                {
                    Vector3 ankle=emergencyTo-rotation*soleLocal;float height=Mathf.Abs(hip.position.y-ankle.y);
                    float horizontal=Mathf.Sqrt(Mathf.Max(0,Mathf.Pow(upper+lower-reserve,2)-height*height));
                    Vector3 flat=Vector3.ProjectOnPlane(ankle-hip.position,Vector3.up);
                    if(flat.magnitude>horizontal)emergencyTo-=flat.normalized*(flat.magnitude-horizontal);
                }
                else emergencyTo=emergencyFrom;
            }
            public void Emergency(float blend){sole=Vector3.Lerp(emergencyFrom,emergencyTo,blend);if(blend>=1)swinging=false;}
        }
    }
}
