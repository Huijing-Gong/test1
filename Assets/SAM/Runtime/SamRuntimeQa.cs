using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SamProduction
{
    /// <summary>Runs against the imported character in the built player. This is functional QA, not an FPS benchmark.</summary>
    public static class SamRuntimeQa
    {
        [Serializable] private sealed class CaseResult
        {
            public string name;
            public bool passed;
            public int hits, effectsAfterCancel, voicesAfterCancel;
            public double finalTime;
            public string[] events;
            public Vector3 finalPosition, contactHand;
            public string error;
        }
        [Serializable] private sealed class Report
        {
            public string status = "built_player_functional_qa_not_art_or_performance_acceptance";
            public string unity, graphicsDevice, build_id;
            public bool passed;
            public CaseResult[] cases;
            public string[] runtimeErrors;
        }

        public static IEnumerator Run(SamSkillRunner runner, string output, GameObject characterPrefab)
        {
            Directory.CreateDirectory(output);
            var results = new List<CaseResult>();
            var errors = new List<string>();
            void Log(string message, string stack, LogType type)
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
            Application.logMessageReceived += Log;
            runner.automaticTick = false;
            var motor = runner.GetComponent<SamCharacterMotor>();
            Vector3 origin = runner.transform.position;
            Quaternion rotation = runner.transform.rotation;
            var target = UnityEngine.Object.FindFirstObjectByType<SamDamageable>();
            var events = new List<string>();
            Vector3 contactHand = Vector3.zero;
            void Event(SamTimedEvent e)
            {
                events.Add(e.id);
                if (e.kind == SamEventKind.Hit) contactHand = runner.Socket(e.socket).position;
            }
            runner.Presented += Event;
            void Reset(float speed)
            {
                runner.Cancel(true);
                motor.Teleport(origin, rotation);
                target?.ResetTarget();
                runner.playbackSpeed = speed;
                events.Clear();
                contactHand = Vector3.zero;
            }
            void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
            void ToTime(double time, float fps)
            {
                int guard = 0;
                while (runner.Clock.Time < time - 1e-8 && runner.IsCasting)
                {
                    runner.Step(Math.Min(1.0 / fps, (time - runner.Clock.Time) / runner.playbackSpeed), true);
                    if (++guard > 2000) throw new InvalidOperationException("Skill clock failed to advance");
                }
            }
            void Case(string name, Action operation)
            {
                var result = new CaseResult { name = name };
                int before = runner.HitCount;
                try { operation(); result.passed = true; }
                catch (Exception exception) { result.error = exception.ToString(); }
                result.hits = runner.HitCount - before;
                result.finalTime = runner.Clock.Time;
                result.finalPosition = runner.transform.position;
                result.contactHand = contactHand;
                result.events = events.ToArray();
                runner.Cancel(true);
                result.effectsAfterCancel = runner.vfx.ActiveEffectCount;
                result.voicesAfterCancel = runner.audioPresenter.ActiveVoiceCount;
                if (result.effectsAfterCancel != 0 || result.voicesAfterCancel != 0)
                { result.passed = false; result.error += " Active effects or audio survived immediate cancellation."; }
                results.Add(result);
            }
            yield return null;
            Case("exact_event_boundary_from_fractional_capture_steps", () => {
                foreach(double split in new[]{0.0,.42,.83})
                {
                    Reset(1);
                    int before=runner.HitCount;
                    Require(runner.TryStart(0), "Boundary start failed");
                    runner.Step(split,true);
                    runner.Step(.92-runner.Clock.Time,true);
                    Require(Math.Abs(runner.Clock.Time-.92)<1e-10,"Exact release time was rounded away");
                    Require(events.Count(e=>e=="energy_release")==1,"Exact boundary missed or duplicated release");
                    Require(runner.HitCount-before==1,"Exact boundary failed to apply damage");
                    runner.Step(0,true);
                    runner.Step(1e-6,true);
                    Require(events.Count(e=>e=="energy_release")==1&&runner.HitCount-before==1,"Boundary replay duplicated release");
                }
            });
            yield return null;
            Vector3? stableContact = null, stableEnd = null;
            Case("release_connection_tracks_hand_with_fixed_mass_at_three_rotations", () => {
                foreach(float yaw in new[]{0f,73f,-115f})
                {
                    Reset(1);
                    motor.Teleport(origin,rotation*Quaternion.Euler(0,yaw,0));
                    Require(runner.TryStart(0),"Connection start failed");
                    runner.Step(.92,true);
                    var release=runner.vfx.CaptureDiagnostics(Camera.main).effects.Single(e=>e.kind=="PunchImpact");
                    foreach(double time in new[]{.92,1.0,1.08})
                    {
                        runner.Step(Math.Max(0,time-runner.Clock.Time),true);
                        var sample=ImpactConnection12RuntimeProbe.Capture(runner,release.seed,"socket_hand.R",Camera.main);
                        ImpactConnection12RuntimeProbe.RequireEndpoints(sample,release.position,release.direction,
                            runner.vfx.punchEnergy,runner.vfx.visualScale,true);
                    }
                    var before=ImpactConnection12RuntimeProbe.Capture(runner,release.seed,"socket_hand.R",Camera.main);
                    runner.Clock.Paused=true;
                    runner.Step(2,true);
                    runner.vfx.SampleReleasedTransfers();runner.vfx.RefreshDiagnostics();
                    var after=ImpactConnection12RuntimeProbe.Capture(runner,release.seed,"socket_hand.R",Camera.main);
                    ImpactConnection12RuntimeProbe.RequirePauseUnchanged(before,after);
                    runner.Clock.Paused=false;
                }
            });
            yield return null;
            Case("release_connection_soft_cancel_freezes_then_immediate_clear", () => {
                Reset(1);Require(runner.TryStart(0),"Connection cancellation start failed");
                runner.Step(1.0,true);
                var release=runner.vfx.CaptureDiagnostics(Camera.main).effects.Single(e=>e.kind=="PunchImpact");
                var before=ImpactConnection12RuntimeProbe.Capture(runner,release.seed,"socket_hand.R",Camera.main);
                runner.Cancel(false);
                var stopped=ImpactConnection12RuntimeProbe.Capture(runner,release.seed,"socket_hand.R",Camera.main);
                ImpactConnection12RuntimeProbe.RequireSoftCancelFrozen(before,stopped);
                runner.Step(.03,true);runner.vfx.SampleReleasedTransfers();
                var tail=ImpactConnection12RuntimeProbe.Capture(runner,release.seed,"socket_hand.R",Camera.main);
                ImpactConnection12RuntimeProbe.RequireSoftCancelFrozen(before,tail);
                runner.Cancel(true);
                ImpactConnection12RuntimeProbe.RequireImmediateClear(
                    ImpactConnection12RuntimeProbe.Capture(runner,release.seed,"socket_hand.R",Camera.main));
            });
            yield return null;
            foreach (float fps in new[] { 30f, 60f, 120f })
            foreach (float speed in new[] { .5f, 1f, 1.5f })
            {
                Case($"complete_{fps}Hz_speed{speed}", () => {
                    Reset(speed);
                    int before = runner.HitCount;
                    Require(runner.TryStart(0), "Could not start imported action");
                    ToTime(runner.Current.duration, fps);
                    Require(!runner.IsCasting, "Clock remained running at duration");
                    Require(runner.HitCount - before == 1, "Expected exactly one damage event");
                    Require(events.SequenceEqual(runner.Current.events.Select(e => e.id)), "Events missing, reordered or duplicated");
                    if (stableContact.HasValue)
                    {
                        Require(Vector3.Distance(contactHand, stableContact.Value) < .0002f, "Contact changed with rate/speed");
                        Require(Vector3.Distance(runner.transform.position, stableEnd.Value) < .0002f, "Root endpoint changed with rate/speed");
                    }
                    else { stableContact = contactHand; stableEnd = runner.transform.position; }
                });
                yield return null;
            }
            foreach (double point in new[] { .5, .85, .95, 1.8 })
            {
                Case("cancel_and_restart_" + point, () => {
                    Reset(1);
                    Require(runner.TryStart(0), "Initial start failed");
                    ToTime(point, 60);
                    long previous = runner.Clock.ExecutionId;
                    int count = events.Count;
                    runner.Cancel(true);
                    runner.Step(10, true);
                    Require(events.Count == count && !runner.IsCasting, "Cancelled events continued");
                    Require(runner.vfx.ActiveEffectCount == 0, "Cancelled VFX survived");
                    motor.Teleport(origin, rotation);
                    events.Clear();
                    int before = runner.HitCount;
                    Require(runner.TryStart(0), "Restart failed");
                    Require(runner.Clock.ExecutionId > previous, "Restart did not create a new execution");
                    ToTime(3.01, 30);
                    Require(runner.HitCount - before == 1, "Restart did not hit exactly once");
                    Require(events.Count == runner.Current.events.Length, "Restart has wrong event count");
                });
                yield return null;
            }
            Case("pause_freezes_clock_bones_effects_and_audio", () => {
                Reset(1);
                Require(runner.TryStart(0), "Start failed");
                ToTime(1.0, 60);
                double time = runner.Clock.Time;
                Vector3 hand = runner.Socket("socket_hand.R").position;
                int active = runner.vfx.ActiveEffectCount, count = events.Count;
                runner.Clock.Paused = true;
                runner.Step(5, true);
                Require(runner.Clock.Time == time && events.Count == count, "Paused clock advanced");
                Require(Vector3.Distance(hand, runner.Socket("socket_hand.R").position) < 1e-6f, "Paused bones moved");
                Require(runner.vfx.ActiveEffectCount == active, "Paused effect age advanced");
                Require(runner.audioPresenter.PlaybackPaused, "Audio presenter did not pause");
                runner.Clock.Paused = false;
                ToTime(3.01, 60);
                Require(!runner.audioPresenter.PlaybackPaused, "Audio did not resume");
                Require(events.Count == runner.Current.events.Length, "Pause/resume duplicated events");
            });
            yield return null;
            Case("energy_hit_reaches_beyond_fist_contact", () => {
                Reset(1);
                Require(runner.skills[0].hitReach > 1, "Energy reach is missing");
                var existingCollider = target.GetComponent<Collider>();
                existingCollider.enabled = false;
                GameObject probe = null;
                try
                {
                    Require(runner.TryStart(0), "Probe sampling start failed");
                    ToTime(.92, 60);
                    Vector3 fist = runner.Socket("socket_hand.R").position;
                    Vector3 center = fist + runner.HitDirection("socket_hand.R") * 1.2f;
                    probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    probe.name = "QA energy target beyond physical fist";
                    probe.transform.SetPositionAndRotation(center, Quaternion.identity);
                    probe.transform.localScale = Vector3.one * .2f;
                    var damageable = probe.AddComponent<SamDamageable>();
                    Require(Vector3.Distance(fist, center) > runner.Current.hitRadius + .1f, "Probe is inside physical fist sphere");
                    Reset(1);
                    Require(runner.TryStart(0), "Energy test start failed");
                    ToTime(3.01, 60);
                    Require(damageable.ReceivedHits == 1, "Energy volume failed to hit the separated target exactly once");
                }
                finally
                {
                    existingCollider.enabled = true;
                    if (probe != null) UnityEngine.Object.Destroy(probe);
                }
            });
            yield return null;
            Case("disable_cancel_then_reenable", () => {
                Reset(1);
                Require(runner.TryStart(0), "Start failed");
                ToTime(1, 60);
                runner.gameObject.SetActive(false);
                Require(!runner.IsCasting && runner.vfx.ActiveEffectCount == 0, "Disabled actor retained an execution");
                runner.gameObject.SetActive(true);
                Reset(1);
                Require(runner.TryStart(0), "Reenabled actor cannot play");
                ToTime(3.01, 60);
                Require(!runner.IsCasting, "Reenabled execution did not complete");
            });
            yield return null;
            Case("two_instances_independent_rotation_pause_and_cancel", () => {
                Require(characterPrefab != null, "A clean character prefab is required; never clone warmed effect pools.");
                var actors = new List<GameObject>();
                var probes = new List<GameObject>();
                try
                {
                    var runners = new SamSkillRunner[2];
                    var independentEvents = new[] { new List<string>(), new List<string>() };
                    var origins = new[] { new Vector3(-8, 0, -8), new Vector3(8, 0, -8) };
                    var rotations = new[] { Quaternion.Euler(0, 70, 0), Quaternion.Euler(0, -115, 0) };
                    var contactLocal = new Vector3[2];
                    for (int i = 0; i < 2; i++)
                    {
                        int index = i;
                        var actor = UnityEngine.Object.Instantiate(characterPrefab, origins[i], rotations[i]);
                        actors.Add(actor);
                        var preview = actor.GetComponent<SamPreviewControls>();
                        if (preview != null) { preview.enabled = false; preview.showPanel = false; }
                        var instance = actor.GetComponent<SamSkillRunner>();
                        runners[i] = instance;
                        instance.automaticTick = false;
                        instance.playbackSpeed = 1;
                        Require(instance.TryStart(0), "Independent sampling failed");
                        instance.Step(.92f, true);
                        Vector3 fist = instance.Socket("socket_hand.R").position;
                        contactLocal[i] = Quaternion.Inverse(rotations[i]) * (fist - origins[i]);
                        var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        probes.Add(probe);
                        probe.name = "Independent energy target " + i;
                        probe.transform.position = fist + instance.HitDirection("socket_hand.R") * .9f;
                        probe.transform.localScale = Vector3.one * .25f;
                        probe.AddComponent<SamDamageable>();
                        instance.Cancel(true);
                        actor.GetComponent<SamCharacterMotor>().Teleport(origins[i], rotations[i]);
                        instance.Presented += e => independentEvents[index].Add(e.id);
                        Require(instance.TryStart(0), "Independent execution failed");
                    }
                    Require(Vector3.Distance(contactLocal[0], contactLocal[1]) < .002f, "World rotation changed the imported contact pose.");
                    runners[0].Step(.85f, true);
                    runners[1].Step(1f, true);
                    runners[0].Clock.Paused = true;
                    double pausedTime = runners[0].Clock.Time;
                    runners[0].Step(5, true);
                    Require(runners[0].Clock.Time == pausedTime, "Instance pause failed");
                    int survivingEffects = runners[1].vfx.ActiveEffectCount;
                    Require(survivingEffects > 0, "Second instance has no release effects to test isolation");
                    runners[0].Cancel(true);
                    Require(runners[1].vfx.ActiveEffectCount == survivingEffects && runners[1].IsCasting,
                        "Cancelling one instance changed the other instance's state");
                    Require(probes[0].GetComponent<SamDamageable>().ReceivedHits == 0, "Cancelled first instance still hit");
                    Require(probes[1].GetComponent<SamDamageable>().ReceivedHits == 1, "Second instance missed or duplicated its target");
                    runners[1].Step(3, true);
                    Require(!runners[1].IsCasting && independentEvents[1].SequenceEqual(runners[1].Current.events.Select(e => e.id)),
                        "Independent completion lost or duplicated an event");
                    foreach (var instance in runners)
                    {
                        instance.Cancel(true);
                        Require(instance.vfx.ActiveEffectCount == 0 && instance.audioPresenter.ActiveVoiceCount == 0,
                            "Independent instance did not release effects/audio");
                    }
                }
                finally
                {
                    foreach (var actor in actors) { actor.SetActive(false); UnityEngine.Object.Destroy(actor); }
                    foreach (var probe in probes) { probe.SetActive(false); UnityEngine.Object.Destroy(probe); }
                }
            });
            yield return null;
            Case("twenty_replays_without_teleport_preserve_return_facing", () => {
                Reset(1);
                Require(runner.skills[0].MotionAt(runner.skills[0].duration).magnitude<.0001f,"Trial expects a returning skill trajectory");
                Require(Mathf.Abs(Mathf.DeltaAngle(runner.skills[0].yaw.Evaluate(0),runner.skills[0].yaw.Evaluate((float)runner.skills[0].duration)))<.001f,
                    "Trial expects matching authored first/end facing");
                Vector3 initial=runner.transform.position;Quaternion facing=runner.transform.rotation;
                for(int i=0;i<20;i++)
                {
                    target?.ResetTarget();
                    Require(runner.TryStart(0),"Unreset replay failed "+i);
                    Require(Math.Abs(Quaternion.Dot(facing,runner.transform.rotation))>.999999f,"Skill entry added its authoring yaw "+i);
                    runner.Step(4,true);
                    Require(!runner.IsCasting,"Unreset replay did not finish "+i);
                    Vector3 drift=runner.transform.position-initial;drift.y=0;
                    Require(drift.magnitude<.0002f,"Returning skill accumulated horizontal translation "+i);
                    Require(Math.Abs(Quaternion.Dot(facing,runner.transform.rotation))>.999999f,"Returning skill accumulated facing "+i);
                }
            });
            yield return null;
            Case("one_hundred_replays_pool_stability", () => {
                int expectedPool = -1;
                for (int i = 0; i < 100; i++)
                {
                    Reset(1);
                    int before = runner.HitCount;
                    Require(runner.TryStart(0), "Replay start failed " + i);
                    runner.Step(4, true);
                    Require(runner.HitCount - before == 1 && !runner.IsCasting, "Replay event failure " + i);
                    runner.Cancel(true);
                    int pool = runner.vfx.PooledEffectCount;
                    if (expectedPool >= 0) Require(pool == expectedPool, "Effect pool grew across equivalent replays");
                    expectedPool = pool;
                    Require(runner.audioPresenter.ActiveVoiceCount == 0, "Audio voice leaked on replay");
                }
            });
            var stage = UnityEngine.Object.FindFirstObjectByType<SamStageLayout>();
            stage?.SetReferenceVisible(true);
            yield return null;
            var cast = UnityEngine.Object.FindFirstObjectByType<SamCastReaction>();
            Case("cast_body_and_ornament_share_clock_pause_and_cancel", () => {
                Require(cast != null, "Cast response was not staged");
                Reset(1);
                var nodes = cast.GetComponentsInChildren<Transform>(true);
                var restPositions = nodes.Select(n => n.localPosition).ToArray();
                var restRotations = nodes.Select(n => n.localRotation).ToArray();
                Vector3 castOwner = cast.transform.position;
                Quaternion castRotation = cast.transform.rotation;
                Require(runner.TryStart(0), "Cast trial start failed");
                ToTime(.92,60);
                Require(cast.HasResponse && Math.Abs(cast.SampledTime - runner.Clock.Time) < 1e-6, "Cast is not synchronized to skill time");
                bool bodyMoved=false, ornamentMoved=false;
                var rig = nodes.FirstOrDefault(n => n.name=="CAST_Lavender_RIG");
                for(int i=0;i<nodes.Length;i++)
                {
                    bool moved=Vector3.Distance(nodes[i].localPosition,restPositions[i])>.005f || Quaternion.Angle(nodes[i].localRotation,restRotations[i])>.5f;
                    bodyMoved |= moved && rig!=null && nodes[i].IsChildOf(rig);
                    ornamentMoved |= moved && nodes[i].name=="CAST_Ornament_ROOT";
                }
                Require(bodyMoved && ornamentMoved,"Body or independent ornament curves did not animate");
                Require(Vector3.Distance(cast.transform.position,castOwner)<1e-6f && Math.Abs(Quaternion.Dot(cast.transform.rotation,castRotation))>.999999f,"Visual response moved gameplay owner");
                var pausedPositions=nodes.Select(n=>n.localPosition).ToArray();
                var pausedRotations=nodes.Select(n=>n.localRotation).ToArray();
                runner.Clock.Paused=true;runner.Step(5,true);
                for(int i=0;i<nodes.Length;i++)Require(Vector3.Distance(nodes[i].localPosition,pausedPositions[i])<1e-6f && Math.Abs(Quaternion.Dot(nodes[i].localRotation,pausedRotations[i]))>.999999f,"Cast drifted while paused: "+nodes[i].name);
                runner.Cancel(true);
                Require(!cast.HasResponse,"Cast remained active after cancellation");
                for(int i=0;i<nodes.Length;i++)Require(Vector3.Distance(nodes[i].localPosition,restPositions[i])<1e-6f && Math.Abs(Quaternion.Dot(nodes[i].localRotation,restRotations[i]))>.999999f,"Cast did not restore on cancellation: "+nodes[i].name);
            });
            yield return null;
            Case("cast_stage_disable_reenable_and_replay", () => {
                Require(cast != null && stage != null,"Cast/stage missing");
                Reset(1);Require(runner.TryStart(0),"Start failed");ToTime(.92,60);
                stage.SetReferenceVisible(false);Require(!cast.HasResponse,"Stage disable retained cast response");
                stage.SetReferenceVisible(true);
                Require(cast.HasResponse && Math.Abs(cast.SampledTime-runner.Clock.Time)<1e-6,"Re-enabled stage did not catch up to current pose");
                runner.Cancel(true);motor.Teleport(origin,rotation);
                Require(runner.TryStart(0),"Replay failed");
                Require(cast.HasResponse && cast.SampledTime==0,"Replay retained response time");
                ToTime(3.01,60);
                Require(Math.Abs(cast.SampledTime-3.01)<1e-5,"Response duration or endpoint changed");
            });
            Case("dash_audio_timeline_spatial_transport_and_cleanup", () => SamAudioRuntimeQa.Run(runner));
            var audioPcm = new CaseResult { name = "dash_audio_realtime_pcm_pause_and_future_cancel" };
            yield return SamAudioRuntimeQa.RunRealtime(runner, output, (passed, error) => {
                audioPcm.passed = passed; audioPcm.error = error;
            });
            results.Add(audioPcm);
            yield return null;
            runner.Presented -= Event;
            yield return null;
            var report = new Report { unity = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName,
                build_id = SamBuildIdentity.Id,
                cases = results.ToArray(), runtimeErrors = errors.ToArray(), passed = results.All(r => r.passed) && errors.Count == 0 };
            File.WriteAllText(Path.Combine(output, "runtime_qa.json"), JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= Log;
            Debug.Log("SAM_RUNTIME_QA_FINISHED passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 1);
        }
    }
}
