using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Import into Assets/_Demo/Editor only in Edit mode. Explicit disposable-scene API QA.
// No input injection, mouse-click claim, natural traversal or combat-completion claim.
[InitializeOnLoad]
public static class AssetReuseBHeroChecks
{
    const string PrepareState = "QA.OverworldV2Play.", State = "QA.AssetReuseBHeroes.";
    const string MainScene = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    const string Report = "ToolReports/AssetsReuse130/b-heroes.txt", Images = "Docs/AssetsReuse130/";
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static BaselineFlow flow;
    static SideWorldTravel travel;
    static double deadline, resumeAt;
    static int resumeFrame;
    static bool oldBackground;
    public static bool Running { get; private set; }
    public static string Status => SessionState.GetString(State + "status", "Not run");

    static AssetReuseBHeroChecks()
    {
        EditorApplication.playModeStateChanged += ModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (Running) Fail("Assembly reload interrupted QA."); };
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) RestoreAudit(); };
    }

    public static void Begin()
    {
        string prefix = SessionState.GetString(PrepareState + "prefix", "");
        string real = SessionState.GetString(PrepareState + "realPrefix", "");
        if (!EditorApplication.isPlaying || EditorApplication.isPaused || Running || OverworldV2PlayChecks.Running ||
            SideGameBIntegrationChecks.Running || OverworldV2TutorialChecks.Running ||
            OverworldV2CameraChecks.Running || OverworldV2TraversalChecks.Running ||
            OverworldV2WaystationChecks.Running || OverworldV2ReliefChecks.Running ||
            OverworldV2ReliefInteractionChecks.Running || OverworldV2ReliefOpeningInputChecks.Running ||
            OverworldV2ReliefTowerChecks.Running || OverworldV2ReliefProfile.Running ||
            !SessionState.GetBool(PrepareState + "prepared", false) || SceneManager.sceneCount != 1 ||
            SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene ||
            !prefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) || string.IsNullOrEmpty(real) || prefix == real)
            throw new InvalidOperationException("Requires exclusive initialized Play in OverworldV2PlayChecks.Prepare's disposable scene.");
        flow = Object.FindFirstObjectByType<BaselineFlow>(); travel = SideWorldTravel.Instance;
        if (!flow || flow.saveKeyPrefix != prefix || !flow.Tutorial || !travel || travel.Busy || travel.IsAway)
            throw new InvalidOperationException("Missing initialized isolated host; no gameplay calls made.");
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); Directory.CreateDirectory(Images);
        File.WriteAllText(Report,
            "METHOD: isolated Prepare scene/save; two actual SideWorldTravel.Enter/Return API visits. " +
            "Four tutorial pages use controlled private page assignments, then the real CloseTutorial and ApplyChoice methods. " +
            "Screenshots are frame-delayed ScreenCapture Game View outputs, not mouse-click evidence. " +
            "Hero animations are manually sampled while runtime controls and game time are temporarily paused. " +
            "No keyboard/mouse injection, locomotion, damage, natural combat, puzzle or complete playthrough claim.\n");
        SessionState.SetString(State + "prefix", prefix); SessionState.SetString(State + "realPrefix", real);
        SessionState.SetString(State + "primaryHash", SlotHash(real + ".primary"));
        SessionState.SetString(State + "backupHash", SlotHash(real + ".backup"));
        SessionState.SetString(State + "mainHash", Hash(File.ReadAllBytes(MainScene)));
        SessionState.SetBool(State + "restore", true); SessionState.SetBool(State + "complete", false);
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        deadline = EditorApplication.timeSinceStartup + 120; resumeAt = 0; resumeFrame = Time.frameCount;
        Running = true; steps.Clear(); steps.Push(Run());
        SessionState.SetString(State + "status", "Running bounded B hero/resource API checks");
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        EditorApplication.update += Tick;
    }

    static IEnumerator Run()
    {
        flow.NewGame(); yield return .2f;
        flow.Tutorial.Cancel(); yield return .2f;
        Check(!flow.BlockWorldInput && Mathf.Approximately(Time.timeScale, 1), "Disposable host releases tutorial modal before travel");
        yield return Visit(CharacterSelectionUI.HeroChoice.Princess, true);
        yield return Visit(CharacterSelectionUI.HeroChoice.Fighter, false);
        CheckReal();
        SessionState.SetBool(State + "complete", true);
        Record("PASS two-hero API/resource suite complete; awaiting Prepare Edit-mode restoration");
    }

    static IEnumerator Visit(CharacterSelectionUI.HeroChoice hero, bool capturePages)
    {
        travel.Enter(true);
        yield return Until(() => travel && travel.IsAway && !travel.Busy &&
            SceneManager.GetActiveScene().path == SideWorldTravel.SideGameBPath, "Enter B for " + hero, 20);
        yield return .15f;
        var controls = Object.FindFirstObjectByType<GameControlsTutorial>();
        var selector = Object.FindFirstObjectByType<CharacterSelectionUI>();
        Check(controls && selector && controls.isActiveAndEnabled && Read<bool>(controls, "visible") &&
            Read<int>(controls, "page") == 0 && Mathf.Approximately(Time.timeScale, 0), hero + " visit starts at paused tutorial page1");
        Check(SceneManager.GetSceneByPath(OverworldV2PlayChecks.ValidationScene).isLoaded && !flow.gameObject.activeInHierarchy,
            hero + " visit retains suspended QA host");
        if (capturePages)
        {
            var page = Field(typeof(GameControlsTutorial), "page");
            for (int i = 0; i < 4; i++)
            {
                page.SetValue(controls, i);
                yield return Frames(2);
                Check(Read<int>(controls, "page") == i && Read<bool>(controls, "visible") && Mathf.Approximately(Time.timeScale, 0),
                    "Controlled static tutorial card " + (i + 1) + "/4 remains visible and paused; no navigation click claim");
                yield return Capture("b-tutorial-" + (i + 1) + ".png");
            }
            CheckEnemyClips();
        }
        Check(Resources.Load<Sprite>("DashWind") != null, hero + " dynamic Resources Sprite DashWind resolves");
        Check(Resources.Load<Texture2D>("Effects/FighterSlash") != null, hero + " dynamic FighterSlash texture resolves");
        Method(typeof(GameControlsTutorial), "CloseTutorial").Invoke(controls, null);
        Check(!controls.gameObject.activeSelf && Read<bool>(selector, "visible") && Mathf.Approximately(Time.timeScale, 0),
            hero + " CloseTutorial opens paused character selection");
        Method(typeof(CharacterSelectionUI), "ApplyChoice").Invoke(selector, new object[] { hero });
        bool fighter = hero == CharacterSelectionUI.HeroChoice.Fighter;
        var animator = selector.GetComponent<Animator>();
        var princessCombat = selector.GetComponent<PlayerCombat2D>();
        var fighterCombat = selector.GetComponent<FighterMeleeCombat2D>();
        var health = selector.GetComponent<PlayerHealth>();
        var expected = Read<RuntimeAnimatorController>(selector, fighter ? "fighterController" : "princessController");
        Check(animator && expected && animator.runtimeAnimatorController == expected, hero + " selected expected animator controller");
        Check(princessCombat && fighterCombat && princessCombat.enabled == !fighter && fighterCombat.enabled == fighter,
            hero + " enables exactly its intended combat component");
        Check(health && !health.IsDead && Read<int>(health, "maxHealth") == (fighter ? 130 : 100) &&
            Read<int>(health, "currentHealth") == (fighter ? 130 : 100), hero + " health/maxHP initialized to " + (fighter ? 130 : 100));
        Check(!Read<bool>(selector, "visible") && Mathf.Approximately(Time.timeScale, 1), hero + " selection closes and releases pause");
        CheckClipSprites(expected, hero.ToString(), true);
        yield return SampleHero(selector, fighter);
        travel.ReturnToMain();
        yield return Until(() => travel && !travel.IsAway && !travel.Busy &&
            SceneManager.GetActiveScene().path == OverworldV2PlayChecks.ValidationScene, "Return from " + hero, 20);
        yield return .15f;
        Check(flow.gameObject.activeInHierarchy && SceneManager.sceneCount == 1 && Mathf.Approximately(Time.timeScale, 1),
            hero + " return restores single active host/time and unloads B");
        CheckReal();
    }

    static IEnumerator SampleHero(CharacterSelectionUI selector, bool fighter)
    {
        var animator = selector.GetComponent<Animator>();
        var movement = selector.GetComponent<PlayerController2D>();
        var ranged = selector.GetComponent<PlayerCombat2D>(); var melee = selector.GetComponent<FighterMeleeCombat2D>();
        var renderer = selector.GetComponent<SpriteRenderer>();
        Check(movement && renderer, "Hero animation fixture has real controller and renderer");
        bool oldMovement = movement.enabled, oldRanged = ranged.enabled, oldMelee = melee.enabled;
        float oldTime = Time.timeScale, oldSpeed = animator.speed;
        try
        {
            movement.enabled = false; ranged.enabled = false; melee.enabled = false;
            Time.timeScale = 0; animator.speed = 0;
            string[] states = { "idle", "run", "jump", "double jump", "dash", fighter ? "attack" : "skill3" };
            foreach (string state in states)
            {
                int hash = Animator.StringToHash("Base Layer." + state);
                Check(animator.HasState(0, hash), (fighter ? "Fighter" : "Princess") + " has state " + state);
                foreach (float sample in new[] { .1f, .5f, .9f })
                {
                    animator.Play(hash, 0, sample); animator.Update(0);
                    var clips = animator.GetCurrentAnimatorClipInfo(0);
                    Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer." + state) &&
                        clips.Length > 0 && clips[0].clip && renderer.sprite,
                        "Sample " + (fighter ? "Fighter " : "Princess ") + state + " t=" + sample + " resolves clip and sprite");
                    yield return null;
                }
            }
            animator.Play("idle", 0, .25f); animator.Update(0);
            yield return Capture(fighter ? "b-fighter.png" : "b-princess.png");
        }
        finally
        {
            if (animator) animator.speed = oldSpeed;
            if (movement) movement.enabled = oldMovement;
            if (ranged) ranged.enabled = oldRanged;
            if (melee) melee.enabled = oldMelee;
            Time.timeScale = oldTime;
        }
    }

    static void CheckEnemyClips()
    {
        var controllers = new HashSet<RuntimeAnimatorController>(); int enemies = 0, animators = 0;
        foreach (var enemy in Object.FindObjectsByType<EnemyHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (enemy.gameObject.scene.path != SideWorldTravel.SideGameBPath) continue;
            enemies++;
            foreach (var animator in enemy.GetComponentsInChildren<Animator>(true))
            {
                animators++;
                Check(animator.runtimeAnimatorController, "Enemy animator controller resolves: " + enemy.name);
                controllers.Add(animator.runtimeAnimatorController);
            }
        }
        Check(enemies > 0 && animators > 0, "B enemy inventory enemies=" + enemies + " animators=" + animators);
        foreach (var controller in controllers) CheckClipSprites(controller, "Enemy " + controller.name, false);
    }

    static void CheckClipSprites(RuntimeAnimatorController controller, string label, bool requireSpriteFrames)
    {
        var clips = new HashSet<AnimationClip>(); int frames = 0, missing = 0;
        foreach (var clip in controller.animationClips)
        {
            if (!clip) { missing++; continue; }
            if (!clips.Add(clip)) continue;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.type != typeof(SpriteRenderer) || binding.propertyName != "m_Sprite") continue;
                foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                {
                    frames++;
                    if (!(key.value is Sprite)) { missing++; Record("MISSING_SPRITE " + label + "/" + clip.name + "/" + binding.path + " time=" + key.time); }
                }
            }
        }
        Check(clips.Count > 0 && missing == 0 && (!requireSpriteFrames || frames > 0),
            label + " clips=" + clips.Count + " sprite keyframes=" + frames + " missing=" + missing +
            (frames == 0 ? "; no sprite-swap curves (transform-only animation possible)" : ""));
    }

    static IEnumerator Capture(string filename)
    {
        yield return Frames(2);
        string path = Images + filename; DateTime request = DateTime.UtcNow; int frame = Time.frameCount;
        ScreenCapture.CaptureScreenshot(path);
        yield return Until(() => Time.frameCount >= frame + 2 && File.Exists(path) &&
            new FileInfo(path).Length > 0 && File.GetLastWriteTimeUtc(path) >= request, "Fresh Game View " + filename, 5);
        Record("PASS GAME_VIEW " + path + " requestFrame=" + frame + " verifiedFrame=" + Time.frameCount + "; controlled API display, no mouse click");
    }

    static IEnumerator Frames(int count)
    {
        int end = Time.frameCount + count;
        while (Time.frameCount < end) yield return null;
    }
    static IEnumerator Until(Func<bool> ready, string label, double seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (!ready()) { if (EditorApplication.timeSinceStartup > end) throw new TimeoutException(label); yield return null; }
    }
    static FieldInfo Field(Type type, string name) => type.GetField(name, PrivateInstance) ?? throw new MissingFieldException(type.Name, name);
    static MethodInfo Method(Type type, string name) => type.GetMethod(name, PrivateInstance) ?? throw new MissingMethodException(type.Name, name);
    static T Read<T>(object target, string name) => (T)Field(target.GetType(), name).GetValue(target);
    static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    static string SlotHash(string key) => PlayerPrefs.HasKey(key) ? Hash(Encoding.UTF8.GetBytes(PlayerPrefs.GetString(key))) : "missing";
    static void CheckReal()
    {
        string real = SessionState.GetString(State + "realPrefix", "");
        Check(SlotHash(real + ".primary") == SessionState.GetString(State + "primaryHash", "") &&
            SlotHash(real + ".backup") == SessionState.GetString(State + "backupHash", ""), "Real save primary/backup hashes unchanged");
    }
    static void Check(bool ok, string message) { Record((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new InvalidOperationException(message); }
    static void Record(string message) => File.AppendAllText(Report, DateTime.UtcNow.ToString("O") + " " + message + "\n");

    static void Tick()
    {
        if (!Running) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("Play interrupted or 120-second API suite deadline exceeded");
            if (EditorApplication.timeSinceStartup < resumeAt || Time.frameCount < resumeFrame) return;
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { steps.Pop(); (step as IDisposable)?.Dispose(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                resumeAt = EditorApplication.timeSinceStartup + (step.Current is float delay ? delay : 0);
                resumeFrame = Time.frameCount + 1; return;
            }
            Stop();
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }
    static void Fail(string reason)
    {
        SessionState.SetBool(State + "complete", false); Record("FAIL " + reason); Stop();
    }
    public static void Stop(bool exitPlay = true)
    {
        if (!Running) return;
        Running = false; EditorApplication.update -= Tick;
        while (steps.Count > 0)
            try { (steps.Pop() as IDisposable)?.Dispose(); }
            catch (Exception exception) { SessionState.SetBool(State + "complete", false); Record("FAIL fixture restoration " + exception); }
        Application.runInBackground = oldBackground;
        bool complete = SessionState.GetBool(State + "complete", false);
        Record(complete ? "PASS fixture cleanup; awaiting Prepare teardown" : "INCOMPLETE stopped before completion; fixtures restored");
        SessionState.SetString(State + "status", complete ? "Awaiting Edit-mode restoration" : "INCOMPLETE/FAIL; see b-heroes.txt");
        // Keep the disposable prefix until Flow teardown autosaves finish.
        if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
    static void ModeChanged(PlayModeStateChange mode)
    {
        if (mode == PlayModeStateChange.ExitingPlayMode && Running)
        { SessionState.SetBool(State + "complete", false); Record("INCOMPLETE external Play stop"); Stop(false); }
        if (mode == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += RestoreAudit;
    }
    public static void RestoreAudit()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!SessionState.GetBool(State + "restore", false)) return;
        bool ok = false;
        try
        {
            string prefix = SessionState.GetString(State + "prefix", "");
            var scene = SceneManager.GetActiveScene();
            CheckReal();
            ok = !EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool(PrepareState + "prepared", false) &&
                scene.path == MainScene && !scene.isDirty && SceneManager.sceneCount == 1 &&
                !PlayerPrefs.HasKey(prefix + ".primary") && !PlayerPrefs.HasKey(prefix + ".backup") &&
                Hash(File.ReadAllBytes(MainScene)) == SessionState.GetString(State + "mainHash", "") &&
                Mathf.Approximately(Time.timeScale, SessionState.GetFloat(PrepareState + "timeScale", 1));
            Record((ok ? "PASS " : "FAIL ") + "Restored clean original main scene/hash, QA slots cleared and original time; no input devices were changed by this suite");
        }
        catch (Exception exception) { Record("FAIL restoration audit " + exception); }
        finally
        {
            SessionState.SetBool(State + "restore", false);
            SessionState.SetString(State + "status", ok && SessionState.GetBool(State + "complete", false)
                ? "PASS bounded B hero/resource API checks and restoration" : "INCOMPLETE/FAIL; inspect b-heroes.txt");
        }
    }
}
