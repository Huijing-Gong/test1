using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SamProduction.BossArena
{
    public sealed class ArenaValidation : MonoBehaviour
    {
        public ArenaSession arena;
        public Mesh originalBossMesh, correctedBossMesh;
        private readonly List<string> errors = new List<string>();
        private ArenaAudioCapture pendingAudioCapture;
        private List<CaptureFrame> pendingFrames;
        private CaptureReport pendingCaptureReport;
        private bool runtimeChecksPassed = true, captureReportWritten, audioRequested;
        private string validationOutput, validationPhase;
        [Serializable] private sealed class CaptureFrame
        {
            public int index;
            public double time;
            public double captureTime_s;
            public string file, bossState, bossAction, playerState;
            public float playerHealth, bossHealth;
            public float bossWalkPhase, bossLocomotionTime;
            public bool bossBlocked;
            public Vector3 bossForward;
            public Vector3 playerPosition, bossPosition, rightHand, leftFoot, rightFoot;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public float cameraActualAngularSpeed, cameraOrbitAngularSpeed, cameraLockYawError, cameraCloseFramingWeight;
            public bool cameraRotationLimited, cameraWorldConstrained, closeCameraOverride;
            public int cameraRenderCallbacks;
            public ArenaCommand input;
        }
        [Serializable] private sealed class CaptureReport
        {
            public string status, unity, graphicsDevice;
            public string source = "Actual Unity combat camera; scripted commands enter the same player simulation and collision damage as hardware input.";
            public string mode = "normal_ai_gameplay", practiceSkill;
            public float practiceInitialDistance;
            public string audio = "No captured audio in this technical gameplay preview.";
            public string audioReportFile, audioWavFile, audioReportSha256, audioWavSha256;
            public int width, height, frameCount, fps = 60;
            public string renderMode, cameraAntialiasing, cameraClearFlags;
            public int targetSamples, pipelineSamples, actualCameraRenderCallbacks;
            public bool postProcessing, explicitMSAAResolve, singleManualRenderVerified;
            public CaptureFrame[] frames;
            public ArenaEventRecord[] events;
            public string[] errors;
        }
        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args,"--arena-qa") < 0 && Array.IndexOf(args,"--arena-demo") < 0 &&
                Array.IndexOf(args,"--arena-snapshot") < 0) yield break;
            validationOutput = Arg(args,"--arena-output");
            int previousCaptureRate = Time.captureFramerate, previousTargetRate = Application.targetFrameRate;
            Application.logMessageReceived += Log;
            IEnumerator routine = RunValidation(args);
            try
            {
                // A coroutine's exceptions occur in MoveNext, including work resumed
                // after a yield. Catch there so every failed phase still reports/exits.
                while (true)
                {
                    bool advance = false;
                    object yielded = null;
                    try { advance = routine.MoveNext(); if (advance) yielded = routine.Current; }
                    catch (Exception exception) { advance = false; errors.Add("Validation phase " + validationPhase + ": " + exception); }
                    if (!advance) break;
                    yield return yielded;
                }
            }
            finally
            {
                TryCleanup(() => (routine as IDisposable)?.Dispose());
                TryCleanup(FinishValidation);
                Application.logMessageReceived -= Log;
                Time.captureFramerate = previousCaptureRate;
                Application.targetFrameRate = previousTargetRate;
                bool passed = runtimeChecksPassed && errors.Count == 0;
                Debug.Log("BOSS_ARENA_VALIDATION_FINISHED " + validationOutput + " passed=" + passed);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(passed?0:1);
#else
                Application.Quit(passed?0:1);
#endif
            }
        }

        private IEnumerator RunValidation(string[] args)
        {
            bool qa = Array.IndexOf(args,"--arena-qa") >= 0;
            bool demo = Array.IndexOf(args,"--arena-demo") >= 0;
            bool snapshot = Array.IndexOf(args,"--arena-snapshot") >= 0;
            bool close = Array.IndexOf(args,"--arena-close") >= 0;
            bool withAudio = Array.IndexOf(args,"--arena-audio") >= 0;
            bool practice = Array.IndexOf(args,"--arena-skill") >= 0;
            ArenaAttack practiceSkill = default;
            if(practice && (!demo || !Enum.TryParse(Arg(args,"--arena-skill"),true,out practiceSkill) ||
                (practiceSkill != ArenaAttack.ComboThree && practiceSkill != ArenaAttack.ComboTwo && practiceSkill != ArenaAttack.Stomp)))
                throw new ArgumentException("--arena-skill requires --arena-demo and ComboThree, ComboTwo or Stomp.");
            if (!qa && !demo && !snapshot) yield break;
            string output = Arg(args,"--arena-output");
            if (string.IsNullOrEmpty(output)) throw new ArgumentException("--arena-output required");
            Directory.CreateDirectory(output);
            if(File.Exists(Path.Combine(output,"capture.json")) || File.Exists(Path.Combine(output,"audio.wav")) ||
                File.Exists(Path.Combine(output,"runtime_errors.json")))
                throw new IOException("Validation requires a fresh output directory; existing evidence is preserved.");
            arena.automaticTick = false;
            arena.view.automaticTick = false;
            // The synchronous gameplay contract suite does not run a DSP clock.
            // Audio is validated separately through the real capture renderer.
            ArenaAudio originalSound = arena.sound;
            bool originalSoundEnabled = originalSound != null && originalSound.enabled;
            if(qa && originalSound != null) originalSound.enabled = false;
            Time.captureFramerate = 60;
            Application.targetFrameRate = -1;
            yield return null;
            bool passed = true;
            if (qa)
            {
                // Null the Session reference as well: even a disabled component's
                // explicitly called Tick would otherwise read the DSP clock.
                arena.sound = null;
                try
                {
                var allCases = new List<ArenaCheckCase>();
                foreach (bool combat in new[] { false, true })
                {
                    string suite = combat ? "P3 combat" : "basic arena";
                    validationPhase = suite + " runtime checks";
                    try
                    {
                        ArenaRuntimeCheckReport part = combat ? ArenaCombatChecks.Run(arena) : ArenaRuntimeChecks.Run(arena);
                        if(part == null) throw new InvalidOperationException(suite+" returned no report.");
                        if(part.cases == null || part.cases.Length == 0) throw new InvalidOperationException(suite+" returned no executed cases.");
                        passed &= part.passed;
                        foreach(var check in part.cases) passed &= check != null && check.passed;
                        allCases.AddRange(part.cases ?? Array.Empty<ArenaCheckCase>());
                        File.WriteAllText(Path.Combine(output,combat?"combat_qa.json":"basic_runtime_qa.json"),JsonUtility.ToJson(part,true));
                    }
                    catch (Exception exception)
                    {
                        errors.Add(suite+": "+exception); passed = false;
                        allCases.Add(new ArenaCheckCase { name=suite+" suite execution", passed=false, error=exception.ToString() });
                    }
                }
                var report = new ArenaRuntimeCheckReport { passed=passed, cases=allCases.ToArray() };
                File.WriteAllText(Path.Combine(output,"runtime_qa.json"),JsonUtility.ToJson(report,true));
                runtimeChecksPassed = passed;
                }
                finally
                {
                    arena.sound = originalSound;
                    if(originalSound != null) originalSound.enabled = originalSoundEnabled;
                }
            }
            if (demo || snapshot)
            {
                var camera = arena.view.GetComponent<Camera>();
                bool originalCameraEnabled = camera.enabled;
                RenderTexture originalTarget = camera.targetTexture;
                float originalAspect = camera.aspect;
                // An enabled camera with targetTexture also renders during the normal
                // frame. Capture owns this camera and submits exactly one request per PNG.
                // This removes duplicate rendering; it is not evidence of temporal ghosting.
                camera.enabled = false;
                var target = new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB) { antiAliasing = SamFrameReadback.Samples };
                target.Create();
                camera.targetTexture = target;
                camera.aspect = target.width / (float)target.height;
                arena.hud.Build(arena,camera);
                var texture = new Texture2D(target.width,target.height,TextureFormat.RGB24,false,false);
                var readback = new SamFrameReadback(target);
                ArenaAudioCaptureReport audioResult = null;
                var frames = new List<CaptureFrame>();
                pendingFrames = frames;
                pendingCaptureReport = new CaptureReport { status="capture_failed", unity=Application.unityVersion,
                    graphicsDevice=SystemInfo.graphicsDeviceName, width=target.width, height=target.height,
                    renderMode="single_manual_request_camera_disabled", targetSamples=target.antiAliasing };
                SetCaptureMode(pendingCaptureReport,practice,practiceSkill);
                int cameraRenderCallbacks = 0;
                bool singleManualRenderVerified = true;
                Action<ScriptableRenderContext, Camera> countRender = (context, renderedCamera) =>
                { if (renderedCamera == camera) cameraRenderCallbacks++; };
                RenderPipelineManager.beginCameraRendering += countRender;
                try
                {
                arena.boss.decisionEnabled = !snapshot && !practice;
                // A combined QA+demo invocation re-enables the hardware/DSP backend
                // after the synchronous contract fixtures deliberately disabled it.
                if(arena.sound != null) arena.sound.enabled = true;
                if(practice)
                {
                    // Process-local fixture only. Damage, hitboxes, animation and
                    // the input simulation remain identical to the playable arena.
                    arena.bossStart = new Vector3(0,.05f,0);
                    arena.playerStart = new Vector3(0,.05f,-1.8f);
                }
                arena.Restart(true);
                if(withAudio && demo)
                {
                    audioRequested = true;
                    validationPhase = "audio Begin";
                    if(arena.sound == null) throw new InvalidOperationException("Audio capture requested without a configured audio backend.");
                    pendingAudioCapture = new ArenaAudioCapture(arena.sound);
                    pendingAudioCapture.Begin(Path.Combine(output,"audio.wav"),60,arena.BattleTime);
                    // Let Unity adopt the modified loop before queuing a pass;
                    // this interval contains no gameplay or audio events.
                    yield return null;
                    int stableWarmupFrames = 0;
                    for(int warmup=0;warmup<8 && stableWarmupFrames<2;warmup++)
                    {
                        validationPhase = "audio native warmup " + warmup;
                        pendingAudioCapture.QueueWarmupFrame(warmup);
                        yield return null;
                        stableWarmupFrames = pendingAudioCapture.CompleteWarmupFrame() ? stableWarmupFrames+1 : 0;
                    }
                    if(stableWarmupFrames<2)
                        throw new InvalidOperationException("Native audio did not provide two positive DSP blocks with matching sample-clock advance during eight pre-simulation warmup frames. See audio diagnostics.");
                    pendingAudioCapture.StartTimeline();
                }
                arena.view.TickLook(Vector2.zero,true);
                if (Array.IndexOf(args,"--arena-original-normals") >= 0 && originalBossMesh != null)
                    arena.boss.visual.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh = originalBossMesh;
                if (close)
                {
                    arena.hud.gameObject.SetActive(false);
                    foreach(var renderer in arena.player.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                    camera.fieldOfView = 38;
                }
                int count = snapshot ? 1 : 2160;
                string requestedFrames=Arg(args,"--arena-frames");
                if(!snapshot && requestedFrames!=null)
                {
                    if(!int.TryParse(requestedFrames,out count)||count<2||count>7200)throw new ArgumentException("Invalid --arena-frames");
                }
                bool practiceStarted = false;
                for(int index=0;index<count;index++)
                {
                    int renderCallbacksBefore = cameraRenderCallbacks;
                    ArenaCommand command = snapshot || practice ? default : DemoCommand(index);
                    validationPhase = "simulation/emit frame " + index;
                    arena.Simulate(command,1f/60);
                    if(practice && index == 15)
                    {
                        // A short real settling interval establishes Brain's
                        // Session reference before the single requested skill.
                        arena.Record("practice_skill_preview",practiceSkill.ToString(),arena.boss.fighter,0,arena.boss.transform.position);
                        if(!arena.boss.TrySkill(practiceSkill))
                            throw new InvalidOperationException("Practice preset failed to start " + practiceSkill);
                        practiceStarted = true;
                    }
                    arena.view.TickCamera(1f/60);
                    if(close)
                    {
                        Vector3 focus = arena.boss.transform.position + Vector3.up * 1.7f;
                        camera.transform.SetPositionAndRotation(focus + new Vector3(2.7f,.35f,-5.8f),
                            Quaternion.LookRotation(new Vector3(-2.7f,-.35f,5.8f)));
                    }
                    arena.hud.Refresh();
                    Canvas.ForceUpdateCanvases();
                    if(pendingAudioCapture != null) pendingAudioCapture.QueueRenderFrame(index);
                    yield return null;
                    // The queued native render ran immediately after Unity's
                    // PostLateUpdate.UpdateAudio, with this same listener/camera
                    // pose. A plain coroutine resume is not the native audio phase.
                    if(pendingAudioCapture != null)
                    {
                        validationPhase = "audio RenderFrame " + index;
                        pendingAudioCapture.CompleteQueuedFrame();
                    }
                    validationPhase = "image capture frame " + index;
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                    int thisFrameRenders = cameraRenderCallbacks - renderCallbacksBefore;
                    if (thisFrameRenders != 1)
                    {
                        singleManualRenderVerified = false;
                        errors.Add("Capture frame " + index + " observed " + thisFrameRenders + " combat-camera render callbacks; expected exactly one.");
                    }
                    readback.Read(texture);
                    string name=$"frame_{index:D4}.png";
                    File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());
                    frames.Add(new CaptureFrame{index=index,time=arena.BattleTime,captureTime_s=index/60.0,file=name,input=command,
                        bossState=arena.boss.StateName,bossAction=arena.boss.ActionName,playerState=arena.player.ActionName,
                        playerHealth=arena.player.fighter.Health,bossHealth=arena.boss.fighter.Health,
                        bossWalkPhase=arena.boss.WalkPhase,bossLocomotionTime=arena.boss.LocomotionTime,
                        bossBlocked=arena.boss.LocomotionBlocked,bossForward=arena.boss.transform.forward,
                        cameraPosition=camera.transform.position,cameraRotation=camera.transform.rotation,cameraRenderCallbacks=thisFrameRenders,
                        cameraActualAngularSpeed=arena.view.ActualAngularSpeedDegPerSecond,
                        cameraOrbitAngularSpeed=arena.view.OrbitAngularSpeedDegPerSecond,
                        cameraLockYawError=arena.view.LockYawErrorDegrees,cameraCloseFramingWeight=arena.view.CloseFramingWeight,
                        cameraRotationLimited=arena.view.RotationLimitedThisTick,cameraWorldConstrained=arena.view.WorldConstrainedThisTick,
                        closeCameraOverride=close,
                        playerPosition=arena.player.transform.position,bossPosition=arena.boss.transform.position,rightHand=arena.boss.FistPosition,
                        leftFoot=arena.boss.Socket("socket_foot.L").position,rightFoot=arena.boss.Socket("socket_foot.R").position});
                }
                if(practice && (!practiceStarted || arena.boss.Attacking))
                    throw new InvalidOperationException("Practice capture must include the complete skill and return to guard; increase --arena-frames.");
                var cameraData = camera.GetUniversalAdditionalCameraData();
                if(pendingAudioCapture != null)
                {
                    // Finish the final native quantum without advancing gameplay.
                    // These separately identified tail blocks retain actual PCM;
                    // no zero padding, frame duplication or synthetic mix is used.
                    for(int tail=0;!pendingAudioCapture.CoversVideoEnd && tail<pendingAudioCapture.MaximumTailPasses;tail++)
                    {
                        validationPhase = "audio native end drain " + tail;
                        pendingAudioCapture.QueueTailFrame();
                        yield return null;
                        pendingAudioCapture.CompleteQueuedFrame();
                    }
                    if(!pendingAudioCapture.CoversVideoEnd)
                        throw new InvalidOperationException("Native audio did not reach the final video sample boundary within its measured DSP-quantum drain limit.");
                    validationPhase = "audio Finish";
                    audioResult = pendingAudioCapture.Finish(Path.Combine(output,"audio_capture.json"));
                    pendingAudioCapture = null;
                    if(!audioResult.passed) errors.Add(audioResult.error);
                }
                var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var report=new CaptureReport{status=errors.Count==0?"captured_actual_gameplay_pending_review":"capture_failed",unity=Application.unityVersion,
                    graphicsDevice=SystemInfo.graphicsDeviceName,width=target.width,height=target.height,frameCount=frames.Count,
                    renderMode="single_manual_request_camera_disabled",targetSamples=target.antiAliasing,
                    pipelineSamples=pipeline!=null?pipeline.msaaSampleCount:0,
                    cameraAntialiasing=cameraData.antialiasing.ToString(),cameraClearFlags=camera.clearFlags.ToString(),
                    postProcessing=cameraData.renderPostProcessing,explicitMSAAResolve=target.antiAliasing>1,
                    actualCameraRenderCallbacks=cameraRenderCallbacks,singleManualRenderVerified=singleManualRenderVerified,
                    frames=frames.ToArray(),events=arena.Events.ToArray(),errors=errors.ToArray()};
                SetCaptureMode(report,practice,practiceSkill);
                if(practice && errors.Count == 0) report.status="captured_practice_skill_pending_review";
                if(audioResult != null && audioResult.passed)
                {
                    report.audio = "Actual Unity AudioRenderer native stereo/mono PCM from this same simulation; no replacement mix.";
                    report.audioReportFile = "audio_capture.json"; report.audioWavFile = "audio.wav";
                    report.audioReportSha256 = FileHash(Path.Combine(output,report.audioReportFile));
                    report.audioWavSha256 = FileHash(Path.Combine(output,report.audioWavFile));
                }
                File.WriteAllText(Path.Combine(output,"capture.json"),JsonUtility.ToJson(report,true));
                pendingCaptureReport = report;
                captureReportWritten = true;
                }
                finally
                {
                    RenderPipelineManager.beginCameraRendering -= countRender;
                    TryCleanup(() => readback.Dispose());
                    TryCleanup(() => { camera.targetTexture=originalTarget;camera.aspect=originalAspect;camera.enabled=originalCameraEnabled; });
                    TryCleanup(() => target.Release());
                    TryCleanup(() => Destroy(target)); TryCleanup(() => Destroy(texture));
                }
            }
        }

        private static void SetCaptureMode(CaptureReport report, bool practice, ArenaAttack skill)
        {
            if(!practice) return;
            report.mode="practice_skill_preview";
            report.practiceSkill=skill.ToString();
            report.practiceInitialDistance=1.8f;
            report.source="Practice-mode skill preview: explicit TrySkill preset, AI decisions disabled, one complete skill, stationary player with normal collision/health. Not autonomous AI gameplay.";
        }

        private void FinishValidation()
        {
            if (pendingAudioCapture != null)
            {
                var failure = new InvalidOperationException(errors.Count > 0 ? string.Join("\n",errors) : "Audio capture ended without Finish.");
                TryCleanup(() => pendingAudioCapture.Fail(EvidencePath("audio_capture.json"),failure));
                TryCleanup(() => pendingAudioCapture.Dispose());
                pendingAudioCapture = null;
            }
            else if(audioRequested && !File.Exists(Path.Combine(validationOutput ?? "", "audio_capture.json")))
            {
                TryCleanup(() => File.WriteAllText(EvidencePath("audio_capture.json"),JsonUtility.ToJson(
                    new ArenaAudioCaptureReport { passed=false, error="Audio capture could not start: "+string.Join("\n",errors) },true)));
            }
            if(pendingCaptureReport != null && !captureReportWritten)
            {
                pendingCaptureReport.status="capture_failed";
                pendingCaptureReport.frames=pendingFrames?.ToArray() ?? Array.Empty<CaptureFrame>();
                pendingCaptureReport.frameCount=pendingCaptureReport.frames.Length;
                pendingCaptureReport.events=arena != null ? arena.Events.ToArray() : Array.Empty<ArenaEventRecord>();
                pendingCaptureReport.errors=errors.ToArray();
                TryCleanup(() => File.WriteAllText(EvidencePath("capture.json"),JsonUtility.ToJson(pendingCaptureReport,true)));
            }
            TryCleanup(() => File.WriteAllText(EvidencePath("runtime_errors.json"),JsonUtility.ToJson(new ErrorReport{errors=errors.ToArray()},true)));
        }

        private string EvidencePath(string file)
        {
            if(string.IsNullOrEmpty(validationOutput)) throw new ArgumentException("Cannot write report without --arena-output.");
            Directory.CreateDirectory(validationOutput);
            string path=Path.Combine(validationOutput,file);
            // A cleanup failure must never overwrite an already written success record.
            if(File.Exists(path)) path=Path.Combine(validationOutput,Path.GetFileNameWithoutExtension(file)+"_failure_"+Guid.NewGuid().ToString("N")+".json");
            return path;
        }

        private void TryCleanup(Action action)
        {
            try { action(); }
            catch(Exception exception) { errors.Add("Validation cleanup/report: "+exception); }
        }
        private ArenaCommand DemoCommand(int frame)
        {
            float distance=Vector3.ProjectOnPlane(arena.boss.transform.position-arena.player.transform.position,Vector3.up).magnitude;
            ArenaCommand command=default;
            if(!arena.Active)return command;
            Vector3 towardBoss = Vector3.ProjectOnPlane(arena.boss.transform.position - arena.player.transform.position, Vector3.up).normalized;
            Vector3 cameraForward = arena.view.PlanarForward, cameraRight = Vector3.Cross(Vector3.up, cameraForward);
            Vector2 approach = new Vector2(Vector3.Dot(towardBoss, cameraRight), Vector3.Dot(towardBoss, cameraForward));
            // Demonstrate actual pursuit as well as close evasion. These are
            // ordinary movement inputs: AI, positions, health and damage remain
            // untouched. Give the boss time to finish recovery and close the gap.
            if((frame >= 660 && frame < 960) || (frame >= 1380 && frame < 1620))
            {
                command.move = distance < 7.5f ? -approach : Vector2.zero;
                command.run = true;
                return command;
            }
            if(distance>2.3f)command.move=approach;
            // Deliberately accept the first readable attack, then evade and counter using real commands.
            if(frame>420 && arena.boss.Attacking && !arena.boss.IsRecovering)
            {
                bool stomp = arena.boss.CurrentAttack == ArenaAttack.Stomp;
                float safeDistance = stomp ? arena.boss.stompRadius + .55f : arena.boss.jabDistance + 1.15f;
                // Continue escaping the whole committed combination. Do not run
                // back into the second fist when the first anticipation UI ends.
                command.move = distance < safeDistance ? Vector2.right : Vector2.zero;
                bool thirdCharge = arena.boss.CurrentAttack == ArenaAttack.ComboThree && arena.boss.SkillTime >= 1.25f;
                // Real dodge input, timed from readable anticipation; the heavy
                // third strike is approached 0.15s before its 1.766667s active window.
                float trigger = stomp ? .78f : thirdCharge ? 1.616667f : .28f;
                command.dodge=arena.boss.Telegraph && distance < safeDistance &&
                    arena.boss.SkillTime>=trigger&&arena.boss.SkillTime<trigger+.06f;
            }
            if(arena.boss.IsRecovering)
            {
                command.move=distance>1.3f?approach:Vector2.zero;
                command.run=distance>2f;
                // Starting a sword swing too far away slows the player and can
                // queue endless whiffs. Close normally before pressing attack.
                command.attack=distance<1.65f && frame%23==0;
            }
            return command;
        }
        [Serializable] private sealed class ErrorReport { public string[] errors; }
        private void Log(string message,string stack,LogType type)
        { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack); }
        private static string Arg(string[] args,string key){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        private static string FileHash(string path)
        {
            using(var hash = SHA256.Create()) using(var stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
