using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroTraversal
    {
        HeroRopeEntryMotion ropeEntry;
        HeroActionDefinition ropeUpDefinition,ropeUpHoldDefinition,ropeUpEntryDefinition,ropeDownDefinition;
        AnimationClip pairedRopeUpClip,pairedRopeHoldClip,pairedRopeDownClip;
        TextAsset pairedRopeUpCorrection,pairedRopeHoldCorrection,pairedRopeDownCorrection;
        void ResetRopeUpPair()
        {
            ResetRopeContact();ropeUpDefinition=ropeUpHoldDefinition=ropeUpEntryDefinition=ropeDownDefinition=null;
            pairedRopeDownClip=null;pairedRopeDownCorrection=null;
            pairedRopeUpClip=pairedRopeHoldClip=null;
            pairedRopeUpCorrection=pairedRopeHoldCorrection=null;
        }
        static bool RopeCycleShape(HeroActionDefinition d,float duration)
        {
            return d!=null&&d.clip&&d.loop&&Mathf.Abs(d.duration-duration)<.001f&&Mathf.Abs(d.clip.length-duration)<.002f;
        }
        void PrepareRopeUpPair(HeroActionDefinition entry,HeroActionDefinition hold)
        {
            ResetRopeUpPair();var up=hero.Library.Find("ROPE_UP");
            if(!RopeCycleShape(up,.96f)||up.blendDuration!=0||!up.canonicalRopeUp||up.clip!=up.ropeUpClip||
                !RopeCycleShape(hold,2.8f)||hold.clip!=up.ropeUpHoldClip||hold.clip!=entry.ropeBottomHoldClip||
                !hero.pose.PoseCorrectionReady(hold)||!hero.pose.PoseCorrectionReady(up))return;
            ropeUpDefinition=up;ropeUpHoldDefinition=hold;ropeUpEntryDefinition=entry;
            pairedRopeUpClip=up.clip;pairedRopeHoldClip=hold.clip;
            pairedRopeUpCorrection=up.poseCorrections;pairedRopeHoldCorrection=hold.poseCorrections;
            var down=hero.Library.Find("ROPE_DOWN");
            if(down!=null&&down.canonicalRopeDown&&RopeCycleShape(down,.96f)&&down.blendDuration==0&&
               down.clip==down.ropeDownClip&&down.ropeDownSourceUpClip==up.clip&&hero.pose.PoseCorrectionReady(down))
            {ropeDownDefinition=down;pairedRopeDownClip=down.clip;pairedRopeDownCorrection=down.poseCorrections;}
        }
        bool UsesPairedRopeUp(HeroPoseDriver pose)=>ActionId=="ROPE_UP"&&pose.CurrentAction=="ROPE_UP"&&RopeUpPairReady(pose);
        bool RopeUpPairReady(HeroPoseDriver pose)
        {
            // Do not parse HOLD again per frame: PoseCorrectionReady caches the current definition.
            var up=ropeUpDefinition;var hold=ropeUpHoldDefinition;var entry=ropeUpEntryDefinition;
            return canonicalRopeSession&&pose==hero.pose&&
                up==hero.Library.Find("ROPE_UP")&&hold==hero.Library.Find("ROPE_HOLD")&&entry==hero.Library.Find("ROPE_ENTER")&&
                entry!=null&&entry.canonicalRopeBottomEntry&&entry.ropeBottomHoldClip==pairedRopeHoldClip&&
                RopeCycleShape(up,.96f)&&up.blendDuration==0&&up.canonicalRopeUp&&up.clip==pairedRopeUpClip&&up.ropeUpClip==pairedRopeUpClip&&
                up.ropeUpHoldClip==pairedRopeHoldClip&&up.poseCorrectionClip==pairedRopeUpClip&&up.poseCorrections==pairedRopeUpCorrection&&
                RopeCycleShape(hold,2.8f)&&hold.clip==pairedRopeHoldClip&&hold.poseCorrectionClip==pairedRopeHoldClip&&hold.poseCorrections==pairedRopeHoldCorrection&&
                pairedRopeUpCorrection&&pairedRopeHoldCorrection&&(ropeDownDefinition==null||RopeDownPairReady())&&
                pose.PoseCorrectionReady(ropeContact!=null&&ropeContact.Current.Direction<0?ropeDownDefinition:up);
        }
        bool RopeDownPairReady()
        {
            var down=ropeDownDefinition;
            return down==hero.Library.Find("ROPE_DOWN")&&RopeCycleShape(down,.96f)&&down.blendDuration==0&&down.canonicalRopeDown&&
                down.clip==pairedRopeDownClip&&down.ropeDownClip==pairedRopeDownClip&&down.ropeDownSourceUpClip==pairedRopeUpClip&&
                down.poseCorrectionClip==pairedRopeDownClip&&down.poseCorrections==pairedRopeDownCorrection&&pairedRopeDownCorrection;
        }
        bool canonicalRopeSession;
        // Ground approach uses the ordinary skin-width support calibration.
        public bool UsesGroundApproachPose=>phase==-4&&ropeEntry!=null&&(ropeEntry.State==HeroRopeEntryMotion.Stage.Approach||ropeEntry.State==HeroRopeEntryMotion.Stage.Settle);
        bool PrepareRopeBottomEntry(HeroTraversalSurface target,HeroActionDefinition entry)
        {
            var hold=hero.Library.Find("ROPE_HOLD");
            var stop=hero.Library.Find("LOC_STOP");
            if(stop==null||!stop.clip||!stop.groundStop||stop.loop||Mathf.Abs(stop.duration-HeroRopeEntryMotion.SettleDuration)>.001f||Mathf.Abs(stop.clip.length-stop.duration)>.002f)return false;
            if(!entry.clip||entry.clip!=entry.ropeBottomEntryClip||entry.loop||Mathf.Abs(entry.duration-.8f)>.001f||Mathf.Abs(entry.clip.length-entry.duration)>.002f||
                hold==null||!hold.clip||hold.clip!=entry.ropeBottomHoldClip||!hold.loop||Mathf.Abs(hold.duration-2.8f)>.001f||Mathf.Abs(hold.clip.length-hold.duration)>.002f||!hero.pose.PoseCorrectionReady(entry)||!hero.pose.PoseCorrectionReady(hold))return false;
            if(!HeroRopeEntryMotion.TryPrepare(capsule,target,out ropeEntry))return false;
            PrepareRopeUpPair(entry,hold);return true;
        }
        void UpdateRopeBottomEntry(float dt,bool cancel)
        {
            if(ropeEntry==null){Cancel();return;}
            if(cancel||(vitals&&!vitals.CanExert)){if(cancel)movement.ConsumeJumpInputForFrame();Cancel();return;}
            var step=ropeEntry.Tick(dt);
            if(ropeEntry.State==HeroRopeEntryMotion.Stage.Cancelled){Cancel();return;}
            if(ropeEntry.State==HeroRopeEntryMotion.Stage.Approach)
            {
                if(step.planarTravel>.0001f){Action("LOC_WALK");var walk=hero.Library.Find(ActionId);ActionTime+=step.planarTravel*Mathf.Max(.01f,walk.duration)/Mathf.Max(.01f,walk.cycleDistance);}
                else{Action("LOC_IDLE");ActionTime+=dt;}
                return;
            }
            if(ropeEntry.State==HeroRopeEntryMotion.Stage.Settle){Action("LOC_STOP");ActionTime=ropeEntry.SettleTime;return;}
            Action("ROPE_ENTER");ActionTime=ropeEntry.GripTime;
            if(ropeEntry.State==HeroRopeEntryMotion.Stage.Complete){phase=2;phaseTime=0;along=0;Action("ROPE_HOLD");}
        }
    }

    // Collision and support state shared by the runtime owner and isolated physics verification.
    // This profile is deliberately limited to the measured upright .035m rope / .05m skin host.
    public sealed class HeroRopeEntryMotion
    {
        public enum Stage { Approach,Settle,Grip,Complete,Cancelled }
        public const float SettleDuration=.36f;
        public Stage State {get;private set;}
        public float GripTime {get;private set;}
        public float SettleTime {get;private set;}
        public CapsuleCollider Rope {get;private set;}
        public Vector3 Entry {get;private set;}
        public readonly struct Step
        {
            public readonly float planarTravel;
            public Step(float planarTravel){this.planarTravel=planarTravel;}
        }
        CharacterController body;
        HeroTraversalSurface facility;
        Quaternion facing;
        Vector3 ropeCenter,ropeScale,facilityTop,bodyCenter;
        Quaternion ropeRotation;float ropeRadius,ropeHeight,bodyStep,bodyMinMove;
        float elapsed,stalled,previousY,routeProgress,routeLength;
        Vector3[] route;float[] routeDistances;bool fineAlignment;
        readonly Collider[] overlaps=new Collider[32];
        readonly RaycastHit[] hits=new RaycastHit[24];
        HeroRopeEntryMotion(){}
        public static bool TryPrepare(CharacterController body,HeroTraversalSurface facility,out HeroRopeEntryMotion motion)
        {
            motion=null;
            if(!body||!body.enabled||!facility||facility.kind!=HeroTraversalKind.Rope||!facility.bottom||!facility.top||
                Vector3.Distance(body.transform.lossyScale,Vector3.one)>.001f||Vector3.Dot(body.transform.up,Vector3.up)<.999f||
                Mathf.Abs(body.height-1.8f)>.001f||Mathf.Abs(body.radius-.3f)>.001f||Vector3.Distance(body.center,Vector3.up*.9f)>.001f||Mathf.Abs(body.skinWidth-.05f)>.001f||
                Vector3.Distance(facility.transform.lossyScale,Vector3.one)>.001f||Vector3.Dot(facility.transform.up,Vector3.up)<.999f)return false;
            var span=facility.Top-facility.Bottom;
            if(span.y<1||Vector3.ProjectOnPlane(span,Vector3.up).magnitude>.005f)return false;
            // Reverse-facing turns need an authored stepping contract; a tight cubic is not one.
            var approachForward=Vector3.ProjectOnPlane(body.transform.forward,Vector3.up);
            var facilityForward=Vector3.ProjectOnPlane(facility.transform.forward,Vector3.up);
            if(approachForward.sqrMagnitude<.99f||facilityForward.sqrMagnitude<.99f||Vector3.Angle(approachForward,facilityForward)>120.001f)return false;
            var m=new HeroRopeEntryMotion{body=body,facility=facility,Entry=facility.Bottom,facing=Quaternion.LookRotation(facility.transform.forward,Vector3.up),previousY=body.transform.position.y};
            foreach(var c in facility.GetComponentsInChildren<CapsuleCollider>())
            {
                if(!c.enabled||c.isTrigger||c.direction!=1||Vector3.Dot(c.transform.up,Vector3.up)<.999f)continue;
                var scale=c.transform.lossyScale;float radius=c.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
                var center=c.transform.TransformPoint(c.center);var axis=facility.Bottom+facility.transform.forward*.25f;
                float height=Mathf.Max(c.height*Mathf.Abs(scale.y),radius*2);
                if(Mathf.Abs(radius-.035f)>.001f||Vector3.ProjectOnPlane(center-axis,Vector3.up).magnitude>.005f||center.y-height*.5f>facility.Bottom.y+.01f||center.y+height*.5f<facility.Top.y+1.8f)continue;
                if(m.Rope)return false;m.Rope=c;m.ropeCenter=center;
            }
            if(!m.Rope||Vector3.Distance(body.transform.position,m.Entry)>facility.reach||Vector3.Distance(body.transform.position,m.Entry)>1.5f||
                !m.Clear(body.transform.position,m.Rope)||!m.Clear(m.Entry,m.Rope)||!m.FloorMatches(body.transform.position)||!m.FloorMatches(m.Entry))return false;
            m.ropeScale=m.Rope.transform.lossyScale;m.ropeRotation=m.Rope.transform.rotation;m.ropeRadius=m.Rope.radius;m.ropeHeight=m.Rope.height;m.facilityTop=facility.Top;m.bodyCenter=body.center;m.bodyStep=body.stepOffset;m.bodyMinMove=body.minMoveDistance;
            if(!m.PrepareRoute())return false;
            motion=m;return true;
        }
        bool PrepareRoute()
        {
            Vector3 origin=body.transform.position;origin.y=Entry.y;
            Vector3 forward=facility.transform.forward,offset=origin-Entry;
            float side=Vector3.ProjectOnPlane(offset,forward).magnitude;
            fineAlignment=offset.magnitude<=.02f&&Quaternion.Angle(body.transform.rotation,facing)<1;
            if(fineAlignment||(side<.025f&&Vector3.Dot(offset,forward)<=.005f&&Quaternion.Angle(body.transform.rotation,facing)<1))
                return UseRoute(new[]{origin,Entry});
            // Circular turns have enough radius for the existing 1.2m/s, 240deg/s follower.
            // Four arc/straight/arc combinations with three lead-ins are checked, not a general navigation solver.
            const float radius=.32f;
            Vector2 start=new Vector2(offset.x,offset.z);
            Vector2 first=new Vector2(body.transform.forward.x,body.transform.forward.z).normalized,last=new Vector2(forward.x,forward.z).normalized;
            var candidates=new System.Collections.Generic.List<Vector3[]>();
            foreach(float setback in new[]{.35f,.65f,.95f})for(int s1=-1;s1<=1;s1+=2)for(int s2=-1;s2<=1;s2+=2)
            {
                Vector2 end=new Vector2(-forward.x*setback,-forward.z*setback);
                Vector2 c1=start+Left(first)*(s1*radius),c2=end+Left(last)*(s2*radius),delta=c2-c1;
                float d=delta.magnitude,k=(s2-s1)*radius;
                if(d<.00001f||d<Mathf.Abs(k))continue;
                float tangent=Mathf.Atan2(delta.y,delta.x)-Mathf.Asin(Mathf.Clamp(k/d,-1,1));
                Vector2 direction=new Vector2(Mathf.Cos(tangent),Mathf.Sin(tangent));
                Vector2 t1=c1-Left(direction)*(s1*radius),t2=c2-Left(direction)*(s2*radius);
                float a1=Mathf.Atan2((start-c1).y,(start-c1).x),a2=Mathf.Atan2((t2-c2).y,(t2-c2).x);
                float turn1=PositiveAngle(s1*(Mathf.Atan2((t1-c1).y,(t1-c1).x)-a1));
                float turn2=PositiveAngle(s2*(Mathf.Atan2((end-c2).y,(end-c2).x)-a2));
                if(radius*(turn1+turn2)+Vector2.Distance(t1,t2)+setback>3.5f)continue;
                var points=new System.Collections.Generic.List<Vector3>();points.Add(origin);
                AddArc(points,c1,a1,s1*turn1,radius);AddLine(points,t2);
                AddArc(points,c2,a2,s2*turn2,radius);AddLine(points,end);points.Add(Entry);
                candidates.Add(points.ToArray());
            }
            candidates.Sort((a,b)=>Length(a).CompareTo(Length(b)));
            foreach(var candidate in candidates)if(UseRoute(candidate))return true;
            return false;
        }
        static Vector2 Left(Vector2 v)=>new Vector2(-v.y,v.x);
        static float PositiveAngle(float a){a=Mathf.Repeat(a,2*Mathf.PI);return a<.00001f||2*Mathf.PI-a<.00001f?0:a;}
        static float Length(Vector3[] points){float length=0;for(int i=1;i<points.Length;i++)length+=Vector3.Distance(points[i-1],points[i]);return length;}
        void AddArc(System.Collections.Generic.List<Vector3> points,Vector2 center,float start,float turn,float radius)
        {
            int count=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(turn)*radius/.01f));
            for(int i=1;i<=count;i++){float a=start+turn*i/count;var p=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;points.Add(Entry+new Vector3(p.x,0,p.y));}
        }
        void AddLine(System.Collections.Generic.List<Vector3> points,Vector2 target)
        {
            Vector3 end=Entry+new Vector3(target.x,0,target.y),start=points[points.Count-1];
            int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(start,end)/.01f));
            for(int i=1;i<=count;i++)points.Add(Vector3.Lerp(start,end,i/(float)count));
        }
        bool UseRoute(Vector3[] candidate)
        {
            route=candidate;routeDistances=new float[route.Length];
            for(int i=1;i<route.Length;i++)routeDistances[i]=routeDistances[i-1]+Vector3.Distance(route[i-1],route[i]);
            routeLength=routeDistances[route.Length-1];
            if(routeLength>3.5f||!CanFollowRoute())return false;
            float radialMinimum=Mathf.Min(.25f,Vector3.ProjectOnPlane(route[0]-ropeCenter,Vector3.up).magnitude)-.001f;
            for(int i=0;i<route.Length;i++)
            {
                var previous=i==0?route[i]:route[i-1];int samples=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(previous,route[i])/.04f));
                for(int j=0;j<=samples;j++)
                {
                    var point=Vector3.Lerp(previous,route[i],j/(float)samples);
                    if(Vector3.ProjectOnPlane(point-ropeCenter,Vector3.up).magnitude<radialMinimum||!Clear(point,Rope)||!FloorMatches(point))return false;
                }
            }
            return true;
        }
        Vector3 RoutePoint(float distance)
        {
            distance=Mathf.Clamp(distance,0,routeLength);
            int lo=1,hi=route.Length-1;
            while(lo<hi){int mid=(lo+hi)/2;if(distance<=routeDistances[mid])hi=mid;else lo=mid+1;}
            return Vector3.Lerp(route[lo-1],route[lo],Mathf.InverseLerp(routeDistances[lo-1],routeDistances[lo],distance));
        }
        public bool Clear(Vector3 root,Collider allow)
        {
            var center=root+body.transform.rotation*body.center;float half=body.height*.5f-body.radius;
            int count=Physics.OverlapCapsuleNonAlloc(center+Vector3.up*half,center-Vector3.up*half,body.radius+.005f,overlaps,~0,QueryTriggerInteraction.Ignore);
            if(count>=overlaps.Length)return false;
            for(int i=0;i<count;i++)if(overlaps[i]&&overlaps[i]!=allow&&!overlaps[i].transform.IsChildOf(body.transform))return false;
            return true;
        }
        public bool FloorMatches(Vector3 root,float tolerance=.015f)
        {
            // Sample a small support patch, not only the root ray, to reject a narrow edge.
            for(int i=0;i<5;i++)
            {
                Vector3 offset=i==0?Vector3.zero:i==1?Vector3.right*.13f:i==2?Vector3.left*.13f:i==3?Vector3.forward*.13f:Vector3.back*.13f;
                int count=Physics.RaycastNonAlloc(root+offset+Vector3.up*.15f,Vector3.down,hits,.35f,~0,QueryTriggerInteraction.Ignore);
                if(count>=hits.Length)return false;bool found=false;float height=float.NegativeInfinity;
                for(int j=0;j<count;j++)if(hits[j].collider&&hits[j].collider!=Rope&&!hits[j].collider.transform.IsChildOf(body.transform)&&hits[j].normal.y>.99f&&hits[j].point.y>height){height=hits[j].point.y;found=true;}
                if(!found||Mathf.Abs(root.y-height-.05f)>tolerance)return false;
            }
            return true;
        }
        public bool ContactGeometryUnchanged=>GeometryUnchanged();
        bool GeometryUnchanged()=>facility&&facility.isActiveAndEnabled&&Rope&&Rope.enabled&&!Rope.isTrigger&&
            Vector3.Distance(facility.Bottom,Entry)<.001f&&Quaternion.Angle(facility.transform.rotation,facing)<.1f&&Vector3.Distance(Rope.transform.TransformPoint(Rope.center),ropeCenter)<.001f&&
            Vector3.Distance(facility.Top,facilityTop)<.001f&&Vector3.Distance(facility.transform.lossyScale,Vector3.one)<.001f&&
            Rope.direction==1&&Mathf.Abs(Rope.radius-ropeRadius)<.00001f&&Mathf.Abs(Rope.height-ropeHeight)<.00001f&&Vector3.Distance(Rope.transform.lossyScale,ropeScale)<.0001f&&Quaternion.Angle(Rope.transform.rotation,ropeRotation)<.1f&&
            Vector3.Distance(body.transform.lossyScale,Vector3.one)<.001f&&Vector3.Dot(body.transform.up,Vector3.up)>.999f&&Mathf.Abs(body.height-1.8f)<.001f&&Mathf.Abs(body.radius-.3f)<.001f&&Mathf.Abs(body.skinWidth-.05f)<.001f&&Vector3.Distance(body.center,bodyCenter)<.001f&&Mathf.Abs(body.stepOffset-bodyStep)<.00001f&&Mathf.Abs(body.minMoveDistance-bodyMinMove)<.00001f;
struct ApproachPlan
        {
            public float requested,distance;
            public Vector3 remaining,delta;
            public Quaternion rotation;
        }
        ApproachPlan PlanApproach(Vector3 position,Quaternion rotation,float progress,float dt)
        {
            float requested=Mathf.Min(1.2f*dt,routeLength-progress);
            Vector3 remaining=Vector3.ProjectOnPlane(RoutePoint(progress+requested)-position,Vector3.up);
            float distance=remaining.magnitude;
            var desired=!fineAlignment&&distance>.0001f?Quaternion.LookRotation(remaining,Vector3.up):facing;
            var nextRotation=Quaternion.RotateTowards(rotation,desired,240*dt);
            float alignment=fineAlignment?1:distance>.0001f?Mathf.Max(0,Vector3.Dot(nextRotation*Vector3.forward,remaining.normalized)):0;
            return new ApproachPlan{requested=requested,distance=distance,remaining=remaining,rotation=nextRotation,delta=Vector3.ClampMagnitude(remaining,1.2f*dt*alignment)};
        }
        static float MeasuredAdvance(ApproachPlan plan,Vector3 actual)=>plan.distance>.0001f?plan.requested*Mathf.Clamp01(Vector3.Dot(actual,plan.remaining/plan.distance)/plan.distance):plan.requested;
        bool CanFollowRoute()
        {
            const float dt=1f/120;
            Vector3 position=body.transform.position;Quaternion rotation=body.transform.rotation;
            float progress=0,elapsed=0,idle=0;
            for(int i=0;i<720;i++)
            {
                elapsed+=dt;if(elapsed>6)return false;
                var plan=PlanApproach(position,rotation,progress,dt);
                var after=position+plan.delta;var actual=Vector3.ProjectOnPlane(after-position,Vector3.up);
                if(actual.magnitude>1.2f*dt+.001f)return false;
                if(actual.magnitude>.0001f)rotation=plan.rotation;
                float advance=MeasuredAdvance(plan,actual);progress=Mathf.Min(routeLength,progress+advance);
                idle=plan.requested>.0001f&&advance<.0005f?idle+dt:0;
                if(idle>.6f||Vector3.ProjectOnPlane(after-RoutePoint(progress),Vector3.up).magnitude>.06f)return false;
                position=after;
                if(Vector3.ProjectOnPlane(Entry-position,Vector3.up).magnitude<=.002f&&routeLength-progress<=.002f)
                    return Quaternion.Angle(rotation,facing)<=1&&Vector3.Distance(position,Entry)<=.015f;
            }
            return false;
        }
                public Step Tick(float dt)
        {
            // The approach curvature must not depend on the caller's render rate.
            // Stop at the first stage boundary so the exact settle endpoint stays visible.
            if(float.IsNaN(dt)||float.IsInfinity(dt)||dt>6.1f){State=Stage.Cancelled;return new Step(0);}
            if(State!=Stage.Approach||dt<=0)return TickSingle(dt);
            int count=Mathf.Max(1,Mathf.CeilToInt(dt*120));
            float consumed=0,travel=0;
            for(int i=0;i<count&&State==Stage.Approach;i++)
            {
                float step=i==count-1?dt-consumed:dt/count;
                travel+=TickSingle(step).planarTravel;
                consumed+=step;
            }
            if(travel>1.2f*dt+.001f)State=Stage.Cancelled;
            return new Step(travel);
        }
        Step TickSingle(float dt)
        {
            if(dt<=0||State==Stage.Complete||State==Stage.Cancelled)return new Step(0);
            if(!body||!body.enabled||!GeometryUnchanged()){State=Stage.Cancelled;return new Step(0);}
            if(State==Stage.Grip||State==Stage.Settle)
            {
                if(Vector3.Distance(body.transform.position,Entry)>.015f||Quaternion.Angle(body.transform.rotation,facing)>1||!Clear(body.transform.position,Rope)||!FloorMatches(body.transform.position)){State=Stage.Cancelled;return new Step(0);}
                if(State==Stage.Settle){if(SettleTime>=SettleDuration){State=Stage.Grip;GripTime=0;}else SettleTime=Mathf.Min(SettleDuration,SettleTime+dt);return new Step(0);}
                GripTime=Mathf.Min(.8f,GripTime+dt);if(GripTime>=.8f)State=Stage.Complete;return new Step(0);
            }
            elapsed+=dt;if(elapsed>6||!FloorMatches(body.transform.position)||!Clear(body.transform.position,Rope)){State=Stage.Cancelled;return new Step(0);}
            var before=body.transform.position;
            var plan=PlanApproach(before,body.transform.rotation,routeProgress,dt);
            float requested=plan.requested;
            var flags=body.Move(plan.delta-Vector3.up*(3*dt));
            var after=body.transform.position;var planar=Vector3.ProjectOnPlane(after-before,Vector3.up);
            float travel=planar.magnitude;
            // A blocked upright capsule must not rotate idle feet or hide a depenetration jump.
            if(travel>1.2f*dt+.001f||!Clear(after,Rope)){State=Stage.Cancelled;return new Step(travel);}
            if(travel>.0001f)body.transform.rotation=plan.rotation;
            float advance=MeasuredAdvance(plan,planar);
            routeProgress=Mathf.Min(routeLength,routeProgress+advance);
            float left=Vector3.ProjectOnPlane(Entry-after,Vector3.up).magnitude;
            stalled=requested>.0001f&&advance<.0005f?stalled+dt:0;
            bool supported=(flags&CollisionFlags.Below)!=0&&Mathf.Abs(after.y-previousY)<.001f&&FloorMatches(after);previousY=after.y;
            if(stalled>.6f||!FloorMatches(after)||Vector3.ProjectOnPlane(after-RoutePoint(routeProgress),Vector3.up).magnitude>.06f){State=Stage.Cancelled;return new Step(travel);}
            if(left<=.002f&&routeLength-routeProgress<=.002f&&supported)
            {
                // Do not pivot planted feet at arrival; the final straight must align the body.
                if(Quaternion.Angle(body.transform.rotation,facing)>1||Vector3.Distance(after,Entry)>.015f||!Clear(after,Rope)){State=Stage.Cancelled;return new Step(travel);}
                State=Stage.Settle;SettleTime=0;
            }
            return new Step(travel);
        }
    }
}


