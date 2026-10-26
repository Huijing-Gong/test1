using UnityEngine;
using UnityEngine.InputSystem;
namespace KingOfGraphic.Hero
{
    [DefaultExecutionOrder(-35),DisallowMultipleComponent]
    public sealed partial class HeroTraversal:MonoBehaviour
    {
        public bool Active=>surface!=null;
        public bool Exerting=>Active&&surface.kind!=HeroTraversalKind.Seat;
        public string ActionId {get;private set;}
        public float ActionTime {get;private set;}
        public HeroTraversalSurface Candidate {get;private set;}
        HeroTraversalSurface surface;
        HeroWorldAdapter hero;PlayerMovement movement;PlayerVitals vitals;CharacterController capsule;PlayerEquipment equipment;
        float along,phaseTime;int phase;bool ownsLock,fromTop,ledgeUpRearmed;
        Vector3 start,goal,entrySafe;
        readonly Collider[] clearance=new Collider[32];
        readonly System.Collections.Generic.List<Collider> ignored=new System.Collections.Generic.List<Collider>();
        public float MaximumHandContactError {get;private set;}
        void IgnoreContactSolids(){foreach(var c in surface.GetComponentsInChildren<Collider>())Ignore(c);if(surface.contactSolids!=null)foreach(var c in surface.contactSolids)Ignore(c);}
        void Ignore(Collider c){if(c&&c!=capsule&&!Physics.GetIgnoreCollision(capsule,c)){Physics.IgnoreCollision(capsule,c,true);ignored.Add(c);}}
        void Awake(){hero=GetComponent<HeroWorldAdapter>();movement=GetComponent<PlayerMovement>();vitals=GetComponent<PlayerVitals>();capsule=GetComponent<CharacterController>();equipment=GetComponent<PlayerEquipment>();}
        void OnDisable(){Cancel(false);}
        public void Cancel(bool preserveContactRecovery=true)
        {
            ResetRopeTopExit();ResetRopeGroundExit();
            bool measuredRope=canonicalRopeSession;
            bool measuredMantle=Active&&(phase==-1||(surface.kind==HeroTraversalKind.Mantle&&hero.Library.Find(ActionId)?.canonicalMantleEntry==true));
            if(preserveContactRecovery&&measuredMantle&&phase==1)BeginMantleRecovery();
            else if(!preserveContactRecovery)AbortContactRecovery();
            if(preserveContactRecovery&&RecoveringContact)ExtendRecoveryForDeath();
            if((Active||measuredRope)&&capsule&&capsule.enabled)ResolveContactExit();
            foreach(var c in ignored)if(c&&capsule)Physics.IgnoreCollision(capsule,c,false);ignored.Clear();
            if((measuredMantle||measuredRope)&&hero)hero.SynchronizeAfterTraversal();
            ropeEntry=null;canonicalRopeSession=false;ResetRopeUpPair();
            surface=null;Candidate=null;ActionId=null;ledgeTopDefinition=null;phase=0;if(vitals)vitals.ResetTraversal();
            if(!RecoveringContact&&ownsLock&&movement){movement.SetActionInputLocked(this,false);ownsLock=false;}
        }
        void ResolveContactExit(){
            for(int attempt=0;attempt<4;attempt++){bool moved=false;foreach(var c in ignored)if(c&&c.enabled&&Physics.ComputePenetration(capsule,transform.position,transform.rotation,c,c.transform.position,c.transform.rotation,out var direction,out var distance)){capsule.Move(direction*(distance+.025f));moved=true;}if(!moved)return;}
            if(!ClearStanding(transform.position)&&ClearStanding(entrySafe)){bool wasEnabled=capsule.enabled;capsule.enabled=false;transform.position=entrySafe;capsule.enabled=wasEnabled;}
        }
        bool ClearStanding(Vector3 feet,bool allowContact=false)
        {
            int count=Physics.OverlapCapsuleNonAlloc(feet+Vector3.up*.32f,feet+Vector3.up*1.48f,.28f,clearance,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)if(clearance[i]&&!clearance[i].transform.IsChildOf(transform)&&!(allowContact&&surface&&(clearance[i].transform.IsChildOf(surface.transform)||(surface.contactSolids!=null&&System.Array.IndexOf(surface.contactSolids,clearance[i])>=0))))return false;
            return true;
        }
        public bool TryEnter(HeroTraversalSurface target)
        {
            if(!target||!hero.AcceptInput||hero.Busy||!(movement.CanStartInteraction||((target.kind==HeroTraversalKind.Ledge||target.kind==HeroTraversalKind.Ladder)&&movement.SwimmingActive&&!movement.IsGameplayInputLocked))||!capsule.enabled)return false;
            if(target.kind!=HeroTraversalKind.Seat&&vitals&&!vitals.CanExert)return false;
            float bottom=(transform.position-target.Bottom).sqrMagnitude,top=(transform.position-target.Top).sqrMagnitude;
            if(Mathf.Min(bottom,top)>target.reach*target.reach)return false;
            if(target.kind==HeroTraversalKind.Ledge&&bottom<=top&&!CanApproachLedge(target))return false;
            if(!hero.PrepareAuthorStanding())return false;
            MaximumHandContactError=0;ledgeUpRearmed=false;ledgeTopDefinition=null;ropeEntry=null;canonicalRopeSession=false;surface=target;fromTop=top<bottom;along=fromTop?1:0;
            // A ledge always enters at its hanging root, including a descent from above.
            Vector3 entryGoal=target.kind==HeroTraversalKind.Ledge?target.Bottom:fromTop?target.Top:target.Bottom;
            if(!ClearStanding(entryGoal,true)&&target.kind!=HeroTraversalKind.Seat){surface=null;return false;}
            ownsLock=!movement.IsGameplayInputLocked;if(!ownsLock){surface=null;return false;}
            string mantleId=string.IsNullOrEmpty(target.mantleActionId)?"OBSTACLE_MANTLE":target.mantleActionId;
            var mantle=target.kind==HeroTraversalKind.Mantle?hero.Library.Find(mantleId):null;
            bool canonical=mantle!=null&&mantle.canonicalMantleEntry;
            if(canonical&&!PrepareCanonicalMantle(target,mantle)){surface=null;ownsLock=false;return false;}
            bool topLedge=fromTop&&target.kind==HeroTraversalKind.Ledge&&!movement.IsInWater&&!movement.SwimmingActive;
            if(topLedge&&!PrepareLedgeTopEntry(target)){surface=null;ownsLock=false;return false;}
            var rope=target.kind==HeroTraversalKind.Rope&&!fromTop?hero.Library.Find("ROPE_ENTER"):null;
            bool canonicalRope=rope!=null&&rope.canonicalRopeBottomEntry;
            if(canonicalRope&&!PrepareRopeBottomEntry(target,rope)){surface=null;ownsLock=false;ropeEntry=null;return false;}
            ResetContactRecovery();movement.SetActionInputLocked(this,true);equipment.StowSword();
            if(canonicalRope){entrySafe=transform.position;canonicalRopeSession=true;phase=-4;phaseTime=ActionTime=0;ActionId=null;Action("LOC_IDLE");Ignore(ropeEntry.Rope);return true;}
            if(topLedge){entrySafe=transform.position;phase=-2;phaseTime=ActionTime=ledgeApproachStall=0;ActionId=null;Action("LOC_IDLE");return true;}
            if(canonical){entrySafe=transform.position;phase=-1;phaseTime=ActionTime=approachStall=0;ActionId=null;Action("LOC_WALK");return true;}
            IgnoreContactSolids();
            entrySafe=transform.position;start=transform.position;goal=entryGoal;phase=1;phaseTime=0;
            switch(target.kind)
            {
                case HeroTraversalKind.Ladder:Action(fromTop?"LADDER_ENTER_TOP":"LADDER_ENTER");break;
                case HeroTraversalKind.Rope:Action("ROPE_ENTER");break;
                case HeroTraversalKind.Pole:Action("POLE_ENTER");break;
                case HeroTraversalKind.Ledge:Action(movement.IsInWater?"WATER_LEDGE_GRAB":"WALL_LEDGE_GRAB");break;
                case HeroTraversalKind.LowStep:Action("OBSTACLE_LOW_STEP");goal=target.Top;break;
                case HeroTraversalKind.Vault:Action("OBSTACLE_VAULT");goal=target.Top;break;
                case HeroTraversalKind.Scramble:Action("CLIMB_SCRAMBLE");goal=target.Top;break;
                case HeroTraversalKind.Mantle:Action(mantleId);goal=target.Top;break;
                default:Action("INTERACT_REST_ENTER");break;
            }
            transform.rotation=target.transform.rotation;return true;
        }
        bool CanApproachLedge(HeroTraversalSurface target)
        {
            Vector3 forward=Vector3.ProjectOnPlane(target.transform.forward,Vector3.up).normalized;
            return forward.sqrMagnitude>.99f&&Vector3.Dot(transform.forward,forward)>.25f&&
                Vector3.Dot(transform.position-target.Bottom,forward)<.25f;
        }
        void Action(string id){if(ActionId==id)return;ActionId=id;ActionTime=0;}
        float Duration=>PoseDefinition?.duration??hero.Library.Duration(ActionId);
        bool IsObstacle=>surface&&(surface.kind==HeroTraversalKind.LowStep||surface.kind==HeroTraversalKind.Vault||surface.kind==HeroTraversalKind.Mantle||surface.kind==HeroTraversalKind.Scramble);
        void Update()
        {
            // A destroyed facility compares null in Unity; release the lock before idle selection.
            if(canonicalRopeSession&&(!surface||!movement||!movement.isActiveAndEnabled)){Cancel(false);return;}
            if(!hero||!hero.Library||Time.timeScale<=0||Time.deltaTime<=0)return;
            if(RecoveringContact){UpdateMantleRecovery(Time.deltaTime);return;}
            var keys=Keyboard.current;
            if(!Active)
            {
                Candidate=null;if(!hero.AcceptInput||hero.Busy)return;
                var npc=GetComponent<PlayerInteraction>();if(npc&&npc.HasCurrentCandidate)return;
                float best=float.PositiveInfinity;
                foreach(var item in HeroTraversalSurface.Available)
                {
                    if(!item||!item.isActiveAndEnabled||(item.kind!=HeroTraversalKind.Seat&&vitals&&!vitals.CanExert))continue;
                    if(item.kind==HeroTraversalKind.Ledge&&(transform.position-item.Bottom).sqrMagnitude<=(transform.position-item.Top).sqrMagnitude&&!CanApproachLedge(item))continue;
                    float candidateDistance=Mathf.Min((transform.position-item.Bottom).sqrMagnitude,(transform.position-item.Top).sqrMagnitude);
                    if(candidateDistance<best&&candidateDistance<item.reach*item.reach){Candidate=item;best=candidateDistance;}
                }
                if(keys!=null&&keys.eKey.wasPressedThisFrame&&Candidate)TryEnter(Candidate);
                return;
            }
            if(!hero.AcceptInput)return;
            float dt=Time.deltaTime;phaseTime+=dt;
            if(phase==-6){UpdateRopeTopExit(dt,keys!=null&&keys.spaceKey.wasPressedThisFrame);return;}
            if(phase==-5){UpdateRopeGroundExit(dt,keys!=null&&keys.spaceKey.wasPressedThisFrame);return;}
            if(phase==-4){UpdateRopeBottomEntry(dt,keys!=null&&keys.spaceKey.wasPressedThisFrame);return;}
            if(phase==-1){UpdateMantleApproach(dt,keys!=null&&keys.spaceKey.wasPressedThisFrame);return;}
            if(phase==-2||phase==-3){UpdateLedgeTopEntry(dt,keys!=null&&keys.spaceKey.wasPressedThisFrame);return;}
            bool distanceDriven=phase==2&&(surface.kind==HeroTraversalKind.Ladder||surface.kind==HeroTraversalKind.Rope||surface.kind==HeroTraversalKind.Pole);
            if(!distanceDriven)ActionTime+=dt;
            if(Exerting&&vitals&&!vitals.CanExert&&phase<3){if(surface.kind==HeroTraversalKind.Mantle&&hero.Library.Find(ActionId)?.canonicalMantleEntry==true){Cancel();return;}BeginExit(false,true);}
            if(keys!=null&&keys.spaceKey.wasPressedThisFrame&&phase<3){if(surface.kind==HeroTraversalKind.Mantle&&hero.Library.Find(ActionId)?.canonicalMantleEntry==true){movement.ConsumeJumpInputForFrame();Cancel();return;}BeginExit(false,true);}
            if(phase==1)
            {
                float t=Mathf.Clamp01(phaseTime/Mathf.Max(.1f,Duration));
                var target=Vector3.Lerp(start,goal,Mathf.SmoothStep(0,1,t));
                var definition=hero.Library.Find(ActionId);
                var ascent=definition?.traversalAscent;
                bool authoredAscent=IsObstacle&&definition!=null&&definition.clip==definition.traversalAscentClip&&ascent!=null&&ascent.length>=2;
                if(authoredAscent){
                    var advance=definition.traversalAdvance;
                    if(advance!=null&&advance.length>=2)target=Vector3.Lerp(start,goal,Mathf.Clamp01(advance.Evaluate(t)));
                    target.y=Mathf.Lerp(start.y,goal.y,Mathf.Clamp01(ascent.Evaluate(t)));
                }
                else if(IsObstacle)target+=Vector3.up*Mathf.Sin(t*Mathf.PI)*(surface.kind==HeroTraversalKind.LowStep?.1f:.4f);
                capsule.Move(target-transform.position);
                if(definition!=null&&definition.canonicalMantleEntry&&Vector3.Distance(transform.position,target)>.06f){Cancel();return;}
                if(t>=1 && Vector3.Distance(transform.position,goal)<.18f)
                {
                    if(IsObstacle){Cancel();return;}
                    phase=2;phaseTime=0;Action(HoldAction());
                }
                else if(phaseTime>Duration+1){Cancel();}
                return;
            }
            if(phase==3||phase==4)
            {
                float t=Mathf.Clamp01(phaseTime/Mathf.Max(.1f,Duration));
                var exitDestination=Vector3.Lerp(start,goal,Mathf.SmoothStep(0,1,t));
                if(phase==3&&ActionId=="WALL_LEDGE_TOP"){
                    Vector3 above=new Vector3(start.x,goal.y,start.z);
                    exitDestination=t<.55f?Vector3.Lerp(start,above,Mathf.SmoothStep(0,1,t/.55f)):
                        Vector3.Lerp(above,goal,Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.55f)/.4f)));
                }
                var definition=hero.Library.Find(ActionId);
                if(phase==3&&definition!=null&&definition.TrySampleTraversalAscent(t,out float authoredY))
                {
                    var advance=definition.traversalAdvance;
                    if(advance!=null&&advance.length>=2)exitDestination=Vector3.Lerp(start,goal,Mathf.Clamp01(advance.Evaluate(t)));
                    exitDestination.y=Mathf.Lerp(start.y,goal.y,authoredY);
                }
                capsule.Move(exitDestination-transform.position);
                if(t>=1 && Vector3.Distance(transform.position,goal)<.18f)
                {
                    if(phase==3)Cancel();
                    else{var axis=surface.Top-surface.Bottom;along=axis.sqrMagnitude>.001f?Mathf.Clamp01(Vector3.Dot(transform.position-surface.Bottom,axis)/axis.sqrMagnitude):0;phase=2;phaseTime=0;Action(HoldAction());}
                }
                else if(phaseTime>Duration+1)
                {
                    if(phase==3){var anchor=start;start=transform.position;goal=anchor;phase=4;phaseTime=0;Action(HoldAction());}
                    else Cancel();
                }
                return;
            }
            if(surface.kind==HeroTraversalKind.Seat)
            {
                if(keys!=null&&(keys.eKey.wasPressedThisFrame||keys.wKey.wasPressedThisFrame||keys.sKey.wasPressedThisFrame))BeginExit(false,false);
                return;
            }
            float vertical=keys==null?0:(keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0);
            float horizontal=keys==null?0:(keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0);
            if(surface.kind==HeroTraversalKind.Ledge)
            {
                if(vertical<=.1f)ledgeUpRearmed=true;
                if(vertical>.1f&&ledgeUpRearmed){BeginExit(true,false);return;}
                Action(Mathf.Abs(horizontal)>.1f?horizontal>0?"LEDGE_SHIMMY_R":"LEDGE_SHIMMY_L":"WALL_LEDGE_HANG");
                var offset=Vector3.Dot(transform.position-surface.Bottom,surface.transform.right);
                float next=Mathf.Clamp(offset+horizontal*.45f*dt,-surface.lateralExtent,surface.lateralExtent);
                capsule.Move(surface.transform.right*(next-offset));return;
            }
            if(UpdatePairedRopeContact(vertical,dt))return;
            if(Mathf.Abs(vertical)<.1f){Action(HoldAction());ActionTime+=dt;return;}
            string prefix=surface.kind==HeroTraversalKind.Ladder?"LADDER":surface.kind==HeroTraversalKind.Rope?"ROPE":"POLE";
            Action(prefix=="POLE"&&vertical>0?"POLE_CLIMB":prefix+(vertical>0?"_UP":"_DOWN"));
            var motion=HeroClimbMotion.Move(capsule,surface.Bottom,surface.Top,vertical,dt);
            along=motion.progress;ActionTime+=motion.seconds;int endpoint=motion.endpoint;
            if(endpoint>0){if(surface.topExit)BeginExit(true,false);else Action(HoldAction());}else if(endpoint<0)BeginExit(false,false);
        }
        string HoldAction()=>surface.kind==HeroTraversalKind.Ladder?"LADDER_HOLD":surface.kind==HeroTraversalKind.Rope?"ROPE_HOLD":surface.kind==HeroTraversalKind.Pole?"POLE_HOLD":surface.kind==HeroTraversalKind.Ledge?"WALL_LEDGE_HANG":"INTERACT_REST_HOLD";
        bool HasLedgeTopSupport(Vector3 feet)
        {
            float radius=Mathf.Max(.05f,capsule.radius-capsule.skinWidth);
            float floor=float.NaN;
            for(int i=0;i<5;i++){
                Vector3 offset=i==0?Vector3.zero:i==1?Vector3.right*radius:i==2?Vector3.left*radius:i==3?Vector3.forward*radius:Vector3.back*radius;
                int count=Physics.RaycastNonAlloc(feet+offset+Vector3.up*.2f,Vector3.down,ledgeSupportHits,.5f,~0,QueryTriggerInteraction.Ignore);
                RaycastHit hit=default;float nearest=float.PositiveInfinity;
                for(int k=0;k<count;k++)if(ledgeSupportHits[k].collider&&!ledgeSupportHits[k].transform.IsChildOf(transform)&&ledgeSupportHits[k].distance<nearest){hit=ledgeSupportHits[k];nearest=hit.distance;}
                if(!hit.collider||hit.normal.y<.707f||hit.collider.GetComponentInParent<NonClimbableSurface>())return false;
                if(i==0)floor=hit.point.y;else if(Mathf.Abs(hit.point.y-floor)>.12f)return false;
            }
            return true;
        }
        readonly RaycastHit[] ledgeSupportHits=new RaycastHit[24];
        void BeginExit(bool top,bool drop)
        {
            if(top&&!drop&&!surface.topExit)return;
            if(top&&!drop&&canonicalRopeSession&&surface.kind==HeroTraversalKind.Rope&&TryBeginRopeTopExit())return;
            if(!top&&!drop&&canonicalRopeSession&&surface.kind==HeroTraversalKind.Rope&&TryBeginRopeGroundExit())return;
            ledgeTopDefinition=null;start=transform.position;goal=start;phase=3;phaseTime=0;
            if(surface.kind==HeroTraversalKind.Seat){goal=surface.Top;Action("INTERACT_REST_EXIT");}
            else if(drop)
            {Action(surface.kind==HeroTraversalKind.Rope?"ROPE_RELEASE":surface.kind==HeroTraversalKind.Pole?"POLE_RELEASE":"LEDGE_DROP");goal=start-surface.transform.forward*.2f;}
            else
            {
                goal=top?surface.Top+surface.transform.forward*.65f:surface.Bottom-surface.transform.forward*.4f;
                if(!ClearStanding(goal)||(top&&(surface.kind==HeroTraversalKind.Ledge||surface.kind==HeroTraversalKind.Pole)&&!HasLedgeTopSupport(goal))){phase=2;if(ropeContact!=null){Action(ropeContact.Current.Direction>0?"ROPE_UP":"ROPE_DOWN");ActionTime=ropeContact.Current.Clock;}else Action(HoldAction());return;}
                Action(surface.kind==HeroTraversalKind.Ladder?top?"LADDER_EXIT_TOP":"LADDER_EXIT_BOTTOM":surface.kind==HeroTraversalKind.Ledge?"WALL_LEDGE_TOP":top?(surface.kind==HeroTraversalKind.Pole?"WALL_LEDGE_TOP":"CLIMB_TOP"):surface.kind==HeroTraversalKind.Rope?"ROPE_EXIT_GROUND":"POLE_EXIT_GROUND");
            }
            ResetRopeContact();
        }
        public void ApplyContactPose(HeroPoseDriver pose)
        {
            if(!Active||!pose||!pose.animator)return;
            if(ApplyMantleSupport(pose))return;
            if(phase!=2||surface.kind==HeroTraversalKind.Seat||surface.kind==HeroTraversalKind.Pole)return;
            // Paired authored hold already places the palm, not the wrist, against the edge.
            if(ropeContactFrame.IsActive||UsesPairedRopeUp(pose))return;
            if((ActionId=="LEDGE_HANG"||(canonicalRopeSession&&ActionId=="ROPE_HOLD"))&&pose.PoseCorrectionReady(hero.Library.Find(ActionId)))return;
            var animator=pose.animator;
            for(int i=0;i<2;i++){bool left=i==0;var hand=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);var lower=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);var upper=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);if(!hand||!lower||!upper)continue;
                float lateral=surface.kind==HeroTraversalKind.Rope?0:surface.kind==HeroTraversalKind.Pole?.13f:.17f;float depth=surface.kind==HeroTraversalKind.Ledge?.4f:surface.kind==HeroTraversalKind.Rope?.25f:.28f;
                Vector3 target=transform.position+surface.transform.right*(left?-lateral:lateral)+surface.transform.forward*depth;target.y=surface.kind==HeroTraversalKind.Ledge?surface.Top.y-.07f:hand.position.y;
                var wrist=hand.rotation;SolveArm(upper,lower,hand,target,upper.position+surface.transform.right*(left?-.5f:.5f)-surface.transform.forward*.3f);hand.rotation=wrist;MaximumHandContactError=Mathf.Max(MaximumHandContactError,Vector3.Distance(hand.position,target));}
        }
        static void SolveArm(Transform upper,Transform lower,Transform end,Vector3 goal,Vector3 pole)
        {
            Vector3 root=upper.position,axis=goal-root;float a=Vector3.Distance(root,lower.position),b=Vector3.Distance(lower.position,end.position);float d=Mathf.Clamp(axis.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);axis=axis.sqrMagnitude>.000001f?axis.normalized:Vector3.forward;Vector3 bend=Vector3.ProjectOnPlane(pole-root,axis).normalized;if(bend.sqrMagnitude<.000001f)bend=Vector3.ProjectOnPlane(upper.forward,axis).normalized;float along=(a*a-b*b+d*d)/(2*d);Vector3 elbow=root+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));upper.rotation=Quaternion.FromToRotation(lower.position-root,elbow-root)*upper.rotation;lower.rotation=Quaternion.FromToRotation(end.position-lower.position,root+axis*d-lower.position)*lower.rotation;
        }
        void OnGUI()
        {
            if(!Candidate||Active||!hero||!hero.AcceptInput)return;WayfarerUITheme.Apply();
            GUI.Box(new Rect(Screen.width*.5f-110,Screen.height*.74f,220,30),"E  "+(Candidate.kind==HeroTraversalKind.Seat?"Rest":Candidate.kind.ToString()));
        }
    }
    // Shared collision-resolved climb step. It neither reads input nor owns action selection.
    public static class HeroClimbMotion
    {
        public readonly struct Step
        {
            public readonly float progress,seconds;public readonly int endpoint;
            public Step(float progress,float seconds,int endpoint){this.progress=progress;this.seconds=seconds;this.endpoint=endpoint;}
        }
        public static Step Move(CharacterController capsule,Vector3 bottom,Vector3 top,float vertical,float dt,float endpointTolerance=.003f,bool requireTargetStep=false)
        {
            var axis=top-bottom;float length=axis.magnitude;
            if(!capsule||!capsule.enabled||length<.1f)return new Step(0,0,0);axis/=length;
            var before=capsule.transform.position;float along=Mathf.Clamp01(Vector3.Dot(before-bottom,axis)/length);
            if(dt<=0||Mathf.Abs(vertical)<.1f)return new Step(along,0,0);
            float requested=vertical*.65f*dt;
            float next=Mathf.Clamp01(along+requested/length);
            capsule.Move(Vector3.Lerp(bottom,top,next)-before);
            var after=capsule.transform.position;
            along=Mathf.Clamp01(Vector3.Dot(after-bottom,axis)/length);
            float travelled=Mathf.Max(0,Mathf.Sign(vertical)*Vector3.Dot(after-before,axis));
            // Depenetration can exceed the request. Do not turn that correction into fast animation.
            float seconds=Mathf.Min(Mathf.Abs(requested),travelled)/.65f;
            int endpoint=vertical>0&&(!requireTargetStep||next>=1)&&Vector3.Distance(after,top)<=endpointTolerance?1:
                vertical<0&&(!requireTargetStep||next<=0)&&Vector3.Distance(after,bottom)<=endpointTolerance?-1:0;
            return new Step(along,seconds,endpoint);
        }
    }
}



