using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SamProduction
{
    /// <summary>Functional candidate checks; no claim about perceived sound quality or DSP capture.</summary>
    public static class SamAudioRuntimeQa
    {
        public static void Run(SamSkillRunner runner)
        {
            var presenter = runner.audioPresenter;
            Require(presenter != null && runner.skills[0].useAudioTimeline, "Dash timeline/presenter missing");
            var skill = runner.skills[0];
            var oldMode = skill.impactAudioMode;
            var oldSpeed = runner.playbackSpeed;
            var oldMuted = presenter.muted;
            var oldOutput = runner.audioOutputMode;
            var motor = runner.GetComponent<SamCharacterMotor>();
            var origin = runner.transform.position;
            var rotation = runner.transform.rotation;
            var visual = new List<string>();
            var audio = new List<string>();
            void Visual(SamTimedEvent e) => visual.Add(e.id);
            void Audio(SamAudioCue cue, long _) => audio.Add(cue.id);
            void Reset(float speed)
            {
                runner.Cancel(true); motor.Teleport(origin, rotation);
                runner.playbackSpeed = speed; visual.Clear(); audio.Clear();
                Require(runner.TryStart(0), "Audio QA start failed");
            }
            runner.Presented += Visual;
            runner.AudioPresented += Audio;
            try
            {
                presenter.muted = false;
                runner.audioOutputMode = SamAudioOutputMode.SilentCapture;
                foreach (var mode in new[] { SamImpactAudioMode.Split, SamImpactAudioMode.Combined })
                foreach (float speed in new[] { .5f, 1f, 1.5f })
                {
                    skill.impactAudioMode = mode;
                    int started = presenter.StartedVoiceCount, stolen = presenter.StolenVoiceCount;
                    int dispatched = presenter.DispatchedCueCount, silent = presenter.SilentCueCount;
                    Reset(speed);
                    runner.Step(10, true);
                    int expected = mode == SamImpactAudioMode.Split ? 9 : 8;
                    Require(audio.Count == expected && audio.Distinct().Count() == expected, "Large step omitted/duplicated audio cues");
                    Require(presenter.DispatchedCueCount - dispatched == expected && presenter.SilentCueCount - silent == expected,
                        "Semantic dispatch count differs from selected timeline");
                    Require(presenter.StartedVoiceCount == started, "Silent capture submitted realtime audio");
                    Require(visual.SequenceEqual(skill.events.Select(e => e.id)), "Audio changed the six Presented events");
                    Require(presenter.StolenVoiceCount == stolen, "Expected concurrent cues exceeded the voice pool");
                    Require(mode == SamImpactAudioMode.Split ? !audio.Contains("energy_release_combined") :
                        !audio.Contains("energy_release_core") && !audio.Contains("energy_release_tail"), "Impact alternatives double-fired");
                    Require(presenter.ActiveVoiceCount == 0 && presenter.ActiveLoopCount == 0, "Completion retained audio/heat loop");
                }

                skill.impactAudioMode = SamImpactAudioMode.Split;
                Reset(1);
                runner.Step(.92, true);
                var atRelease = presenter.InspectVoices();
                var core = atRelease.Single(v => v.cueId == "energy_release_core");
                Require(Vector3.Distance(core.position, runner.Socket("socket_hand.R").position) < .0002f, "Impact missed sampled release position");
                Require(atRelease.All(v => Math.Abs(v.pitch - 1) < 1e-6), "Reference playback has pitch variation");
                Require(atRelease.Any(v => v.cueId == "heat_existing" && v.loop), "Heat loop missing at release");
                runner.Step(.05, true);
                var following = presenter.InspectVoices();
                Require(Vector3.Distance(following.Single(v => v.cueId == "energy_release_core").position, core.position) < 1e-6f,
                    "Fixed impact moved after release");
                Require(Vector3.Distance(following.Single(v => v.cueId == "punch_trail").position, runner.Socket("socket_hand.R").position) < .0002f,
                    "Whoosh failed to follow the animated fist");
                Require(Vector3.Distance(following.Single(v => v.cueId == "heat_existing").position, runner.Socket("socket_chest").position) < .0002f,
                    "Heat loop failed to follow chest");
                int count = audio.Count;
                runner.Clock.Paused = true; runner.Step(5, true);
                var paused = presenter.InspectVoices();
                Require(paused.Length == following.Length && paused.All(v => v.paused), "Pause reclaimed a source");
                Require(paused.All(v => Math.Abs(v.age - following.Single(p => p.cueId == v.cueId).age) < 1e-9), "Paused source lifetime advanced");
                Require(audio.Count == count, "Pause dispatched new audio");
                runner.Clock.Paused = false; runner.Step(.01, true);
                Require(presenter.InspectVoices().All(v => !v.paused), "Resume failed");
                runner.Cancel(true);
                runner.Step(10, true);
                Require(audio.Count == count && presenter.ActiveVoiceCount == 0, "Cancel fired later cues or retained voices");

                foreach (double point in new[] { 0, .85, .95, 1.8, 2.7 })
                {
                    Reset(1); runner.Step(point, true); runner.Cancel(true);
                    Require(presenter.ActiveVoiceCount == 0 && presenter.ActiveLoopCount == 0, "Immediate cancellation retained a loop");
                }
                Reset(1); runner.Step(.85, true); runner.Cancel(false); runner.Step(.1, true);
                Require(presenter.ActiveVoiceCount == 0, "Soft cancellation fade did not finish through idle Runner.Step");
                Reset(1); runner.Step(.85, true); runner.playbackSpeed = 0; runner.Step(1, true); runner.Cancel(false);
                Require(presenter.ActiveVoiceCount == 0, "Zero-speed cancellation trapped a fading loop");
                Reset(1); runner.Step(.85, true); runner.playbackSpeed = 0; runner.Cancel(false);
                Require(presenter.ActiveVoiceCount == 0, "Speed edit before the next Step trapped cancelled audio");
                Reset(1); runner.Step(.85, true); runner.Clock.Paused = true; runner.Cancel(false);
                Require(presenter.ActiveVoiceCount == 0 && presenter.ScheduledVoiceCount == 0,
                    "Pause edit then Cancel without Step retained audio");
                CheckSampleReentry(runner);
                Reset(1); runner.Step(2.33, true);
                var landing = presenter.InspectVoices().Single(v => v.cueId == "land");
                runner.Step(.05, true);
                Require(Vector3.Distance(landing.position, presenter.InspectVoices().Single(v => v.cueId == "land").position) < 1e-6,
                    "Landing emitter moved with recovering foot");
                runner.gameObject.SetActive(false);
                Require(presenter.ActiveVoiceCount == 0, "Disable retained voices");
                runner.gameObject.SetActive(true);
            }
            finally
            {
                runner.Cancel(true); runner.Presented -= Visual; runner.AudioPresented -= Audio;
                skill.impactAudioMode = oldMode; runner.playbackSpeed = oldSpeed; presenter.muted = oldMuted;
                runner.audioOutputMode = oldOutput;
                if (!runner.gameObject.activeSelf) runner.gameObject.SetActive(true);
                motor.Teleport(origin, rotation);
            }
        }

        private static void CheckSampleReentry(SamSkillRunner runner)
        {
            // time-zero marker, TryStart's final sample, ordinary Step sample, and later audio marker.
            foreach (int scenario in new[] { 0, 1, 2, 3 })
            {
                runner.Cancel(true); runner.playbackSpeed = 1;
                int zeroSamples = 0; bool intercepted = false; long replacement = -1;
                void Pose(SamSkillDefinition _, double time)
                {
                    if (Math.Abs(time) < 1e-9) zeroSamples++;
                    bool atPoint = scenario == 0 ? zeroSamples == 1 : scenario == 1 ? zeroSamples == 2 :
                        scenario == 2 ? time > .2 && time < .3 : Math.Abs(time - .8) < 1e-9;
                    if (intercepted || !atPoint) return;
                    intercepted = true; runner.Cancel(true);
                    if (scenario >= 2)
                    { Require(runner.TryStart(0), "Reentrant replacement failed"); replacement = runner.Clock.ExecutionId; }
                }
                runner.PoseSampled += Pose;
                try
                {
                    bool started = runner.TryStart(0);
                    if (scenario < 2)
                        Require(!started && !runner.IsCasting && runner.audioPresenter.ActiveVoiceCount == 0,
                            "Cancelled TryStart sample continued into old audio/heat initialization");
                    else
                    {
                        Require(started, "Reentry setup failed"); runner.Step(1, true);
                        Require(intercepted && runner.Clock.ExecutionId == replacement && Math.Abs(runner.Clock.Time) < 1e-9,
                            "Old outer Step advanced the replacement execution");
                        Require(runner.audioPresenter.InspectVoices().All(v => v.execution == replacement && v.age == 0),
                            "Old sample handler retained or aged a replacement voice");
                    }
                }
                finally { runner.PoseSampled -= Pose; runner.Cancel(true); }
            }
        }
        [Serializable] private sealed class PcmReport
        {
            public string status = "runtime_backend_evidence_not_human_listening";
            public string unity, error;
            public bool passed;
            public int sampleRate, bufferLength, bufferCount;
            public int cursorBefore, cursorAfter, pauseCursorBefore, pauseCursorAfter, resumedCursor;
            public int scheduledBeforeCancel, backendAfterCancel, writesBeforeStableFrames, writesAfterStableFrames;
            public int hitchFollowCursor, hitchFollowCursorAfterYield;
            public double clockAtPause, clockAfterPause, dspBefore, dspAfter, fixedLateness, fixedRenderAge;
            public float outputPeak;
        }

        /// <summary>Real yields and AudioSource PCM/cursor evidence. Missing DSP output is a failure, never a pass.</summary>
        public static IEnumerator RunRealtime(SamSkillRunner runner, string output, Action<bool, string> completed)
        {
            var report = new PcmReport { unity = Application.unityVersion, sampleRate = AudioSettings.outputSampleRate };
            AudioSettings.GetDSPBufferSize(out report.bufferLength, out report.bufferCount);
            var checks = RealtimeChecks(runner, report);
            while (true)
            {
                bool next;
                try { next = checks.MoveNext(); }
                catch (Exception ex) { report.error = ex.ToString(); break; }
                if (!next) { report.passed = true; break; }
                yield return checks.Current;
            }
            (checks as IDisposable)?.Dispose();
            File.WriteAllText(Path.Combine(output, "audio_pcm_transport.json"), JsonUtility.ToJson(report, true));
            completed(report.passed, report.error);
        }

        private static IEnumerator RealtimeChecks(SamSkillRunner runner, PcmReport report)
        {
            var p = runner.audioPresenter;
            var oldAutomatic = runner.automaticTick; var oldOutput = runner.audioOutputMode;
            var oldSpeed = runner.playbackSpeed; var oldMuted = p.muted; var oldSpatial = p.spatialBlend;
            var oldMode = runner.skills[0].impactAudioMode;
            var position = runner.transform.position; var rotation = runner.transform.rotation;
            var motor = runner.GetComponent<SamCharacterMotor>();
            SamAudioPresenter.VoiceState Heat() => p.InspectVoices().Single(v => v.cueId == "heat_existing");
            try
            {
                runner.Cancel(true); runner.automaticTick = true; runner.audioOutputMode = SamAudioOutputMode.Realtime;
                runner.playbackSpeed = 1; p.muted = false; p.spatialBlend = 0;
                runner.skills[0].impactAudioMode = SamImpactAudioMode.Split;
                Require(runner.TryStart(0), "PCM QA start failed");
                double deadline = Time.realtimeSinceStartupAsDouble + 4;
                while (runner.Clock.Time < .25 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Require(runner.IsCasting && runner.Clock.Time < .6, "PCM QA real-time frames stalled");
                report.cursorBefore = Heat().sampleCursor; report.dspBefore = AudioSettings.dspTime;
                report.writesBeforeStableFrames = p.CursorWriteCount;
                var pcm = new float[1024];
                p.ReadOutput("heat_existing", pcm); // Prime Unity's per-source history buffer before waiting.
                yield return new WaitForSecondsRealtime(.08f);
                report.cursorAfter = Heat().sampleCursor; report.dspAfter = AudioSettings.dspTime;
                report.writesAfterStableFrames = p.CursorWriteCount;
                Require(report.dspAfter > report.dspBefore && report.cursorAfter != report.cursorBefore,
                    "DSP or real PCM cursor did not advance across yield");
                Require(report.writesBeforeStableFrames == report.writesAfterStableFrames,
                    "Normal real-time frames rewrote source cursors");
                Require(p.ReadOutput("heat_existing", pcm), "Active PCM probe missing");
                report.outputPeak = pcm.Max(v => Mathf.Abs(v));
                Require(report.outputPeak > 1e-7f, "No nonzero source output observed; audio device/backend may be unavailable");

                runner.Clock.Paused = true; // Deliberately no intervening Step call.
                report.clockAtPause = runner.Clock.Time;
                yield return new WaitForSecondsRealtime(.08f); // Allow the queued audio-thread pause command to settle.
                report.pauseCursorBefore = Heat().sampleCursor;
                yield return new WaitForSecondsRealtime(.12f);
                report.pauseCursorAfter = Heat().sampleCursor; report.clockAfterPause = runner.Clock.Time;
                Require(report.pauseCursorBefore == report.pauseCursorAfter && report.clockAtPause == report.clockAfterPause,
                    "Pause did not freeze the actual PCM cursor and semantic clock across yields");
                Require(p.InspectVoices().All(v => v.paused) && p.ScheduledVoiceCount == 0,
                    "Pause retained an unpaused/future source");
                runner.Clock.Paused = false;
                yield return new WaitForSecondsRealtime(.08f);
                report.resumedCursor = Heat().sampleCursor;
                Require(report.resumedCursor != report.pauseCursorAfter, "PCM did not resume across yield");
                runner.playbackSpeed = .5f;
                yield return new WaitForSecondsRealtime(.08f);
                Require(Math.Abs(Heat().pitch - .5f) < 1e-6, "Transport speed did not reach AudioSource pitch");

                // Test cancellation past a future DSP deadline, not just an immediate voice counter.
                runner.Cancel(true); motor.Teleport(position, rotation); runner.playbackSpeed = 1;
                Require(runner.TryStart(0), "Future schedule QA start failed");
                report.scheduledBeforeCancel = p.ScheduledVoiceCount;
                Require(report.scheduledBeforeCancel > 0, "No real future DSP request was created");
                runner.Clock.Paused = true; runner.Cancel(false);
                yield return new WaitForSecondsRealtime(.3f);
                report.backendAfterCancel = p.BackendPlayingCount;
                Require(p.ActiveVoiceCount == 0 && p.ScheduledVoiceCount == 0 && report.backendAfterCancel == 0,
                    "Cancelled scheduled source played after its original deadline");

                // A deliberately late fixed impact keeps the attack; only its explicit render cursor starts at zero.
                runner.automaticTick = false; Require(runner.TryStart(0), "Late fixed QA start failed");
                runner.Step(.933333333333, true);
                var core = p.InspectVoices().Single(v => v.cueId == "energy_release_core");
                report.fixedLateness = core.lateness; report.fixedRenderAge = core.renderAge;
                Require(core.age > .013 && core.age < .014 && core.renderAge == 0 && core.submitted,
                    "Fixed transient lost its attack or hid semantic lateness");
                var dash = p.InspectVoices().Single(v => v.cueId == "dash_start");
                report.hitchFollowCursor = dash.sampleCursor;
                var dashClip = runner.skills[0].audioCues.Single(c => c.id == "dash_start").clip;
                int expectedCursor = SamAudioPresenter.SampleOffset(dashClip, dash.renderAge, false);
                int tolerance = Math.Max(256, report.bufferLength * report.bufferCount);
                Require(Math.Abs(dash.sampleCursor - expectedCursor) <= tolerance && dash.sampleCursor > dashClip.frequency * .08,
                    "Late following cue did not seek its actual PCM cursor to the semantic age");
                double waitStart = AudioSettings.dspTime;
                yield return new WaitForSecondsRealtime(.03f);
                report.hitchFollowCursorAfterYield = p.InspectVoices().Single(v => v.cueId == "dash_start").sampleCursor;
                Require(report.hitchFollowCursorAfterYield > report.hitchFollowCursor,
                    "Late following cue PCM did not continue across a real yield");
                double elapsed = AudioSettings.dspTime - waitStart;
                Require(elapsed < .15, "Late-cue PCM probe hit an uncontrolled long frame");
                runner.Step(elapsed, true);
                double renderBeforePause = p.InspectVoices().Single(v => v.cueId == "energy_release_core").renderAge;
                runner.Clock.Paused = true; yield return new WaitForSecondsRealtime(.06f);
                runner.Clock.Paused = false; runner.Step(.005, true);
                Require(Math.Abs(p.InspectVoices().Single(v => v.cueId == "energy_release_core").renderAge - renderBeforePause - .005) < 1e-8,
                    "Resume rebuilt fixed transient from cue time instead of its render origin");
                runner.Cancel(true); Require(runner.TryStart(0), "Hitch QA start failed");
                int skipped = p.ExpiredSkippedCount;
                runner.Step(1.3, true);
                Require(p.ExpiredSkippedCount > skipped && !p.InspectVoices().Any(v => v.cueId == "energy_release_core"),
                    "Severely late fixed transient was played or its skip was not recorded");
            }
            finally
            {
                runner.Cancel(true); runner.automaticTick = oldAutomatic; runner.audioOutputMode = oldOutput;
                runner.playbackSpeed = oldSpeed; p.muted = oldMuted; p.spatialBlend = oldSpatial;
                runner.skills[0].impactAudioMode = oldMode; motor.Teleport(position, rotation);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
