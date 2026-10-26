using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using KingOfGraphic.Hero;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Focused ambient-life verification on the existing authored world. No source
// animation sampling, scene saves, prefab edits or combat timing claims.
[InitializeOnLoad]
public static class WorldLifeCreatureChecks
{
    const string Prefix = "QA.WorldLifeCreature.";
    const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    static string Output => Path.Combine(Path.GetDirectoryName(Application.dataPath), "LocalDocs/Environment/WorldLife-20261024");
    static string Report => Path.Combine(Output, "idle-runtime-validation.txt");
    [Serializable] class Saved { public string key, value; public bool exists; }
    [Serializable] class Snapshot { public Saved[] entries; }
    static BaselineFlow flow;
    static BaselineWorld world;
    static Transform hero;
    static PlayerMovement movement;
    static PlayerVitals vitals;
    static HeroWorldAdapter adapter;
    static Health hp;
    static AdventureCreatureAI actor;
    static int missingActions;
    static readonly Vector3 Far = new Vector3(1000, 100, 1000);

    static WorldLifeCreatureChecks()
    { EditorApplication.playModeStateChanged += Changed; EditorApplication.update += Watch; }

    public static void RunPlay()
    {
        if (!Application.dataPath.StartsWith("/private/tmp/adventure-trio-", StringComparison.Ordinal))
            throw new InvalidOperationException("World life checks require the disposable clone.");
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Native ambient checks require an idle GUI Unity editor.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Unsaved scene edits; no changes discarded.");
        EditorSceneManager.OpenScene(ScenePath);
        var host = Object.FindFirstObjectByType<BaselineFlow>();
        var saved = new List<Saved>();
        foreach (string prefix in new HashSet<string> { host.saveKeyPrefix, BaselineSaveStore.DefaultKeyPrefix })
            foreach (string suffix in new[] { ".primary", ".backup" })
            { string key = prefix + suffix; saved.Add(new Saved { key = key, exists = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") }); }
        SessionState.SetString(Prefix + "saved", JsonUtility.ToJson(new Snapshot { entries = saved.ToArray() }));
        SessionState.SetString(Prefix + "scene", Hash(ScenePath));
        SessionState.SetString(Prefix + "prefix", Prefix + Guid.NewGuid().ToString("N"));
        SessionState.SetString(Prefix + "deadline", DateTime.UtcNow.AddSeconds(360).Ticks.ToString());
        SessionState.SetBool(Prefix + "background", Application.runInBackground);
        SessionState.SetFloat(Prefix + "captureDelta", Time.captureDeltaTime);
        SessionState.SetFloat(Prefix + "timeScale", Time.timeScale);
        SessionState.SetBool(Prefix + "passed", false);
        SessionState.SetBool(Prefix + "active", true);
        host.saveKeyPrefix = SessionState.GetString(Prefix + "prefix", "");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Report, "Actual authored-world Unity Play / native ambient animations. Hero stays far away during idle captures; only camera changes. Controlled Health events are labelled. This focused suite does not claim a full combat regression.\n");
        Application.runInBackground = true; Time.captureDeltaTime = 0; Time.timeScale = 1;
        EditorApplication.isPaused = false;
        EditorApplication.ExecuteMenuItem("Window/General/Game"); FocusStartup(); EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Prefix + "active", false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            Application.runInBackground = true; missingActions = 0;
            Application.logMessageReceived += CheckActionError;
            flow = Object.FindFirstObjectByType<BaselineFlow>();
            flow.saveKeyPrefix = SessionState.GetString(Prefix + "prefix", "");
            flow.StartCoroutine(Guard(Tests()));
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            bool passed = SessionState.GetBool(Prefix + "passed", false);
            try
            {
                var snapshot = JsonUtility.FromJson<Snapshot>(SessionState.GetString(Prefix + "saved", ""));
                foreach (var entry in snapshot.entries)
                    if (entry.exists != PlayerPrefs.HasKey(entry.key) || entry.value != PlayerPrefs.GetString(entry.key, ""))
                    {
                        passed = false; Log("FAIL original save changed; restoring " + entry.key);
                        if (entry.exists) PlayerPrefs.SetString(entry.key, entry.value); else PlayerPrefs.DeleteKey(entry.key);
                    }
                if (Hash(ScenePath) != SessionState.GetString(Prefix + "scene", "")) { passed = false; Log("FAIL scene bytes changed."); }
                BaselineSaveStore.Clear(SessionState.GetString(Prefix + "prefix", "")); PlayerPrefs.Save();
            }
            catch (Exception error) { passed = false; Log("FAIL cleanup " + error); }
            Application.runInBackground = SessionState.GetBool(Prefix + "background", false);
            Time.captureDeltaTime = SessionState.GetFloat(Prefix + "captureDelta", 0);
            Time.timeScale = SessionState.GetFloat(Prefix + "timeScale", 1);
            SessionState.SetBool(Prefix + "active", false);
            Log(passed ? "FINAL PASS; original save keys and authored scene bytes unchanged; no scene/prefab saved." : "FINAL FAIL");
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }

    static IEnumerator Tests()
    {
        yield return null; flow.NewGame(); if (flow.Tutorial) flow.Tutorial.Skip(); flow.SetMenu(false);
        GameSession.Instance.GrantSword(); world = flow.GetComponent<BaselineWorld>(); hero = world.player;
        movement = hero.GetComponent<PlayerMovement>(); adapter = hero.GetComponent<HeroWorldAdapter>();
        vitals = hero.GetComponent<PlayerVitals>(); hp = hero.GetComponent<Health>();
        yield return null; yield return null;
        yield return Wait(() => !flow.BlockWorldInput && Time.timeScale > 0 && !movement.HasExternalInputLock, 10, "opening releases gameplay locks");
        float readyUntil = Time.realtimeSinceStartup + 10;
        while ((!adapter.pose || !adapter.pose.Ready || !adapter.AcceptInput || !vitals.EnemyCombatActive) && Time.realtimeSinceStartup < readyUntil)
        { FocusStartup(); yield return null; }
        Need(adapter.pose && adapter.pose.Ready && adapter.AcceptInput && vitals.EnemyCombatActive, "real focused Hero and incoming-hit gates ready");
        if (Camera.main && Camera.main.GetComponent<ThirdPersonCamera>()) Camera.main.GetComponent<ThirdPersonCamera>().enabled = false;
        Disable<StoneScoutRoadsideAI>(); Disable<DomainPrototypeEnemy>(); Disable<RailBeetleAI>(); Disable<GelBlobAI>(); Disable<RepairWispAI>();
        var authored = Object.FindObjectsByType<AdventureCreatureAI>(FindObjectsSortMode.None);
        Need(authored.Length == 3, "exactly three native authored creature types");
        foreach (var item in authored) item.enabled = false;
        foreach (var native in authored.OrderBy(item => item.GetType().Name))
        {
            actor = native; Place(Far); actor.ResetForJourney(); actor.enabled = true;
            string name = actor.GetType().Name;
            int swings = actor.Swings, hits = actor.Hits;
            yield return Wait(() => actor.AmbientIdleActive && actor.AmbientIdleCount > 0, 22, name + " autonomously starts a native ambient action");
            Need(actor.animator.runtimeAnimatorController.animationClips.Any(clip => clip.name == actor.CurrentAction), name + " ambient action exists in its actual controller");
            Need(actor.CurrentAction == (actor is SmilingMushroomAI ? "IdlePlant" : "Taunting"), name + " uses intended native safe ambient clip");
            Need(actor.Swings == swings && actor.Hits == hits && !actor.HitWindow && !actor.WarningVisible, name + " idle opens no damage window or telegraph");
            if (actor is BishopKnightAI knight) Need(!knight.ShieldRaised && !knight.TerritoryEngaged, "knight ambient flourish is not gameplay guard or territory engagement");
            if (actor is SmilingMushroomAI mushroom) Need(mushroom.IsDisguised && mushroom.StateName == "Plant", "mushroom remains planted and disguised throughout its native breathing variation");
            yield return Wait(() => actor.AmbientIdleAge >= .6f, 2, name + " native ambient pose advances to capture time");
            Capture(actor, name + "-idle.png");
            Need(Vector3.Distance(hero.position, Far) < .1f && !actor.Awareness.Visible && !actor.Awareness.Engaged,
                name + " screenshot keeps real Hero distant and undetected");

            // Esc menu handler is genuine; opening and closing animations retain
            // their normal modal gates. Sample after its first suspension tick.
            flow.SetMenu(true); Need(flow.MenuOpen, name + " real Esc menu opens");
            yield return null; yield return new WaitForEndOfFrame();
            float age = actor.AmbientIdleAge, animTime = actor.CurrentActionTime;
            Vector3 position = actor.transform.position;
            yield return new WaitForSecondsRealtime(.24f);
            Need(!vitals.EnemyCombatActive && actor.AmbientIdleActive && Mathf.Abs(actor.AmbientIdleAge - age) < .001f &&
                Mathf.Abs(actor.CurrentActionTime - animTime) < .002f && Vector3.Distance(position, actor.transform.position) < .002f,
                name + " menu freezes ambient clock, native pose and root position without canceling life state");
            flow.SetMenu(false);
            yield return Wait(() => vitals.EnemyCombatActive && Time.timeScale > 0, 6, name + " menu close restores world gates");
            yield return Wait(() => actor.AmbientIdleCompleted > 0, 4, name + " interrupted-by-menu action resumes and completes normally");
            Need(!actor.AmbientIdleActive && actor.Swings == swings && actor.Hits == hits,
                name + " completion returns to native idle/patrol without a strike");

            yield return BeginIdle(name + " perception case");
            Place(PerceptionApproach(actor));
            var heroShape = hero.GetComponent<CharacterController>();
            Need(ClearApproachSight(actor, actor.GetComponent<CapsuleCollider>().bounds.center, heroShape.bounds.center),
                name + " actual placed Hero has clear native collider-centre sight line");
            yield return Wait(() => actor.Awareness.Curious || actor.Awareness.Engaged, 5, name + " actual nearby Hero creates native suspicion/alert");
            Need(!actor.AmbientIdleActive && actor.AmbientIdleInterrupted > 0, name + " native perception interrupts ambient action before brain response");

            yield return BeginIdle(name + " accepted-hit case");
            Need(Hurt(1), name + " controlled 1 HP incoming Health event accepted while idling");
            Need(!actor.AmbientIdleActive && actor.AmbientIdleInterrupted > 0 && !actor.IsDead,
                name + " accepted damage cancels ambient state immediately");

            yield return BeginIdle(name + " death case");
            Need(Hurt(10000) && actor.IsDead && !actor.AmbientIdleActive, name + " controlled lethal Health event cancels ambient state and starts native death");
            actor.ResetForJourney();
            Need(!actor.IsDead && !actor.AmbientIdleActive && actor.AmbientIdleCount == 0,
                name + " journey reset restores HP and clears ambient lifecycle counters");
            yield return Wait(() => actor.AmbientIdleActive, 22, name + " fresh native idle is available after death/reset");
            actor.ResetForJourney();
            Need(!actor.AmbientIdleActive && actor.AmbientIdleAge == 0 && actor.AmbientIdleCount == 0,
                name + " reset during an active variation cancels it immediately");
            actor.enabled = false;
            Need(!actor.AmbientIdleActive, name + " component disable leaves no active ambient state");
        }
        Need(missingActions == 0, "no missing native action errors during all ambient lifecycle cases");
    }

    static IEnumerator BeginIdle(string context)
    {
        Place(Far); actor.ResetForJourney();
        yield return Wait(() => actor.AmbientIdleActive, 22, context + " starts autonomously");
    }
    static bool Hurt(int damage) => actor.GetComponent<Health>().TryApplyDamage(new DamageInfo(damage,
        hero.gameObject, actor.transform.position + Vector3.up, actor.transform.position - hero.position));
    static Vector3 PerceptionApproach(AdventureCreatureAI native)
    {
        var capsule = hero.GetComponent<CharacterController>();
        Vector3 scale = hero.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float half = Mathf.Max(0, capsule.height * Mathf.Abs(scale.y) * .5f - radius);
        Vector3 sightFrom = native.GetComponent<CapsuleCollider>().bounds.center;
        int wetOrBlocked = 0, outside = 0, noSight = 0;
        // Actual authored ground and Hero capsule clearance, then the same
        // collider-centre line as AdventureCreatureAI.HasSight. Never put the
        // player inside the boulder beside the fishman's random patrol facing.
        foreach (float distance in new[] { 1.6f, 2f, 2.5f, 3f, 3.75f, 4.5f, 5.25f })
            foreach (float yaw in new[] { 0f, 20f, -20f, 40f, -40f, 58f, -58f })
            {
                if (distance >= native.noticeRange * .94f) continue;
                Vector3 query = native.transform.position + Quaternion.Euler(0, yaw, 0) * native.transform.forward * distance;
                if (!world.NavigationGround(query, out Vector3 point)) { wetOrBlocked++; continue; }
                Vector3 homeDelta = point - native.Home;
                if (Mathf.Abs(homeDelta.y) >= 2.8f || Vector3.ProjectOnPlane(homeDelta, Vector3.up).magnitude >= native.leashRange - .1f)
                { outside++; continue; }
                Vector3 centre = point + Vector3.Scale(capsule.center, scale);
                bool occupied = Physics.OverlapCapsule(centre - Vector3.up * half, centre + Vector3.up * half,
                    Mathf.Max(.05f, radius - .025f), ~0, QueryTriggerInteraction.Ignore)
                    .Any(c => !c.transform.IsChildOf(hero));
                if (occupied) { wetOrBlocked++; continue; }
                if (!ClearApproachSight(native, sightFrom, centre)) { noSight++; continue; }
                Log("APPROACH " + native.GetType().Name + " actualGround=" + point.ToString("F3") +
                    " nativePosition=" + native.transform.position.ToString("F3") + " yaw=" + yaw + " distance=" + distance +
                    " clearColliderCentreRay=true; rejected occupied/wet=" + wetOrBlocked + " outside=" + outside + " occluded=" + noSight);
                return point;
            }
        throw new InvalidOperationException(native.GetType().Name + " has no grounded, capsule-clear, visible front approach: occupied/wet=" +
            wetOrBlocked + ", outside=" + outside + ", occluded=" + noSight + ", native=" + native.transform.position + ", forward=" + native.transform.forward);
    }
    static bool ClearApproachSight(AdventureCreatureAI native, Vector3 from, Vector3 to)
    {
        Vector3 ray = to - from;
        return !Physics.RaycastAll(from, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore)
            .Any(hit => !hit.collider.transform.IsChildOf(native.transform) && !hit.collider.transform.IsChildOf(hero));
    }
    static void Place(Vector3 position)
    {
        hp.RestoreFullHealth(); adapter.ResetForRespawn(); movement.enabled = true;
        movement.Relocate(position, Quaternion.identity); vitals.Refill(); movement.enabled = false; Physics.SyncTransforms();
    }
    static void Disable<T>() where T : Behaviour
    { foreach (var item in Object.FindObjectsByType<T>(FindObjectsSortMode.None)) item.enabled = false; }
    static IEnumerator Wait(Func<bool> condition, float seconds, string context)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Need(condition(), context + (actor ? " [" + actor.StateName + ", " + actor.CurrentAction + ", ambient=" + actor.AmbientIdleAge.ToString("F2") + ", awareness=" + actor.Awareness.State + "]" : ""));
    }
    static void Capture(AdventureCreatureAI native, string file)
    {
        var camera = Camera.main; Need(camera, "actual world camera available for " + file);
        float distance = native is BishopKnightAI ? 7.8f : native is SmilingMushroomAI ? 3.1f : 4.8f;
        var body = native.GetComponentsInChildren<SkinnedMeshRenderer>()
            .FirstOrDefault(r => r.enabled && r.sharedMesh && !r.gameObject.name.Contains("patch"));
        Bounds subject = body ? body.bounds : native.GetComponent<CapsuleCollider>().bounds;
        Vector3 focus = subject.center;
        float height = Mathf.Max(.4f, subject.size.y);
        var obstacles = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r => r.enabled && !r.transform.IsChildOf(native.transform) && !r.transform.IsChildOf(hero) &&
                !r.GetComponentInParent<Health>() && r.bounds.size.y > .25f && r.bounds.size.y < 30 &&
                r.bounds.size.x < 30 && r.bounds.size.z < 30 &&
                !r.sharedMaterials.Any(m => m && m.shader &&
                    (m.shader.name.Contains("Vegetation") || m.shader.name.Contains("Grass"))))
            .Select(r => r.bounds).ToArray();
        var targets = new[] { focus, focus + Vector3.up * height * .28f, focus - Vector3.up * height * .28f };
        Vector3 initial = native.transform.forward * distance + native.transform.right * distance * .45f + Vector3.up * distance * .18f;
        int bestVisible = -1;
        Vector3 best = focus + initial;
        bool clear = false;
        // Camera-only orbit. Static renderer bounds catch decorative boulders
        // without colliders; physical rays catch terrain and actual walls.
        foreach (float scale in new[] { 1f, .75f, 1.25f })
        {
            if (clear) break;
            foreach (float lift in new[] { 0f, height * .45f, height * .9f })
            {
                if (clear) break;
                foreach (float yaw in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 180f })
                {
                    Vector3 candidate = focus + Quaternion.Euler(0, yaw, 0) * initial * scale + Vector3.up * lift;
                    int visible = 0;
                    foreach (Vector3 point in targets)
                    {
                        Vector3 ray = candidate - point; float length = ray.magnitude;
                        bool blocked = Physics.SphereCastAll(point, .04f, ray / length, length, ~0, QueryTriggerInteraction.Ignore)
                            .Any(hit => hit.distance > .08f && !hit.collider.transform.IsChildOf(native.transform) && !hit.collider.transform.IsChildOf(hero));
                        var cast = new Ray(point, ray / length);
                        if (!blocked) foreach (var bounds in obstacles)
                            if (bounds.IntersectRay(cast, out float along) && along > .04f && along < length + .1f)
                            { blocked = true; break; }
                        if (!blocked) visible++;
                    }
                    if (visible > bestVisible) { bestVisible = visible; best = candidate; }
                    if (visible == targets.Length) { clear = true; break; }
                }
            }
        }
        Need(bestVisible >= 2, file + " review camera has unobstructed view to at least two body landmarks (" + bestVisible + "/3)");
        camera.transform.position = best; camera.transform.LookAt(focus); camera.fieldOfView = 38;
        camera.nearClipPlane = .03f;
        camera.aspect = 16f / 9f;
        Need(targets.All(point => { var view = camera.WorldToViewportPoint(point); return view.z > camera.nearClipPlane && view.x > .04f && view.x < .96f && view.y > .04f && view.y < .96f; }),
            file + " actual actor body landmarks fit within camera frame");
        Log("CAMERA " + file + " camera=" + best.ToString("F3") + " target=" + focus.ToString("F3") + " visible=" + bestVisible + "/3; original actor/root/Hero positions preserved");
        var target = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; float oldAspect = camera.aspect;
        try
        {
            camera.targetTexture = target; camera.aspect = 16f / 9f; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(Output, file), texture.EncodeToPNG());
            Log("CAPTURE actual authored-map native " + native.CurrentAction + " at ambient age " + native.AmbientIdleAge.ToString("F3") + ": " + file);
        }
        finally { camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
    }
    static void FocusStartup()
    {
        var type = Type.GetType("UnityEditor.GameView,UnityEditor"); if (type != null) EditorWindow.GetWindow(type)?.Focus();
        if (Application.isPlaying) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }
    static IEnumerator Guard(IEnumerator tests)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(tests); bool passed = true;
        while (stack.Count > 0)
        {
            object item = null; bool more;
            try { more = stack.Peek().MoveNext(); if (more) item = stack.Peek().Current; }
            catch (Exception error) { passed = false; Log("FAIL " + error); break; }
            if (!more) { stack.Pop(); continue; }
            if (item is IEnumerator nested) stack.Push(nested); else yield return item;
        }
        Application.logMessageReceived -= CheckActionError;
        SessionState.SetBool(Prefix + "passed", passed); EditorApplication.ExitPlaymode();
    }
    static void CheckActionError(string text, string trace, LogType kind)
    { if (kind == LogType.Error && text.Contains("Missing native creature action")) missingActions++; }
    static void Watch()
    {
        if (!SessionState.GetBool(Prefix + "active", false) || !long.TryParse(SessionState.GetString(Prefix + "deadline", ""), out long end) || DateTime.UtcNow.Ticks < end) return;
        Log("FAIL overall idle Play watchdog timeout"); SessionState.SetBool(Prefix + "passed", false);
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else { SessionState.SetBool(Prefix + "active", false); EditorApplication.Exit(1); }
    }
    static string Hash(string path) { using (var digest = SHA256.Create()) return Convert.ToBase64String(digest.ComputeHash(File.ReadAllBytes(path))); }
    static void Need(bool success, string message) { if (!success) throw new InvalidOperationException(message); Log("PASS " + message); }
    static void Log(string message) { File.AppendAllText(Report, message + "\n"); Debug.Log("WORLD_LIFE_CREATURE_QA " + message); }
}
