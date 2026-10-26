using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction.BossArena
{
    // Real-time playback in the actual arena. This is a motion review, not a combat or performance pass.
    public sealed class ArenaFoundationReview : MonoBehaviour
    {
        [Serializable] private sealed class Sample
        {
            public double elapsed;
            public string clip;
            public float clipTime;
            public int sequenceIndex;
            public Vector3 actor, rightFoot, leftFoot, origin;
            public float yaw, headingYaw, requestedTravel, actualTravel;
            public ArenaFoundationSoleProbe.Sample sole;
        }
        [Serializable] private sealed class Report
        {
            public string kind = "realtime_unity_foundation_review_not_performance_acceptance";
            public string view, unity, build;
            public int width, height, captureFramerate;
            public int boneCount, shapeCount;
            public bool complete;
            public Sample[] samples;
        }
        private ArenaSession arena;
        private ArenaProductionClips library;
        private ArenaFoundationSoleProbe soleProbe;
        private readonly List<Sample> samples = new List<Sample>();
        private readonly string[] sequence = { "guard", "walk_start", "walk_loop", "walk_loop", "walk_stop", "turn_right_180", "walk_start", "walk_loop", "walk_loop", "walk_stop", "turn_left_180",
            "turn_right_15", "turn_left_15", "turn_right_45", "turn_left_45", "turn_right_90", "turn_left_90", "guard", "hit_light", "guard", "death" };
        private int index;
        private float clipTime, nextSample;
        private double started, previousTick;
        private Vector3 origin;
        private Quaternion heading;
        private string output, view;
        private bool ready, written, displayReady;
        private bool finishing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--sam-foundation-review") >= 0)
                new GameObject("Production18 real time motion review").AddComponent<ArenaFoundationReview>();
        }
        private static string Arg(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        private IEnumerator Start()
        {
            yield return null;
            arena = FindFirstObjectByType<ArenaSession>();
            library = arena.boss.GetComponent<ArenaProductionClips>();
            if (library == null) throw new InvalidOperationException("Production18 foundation assets were not imported.");
            soleProbe=new ArenaFoundationSoleProbe(arena.boss.GetComponentInChildren<SkinnedMeshRenderer>());
            output = Path.GetFullPath(Arg("--sam-output", Path.Combine(Application.persistentDataPath, "foundation_review")));
            view = Arg("--sam-view", "front");
            Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "realtime_motion.json"))) throw new IOException("Use a new motion evidence directory.");
            Application.runInBackground = true;
            Time.captureFramerate = 0;
            Time.timeScale = 1;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            arena.automaticTick = false;
            arena.boss.decisionEnabled = false;
            arena.Restart(true);
            arena.view.automaticTick = false;
            arena.hud.gameObject.SetActive(false);
            arena.player.gameObject.SetActive(false);
            arena.boss.capsule.enabled = false;
            arena.boss.transform.SetPositionAndRotation(new Vector3(0,.025f,-6), Quaternion.identity);
            arena.boss.capsule.enabled = true;
            var camera = arena.view.GetComponent<Camera>();
            camera.fieldOfView = 43;
            arena.boss.capsule.Move(Vector3.down*.05f);
            BeginClip();
            PositionCamera();
            foreach(string id in library.ids){library.Motion(id);library.Clip(id);}
            arena.boss.animationDriver.PoseContactClip(library.Clip(sequence[0]),0,0);
            soleProbe.Capture(0,0);
            displayReady=true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // Let the window/resolution settle before starting the measured motion clock.
            yield return null;
            yield return new WaitForEndOfFrame();
            // The independent framebuffer recorder initializes here too; keep the initial
            // guard visible while its buffers and encoder warm, before timed motion starts.
            yield return new WaitForSecondsRealtime(1.2f);
            yield return new WaitForEndOfFrame();
            started = previousTick = Time.realtimeSinceStartupAsDouble;
            AddSample(0,0);
            nextSample=1f/30;
            ready = true;
        }
        private void BeginClip()
        {
            long measuredStart=System.Diagnostics.Stopwatch.GetTimestamp();
            clipTime = 0;
            origin = arena.boss.transform.position;
            heading = arena.boss.transform.rotation;
            bool support = sequence[index]=="hit_light" || sequence[index]=="death"
                || (index>0 && sequence[index-1]=="hit_light");
            arena.boss.animationDriver.Pose(library.Clip(sequence[index]),0,0,false,support ? .14f : 0);
            ArenaReviewFrameDiagnostics.RecordCost("foundation_begin_clip", (System.Diagnostics.Stopwatch.GetTimestamp()-measuredStart)*1000.0/System.Diagnostics.Stopwatch.Frequency);
        }
        private void Update()
        {
            if (!ready || written || finishing) return;
            long measuredStart=System.Diagnostics.Stopwatch.GetTimestamp();
            try { ReviewTick(); }
            finally { ArenaReviewFrameDiagnostics.RecordCost("foundation_update", (System.Diagnostics.Stopwatch.GetTimestamp()-measuredStart)*1000.0/System.Diagnostics.Stopwatch.Frequency); }
        }
        private void ReviewTick()
        {
            if (!ready || written || finishing) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Save(false); Application.Quit(); return; }
            double now=Time.realtimeSinceStartupAsDouble;
            float remaining = (float)(now-previousTick);
            previousTick=now;
            while (remaining > .000001f && index < sequence.Length)
            {
                var motion = library.Motion(sequence[index]);
                float next = Mathf.Min(motion.duration, clipTime + remaining);
                float part = next - clipTime;
                motion.Sample(clipTime, out var oldPosition, out _);
                motion.Sample(next, out var newPosition, out float yaw);
                Vector3 requested = heading * (newPosition - oldPosition);
                Vector3 before = arena.boss.transform.position;
                arena.boss.capsule.Move(requested + Vector3.down * (2 * part));
                arena.boss.transform.rotation = heading * Quaternion.Euler(0,yaw,0);
                arena.boss.animationDriver.PoseContactClip(library.Clip(sequence[index]), next, part);
                clipTime = next;
                if (Time.realtimeSinceStartupAsDouble - started >= nextSample || clipTime >= motion.duration-.000001f)
                {
                    AddSample(requested.magnitude,Vector3.ProjectOnPlane(arena.boss.transform.position-before,Vector3.up).magnitude);
                    nextSample += 1f/30;
                }
                remaining -= part;
                if (clipTime >= motion.duration - .000001f)
                {
                    index++;
                    if (index < sequence.Length) BeginClip();
                    else { finishing=true; StartCoroutine(FinishAfterRenderedHold()); }
                }
            }
            if (index >= sequence.Length) return;
            PositionCamera();
        }
        private void PositionCamera()
        {
            // A constant world view keeps real root translation and foot contact visible.
            Vector3 target = new Vector3(0,1.5f,-2.8f);
            Vector3 position = view == "side" ? new Vector3(8,2.4f,-2.8f)
                : view == "three_quarter" ? new Vector3(9,5,10) : new Vector3(0,2.4f,8);
            arena.view.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
        }
        private void AddSample(float requested,float actual)
        {
            var sample=new Sample { elapsed=Time.realtimeSinceStartupAsDouble-started,
                clip=sequence[index],clipTime=clipTime,actor=arena.boss.transform.position,
                sequenceIndex=index,origin=origin,headingYaw=heading.eulerAngles.y,
                rightFoot=arena.boss.Socket("socket_foot.R").position,
                leftFoot=arena.boss.Socket("socket_foot.L").position,
                yaw=arena.boss.transform.eulerAngles.y,requestedTravel=requested,actualTravel=actual,
                sole=soleProbe.Capture(index,clipTime) };
            samples.Add(sample);
            ArenaReviewFrameDiagnostics.RecordCost("foundation_sole",sample.sole.sampleCostMs);
        }
        private void OnDestroy(){soleProbe?.Dispose();}
        private IEnumerator FinishAfterRenderedHold()
        {
            // Preserve the complete authored terminal pose for actual display and recording.
            yield return new WaitForSecondsRealtime(1.2f);
            yield return new WaitForEndOfFrame();
            Save(true);
            Application.Quit();
        }
        private void OnGUI()
        {
            if (!displayReady) return;
            GUI.color = Color.white;
            GUI.Label(new Rect(24,20,1000,40), "SAM / FOUNDATION 18 / REAL TIME / " + view + " / " + (index < sequence.Length ? sequence[index] : "COMPLETE"));
        }
        private void Save(bool complete)
        {
            if (written) return;
            written = true;
            var report = new Report { view=view, unity=Application.unityVersion, build=Application.version,
                width=Screen.width,height=Screen.height,captureFramerate=Time.captureFramerate,complete=complete,samples=samples.ToArray() };
            var mesh=arena.boss.GetComponentInChildren<SkinnedMeshRenderer>();
            report.boneCount=mesh.bones.Length;report.shapeCount=mesh.sharedMesh.blendShapeCount;
            File.WriteAllText(Path.Combine(output,"realtime_motion.json"),JsonUtility.ToJson(report,true));
        }
    }
}
