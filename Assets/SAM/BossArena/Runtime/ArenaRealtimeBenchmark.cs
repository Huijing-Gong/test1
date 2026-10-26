using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction.BossArena
{
    // Opt-in real-time evidence. No frame capture, fixed capture clock, damage override,
    // forced Boss skill, or render-to-texture is used by this component.
    public sealed class ArenaRealtimeBenchmark : MonoBehaviour
    {
        [Serializable] public sealed class Machine
        {
            public string unity, operatingSystem, processor, graphicsDevice, graphicsVersion, graphicsType;
            public int processorCount, systemMemoryMB, graphicsMemoryMB, width, height, qualityLevel, antiAliasing;
            public string qualityName, renderPipeline, buildIdentity, executable, projectVersion;
            public bool developmentBuild, fullScreen, runInBackground;
            public int vSyncCount, targetFrameRate, captureFramerate;
            public float timeScale, renderScale, shadowDistance;
        }
        [Serializable] public sealed class EventRow
        {
            public double wallSeconds, battleSeconds;
            public int round;
            public string kind, skill, source, target;
            public long strike;
            public float health;
        }
        [Serializable] public sealed class QualitySample
        {
            public double wallSeconds;
            public string signature, pipeline;
            public int width, height, qualityLevel, qualityMsaa, pipelineMsaa, vSyncCount, targetFrameRate;
            public float renderScale, shadowDistance, dynamicWidthScale, dynamicHeightScale;
            public int shadowCascades, mainShadowResolution;
            public bool mainLightShadows, additionalLightShadows, softShadows;
        }
        [Serializable] public sealed class Report
        {
            public int schema = 2;
            public string status = "not_analyzed", mode, rate, startedUtc, finishedUtc;
            public string timing = "Stopwatch monotonic time between consecutive Unity Update callbacks; normal enabled game camera renders between callbacks. Every duration includes CPU, rendering and presentation waits. No fixed capture clock.";
            public string inputPolicy, phasePolicy = "Explicit Boss Phase/Stage/Overheated state only; health percentage is never treated as evidence of a completed transition.";
            public string artifactPolicy = "frames.csv, events.json and metadata.json are written after measurement. Run analyze_realtime_benchmark.py; this raw report never claims a performance pass.";
            public Machine machine;
            public double requestedWarmupSeconds, requestedSampleSeconds, actualWallSeconds;
            public int frameRows, roundCount;
            public bool completed, normalCameraRendering, defaultBossAI, captureClockDisabled;
            public string[] invalidReasons, errors;
            public string[] phaseMembers;
            public QualitySample[] qualitySamples;
            public ArenaPatrolPlanner.SearchTiming[] slowPatrolSearches;
            public string visibilityPolicy = "Per rendered interval: actual camera transform and projection settings; GeometryUtility frustum/AABB intersection for enabled Boss renderers and an explicit MainMeteorActive/MainMeteorBounds provider. Boss visibility is never substituted for main meteor visibility. Frustum intersection does not prove absence of occlusion or readable pixels; same-build video remains required.";
        }
        private struct Frame
        {
            public double start, end, ms, battle;
            public int frame, round, renders, width, height;
            public string state, skill, phase, playerState;
            public bool attacking, focused, active, sampled, inputLocked;
            public float bossHealth, playerHealth;
            public ArenaCommand command;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public float cameraFov, cameraAspect, cameraNear, cameraFar;
            public Bounds bossBounds, meteorBounds;
            public bool bossBoundsAvailable, bossVisible, meteorEvidenceAvailable, meteorActive, meteorVisible;
            public string meteorProvider, qualitySignature;
        }
        private ArenaSession arena;
        private Camera gameCamera;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly List<Frame> frames = new List<Frame>(131072);
        private readonly List<EventRow> events = new List<EventRow>(2048);
        private readonly List<string> errors = new List<string>();
        private readonly HashSet<string> invalid = new HashSet<string>();
        private readonly Dictionary<string, MemberInfo> members = new Dictionary<string, MemberInfo>();
        private readonly List<QualitySample> qualitySamples = new List<QualitySample>(256);
        private readonly Plane[] cameraPlanes = new Plane[6];
        private Renderer[] bossRenderers;
        private MonoBehaviour meteorProvider;
        private PropertyInfo meteorBoundsProperty, meteorActiveProperty;
        private FieldInfo meteorBoundsField, meteorActiveField;
        private double nextQualitySample;
        private string initialQualitySignature, currentQualitySignature;
        private readonly string[] phaseCandidates = { "CombatPhase", "Phase", "Stage", "PhaseName", "StageName", "Overheated", "IsOverheated", "SecondPhase" };
        private string output, controlMode, rate;
        private double warmup, duration, previousWall, nextAttack, nextDodge, endedAt = -1;
        private Frame previousFrame;
        private int eventIndex, round, renderCount;
        private bool armed, saved, sampleRestarted;
        private bool pilotSawAttack;
        private ArenaAttack pilotAttack;
        private float pilotLastSkillTime;
        private int pilotAvoidsAtAttackStart;
        private Report report;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--sam-benchmark") < 0) return;
            new GameObject("SAM real-time benchmark observer").AddComponent<ArenaRealtimeBenchmark>();
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            output = Arg(args, "--sam-output", null);
            if (string.IsNullOrEmpty(output)) { UnityEngine.Debug.LogError("--sam-output is required"); Application.Quit(2); yield break; }
            output = Path.GetFullPath(output);
            if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length > 0)
            { UnityEngine.Debug.LogError("Benchmark output must be empty; prior evidence is preserved: " + output); Application.Quit(2); yield break; }
            Directory.CreateDirectory(output);
            controlMode = Arg(args, "--sam-control", "pilot");
            rate = Arg(args, "--sam-rate", "unlimited");
            if (controlMode != "pilot" && controlMode != "hardware" && controlMode != "observe")
            { UnityEngine.Debug.LogError("--sam-control must be pilot, hardware or observe"); Application.Quit(2); yield break; }
            if (rate != "unlimited" && rate != "60")
            { UnityEngine.Debug.LogError("--sam-rate must be unlimited or 60"); Application.Quit(2); yield break; }
            warmup = Number(args, "--sam-warmup", 30);
            duration = Number(args, "--sam-duration", 180);
            Application.logMessageReceived += OnLog;
            // ArenaSession.Start owns normal display settings. Configure after its startup.
            yield return null;
            arena = FindFirstObjectByType<ArenaSession>();
            if (arena == null) { FailStartup("No ArenaSession in the built scene"); yield break; }
            gameCamera = arena.view.GetComponent<Camera>();
            bossRenderers = arena.boss.GetComponentsInChildren<Renderer>(true);
            Time.captureFramerate = 0;
            Time.timeScale = 1;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = rate == "60" ? 60 : -1;
            Application.runInBackground = true;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            arena.boss.decisionEnabled = true;
            if (controlMode == "pilot")
            {
                // This replaces only the command source. Each simulation delta below is
                // elapsed wall time, through the same public combat/collision simulation.
                arena.automaticTick = false;
                arena.view.automaticTick = false;
            }
            foreach (var name in phaseCandidates) Cache(arena.boss, name);
            Cache(arena, "InputLocked"); Cache(arena, "PlayerInputLocked"); Cache(arena, "CinematicActive");
            RenderPipelineManager.endCameraRendering += OnCameraRendered;
            for (int i = 0; i < 8; ++i) yield return null;
            report = new Report {
                mode = controlMode, rate = rate, startedUtc = DateTime.UtcNow.ToString("o"),
                requestedWarmupSeconds = warmup, requestedSampleSeconds = duration,
                inputPolicy = controlMode == "pilot" ? "Scripted movement, sword and dodge commands through ArenaSession.Simulate using elapsed monotonic wall time. Default Boss AI remains enabled. No health, damage, immunity, skill-selection or scene-position overrides. Natural victory/defeat restarts are recorded. A fresh battle begins at the sample boundary. Pilot uses current visible action phase/source time as a cue proxy and current warning-wave radius to request ordinary pre-contact dodges. It waits during patrol and counters only actual recovery after local wave danger is cleared, using ordinary run input to close distance before attacking a grounded, faced target within 1.55m. This is automated command input, not native keyboard/mouse evidence; it can still fail."
                    : controlMode == "hardware" ? "Unmodified ArenaSession hardware input loop; user must focus and play the game. Observer never supplies gameplay commands." : "Observer only; does not start or drive gameplay.",
                machine = Describe(), normalCameraRendering = true, defaultBossAI = true, captureClockDisabled = true,
                phaseMembers = AvailablePhaseMembers()
            };
            if (warmup < 30 || duration < 180) invalid.Add("Short diagnostic run: final acceptance requires warmup >= 30s and sample >= 180s");
            if (controlMode == "pilot") RestartBattle();
            InspectQuality(0);
            DiscoverMeteorProvider();
            armed = true; clock.Start(); previousWall = 0;
            previousFrame = Observe(0, default);
            renderCount = 0;
        }

        private void Update()
        {
            if (!armed || saved) return;
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = now - previousWall;
            if (elapsed <= 0) return;
            previousFrame.start = previousWall; previousFrame.end = now; previousFrame.ms = elapsed * 1000;
            previousFrame.renders = renderCount;
            previousFrame.sampled = previousWall >= warmup && previousWall < warmup + duration;
            frames.Add(previousFrame);
            renderCount = 0;
            CheckConfiguration(previousFrame);
            CollectEvents(now);
            if (now >= warmup + duration) { Complete(true); return; }
            if (now >= nextQualitySample)
            {
                nextQualitySample = now + 1;
                InspectQuality(now);
                if (meteorProvider == null) DiscoverMeteorProvider();
            }

            ArenaCommand command = default, issuedCommand = default;
            if (controlMode == "pilot")
            {
                if (!sampleRestarted && now >= warmup)
                { sampleRestarted = true; RestartBattle(); }
                if (!arena.Active)
                {
                    if (endedAt < 0) endedAt = now;
                    if (now - endedAt >= 1.0) RestartBattle();
                }
                command = Pilot(now); issuedCommand = command;
                // Consume the full measured real interval, including long frames. The
                // public simulation has a 0.25s safety bound, so subdivide only when
                // needed; never substitute 1/60 or discard elapsed wall time.
                arena.SimulateElapsed(command,elapsed);
                arena.view.TickCamera((float)elapsed);
                CollectEvents(now);
            }
            previousWall = now;
            previousFrame = Observe(now, issuedCommand);
        }

        private ArenaCommand Pilot(double now)
        {
            if (!arena.Active) return default;
            bool attacking = arena.boss.Attacking;
            float skillTime = arena.boss.SkillTime;
            if (attacking && (!pilotSawAttack || pilotAttack != arena.boss.CurrentAttack || skillTime < pilotLastSkillTime))
            {
                pilotAttack = arena.boss.CurrentAttack;
                pilotAvoidsAtAttackStart = arena.player.fighter.DodgeCount;
            }
            pilotSawAttack = attacking; pilotLastSkillTime = skillTime;
            if (arena.view.CinematicActive) return default;
            Vector3 offset = Vector3.ProjectOnPlane(arena.boss.transform.position - arena.player.transform.position, Vector3.up);
            float distance = offset.magnitude;
            Vector3 direction = distance > .01f ? offset / distance : arena.player.transform.forward;
            Vector3 forward = arena.view.PlanarForward, right = Vector3.Cross(Vector3.up, forward);
            // A counter-window player waits during patrol instead of pursuing
            // every planted turn. This is a disclosed input policy, not a change
            // to Boss cadence, skill choice, health or collision behavior.
            Vector3 wanted = arena.boss.Attacking
                ? (distance > 1.30f ? direction : distance < 1.0f ? -direction : Vector3.zero)
                : Vector3.zero;
            // Telegraph/Recovery do not define a universal damaging-state gap:
            // Dive and Stomp change directly from one to the other at impact.
            // Source time is a disclosed proxy for the currently visible pose,
            // not a prediction of future AI choices or patrol routes.
            float contactIn = float.PositiveInfinity;
            Vector3 evade = Vector3.Cross(Vector3.up, direction);
            bool localWaveDanger = false;
            float impact = 0, initialRadius = 0, maximumRadius = 0, waveSeconds = 0, waveRadius = 0;
            Vector3 waveCentre = Vector3.zero;
            var dash = arena.boss.DashMotor;
            var dive = arena.boss.DiveMotor;
            var stomp = arena.boss.StompMotor;
            var meteor = arena.boss.MeteorMotor;
            if (dash != null && dash.Active && dash.Phase == ArenaDashPhase.LockRelease && !dash.ReleaseSuppressed)
                contactIn = dash.clips.EventTime("dash_lock_release", "strike_open") - dash.PhaseTime;
            if (dive != null && dive.Active && !dive.Cancelled)
            {
                impact = dive.Settings.impactTime; initialRadius = dive.Settings.impactRadius;
                maximumRadius = dive.Settings.radius; waveSeconds = dive.Settings.waveSeconds;
                waveRadius = dive.WaveRadius; waveCentre = dive.Centre;
            }
            else if (stomp != null && stomp.Active && !stomp.Cancelled)
            {
                impact = stomp.Settings.stampTime; maximumRadius = stomp.Settings.waveRadiusMetres;
                waveSeconds = stomp.Settings.waveDurationSeconds; waveRadius = stomp.WaveRadius; waveCentre = stomp.Centre;
            }
            else if (meteor != null && meteor.Active && !meteor.Cancelled)
            {
                impact = meteor.Settings.impactTime; initialRadius = meteor.Settings.impactRadius;
                maximumRadius = meteor.Settings.radius; waveSeconds = meteor.Settings.waveSeconds;
                waveRadius = meteor.WaveRadius; waveCentre = meteor.Centre;
            }
            if (waveSeconds > 0 && skillTime <= impact + waveSeconds)
            {
                Vector3 radial = Vector3.ProjectOnPlane(arena.player.transform.position - waveCentre, Vector3.up);
                float radialDistance = radial.magnitude;
                float bodyRadius = arena.player.capsule.radius * Mathf.Max(Mathf.Abs(arena.player.transform.lossyScale.x), Mathf.Abs(arena.player.transform.lossyScale.z));
                bool behindPassedWave = skillTime >= impact && radialDistance + bodyRadius < Mathf.Max(0, waveRadius - .75f);
                bool actuallyAvoided = arena.player.fighter.DodgeCount > pilotAvoidsAtAttackStart;
                localWaveDanger = !behindPassedWave && !actuallyAvoided && radialDistance - bodyRadius <= maximumRadius;
                if (localWaveDanger)
                {
                    float speed = (maximumRadius - initialRadius) / waveSeconds;
                    float currentFront = skillTime < impact ? initialRadius : waveRadius;
                    contactIn = Mathf.Max(0, impact - skillTime) + Mathf.Max(0, radialDistance - bodyRadius - currentFront) / Mathf.Max(.01f, speed);
                    // Cross the arriving front toward its cleared interior. Rolling
                    // away prolongs contact until after the normal iframe window.
                    evade = radialDistance > .15f ? -radial / radialDistance : Vector3.Cross(Vector3.up, direction);
                    wanted = Vector3.zero;
                }
            }
            bool safeRecovery = arena.boss.IsRecovering && !localWaveDanger;
            bool groundedTarget = dive != null && dive.Active ? dive.Landed
                : meteor != null && meteor.Active ? meteor.Landed : arena.boss.capsule.isGrounded;
            bool facingTarget = Vector3.Angle(arena.player.transform.forward, direction) <= 35;
            // Close the remaining gap and finish normal
            // facing before committing. A distant swing slows movement to .45m/s.
            var command = new ArenaCommand { move = new Vector2(Vector3.Dot(wanted, right), Vector3.Dot(wanted, forward)), run = distance > 4 || (safeRecovery && distance > 1.8f), toggleLock = !arena.view.LockedOn };
            if (safeRecovery && groundedTarget && facingTarget && !arena.player.Dodging && distance < 1.55f && now >= nextAttack)
            { command.attack = true; nextAttack = now + .68; }
            float reactionLead = Mathf.Clamp(arena.player.invulnerableStart + .10f, .10f, arena.player.invulnerableEnd - .07f);
            bool canRequestDodge = !arena.player.Dodging && arena.player.ActionName != "Hurt"
                && (arena.player.AttackTime < 0 || arena.player.AttackTime >= .43f);
            if (attacking && contactIn >= -.03f && contactIn <= reactionLead && canRequestDodge && now >= nextDodge)
            {
                command.attack = false; command.dodge = true;
                nextDodge = now + Mathf.Max(.85f, arena.player.dodgeCooldown);
                command.move = new Vector2(Vector3.Dot(evade, right), Vector3.Dot(evade, forward));
            }
            return command;
        }

        private void RestartBattle()
        {
            CollectEvents(clock.Elapsed.TotalSeconds);
            arena.Restart(true); ++round; eventIndex = 0; endedAt = -1;
            nextAttack = nextDodge = 0; pilotSawAttack = false; pilotLastSkillTime = 0; pilotAvoidsAtAttackStart = 0;
            events.Add(new EventRow { wallSeconds = clock.Elapsed.TotalSeconds, round = round, kind = "benchmark_battle_restart", skill = "normal_session_restart" });
            CollectEvents(clock.Elapsed.TotalSeconds);
        }
        private void CollectEvents(double now)
        {
            if (arena == null) return;
            if (eventIndex > arena.Events.Count) { eventIndex = 0; ++round; }
            while (eventIndex < arena.Events.Count)
            {
                var e = arena.Events[eventIndex++];
                events.Add(new EventRow { wallSeconds = now, battleSeconds = e.time, round = round, kind = e.kind,
                    skill = e.skill, source = e.source, target = e.target, strike = e.strike, health = e.health });
            }
        }
        private Frame Observe(double now, ArenaCommand command)
        {
            var result = new Frame { frame = Time.frameCount, round = round, battle = arena.BattleTime,
                width = Screen.width, height = Screen.height, focused = Application.isFocused, active = arena.Active,
                state = arena.boss.StateName, skill = arena.boss.CurrentAttack.ToString(), phase = Phase(),
                attacking = arena.boss.Attacking, bossHealth = arena.boss.fighter.Health,
                playerHealth = arena.player.fighter.Health, playerState = arena.player.ActionName,
                inputLocked = BoolMember(arena, "InputLocked") || BoolMember(arena, "PlayerInputLocked") || BoolMember(arena, "CinematicActive"), command = command,
                cameraPosition = gameCamera.transform.position, cameraRotation = gameCamera.transform.rotation,
                cameraFov = gameCamera.fieldOfView, cameraAspect = gameCamera.aspect,
                cameraNear = gameCamera.nearClipPlane, cameraFar = gameCamera.farClipPlane,
                qualitySignature = currentQualitySignature };
            GeometryUtility.CalculateFrustumPlanes(gameCamera, cameraPlanes);
            foreach (var renderer in bossRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly ||
                    (gameCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                if (!result.bossBoundsAvailable) { result.bossBounds = renderer.bounds; result.bossBoundsAvailable = true; }
                else result.bossBounds.Encapsulate(renderer.bounds);
            }
            result.bossVisible = result.bossBoundsAvailable && GeometryUtility.TestPlanesAABB(cameraPlanes, result.bossBounds);
            if (meteorProvider != null)
            {
                try
                {
                    object active = meteorActiveProperty != null ? meteorActiveProperty.GetValue(meteorProvider) : meteorActiveField.GetValue(meteorProvider);
                    object bounds = meteorBoundsProperty != null ? meteorBoundsProperty.GetValue(meteorProvider) : meteorBoundsField.GetValue(meteorProvider);
                    result.meteorEvidenceAvailable = active is bool && bounds is Bounds;
                    result.meteorActive = active is bool flag && flag;
                    result.meteorProvider = meteorProvider.GetType().FullName + ".MainMeteorBounds";
                    if (bounds is Bounds value) result.meteorBounds = value;
                    result.meteorVisible = result.meteorEvidenceAvailable && result.meteorActive &&
                        result.meteorBounds.size.sqrMagnitude > .000001f && GeometryUtility.TestPlanesAABB(cameraPlanes, result.meteorBounds);
                }
                catch (Exception exception) { invalid.Add("Main meteor visibility provider failed: " + exception.Message); }
            }
            return result;
        }
        private void DiscoverMeteorProvider()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var type = component.GetType();
                var boundsProperty = type.GetProperty("MainMeteorBounds", flags);
                var activeProperty = type.GetProperty("MainMeteorActive", flags);
                var boundsField = type.GetField("MainMeteorBounds", flags);
                var activeField = type.GetField("MainMeteorActive", flags);
                if ((boundsProperty?.PropertyType ?? boundsField?.FieldType) != typeof(Bounds) ||
                    (activeProperty?.PropertyType ?? activeField?.FieldType) != typeof(bool)) continue;
                meteorProvider = component; meteorBoundsProperty = boundsProperty; meteorBoundsField = boundsField;
                meteorActiveProperty = activeProperty; meteorActiveField = activeField;
                return;
            }
        }
        private void InspectQuality(double now)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var sample = new QualitySample {
                width = Screen.width, height = Screen.height, qualityLevel = QualitySettings.GetQualityLevel(),
                qualityMsaa = QualitySettings.antiAliasing, pipelineMsaa = (int)PipelineNumber(pipeline, "msaaSampleCount", -1),
                vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
                renderScale = (float)PipelineNumber(pipeline, "renderScale", 1),
                shadowDistance = (float)PipelineNumber(pipeline, "shadowDistance", QualitySettings.shadowDistance),
                shadowCascades = (int)PipelineNumber(pipeline, "shadowCascadeCount", QualitySettings.shadowCascades),
                mainShadowResolution = (int)PipelineNumber(pipeline, "mainLightShadowmapResolution", -1),
                mainLightShadows = PipelineBoolean(pipeline, "supportsMainLightShadows"),
                additionalLightShadows = PipelineBoolean(pipeline, "supportsAdditionalLightShadows"),
                softShadows = PipelineBoolean(pipeline, "supportsSoftShadows"),
                dynamicWidthScale = ScalableBufferManager.widthScaleFactor, dynamicHeightScale = ScalableBufferManager.heightScaleFactor,
                pipeline = pipeline == null ? "built_in" : pipeline.GetType().FullName + ":" + pipeline.name };
            // Time and signature are populated only after the stable settings hash.
            using (var sha = System.Security.Cryptography.SHA256.Create())
                sample.signature = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(sample)))).Replace("-", "").ToLowerInvariant();
            sample.wallSeconds = now;
            qualitySamples.Add(sample); currentQualitySignature = sample.signature;
            if (initialQualitySignature == null) initialQualitySignature = sample.signature;
            else if (initialQualitySignature != sample.signature) invalid.Add("Render quality/resolution signature changed during the run");
        }
        private static double PipelineNumber(object pipeline, string name, double fallback)
        {
            object value = pipeline?.GetType().GetProperty(name)?.GetValue(pipeline);
            return value is IConvertible ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : fallback;
        }
        private static bool PipelineBoolean(object pipeline, string name)
            => pipeline?.GetType().GetProperty(name)?.GetValue(pipeline) is bool flag && flag;
        private void CheckConfiguration(Frame frame)
        {
            if (Time.captureFramerate != 0 || Time.captureDeltaTime != 0) { invalid.Add("Capture clock enabled during real-time benchmark"); report.captureClockDisabled = false; }
            if (Mathf.Abs(Time.timeScale - 1) > .00001f) invalid.Add("timeScale changed from 1");
            if (Screen.width != 1920 || Screen.height != 1080) invalid.Add("Backbuffer was not 1920x1080");
            if (QualitySettings.vSyncCount != 0 || Application.targetFrameRate != (rate == "60" ? 60 : -1)) invalid.Add("Requested VSync/frame-limit settings changed during measurement");
            if (!arena.boss.decisionEnabled) { invalid.Add("Default Boss AI was disabled"); report.defaultBossAI = false; }
            if (arena.player.fighter.TrainingInvincible || arena.boss.fighter.TrainingInvincible) invalid.Add("Training invincibility was enabled");
            if (!gameCamera.enabled || gameCamera.targetTexture != null || (frame.sampled && frame.renders < 1))
            { invalid.Add("Normal game camera did not render to the screen for every sampled interval"); report.normalCameraRendering = false; }
            if (frame.sampled && controlMode == "hardware" && !frame.focused) invalid.Add("Hardware-input sample included unfocused gameplay");
        }
        private void Cache(object target, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            string key = target.GetType().FullName + "." + name;
            if (!members.ContainsKey(key)) members[key] = (MemberInfo)target.GetType().GetProperty(name, flags) ?? target.GetType().GetField(name, flags);
        }
        private object Read(object target, string name)
        {
            string key = target.GetType().FullName + "." + name;
            if (!members.TryGetValue(key, out var member) || member == null) return null;
            try { return member is PropertyInfo property ? property.GetValue(target) : ((FieldInfo)member).GetValue(target); }
            catch { return null; }
        }
        private bool BoolMember(object target, string name) => Read(target, name) is bool flag && flag;
        private string Phase()
        {
            foreach (var name in phaseCandidates)
            {
                object value = Read(arena.boss, name);
                if (value == null) continue;
                if (value is bool flag) return flag ? "phase_2" : "phase_1";
                return name + ":" + value;
            }
            return "unknown";
        }
        private string[] AvailablePhaseMembers()
        {
            var result = new List<string>();
            foreach (var name in phaseCandidates) if (Read(arena.boss, name) != null) result.Add(name);
            return result.ToArray();
        }
        private Machine Describe()
        {
            string identityPath = Path.Combine(Application.streamingAssetsPath, "SAM_BuildIdentity.json");
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return new Machine { unity = Application.unityVersion, operatingSystem = SystemInfo.operatingSystem,
                processor = SystemInfo.processorType, processorCount = SystemInfo.processorCount, systemMemoryMB = SystemInfo.systemMemorySize,
                graphicsDevice = SystemInfo.graphicsDeviceName, graphicsVersion = SystemInfo.graphicsDeviceVersion,
                graphicsType = SystemInfo.graphicsDeviceType.ToString(), graphicsMemoryMB = SystemInfo.graphicsMemorySize,
                width = Screen.width, height = Screen.height, qualityLevel = QualitySettings.GetQualityLevel(),
                qualityName = QualitySettings.names[QualitySettings.GetQualityLevel()], antiAliasing = QualitySettings.antiAliasing,
                renderPipeline = pipeline == null ? "built_in" : pipeline.GetType().FullName + ":" + pipeline.name,
                buildIdentity = File.Exists(identityPath) ? File.ReadAllText(identityPath) : "MISSING",
                executable = Environment.GetCommandLineArgs()[0], projectVersion = Application.version,
                developmentBuild = UnityEngine.Debug.isDebugBuild, fullScreen = Screen.fullScreen, runInBackground = Application.runInBackground,
                vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
                captureFramerate = Time.captureFramerate, timeScale = Time.timeScale, shadowDistance = QualitySettings.shadowDistance,
                renderScale = PipelineFloat(pipeline, "renderScale", 1) };
        }
        private static float PipelineFloat(object target, string property, float fallback)
        {
            if (target == null) return fallback;
            object value = target.GetType().GetProperty(property)?.GetValue(target);
            return value is float result ? result : fallback;
        }
        private void OnCameraRendered(ScriptableRenderContext context, Camera camera) { if (camera == gameCamera) ++renderCount; }
        private void OnLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }
        private void FailStartup(string reason)
        {
            File.WriteAllText(Path.Combine(output, "startup_error.txt"), reason);
            UnityEngine.Debug.LogError(reason); Application.Quit(2);
        }
        private void Complete(bool completed)
        {
            if (saved || report == null) return;
            armed = false; saved = true; clock.Stop();
            CollectEvents(clock.Elapsed.TotalSeconds);
            report.completed = completed; report.status = completed ? "captured_requires_analysis" : "interrupted_invalid";
            if (!completed) invalid.Add("Measurement interrupted before the requested duration");
            report.finishedUtc = DateTime.UtcNow.ToString("o"); report.actualWallSeconds = clock.Elapsed.TotalSeconds;
            report.frameRows = frames.Count; report.roundCount = round;
            report.qualitySamples = qualitySamples.ToArray();
            report.slowPatrolSearches = new List<ArenaPatrolPlanner.SearchTiming>(arena.boss.PatrolPlanner.SlowSearchTimings).ToArray();
            report.invalidReasons = new List<string>(invalid).ToArray(); report.errors = errors.ToArray();
            File.WriteAllText(Path.Combine(output, "metadata.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, "events.json"), JsonUtility.ToJson(new EventList { events = events.ToArray() }, true));
            using (var writer = new StreamWriter(Path.Combine(output, "frames.csv"), false, new UTF8Encoding(false)))
            {
                writer.WriteLine("frame,round,start_s,end_s,frame_ms,sampled,battle_s,state,skill,phase,attacking,player_state,boss_health,player_health,active,focused,camera_renders,width,height,input_locked,move_x,move_y,run,attack,dodge,lock,camera_x,camera_y,camera_z,camera_qx,camera_qy,camera_qz,camera_qw,camera_fov,camera_aspect,camera_near,camera_far,boss_bounds_available,boss_visible,boss_cx,boss_cy,boss_cz,boss_sx,boss_sy,boss_sz,meteor_evidence_available,meteor_active,meteor_visible,meteor_provider,meteor_cx,meteor_cy,meteor_cz,meteor_sx,meteor_sy,meteor_sz,quality_signature");
                foreach (var f in frames)
                    writer.WriteLine(string.Join(",", f.frame, f.round, N(f.start), N(f.end), N(f.ms), B(f.sampled), N(f.battle),
                        Q(f.state), Q(f.skill), Q(f.phase), B(f.attacking), Q(f.playerState), N(f.bossHealth), N(f.playerHealth),
                        B(f.active), B(f.focused), f.renders, f.width, f.height, B(f.inputLocked), N(f.command.move.x), N(f.command.move.y),
                        B(f.command.run), B(f.command.attack), B(f.command.dodge), B(f.command.toggleLock),
                        N(f.cameraPosition.x), N(f.cameraPosition.y), N(f.cameraPosition.z),
                        N(f.cameraRotation.x), N(f.cameraRotation.y), N(f.cameraRotation.z), N(f.cameraRotation.w),
                        N(f.cameraFov), N(f.cameraAspect), N(f.cameraNear), N(f.cameraFar), B(f.bossBoundsAvailable), B(f.bossVisible),
                        N(f.bossBounds.center.x), N(f.bossBounds.center.y), N(f.bossBounds.center.z),
                        N(f.bossBounds.size.x), N(f.bossBounds.size.y), N(f.bossBounds.size.z),
                        B(f.meteorEvidenceAvailable), B(f.meteorActive), B(f.meteorVisible), Q(f.meteorProvider),
                        N(f.meteorBounds.center.x), N(f.meteorBounds.center.y), N(f.meteorBounds.center.z),
                        N(f.meteorBounds.size.x), N(f.meteorBounds.size.y), N(f.meteorBounds.size.z), Q(f.qualitySignature)));
            }
            UnityEngine.Debug.Log("SAM_REALTIME_CAPTURE_FINISHED " + output + "; raw evidence requires coverage and timing analysis");
            if (completed) Application.Quit(errors.Count == 0 && invalid.Count == 0 ? 0 : 2);
        }
        [Serializable] private sealed class EventList { public EventRow[] events; }
        private static string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string B(bool value) => value ? "1" : "0";
        private static string Arg(string[] args, string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        private static double Number(string[] args, string name, double fallback)
        { string value = Arg(args, name, null); return value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && result > 0 ? result : fallback; }
        private void OnApplicationQuit() { if (!saved) Complete(false); }
        private void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= OnCameraRendered;
            Application.logMessageReceived -= OnLog;
        }
    }
}
