using UnityEngine;

namespace SamProduction.BossArena
{
    [RequireComponent(typeof(Camera))]
    public sealed class ArenaThirdPersonCamera : MonoBehaviour
    {
        public Transform player, target;
        public ArenaBlackSunFx blackSun;
        [Min(1)] public float distance = 5.6f;
        public float focusHeight = 1.25f, shoulderOffset = .32f;
        [Min(1)] public float lockOrbitSpeed = 125f;
        [Min(.05f)] public float lockOrbitSmoothTime = .22f;
        [Tooltip("Actual camera rotation limit while locked, including motion of the focus point.")]
        [Min(1)] public float lockCameraSpeed = 140f;
        [Tooltip("Small orbit toward the existing shoulder side to separate close combat silhouettes.")]
        [Range(0, 25)] public float closeCombatOrbitAngle = 16f;
        [Min(0)] public float closeCombatShoulderOffset = .55f;
        [Min(.1f)] public float closeCombatFullDistance = 2.2f;
        [Min(.2f)] public float closeCombatFadeDistance = 5.2f;
        [Min(.05f)] public float closeCombatBlendTime = .28f;
        [Tooltip("Extra offset toward the existing shoulder side during a locked source stomp. Zero restores the previous shoulder framing.")]
        [Range(0, 1.5f)] public float stompShoulderExtra = 1.4f;
        [Tooltip("Lower the locked stomp focus to reveal ground contact and nearby warning edges. Zero restores the previous focus.")]
        [Range(0, .3f)] public float stompGroundFocusDrop = .15f;
        [Tooltip("Authored-source time used to ease into the stomp shot and out before its recovery ends. No impact-time cut.")]
        [Range(.05f, 1)] public float stompFramingBlendSeconds = .65f;
        [Tooltip("Screen fraction reserved for the player and the locked target, with space for attack silhouettes.")]
        [Range(.5f, .95f)] public float groupFramingSize = .78f;
        [Tooltip("Extra total world-axis size reserved around the boss capsule for the body and broad poses. Stable padding avoids tracking animated fists or fire. Legacy framing: (0.7, 0.2, 0.7). Negative values are clamped to zero.")]
        public Vector3 bossFramingPadding = new Vector3(2.2f, .6f, 2.2f);
        [Min(5)] public float maximumGroupDistance = 14f;
        [Min(.05f)] public float framingDamping = .24f;
        [Tooltip("The shot returns to gameplay during the existing cinematic, before input is restored.")]
        [Range(.1f, 1)] public float cinematicReturnSeconds = .65f;
        [Range(.03f, .5f)] public float mouseSensitivity = .13f;
        [Min(.05f)] public float collisionRadius = .23f;
        public LayerMask worldMask = 1 << 8;
        [Tooltip("Disable when a deterministic simulation calls TickCamera itself.")]
        public bool automaticTick = true;
        public bool LockedOn { get; private set; }
        public void ClearTargetLock() { LockedOn = false; }
        public float ActualAngularSpeedDegPerSecond { get; private set; }
        public float OrbitAngularSpeedDegPerSecond { get; private set; }
        public float LockYawErrorDegrees { get; private set; }
        public float CloseFramingWeight { get; private set; }
        public bool RotationLimitedThisTick { get; private set; }
        public bool WorldConstrainedThisTick { get; private set; }
        public float CinematicReturnWeight { get; private set; }
        public float HazardFramingWeight { get; private set; }
        public float GroupFramingDistance { get; private set; }
        public Vector3 PlanarForward
        {
            get
            {
                var result = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                return result.sqrMagnitude > .0001f ? result.normalized : Vector3.forward;
            }
        }
        public bool BlackSunFramingConstrained { get; private set; }
        private readonly Vector3[] sunFramePoints = new Vector3[28];
        private float yaw, pitch = 15, orbitOffset, yawVelocity;
        private bool recentering;
        private float recenterYaw, recenterYawVelocity, recenterPitchVelocity;
        private float stompFramingWeight;
        private float sunReturnWeight, boundaryLift;
        // This shot owns framing only: gameplay lock and movement remain independent.
        public bool BlackSunShotActive => sunShotOwned || sunShotReturn > 0;
        private bool sunShotOwned;
        private float sunShotReturn, sunEntryFov, sunPeakRadius;
        private Vector3 sunPeakCentre;
        private Vector3 sunEntryPosition;
        private Quaternion sunEntryRotation;
        private Vector3 smoothFocus;
        private bool initialized;
        private object cinematicOwner;
        private float gameplayFov;
        private Camera outputCamera;
        private Transform cachedTarget;
        private ArenaBossBrain targetBrain;
        private CharacterController targetCapsule;
        private Camera OutputCamera => outputCamera != null ? outputCamera : outputCamera = GetComponent<Camera>();
        public bool CinematicActive => cinematicOwner != null;
        public void BeginCinematic(object owner)
        {
            recentering = false;
            if(owner==null || cinematicOwner!=null)throw new System.InvalidOperationException("Camera already owned or missing cinematic owner.");
            EnsureLens();cinematicOwner=owner;CinematicReturnWeight=0;
        }
        public void PoseCinematic(object owner, Vector3 focus, Vector3 wanted, float fov, float explicitReturnWeight = -1, bool sweepFromCurrent = false)
        {
            if(!ReferenceEquals(owner,cinematicOwner))throw new System.InvalidOperationException("Wrong cinematic camera owner.");
            Vector3 position=sweepFromCurrent?wanted:ConstrainToWorld(focus,wanted);
            Quaternion rotation=SafeLook(focus-(sweepFromCurrent?wanted:position),transform.rotation);
            float sourceTime=0,restoreTime=0;
            if(owner is ArenaIgniteMotor ignite && ignite.Settings!=null)
            {sourceTime=ignite.SourceTime;restoreTime=ignite.Settings.completeSeconds;}
            else if(owner is ArenaMeteorMotor meteor && meteor.Settings!=null)
            {sourceTime=meteor.SourceTime;restoreTime=meteor.Settings.restoreTime;}
            // This uses the authored simulation clock, not Time.deltaTime: a frame
            // can contain several 120 Hz motor steps. No input-lock time is added.
            CinematicReturnWeight=restoreTime>0?Mathf.SmoothStep(0,1,
                Mathf.InverseLerp(restoreTime-Mathf.Max(.1f,cinematicReturnSeconds),restoreTime,sourceTime)):0;
            if(explicitReturnWeight>=0)CinematicReturnWeight=Mathf.Clamp01(explicitReturnWeight);
            if(owner is ArenaMeteorMotor framedMeteor)
                FrameMeteorShowcase(framedMeteor,fov,ref focus,ref position,ref rotation);
            if(CinematicReturnWeight>0 && player!=null)
            {
                float close=DesiredCloseWeight();
                float returnYaw=DesiredLockYaw(yaw,close);
                ComposeGameplay(returnYaw,close,out var gameFocus,out var gameAim,out float gameDistance,out float gameFov);
                Vector3 gamePosition=ConstrainToWorld(gameFocus,gameFocus-gameAim*Vector3.forward*gameDistance
                    +gameAim*Vector3.right*FramedShoulder(close));
                Quaternion gameRotation=SafeLook(gameFocus-gamePosition,rotation);
                Vector3 blendedPosition=Vector3.Lerp(position,gamePosition,CinematicReturnWeight);
                position=sweepFromCurrent?blendedPosition:
                    ConstrainToWorld(Vector3.Lerp(focus,gameFocus,CinematicReturnWeight),blendedPosition);
                rotation=Quaternion.Slerp(rotation,gameRotation,CinematicReturnWeight);
                fov=Mathf.Lerp(fov,gameFov,CinematicReturnWeight);
                // Warm the gameplay state while its input is still locked. The
                // ownership release itself must never move the displayed camera.
                smoothFocus=gameFocus;GroupFramingDistance=gameDistance;
                yaw=returnYaw;yawVelocity=0;CloseFramingWeight=close;initialized=true;
            }
            // Constrain only the final requested flight, including the return blend.
            if(sweepFromCurrent)position=ConstrainCinematicFlight(position);
            transform.SetPositionAndRotation(position,rotation);
            OutputCamera.fieldOfView=fov;
        }
        private void FrameMeteorShowcase(ArenaMeteorMotor meteor,float fov,
            ref Vector3 focus,ref Vector3 position,ref Quaternion rotation)
        {
            // The existing return below still hands the camera to ground danger at
            // exactly the authored time. Reserve HUD space in the owned showcase.
            float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(3.0f,3.7f,meteor.SourceTime));
            if(blend<=0||meteor.Cancelled||meteor.Settings==null)return;
            var brain=meteor.GetComponent<ArenaBossBrain>();
            Bounds body=new Bounds(brain.Socket("pelvis").position,Vector3.zero);
            body.Encapsulate(brain.Socket("chest").position);
            body.Encapsulate(brain.Socket("socket_hand.L").position);body.Encapsulate(brain.Socket("socket_hand.R").position);
            body.Encapsulate(brain.Socket("socket_foot.L").position);body.Encapsulate(brain.Socket("socket_foot.R").position);
            body.Expand(new Vector3(1.2f,.9f,1.2f));
            // Anticipate the existing authored core path so its activation does
            // not trigger a camera cut. This is an envelope, not a VFX change.
            Vector3 end=meteor.Centre;
            Vector3 from=end+brain.transform.TransformDirection(new Vector3(-4,17,-8));
            float fall=Mathf.InverseLerp(meteor.Settings.mainTime,meteor.Settings.impactTime,meteor.SourceTime);
            Bounds core=new Bounds(Vector3.Lerp(from,end,fall*fall),Vector3.one*5.72f);
            if(meteor.presentation.MainMeteorActive)core.Encapsulate(meteor.presentation.MainMeteorBounds);
            Bounds group=body;group.Encapsulate(core);
            float anticipate=Mathf.SmoothStep(0,1,Mathf.InverseLerp(3.5f,4.3f,meteor.SourceTime));
            Vector3 centre=Vector3.Lerp(body.center,group.center,anticipate);
            Vector3 size=Vector3.Lerp(body.size,group.size,anticipate);
            Bounds envelope=new Bounds(centre,size);
            float tan=Mathf.Tan(fov*.5f*Mathf.Deg2Rad),horizontal=tan*Mathf.Max(.5f,OutputCamera.aspect)*.84f;
            Quaternion inverse=Quaternion.Inverse(rotation);float required=Vector3.Distance(position,focus);
            for(int i=0;i<8;i++)
            {
                Vector3 p=inverse*(envelope.center+Vector3.Scale(envelope.extents,
                    new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))-centre);
                // Safe projected rectangle x=.08..92, y=.08..80. Its centre is
                // y=.44; aim above the group to leave the top HUD clear.
                required=Mathf.Max(required,(Mathf.Abs(p.x)+.12f)/horizontal-p.z);
                required=Mathf.Max(required,(p.y+.12f-.60f*tan*p.z)/(.72f*tan));
                required=Mathf.Max(required,(-p.y+.12f-.84f*tan*p.z)/(.72f*tan));
            }
            Vector3 framedFocus=centre+rotation*Vector3.up*(.12f*tan*required);
            Vector3 framedPosition=ConstrainToWorld(framedFocus,framedFocus-rotation*Vector3.forward*required);
            focus=Vector3.Lerp(focus,framedFocus,blend);position=Vector3.Lerp(position,framedPosition,blend);
        }
        public void EndCinematic(object owner)
        {
            if(!ReferenceEquals(owner,cinematicOwner))return;
            cinematicOwner=null;
            // Do not cut the transform or the lens here, including on cancellation.
            // Normal tracking resumes from the exact displayed pose. In a complete
            // shot the pre-return above has already placed it in the gameplay view.
            if(player!=null && !initialized)
            {smoothFocus=player.position+Vector3.up*focusHeight;GroupFramingDistance=distance;initialized=true;}
        }

        public void TickLook(Vector2 look, bool toggleLock)
        {
            LockedOn=player!=null&&player.GetComponent<ArenaPlayerController>()?.FocusActive==true&&TargetAvailable();toggleLock=false;
            if(CinematicActive || BlackSunShotActive)return;
            // Mouse delta is consumed once, with no delta-time multiplier.
            look = Vector2.ClampMagnitude(look, 600);
            if (look.sqrMagnitude > .25f || toggleLock) recentering = false;
            if (LockedOn) orbitOffset = Mathf.Clamp(orbitOffset + look.x * mouseSensitivity, -45, 45);
            else yaw += look.x * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, -12, 58);
        }

        public void ResetCamera()
        {
            recentering = false;
            sunShotOwned=false;sunShotReturn=0;
            EnsureLens();cinematicOwner=null;OutputCamera.fieldOfView=gameplayFov;
            yaw = player != null ? player.eulerAngles.y : 0;
            pitch = 15;
            orbitOffset = yawVelocity = 0;
            LockedOn = false;
            initialized = false;
            CloseFramingWeight = 0;
            stompFramingWeight = sunReturnWeight = boundaryLift = 0;
            CinematicReturnWeight = HazardFramingWeight = 0;
            GroupFramingDistance = distance;
            TickCamera(0, true);
        }

        private bool TargetAvailable()
        {
            if (target == null || !target.gameObject.activeInHierarchy) return false;
            if (player != null && Vector3.Distance(player.position, target.position) > 35f) return false;
            var fighter = target.GetComponent<ArenaFighter>();
            return fighter == null || !fighter.Dead;
        }

        private void LateUpdate() { if (automaticTick) TickCamera(Time.deltaTime); }

        public void RecenterView()
        {
            if (CinematicActive || BlackSunShotActive || player == null) return;
            Vector3 toward = LockedOn && TargetAvailable() ? target.position - player.position : player.forward;
            recenterYaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            recentering = true;
            recenterYawVelocity = recenterPitchVelocity = 0f;
        }

        public void TickCamera(float delta, bool instant = false)
        {
            if(CinematicActive)return;
            if (delta < 0 || float.IsNaN(delta) || float.IsInfinity(delta))
                throw new System.ArgumentOutOfRangeException(nameof(delta));
            if (player == null) return;
            EnsureLens();
            if (delta == 0 && !instant && initialized) return;
            instant |= !initialized;
            if (TickBlackSunShot(delta,instant)) return;
            if (recentering && delta > 0f)
            {
                pitch = Mathf.SmoothDampAngle(pitch, 15f, ref recenterPitchVelocity, .16f, Mathf.Infinity, delta);
                orbitOffset = Mathf.Lerp(orbitOffset, 0f, 1f - Mathf.Exp(-12f * delta));
                if (!LockedOn) yaw = Mathf.SmoothDampAngle(yaw, recenterYaw, ref recenterYawVelocity, .16f, Mathf.Infinity, delta);
                if (Mathf.Abs(pitch - 15f) < .1f && (LockedOn ? Mathf.Abs(orbitOffset) < .1f : Mathf.Abs(Mathf.DeltaAngle(yaw, recenterYaw)) < .1f))
                    recentering = false;
            }
            ActualAngularSpeedDegPerSecond = OrbitAngularSpeedDegPerSecond = LockYawErrorDegrees = 0;
            RotationLimitedThisTick = WorldConstrainedThisTick = false;
            if (LockedOn && !TargetAvailable())
            {
                LockedOn = false;
                yaw = transform.eulerAngles.y;
                orbitOffset = yawVelocity = 0;
            }
            Vector3 flat = LockedOn ? Vector3.ProjectOnPlane(target.position - player.position, Vector3.up) : Vector3.zero;
            float closeBlend = DesiredCloseWeight();
            CloseFramingWeight = instant ? closeBlend : Mathf.Lerp(CloseFramingWeight, closeBlend,
                1 - Mathf.Exp(-delta / Mathf.Max(.05f, closeCombatBlendTime)));
            float shoulderSide = shoulderOffset < 0 ? -1 : 1;
            if (LockedOn)
            {
                if (flat.sqrMagnitude > .01f)
                {
                    // Moving only the look-at point shifts both silhouettes together.
                    // A small physical orbit provides parallax without zooming out.
                    float wantedYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg + orbitOffset
                        - shoulderSide * closeCombatOrbitAngle * CloseFramingWeight;
                    float previousYaw = yaw;
                    if (instant) { yaw = wantedYaw; yawVelocity = 0; }
                    else
                    {
                        float maxStep = Mathf.Max(1, lockOrbitSpeed) * delta;
                        float smoothed = Mathf.SmoothDampAngle(yaw, wantedYaw, ref yawVelocity,
                            Mathf.Max(.05f, lockOrbitSmoothTime), Mathf.Max(1, lockOrbitSpeed), delta);
                        float step = Mathf.Clamp(Mathf.DeltaAngle(yaw, smoothed), -maxStep, maxStep);
                        yaw += step;
                        // SmoothDamp's maxSpeed is a smoothing parameter, not a
                        // promise about the final per-tick rotation. Enforce both.
                        if (Mathf.Abs(Mathf.DeltaAngle(previousYaw, smoothed)) > maxStep)
                            yawVelocity = step / delta;
                        OrbitAngularSpeedDegPerSecond = Mathf.Abs(step) / delta;
                    }
                    LockYawErrorDegrees = Mathf.DeltaAngle(yaw, wantedYaw);
                }
            }
            ComposeGameplay(yaw,CloseFramingWeight,out var focus,out var aim,out float wantedDistance,out float wantedFov);
            if (!initialized || instant) { smoothFocus = focus; initialized = true; }
            else
            {
                // Vertical animation should not bob the frame as strongly as a
                // player's lateral dodge. Damping is independent of frame rate.
                smoothFocus.x=Mathf.Lerp(smoothFocus.x,focus.x,1-Mathf.Exp(-delta/.10f));
                smoothFocus.z=Mathf.Lerp(smoothFocus.z,focus.z,1-Mathf.Exp(-delta/.10f));
                smoothFocus.y=Mathf.Lerp(smoothFocus.y,focus.y,1-Mathf.Exp(-delta/.18f));
            }
            float framingTime=Mathf.Max(.05f,framingDamping)*(wantedDistance>GroupFramingDistance?.65f:1.25f);
            GroupFramingDistance=instant?wantedDistance:Mathf.Lerp(GroupFramingDistance,wantedDistance,1-Mathf.Exp(-delta/framingTime));
            OutputCamera.fieldOfView=instant?wantedFov:Mathf.Lerp(OutputCamera.fieldOfView,wantedFov,1-Mathf.Exp(-delta/.18f));
            float framedShoulder = FramedShoulder(CloseFramingWeight);
            Vector3 wanted = smoothFocus - aim * Vector3.forward * GroupFramingDistance + aim * Vector3.right * framedShoulder;
            wanted = LiftFromLowBoundary(smoothFocus,wanted,delta,instant);
            wanted = ConstrainToWorld(smoothFocus, wanted);
            bool sunFitActive = LockedOn && blackSun != null && blackSun.Active && TargetAvailable();
            sunReturnWeight = sunFitActive ? 1 : Mathf.MoveTowards(sunReturnWeight, 0, delta / .7f);
            float positionDamping = sunFitActive ? 18 : Mathf.Lerp(18, 5, sunReturnWeight);
            Vector3 position = instant ? wanted : Vector3.Lerp(transform.position, wanted, 1 - Mathf.Exp(-positionDamping * delta));
            // The smoothed path must also stay in front of walls after a fast turn.
            position = ConstrainToWorld(smoothFocus, position);
            Vector3 direction = smoothFocus - position;
            if (direction.sqrMagnitude > .001f)
            {
                Quaternion previousRotation = transform.rotation;
                // Use the authored orbit when fitting the sun: the corrected camera
                // position must not feed back into next frame's pitch.
                bool sunFraming = LockedOn && blackSun != null && blackSun.Active && TargetAvailable();
                Quaternion rotation = sunFraming ? aim : Quaternion.LookRotation(direction, Vector3.up);
                if (LockedOn && !instant)
                {
                    float maxStep = Mathf.Max(1, lockCameraSpeed) * delta;
                    RotationLimitedThisTick = Quaternion.Angle(previousRotation, rotation) > maxStep;
                    rotation = Quaternion.RotateTowards(previousRotation, rotation, maxStep);
                }
                // Small simulation-timed rotational impact; no extra camera translation or input owner.
                if(!instant && blackSun != null)
                { float impact=blackSun.ImpactWeight; rotation*=Quaternion.Euler(impact*.24f,0,impact*.13f); }
                ResolveBlackSunViewport(ref position, rotation);
                transform.SetPositionAndRotation(position, rotation);
                if (!instant && delta > 0)
                    ActualAngularSpeedDegPerSecond = Quaternion.Angle(previousRotation, rotation) / delta;
            }
        }


        // A fixed background-facing composition makes the growing foreground sun
        // readable against the fading eclipse. Never use BeginCinematic here: the
        // player must retain all six seconds to run to a safe corner.
        private bool TickBlackSunShot(float delta,bool instant)
        {
            // An arena-wide attack must not inherit the 35 m manual-lock limit.
            var targetFighter=target?target.GetComponent<ArenaFighter>():null;
            bool active=blackSun!=null && blackSun.Active && target && target.gameObject.activeInHierarchy && (targetFighter==null || !targetFighter.Dead);
            if(active)
            {
                if(!sunShotOwned)
                {
                    sunShotOwned=true;sunShotReturn=0;recentering=false;sunPeakRadius=0;
                    sunEntryPosition=transform.position;sunEntryRotation=transform.rotation;
                    sunEntryFov=OutputCamera.fieldOfView;
                }
                Vector3 moon=Vector3.forward;
                if(RenderSettings.skybox && RenderSettings.skybox.HasProperty("_EclipseDirection"))
                    moon=RenderSettings.skybox.GetVector("_EclipseDirection");
                Vector3 forward=Vector3.ProjectOnPlane(moon,Vector3.up).normalized;
                if(forward.sqrMagnitude<.001f)forward=Vector3.forward;
                Quaternion aim=Quaternion.LookRotation(forward+Vector3.up*.035f,Vector3.up);
                float fov=60;
                int count=0;
                AddSunFrameBounds(new Bounds(player.position+Vector3.up*.95f,new Vector3(.9f,1.9f,.9f)),ref count);
                Bounds body=new Bounds(target.position+Vector3.up*1.55f,new Vector3(4,3.8f,4));
                AddSunFrameBounds(body,ref count);
                // Fit the visible corona, not the transparent corners of its quad.
                float radius=Mathf.Max(.2f,blackSun.CoreVisualRadius/1.4f*1.12f);
                Vector3 coreCentre=blackSun.CorePosition;
                if(radius>sunPeakRadius){sunPeakRadius=radius;sunPeakCentre=coreCentre;}
                if(blackSun.SourceTime>=blackSun.ReleaseAt-.8f){radius=sunPeakRadius;coreCentre=sunPeakCentre;}
                Vector3 right=aim*Vector3.right*radius,up=aim*Vector3.up*radius;
                if(blackSun.CoreVisible || blackSun.Released)
                {
                    sunFramePoints[count++]=coreCentre-right-up;
                    sunFramePoints[count++]=coreCentre-right+up;
                    sunFramePoints[count++]=coreCentre+right-up;
                    sunFramePoints[count++]=coreCentre+right+up;
                }
                // Preserve all four destinations, including a player behind the Boss.
                float edge=Mathf.Min(16,blackSun.MaximumRadius*.85f);
                for(int i=0;i<4;i++)sunFramePoints[count++]=blackSun.Centre+new Vector3((i&1)==0?-edge:edge,.1f,(i&2)==0?-edge:edge);
                Vector3 origin=blackSun.Centre+Vector3.up*10-forward*18;
                Quaternion inverse=Quaternion.Inverse(aim);
                float tan=Mathf.Tan(fov*.5f*Mathf.Deg2Rad),lo=0,hi=110;
                for(int i=0;i<16;i++)
                {
                    float mid=(lo+hi)*.5f;
                    if(SunFrameInterval(origin,inverse,mid,tan,count,out _,out _))hi=mid;else lo=mid;
                }
                Vector3 anchor=target.position+Vector3.up*2;
                Vector3 wanted=origin;bool clear=false;
                for(float back=hi+.5f;back<=hi+12;back+=1)
                {
                    if(!SunFrameInterval(origin,inverse,back,tan,count,out float lower,out float upper))continue;
                    for(int i=0;i<5;i++)
                    {
                        float floorRise=(blackSun.Centre.y+3-origin.y+(aim*Vector3.forward*back).y)/(aim*Vector3.up).y;
                        float low=Mathf.Max(lower,floorRise);
                        if(low>upper)continue;
                        float rise=Mathf.Lerp(low,upper,i/4f);
                        var candidate=origin-aim*Vector3.forward*back+aim*Vector3.up*rise;
                        var ray=candidate-anchor;float length=ray.magnitude;
                        if(Physics.SphereCast(anchor,collisionRadius,ray/length,out _,length,worldMask,QueryTriggerInteraction.Ignore)
                            ||Physics.CheckSphere(candidate,collisionRadius,worldMask,QueryTriggerInteraction.Ignore))continue;
                        wanted=candidate;clear=true;break;
                    }
                    if(clear)break;
                }
                BlackSunFramingConstrained=!clear;
                if(!clear)wanted=ConstrainToWorld(anchor,origin-aim*Vector3.forward*hi);
                float enter=Mathf.SmoothStep(0,1,Mathf.Clamp01(blackSun.SourceTime/1.6f));
                Vector3 candidatePosition=Vector3.Lerp(sunEntryPosition,wanted,enter);
                candidatePosition.y=Mathf.Max(blackSun.Centre.y+2.5f,candidatePosition.y);
                WorldConstrainedThisTick=false;RotationLimitedThisTick=false;
                Vector3 position=ConstrainToWorld(anchor,candidatePosition);
                if(Physics.CheckSphere(position,collisionRadius,worldMask,QueryTriggerInteraction.Ignore))
                {position=transform.position;BlackSunFramingConstrained=true;}
                Quaternion rotation=Quaternion.Slerp(sunEntryRotation,aim,enter);
                if(!instant)rotation*=Quaternion.Euler(blackSun.ImpactWeight*.24f,0,blackSun.ImpactWeight*.13f);
                var previous=transform.rotation;
                transform.SetPositionAndRotation(position,rotation);
                OutputCamera.fieldOfView=Mathf.Lerp(sunEntryFov,fov,enter);
                ActualAngularSpeedDegPerSecond=delta>0?Quaternion.Angle(previous,rotation)/delta:0;
                HazardFramingWeight=enter;GroupFramingDistance=Vector3.Distance(position,anchor);
                initialized=true;return true;
            }
            if(sunShotOwned)
            {
                sunShotOwned=false;sunShotReturn=.9f;
                sunEntryPosition=transform.position;sunEntryRotation=transform.rotation;sunEntryFov=OutputCamera.fieldOfView;
            }
            if(sunShotReturn<=0)return false;
            sunShotReturn=Mathf.Max(0,sunShotReturn-delta);
            float blend=Mathf.SmoothStep(0,1,1-sunShotReturn/.9f);
            float close=DesiredCloseWeight(),returnYaw=DesiredLockYaw(yaw,close);
            ComposeGameplay(returnYaw,close,out var focus,out var gameAim,out float gameDistance,out float gameFov);
            Vector3 gamePosition=LiftFromLowBoundary(focus,focus-gameAim*Vector3.forward*gameDistance+gameAim*Vector3.right*FramedShoulder(close),delta,instant);
            gamePosition=ConstrainToWorld(focus,gamePosition);
            var positionBack=ConstrainToWorld(focus,Vector3.Lerp(sunEntryPosition,gamePosition,blend));
            transform.SetPositionAndRotation(positionBack,Quaternion.Slerp(sunEntryRotation,SafeLook(focus-gamePosition,gameAim),blend));
            OutputCamera.fieldOfView=Mathf.Lerp(sunEntryFov,gameFov,blend);
            smoothFocus=focus;GroupFramingDistance=gameDistance;CloseFramingWeight=close;HazardFramingWeight=0;
            BlackSunFramingConstrained=false;
            if(LockedOn){yaw=returnYaw;yawVelocity=0;}
            return true;
        }

        // Fit the displayed pose after damping and rotation limits. The effect never
        // gets to crop the player merely because the desired pose would have fitted.
        private void ResolveBlackSunViewport(ref Vector3 position, Quaternion rotation)
        {
            BlackSunFramingConstrained = false;
            if (!LockedOn || blackSun == null || !blackSun.Active || !TargetAvailable()) return;
            int count = 0;
            AddSunFrameBounds(new Bounds(player.position + Vector3.up * .95f,
                new Vector3(.9f, 1.9f, .9f)), ref count);
            Bounds body = targetCapsule != null ? targetCapsule.bounds :
                new Bounds(target.position + Vector3.up * 1.55f, new Vector3(1.8f, 3.1f, 1.8f));
            body.Expand(Vector3.Max(Vector3.zero, bossFramingPadding));
            AddSunFrameBounds(body, ref count);
            int actors = count;
            if (blackSun.CoreVisible || !blackSun.Released)
            {
                float radius = blackSun.CoreVisualRadius;
                // Prepare the high shot before the first visible pixel appears.
                float reveal = blackSun.Released ? 1 : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .32f, blackSun.SourceTime));
                Vector3 centre = Vector3.Lerp(body.center, blackSun.CorePosition, reveal);
                Vector3 right = rotation * Vector3.right * radius;
                Vector3 up = rotation * Vector3.up * radius;
                sunFramePoints[count++] = centre - right - up;
                sunFramePoints[count++] = centre - right + up;
                sunFramePoints[count++] = centre + right - up;
                sunFramePoints[count++] = centre + right + up;
            }
            float lens = OutputCamera.fieldOfView;
            if (TrySunFrame(position, rotation, lens, count, out Vector3 fitted))
            { position = fitted; return; }
            float wide = Mathf.Max(lens, 78f);
            if (TrySunFrame(position, rotation, wide, count, out fitted))
            { position = fitted; OutputCamera.fieldOfView = wide; return; }
            // Walls remain authoritative. If the entire effect cannot fit, retain
            // the actors and flag the constrained shot rather than cross geometry.
            BlackSunFramingConstrained = true;
            if (TrySunFrame(position, rotation, wide, actors, out fitted))
            { position = fitted; OutputCamera.fieldOfView = wide; }
        }

        private void AddSunFrameBounds(Bounds bounds, ref int count)
        {
            for (int i = 0; i < 8; i++)
                sunFramePoints[count++] = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        }

        private bool SunFrameInterval(Vector3 position, Quaternion inverse, float back,
            float tan, int count, out float lower, out float upper)
        {
            lower = float.NegativeInfinity; upper = float.PositiveInfinity;
            float side = tan * Mathf.Max(.5f, OutputCamera.aspect) * .84f;
            for (int i = 0; i < count; i++)
            {
                Vector3 local = inverse * (sunFramePoints[i] - position);
                float depth = local.z + back;
                if (depth <= OutputCamera.nearClipPlane + .05f || Mathf.Abs(local.x) + .08f > side * depth)
                    return false;
                // x=.08..92, y=.12..78: reserve the top HUD and bottom controls.
                lower = Mathf.Max(lower, local.y + .08f - .56f * tan * depth);
                upper = Mathf.Min(upper, local.y - .08f + .76f * tan * depth);
            }
            return lower <= upper;
        }

        private bool TrySunFrame(Vector3 position, Quaternion rotation, float fov, int count, out Vector3 fitted)
        {
            fitted = position;
            Quaternion inverse = Quaternion.Inverse(rotation);
            float tan = Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
            float lo = 0, hi = 12;
            if (!SunFrameInterval(position, inverse, hi, tan, count, out _, out _)) return false;
            if (!SunFrameInterval(position, inverse, 0, tan, count, out _, out _))
                for (int i = 0; i < 12; i++)
                {
                    float mid = (lo + hi) * .5f;
                    if (SunFrameInterval(position, inverse, mid, tan, count, out _, out _)) hi = mid;
                    else lo = mid;
                }
            else hi = 0;
            SunFrameInterval(position, inverse, hi, tan, count, out float lower, out float upper);
            float rise = Mathf.Clamp(0, lower, upper);
            Vector3 candidate = position - rotation * Vector3.forward * hi + rotation * Vector3.up * rise;
            candidate = ConstrainToWorld(smoothFocus, candidate);
            for (int i = 0; i < count; i++)
            {
                Vector3 local = inverse * (sunFramePoints[i] - candidate);
                float depth = local.z;
                if (depth <= OutputCamera.nearClipPlane + .03f) return false;
                float x = .5f + local.x / (2 * tan * Mathf.Max(.5f, OutputCamera.aspect) * depth);
                float y = .5f + local.y / (2 * tan * depth);
                if (x < .079f || x > .921f || y < .119f || y > .781f) return false;
            }
            fitted = candidate;
            return true;
        }

        private void EnsureLens()
        { if(gameplayFov<=0)gameplayFov=OutputCamera.fieldOfView; }

        private float DesiredCloseWeight()
        {
            if(!LockedOn || !TargetAvailable() || player==null)return 0;
            float separation=Vector3.ProjectOnPlane(target.position-player.position,Vector3.up).magnitude;
            return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(closeCombatFullDistance,
                Mathf.Max(closeCombatFullDistance+.1f,closeCombatFadeDistance),separation));
        }

        private float DesiredLockYaw(float fallback,float close)
        {
            if(!LockedOn || !TargetAvailable() || player==null)return fallback;
            var flat=Vector3.ProjectOnPlane(target.position-player.position,Vector3.up);
            return flat.sqrMagnitude>.01f?Mathf.Atan2(flat.x,flat.z)*Mathf.Rad2Deg+orbitOffset
                -(shoulderOffset<0?-1:1)*closeCombatOrbitAngle*close:fallback;
        }

        private float FramedShoulder(float close)
        {
            float side = shoulderOffset < 0 ? -1 : 1;
            return Mathf.Lerp(shoulderOffset, side * Mathf.Max(Mathf.Abs(shoulderOffset), closeCombatShoulderOffset), close)
                + side * Mathf.Clamp(stompShoulderExtra, 0, 1.5f) * close * stompFramingWeight;
        }

        private void ComposeGameplay(float framingYaw,float close,out Vector3 focus,out Quaternion aim,out float wantedDistance,out float fov)
        {
            if(cachedTarget!=target)
            {
                cachedTarget=target;targetBrain=target!=null?target.GetComponent<ArenaBossBrain>():null;
                targetCapsule=target!=null?target.GetComponent<CharacterController>():null;
            }
            Vector3 playerFocus=player.position+Vector3.up*focusHeight;
            focus=playerFocus;wantedDistance=distance;fov=gameplayFov;
            bool frameBoss=LockedOn&&TargetAvailable();
            Bounds bossBounds=targetCapsule!=null?targetCapsule.bounds:
                new Bounds(target!=null?target.position+Vector3.up*1.55f:playerFocus,new Vector3(1.8f,3.1f,1.8f));
            // A stable authored envelope reserves room for the broad body and
            // poses without chasing animated fists or flame particle bounds.
            bossBounds.Expand(Vector3.Max(Vector3.zero, bossFramingPadding));
            if(frameBoss)
            {
                float separation=Vector3.ProjectOnPlane(target.position-player.position,Vector3.up).magnitude;
                focus=Vector3.Lerp(playerFocus,bossBounds.center,Mathf.Lerp(.32f,.43f,Mathf.InverseLerp(2,10,separation)));
                wantedDistance+=Mathf.Clamp(separation*.12f,.15f,1.6f);
            }
            // Establish the same-side ground shot during anticipation, then release
            // it before recovery completes. The source clock keeps impact/hold frames
            // steady without camera cuts, extra orbit or tracking the moving foot.
            stompFramingWeight = 0;
            var stomp = targetBrain != null ? targetBrain.StompMotor : null;
            if (frameBoss && stomp != null && stomp.Active && !stomp.Cancelled && stomp.Settings != null)
            {
                float blend = Mathf.Clamp(stompFramingBlendSeconds, .05f,
                    Mathf.Max(.05f, stomp.Settings.completeSeconds * .25f));
                float enter = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, blend, stomp.SourceTime));
                float leave = 1 - Mathf.SmoothStep(0, 1,
                    Mathf.InverseLerp(stomp.Settings.completeSeconds - blend, stomp.Settings.completeSeconds, stomp.SourceTime));
                stompFramingWeight = enter * leave;
                focus.y -= Mathf.Clamp(stompGroundFocusDrop, 0, .3f) * stompFramingWeight;
            }
            var meteor=targetBrain!=null?targetBrain.MeteorMotor:null;
            float hazard=0;
            if(meteor!=null&&meteor.Active&&!meteor.Cancelled&&meteor.Settings!=null)
            {
                float restore=meteor.Settings.restoreTime;
                float enter=Mathf.SmoothStep(0,1,Mathf.InverseLerp(restore-Mathf.Max(.1f,cinematicReturnSeconds),restore,meteor.SourceTime));
                float leave=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(meteor.Settings.impactTime+meteor.Settings.waveSeconds,
                    meteor.Settings.completeSeconds,meteor.SourceTime));
                hazard=enter*leave;
                Vector3 threatFocus=Vector3.Lerp(playerFocus,meteor.Centre+Vector3.up*.8f,.28f);
                // A torso-height focus cropped the camera-near warning arc at
                // handback. Aim nearer the ground before adding more distance:
                // the escape boundary and player stay readable at the same dolly.
                threatFocus.y=Mathf.Min(threatFocus.y,player.position.y+.45f);
                focus=Vector3.Lerp(focus,threatFocus,hazard);
                fov+=8*hazard;
            }
            float sunFrame=0;
            if(frameBoss && blackSun!=null && blackSun.Active)
            {
                float t=blackSun.SourceTime;
                sunFrame=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.15f,1.2f,t))
                    *(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(blackSun.ReleaseAt-.3f,blackSun.ReleaseAt+.65f,t)));
                Vector3 eclipseFocus=Vector3.Lerp(playerFocus,Vector3.Lerp(blackSun.Centre,blackSun.CorePosition,.45f),.82f);
                focus=Vector3.Lerp(focus,eclipseFocus,sunFrame);
                fov+=10*sunFrame;
            }
            HazardFramingWeight=Mathf.Max(hazard,sunFrame);
            // This temporary ground composition also releases back to the user's
            // pitch. A steeper view pushes the nearest edge below the frame.
            float framedPitch=Mathf.Lerp(Mathf.Lerp(pitch,22,hazard),-3,sunFrame);
            aim=Quaternion.Euler(framedPitch,framingYaw,0);
            if(frameBoss||hazard>0)
            {
                float verticalTan=Mathf.Tan(fov*.5f*Mathf.Deg2Rad)*Mathf.Clamp(groupFramingSize,.5f,.95f);
                float horizontalTan=verticalTan*Mathf.Max(.5f,OutputCamera.aspect);
                Quaternion inverse=Quaternion.Inverse(aim);
                float shoulder=FramedShoulder(close);
                FitBounds(new Bounds(player.position+Vector3.up*.95f,new Vector3(.9f,1.9f,.9f)),
                    focus,inverse,horizontalTan,verticalTan,shoulder,ref wantedDistance);
                if(frameBoss)
                {
                    // Once the player can dodge, ground danger wins over a distant
                    // airborne body. Keep a limited amount of sky in the composition.
                    if(hazard>0)
                    {
                        Vector3 centre=bossBounds.center;
                        centre.y=Mathf.Lerp(centre.y,Mathf.Min(centre.y,player.position.y+3.6f),hazard);
                        bossBounds.center=centre;
                    }
                    FitBounds(bossBounds,focus,inverse,horizontalTan,verticalTan,shoulder,ref wantedDistance);
                }
                if(hazard>0)
                {
                    // Fit the actual warning disc in camera space, including its
                    // near edge. A centre-point look-at cannot guarantee this.
                    float radius=meteor.Settings.radius;
                    float hazardDistance=distance;
                    for(int i=0;i<16;i++)
                    {
                        float angle=i*Mathf.PI/8;
                        Vector3 edge=meteor.Centre+new Vector3(Mathf.Cos(angle)*radius,.08f,Mathf.Sin(angle)*radius);
                        FitPoint(edge,focus,inverse,horizontalTan,verticalTan,shoulder,ref hazardDistance);
                    }
                    wantedDistance=Mathf.Max(wantedDistance,Mathf.Lerp(distance,hazardDistance,hazard));
                }
                if(sunFrame>0)
                {
                    float sunDistance=distance;
                    FitBounds(new Bounds(blackSun.CorePosition,Vector3.one*Mathf.Max(1,blackSun.CoreVisualRadius*2)),
                        focus,inverse,horizontalTan,verticalTan,shoulder,ref sunDistance);
                    for(int i=0;i<16;i++)
                    {
                        float angle=i*Mathf.PI/8;
                        FitPoint(blackSun.Centre+new Vector3(Mathf.Cos(angle)*blackSun.MaximumRadius,.08f,Mathf.Sin(angle)*blackSun.MaximumRadius),
                            focus,inverse,horizontalTan,verticalTan,shoulder,ref sunDistance);
                    }
                    wantedDistance=Mathf.Max(wantedDistance,Mathf.Lerp(distance,sunDistance,sunFrame));
                }
                wantedDistance=Mathf.Clamp(wantedDistance,distance,Mathf.Max(distance,Mathf.Lerp(maximumGroupDistance,Mathf.Max(20,maximumGroupDistance),sunFrame)));
            }
        }

        private static void FitBounds(Bounds bounds,Vector3 focus,Quaternion inverse,float horizontalTan,float verticalTan,float shoulder,ref float required)
        {
            var extent=bounds.extents;
            for(int i=0;i<8;i++)FitPoint(bounds.center+new Vector3((i&1)==0?-extent.x:extent.x,
                (i&2)==0?-extent.y:extent.y,(i&4)==0?-extent.z:extent.z),focus,inverse,horizontalTan,verticalTan,shoulder,ref required);
        }

        private static void FitPoint(Vector3 point,Vector3 focus,Quaternion inverse,float horizontalTan,float verticalTan,float shoulder,ref float required)
        {
            Vector3 local=inverse*(point-focus);
            float fit=Mathf.Max((Mathf.Abs(local.x-shoulder)+.12f)/horizontalTan,
                (Mathf.Abs(local.y)+.12f)/verticalTan)-local.z;
            required=Mathf.Max(required,fit);
        }

        private static Quaternion SafeLook(Vector3 direction,Quaternion fallback)
        {return direction.sqrMagnitude>.0001f?Quaternion.LookRotation(direction,Vector3.up):fallback;}

        bool PlayerFits(Vector3 position,Quaternion rotation)
        {
            var inverse=Quaternion.Inverse(rotation);float tan=Mathf.Tan(OutputCamera.fieldOfView*.5f*Mathf.Deg2Rad);
            for(int i=0;i<8;i++)
            {
                Vector3 p=player.position+new Vector3((i&1)==0?-.45f:.45f,(i&2)==0?0:1.9f,(i&4)==0?-.45f:.45f);
                Vector3 v=inverse*(p-position);if(v.z<=OutputCamera.nearClipPlane+.05f)return false;
                float x=.5f+v.x/(2*tan*OutputCamera.aspect*v.z),y=.5f+v.y/(2*tan*v.z);
                if(x<.04f||x>.96f||y<.09f||y>.88f)return false;
            }
            return true;
        }
        Vector3 LiftFromLowBoundary(Vector3 focus,Vector3 raw,float delta,bool instant)
        {
            if(blackSun&&blackSun.Active)return raw;
            var away=UnityEngine.Vector3.ProjectOnPlane(raw-focus,UnityEngine.Vector3.up).normalized;var liftAxis=UnityEngine.Vector3.up+away*.95f;
            var clipped=ConstrainToWorld(focus,raw);float needed=0;
            bool cramped=(clipped-(player.position+Vector3.up)).sqrMagnitude<6.25f
                ||!PlayerFits(clipped,SafeLook(focus-clipped,transform.rotation));
            if(cramped)
            {
                for(float lift=.5f;lift<=8;lift+=.5f)
                {
                    var candidate=raw+liftAxis*lift;var ray=candidate-focus;float length=ray.magnitude;
                    if(Physics.SphereCast(focus,collisionRadius,ray/length,out _,length,worldMask,QueryTriggerInteraction.Ignore)
                        ||Physics.CheckSphere(candidate,collisionRadius,worldMask,QueryTriggerInteraction.Ignore))continue;
                    if(!PlayerFits(candidate,SafeLook(focus-candidate,transform.rotation)))continue;
                    needed=lift;break;
                }
            }
            // Increase clearance immediately; descend gradually only after the route opens.
            boundaryLift=instant?needed:Mathf.Max(needed,Mathf.Lerp(boundaryLift,needed,1-Mathf.Exp(-delta/.25f)));
            return raw+liftAxis*boundaryLift;
        }

        // Opt-in for the summit's sky-to-room shot only. Combat cinematics retain
        // their existing focus-ray constraint. Keep both swept and endpoint safety.
        private Vector3 ConstrainCinematicFlight(Vector3 wanted)
        {
            Vector3 current=transform.position,step=wanted-current;
            float length=step.magnitude;
            if(length>.0001f && Physics.SphereCast(current,collisionRadius,step/length,out var hit,length,
                worldMask,QueryTriggerInteraction.Ignore))
            {
                WorldConstrainedThisTick=true;
                wanted=current+step/length*Mathf.Max(0,hit.distance-.06f);
            }
            if(Physics.CheckSphere(wanted,collisionRadius,worldMask,QueryTriggerInteraction.Ignore))
            {WorldConstrainedThisTick=true;return current;}
            return wanted;
        }

        private Vector3 ConstrainToWorld(Vector3 focus, Vector3 wanted)
        {
            Vector3 ray = wanted - focus;
            float length = ray.magnitude;
            if (length < .001f) return wanted;
            if (Physics.SphereCast(focus, collisionRadius, ray / length, out var hit, length,
                worldMask, QueryTriggerInteraction.Ignore))
            {
                WorldConstrainedThisTick = true;
                return focus + ray / length * Mathf.Max(.12f, hit.distance - .06f);
            }
            return wanted;
        }
    }
}
