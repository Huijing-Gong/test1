using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// Real Play regression; all fixtures are runtime-only. Never saves the authored map or prefabs.
[InitializeOnLoad]
public static class EnemyFoundationPlayChecks
{
    const string Key = "QA.EnemyFoundation.";
    static readonly string Report = Path.Combine(Path.GetTempPath(), "anca-enemy-foundations-play.txt");
    const string Domain = "Assets/Art/Enemies/DomainPrototypes/Generated/";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new Vector3(16000, 100, 16000);
    [Serializable] class Saved { public string key, value; public bool exists; }
    [Serializable] class Snapshot { public Saved[] entries; }

    static EnemyFoundationPlayChecks()
    { EditorApplication.playModeStateChanged += Changed; EditorApplication.update += Watch; }

    public static void RunAutomation()
    { SessionState.SetBool(Key + "quit", true); Run(); }
    public static void RunAwarenessAutomation()
    { EnemyAwarenessPlayChecks.Prepare(); SessionState.SetBool(Key+"awarenessOnly",true); RunAutomation(); }
    public static void RunPauseResumeAutomation()
    { SessionState.SetBool(Key+"pauseResumeOnly",true); RunAutomation(); }
    public static void RunAttackVfxAutomation()
    { SessionState.SetBool(Key + "quit", true); SessionState.SetBool(Key + "attackVfxOnly", true); Run(); }
    public static void RunHealingStatusAutomation()
    { SessionState.SetBool("QA.HealingStatusOnly",true); RunAttackVfxAutomation(); }
    public static void RunChargeArrowAutomation()
    { SessionState.SetBool("QA.ChargeArrowOnly",true); RunAttackVfxAutomation(); }
    public static void RunGelAutomation()
    { SessionState.SetBool(Key + "quit", true); RunGelChecks(); }
    [MenuItem("Tools/Enemies/Check Gel Blob Combat And Presentation (Play)")]
    public static void RunGelChecks()
    { SessionState.SetBool(Key + "gelOnly", true); Run(); }
    public static void RunGelCameraAutomation()
    { SessionState.SetBool(Key + "quit", true); RunGelCameraChecks(); }
    [MenuItem("Tools/Enemies/Check Gel Blob Follow Camera (Play)")]
    public static void RunGelCameraChecks()
    { SessionState.SetBool(Key + "gelCameraOnly", true); RunGelChecks(); }
    public static void RunWispAutomation()
    { SessionState.SetBool(Key + "quit", true); RunWispChecks(); }
    [MenuItem("Tools/Enemies/Check Leaf Wisp Combat And Presentation (Play)")]
    public static void RunWispChecks()
    { SessionState.SetBool(Key + "wispOnly", true); Run(); }
    public static void RunVisualAutomation()
    { SessionState.SetBool(Key + "quit", true); RunVisualChecks(); }
    public static void RunVisualChecks()
    { SessionState.SetBool(Key + "visualOnly", true); Run(); }
    public static void RunEnvironmentAutomation()
    { SessionState.SetBool(Key + "quit", true); RunEnvironmentChecks(); }
    public static void RunEnvironmentChecks()
    { SessionState.SetBool(Key + "environmentOnly", true); Run(); }
    public static void RunAudioAutomation()
    { SessionState.SetBool(Key + "quit", true); RunAudioChecks(); }
    public static void RunAudioChecks()
    { SessionState.SetBool(Key + "audioOnly", true); Run(); }
    public static void RunPerformanceAutomation()
    { SessionState.SetBool(Key + "quit", true); RunPerformanceChecks(); }
    [MenuItem("Tools/Enemies/Check Enemy Performance Budget (Play)")]
    public static void RunPerformanceChecks()
    { SessionState.SetBool(Key + "performanceOnly", true); Run(); }

    [MenuItem("Tools/Enemies/Check Combat Ground And Hit Foundations (Play)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save scene edits first; no edits were discarded.");
        EditorSceneManager.OpenScene(EnemyNavigationBuilder.ScenePath);
        var flow = Object.FindFirstObjectByType<BaselineFlow>();
        var saved = new List<Saved>();
        foreach (string prefix in new HashSet<string> { flow.saveKeyPrefix, BaselineSaveStore.DefaultKeyPrefix })
            foreach (string suffix in new[] { ".primary", ".backup" })
            { string key = prefix + suffix; saved.Add(new Saved { key = key, exists = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") }); }
        SessionState.SetString(Key + "saved", JsonUtility.ToJson(new Snapshot { entries = saved.ToArray() }));
        SessionState.SetString(Key + "prefix", Key + Guid.NewGuid().ToString("N"));
        SessionState.SetString(Key + "deadline", DateTime.UtcNow.AddSeconds(240).Ticks.ToString());
        SessionState.SetBool(Key + "active", true); SessionState.SetBool(Key + "done", false);
        SessionState.SetBool(Key + "background", Application.runInBackground);
        File.WriteAllText(Report, "Enemy foundation real Play checks (runtime fixtures; unchanged authored scene)\n");
        Debug.Log("Enemy foundation Play report: " + Report);
        Application.runInBackground = true; EditorApplication.isPaused = false; EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange mode)
    {
        if (!SessionState.GetBool(Key + "active", false)) return;
        if (mode == PlayModeStateChange.EnteredPlayMode)
        {
            Application.runInBackground = true;
            var flow = Object.FindFirstObjectByType<BaselineFlow>();
            flow.saveKeyPrefix = SessionState.GetString(Key + "prefix", "");
            flow.StartCoroutine(Guard(Tests(flow)));
        }
        if (mode == PlayModeStateChange.EnteredEditMode)
        {
            bool passed = SessionState.GetBool(Key + "passed", false);
            foreach (Saved item in JsonUtility.FromJson<Snapshot>(SessionState.GetString(Key + "saved", "")).entries)
                if (item.exists != PlayerPrefs.HasKey(item.key) || item.value != PlayerPrefs.GetString(item.key, ""))
                { passed = false; Log("FAIL original save changed: " + item.key); }
            BaselineSaveStore.Clear(SessionState.GetString(Key + "prefix", ""));
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
            SessionState.SetBool(Key + "active", false);
            SessionState.SetBool(Key + "visualOnly", false);
            SessionState.SetBool(Key + "awarenessOnly", false);
            SessionState.SetBool(Key + "pauseResumeOnly", false);
            SessionState.SetBool(Key + "environmentOnly", false);
            SessionState.SetBool(Key + "audioOnly", false);
            SessionState.SetBool(Key + "performanceOnly", false);
            SessionState.SetBool(Key + "gelOnly", false);
            SessionState.SetBool(Key + "gelCameraOnly", false);
            SessionState.SetBool(Key + "wispOnly", false);
            SessionState.SetBool(Key + "attackVfxOnly", false);
            Log(passed ? "FINAL PASS; original saves unchanged; no scene/prefab saved." : "FINAL FAIL");
            bool quit = SessionState.GetBool(Key + "quit", false); SessionState.SetBool(Key + "quit", false);
            if (Application.isBatchMode || quit) EditorApplication.Exit(passed ? 0 : 1);
        }
    }

    static IEnumerator WaitForWorldResume(BaselineFlow flow)
    {
        // Wait for the course main's animated opening/menu exits, without changing production UI.
        float deadline = Time.realtimeSinceStartup + 4f;
        while (flow.BlockWorldInput && Time.realtimeSinceStartup < deadline) yield return null;
        yield return null;
    }

    static IEnumerator Tests(BaselineFlow flow)
    {
        yield return null; flow.NewGame(); if (flow.Tutorial) flow.Tutorial.Skip(); flow.SetMenu(false);
        GameSession.Instance.GrantSword();
        yield return null; yield return null;
        yield return WaitForWorldResume(flow);
        var world = flow.GetComponent<BaselineWorld>(); Transform player = world.player;
        var movement = player.GetComponent<PlayerMovement>(); var sword = player.GetComponent<PlayerCombat>();
        var vitals = player.GetComponent<PlayerVitals>(); var playerHealth = player.GetComponent<Health>();
        Require(vitals.EnemyCombatActive, "Gameplay gate closed: started=" + flow.Started +
            ", blocked=" + flow.BlockWorldInput + ", menu=" + flow.MenuOpen + ", death=" + flow.DeathOpen +
            ", tutorial=" + (flow.Tutorial && flow.Tutorial.ModalOpen) + ", timeScale=" + Time.timeScale +
            ", movementLocked=" + movement.IsGameplayInputLocked + ", HP=" + playerHealth.CurrentHealth +
            ", vitalsFlow=" + ((BaselineFlow)typeof(PlayerVitals).GetField("flow", Private).GetValue(vitals) == flow));
        if (SessionState.GetBool(Key + "pauseResumeOnly", false))
        {
            IEnumerator checks=EnemyPauseResumePlayChecks.Tests(flow,player,Log);
            while(checks.MoveNext())yield return checks.Current;
            yield break;
        }
        if (SessionState.GetBool(Key + "awarenessOnly", false))
        {
            IEnumerator checks=EnemyAwarenessPlayChecks.Tests(flow,player,Log);
            while(checks.MoveNext())yield return checks.Current;
            yield break;
        }
        if (SessionState.GetBool(Key + "attackVfxOnly", false))
        {
            IEnumerator checks = EnemyAttackPresentationPlayChecks.Tests(flow, player, Log);
            while (checks.MoveNext()) yield return checks.Current;
            yield break;
        }
        if (SessionState.GetBool(Key + "wispOnly", false))
        {
            IEnumerator checks = LeafWispPlayChecks.Tests(flow, player, Log);
            while (checks.MoveNext()) yield return checks.Current;
            yield break;
        }
        if (SessionState.GetBool(Key + "gelOnly", false))
        {
            IEnumerator checks = GelBlobPlayChecks.Tests(flow, player, Log, SessionState.GetBool(Key + "gelCameraOnly", false));
            while (checks.MoveNext()) yield return checks.Current;
            yield break;
        }
        foreach (var gel in Object.FindObjectsByType<GelBlobAI>(FindObjectsSortMode.None)) gel.enabled = false;
        if (SessionState.GetBool(Key + "performanceOnly", false))
        {
            movement.enabled = false; movement.ClearImpact();
            IEnumerator checks = EnemyPerformancePlayChecks.Tests(flow, player, Log);
            while (checks.MoveNext()) yield return checks.Current;
            yield break;
        }
        if (SessionState.GetBool(Key + "audioOnly", false))
        {
            movement.enabled = false; movement.ClearImpact();
            IEnumerator checks = EnemyAudioPlayChecks.Tests(flow, player, Log);
            while (checks.MoveNext()) yield return checks.Current;
            yield break;
        }
        if (SessionState.GetBool(Key + "environmentOnly", false))
        {
            movement.enabled = false; movement.ClearImpact();
            IEnumerator checks = EnemyEnvironmentVisualChecks.Tests(flow, player, Log);
            while (checks.MoveNext()) yield return checks.Current;
            yield break;
        }
        if (SessionState.GetBool(Key + "visualOnly", false))
        {
            movement.enabled = false; movement.ClearImpact();
            Vector3 origin = new Vector3(19000, 100, 19000);
            Collider visualFloor = Box("QA visual arena", origin + Vector3.down * .15f, new Vector3(48, .3f, 48));
            var layoutData = new SerializedObject(flow.GetComponent<OverworldV2Layout>());
            var grounds = layoutData.FindProperty("navigationSurfaces"); grounds.arraySize++;
            grounds.GetArrayElementAtIndex(grounds.arraySize - 1).objectReferenceValue = visualFloor;
            layoutData.ApplyModifiedPropertiesWithoutUndo(); Physics.SyncTransforms();
            IEnumerator checks = EnemyVisualStatePlayChecks.Tests(flow, player, Log);
            while (checks.MoveNext()) yield return checks.Current;
            Object.Destroy(visualFloor.gameObject);
            yield break;
        }
        // Existing autonomous navigation tests remain separate; synthetic physics fixtures are not baked.
        foreach (var navigation in Object.FindObjectsByType<EnemyNavigationWorld>(FindObjectsSortMode.None)) navigation.enabled = false;
        foreach (var ai in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None)) ai.enabled = false;
        movement.enabled = false; movement.ClearImpact();
        Collider floor = Box("QA approved floor", Origin + Vector3.down * .15f, new Vector3(28, .3f, 28));
        Collider platform = Box("QA upper platform", Origin + new Vector3(8, 1.85f, 0), new Vector3(5, .3f, 5));
        var layout = flow.GetComponent<OverworldV2Layout>();
        var data = new SerializedObject(layout); var surfaces = data.FindProperty("navigationSurfaces");
        int n = surfaces.arraySize; surfaces.arraySize += 2;
        surfaces.GetArrayElementAtIndex(n).objectReferenceValue = floor;
        surfaces.GetArrayElementAtIndex(n + 1).objectReferenceValue = platform;
        data.ApplyModifiedPropertiesWithoutUndo(); Physics.SyncTransforms();
        MovePlayer(player, Origin + new Vector3(0, .015f, 5), Quaternion.Euler(0, 180, 0));
        var scout = Spawn("Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab", Origin + Vector3.up * .015f).GetComponent<StoneScoutRoadsideAI>();
        yield return null;
        var query = new EnemyGroundQuery(scout.transform);
        Require(query.TrySample(Origin + new Vector3(8, 2.015f, 0), .35f, .6f, 35f, out RaycastHit upper) && upper.collider == platform,
            "Platform query selected lower terrain.");
        Require(!query.TryFootprint(Origin + new Vector3(10.3f, 2.015f, 0), Quaternion.identity, Vector2.one * .6f,
            35f, .35f, .6f, out _), "Centre-only support accepted overhanging footprint.");
        Collider obstacle = Box("QA unapproved obstacle", Origin + new Vector3(-7, .35f, 0), Vector3.one);
        Require(!query.TrySample(Origin + new Vector3(-7, .9f, 0), .35f, 1.5f, 35f, out _), "Ray passed through unapproved object to floor.");
        Object.Destroy(obstacle);
        Log("PASS common floor policy: nearest approved platform, full footprint edge rejection, no ray-through-obstacle.");
        foreach (string path in new[] { "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab", Domain + "LeafWisp.prefab",
            Domain + "RuinPrism.prefab", "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab", RepairWispBuilder.PrefabPath })
        {
            GameObject actor = Spawn(path, Origin + new Vector3(8, 2.015f, 0)); Physics.SyncTransforms();
            if (actor.TryGetComponent(out StoneScoutRoadsideAI walking))
            { object[] args = { Vector3.forward, .03f, false, Vector3.zero }; Require((bool)Call(walking, "TryStep", args) &&
                Mathf.Abs(((Vector3)args[3]).y - Origin.y - 2.015f) < .02f, "Scout failed upper-platform physical step."); }
            else if (actor.TryGetComponent(out DomainPrototypeEnemy hovering))
                Require((bool)Call(hovering, "TryHoverMove", Vector3.forward, .02f, 2f, null) && actor.transform.position.y > Origin.y + 1.9f,
                    hovering.EnemyKind + " failed upper-platform physical step.");
            else if (actor.TryGetComponent(out RailBeetleAI charging))
            { object[] args = { actor.transform.position, 0f }; Require((bool)Call(charging, "TryGround", args) &&
                (float)args[1] > Origin.y + 1.9f, "Rail full footprint selected lower floor."); }
            else if (actor.TryGetComponent(out RepairWispAI repairing))
            { object[] args = { actor.transform.position, Vector3.zero }; Require((bool)Call(repairing, "TryFloor", args) &&
                ((Vector3)args[1]).y > Origin.y + 1.9f, "Repair floor selected lower terrain."); }
            Object.Destroy(actor); yield return null;
        }
        Log("PASS all five real physical movers use the upper platform, never the lower terrain.");

        // Actual PlayerCombat.TryAttack causes the real movement lock; the enemy is not actually hit.
        Set(scout, "player", vitals); Set(scout, "playerHealth", playerHealth); Set(scout, "playerBody", player.GetComponent<CharacterController>());
        movement.enabled = true; movement.Relocate(player.position, player.rotation);
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        int ticks = 0;
        while (ticks++ < 180)
        { Cursor.lockState = CursorLockMode.Locked; if (sword.CanAttackNow) break; yield return null; }
        var playerAnimator = player.GetComponentInChildren<Animator>();
        Log("Air setup: ticks=" + ticks + " active=" + vitals.EnemyCombatActive + " grounded=" + movement.IsStableGrounded +
            " interaction=" + movement.CanStartInteraction + " lock=" + movement.IsGameplayInputLocked +
            " sword=" + GameSession.Instance.HasSword + " equipped=" + player.GetComponent<PlayerEquipment>().IsSwordEquipped +
            " cursor=" + Cursor.lockState + " animator=" + playerAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash +
            " expected=" + Animator.StringToHash("Locomotion") + " transition=" + playerAnimator.IsInTransition(0) + " pos=" + player.position);
        SetState(scout, "state", "Windup"); Set(scout, "timer", 1f);
        Require(sword.TryAttack(), "Actual air swing failed to start.");
        Require(!vitals.SimulationActive && vitals.EnemyCombatActive, "Sword lock does not demonstrate the old/new gate distinction.");
        yield return new WaitForFixedUpdate();
        Require(State(scout) == "Windup", "Air swing cancelled Scout windup: " + State(scout));
        sword.CancelForTransition(); movement.SetGameplayInputLocked(false);
        movement.enabled = false;
        Set(scout, "timer", 1f); Time.timeScale = 0; Set(sword, "ownsHitStop", true);
        Call(scout, "Update"); Require(State(scout) == "Windup", "Sword hit-stop cancelled Scout windup.");
        Set(sword, "ownsHitStop", false); Time.timeScale = 1;
        scout.GetComponent<Health>().TryApplyDamage(new DamageInfo(1, player.gameObject, scout.transform.position, Vector3.forward));
        Require(State(scout) == "Stagger", "Real damage did not interrupt Scout.");
        Log("PASS actual air swing/movement lock and sword hit-stop preserve attack; real damage staggers.");

        // Existing visible fist pose, not the old body-contact fallback.
        MovePlayer(player, Origin + new Vector3(0, .015f, .9f), Quaternion.identity);
        var visual = scout.GetComponentInChildren<StoneScoutVisualMotion>();
        int before = playerHealth.CurrentHealth;
        Require(!(bool)Call(scout, "TryStrike") && playerHealth.CurrentHealth == before, "Idle/retracted fist damaged touching player.");
        visual.ResetVisual(); visual.PlayAttack(); var animator = visual.GetComponent<Animator>();
        bool struck = false;
        for (int i = 0; i < 36; i++)
        {
            animator.Update(.02f); Physics.SyncTransforms();
            Vector3 fist = visual.RightFistWorldPosition;
            if ((bool)Call(scout, "TryStrike")) { struck = true; break; }
            Set(scout, "previousFist", fist); Set(scout, "hasFistSample", true);
        }
        Require(struck && playerHealth.CurrentHealth == before - 8, "Authored punch never contacted player; adjust pose, not damage radius.");
        Log("PASS retracted fist misses; visible authored fist sweep hits for the unchanged 8 damage.");
        scout.enabled = false;

        var prism = Spawn(Domain + "RuinPrism.prefab", Origin + new Vector3(5, .015f, 5)).GetComponent<DomainPrototypeEnemy>();
        Set(prism, "player", vitals); Set(prism, "playerHealth", playerHealth);
        Call(prism, "MeasurePulseCover"); var limits = (float[])Field(prism, "pulseSectorLimits");
        Require(limits.Length == 48, "Pulse segment contract changed.");
        float angle = Mathf.PI / 48f;
        Require(Mathf.Abs((float)Call(prism, "WaveRadiusAt", angle, 2f) - 2f * Mathf.Cos(angle)) < .002f,
            "Wave hit radius differs from drawn polygon.");
        MovePlayer(player, prism.transform.position + Vector3.right * 2.5f, Quaternion.identity);
        player.GetComponent<CharacterController>().Move(Vector3.down * .08f);
        Require(player.GetComponent<CharacterController>().isGrounded, "Standing wave fixture lacks real floor contact.");
        playerHealth.RestoreFullHealth(); before = playerHealth.CurrentHealth;
        Set(prism, "pulseHitPlayer", false); Call(prism, "TryPulseHit", 0f, 1f);
        Require(playerHealth.CurrentHealth == before, "Pulse damaged beyond visible front.");
        Call(prism, "TryPulseHit", 1f, 3f);
        Require(playerHealth.CurrentHealth == before - 14, "Advancing visible wave failed to hit once.");
        Call(prism, "TryPulseHit", 1f, 3f);
        Require(playerHealth.CurrentHealth == before - 14, "Wave hit same player twice.");
        playerHealth.RestoreFullHealth(); Set(prism, "pulseHitPlayer", false);
        Collider wall = Box("QA pulse cover", prism.transform.position + new Vector3(1.25f, 1.1f, 0), new Vector3(.3f, 3, 4));
        Call(prism, "MeasurePulseCover"); Call(prism, "TryPulseHit", 0f, 5.4f);
        Require(playerHealth.CurrentHealth == playerHealth.MaxHealth && limits[0] < 1.3f, "Wave penetrated cover or tell ignored wall.");
        Object.Destroy(wall); yield return null;
        // A lower approved floor must not let the wave jump off a platform edge.
        platform.transform.position -= Vector3.up * .8f; Physics.SyncTransforms();
        var ledgePrism = Spawn(Domain + "RuinPrism.prefab", Origin + new Vector3(8, 1.215f, 0)).GetComponent<DomainPrototypeEnemy>();
        Call(ledgePrism, "MeasurePulseCover");
        Require(((float[])Field(ledgePrism, "pulseSectorLimits"))[0] <= 2.5f,
            "Ground wave bridged a cliff to the approved lower floor.");
        Object.Destroy(ledgePrism); yield return null;
        Log("PASS wavefront uses drawn segments, unchanged radius/damage, one hit, wall cover and cliff continuity.");
        Set(prism, "pulseVisualTime", .3f); SetState(prism, "state", "Recover");
        flow.SetMenu(true); Call(prism, "Update"); Call(scout, "Update");
        Require((float)Field(prism, "pulseVisualTime") == 0 && !((LineRenderer)Field(prism, "warningLine")).enabled,
            "Menu retained an active pulse.");
        flow.SetMenu(false); prism.enabled = false;
        yield return WaitForWorldResume(flow);
        IEnumerator jumpDodge = PrismJumpDodge(prism, player);
        while (jumpDodge.MoveNext()) yield return jumpDodge.Current;

        var wisp = Spawn(Domain + "LeafWisp.prefab", Origin + new Vector3(-5, .015f, 5)).GetComponent<DomainPrototypeEnemy>();
        Set(wisp, "player", vitals); Set(wisp, "playerHealth", playerHealth);
        wisp.enabled = false; // Controlled projectile fixtures; owner Health remains alive.
        MovePlayer(player, wisp.transform.position + Vector3.forward * 4f, Quaternion.identity);
        Vector3 muzzle = wisp.transform.position + Vector3.up;
        wall = Box("QA dart cover", muzzle + Vector3.forward * 1.5f, new Vector3(3, 3, .2f));
        var dart = Spawn(Domain + "LeafSeedProjectile.prefab", muzzle).GetComponent<DomainSeedProjectile>();
        dart.Launch(wisp.gameObject, Vector3.forward, 9); before = playerHealth.CurrentHealth;
        ticks = 0; while (dart && dart.gameObject.activeSelf && ticks++ < 90) yield return null;
        Require(ticks < 90 && playerHealth.CurrentHealth == before, "Swept dart passed through wall.");
        Object.Destroy(wall); yield return null;
        wall = Box("QA initial dart overlap", muzzle, Vector3.one * .5f);
        dart = Spawn(Domain + "LeafSeedProjectile.prefab", muzzle).GetComponent<DomainSeedProjectile>(); dart.Launch(wisp.gameObject, Vector3.forward, 9);
        yield return null; yield return null;
        Require(!dart || !dart.gameObject.activeSelf, "Dart escaped initial wall overlap.");
        Object.Destroy(wall);
        dart = Spawn(Domain + "LeafSeedProjectile.prefab", muzzle).GetComponent<DomainSeedProjectile>(); dart.Launch(wisp.gameObject, Vector3.forward, 9);
        flow.SetMenu(true); yield return null; yield return null;
        Require(!dart || !dart.gameObject.activeSelf, "Paused dart survived to hit after resume."); flow.SetMenu(false);
        yield return WaitForWorldResume(flow);
        dart = Spawn(Domain + "LeafSeedProjectile.prefab", muzzle).GetComponent<DomainSeedProjectile>(); dart.Launch(wisp.gameObject, Vector3.forward, 9);
        wisp.GetComponent<Health>().TryApplyDamage(new DamageInfo(1000, player.gameObject, muzzle, Vector3.forward));
        Require(!dart.gameObject.activeSelf, "Owner death retained active projectile.");
        Log("PASS dart wall sweep, initial overlap, paused cleanup and owner-death cleanup.");

        var support = Spawn(RepairWispBuilder.PrefabPath, Origin + new Vector3(7, .015f, 5)).GetComponent<RepairWispAI>();
        var group = new GameObject("QA finite support group").AddComponent<RepairWispGroup>();
        group.Configure(new[] { prism.GetComponent<Health>() }); support.ConfigureGroup(group);
        yield return null; yield return null; // Let the previous menu's deferred input unlock finish.
        support.ResetForJourney();
        prism.GetComponent<Health>().RestoreFullHealth();
        MovePlayer(player, prism.transform.position + Vector3.back * 3f, Quaternion.identity);
        prism.GetComponent<Health>().TryApplyDamage(new DamageInfo(50, player.gameObject, prism.transform.position, Vector3.forward));
        ticks = 0; while (!support.BeamVisible && ticks++ < 600) yield return new WaitForFixedUpdate();
        Require(support.BeamVisible, "Real finite support failed: phase=" + support.CurrentPhase + " active=" + support.CombatAvailable +
            " home=" + Field(support, "home") + " pos=" + support.transform.position + " guard=" + prism.transform.position +
            " hp=" + prism.GetComponent<Health>().CurrentHealth + " valid=" + Call(support, "ValidTarget", prism.GetComponent<Health>()) +
            " position=" + Call(support, "InRepairPosition", prism.GetComponent<Health>()));
        yield return null; yield return null;
        var glow = prism.GetComponent<RepairSupportHighlight>(); Require(glow && glow.Visible, "Support shell missing.");
        flow.SetMenu(true); yield return null; yield return null;
        Require(!support.BeamVisible && !glow.Visible, "Menu retained healing line or shader.");
        flow.SetMenu(false);
        yield return WaitForWorldResume(flow);
        scout.enabled = true; prism.enabled = true; wisp.enabled = true;
        foreach (Health enemy in new[] { scout.GetComponent<Health>(), prism.GetComponent<Health>(), support.GetComponent<Health>() })
            enemy.TryApplyDamage(new DamageInfo(1000, player.gameObject, enemy.transform.position, Vector3.forward));
        flow.NewGame(); if (flow.Tutorial) flow.Tutorial.Skip(); flow.SetMenu(false);
        yield return WaitForWorldResume(flow);
        yield return null;
        Require(!scout.GetComponent<Health>().IsDead && !prism.GetComponent<Health>().IsDead &&
            !wisp.GetComponent<Health>().IsDead && !support.GetComponent<Health>().IsDead && support.RemainingRepairs == 3 &&
            !support.BeamVisible && !glow.Visible, "New game did not restore enemies/finite support or clear shader.");
        Log("PASS actual repair stops in menu with immediate shader cleanup; actual NewGame restores dead enemies and three charges.");
        IEnumerator roles = EnemyRolePlayChecks.Tests(flow, player, Log);
        while (roles.MoveNext()) yield return roles.Current;
        IEnumerator audio = EnemyAudioPlayChecks.Tests(flow, player, Log);
        while (audio.MoveNext()) yield return audio.Current;
        yield return WaitForWorldResume(flow); GameSession.Instance.GrantSword();
        IEnumerator visuals = EnemyVisualStatePlayChecks.Tests(flow, player, Log);
        while (visuals.MoveNext()) yield return visuals.Current;
    }

    static IEnumerator PrismJumpDodge(DomainPrototypeEnemy prism, Transform player)
    {
        var movement = player.GetComponent<PlayerMovement>();
        var controller = player.GetComponent<CharacterController>();
        var health = player.GetComponent<Health>();
        var vitals = player.GetComponent<PlayerVitals>();
        yield return WaitForWorldResume(Object.FindFirstObjectByType<BaselineFlow>());
        Require(vitals.EnemyCombatActive && Time.deltaTime > 0f, "Menu did not release combat before the jump fixture.");
        Vector3 standing = prism.transform.position + Vector3.right * 2.5f;
        MovePlayer(player, standing, Quaternion.identity); controller.Move(Vector3.down * .08f);
        health.RestoreFullHealth(); Call(prism, "MeasurePulseCover");

        // Queue a crossing while grounded, then leave the floor during the same frame.
        Set(prism, "pulseHitPlayer", false); Set(prism, "pulseVisualTime", .36f * (1f - 2.4f / 5.4f));
        Set(prism, "timer", 2f); Call(prism, "Update");
        Require((bool)Field(prism, "pulseHitPending") && health.CurrentHealth == health.MaxHealth,
            "Wave resolved in Update before this frame's player movement.");
        controller.Move(Vector3.up * .12f); Set(movement, "stableGrounded", true);
        Require(!controller.isGrounded && movement.IsStableGrounded, "Off-edge grace fixture invalid.");
        Call(prism, "LateUpdate");
        Require(health.CurrentHealth == health.MaxHealth && !(bool)Field(prism, "pulseHitPlayer"),
            "Same-frame takeoff/grace contact was hit by a ground wave.");
        Call(prism, "CancelWarning");

        // Traversal flags must not turn stale capsule contact into a ground-wave hit.
        MovePlayer(player, standing, Quaternion.identity); controller.Move(Vector3.down * .08f);
        foreach (string mode in new[] { "swimmingActive", "climbingActive", "mantlingActive" })
        {
            Set(movement, mode, true);
            try { Call(prism, "TryPulseHit", 1f, 3f); }
            finally { Set(movement, mode, false); }
            Require(health.CurrentHealth == health.MaxHealth, "Ground wave hit traversal mode: " + mode);
        }

        Keyboard previousKeyboard = Keyboard.current;
        var previousBackground = InputSystem.settings.backgroundBehavior;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>("PrismJumpQA"); keyboard.MakeCurrent();
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        try
        {
            movement.Relocate(standing, Quaternion.identity); movement.enabled = true;
            int ticks = 0;
            while (!controller.isGrounded && ticks++ < 60) yield return null;
            Require(controller.isGrounded, "Jump fixture did not settle on the floor.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Space)); yield return null;
            ticks = 0;
            while (controller.isGrounded && ticks++ < 60) yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Require(!controller.isGrounded && (float)Field(movement, "verticalVelocity") > 0f,
                "Real Space input did not start physical jump.");
            bool rising = false, falling = false;
            float landingDeadline = Time.time + 4f;
            while (!controller.isGrounded && Time.time < landingDeadline)
            {
                if (Mathf.Abs(player.position.y - prism.transform.position.y) < 1.6f)
                {
                    rising |= (float)Field(movement, "verticalVelocity") > 0f;
                    falling |= (float)Field(movement, "verticalVelocity") < 0f;
                    Call(prism, "TryPulseHit", 1f, 3f);
                    Require(health.CurrentHealth == health.MaxHealth,
                        "Ground wave damaged the real rising/falling jump inside the old height envelope.");
                }
                yield return null;
            }
            Require(rising && falling && controller.isGrounded && Time.time < landingDeadline,
                "Real jump did not cover rising, falling and landing: rising=" + rising + " falling=" + falling +
                " grounded=" + controller.isGrounded + " y=" + player.position.y + " velocity=" + Field(movement, "verticalVelocity"));
            Call(prism, "TryPulseHit", 1f, 3f);
            Require(health.CurrentHealth == health.MaxHealth - 14, "Landing in a crossing wave incorrectly grants invulnerability.");
            health.RestoreFullHealth(); Set(prism, "pulseHitPlayer", false);
            Call(prism, "TryPulseHit", 3.5f, 5.4f);
            Require(health.CurrentHealth == health.MaxHealth, "Landing after the front has passed still deals damage.");
            Set(prism, "pulseHitPending", true); Set(prism, "pendingPulsePreviousRadius", 1f);
            Set(prism, "pendingPulseCurrentRadius", 3f); Call(prism, "CancelWarning"); Call(prism, "LateUpdate");
            Require(health.CurrentHealth == health.MaxHealth && !(bool)Field(prism, "pulseHitPending"),
                "Canceled wave retained a queued late hit.");
            Log("PASS Prism jump dodge: real Space takeoff/rising/falling avoid damage; actual landing can be hit; passed front is safe; off-edge grace/traversal and same-frame cancellation are safe.");
        }
        finally
        {
            movement.enabled = false; movement.ClearImpact();
            InputSystem.RemoveDevice(keyboard); if (previousKeyboard != null) previousKeyboard.MakeCurrent();
            InputSystem.settings.backgroundBehavior = previousBackground;
        }
    }

    static GameObject Spawn(string path, Vector3 point) => Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), point, Quaternion.identity);
    static Collider Box(string name, Vector3 point, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.position = point; go.transform.localScale = size; Physics.SyncTransforms(); return go.GetComponent<Collider>();
    }
    static void MovePlayer(Transform player, Vector3 point, Quaternion rotation)
    { var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.SetPositionAndRotation(point, rotation); cc.enabled = true; Physics.SyncTransforms(); }
    static object Field(object actor, string name) => actor.GetType().GetField(name, Private).GetValue(actor);
    static void Set(object actor, string name, object value) => actor.GetType().GetField(name, Private).SetValue(actor, value);
    static object Call(object actor, string name, params object[] args) => actor.GetType().GetMethod(name, Private).Invoke(actor, args);
    static string State(object actor) => Field(actor, "state").ToString();
    static void SetState(object actor, string field, string value)
    { var info = actor.GetType().GetField(field, Private); info.SetValue(actor, Enum.Parse(info.FieldType, value)); }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static void Log(string message) { File.AppendAllText(Report, message + "\n"); Debug.Log("EnemyFoundation: " + message); }
    static IEnumerator Guard(IEnumerator tests)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(tests);
        while (stack.Count > 0)
        {
            object current;
            try
            {
                if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                current = stack.Peek().Current;
            }
            catch (Exception error)
            {
                while (stack.Count > 0) try { (stack.Pop() as IDisposable)?.Dispose(); } catch { }
                Finish(false, error.ToString()); yield break;
            }
            // Unity otherwise logs a nested coroutine exception without finishing this harness.
            if (current is IEnumerator nested && !(current is CustomYieldInstruction)) { stack.Push(nested); continue; }
            yield return current;
        }
        Finish(true, "All real Play checks completed.");
    }
    static void Finish(bool passed, string message)
    {
        if (SessionState.GetBool(Key + "done", false)) return;
        SessionState.SetBool(Key + "done", true); SessionState.SetBool(Key + "passed", passed); Log((passed ? "PASS " : "FAIL ") + message);
        Time.timeScale = 1; EditorApplication.delayCall += () => EditorApplication.ExitPlaymode();
    }
    static void Watch()
    {
        if (SessionState.GetBool(Key + "active", false) && !SessionState.GetBool(Key + "done", false) &&
            long.TryParse(SessionState.GetString(Key + "deadline", ""), out long ticks) && DateTime.UtcNow.Ticks > ticks)
            Finish(false, "240-second watchdog; no silent infinite test loop.");
    }
}
