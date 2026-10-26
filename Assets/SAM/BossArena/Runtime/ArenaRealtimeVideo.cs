using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction.BossArena
{
    // Opt-in game framebuffer recorder. Never drives or replays the simulation.
    // Unity references (checked against Unity 6 official API documentation):
    // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ScreenCapture.CaptureScreenshotIntoRenderTexture.html
    // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.Request.html
    // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html
    // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings-dspTime.html
    public sealed class ArenaRealtimeVideo : MonoBehaviour
    {
        private const int Fps = 30, TextureCount = 4, BufferCount = 12;
        [Serializable] private sealed class Manifest
        {
            public int schema = 1, width, height, containerFps = Fps, initialCaptureFramerate, finalCaptureFramerate;
            public string kind = "real_time_game_framebuffer_video_not_a_performance_sample";
            public string status, startedUtc, finishedUtc, video, ffmpeg, ffmpegArguments, build, unity, graphics;
            public string timing = "Normal simulation. Stopwatch monotonic timestamps at completed game frames. Nearest captured frame is selected for each 30fps container timestamp; missing frames are held, never re-simulated. video_frames.csv records every encoded frame and source timestamp.";
            public string audioPolicy = "Unmodified interleaved float32 PCM from OnAudioFilterRead on the active AudioListener; not synthesized. Audio callback DSP and monotonic timestamps are retained in audio_blocks.csv. DSP-to-wall-clock anchor has audio-buffer uncertainty; hardware output latency is not measured. No automatic claim of sample-exact AV synchronization.";
            public string frameOrientation = "Native readback order; smoke-test game UI orientation. --sam-video-flip-y explicitly applies FFmpeg vflip and is recorded.";
            public string limitation = "Capture, readback, audio copies and encoding add overhead. This recording is not valid performance acceptance evidence. Missing audio callbacks or drops are reported, not silently filled.";
            public bool complete, normalClockThroughout = true, flipY, hadFocusLoss, audioAvailable, audioCaptureComplete;
            public float initialTimeScale, finalTimeScale;
            public double epochDspSeconds, epochUnityRealtimeSeconds, wallDurationSeconds, encodedDurationSeconds;
            public double firstVideoSampleSeconds = -1, lastVideoSampleSeconds = -1;
            public double firstAudioDspSeconds = -1, firstAudioWallSeconds = -1, lastAudioDspSeconds = -1;
            public double audioDurationSeconds, audioStartOffsetFromVideoSeconds, audioMaxDspDiscontinuitySeconds;
            public int requestedSamples, acceptedSamples, readbackFailures, droppedCaptureSamples, encoderQueueDrops;
            public int encodedFrames, duplicatedFrames, resampleDiscardedSources, ffmpegExitCode = -1;
            public int audioSampleRate, audioChannels, audioBufferFrames, audioBufferCount, audioBlocks, audioDroppedBlocks;
            public long audioSampleFrames;
            public long audioNonzeroSamples, audioClippedSamples;
            public double audioPeakAmplitude, audioRmsAmplitude;
            public string[] errors;
        }
        private sealed class Sample
        {
            public int id, unityFrame;
            public double wall, dsp;
            public byte[] bytes;
            public RenderTexture texture;
            public volatile bool done;
            public string status;
        }
        private sealed class AudioBlock
        {
            public byte[] bytes;
            public int byteCount, channels, sampleFrames;
            public double wall, dsp;
        }

        private readonly ConcurrentQueue<string> errors = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<byte[]> freeBuffers = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> freeAudioBuffers = new ConcurrentQueue<byte[]>();
        private readonly BlockingCollection<Sample> videoQueue = new BlockingCollection<Sample>(8);
        private readonly BlockingCollection<AudioBlock> audioQueue = new BlockingCollection<AudioBlock>(32);
        private readonly Queue<RenderTexture> freeTextures = new Queue<RenderTexture>();
        private readonly Queue<Sample> pending = new Queue<Sample>();
        private readonly List<Sample> samples = new List<Sample>();
        private readonly object audioGate = new object(), stderrGate = new object();
        private Manifest report;
        private Process encoder;
        private Thread videoThread, audioThread;
        private ArenaRealtimeAudioTap audioTap;
        private StreamWriter stderr;
        private string output, directory;
        private long epochTicks;
        private double nextCapture, duration, stopWall;
        private int sourceId, currentAudioChannels;
        private volatile bool capturing, videoWorkerFailed, audioWorkerFailed;
        private bool initialized, stopped;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--sam-record-video") < 0) return;
            if (Array.IndexOf(args, "--sam-benchmark") >= 0)
            {
                UnityEngine.Debug.LogError("--sam-record-video and --sam-benchmark are mutually exclusive.");
                Application.Quit(2);
                return;
            }
            new GameObject("SAM actual framebuffer recorder").AddComponent<ArenaRealtimeVideo>();
        }
        private static string Arg(string[] args, string key, string fallback = null)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[index + 1] : fallback;
        }
        private static string N(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private double Wall { get { return (Stopwatch.GetTimestamp() - epochTicks) / (double)Stopwatch.Frequency; } }
        private static string Quote(string value)
        {
            // Windows command-line argument quoting, not shell escaping. UseShellExecute=false.
            var text = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { ++slashes; continue; }
                if (c == '"') { text.Append('\\', slashes * 2 + 1); text.Append(c); slashes = 0; continue; }
                text.Append('\\', slashes); text.Append(c); slashes = 0;
            }
            text.Append('\\', slashes * 2); text.Append('"'); return text.ToString();
        }

        private IEnumerator Start()
        {
            // Allow the arena's normal Start to choose resolution and display settings.
            yield return null;
            yield return new WaitForEndOfFrame();
            try { Initialize(); }
            catch (Exception exception)
            {
                errors.Enqueue("Startup: " + exception);
                UnityEngine.Debug.LogError("SAM video recorder failed: " + exception.Message);
                StopCapture("startup_failed");
                yield break;
            }
            var endOfFrame = new WaitForEndOfFrame();
            while (capturing)
            {
                DrainCompleted();
                double now = Wall;
                if (videoWorkerFailed) { StopCapture("encoder_failed"); yield break; }
                if (Time.captureFramerate != 0 || Math.Abs(Time.timeScale - 1) > .00001f)
                {
                    report.normalClockThroughout = false;
                    errors.Enqueue("A component enabled a capture clock or changed timeScale while recording.");
                    StopCapture("invalid_simulation_clock"); yield break;
                }
                if (Screen.width != report.width || Screen.height != report.height)
                { errors.Enqueue("Game resolution changed while recording."); StopCapture("resolution_changed"); yield break; }
                if (AudioSettings.outputSampleRate != report.audioSampleRate)
                { errors.Enqueue("Audio output sample rate changed while recording."); StopCapture("audio_format_changed"); yield break; }
                if (!Application.isFocused) report.hadFocusLoss = true;
                if (duration > 0 && now >= duration) { StopCapture("duration_reached"); yield break; }
                if (now >= nextCapture)
                {
                    int skipped = Math.Max(0, (int)Math.Floor((now - nextCapture) * Fps));
                    report.droppedCaptureSamples += skipped;
                    nextCapture += (skipped + 1) / (double)Fps;
                    Capture(now);
                }
                yield return endOfFrame;
            }
        }

        private void Initialize()
        {
            var args = Environment.GetCommandLineArgs();
            string requested = Arg(args, "--sam-record-video"), ffmpeg = Arg(args, "--sam-ffmpeg");
            if (string.IsNullOrWhiteSpace(requested) || !Path.IsPathRooted(requested))
                throw new ArgumentException("--sam-record-video needs an absolute new .mp4 path.");
            if (string.IsNullOrWhiteSpace(ffmpeg) || !Path.IsPathRooted(ffmpeg) || !File.Exists(ffmpeg))
                throw new ArgumentException("--sam-ffmpeg needs the absolute path of an existing FFmpeg executable.");
            output = Path.GetFullPath(requested);
            if (!output.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Video must use .mp4 extension.");
            directory = output + ".evidence";
            if (File.Exists(output) || Directory.Exists(directory)) throw new IOException("Recording output already exists; choose a new path.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)); Directory.CreateDirectory(directory);
            report = new Manifest {
                status = "starting", video = output, ffmpeg = Path.GetFullPath(ffmpeg),
                unity = Application.unityVersion, build = Application.buildGUID, graphics = SystemInfo.graphicsDeviceVersion,
                width = Screen.width, height = Screen.height, initialCaptureFramerate = Time.captureFramerate,
                initialTimeScale = Time.timeScale, startedUtc = DateTime.UtcNow.ToString("o"),
                flipY = Array.IndexOf(args, "--sam-video-flip-y") >= 0,
                audioSampleRate = AudioSettings.outputSampleRate
            };
            if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("AsyncGPUReadback is unavailable on this device.");
            if (Time.captureFramerate != 0 || Math.Abs(Time.timeScale - 1) > .00001f)
                throw new InvalidOperationException("Recorder requires the existing normal clock: captureFramerate=0 and timeScale=1.");
            if (report.width < 2 || report.height < 2 || report.width % 2 != 0 || report.height % 2 != 0)
                throw new InvalidOperationException("H.264 requires even game framebuffer dimensions.");
            string durationText = Arg(args, "--sam-video-duration", "0");
            if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out duration)
                || double.IsNaN(duration) || double.IsInfinity(duration) || duration < 0)
                throw new ArgumentException("--sam-video-duration must be non-negative wall seconds; 0 records until quit.");
            int bytes = checked(report.width * report.height * 4);
            for (int i = 0; i < BufferCount; ++i) freeBuffers.Enqueue(new byte[bytes]);
            for (int i = 0; i < TextureCount; ++i)
            {
                var rt = new RenderTexture(report.width, report.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.name = "SAM recording framebuffer " + i; rt.Create(); freeTextures.Enqueue(rt);
            }
            AudioSettings.GetDSPBufferSize(out report.audioBufferFrames, out report.audioBufferCount);
            int audioBytes = Math.Max(32768, report.audioBufferFrames * 8 * sizeof(float));
            for (int i = 0; i < 34; ++i) freeAudioBuffers.Enqueue(new byte[audioBytes]);
            stderr = new StreamWriter(Path.Combine(directory, "ffmpeg.log"), false, new UTF8Encoding(false));
            report.ffmpegArguments = "-hide_banner -nostats -loglevel warning -n -f rawvideo -pixel_format rgba -video_size "
                + report.width + "x" + report.height + " -framerate 30 -i pipe:0 -an "
                + (report.flipY ? "-vf vflip " : "")
                + "-c:v libx264 -preset ultrafast -crf 19 -threads 2 -pix_fmt yuv420p -movflags +faststart " + Quote(output);
            encoder = new Process { StartInfo = new ProcessStartInfo {
                FileName = report.ffmpeg, Arguments = report.ffmpegArguments, UseShellExecute = false,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardError = true
            }};
            encoder.ErrorDataReceived += (sender, e) => { if (e.Data != null) lock (stderrGate) { stderr?.WriteLine(e.Data); stderr?.Flush(); } };
            if (!encoder.Start()) throw new IOException("FFmpeg did not start.");
            encoder.BeginErrorReadLine();
            epochTicks = Stopwatch.GetTimestamp();
            report.epochDspSeconds = AudioSettings.dspTime;
            report.epochUnityRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            videoThread = new Thread(EncodeVideo) { IsBackground = true, Name = "SAM video encoder pipe" };
            audioThread = new Thread(WriteAudio) { IsBackground = true, Name = "SAM listener PCM writer" };
            initialized = true; capturing = true; videoThread.Start(); audioThread.Start();
            AudioListener listener = null;
            foreach (var item in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (item.enabled && item.gameObject.activeInHierarchy) { listener = item; break; }
            if (listener != null)
            { audioTap = listener.gameObject.AddComponent<ArenaRealtimeAudioTap>(); audioTap.Owner = this; }
            else errors.Enqueue("No active AudioListener; video can be captured but live PCM is unavailable.");
            report.status = "recording"; SaveManifest();
            UnityEngine.Debug.Log("SAM real-time framebuffer video: " + output);
        }

        private void Capture(double now)
        {
            long diagnosticStarted = ArenaReviewFrameDiagnostics.Enabled ? Stopwatch.GetTimestamp() : 0;
            try
            {
            ++report.requestedSamples;
            var sample = new Sample { id = sourceId++, wall = now, dsp = AudioSettings.dspTime, unityFrame = Time.frameCount };
            samples.Add(sample);
            if (freeTextures.Count == 0 || !freeBuffers.TryDequeue(out sample.bytes))
            { sample.status = "capture_pool_exhausted"; ++report.droppedCaptureSamples; return; }
            sample.texture = freeTextures.Dequeue(); pending.Enqueue(sample);
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(sample.texture);
                AsyncGPUReadback.Request(sample.texture, 0, TextureFormat.RGBA32, request => {
                    try
                    {
                        if (request.hasError) { sample.status = "gpu_readback_error"; ++report.readbackFailures; }
                        else
                        {
                            long copyStarted = ArenaReviewFrameDiagnostics.Enabled ? Stopwatch.GetTimestamp() : 0;
                            try { request.GetData<byte>().CopyTo(sample.bytes); sample.status = "readback_ready"; }
                            finally
                            {
                                if (copyStarted != 0) ArenaReviewFrameDiagnostics.RecordCost("recorder_copy",
                                    (Stopwatch.GetTimestamp() - copyStarted) * 1000.0 / Stopwatch.Frequency);
                            }
                        }
                    }
                    catch (Exception exception) { sample.status = "readback_exception"; ++report.readbackFailures; errors.Enqueue(exception.Message); }
                    finally { sample.done = true; }
                });
            }
            catch (Exception exception) { sample.status = "capture_exception"; sample.done = true; ++report.readbackFailures; errors.Enqueue(exception.Message); }
            }
            finally
            {
                if (diagnosticStarted != 0) ArenaReviewFrameDiagnostics.RecordCost("recorder_capture",
                    (Stopwatch.GetTimestamp() - diagnosticStarted) * 1000.0 / Stopwatch.Frequency);
            }
        }
        private void DrainCompleted()
        {
            long diagnosticStarted = ArenaReviewFrameDiagnostics.Enabled ? Stopwatch.GetTimestamp() : 0;
            try
            {
            // Preserve capture order even if GPU requests complete out of order.
            while (pending.Count > 0 && pending.Peek().done)
            {
                var sample = pending.Dequeue(); freeTextures.Enqueue(sample.texture); sample.texture = null;
                if (sample.status == "readback_ready" && !videoWorkerFailed && videoQueue.TryAdd(sample))
                {
                    sample.status = "queued"; ++report.acceptedSamples;
                    if (report.firstVideoSampleSeconds < 0) report.firstVideoSampleSeconds = sample.wall;
                    report.lastVideoSampleSeconds = sample.wall;
                }
                else
                {
                    if (sample.status == "readback_ready") { sample.status = "encoder_queue_full"; ++report.encoderQueueDrops; }
                    freeBuffers.Enqueue(sample.bytes); sample.bytes = null;
                }
            }
            }
            finally
            {
                if (diagnosticStarted != 0) ArenaReviewFrameDiagnostics.RecordCost("recorder_drain",
                    (Stopwatch.GetTimestamp() - diagnosticStarted) * 1000.0 / Stopwatch.Frequency);
            }
        }

        private void EncodeVideo()
        {
            Sample previous = null; int index = 0, lastSource = -1, previousUses = 0;
            try
            {
                using (var csv = new StreamWriter(Path.Combine(directory, "video_frames.csv"), false, new UTF8Encoding(false)))
                {
                    csv.WriteLine("encoded_index,presentation_wall_s,source_id,source_capture_wall_s,source_dsp_s,unity_frame,repeated_source,timestamp_error_s");
                    Action<Sample> write = sample => {
                        encoder.StandardInput.BaseStream.Write(sample.bytes, 0, sample.bytes.Length);
                        bool repeated = lastSource == sample.id;
                        csv.WriteLine(index + "," + N(index / (double)Fps) + "," + sample.id + "," + N(sample.wall) + "," + N(sample.dsp) + "," + sample.unityFrame + "," + (repeated ? "1" : "0") + "," + N(index / (double)Fps - sample.wall));
                        if (repeated) ++report.duplicatedFrames;
                        lastSource = sample.id; ++index; ++previousUses;
                    };
                    foreach (var current in videoQueue.GetConsumingEnumerable())
                    {
                        if (previous != null)
                        {
                            double midpoint = (previous.wall + current.wall) * .5;
                            while (index / (double)Fps < midpoint) write(previous);
                            if (previousUses == 0) ++report.resampleDiscardedSources;
                            freeBuffers.Enqueue(previous.bytes); previous.bytes = null;
                        }
                        previous = current; previousUses = 0;
                    }
                    if (previous != null)
                    {
                        while (index / (double)Fps < stopWall) write(previous);
                        if (previousUses == 0) ++report.resampleDiscardedSources;
                        freeBuffers.Enqueue(previous.bytes); previous.bytes = null;
                    }
                }
                report.encodedFrames = index; report.encodedDurationSeconds = index / (double)Fps;
                encoder.StandardInput.Close();
                if (!encoder.WaitForExit(20000)) throw new TimeoutException("FFmpeg did not finish after the pipe closed.");
                report.ffmpegExitCode = encoder.ExitCode;
                if (encoder.ExitCode != 0) throw new IOException("FFmpeg exited " + encoder.ExitCode + "; see ffmpeg.log.");
            }
            catch (Exception exception) { errors.Enqueue("Encoder: " + exception); videoWorkerFailed = true; }
        }

        // Called only by the audio-thread tap. No gameplay, Transform, Time or logging API here.
        internal void ReceiveAudio(float[] data, int channels, double dsp)
        {
            lock (audioGate)
            {
                if (!capturing) return;
                if (currentAudioChannels == 0) currentAudioChannels = channels;
                if (channels <= 0 || channels != currentAudioChannels || data.Length % channels != 0 || !BitConverter.IsLittleEndian)
                { ++report.audioDroppedBlocks; return; }
                if (!freeAudioBuffers.TryDequeue(out var bytes)) { ++report.audioDroppedBlocks; return; }
                int length = data.Length * sizeof(float);
                if (length > bytes.Length) { freeAudioBuffers.Enqueue(bytes); ++report.audioDroppedBlocks; return; }
                Buffer.BlockCopy(data, 0, bytes, 0, length);
                if (!audioQueue.TryAdd(new AudioBlock { bytes = bytes, byteCount = length, channels = channels,
                    sampleFrames = data.Length / channels, wall = Wall, dsp = dsp }))
                { freeAudioBuffers.Enqueue(bytes); ++report.audioDroppedBlocks; }
            }
        }
        private void WriteAudio()
        {
            double squares = 0; long values = 0;
            try
            {
                using (var pcm = new FileStream(Path.Combine(directory, "listener.f32le.pcm"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var csv = new StreamWriter(Path.Combine(directory, "audio_blocks.csv"), false, new UTF8Encoding(false)))
                {
                    csv.WriteLine("block_index,callback_wall_s,callback_dsp_s,pcm_start_sample_frame,sample_frames,channels,dsp_discontinuity_s");
                    double previousDsp = 0; int previousSamples = 0;
                    foreach (var block in audioQueue.GetConsumingEnumerable())
                    {
                        if (report.audioBlocks == 0)
                        {
                            report.firstAudioDspSeconds = block.dsp; report.firstAudioWallSeconds = block.wall;
                            report.audioChannels = block.channels; report.audioAvailable = true;
                        }
                        double gap = report.audioBlocks > 0 ? block.dsp - previousDsp - previousSamples / (double)report.audioSampleRate : 0;
                        report.audioMaxDspDiscontinuitySeconds = Math.Max(report.audioMaxDspDiscontinuitySeconds, Math.Abs(gap));
                        csv.WriteLine(report.audioBlocks + "," + N(block.wall) + "," + N(block.dsp) + "," + report.audioSampleFrames + "," + block.sampleFrames + "," + block.channels + "," + N(gap));
                        pcm.Write(block.bytes, 0, block.byteCount);
                        for (int offset = 0; offset < block.byteCount; offset += sizeof(float))
                        {
                            double value = BitConverter.ToSingle(block.bytes, offset);
                            double amplitude = Math.Abs(value);
                            squares += value * value; ++values;
                            report.audioPeakAmplitude = Math.Max(report.audioPeakAmplitude, amplitude);
                            if (amplitude > .000001) ++report.audioNonzeroSamples;
                            if (amplitude >= 1) ++report.audioClippedSamples;
                        }
                        ++report.audioBlocks; report.audioSampleFrames += block.sampleFrames;
                        report.lastAudioDspSeconds = block.dsp; previousDsp = block.dsp; previousSamples = block.sampleFrames;
                        freeAudioBuffers.Enqueue(block.bytes);
                    }
                }
                report.audioRmsAmplitude = values > 0 ? Math.Sqrt(squares / values) : 0;
            }
            catch (Exception exception) { errors.Enqueue("Audio writer: " + exception); audioWorkerFailed = true; }
        }

        private void StopCapture(string reason)
        {
            if (stopped) return; stopped = true;
            if (report == null) return;
            stopWall = initialized ? Wall : 0;
            lock (audioGate) { capturing = false; audioQueue.CompleteAdding(); }
            if (audioTap != null) { audioTap.Owner = null; Destroy(audioTap); }
            if (initialized)
            {
                // Shutdown only. The measured gameplay interval has already ended.
                AsyncGPUReadback.WaitAllRequests(); DrainCompleted();
            }
            videoQueue.CompleteAdding();
            bool videoFinished = videoThread == null || videoThread.Join(25000);
            if (!videoFinished)
            {
                errors.Enqueue("Encoder drain timed out; recording is incomplete.");
                try { if (encoder != null && !encoder.HasExited) encoder.Kill(); } catch (Exception ex) { errors.Enqueue(ex.Message); }
                videoThread.Join(3000);
            }
            bool audioFinished = audioThread == null || audioThread.Join(5000);
            if (!audioFinished) errors.Enqueue("Audio writer drain timed out; PCM may be incomplete.");
            try
            {
                if (encoder != null && !encoder.HasExited)
                {
                    errors.Enqueue("Stopping the recorder-owned FFmpeg process after incomplete drain.");
                    encoder.Kill(); encoder.WaitForExit(3000);
                }
            }
            catch (InvalidOperationException) { /* Process never started. */ }
            catch (Exception exception) { errors.Enqueue("Encoder cleanup: " + exception.Message); }
            foreach (var texture in freeTextures) { texture.Release(); Destroy(texture); }
            freeTextures.Clear();
            lock (stderrGate) { stderr?.Flush(); stderr?.Dispose(); stderr = null; }
            report.wallDurationSeconds = stopWall; report.finishedUtc = DateTime.UtcNow.ToString("o");
            report.finalCaptureFramerate = Time.captureFramerate; report.finalTimeScale = Time.timeScale;
            report.audioDurationSeconds = report.audioSampleFrames / (double)Math.Max(1, report.audioSampleRate);
            report.audioStartOffsetFromVideoSeconds = report.firstAudioDspSeconds < 0 ? 0 : report.firstAudioDspSeconds - report.epochDspSeconds;
            report.audioCaptureComplete = report.audioAvailable && audioFinished && !audioWorkerFailed && report.audioDroppedBlocks == 0;
            report.status = reason;
            report.complete = initialized && videoFinished && audioFinished && !videoWorkerFailed && report.ffmpegExitCode == 0
                && report.encodedFrames > 0 && report.normalClockThroughout && (reason == "duration_reached" || reason == "application_quit" || reason == "component_destroyed");
            using (var csv = new StreamWriter(Path.Combine(directory, "capture_samples.csv"), false, new UTF8Encoding(false)))
            {
                csv.WriteLine("source_id,capture_wall_s,capture_dsp_s,unity_frame,status");
                foreach (var sample in samples) csv.WriteLine(sample.id + "," + N(sample.wall) + "," + N(sample.dsp) + "," + sample.unityFrame + "," + sample.status);
            }
            SaveManifest();
            UnityEngine.Debug.Log("SAM video recorder stopped: " + reason + " complete=" + report.complete + " " + output);
        }
        private void SaveManifest()
        {
            if (report == null || directory == null) return;
            report.errors = errors.ToArray();
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonUtility.ToJson(report, true));
        }
        private void OnApplicationQuit() { StopCapture("application_quit"); }
        private void OnDestroy() { StopCapture("component_destroyed"); }
    }

    // Installed exclusively on the already active listener by the opt-in recorder.
    // The data are copied unchanged; callback does not create, mute or modify audio.
    public sealed class ArenaRealtimeAudioTap : MonoBehaviour
    {
        internal ArenaRealtimeVideo Owner;
        private void OnAudioFilterRead(float[] data, int channels)
        {
            var owner = Owner;
            if (!ReferenceEquals(owner, null)) owner.ReceiveAudio(data, channels, AudioSettings.dspTime);
        }
    }
}
