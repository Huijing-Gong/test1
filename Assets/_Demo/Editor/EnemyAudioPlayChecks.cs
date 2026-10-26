using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Uses the existing save-isolated real Play harness. Never saves a scene or prefab.
public static class EnemyAudioPlayChecks
{
    const string Folder = "Assets/Art/Enemies/Shared/Audio/Resources/EnemyAudio/";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new Vector3(18500, 120, 18500);
    [MenuItem("Tools/Enemies/Check Five Enemy Audio Events And Budget (Play)")]
    public static void Run() => EnemyFoundationPlayChecks.RunAudioChecks();
    public static void CheckAssets()
    {
        long total = 0;
        foreach (EnemySoundCue cue in Enum.GetValues(typeof(EnemySoundCue)))
        {
            string path = Folder + cue + ".wav";
            AudioClip clip = EnemyAudio.Clip(cue);
            Need(clip && clip.channels == 1 && clip.frequency == 22050 && clip.length >= .15f && clip.length < .8f,
                "Missing, oversized or non-mono clip: " + cue);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            Need(importer.defaultSampleSettings.compressionFormat == AudioCompressionFormat.PCM &&
                importer.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad &&
                importer.defaultSampleSettings.preloadAudioData, "Delayed/streamed warning import: " + cue);
            float[] data = new float[clip.samples]; Need(clip.GetData(data, 0), "Cannot decode clip: " + cue);
            double sum = 0, mean = 0; float peak = 0;
            foreach (float sample in data) { sum += sample * sample; mean += sample; peak = Mathf.Max(peak, Mathf.Abs(sample)); }
            Need(peak > .15f && peak <= .81f && Math.Sqrt(sum / data.Length) > .04 &&
                Math.Abs(mean/data.Length) < .001 && Mathf.Abs(data[0]) < .0001f && Mathf.Abs(data[data.Length-1]) < .008f,
                "Silent/clipped/DC or click-edge waveform: " + cue + " peak=" + peak);
            total += new FileInfo(path).Length;
        }
        Need(total < 500000, "Enemy mini-library exceeded 500 KB.");
        Debug.Log("EnemyAudio: PASS " + Enum.GetValues(typeof(EnemySoundCue)).Length + " mono preloaded PCM clips, bounded peaks/DC/edges; " + total + " source bytes.");
    }
    public static IEnumerator Tests(BaselineFlow flow, Transform player, Action<string> log)
    {
        CheckAssets();
        foreach (var ai in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None)) ai.enabled = false;
        Camera camera = Camera.main; Need(camera, "No real gameplay camera.");
        var follow = camera.GetComponent<ThirdPersonCamera>(); bool wasFollowing = follow && follow.enabled;
        Vector3 cameraPosition = camera.transform.position; Quaternion cameraRotation = camera.transform.rotation;
        Vector3 playerPosition = player.position; Quaternion playerRotation = player.rotation;
        var playerBody = player.GetComponent<CharacterController>();
        var spawned = new List<GameObject>();
        if (follow) follow.enabled = false;
        camera.transform.SetPositionAndRotation(Origin + new Vector3(0, 2f, -3f), Quaternion.identity);
        float listenerVolume = AudioListener.volume; bool listenerPaused = AudioListener.pause;
        AudioListener.volume = 1f; AudioListener.pause = false;
        player.GetComponent<Health>().RestoreFullHealth();
        player.GetComponent<PlayerMovement>().ClearImpact();
        EnemyAudio.StopAll(); yield return null;
        try
        {
            string[] paths = { "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab",
                "Assets/Art/Enemies/DomainPrototypes/Generated/LeafWisp.prefab",
                "Assets/Art/Enemies/DomainPrototypes/Generated/RuinPrism.prefab",
                "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab", RepairWispBuilder.PrefabPath };
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]), Origin, Quaternion.identity);
                spawned.Add(actor);
                if (actor.TryGetComponent(out StoneScoutRoadsideAI scout)) scout.enabled = false;
                if (actor.TryGetComponent(out DomainPrototypeEnemy domain)) domain.enabled = false;
                if (actor.TryGetComponent(out RailBeetleAI rail)) rail.enabled = false;
                if (actor.TryGetComponent(out RepairWispAI repair)) repair.enabled = false;
                yield return null;
                var sound = actor.GetComponent<EnemySoundEmitter>(); var health = actor.GetComponent<Health>();
                var feedback = actor.GetComponent<CombatFeedback>();
                Need(sound && (!feedback || feedback.UsesEnemySoundLibrary) && sound.CanSound, "Enemy audio not installed: " + paths[i]);
                EnemyAudio.StopAll();
                int before = EnemyAudio.StartedCount;
                health.TryApplyDamage(new DamageInfo(1, player.gameObject, Origin + Vector3.up, Vector3.forward));
                Need(EnemyAudio.StartedCount == before + 1 && EnemyAudio.LastStartedCue ==
                    (i == 0 || i == 3 ? EnemySoundCue.StoneHit : EnemySoundCue.ShellHit), "Actual hit missing/duplicated: " + paths[i]);
                foreach (AudioSource old in actor.GetComponents<AudioSource>())
                    Need(!old.isPlaying, "Legacy generic audio doubled the new hit: " + paths[i]);
                yield return new WaitForSeconds(.14f);
                before = EnemyAudio.StartedCount;
                health.TryApplyDamage(new DamageInfo(1000, player.gameObject, Origin + Vector3.up, Vector3.forward));
                Need(EnemyAudio.StartedCount == before + 1 && EnemyAudio.LastStartedCue == EnemySoundCue.CorePowerDown,
                    "Shutdown missing/duplicated on actual death: " + paths[i]);
                Need(!sound.Cue(EnemySoundCue.CoreLock), "Dead enemy produced a warning.");
                if (i == 0 || i == 3)
                {
                    yield return new WaitForSeconds(.71f);
                    Need(EnemyAudio.LastStartedCue == EnemySoundCue.StoneFall, "Fall sound missed physical collapse timing.");
                }
                sound.ResetForJourney(); Need(EnemyAudio.OwnerVoices(sound) == 0, "Reset retained audio.");
                health.RestoreFullHealth();
                // Exercise the real attack methods, not a polling approximation of states.
                if (i == 0)
                {
                    var feel = actor.GetComponent<StoneScoutFeedback>();
                    feel.BeginWindup(.38f);
                    Need(EnemyAudio.LastStartedCue == EnemySoundCue.StoneScrape, "Scout scrape not on windup.");
                    feel.BeginStrike();
                    Need(EnemyAudio.CountCue(EnemySoundCue.StoneScrape) == 0 && EnemyAudio.LastStartedCue == EnemySoundCue.StonePunch,
                        "Punch cue missing/anticipation lingered.");
                }
                else if (i == 1 || i == 2)
                {
                    Set(domain, "player", player.GetComponent<PlayerVitals>()); Set(domain, "playerHealth", player.GetComponent<Health>());
                    Call(domain, "BeginWarning");
                    Need(EnemyAudio.LastStartedCue == (i == 1 ? EnemySoundCue.CoreLock : EnemySoundCue.PrismCharge), "Wrong ranged/Prism warning.");
                    if (i == 2)
                    { Set(domain, "timer", 0f); Call(domain, "UpdateWarning"); Need(EnemyAudio.LastStartedCue == EnemySoundCue.PlateOpen, "No plate-open sound."); }
                    Call(domain, "Fire");
                    Need(EnemyAudio.LastStartedCue == (i == 1 ? EnemySoundCue.DartFire : EnemySoundCue.Pulse), "No actual release cue.");
                    Call(domain, "CancelWarning");
                }
                else if (i == 3)
                {
                    var visual = actor.GetComponent<RailBeetleVisual>(); visual.ResetVisual();
                    visual.SetAttack(.1f, false, .9f); Need(EnemyAudio.LastStartedCue == EnemySoundCue.BeetleCharge, "No mechanical charge.");
                    visual.SetAttack(1f, true); Need(EnemyAudio.LastStartedCue == EnemySoundCue.BeetleRush, "No rush sound.");
                    visual.SetAttack(0f, false); visual.SetStunned(true, true); visual.PlayImpact(Origin, true);
                    Need(EnemyAudio.LastStartedCue == EnemySoundCue.WallCrash, "Wall crash lost to rush/stun.");
                    yield return new WaitForSeconds(.37f);
                    Need(EnemyAudio.LastStartedCue == EnemySoundCue.Stun, "No post-crash stagger power-down.");
                }
                else
                {
                    sound.Cue(EnemySoundCue.RepairPrepare);
                    Set(repair, "player", player.GetComponent<PlayerVitals>()); Set(repair, "playerHealth", player.GetComponent<Health>());
                    SetPhase(repair, RepairWispAI.Phase.Prepare); Call(repair, "CancelRepair", false);
                    Need(EnemyAudio.LastStartedCue == EnemySoundCue.RepairBreak, "Cancelled real preparation had no break cue.");
                    EnemyAudio.StopAll(); yield return new WaitForSeconds(.1f);
                    before = EnemyAudio.StartedCount;
                    SetPhase(repair, RepairWispAI.Phase.Follow); Call(repair, "CancelRepair", false);
                    Need(EnemyAudio.StartedCount == before, "Idle target search emitted a fake interruption.");
                }
                sound.ResetForJourney();
                sound.Cue(EnemySoundCue.CoreLock); actor.SetActive(false);
                Need(EnemyAudio.OwnerVoices(sound) == 0, "Disabled actor retained audio.");
                Object.Destroy(actor); spawned.Remove(actor); yield return null;
                log("PASS audio role " + i + ": actual health hit/death, distinct attack cues, no old double-hit, reset/disable clean.");
            }

            // Drive a real heal to completion, including the last-charge sound. This
            // stationary fixture does not need to navigate or save a modified map.
            EnemyAudio.StopAll();
            playerBody.enabled = false; player.SetPositionAndRotation(Origin + Vector3.right * 4f, Quaternion.identity); playerBody.enabled = true;
            var recipient = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[2]), Origin + Vector3.right * 2f, Quaternion.identity);
            spawned.Add(recipient); recipient.GetComponent<DomainPrototypeEnemy>().enabled = false;
            var support = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[4]), Origin, Quaternion.identity);
            spawned.Add(support); var healing = support.GetComponent<RepairWispAI>();
            var groupObject = new GameObject("QA real audio repair group"); spawned.Add(groupObject);
            var group = groupObject.AddComponent<RepairWispGroup>(); group.Configure(new[] { recipient.GetComponent<Health>() });
            healing.ConfigureGroup(group); recipient.GetComponent<Health>().SetMaximumHealth(120);
            recipient.GetComponent<Health>().TryApplyDamage(new DamageInfo(90, player.gameObject, recipient.transform.position + Vector3.up, Vector3.forward));
            typeof(RepairWispAI).GetProperty("RemainingRepairs").SetValue(healing, 1);
            group.Alert(); Physics.SyncTransforms();
            bool heardPrepare = false, heardLink = false; int ticks = 0;
            while (healing.SuccessfulRepairs == 0 && ticks++ < 150)
            {
                heardPrepare |= EnemyAudio.CountCue(EnemySoundCue.RepairPrepare) > 0;
                heardLink |= EnemyAudio.CountCue(EnemySoundCue.RepairLink) > 0;
                yield return new WaitForFixedUpdate();
            }
            Need(healing.SuccessfulRepairs == 1 && healing.RemainingRepairs == 0 && recipient.GetComponent<Health>().CurrentHealth == 48 &&
                heardPrepare && heardLink && EnemyAudio.LastStartedCue == EnemySoundCue.RepairSuccess, "Actual finite heal did not sound prepare/link/success.");
            yield return new WaitForSeconds(.6f);
            Need(EnemyAudio.LastStartedCue == EnemySoundCue.RepairEmpty, "Last charge was silent or interrupted success.");
            healing.enabled = false; foreach (GameObject actor in spawned) if (actor) Object.Destroy(actor); spawned.Clear(); yield return null;
            log("PASS audio actual repair: preparation, visible link, +18 heal success, last-charge shutdown in sequence; idle cancellation silent.");

            // A crowded combat mix still owns exactly ten shared AudioSources.
            for (int i = 0; i < 18; i++)
            {
                var actor = new GameObject("QA audio crowd " + i); actor.transform.position = Origin + new Vector3(i * .02f, 0, 0);
                actor.AddComponent<Health>(); var emitter = EnemySoundEmitter.Ensure(actor); spawned.Add(actor);
                emitter.Cue(EnemySoundCue.ShellHit);
            }
            for (int i = 0; i < 6; i++) spawned[i].GetComponent<EnemySoundEmitter>().Cue(EnemySoundCue.CoreLock);
            yield return null;
            Need(EnemyAudio.ActiveVoices <= 10 && EnemyAudio.CountCue(EnemySoundCue.CoreLock) == 4 && EnemyAudio.PeakVoices <= 10,
                "Crowd voice budget/priority failed.");
            var pool = Object.FindFirstObjectByType<EnemyAudio>(); var sources = pool.GetComponentsInChildren<AudioSource>();
            Need(sources.Length == 10, "Per-hit sources allocated.");
            float gainSum = 0f; bool actualPlayback = false;
            foreach (AudioSource source in sources)
            {
                Need(!source.loop && source.spatialBlend == 1f && source.rolloffMode == AudioRolloffMode.Linear && source.dopplerLevel == 0,
                    "Unbounded looping/2D source or Doppler distortion.");
                if (source.clip) { gainSum += source.volume; actualPlayback |= source.isPlaying; }
            }
            Need(gainSum <= 1.151f && actualPlayback, "No native source playback or crowd summing headroom lost: " + gainSum);
            var farEnemy = spawned[17].GetComponent<EnemySoundEmitter>(); farEnemy.transform.position = Origin + Vector3.right * 100;
            int count = EnemyAudio.StartedCount; yield return new WaitForSeconds(.1f);
            Need(!farEnemy.Cue(EnemySoundCue.PrismCharge) && EnemyAudio.StartedCount == count, "Distant enemy occupied an audible warning slot.");
            Need(EnemyAudio.Attenuation(5, 3, 32) > EnemyAudio.Attenuation(22, 3, 32) && EnemyAudio.Attenuation(32, 3, 32) == 0,
                "Distance attenuation invalid.");
            var sword = player.GetComponent<PlayerCombat>();
            Call(sword, "RequestHitStop", .1f);
            int preserved = EnemyAudio.ActiveVoices;
            foreach (GameObject actor in spawned) Call(actor.GetComponent<EnemySoundEmitter>(), "Update");
            Call(pool, "LateUpdate");
            Need(EnemyAudio.ActiveVoices == preserved, "A real sword hit-stop cancelled audio like a menu.");
            foreach (AudioSource source in sources) if (source.clip) Need(!source.isPlaying, "Hit-stop did not freeze sound with the pose.");
            Call(sword, "RestoreOwnedTimeScale"); yield return null;
            flow.SetMenu(true); yield return null; yield return null;
            Need(EnemyAudio.ActiveVoices == 0, "Menu retained sound, unlike an actual hit-stop.");
            flow.SetMenu(false); yield return new WaitForSeconds(.15f);
            foreach (GameObject actor in spawned) actor.GetComponent<EnemySoundEmitter>().ResetForJourney();
            Need(!player.GetComponent<CombatFeedback>() || !player.GetComponent<CombatFeedback>().UsesEnemySoundLibrary,
                "Enemy audio replaced teammate's player sounds.");
            log("PASS audio crowd: <=10 shared native voices, 4 protected warnings, finite 3D range, bounded mix, no loops; menu clears, player audio untouched.");
            // Actual world reset cancels delayed cues too, not just current sources.
            var resetOwner = spawned[0].GetComponent<EnemySoundEmitter>();
            resetOwner.ResetForJourney(); resetOwner.Cue(EnemySoundCue.CoreLock);
            resetOwner.QueueCue(EnemySoundCue.RepairEmpty, .4f);
            flow.NewGame(); if (flow.Tutorial) flow.Tutorial.Skip(); flow.SetMenu(false);
            yield return null; yield return null;
            Need(EnemyAudio.ActiveVoices == 0, "New game retained shared sound.");
            count = EnemyAudio.StartedCount;
            yield return new WaitForSeconds(.45f);
            Need(EnemyAudio.StartedCount == count, "New game retained scheduled exhaust/fall cue.");
            // The integrated main resets the actual camera to camp on NewGame.
            // Return the test listener to its audible fixture before testing unload.
            camera.transform.position = Origin + new Vector3(0, 2, -6);
            // Create an unsaved runtime scene and unload a playing owner. The pool
            // survives globally but must not keep its unloaded owner's sound.
            var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("QA enemy audio unload");
            var unloading = new GameObject("QA unloading sound owner"); unloading.transform.position = Origin;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(unloading, scene);
            unloading.AddComponent<Health>(); var unloadOwner = EnemySoundEmitter.Ensure(unloading);
            Need(unloadOwner.Cue(EnemySoundCue.CoreLock), "Unload fixture did not play a source.");
            yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene); yield return null;
            Need(EnemyAudio.ActiveVoices == 0, "Unloaded scene retained a sound owner.");
            log("PASS audio lifecycle: actual NewGame cancels voices and scheduled cues; runtime scene unload leaves no ghost sound.");
        }
        finally
        {
            EnemyAudio.StopAll(); foreach (GameObject actor in spawned) if (actor) Object.Destroy(actor);
            Time.timeScale = 1f; Set(player.GetComponent<PlayerCombat>(), "ownsHitStop", false);
            playerBody.enabled = false; player.SetPositionAndRotation(playerPosition, playerRotation); playerBody.enabled = true;
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation); if (follow) follow.enabled = wasFollowing;
            AudioListener.volume = listenerVolume; AudioListener.pause = listenerPaused;
            flow.SetMenu(false);
        }
    }
    static void SetPhase(RepairWispAI target, RepairWispAI.Phase phase) =>
        typeof(RepairWispAI).GetProperty("CurrentPhase").SetValue(target, phase);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException("EnemyAudio: " + message); }
}
