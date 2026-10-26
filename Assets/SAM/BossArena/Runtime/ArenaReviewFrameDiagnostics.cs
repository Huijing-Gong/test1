using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Unity.Profiling;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Opt-in clocks for review troubleshooting. Fixed storage; no per-frame formatting or disk writes.
    // https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html
    public sealed class ArenaReviewFrameDiagnostics : MonoBehaviour
    {
        private const int Capacity = 16384;
        [Serializable] private struct MetricValue
        {
            public bool valid;
            public long raw;
            public int collectedSamples;
        }
        [Serializable] private sealed class MetricDescription
        {
            public string name, category, unit = "unavailable", dataType = "unavailable", error = "";
            public bool valid;
        }
        [Serializable] private struct Frame
        {
            public int frameCount;
            public bool updateObserved, endOfFrameObserved, focused;
            public long updateStopwatchTicks, endOfFrameStopwatchTicks;
            public double updateUnityRealtimeSeconds, endOfFrameUnityRealtimeSeconds;
            public int gcCollectionCount0, gcCollectionCount1, gcCollectionCount2;
            public MetricValue mainThread, renderThread, gcAlloc, gcAllocatedInFrame;
            public double foundation_update, foundation_begin_clip, foundation_sole;
            public double recorder_capture, recorder_copy, recorder_drain;
            public int foundation_update_calls, foundation_begin_clip_calls, foundation_sole_calls;
            public int recorder_capture_calls, recorder_copy_calls, recorder_drain_calls;
        }
        [Serializable] private sealed class Report
        {
            public int schema = 1;
            public string kind = "actual_review_frame_clocks_not_performance_acceptance";
            public string unity, build, startedUtc, finishedUtc, status;
            public string costUnit = "milliseconds";
            public string costRelationship = "foundation_update contains foundation_begin_clip and foundation_sole when called inside Update. recorder_capture contains the screenshot/readback request; recorder_copy measures only the completed readback data copy. Costs are nested or overlapping and must not be summed as total frame time.";
            public string profilerPhase = "ProfilerRecorder LastValue is sampled at this component's Update. It describes the most recently collected profiler sample, commonly the previous completed frame; its exact frame id is unavailable. Raw units and validity are explicit. Unavailable or not-yet-collected values are -1, never zero evidence.";
            public string boundary = "Normal real-time simulation plus optional diagnostic/recording overhead. Update and end-of-frame timestamps do not independently separate GPU waits from OS scheduling. GC collection counters show completed collection counts, not pause durations. Off-main-thread cost calls, if any, use the last observed main-thread frame as a disclosed approximate attribution. Shutdown serialization/drain is outside measured gameplay; other components may finish shutdown after this report is saved. Not visual, gameplay, audio or 60fps performance acceptance.";
            public long stopwatchFrequency, startupStopwatchTicks;
            public double startupUnityRealtimeSeconds;
            public int capacity = Capacity, frameSlots, overflowCalls, unknownCostCalls, offMainThreadCostCalls;
            public bool storageComplete;
            public MetricDescription[] metrics;
            public Frame[] frames;
        }
        private sealed class Metric : IDisposable
        {
            public readonly MetricDescription description;
            private ProfilerRecorder recorder;
            public Metric(ProfilerCategory category, string categoryName, string name)
            {
                description = new MetricDescription { name = name, category = categoryName };
                try
                {
                    recorder = ProfilerRecorder.StartNew(category, name, 1);
                    description.valid = recorder.Valid;
                    if (recorder.Valid)
                    {
                        description.unit = recorder.UnitType.ToString();
                        description.dataType = recorder.DataType.ToString();
                    }
                    else description.error = "Metric unavailable in this player.";
                }
                catch (Exception exception) { description.error = exception.GetType().Name + ": " + exception.Message; }
            }
            public MetricValue Read()
            {
                bool valid = recorder.Valid && recorder.IsRunning;
                int count = valid ? recorder.Count : 0;
                return new MetricValue { valid = valid && count > 0, raw = valid && count > 0 ? recorder.LastValue : -1,
                                         collectedSamples = count };
            }
            public void Dispose() { recorder.Dispose(); }
        }

        private static ArenaReviewFrameDiagnostics active;
        private readonly object gate = new object();
        private Frame[] frames;
        private Metric mainThread, renderThread, gcAlloc, gcAllocatedInFrame;
        private Report report;
        private string output;
        private int firstFrame, observedFrame, frameSlots, mainThreadId;
        private int overflowCalls, unknownCostCalls, offMainThreadCostCalls;
        private volatile bool recording;
        private bool saved, disposed;

        public static bool Enabled
        {
            get { var value = active; return !ReferenceEquals(value, null) && value.recording; }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--sam-review-frame-diagnostics") < 0) return;
            if (Array.IndexOf(args, "--sam-benchmark") >= 0)
            {
                UnityEngine.Debug.LogError("Review frame diagnostics refused: --sam-benchmark is incompatible.");
                return;
            }
            if (Array.IndexOf(args, "--sam-foundation-review") < 0)
            {
                UnityEngine.Debug.LogError("Review frame diagnostics require --sam-foundation-review.");
                return;
            }
            int index = Array.IndexOf(args, "--sam-output");
            if (index < 0 || index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]))
            {
                UnityEngine.Debug.LogError("Review frame diagnostics need an absolute --sam-output directory.");
                return;
            }
            string path = Path.Combine(Path.GetFullPath(args[index + 1]), "frame_diagnostics.json");
            if (File.Exists(path))
            {
                UnityEngine.Debug.LogError("Review frame diagnostics refuse to overwrite existing evidence: " + path);
                return;
            }
            var owner = new GameObject("SAM optional review frame clocks").AddComponent<ArenaReviewFrameDiagnostics>();
            owner.Initialize(path);
        }
        private void Initialize(string path)
        {
            output = path;
            frames = new Frame[Capacity];
            firstFrame = observedFrame = Time.frameCount;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            mainThread = new Metric(ProfilerCategory.Internal, "Internal", "Main Thread");
            renderThread = new Metric(ProfilerCategory.Internal, "Internal", "Render Thread");
            gcAlloc = new Metric(ProfilerCategory.Memory, "Memory", "GC.Alloc");
            gcAllocatedInFrame = new Metric(ProfilerCategory.Memory, "Memory", "GC Allocated In Frame");
            report = new Report { unity = Application.unityVersion, build = Application.version,
                startedUtc = DateTime.UtcNow.ToString("o"), stopwatchFrequency = Stopwatch.Frequency,
                startupStopwatchTicks = Stopwatch.GetTimestamp(), startupUnityRealtimeSeconds = Time.realtimeSinceStartupAsDouble,
                metrics = new[] { mainThread.description, renderThread.description, gcAlloc.description, gcAllocatedInFrame.description } };
            active = this; recording = true;
            StartCoroutine(EndOfFrameClocks());
        }
        private int Slot(int frame)
        {
            int index = frame-firstFrame;
            if (index < 0 || index >= Capacity) { ++overflowCalls; return -1; }
            while (frameSlots <= index)
            {
                frames[frameSlots] = new Frame { frameCount = firstFrame + frameSlots,
                    gcCollectionCount0 = -1, gcCollectionCount1 = -1, gcCollectionCount2 = -1,
                    mainThread = new MetricValue { raw = -1 }, renderThread = new MetricValue { raw = -1 },
                    gcAlloc = new MetricValue { raw = -1 }, gcAllocatedInFrame = new MetricValue { raw = -1 } };
                ++frameSlots;
            }
            return index;
        }
        private void Update()
        {
            if (!recording) return;
            int frame = Time.frameCount;
            Volatile.Write(ref observedFrame, frame);
            lock (gate)
            {
                int index = Slot(frame); if (index < 0) return;
                ref Frame sample = ref frames[index];
                sample.updateObserved = true;
                sample.updateStopwatchTicks = Stopwatch.GetTimestamp();
                sample.updateUnityRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
                sample.focused = Application.isFocused;
                sample.gcCollectionCount0 = GC.CollectionCount(0);
                sample.gcCollectionCount1 = GC.CollectionCount(1);
                sample.gcCollectionCount2 = GC.CollectionCount(2);
                sample.mainThread = mainThread.Read(); sample.renderThread = renderThread.Read();
                sample.gcAlloc = gcAlloc.Read(); sample.gcAllocatedInFrame = gcAllocatedInFrame.Read();
            }
        }
        private IEnumerator EndOfFrameClocks()
        {
            var end = new WaitForEndOfFrame();
            while (recording)
            {
                yield return end;
                if (!recording) yield break;
                lock (gate)
                {
                    int index = Slot(Time.frameCount); if (index < 0) continue;
                    frames[index].endOfFrameObserved = true;
                    frames[index].endOfFrameStopwatchTicks = Stopwatch.GetTimestamp();
                    frames[index].endOfFrameUnityRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
                }
            }
        }
        // Fixed names only; literal names cause no per-call formatting or dictionary allocation.
        public static void RecordCost(string name, double milliseconds)
        {
            var owner = active;
            if (ReferenceEquals(owner, null) || !owner.recording || double.IsNaN(milliseconds)
                || double.IsInfinity(milliseconds) || milliseconds < 0) return;
            bool main = Thread.CurrentThread.ManagedThreadId == owner.mainThreadId;
            int frame = main ? Time.frameCount : Volatile.Read(ref owner.observedFrame);
            lock (owner.gate)
            {
                if (!owner.recording) return;
                if (!main) ++owner.offMainThreadCostCalls;
                int index = owner.Slot(frame); if (index < 0) return;
                ref Frame sample = ref owner.frames[index];
                switch (name)
                {
                    case "foundation_update": sample.foundation_update += milliseconds; ++sample.foundation_update_calls; break;
                    case "foundation_begin_clip": sample.foundation_begin_clip += milliseconds; ++sample.foundation_begin_clip_calls; break;
                    case "foundation_sole": sample.foundation_sole += milliseconds; ++sample.foundation_sole_calls; break;
                    case "recorder_capture": sample.recorder_capture += milliseconds; ++sample.recorder_capture_calls; break;
                    case "recorder_copy": sample.recorder_copy += milliseconds; ++sample.recorder_copy_calls; break;
                    case "recorder_drain": sample.recorder_drain += milliseconds; ++sample.recorder_drain_calls; break;
                    default: ++owner.unknownCostCalls; break;
                }
            }
        }
        private void Save(string reason)
        {
            if (saved || report == null) return;
            saved = true;
            lock (gate)
            {
                recording = false;
                report.frames = new Frame[frameSlots]; Array.Copy(frames, report.frames, frameSlots);
                report.frameSlots = frameSlots; report.overflowCalls = overflowCalls;
                report.unknownCostCalls = unknownCostCalls; report.offMainThreadCostCalls = offMainThreadCostCalls;
                report.storageComplete = overflowCalls == 0;
            }
            report.finishedUtc = DateTime.UtcNow.ToString("o"); report.status = reason;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.Write(JsonUtility.ToJson(report, true));
            }
            catch (Exception exception) { UnityEngine.Debug.LogError("Review frame clocks not saved: " + exception.Message); }
            DisposeRecorders();
            if (ReferenceEquals(active, this)) active = null;
        }
        private void DisposeRecorders()
        {
            if (disposed) return; disposed = true;
            mainThread?.Dispose(); renderThread?.Dispose(); gcAlloc?.Dispose(); gcAllocatedInFrame?.Dispose();
        }
        private void OnApplicationQuit() { Save("application_quit"); }
        private void OnDestroy() { Save("component_destroyed"); DisposeRecorders(); }
    }
}
