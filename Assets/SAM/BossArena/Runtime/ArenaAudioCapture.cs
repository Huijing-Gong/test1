using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.LowLevel;

namespace SamProduction.BossArena
{
    [Serializable]
    public sealed class ArenaAudioCaptureBlock
    {
        public int frame, sampleFrames;
        public long firstSampleFrame;
        public double semanticStart, dspBefore, dspAfter;
        public string kind;
        public double videoEndTime, cumulativeErrorSamples;
        public bool nativeRenderSucceeded;
        public float peak;
    }

    [Serializable]
    public sealed class ArenaAudioCaptureDiagnostic
    {
        public string stage, audioDisabled;
        public int unityFrame, samples, activeListeners, totalListeners;
        public bool batchMode, focused, listenerPaused, backendEnabled, nativeRenderSucceeded;
        public float deltaTime, captureDeltaTime, timeScale, listenerVolume;
        public double dspTime, realtime;
        public float renderedPeak;
    }

    [Serializable]
    public sealed class ArenaAudioCaptureReport
    {
        public string method = "Unity AudioRenderer.Render native main-output PCM; not remixed cue audio";
        public string wavPath, error;
        public bool passed;
        public int sampleRate, channels, videoFps, frames, nonfiniteSamples, samplesOverFullScale;
        public int dspBufferLength, dspBufferCount;
        public long sampleFrames, videoSampleFrames, tailSampleFrames;
        public double duration, peak, rms, videoDuration, finalDurationDifference, maxAbsCumulativeErrorSamples;
        public ArenaAudioCaptureBlock[] blocks, tailBlocks;
        public ArenaSoundEvent[] events;
        public string renderPhase = "PlayerLoop immediately after PostLateUpdate.UpdateAudio";
        public string warmupPolicy = "Before simulation, submit native-reported buffers including zero; require two positive DSP blocks with matching native DSP advance. Warmup PCM is excluded from gameplay WAV.";
        public string alignmentPolicy = "Preserve every native PCM sample, including 0-sample video blocks. If needed, drain native audio only after simulation ends until WAV covers video; preserve the final partial DSP quantum as an explicit tail, never pad or trim source WAV.";
        public ArenaAudioCaptureDiagnostic[] diagnostics;
        public int warmupFrames;
        public double timelineOriginDsp, timelineSemanticOrigin;
    }

    /// <summary>
    /// One owner per Unity audio engine. Call Begin before simulation, then RenderFrame
    /// once per corresponding video frame AFTER its simulation/pose and camera update.
    /// Writes native float32 WAV without normalization/limiting or synthetic replacement.
    /// </summary>
    public sealed class ArenaAudioCapture : IDisposable
    {
        private readonly ArenaAudio audio;
        private readonly ArenaAudioCaptureReport report = new ArenaAudioCaptureReport();
        private readonly List<ArenaAudioCaptureBlock> blocks = new List<ArenaAudioCaptureBlock>();
        private readonly List<ArenaAudioCaptureBlock> tailBlocks = new List<ArenaAudioCaptureBlock>();
        private readonly List<ArenaAudioCaptureDiagnostic> diagnostics = new List<ArenaAudioCaptureDiagnostic>();
        private BinaryWriter writer;
        private bool ownsRenderer, ownsCaptureRate, closed;
        private int originalCaptureRate, generation;
        private double origin, sumSquares;
        private bool ownsLoopHook, timelineArmed, operationCompleted, warmupStable;
        private int consecutiveWarmupFrames;
        private Action queuedOperation;
        private Exception operationError;
        private sealed class ArenaNativeAudioRenderPass { }

        public ArenaAudioCapture(ArenaAudio backend) { audio = backend != null ? backend : throw new ArgumentNullException(nameof(backend)); }

        public void Begin(string wavPath, int fps, double semanticOrigin = 0)
        {
            if (writer != null || closed) throw new InvalidOperationException("Create a fresh capture per recording.");
            if (fps <= 0 || fps > 240) throw new ArgumentOutOfRangeException(nameof(fps));
            var configuration = AudioSettings.GetConfiguration();
            int channels = configuration.speakerMode == AudioSpeakerMode.Mono ? 1 : configuration.speakerMode == AudioSpeakerMode.Stereo ? 2 : 0;
            if (channels == 0) throw new InvalidOperationException("Capture requires actual Mono or Stereo speaker configuration.");
            if (AudioListener.pause || AudioListener.volume <= 0) throw new InvalidOperationException("The audio listener is paused or muted.");
            report.wavPath = Path.GetFullPath(wavPath); report.sampleRate = configuration.sampleRate;
            report.channels = channels; report.videoFps = fps; origin = semanticOrigin;
            report.timelineSemanticOrigin = semanticOrigin;
            AudioSettings.GetDSPBufferSize(out report.dspBufferLength,out report.dspBufferCount);
            if(report.sampleRate<=0 || report.dspBufferLength<=0)
                throw new InvalidOperationException("Audio device did not expose a valid sample rate and DSP quantum.");
            originalCaptureRate = Time.captureFramerate;
            Trace("before Begin",-1);
            try
            {
                Time.captureFramerate = fps;
                ownsCaptureRate = true;
                if (!AudioRenderer.Start()) throw new InvalidOperationException("Unity AudioRenderer is already owned by another recording.");
                ownsRenderer = true;
                generation = audio.Generation;
                InstallLoopHook();
                Directory.CreateDirectory(Path.GetDirectoryName(report.wavPath));
                writer = new BinaryWriter(new FileStream(report.wavPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
                WriteHeader(0);
                Trace("after Begin",AudioRenderer.GetSampleCountForCaptureFrame());
            }
            catch { Dispose(); throw; }
        }

        public void QueueWarmupFrame(int index)
        {
            if(timelineArmed) throw new InvalidOperationException("Warmup cannot skip gameplay audio.");
            Queue(() =>
            {
                if(audio.Generation != generation || audio.Events.Count != 0 || audio.ActiveVoiceCount != 0)
                    throw new InvalidOperationException("Audio warmup must run before gameplay events or restarts.");
                int count = AudioRenderer.GetSampleCountForCaptureFrame();
                var diagnostic = Trace("warmup " + index + " after UpdateAudio",count);
                if(count < 0 || count > report.sampleRate)
                    throw new InvalidOperationException("Native audio warmup returned an invalid sample count: " + count);
                // Unity Recorder submits Render even for a zero-length first
                // native buffer. This primes the renderer; no PCM is fabricated.
                using(var buffer = new NativeArray<float>(count * report.channels,Allocator.Temp))
                {
                    diagnostic.nativeRenderSucceeded = AudioRenderer.Render(buffer);
                    if(!diagnostic.nativeRenderSucceeded) throw new InvalidOperationException("Native audio warmup Render failed.");
                    for(int i=0;i<buffer.Length;i++)
                    {
                        if(float.IsNaN(buffer[i]) || float.IsInfinity(buffer[i]))
                            throw new InvalidDataException("Native audio warmup contains nonfinite PCM.");
                        diagnostic.renderedPeak = Mathf.Max(diagnostic.renderedPeak,Mathf.Abs(buffer[i]));
                    }
                }
                report.warmupFrames++;
                warmupStable = count > 0 && Math.Abs((AudioSettings.dspTime-diagnostic.dspTime)*report.sampleRate-count) <= 1;
                consecutiveWarmupFrames = warmupStable ? consecutiveWarmupFrames+1 : 0;
                Trace("warmup " + index + " after Render",-1);
            });
        }

        public bool CompleteWarmupFrame() { CompleteQueuedFrame(); return warmupStable; }

        public void StartTimeline()
        {
            if(timelineArmed || consecutiveWarmupFrames < 2 || queuedOperation != null)
                throw new InvalidOperationException("Audio must complete its native warmup before starting the timeline.");
            if(audio.Generation != generation || audio.Events.Count != 0 || audio.ActiveVoiceCount != 0)
                throw new InvalidOperationException("Audio timeline cannot hide pre-existing gameplay events.");
            report.timelineOriginDsp = AudioSettings.dspTime;
            audio.BeginCaptureClock(origin,report.timelineOriginDsp);
            timelineArmed = true;
            Trace("timeline armed",-1);
        }

        public void QueueRenderFrame(int frame)
        {
            if(!timelineArmed) throw new InvalidOperationException("Start the capture timeline before simulation.");
            Queue(() => RenderFrame(frame));
        }

        public bool CoversVideoEnd => blocks.Count > 0 &&
            report.sampleFrames >= (long)Math.Ceiling(blocks.Count*report.sampleRate/(double)report.videoFps);
        public int MaximumTailPasses => (int)Math.Ceiling(report.dspBufferLength*report.videoFps/(double)report.sampleRate)+2;

        public void QueueTailFrame()
        {
            if(CoversVideoEnd || blocks.Count == 0) throw new InvalidOperationException("No native tail drain is needed.");
            Queue(() =>
            {
                var block = RenderNativeBlock(blocks.Count+tailBlocks.Count,true);
                tailBlocks.Add(block); report.tailSampleFrames += block.sampleFrames;
                ValidateNativeClock(block);
                double excess = report.sampleFrames-blocks.Count*report.sampleRate/(double)report.videoFps;
                if(excess > report.dspBufferLength+1)
                    throw new InvalidOperationException("Native end drain exceeded one DSP quantum: " + excess + " samples.");
            });
        }

        private void Queue(Action action)
        {
            if(closed || !ownsLoopHook || queuedOperation != null)
                throw new InvalidOperationException("A native audio operation is unavailable or already queued.");
            operationCompleted = false; operationError = null; queuedOperation = action;
        }

        public void CompleteQueuedFrame()
        {
            if(!operationCompleted)
                throw new InvalidOperationException("The scheduled PostLateUpdate audio pass did not execute before the next simulation frame.");
            if(operationError != null) throw new InvalidOperationException("Native audio frame operation failed.",operationError);
        }

        private void RunQueuedOperation()
        {
            var operation = queuedOperation;
            if(operation == null) return;
            queuedOperation = null;
            try { operation(); }
            catch(Exception exception) { operationError = exception; }
            finally { operationCompleted = true; }
        }

        public ArenaAudioCaptureBlock RenderFrame(int frame)
        {
            if(frame != blocks.Count || tailBlocks.Count != 0) throw new InvalidOperationException("Audio/video frame sequence is not contiguous.");
            var block = RenderNativeBlock(frame,false);
            blocks.Add(block); report.videoSampleFrames += block.sampleFrames;
            report.maxAbsCumulativeErrorSamples=Math.Max(report.maxAbsCumulativeErrorSamples,Math.Abs(block.cumulativeErrorSamples));
            ValidateNativeClock(block);
            if(Math.Abs(block.cumulativeErrorSamples) > report.dspBufferLength+1)
                throw new InvalidOperationException("Cumulative native audio/video offset exceeded the actual DSP quantum: " +
                    block.cumulativeErrorSamples + " samples; quantum=" + report.dspBufferLength);
            return block;
        }

        private ArenaAudioCaptureBlock RenderNativeBlock(int frame,bool tail)
        {
            if (writer == null || !ownsRenderer || closed || !timelineArmed) throw new InvalidOperationException("Capture timeline has not begun.");
            if (generation != audio.Generation) throw new InvalidOperationException("Arena restarted during capture; use a new recording origin.");
            int count = AudioRenderer.GetSampleCountForCaptureFrame();
            // Actual Unity 6000.3 output is DSP-quantized (0/1024 on this host),
            // not necessarily 800 per 60 Hz video frame. Preserve each native block.
            if(frame < 3 || tail || count < 0)
                Trace("capture " + frame + " after UpdateAudio",count);
            if(count < 0 || count > report.sampleRate)
                throw new InvalidOperationException("Native audio returned an invalid sample count: " + count);
            double videoEnd=(tail?blocks.Count:frame+1d)/report.videoFps;
            var block = new ArenaAudioCaptureBlock { frame = frame, sampleFrames = count, firstSampleFrame = report.sampleFrames,
                kind=tail?"tail_drain":"video_frame",videoEndTime=videoEnd,
                cumulativeErrorSamples=report.sampleFrames+count-videoEnd*report.sampleRate,
                semanticStart = origin + (tail?blocks.Count:frame) / (double)report.videoFps, dspBefore = AudioSettings.dspTime };
            using (var buffer = new NativeArray<float>(count * report.channels, Allocator.Temp))
            {
                block.nativeRenderSucceeded=AudioRenderer.Render(buffer);
                if (!block.nativeRenderSucceeded) throw new InvalidOperationException("Unity AudioRenderer.Render failed; no replacement mix is allowed.");
                block.dspAfter = AudioSettings.dspTime;
                for (int i = 0; i < buffer.Length; i++)
                {
                    float sample = buffer[i];
                    if (float.IsNaN(sample) || float.IsInfinity(sample))
                    { report.nonfiniteSamples++; throw new InvalidDataException("Native audio output contains nonfinite PCM."); }
                    float magnitude = Mathf.Abs(sample);
                    block.peak = Mathf.Max(block.peak, magnitude);
                    if (magnitude >= 1) report.samplesOverFullScale++;
                    sumSquares += sample * (double)sample;
                    writer.Write(sample);
                }
            }
            report.sampleFrames += count; report.peak = Math.Max(report.peak, block.peak);
            return block;
        }

        private void ValidateNativeClock(ArenaAudioCaptureBlock block)
        {
            double startError=(block.dspBefore-report.timelineOriginDsp)*report.sampleRate-block.firstSampleFrame;
            double advanceError=(block.dspAfter-block.dspBefore)*report.sampleRate-block.sampleFrames;
            if(Math.Abs(startError)>1 || Math.Abs(advanceError)>1)
                throw new InvalidOperationException("Native PCM continuity disagrees with DSP sample clock: start="+
                    startError+" advance="+advanceError+" samples.");
        }

        private void UpdateReport()
        {
            report.frames=blocks.Count; report.blocks=blocks.ToArray(); report.tailBlocks=tailBlocks.ToArray();
            report.duration=report.sampleFrames/(double)Math.Max(1,report.sampleRate);
            report.videoDuration=report.frames/(double)Math.Max(1,report.videoFps);
            report.finalDurationDifference=report.duration-report.videoDuration;
            report.rms=Math.Sqrt(sumSquares/Math.Max(1,report.sampleFrames*report.channels));
            report.events=CopyEvents(); report.diagnostics=diagnostics.ToArray();
        }

        public ArenaAudioCaptureReport Finish(string reportPath)
        {
            if (closed) throw new InvalidOperationException("Capture already closed.");
            UpdateReport();
            report.passed = report.frames > 0 && CoversVideoEnd &&
                report.finalDurationDifference >= -1d/report.sampleRate &&
                report.finalDurationDifference <= (report.dspBufferLength+1d)/report.sampleRate &&
                report.maxAbsCumulativeErrorSamples <= report.dspBufferLength+1 &&
                writer != null && writer.BaseStream.Length == 44+report.sampleFrames*report.channels*4 &&
                report.peak > .00001 && report.nonfiniteSamples == 0 && report.samplesOverFullScale == 0 &&
                audio.MissingClipCount == 0 && audio.CapacityDroppedCount == 0;
            if (!report.passed) report.error = "Native capture did not satisfy DSP-quantum alignment, continuous WAV bytes, audible PCM, finite headroom or cue delivery checks.";
            CloseWriter();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllText(reportPath, JsonUtility.ToJson(report,true));
            Dispose(); return report;
        }

        public void Fail(string reportPath, Exception error)
        {
            report.passed = false; report.error = error.ToString();
            UpdateReport();
            CloseWriter();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));
            Dispose();
        }

        private ArenaSoundEvent[] CopyEvents()
        {
            var copy = new ArenaSoundEvent[audio.Events.Count];
            for(int i=0;i<copy.Length;i++)copy[i]=audio.Events[i];
            return copy;
        }

        private ArenaAudioCaptureDiagnostic Trace(string stage,int samples)
        {
            var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            int active = 0; foreach(var listener in listeners) if(listener.isActiveAndEnabled) active++;
            string disabled = "unavailable";
            try
            {
                // Read-only diagnostic of the engine flag; never turn a disabled
                // engine back on through reflection or hide configuration failures.
                var property = typeof(AudioSettings).GetProperty("unityAudioDisabled",BindingFlags.NonPublic|BindingFlags.Static);
                if(property != null) disabled = Convert.ToString(property.GetValue(null));
            }
            catch(Exception exception) { disabled = "unavailable: " + exception.GetType().Name; }
            var diagnostic = new ArenaAudioCaptureDiagnostic { stage=stage,samples=samples,
                unityFrame=Time.frameCount,deltaTime=Time.deltaTime,captureDeltaTime=Time.captureDeltaTime,
                timeScale=Time.timeScale,dspTime=AudioSettings.dspTime,realtime=Time.realtimeSinceStartupAsDouble,
                batchMode=Application.isBatchMode,focused=Application.isFocused,listenerPaused=AudioListener.pause,
                listenerVolume=AudioListener.volume,totalListeners=listeners.Length,activeListeners=active,
                backendEnabled=audio.isActiveAndEnabled,audioDisabled=disabled };
            diagnostics.Add(diagnostic); return diagnostic;
        }

        private void InstallLoopHook()
        {
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var pass = new PlayerLoopSystem { type=typeof(ArenaNativeAudioRenderPass),updateDelegate=RunQueuedOperation };
            if(!InsertAfterAudio(ref loop,pass)) throw new InvalidOperationException("Current Unity PlayerLoop has no PostLateUpdate.UpdateAudio phase.");
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
            ownsLoopHook = true;
        }

        private static bool InsertAfterAudio(ref PlayerLoopSystem loop,PlayerLoopSystem pass)
        {
            var children = loop.subSystemList;
            if(children == null) return false;
            for(int i=0;i<children.Length;i++)
            {
                if(children[i].type == typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAudio))
                {
                    var expanded = new PlayerLoopSystem[children.Length+1];
                    Array.Copy(children,0,expanded,0,i+1); expanded[i+1]=pass;
                    Array.Copy(children,i+1,expanded,i+2,children.Length-i-1);
                    loop.subSystemList=expanded; return true;
                }
                var child=children[i];
                if(InsertAfterAudio(ref child,pass)) { children[i]=child; return true; }
            }
            return false;
        }

        private static void RemoveOwnLoopHook(ref PlayerLoopSystem loop)
        {
            if(loop.subSystemList == null) return;
            var retained = new List<PlayerLoopSystem>();
            foreach(var item in loop.subSystemList)
            {
                if(item.type == typeof(ArenaNativeAudioRenderPass)) continue;
                var child=item; RemoveOwnLoopHook(ref child); retained.Add(child);
            }
            loop.subSystemList=retained.ToArray();
        }

        private void WriteHeader(uint dataBytes)
        {
            writer.Seek(0,SeekOrigin.Begin);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(dataBytes+36u);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16u);
            writer.Write((ushort)3); // IEEE float, preserving Unity's actual output and overloads.
            writer.Write((ushort)report.channels); writer.Write(report.sampleRate);
            writer.Write(report.sampleRate*report.channels*4); writer.Write((ushort)(report.channels*4)); writer.Write((ushort)32);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataBytes);
        }

        private void CloseWriter()
        {
            if(writer==null)return;
            long bytes=writer.BaseStream.Length-44;
            if(bytes>uint.MaxValue-36)throw new InvalidDataException("WAV exceeds RIFF size; split recording.");
            WriteHeader((uint)Math.Max(0,bytes)); writer.Dispose(); writer=null;
        }

        public void Dispose()
        {
            if(closed)return;
            try { CloseWriter(); }
            finally
            {
                queuedOperation = null;
                try
                {
                    if(ownsLoopHook)
                    {
                        var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
                        RemoveOwnLoopHook(ref loop); UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop); ownsLoopHook=false;
                    }
                }
                finally
                {
                    try { if(ownsRenderer) { ownsRenderer=false; AudioRenderer.Stop(); } }
                    finally
                    {
                        if(timelineArmed) { audio.EndCaptureClock(); timelineArmed=false; }
                        if(ownsCaptureRate) { Time.captureFramerate=originalCaptureRate; ownsCaptureRate=false; }
                        closed=true;
                    }
                }
            }
        }
    }
}
