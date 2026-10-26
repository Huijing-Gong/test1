using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Isolated authoring QA. Capture paths move the real Hero through an authored
// flower patch; these controlled captures are not recordings of human gameplay.
[InitializeOnLoad]
public static class LivingEnvironmentChecks
{
    const string Prefix = "QA.LivingEnvironment.";
    const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    const string Output = "LocalDocs/Environment/VegetationContactFix-20261024";
    static string report;
    [Serializable] class Saved { public string key, value; public bool exists; }
    [Serializable] class Snapshot { public Saved[] entries; }
    static BaselineFlow flow;
    static BaselineWorld world;
    static Transform hero;
    static WorldVegetationWind wind;
    static float originalCaptureDelta;
    static int errors;

    static LivingEnvironmentChecks()
    { EditorApplication.playModeStateChanged += Changed; EditorApplication.update += Watch; }

    static void Isolated()
    {
        if (!Application.dataPath.StartsWith("/private/tmp/adventure-trio-knight-", StringComparison.Ordinal))
            throw new InvalidOperationException("Living environment QA runs only in its isolated project.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(s).isDirty)
                throw new InvalidOperationException("Unsaved scene edits; QA did not discard them.");
        Directory.CreateDirectory(Output);
    }

    public static void RunStatic()
    {
        Isolated(); report = Output + "/static-validation.txt";
        File.WriteAllText(report, "Actual authored vegetation static validation. No scenes or source assets are saved.\n");
        try
        {
            string sceneHash = Hash(ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            var hosts = Object.FindObjectsByType<WorldVegetationWind>(FindObjectsSortMode.None);
            Need(hosts.Length == 1 && hosts[0].GetComponent<BaselineWorld>() && hosts[0].GetComponent<BaselineFlow>(), "one native world-owned wind host");
            var shader = Shader.Find("FoldedRealm/LivingVegetation");
            Need(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "vegetation shader compiles without shader errors");
            Need(shader.passCount >= 4, "vegetation has forward, shadow and depth passes");
            var checkedMeshes = new HashSet<Mesh>();
            int instanceCount = 0, batches = 0;
            foreach (var meadow in Object.FindObjectsByType<OwnInstancedMeadow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var batch in meadow.batches)
                {
                    if (!batch.mesh || batch.positions.Length == 0) continue;
                    Need(batch.positions.Length == batch.scales.Length && batch.positions.Length == batch.eulers.Length, "batch placement arrays retain equal lengths: " + meadow.name);
                    if (!batch.material || batch.material.shader != shader) continue;
                    Need(batch.material.enableInstancing, "wind material retains GPU instancing: " + batch.material.name);
                    Need(batch.material.GetFloat("_VertexDataValid") > .5f, "wind material requires prepared geometry: " + batch.material.name);
                    instanceCount += batch.positions.Length; batches++;
                    if (checkedMeshes.Add(batch.mesh)) CheckMesh(batch.mesh);
                    if (batch.farMesh && checkedMeshes.Add(batch.farMesh)) CheckMesh(batch.farMesh);
                }
            Need(instanceCount > 20000 && batches > 20, "actual meadow coverage exceeds 20,000 instances: " + instanceCount + " / " + batches + " batches");
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!renderer.sharedMaterials.Any(m => m && m.shader == shader)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                Need(filter && filter.sharedMesh, "wind renderer has prepared mesh: " + renderer.name);
                Need(!renderer.GetComponentInParent<Health>() && !renderer.GetComponentInParent<AdventureCreatureAI>(), "enemy geometry excluded from plant shader: " + renderer.name);
                string name = renderer.name.ToLowerInvariant();
                Need(!new[] { "banner", "wall", "stone", "bridge", "platform", "terrain", "stair", "crystal" }.Any(name.Contains), "architecture excluded from plant shader: " + renderer.name);
                if (checkedMeshes.Add(filter.sharedMesh)) CheckMesh(filter.sharedMesh);
            }
            var sources = "LocalDocs/Environment/LivingWorld-20261024/source-assets-sha256.txt";
            Need(File.Exists(sources), "installer protected-source manifest exists");
            int protectedCount = 0;
            foreach (string line in File.ReadLines(sources))
            {
                if (line.Length < 67) continue;
                string path = line.Substring(66);
                if (!File.Exists(path) || Hash(path) != line.Substring(0, 64)) throw new InvalidOperationException("Source asset changed: " + path);
                protectedCount++;
            }
            Need(protectedCount > 100, "protected source assets remain byte-identical: " + protectedCount);
            Need(Hash(ScenePath) == sceneHash, "static verification leaves authored scene bytes unchanged");
            File.WriteAllLines(Output+"/asset-manifest.txt",AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/Art/LivingEnvironment/",StringComparison.Ordinal)));
            Log("FINAL PASS");
        }
        catch (Exception error) { Log("FINAL FAIL " + error); throw; }
    }

    static void CheckMesh(Mesh mesh)
    {
        var roots = new List<Vector4>(); var details = new List<Vector4>();
        mesh.GetUVs(2, roots); mesh.GetUVs(3, details);
        Need(roots.Count == mesh.vertexCount && details.Count == mesh.vertexCount, "all vertices have UV2/UV3 wind metadata: " + mesh.name);
        bool pinned = false, moving = false;
        for (int i = 0; i < roots.Count; i++)
        {
            Vector4 root = roots[i], detail = details[i];
            for (int j = 0; j < 4; j++)
                if (!Finite(root[j]) || !Finite(detail[j])) throw new InvalidOperationException("Nonfinite metadata: " + mesh.name);
            bool validStem = detail.w > .5f ? new Vector3(detail.x, detail.y, detail.z).sqrMagnitude > .0000001f :
                detail.y >= 0 && detail.y <= 1 && detail.x > 0;
            if (root.w < -.00001f || root.w > 1.00001f || !validStem)
                throw new InvalidOperationException("Invalid wind metadata weights: " + mesh.name);
            pinned |= root.w <= .00001f; moving |= root.w > .2f;
        }
        Need(pinned && moving, "root anchors are fixed and upper geometry can move: " + mesh.name);
        Need(mesh.normals.Length == mesh.vertexCount, "wind geometry preserves complete normals: " + mesh.name);
        if (details.Any(d => d.w > .5f))
        {
            Need(details.All(d => d.w > .5f), "coherent metadata covers the entire plant: " + mesh.name);
            var groups = roots.Select((r, i) => new { root = new Vector3(r.x, r.y, r.z), index = i })
                .GroupBy(p => p.root).ToArray();
            bool flowers = (mesh.name.ToLowerInvariant().Contains("flower") || mesh.name.ToLowerInvariant().Contains("daisy")) &&
                (mesh.vertexCount == 270 || mesh.vertexCount == 561);
            Need(groups.Length == (flowers ? 3 : 1), "whole botanical roots, never per-petal anchors: " + mesh.name + " / " + groups.Length);
            foreach (var group in groups)
            {
                var first = details[group.First().index];
                Need(group.All(v => Vector3.Distance((Vector3)details[v.index], (Vector3)first) < .00001f),
                    "one botanical span vector per complete plant group, including tilted/scaled instances: " + mesh.name);
            }
        }
    }

    public static void RunPlay()
    {
        Isolated();
        if (Application.isBatchMode) throw new InvalidOperationException("Use GUI Unity for actual rendered vegetation captures.");
        EditorSceneManager.OpenScene(ScenePath);
        var host = Object.FindFirstObjectByType<BaselineFlow>();
        var saved = new List<Saved>();
        foreach (string prefix in new HashSet<string> { host.saveKeyPrefix, BaselineSaveStore.DefaultKeyPrefix })
            foreach (string suffix in new[] { ".primary", ".backup" })
            { string key = prefix + suffix; saved.Add(new Saved { key = key, exists = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") }); }
        SessionState.SetString(Prefix + "saved", JsonUtility.ToJson(new Snapshot { entries = saved.ToArray() }));
        SessionState.SetString(Prefix + "scene", Hash(ScenePath));
        SessionState.SetString(Prefix + "savePrefix", Prefix + Guid.NewGuid().ToString("N"));
        SessionState.SetString(Prefix + "deadline", DateTime.UtcNow.AddSeconds(360).Ticks.ToString());
        SessionState.SetBool(Prefix + "background", Application.runInBackground);
        SessionState.SetBool(Prefix + "passed", false); SessionState.SetBool(Prefix + "active", true);
        host.saveKeyPrefix = SessionState.GetString(Prefix + "savePrefix", "");
        report = Output + "/runtime-validation.txt";
        File.WriteAllText(report, "Actual Unity authored-world wind validation. Controlled camera and Hero path; unrelated enemy AI suspended in memory only. Not human Gameplay or a performance benchmark.\n");
        Application.runInBackground = true; EditorApplication.isPaused = false;
        EditorApplication.ExecuteMenuItem("Window/General/Game"); Focus(); EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Prefix + "active", false)) return;
        report = Output + "/runtime-validation.txt";
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            errors = 0; Application.logMessageReceived += CheckLog;
            originalCaptureDelta = Time.captureDeltaTime;
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
        Log(passed ? "FINAL PASS; authored scene and original saves unchanged; no scene saved" : "FINAL FAIL");
        EditorApplication.Exit(passed ? 0 : 1);
    }

    static IEnumerator Tests()
    {
        yield return null; flow.NewGame(); if (flow.Tutorial) flow.Tutorial.Skip(); flow.SetMenu(false);
        world = flow.GetComponent<BaselineWorld>(); hero = world.player; wind = world.GetComponent<WorldVegetationWind>();
        Need(wind && hero, "actual world wind and Hero are present");
        yield return Wait(() => !flow.BlockWorldInput && wind.SimulationRunning, 18, "opening completes and wind starts");
        var view = Camera.main; Need(view, "native main camera exists");
        var thirdPerson = view.GetComponent<ThirdPersonCamera>(); if (thirdPerson) thirdPerson.enabled = false;
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component is AdventureCreatureAI || component is StoneScoutRoadsideAI || component is DomainPrototypeEnemy ||
                component is RailBeetleAI || component is GelBlobAI || component is RepairWispAI || component is EnemyNavigationWorld)
                component.enabled = false;
        float start = wind.Elapsed; yield return new WaitForSeconds(.25f);
        Need(wind.Elapsed > start + .1f, "wind clock advances with active world simulation");
        flow.SetMenu(true); yield return null; yield return new WaitForEndOfFrame();
        Need(flow.MenuOpen && wind.IsSuspended, "real Esc menu suspends plant simulation");
        start = wind.Elapsed; yield return new WaitForSecondsRealtime(.24f);
        Need(Mathf.Abs(wind.Elapsed - start) < .00001f, "Esc freezes wind clock exactly");
        flow.SetMenu(false); yield return Wait(() => !flow.BlockWorldInput && wind.SimulationRunning, 8, "Esc close resumes plant simulation");
        world.SetJournal(true); yield return null; yield return new WaitForEndOfFrame();
        Need(world.JournalOpen && wind.IsSuspended, "real J journal suspends wind");
        start = wind.Elapsed; yield return new WaitForSecondsRealtime(.24f);
        Need(Mathf.Abs(wind.Elapsed - start) < .00001f, "journal freezes wind clock exactly");
        world.SetJournal(false); yield return Wait(() => !flow.BlockWorldInput && wind.SimulationRunning, 8, "journal close resumes wind");
        Need(flow.ShowMap(), "real M map opens"); yield return null; yield return new WaitForEndOfFrame();
        start = wind.Elapsed; yield return new WaitForSecondsRealtime(.24f);
        Need(flow.MapOpen && wind.IsSuspended && Mathf.Abs(wind.Elapsed - start) < .00001f, "map freezes wind clock exactly");
        flow.SetMenu(false); yield return Wait(() => !flow.BlockWorldInput && wind.SimulationRunning, 8, "map close resumes wind");

        Vector3 originalPosition = hero.position;
        Vector3 patch = FindFlowerPatch(originalPosition);
        Need(world.NavigationGround(patch, out var ground), "selected actual flower patch has nearby authored navigable ground");
        patch.y = ground.y;
        Log("CONTROLLED CAPTURE flower patch " + patch.ToString("F3") + "; mesh placements and shader parameters unchanged");
        var movement = hero.GetComponent<PlayerMovement>(); if (movement) movement.enabled = false;
        var body = hero.GetComponent<CharacterController>(); if (body) body.enabled = false;
        hero.position = patch + new Vector3(0, .1f, -3); Physics.SyncTransforms();
        yield return null; yield return new WaitForEndOfFrame();
        Time.captureFramerate = 30;
        for (int i = 0; i < 9; i++) { hero.position += Vector3.right * .1f; yield return null; yield return new WaitForEndOfFrame(); }
        Need(wind.TrailSamples.Any(s => s.w > .01f), "controlled real-Hero movement creates a local bend trail");
        hero.position += Vector3.right * 12; Physics.SyncTransforms();
        yield return null; yield return new WaitForEndOfFrame();
        Need(wind.TrailSamples.All(s => s.w == 0), "teleport over five metres clears all old trail samples");
        hero.position = patch + new Vector3(0, .1f, -8); Physics.SyncTransforms();
        yield return null; yield return new WaitForEndOfFrame();
        var frames = Output + "/Frames"; Directory.CreateDirectory(frames);
        view.fieldOfView = 48; view.nearClipPlane = .05f; view.farClipPlane = 240;
        view.transform.position = patch + new Vector3(3.4f, 1.7f, -4.5f); view.transform.LookAt(patch + Vector3.up * .35f);
        byte[] first = null; int differentPixels = 0;
        var frameTimes = new List<float>();
        for (int frame = 0; frame < 60; frame++)
        {
            yield return null; yield return new WaitForEndOfFrame();
            byte[] pixels = Capture(view, frames + "/" + frame.ToString("D4") + ".png");
            if (frame == 0) { first = pixels; File.Copy(frames + "/0000.png", Output + "/flowers-time-a.png", true); }
            if (frame == 59)
            {
                for (int p = 0; p < pixels.Length; p += 3)
                    if (Math.Abs(pixels[p] - first[p]) + Math.Abs(pixels[p + 1] - first[p + 1]) + Math.Abs(pixels[p + 2] - first[p + 2]) > 10) differentPixels++;
                File.Copy(frames + "/0059.png", Output + "/flowers-time-b.png", true);
            }
            frameTimes.Add(Time.unscaledDeltaTime);
        }
        Need(differentPixels > 200, "fixed-camera actual flower image changes across two seconds: " + differentPixels + " pixels");

        Vector4 canopy = wind.canopySources.OrderBy(s => (new Vector3(s.x, s.y, s.z) - patch).sqrMagnitude).First();
        Vector3 crown = new Vector3(canopy.x, canopy.y, canopy.z);
        hero.position = crown + new Vector3(5, -2, 0); Physics.SyncTransforms();
        view.transform.position = crown + new Vector3(7, 3, -7); view.transform.LookAt(crown); view.fieldOfView = 52;
        for (int frame = 60; frame < 120; frame++)
        {
            yield return null; yield return new WaitForEndOfFrame();
            Capture(view, frames + "/" + frame.ToString("D4") + ".png");
            if (frame == 60) File.Copy(frames + "/0060.png", Output + "/canopy-time-a.png", true);
            if (frame == 119) File.Copy(frames + "/0119.png", Output + "/canopy-time-b.png", true);
        }
        Need(wind.ActiveParticleCount <= 32, "nearby canopy leaf/petal particles stay within 32-particle budget");
        view.transform.position = patch + new Vector3(1.7f, 1.2f, -2.4f); view.transform.LookAt(patch + Vector3.up * .5f); view.fieldOfView = 50;
        float maxTrail = 0;
        for (int frame = 120; frame < 180; frame++)
        {
            hero.position = patch + Vector3.Lerp(new Vector3(-2, .1f, 0), new Vector3(2, .1f, 0), (frame - 120) / 59f);
            Physics.SyncTransforms(); yield return null; yield return new WaitForEndOfFrame();
            maxTrail = Mathf.Max(maxTrail, wind.TrailSamples.Max(s => s.w));
            Capture(view, frames + "/" + frame.ToString("D4") + ".png");
        }
        Need(maxTrail > .3f && wind.InfluencePosition.w > .5f, "controlled authored-flower pass carries actual player influence and recovering wake");
        // Close enough to reproduce the user's stand-in-flowers case. Neither
        // a 1.6m permanent radial halo nor ongoing full-strength idle pressure.
        hero.position = patch + Vector3.up * .1f; Physics.SyncTransforms();
        for (int frame = 180; frame < 240; frame++)
        {
            yield return null; yield return new WaitForEndOfFrame();
            Capture(view, frames + "/" + frame.ToString("D4") + ".png");
            if (frame == 239) File.Copy(frames + "/0239.png", Output + "/standing-no-halo.png", true);
        }
        Need(wind.ContactMotionStrength < .001f && wind.InfluencePosition.w <= .081f,
            "standing still removes strong passage pressure instead of holding a full-strength radial halo");
        for (int frame = 240; frame < 300; frame++)
        {
            hero.position = patch + new Vector3(Mathf.Clamp01((frame - 240) / 29f) * 2.5f, .1f, 0);
            Physics.SyncTransforms();
            yield return null; yield return new WaitForEndOfFrame();
            Capture(view, frames + "/" + frame.ToString("D4") + ".png");
            if (frame == 299) File.Copy(frames + "/0299.png", Output + "/recovered.png", true);
        }
        yield return new WaitForSeconds(.5f);
        Need(wind.TrailSamples.All(s => s.w < .005f), "stationary player wake fades out naturally after passage");
        Need(wind.interactionRadius <= .75f && wind.interactionRadius >= .6f, "contact range stays close to the body, not a wide proximity field");
        wind.enabled = false; yield return null;
        Need(Shader.GetGlobalVector("_FRWindDirection").w == 0 && Shader.GetGlobalVector("_FRWindPlayer").w == 0,
            "host suspension clears plant globals and player influence for side-world travel");
        wind.enabled = true;
        yield return Wait(() => wind.SimulationRunning, 3, "host re-enable resumes wind ownership");
        hero.position = originalPosition; Physics.SyncTransforms();
        Need(errors == 0, "no plant runtime or shader errors during focused validation");
        Log("CAPTURE 300 real Unity frames at 1280x720, fixed simulation 30 fps: flowers 0–2s, canopy 2–4s, controlled passage 4–6s, stationary Hero 6–8s, recovery 8–10s. No forced shader globals in footage, no plant placement edits. Controlled QA, not human Gameplay or a performance benchmark.");
    }

    static Vector3 FindFlowerPatch(Vector3 origin)
    {
        var choices = new List<Vector3>();
        foreach (var meadow in Object.FindObjectsByType<OwnInstancedMeadow>(FindObjectsSortMode.None))
            foreach (var batch in meadow.batches)
                if (batch.mesh && batch.mesh.name.ToLowerInvariant().Contains("flower") && batch.material && batch.material.shader.name == "FoldedRealm/LivingVegetation")
                    choices.AddRange(batch.positions);
        var solidVisuals=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r=>r.enabled&&r.bounds.size.y>.8f&&r.bounds.size.y<25&&r.bounds.size.x<14&&r.bounds.size.z<14&&
                !r.transform.IsChildOf(hero)&&!r.sharedMaterials.Any(m=>m&&m.shader&&m.shader.name=="FoldedRealm/LivingVegetation"))
            .Select(r=>r.bounds).ToArray();
        foreach (var choice in choices.OrderBy(p => (p - origin).sqrMagnitude))
        {
            if(!world.NavigationGround(choice,out var floor)||Mathf.Abs(floor.y-choice.y)>=.5f)continue;
            bool clear=true;
            foreach(float x in new[]{-1.8f,0,1.8f})
            {
                Vector3 foot=new Vector3(choice.x+x,floor.y,choice.z);
                if(!world.NavigationGround(foot,out var sample)||Mathf.Abs(sample.y-floor.y)>.22f){clear=false;break;}
                if(Physics.OverlapSphere(foot+Vector3.up*1.35f,1.1f,~0,QueryTriggerInteraction.Ignore)
                    .Any(c=>c&&!c.transform.IsChildOf(hero)&&c.bounds.max.y>floor.y+.8f)){clear=false;break;}
                if(solidVisuals.Any(b=>(b.ClosestPoint(foot+Vector3.up*.9f)-(foot+Vector3.up*.9f)).sqrMagnitude<1.1f*1.1f))
                {clear=false;break;}
            }
            if(clear)return new Vector3(choice.x,floor.y,choice.z);
        }
        throw new InvalidOperationException("No real authored flower patch near navigable ground found.");
    }

    static byte[] Capture(Camera camera, string path)
    {
        var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previous = camera.targetTexture; var active = RenderTexture.active; float aspect = camera.aspect;
        try
        {
            camera.targetTexture = target; camera.aspect = 16f / 9f; camera.Render(); RenderTexture.active = target;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG()); return tex.GetRawTextureData().ToArray();
        }
        finally { camera.targetTexture = previous; camera.aspect = aspect; RenderTexture.active = active; Object.Destroy(tex); RenderTexture.ReleaseTemporary(target); }
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
        Time.captureDeltaTime = originalCaptureDelta; Application.logMessageReceived -= CheckLog;
        SessionState.SetBool(Prefix + "passed", passed); EditorApplication.ExitPlaymode();
    }
    static void CheckLog(string text, string trace, LogType kind)
    {
        if ((kind == LogType.Error || kind == LogType.Exception) &&
            (text.Contains("LivingVegetation") || text.Contains("WorldVegetationWind") || trace.Contains("WorldVegetationWind"))) errors++;
    }
    static void Watch()
    {
        if (!SessionState.GetBool(Prefix + "active", false) || !long.TryParse(SessionState.GetString(Prefix + "deadline", ""), out long end) || DateTime.UtcNow.Ticks < end) return;
        report = Output + "/runtime-validation.txt"; Log("FAIL overall 360-second Play watchdog timeout");
        Time.captureDeltaTime = originalCaptureDelta; SessionState.SetBool(Prefix + "passed", false);
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else { SessionState.SetBool(Prefix + "active", false); EditorApplication.Exit(1); }
    }
    static void Focus()
    {
        var type = Type.GetType("UnityEditor.GameView,UnityEditor"); if (type != null) EditorWindow.GetWindow(type)?.Focus();
        if (Application.isPlaying) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Log("PASS " + message); }
    static void Log(string message) { File.AppendAllText(report, message + "\n"); Debug.Log("LIVING_ENVIRONMENT_QA " + message); }
}
