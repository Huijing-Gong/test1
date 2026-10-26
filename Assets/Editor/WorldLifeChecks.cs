using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Focused, isolated authoring checks. Hero relocation and review camera paths
// below are explicitly controlled QA, not footage of a person playing the game.
[InitializeOnLoad]
public static class WorldLifeChecks
{
    const string Prefix = "QA.WorldLife.";
    const string ScenePath = WorldLifeInstaller.ScenePath;
    const string Output = WorldLifeInstaller.Output;
    static string report;
    [Serializable] class Saved { public string key, value; public bool exists; }
    [Serializable] class Snapshot { public Saved[] entries; }
    static BaselineFlow flow;
    static BaselineWorld world;
    static WorldVegetationWind wind;
    static WorldSmallWildlife wildlife;
    static RegionalNatureAudio nature;
    static Transform hero;
    static float originalCaptureDelta;
    static int errors;
    static bool audioRecording;
    static MemoryStream pcm;
    static BinaryWriter pcmWriter;
    static double audioEnergy;
    static long audioSampleCount;
    static int audioChannels, audioRate;
    static Bounds[] reviewOccluders;

    static WorldLifeChecks()
    { EditorApplication.playModeStateChanged += Changed; EditorApplication.update += Watch; }

    static void Isolated()
    {
        if (!Application.dataPath.StartsWith("/private/tmp/adventure-trio-knight-", StringComparison.Ordinal))
            throw new InvalidOperationException("World life QA runs only in the isolated project.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Unsaved scene edits; QA did not discard them.");
        Directory.CreateDirectory(Output);
    }

    public static void RunStatic()
    {
        Isolated(); report = Output + "/static-validation.txt";
        File.WriteAllText(report, "Authored nature/wildlife validation. No original resource or scene saves.\n");
        try
        {
            string sceneHash = Hash(ScenePath); EditorSceneManager.OpenScene(ScenePath);
            var lives = Object.FindObjectsByType<WorldSmallWildlife>(FindObjectsSortMode.None);
            var sounds = Object.FindObjectsByType<RegionalNatureAudio>(FindObjectsSortMode.None);
            Need(lives.Length == 1 && sounds.Length == 1 && lives[0].gameObject == sounds[0].gameObject,
                "one world-owned wildlife/audio host");
            var life = lives[0]; var sound = sounds[0];
            Need(life.GetComponent<BaselineWorld>() && life.GetComponent<BaselineFlow>() && life.GetComponent<WorldVegetationWind>(),
                "life layer uses existing native world, flow and wind");
            Need(life.birdAnchors.Length == 6 && life.butterflyAnchors.Length == 8, "six birds and eight butterflies authored");
            Need(life.birdAnchors.All(Finite) && life.butterflyAnchors.All(Finite), "wildlife anchors finite");
            var shader = Shader.Find("FoldedRealm/WildlifeWing");
            Need(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "wildlife shader supported and compiles");
            Need(life.birdMaterial && life.butterflyMaterial && life.birdMaterial.shader == shader && life.butterflyMaterial.shader == shader,
                "independent bird and butterfly materials assigned");
            foreach (var mesh in new[] { life.birdBodyMesh, life.birdWingMesh, life.butterflyMesh })
            {
                Need(mesh && AssetDatabase.GetAssetPath(mesh).StartsWith(WorldLifeInstaller.Folder + "/", StringComparison.Ordinal),
                    "wildlife mesh is an own serialized asset");
                Need(mesh.vertexCount > 10 && mesh.normals.Length == mesh.vertexCount && mesh.colors.Length == mesh.vertexCount,
                    "wildlife mesh carries complete geometry, normals and plumage colors: " + mesh.name);
                Need(mesh.vertices.All(Finite) && mesh.bounds.size.magnitude < 1.5f,
                    "small wildlife geometry has finite metre-scale bounds: " + mesh.name);
            }
            var hinges = new List<Vector2>(); life.butterflyMesh.GetUVs(1, hinges);
            Need(hinges.Count == life.butterflyMesh.vertexCount && hinges.Any(h => h.x > .9f) && hinges.Any(h => h.x == 0),
                "butterfly shader hinge data separates wings from fixed body");
            Need(sound.waterLoop && sound.foliageLoop && sound.BirdClipCount >= 2 && sound.birdCalls.All(c => c),
                "actual nature loops and two recorded bird calls assigned");
            foreach (var clip in new[] { sound.waterLoop, sound.foliageLoop }.Concat(sound.birdCalls))
            {
                string path = AssetDatabase.GetAssetPath(clip);
                Need(path.StartsWith(WorldLifeInstaller.Folder + "/Audio/", StringComparison.Ordinal) && clip.length > .05f && clip.channels == 1 && clip.frequency >= 22050,
                    "valid compact mono nature recording: " + clip.name + " / " + clip.length.ToString("F2") + " sec");
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                Need(importer && importer.defaultSampleSettings.loadType ==
                    (clip == sound.waterLoop || clip == sound.foliageLoop ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory),
                    "appropriate audio import/load mode: " + clip.name);
            }
            string sourceFile = WorldLifeInstaller.Folder + "/Audio/SOURCES.md";
            Need(File.Exists(sourceFile), "recording author/source/license metadata retained");
            string sourceText = File.ReadAllText(sourceFile);
            Need(sourceText.Contains("Creative Commons 0") && sourceText.Contains("freesound.org/people/") && sourceText.Contains("creativecommons.org/publicdomain/zero/1.0"),
                "audio source metadata includes CC0 and source links");
            Need(sound.waterAnchors.Length >= 4 && sound.treeAnchors.Length >= 1 && sound.waterAnchors.All(Finite) && sound.treeAnchors.All(Finite),
                "spatial nature uses authored shoreline and tree anchors");
            VerifySources();
            Need(Hash(ScenePath) == sceneHash, "static checks preserve authored scene bytes");
            Log("FINAL PASS");
        }
        catch (Exception error) { Log("FINAL FAIL " + error); throw; }
    }

    static void VerifySources()
    {
        string path = Output + "/source-assets-sha256.txt";
        Need(File.Exists(path), "installer protected-source manifest exists"); int count = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length < 67) continue;
            string asset = line.Substring(66);
            if (!File.Exists(asset) || Hash(asset) != line.Substring(0, 64)) throw new InvalidOperationException("Protected resource changed: " + asset);
            count++;
        }
        Need(count > 100, "protected original resources remain byte-identical: " + count);
    }

    public static void RunPlay()
    {
        Isolated(); if (Application.isBatchMode) throw new InvalidOperationException("Use GUI Unity for rendered life captures.");
        EditorSceneManager.OpenScene(ScenePath);
        var host = Object.FindFirstObjectByType<BaselineFlow>();
        var saved = new List<Saved>();
        foreach (string prefix in new HashSet<string> { host.saveKeyPrefix, BaselineSaveStore.DefaultKeyPrefix })
            foreach (string suffix in new[] { ".primary", ".backup" })
            { string key = prefix + suffix; saved.Add(new Saved { key = key, exists = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") }); }
        SessionState.SetString(Prefix + "saved", JsonUtility.ToJson(new Snapshot { entries = saved.ToArray() }));
        SessionState.SetString(Prefix + "scene", Hash(ScenePath));
        SessionState.SetString(Prefix + "savePrefix", Prefix + Guid.NewGuid().ToString("N"));
        SessionState.SetString(Prefix + "deadline", DateTime.UtcNow.AddSeconds(480).Ticks.ToString());
        SessionState.SetBool(Prefix + "background", Application.runInBackground);
        SessionState.SetBool(Prefix + "passed", false); SessionState.SetBool(Prefix + "active", true);
        host.saveKeyPrefix = SessionState.GetString(Prefix + "savePrefix", "");
        report = Output + "/runtime-validation.txt";
        File.WriteAllText(report, "Actual Unity world life validation. Controlled camera and Hero relocations; unrelated enemy AI disabled in memory. Not human Gameplay, no new ground or forced shaders.\n");
        Application.runInBackground = true; EditorApplication.isPaused = false;
        EditorApplication.ExecuteMenuItem("Window/General/Game"); Focus(); EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Prefix + "active", false)) return;
        report = Output + "/runtime-validation.txt";
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            errors = 0; Application.logMessageReceived += CheckLog; originalCaptureDelta = Time.captureDeltaTime;
            flow = Object.FindFirstObjectByType<BaselineFlow>();
            flow.saveKeyPrefix = SessionState.GetString(Prefix + "savePrefix", "");
            flow.StartCoroutine(Guard(Tests())); return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        bool passed = SessionState.GetBool(Prefix + "passed", false);
        try
        {
            foreach (var saved in JsonUtility.FromJson<Snapshot>(SessionState.GetString(Prefix + "saved", "")).entries)
                if (saved.exists != PlayerPrefs.HasKey(saved.key) || saved.value != PlayerPrefs.GetString(saved.key, ""))
                {
                    passed = false; Log("FAIL original save changed; restoring snapshot: " + saved.key);
                    if (saved.exists) PlayerPrefs.SetString(saved.key, saved.value); else PlayerPrefs.DeleteKey(saved.key);
                }
            if (Hash(ScenePath) != SessionState.GetString(Prefix + "scene", "")) { passed = false; Log("FAIL authored scene bytes changed"); }
            BaselineSaveStore.Clear(SessionState.GetString(Prefix + "savePrefix", "")); PlayerPrefs.Save();
        }
        catch (Exception error) { passed = false; Log("FAIL cleanup " + error); }
        Application.runInBackground = SessionState.GetBool(Prefix + "background", false);
        SessionState.SetBool(Prefix + "active", false);
        Log(passed ? "FINAL PASS; original scenes and saves unchanged" : "FINAL FAIL");
        EditorApplication.Exit(passed ? 0 : 1);
    }

    static IEnumerator Tests()
    {
        yield return null; flow.NewGame(); if (flow.Tutorial) flow.Tutorial.Skip(); flow.SetMenu(false);
        world = flow.GetComponent<BaselineWorld>(); hero = world.player;
        wind = world.GetComponent<WorldVegetationWind>(); wildlife = world.GetComponent<WorldSmallWildlife>(); nature = world.GetComponent<RegionalNatureAudio>();
        Need(hero && wind && wildlife && nature, "real Hero, wind, wildlife and audio hosts exist");
        yield return Wait(() => Running, 20, "world opening completes and all life clocks start");
        yield return new WaitForSeconds(.3f);
        Need(wildlife.BirdCount == 6 && wildlife.ButterflyCount == 8, "runtime population matches six birds and eight butterflies");
        Need(!WildlifeRoot().GetComponentInChildren<Collider>(true) && !WildlifeRoot().GetComponentInChildren<AdventureCreatureAI>(true),
            "wildlife creates no gameplay colliders or enemy AI");
        yield return PauseCheck(() => flow.SetMenu(true), () => flow.SetMenu(false), () => flow.MenuOpen, "Esc");
        yield return PauseCheck(() => world.SetJournal(true), () => world.SetJournal(false), () => world.JournalOpen, "J journal");
        yield return PauseCheck(() => Need(flow.ShowMap(), "real M map opens"), () => flow.SetMenu(false), () => flow.MapOpen, "M map");
        var view = Camera.main; Need(view, "real main camera exists");
        var thirdPerson = view.GetComponent<ThirdPersonCamera>(); if (thirdPerson) thirdPerson.enabled = false;
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component is AdventureCreatureAI || component is StoneScoutRoadsideAI || component is DomainPrototypeEnemy ||
                component is RailBeetleAI || component is GelBlobAI || component is RepairWispAI || component is EnemyNavigationWorld)
                component.enabled = false;
        var movement = hero.GetComponent<PlayerMovement>(); if (movement) movement.enabled = false;
        var capsule = hero.GetComponent<CharacterController>(); if (capsule) capsule.enabled = false;
        // Existing sounds are muted only in this disposable Play session so the
        // capture can be inspected for actual new nature audio, not game music.
        foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!IsNatureSource(source)) source.mute = true;
        var birdAnchor = wildlife.birdAnchors[0]; hero.position = birdAnchor + Vector3.right * 5;
        Physics.SyncTransforms();
        wildlife.enabled = false; yield return null; yield return new WaitForEndOfFrame(); wildlife.enabled = true;
        yield return null; yield return new WaitForEndOfFrame();
        Need(WorldLifeRootCount() == 1 && wildlife.BirdCount == 6 && wildlife.ButterflyCount == 8,
            "disable and re-enable recreate one owned population without duplicate roots");
        int natureSources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(IsNatureSource);
        nature.enabled = false; yield return null; nature.enabled = true; yield return null;
        Need(natureSources == 5 && Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(IsNatureSource) == 5,
            "audio re-enable reuses exactly five voices");
        yield return new WaitForSeconds(.4f);
        var bird = WildlifeRoot().Find("Ground bird 1"); Need(bird, "authored first bird has real rendered runtime body");
        Need(bird.GetComponentsInChildren<Renderer>().Length == 3, "bird body and two separate feathered wings are visible geometry");
        view.fieldOfView = 43; view.nearClipPlane = .03f; view.farClipPlane = 240;
        reviewOccluders = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r => r.enabled && r.bounds.size.y > .6f && r.bounds.size.y < 25 && r.bounds.size.x < 14 && r.bounds.size.z < 14 &&
                !r.transform.IsChildOf(hero) && !r.transform.IsChildOf(WildlifeRoot()) && !r.GetComponentInParent<Health>() &&
                !r.sharedMaterials.Any(m => m && m.shader && m.shader.name == "FoldedRealm/LivingVegetation"))
            .Select(r => r.bounds).ToArray();
        AimReviewCamera(view, birdAnchor + Vector3.up * .15f, new Vector3(1.5f, .7f, -1.75f));
        string frames = Output + "/Frames"; Directory.CreateDirectory(frames);
        Time.captureFramerate = 30; BeginAudioCapture();
        int startles = wildlife.StartleCount, calls = nature.BirdPlayCount;
        // Lake-island flower pockets face nature rather than the spawn stair's
        // stone pillars. Select an existing serialized animal, never move it.
        int butterflyIndex = Mathf.Min(4, wildlife.butterflyAnchors.Length - 1);
        Vector3 butterfly = wildlife.butterflyAnchors[butterflyIndex];
        var butterflyActor = WildlifeRoot().Find("Flower butterfly " + (butterflyIndex + 1));
        var lakeVolume = Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None).Where(v => v.IsValid)
            .OrderByDescending(v => v.VolumeCollider.bounds.size.x * v.VolumeCollider.bounds.size.z).First();
        Vector3 lakeCenter = lakeVolume.VolumeCollider.bounds.center; lakeCenter.y = lakeVolume.WaterSurfaceY;
        Vector3 bank = FindReviewBank(lakeVolume, lakeCenter);
        byte[] butterflyA = null;
        for (int frame = 0; frame < 240; frame++)
        {
            if (frame == 60) { hero.position = birdAnchor + Vector3.right * 2; Physics.SyncTransforms(); }
            if (frame >= 60 && frame < 120)
            {
                // Track the real departing bird from nearby; the old pullback
                // crossed a banner and hid the entire animal behind its cloth.
                AimReviewCamera(view, bird.position + Vector3.up * .08f, new Vector3(1.5f, .7f, -1.75f));
            }
            if (frame == 120)
            {
                Need(wildlife.StartleCount > startles && bird.position.y > birdAnchor.y + 2, "real Hero proximity starts upward curved bird escape");
                Need(nature.BirdPlayCount > calls && (nature.LastBirdPosition - birdAnchor).sqrMagnitude < 49,
                    "actual takeoff triggers a nearby spatial recorded bird call");
                hero.position = butterfly + new Vector3(0, 0, -5); Physics.SyncTransforms();
                view.fieldOfView = 46;
                AimReviewCamera(view, butterflyActor.position, new Vector3(.70f, .40f, -.90f));
            }
            if (frame == 180)
            {
                // The real walkable bank lies outside the water footprint. Aim
                // into the lake, not out past its irregular rendered mesh.
                Vector3 inward = lakeCenter - bank; inward.y = 0; inward.Normalize();
                hero.position = bank - Vector3.Cross(inward, Vector3.up) * 10;
                if (world.NavigationGround(hero.position, out var heroGround)) hero.position = heroGround;
                Physics.SyncTransforms();
                Vector3 eye = bank + Vector3.up * 1.65f;
                view.fieldOfView = 50; view.transform.position = eye;
                view.transform.LookAt(new Vector3(eye.x + inward.x * 28, lakeVolume.WaterSurfaceY + .5f, eye.z + inward.z * 28));
                Log("CAPTURE shoreline real bank " + bank.ToString("F3") + " camera " + eye.ToString("F3") + " towards actual lake centre " + lakeCenter.ToString("F3"));
            }
            yield return null; yield return new WaitForEndOfFrame();
            byte[] image = Capture(view, frames + "/" + frame.ToString("D4") + ".png");
            CaptureAudioFrame();
            if (frame == 20) File.Copy(frames + "/0020.png", Output + "/birds-quiet.png", true);
            if (frame == 110) File.Copy(frames + "/0110.png", Output + "/birds-startled.png", true);
            if (frame == 130) { butterflyA = image; File.Copy(frames + "/0130.png", Output + "/butterflies.png", true); }
            if (frame == 175) Need(ChangedPixels(butterflyA, image) > 20, "actual fixed-camera flower/butterfly frames change naturally over time");
            if (frame == 220) File.Copy(frames + "/0220.png", Output + "/shoreline.png", true);
            NeedBudget();
        }
        EndAudioCapture();
        Need(nature.LakeMix > .5f && nature.ActiveSourceCount >= 1, "near shoreline actual lake mix and nature voices are active");
        Need(errors == 0, "no wildlife, wind or nature runtime/shader errors during focused validation");
        Log("CAPTURE 240 actual Unity 1280x720 frames, fixed simulation 30 fps / eight seconds: quiet bird 0–2s, real proximity startle 2–4s, butterflies 4–6s, shoreline 6–8s. Controlled QA camera and Hero relocations; not human Gameplay or a performance benchmark.");
    }

    static IEnumerator PauseCheck(Action open, Action close, Func<bool> opened, string label)
    {
        open(); yield return null; yield return new WaitForEndOfFrame();
        Need(opened() && wind.IsSuspended && !wildlife.SimulationRunning && nature.IsSuspended, label + " suspends all three life systems");
        float w = wind.Elapsed, l = wildlife.Elapsed, n = nature.Elapsed;
        yield return new WaitForSecondsRealtime(.24f);
        Need(Mathf.Abs(wind.Elapsed - w) < .00001f && Mathf.Abs(wildlife.Elapsed - l) < .00001f && Mathf.Abs(nature.Elapsed - n) < .00001f,
            label + " freezes wind, wildlife and audio clocks exactly");
        close(); yield return Wait(() => Running, 10, label + " closes and all systems resume");
    }
    static bool Running => wind && wind.SimulationRunning && wildlife && wildlife.SimulationRunning && nature && !nature.IsSuspended;
    static void AimReviewCamera(Camera view, Vector3 target, Vector3 offset)
    {
        Vector3 best = target + offset; int fewest = int.MaxValue;
        foreach (float lift in new[] { 0f, .6f, 1.2f })
            foreach (float yaw in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 180f })
            {
                Vector3 candidate = target + Quaternion.Euler(0, yaw, 0) * offset + Vector3.up * lift;
                Vector3 ray = candidate - target; float length = ray.magnitude;
                var cast = new Ray(target, ray / length);
                int blocked = Physics.SphereCastAll(target, .06f, ray / length, length, ~0, QueryTriggerInteraction.Ignore)
                    .Count(hit => hit.distance > .15f && !hit.collider.transform.IsChildOf(hero) && !hit.collider.transform.IsChildOf(WildlifeRoot()));
                foreach (var bounds in reviewOccluders)
                    if (bounds.IntersectRay(cast, out float distance) && distance > .08f && distance < length + .08f) blocked++;
                if (blocked < fewest) { fewest = blocked; best = candidate; }
                if (blocked == 0) { view.transform.position = candidate; view.transform.LookAt(target); return; }
            }
        view.transform.position = best; view.transform.LookAt(target);
    }
    static Vector3 FindReviewBank(WaterVolume water, Vector3 centre)
    {
        foreach (var edge in nature.waterAnchors.OrderBy(p => (p - wildlife.birdAnchors[3]).sqrMagnitude))
            foreach (float radius in new[] { 2f, 4f, 6f, 9f })
                for (int i = 0; i < 16; i++)
                {
                    Vector3 point = edge + new Vector3(Mathf.Cos(i * Mathf.PI / 8), 0, Mathf.Sin(i * Mathf.PI / 8)) * radius;
                    if (water.ContainsSurfaceXZ(point) || !world.NavigationGround(point, out var ground) || ground.y > water.WaterSurfaceY + 6) continue;
                    Vector3 inward = centre - ground; inward.y = 0; inward.Normalize();
                    if (!water.ContainsSurfaceXZ(ground + inward * 5)) continue;
                    // Foreground line must be lake/terrain, not inside a wall.
                    Vector3 eye = ground + Vector3.up * 1.65f;
                    if (Physics.CheckSphere(eye, .3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    return ground;
                }
        // Existing lake-island bird ground is authored dry floor; it is a safe
        // review platform even if none of the sparse outer edge samples qualify.
        Need(world.NavigationGround(wildlife.birdAnchors[3], out var fallback), "lake review has real dry authored ground");
        return fallback;
    }
    static Transform WildlifeRoot() => world.transform.Find("Nearby birds and butterflies (runtime)");
    static int WorldLifeRootCount() => world.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Nearby birds and butterflies (runtime)");
    static bool IsNatureSource(AudioSource source) => source && source.name.EndsWith("(runtime)", StringComparison.Ordinal) &&
        (source.name.StartsWith("Nearby lake lapping", StringComparison.Ordinal) || source.name.StartsWith("Regional tree breeze", StringComparison.Ordinal) || source.name.StartsWith("Spatial bird voice", StringComparison.Ordinal));
    static void NeedBudget()
    {
        if (wildlife.ActiveBirdCount > 6 || wildlife.ActiveButterflyCount > 8 || nature.ActiveSourceCount > 5)
            throw new InvalidOperationException("Life layer exceeds its fixed population/audio budget.");
    }

    static void BeginAudioCapture()
    {
        try
        {
            audioRecording = AudioRenderer.Start();
            if (!audioRecording) { Log("AUDIO LIMITATION offline renderer already owned or unsupported; no synthetic replacement audio"); return; }
            var config = AudioSettings.GetConfiguration(); audioRate = config.sampleRate;
            audioChannels = config.speakerMode == AudioSpeakerMode.Mono ? 1 : config.speakerMode == AudioSpeakerMode.Quad ? 4 :
                config.speakerMode == AudioSpeakerMode.Surround ? 5 : config.speakerMode == AudioSpeakerMode.Mode5point1 ? 6 :
                config.speakerMode == AudioSpeakerMode.Mode7point1 ? 8 : 2;
            pcm = new MemoryStream(); pcmWriter = new BinaryWriter(pcm); audioEnergy = 0; audioSampleCount = 0;
            Log("AUDIO real Unity offline output recording started " + audioRate + " Hz / " + audioChannels + " channels");
        }
        catch (Exception e) { Log("AUDIO LIMITATION offline renderer unavailable " + e.Message); audioRecording = false; }
    }
    static void CaptureAudioFrame()
    {
        if (!audioRecording) return;
        try
        {
            int count = AudioRenderer.GetSampleCountForCaptureFrame() * audioChannels;
            if (count <= 0) return;
            using (var samples = new NativeArray<float>(count, Allocator.Temp))
            {
                if (!AudioRenderer.Render(samples)) { Log("AUDIO LIMITATION offline frame render failed"); return; }
                foreach (float value in samples)
                { pcmWriter.Write((short)Mathf.RoundToInt(Mathf.Clamp(value, -1, 1) * 32767)); audioEnergy += value * value; audioSampleCount++; }
            }
        }
        catch (Exception e) { Log("AUDIO LIMITATION " + e.Message); EndAudioCapture(); }
    }
    static void EndAudioCapture()
    {
        if (audioRecording) { AudioRenderer.Stop(); audioRecording = false; }
        if (pcm == null) return;
        byte[] bytes = pcm.ToArray();
        using (var writer = new BinaryWriter(File.Create(Output + "/OfflineNature.wav")))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)audioChannels);
            writer.Write(audioRate); writer.Write(audioRate * audioChannels * 2); writer.Write((short)(audioChannels * 2)); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes.Length); writer.Write(bytes);
        }
        double rms = Math.Sqrt(audioEnergy / Math.Max(1, audioSampleCount));
        Log("AUDIO actual recorded output " + audioSampleCount + " interleaved samples; RMS=" + rms.ToString("F7") +
            (rms > .000001 ? "; audible nature energy present" : "; LIMITATION output silent, requires alternate live audio capture"));
        pcmWriter.Dispose(); pcm.Dispose(); pcm = null; pcmWriter = null;
    }

    static byte[] Capture(Camera camera, string path)
    {
        var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previous = camera.targetTexture; var active = RenderTexture.active; float aspect = camera.aspect;
        try
        {
            camera.targetTexture = target; camera.aspect = 16f / 9f; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG()); return texture.GetRawTextureData().ToArray();
        }
        finally { camera.targetTexture = previous; camera.aspect = aspect; RenderTexture.active = active; Object.Destroy(texture); RenderTexture.ReleaseTemporary(target); }
    }
    static int ChangedPixels(byte[] a, byte[] b)
    {
        if (a == null || a.Length != b.Length) return 0;
        int count = 0;
        for (int p = 0; p < a.Length; p += 3) if (Math.Abs(a[p] - b[p]) + Math.Abs(a[p + 1] - b[p + 1]) + Math.Abs(a[p + 2] - b[p + 2]) > 10) count++;
        return count;
    }
    static IEnumerator Wait(Func<bool> ready, float seconds, string message)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!ready() && Time.realtimeSinceStartup < until) { Focus(); yield return null; }
        Need(ready(), message);
    }
    static IEnumerator Guard(IEnumerator routine)
    {
        bool passed = true; var stack = new Stack<IEnumerator>(); stack.Push(routine);
        while (stack.Count > 0)
        {
            bool more; object item = null;
            try { more = stack.Peek().MoveNext(); if (more) item = stack.Peek().Current; }
            catch (Exception error) { passed = false; Log("FAIL " + error); break; }
            if (!more) { stack.Pop(); continue; }
            if (item is IEnumerator nested) stack.Push(nested); else yield return item;
        }
        EndAudioCapture(); Time.captureDeltaTime = originalCaptureDelta; Application.logMessageReceived -= CheckLog;
        SessionState.SetBool(Prefix + "passed", passed); EditorApplication.ExitPlaymode();
    }
    static void CheckLog(string text, string trace, LogType kind)
    {
        if ((kind == LogType.Error || kind == LogType.Exception) &&
            new[] { "WorldSmallWildlife", "WildlifeWing", "RegionalNatureAudio", "WorldVegetationWind", "LivingVegetation" }.Any(t => text.Contains(t) || trace.Contains(t))) errors++;
    }
    static void Watch()
    {
        if (!SessionState.GetBool(Prefix + "active", false) || !long.TryParse(SessionState.GetString(Prefix + "deadline", ""), out long end) || DateTime.UtcNow.Ticks < end) return;
        report = Output + "/runtime-validation.txt"; Log("FAIL overall 480-second Play watchdog timeout");
        EndAudioCapture(); Time.captureDeltaTime = originalCaptureDelta; SessionState.SetBool(Prefix + "passed", false);
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else { SessionState.SetBool(Prefix + "active", false); EditorApplication.Exit(1); }
    }
    static void Focus()
    {
        var type = Type.GetType("UnityEditor.GameView,UnityEditor"); if (type != null) EditorWindow.GetWindow(type)?.Focus();
        if (Application.isPlaying) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }
    static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
    static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Log("PASS " + message); }
    static void Log(string message) { File.AppendAllText(report, message + "\n"); Debug.Log("WORLD_LIFE_QA " + message); }
}
