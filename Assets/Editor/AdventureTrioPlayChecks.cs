using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using KingOfGraphic.Hero;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Real native animation / autonomous AI checks, only in the disposable clone.
// Controlled Health events explicitly isolate interruption, shielding and death.
[InitializeOnLoad]
public static class AdventureTrioPlayChecks
{
    const string Prefix = "QA.AdventureTrioStyles.";
    const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    const string Generated = "Assets/Art/Enemies/AdventureTrio/Generated/";
    const string NativeKnightPrefab = "Assets/RPGMonsterWave03PBR/Prefab/Characters/BishopKnightPBR.prefab";
    const string OriginalSpearMeshPath = "Assets/RPGMonsterWave03PBR/Mesh/Characters/BishopKnight_Mesh.fbx";
    const string OriginalSpearMaterialPath = "Assets/Art/Enemies/AdventureTrio/Styled/Knight/KnightSpear.mat";
    static readonly Vector3 Origin = new Vector3(23000, 100, 23000);
    static string Output => Path.Combine(Path.GetDirectoryName(Application.dataPath), "LocalDocs/Enemies/SilverSentinelRemodel-20261021");
    static string Report => Path.Combine(Output, SessionState.GetBool(Prefix + "shieldMaterial", false) ? "shield-material-validation.txt" : SessionState.GetBool(Prefix + "mapPreview", false) ? "map-preview-validation.txt" :
        SessionState.GetBool(Prefix + "knightOnly", false) ? "knight-runtime-validation.txt" : "runtime-validation.txt");
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
    static GameObject demon;
    static DemonKingCombatVisual demonVisual;
    static int demonEvents;
    static int hitEvents, missingActions;
    static int hpAtContact;
    static float ageAtContact;
    static string actionAtContact;
    static string contactImage;
    static readonly Dictionary<string, Color> SentinelPalette = new Dictionary<string, Color> {
        { "Silver", SentinelColor(0xD4D8DD) }, { "Indigo", SentinelColor(0x172B4B) },
        { "Gold", SentinelColor(0xDBAE58) }, { "Dark", SentinelColor(0x101923) },
        { "Eye", SentinelColor(0x7AE5F2) } };

    static AdventureTrioPlayChecks()
    { EditorApplication.playModeStateChanged += Changed; EditorApplication.update += Watch; }

    public static void RunAutomation() => Start(false);
    public static void RunMapPreviewAutomation() => Start(true);
    public static void RunKnightAutomation() => Start(false, true);
    public static void RunShieldMaterialAutomation() => Start(false,true,true);
    public static void RunSkillAutomation() => Start(false,true,false,true);

    static void Start(bool mapPreview, bool knightOnly = false, bool shieldMaterial = false, bool skillsOnly = false)
    {
        if (!Application.dataPath.StartsWith("/private/tmp/adventure-trio-", StringComparison.Ordinal) &&
            !Application.dataPath.StartsWith("/private/tmp/sidea-loading-", StringComparison.Ordinal))
            throw new InvalidOperationException("Adventure trio QA requires its disposable clone.");
        if (Application.isBatchMode) throw new InvalidOperationException("Native animation, Game view and HUD checks require a GUI Unity session.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Unsaved scene edits; QA did not discard them.");
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
        SessionState.SetBool(Prefix + "mapPreview", mapPreview);
        SessionState.SetBool(Prefix + "knightOnly", knightOnly);
        SessionState.SetBool(Prefix + "shieldMaterial", shieldMaterial);
        SessionState.SetBool(Prefix + "skillsOnly", skillsOnly);
        SessionState.SetBool(Prefix + "background", Application.runInBackground);
        SessionState.SetBool(Prefix + "passed", false); SessionState.SetBool(Prefix + "active", true);
        host.saveKeyPrefix = SessionState.GetString(Prefix + "prefix", "");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Report, shieldMaterial ? "Shield material focused Play validation: authored-map guard capture, controlled hit/flash, native death and journey reset. Not a full combat timing regression.\n" : mapPreview ? "Adventure trio: actual authored-map screenshot preview only; combat suite is not executed.\n" :
            "Adventure trio: actual Unity Play / native animations in an isolated clone. Scope: " + (knightOnly ? "Knight only; Fishman, Mushroom and Demon voice suite omitted.\n" : "complete trio and Demon voice suite.\n") +
            "Autonomous AI + real contacts run on a registered runtime test floor; controlled Health cases are labelled. Not a human feel review.\n");
        Application.runInBackground = true; EditorApplication.isPaused = false;
        EditorApplication.ExecuteMenuItem("Window/General/Game"); FocusStartup(); EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Prefix + "active", false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            Application.runInBackground = true; missingActions = 0;
            Application.logMessageReceived += CheckActionError;
            flow = Object.FindFirstObjectByType<BaselineFlow>(); flow.saveKeyPrefix = SessionState.GetString(Prefix + "prefix", "");
            flow.StartCoroutine(Guard(Tests())); return;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        bool passed = SessionState.GetBool(Prefix + "passed", false);
        try
        {
            foreach (var entry in JsonUtility.FromJson<Snapshot>(SessionState.GetString(Prefix + "saved", "")).entries)
                if (entry.exists != PlayerPrefs.HasKey(entry.key) || entry.value != PlayerPrefs.GetString(entry.key, ""))
                {
                    passed = false; Log("FAIL original save changed; restoring original snapshot: " + entry.key);
                    if (entry.exists) PlayerPrefs.SetString(entry.key, entry.value); else PlayerPrefs.DeleteKey(entry.key);
                }
            if (Hash(ScenePath) != SessionState.GetString(Prefix + "scene", "")) { passed = false; Log("FAIL authored scene bytes changed."); }
            BaselineSaveStore.Clear(SessionState.GetString(Prefix + "prefix", "")); PlayerPrefs.Save();
        }
        catch (Exception error) { passed = false; Log("FAIL cleanup " + error); }
        Application.runInBackground = SessionState.GetBool(Prefix + "background", false);
        SessionState.SetBool(Prefix + "active", false);
        Log(passed ? "FINAL PASS; original saves and authored scene bytes unchanged; no scene/prefab saved." : "FINAL FAIL");
        EditorApplication.Exit(passed ? 0 : 1);
    }

    static IEnumerator Tests()
    {
        yield return null; flow.NewGame(); if (flow.Tutorial) flow.Tutorial.Skip(); flow.SetMenu(false);
        GameSession.Instance.GrantSword(); world = flow.GetComponent<BaselineWorld>(); hero = world.player;
        movement = hero.GetComponent<PlayerMovement>(); adapter = hero.GetComponent<HeroWorldAdapter>();
        vitals = hero.GetComponent<PlayerVitals>(); hp = hero.GetComponent<Health>();
        yield return null; yield return null;
        yield return Wait(() => !flow.BlockWorldInput && Time.timeScale > 0 && !movement.HasExternalInputLock, 8, "native opening releases deferred gameplay locks");
        float readyUntil = Time.realtimeSinceStartup + 8;
        while ((!adapter.pose || !adapter.pose.Ready || !adapter.AcceptInput || !vitals.EnemyCombatActive) && Time.realtimeSinceStartup < readyUntil)
        { FocusStartup(); yield return null; }
        Need(adapter.pose && adapter.pose.Ready && adapter.AcceptInput && vitals.EnemyCombatActive, "focused Game view has real hero pose/incoming-hit gates active");
        if (Camera.main && Camera.main.GetComponent<ThirdPersonCamera>()) Camera.main.GetComponent<ThirdPersonCamera>().enabled = false;
        var authored = Object.FindObjectsByType<AdventureCreatureAI>(FindObjectsSortMode.None);
        Need(authored.Length == 3, "main world contains exactly the three authored new creatures");
        // All map portraits share a stable authored scene, including when the
        // hero happened to start inside an encounter's acquisition radius.
        foreach (var native in authored) native.enabled = false;
        foreach (var native in authored)
        {
            Capture("map-" + native.GetType().Name + ".png", native.transform.position);
            float portraitDistance = native is BishopKnightAI ? 8.5f : native is SmilingMushroomAI ? 2.4f : 3.8f;
            Capture("map-" + native.GetType().Name + "-styled-front.png", native.transform.position,
                native.transform.forward * portraitDistance + Vector3.up * (native is BishopKnightAI ? 1.35f : .35f), 42f);
            if (native is BishopKnightAI)
            {
                Need(AdventureTrioStyleInstaller.IsRemodeledKnight(native.gameObject),
                    "Actual map screenshot uses the remodeled Silver Sentinel geometry");
                Capture("map-BishopKnightAI-remodel-three-quarter.png", native.transform.position,
                    native.transform.forward * 6f + native.transform.right * 3.8f + Vector3.up * .65f, 36f);
                Capture("map-BishopKnightAI-remodel-opposite-quarter.png", native.transform.position,
                    native.transform.forward * 6f - native.transform.right * 3.8f + Vector3.up * .65f, 36f);
                Capture("map-BishopKnightAI-remodel-helmet-detail.png", native.transform.position + Vector3.up * .86f,
                    native.transform.forward * 2.8f + native.transform.right * 1.4f + Vector3.up * .2f, 32f);
                Capture("map-BishopKnightAI-remodel-shield-detail.png", native.transform.position,
                    native.transform.forward * 3.8f + native.transform.right * 2.4f + Vector3.up * .2f, 36f);
            }
            if (native is SmilingMushroomAI)
            {
                Capture("map-SmilingMushroomAI-nearfront.png", native.transform.position, native.transform.forward * 3.2f + Vector3.up, 48f);
                Capture("map-SmilingMushroomAI-overhead.png", native.transform.position, new Vector3(.5f, 9f, -2f), 42f);
            }
            Log("AUTHORED " + native.name + " at " + native.transform.position);
        }
        // This is a real authored-map encounter, not a sampled pose or a forced
        // glow value. Keep every map renderer; only unrelated enemy brains stop.
        Disable<StoneScoutRoadsideAI>(); Disable<DomainPrototypeEnemy>(); Disable<RailBeetleAI>(); Disable<GelBlobAI>(); Disable<RepairWispAI>(); Disable<EnemyNavigationWorld>();
        var mapKnight = authored.OfType<BishopKnightAI>().Single();
        actor = mapKnight;
        mapKnight.ResetForJourney();
        Place(mapKnight.Home + mapKnight.transform.forward * 3.1f);
        mapKnight.enabled = true;
        Physics.SyncTransforms();
        yield return Wait(() => mapKnight.ShieldRaised, 12,
            "authored-map Silver Sentinel recognizes the real Hero and autonomously raises its native guard");
        yield return new WaitForSeconds(.22f);
        Need(mapKnight.ShieldRaised && !mapKnight.IsDead && mapKnight.CurrentAction == "Defend",
            "authored-map beauty capture retains the actual alive native Defend phase");
        CheckStylePropertyBlocks(mapKnight, "authored-map alive Silver Sentinel native guard");
        Capture("map-SilverSentinel-guard-threequarter.png", mapKnight.transform.position,
            mapKnight.transform.right * 4f + mapKnight.transform.forward * 6f + Vector3.up * 1.4f, 32f);
        Capture("map-SilverSentinel-guard-helmet-detail.png", mapKnight.transform.position + Vector3.up,
            mapKnight.transform.right * 1.5f + mapKnight.transform.forward * 2.5f + Vector3.up * .2f, 32f);
        Capture("map-SilverSentinel-guard-shield-detail.png", mapKnight.transform.position,
            mapKnight.transform.right * 2f + mapKnight.transform.forward * 3.2f + Vector3.up * .1f, 30f);
        Log("CAPTURE authored-map live guard; genuine AI/animation/style lifecycle, no forced shader values or scene save.");
        if(SessionState.GetBool(Prefix+"shieldMaterial",false))
        {
            Need(Hurt(25),"Shield material controlled heavy hit is accepted through native shield-break rules");
            yield return new WaitForSeconds(.3f);
            CheckStylePropertyBlocks(mapKnight,"Shield material after native hit flash");
            mapKnight.ResetForJourney();Place(mapKnight.Home+Vector3.right*10);
            Need(Hurt(10000)&&mapKnight.IsDead,"Shield material controlled lethal hit starts native death");
            yield return null;
            CheckStylePropertyBlocks(mapKnight,"Shield material native death",0);
            mapKnight.ResetForJourney();yield return null;
            CheckStylePropertyBlocks(mapKnight,"Shield material journey reset");
            Log("SHIELD MATERIAL PASS; no combat timing/input-lock regression claimed.");
            yield break;
        }
        mapKnight.enabled = false;
        actor = null;
        if (SessionState.GetBool(Prefix + "mapPreview", false))
        { Log("MAP PREVIEW ONLY; actual map renders captured, no fixture/combat suite executed."); yield break; }
        Disable<StoneScoutRoadsideAI>(); Disable<DomainPrototypeEnemy>(); Disable<RailBeetleAI>(); Disable<GelBlobAI>(); Disable<RepairWispAI>(); Disable<EnemyNavigationWorld>();
        var floor = Box("QA adventure trio registered runtime floor", Origin - Vector3.up * .15f, new Vector3(70, .3f, 70));
        var layout = new SerializedObject(flow.GetComponent<OverworldV2Layout>()); var surfaces = layout.FindProperty("navigationSurfaces");
        surfaces.arraySize++; surfaces.GetArrayElementAtIndex(surfaces.arraySize - 1).objectReferenceValue = floor; layout.ApplyModifiedPropertiesWithoutUndo();
        hp.Damaged += PlayerDamaged;
        foreach (string name in SessionState.GetBool(Prefix + "knightOnly", false) ? new[] { "BishopKnight" } : new[] { "Fishman", "SmilingMushroom", "BishopKnight" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Generated + name + ".prefab"); Need(prefab, name + " generated prefab exists");
            var instance = Object.Instantiate(prefab, Origin + Vector3.up * .025f, Quaternion.identity); actor = instance.GetComponent<AdventureCreatureAI>();
            Need(actor && actor.animator && actor.animator.runtimeAnimatorController, name + " owns native AI and animation controller");
            Place(actor.Home + Vector3.forward * FixtureDistance(actor)); hitEvents = 0; contactImage = null;
            // Voice distance uses the listener/camera, not the synthetic target's
            // location; keep it by the remote fixture before native acquisition.
            Capture("fixture-start-" + name + ".png", actor.transform.position,
                actor.transform.forward * (actor is BishopKnightAI ? 7.5f : 5f) + actor.transform.right * (actor is BishopKnightAI ? 4f : 2.4f) + Vector3.up * (actor is BishopKnightAI ? 2f : 1f));
            yield return null;
            if (SessionState.GetBool(Prefix + "skillsOnly", false) && actor is BishopKnightAI skillKnight)
            {
                yield return KnightNativeContactChecks(skillKnight);
                yield return KnightConceptChecks(skillKnight);
                Object.Destroy(instance); actor = null; yield return null;
                continue;
            }
            if (actor is FishmanAI)
            {
                Need(!actor.IsDead && actor.isActiveAndEnabled && actor.GetComponent<AdventureCreatureStyle>().StyleClock > 0,
                    "actual alive Fishman fixture has received its normal style update before close rendering");
                CheckStylePropertyBlocks(actor, "actual alive Fishman close rendering");
                Capture("fixture-style-Fishman.png", actor.transform.position,
                    actor.transform.forward * 4.3f + actor.transform.right * 2.4f + Vector3.up * .4f);
                Vector3 harpoon = (actor.weapon.TransformPoint(actor.weaponBaseLocal) + actor.weapon.TransformPoint(actor.weaponTipLocal)) * .5f;
                Capture("fixture-style-Fishman-Harpoon.png", harpoon - Vector3.up,
                    actor.transform.forward * 2.4f + actor.transform.right * 2f + Vector3.up * .8f);
            }
            if (actor is SmilingMushroomAI mushroom) Need(mushroom.IsDisguised, "mushroom begins with true plant disguise");
            int before = hp.CurrentHealth;
            yield return Wait(() => hitEvents > 0, 17, name + " autonomously acquires and lands an actual native attack");
            Need(hitEvents == 1 && before - hp.CurrentHealth == hpAtContact && hpAtContact == Convert.ToInt32(Field(actor, "strikeDamage")), name + " native event has exactly configured damage: " + hpAtContact);
            Need(ageAtContact >= (float)Field(actor, "strikeBegin") - .025f && ageAtContact <= (float)Field(actor, "strikeEnd") + .025f && actionAtContact.StartsWith("Attack", StringComparison.Ordinal), name + " damage is inside the native animated window: " + ageAtContact.ToString("F3") + " / " + actionAtContact);
            Need(actor.Contacts <= actor.Swings && actor.Hits <= actor.Contacts, name + " allows one real contact at most per committed swing");
            yield return Wait(() => !string.IsNullOrEmpty(contactImage) && File.Exists(contactImage) && new FileInfo(contactImage).Length > 1000, 3,
                name + " real Game view first-contact screenshot includes native pose, Hero scale and runtime HUD");
            CheckStylePropertyBlocks(actor, name + " actual native contact");
            if (actor is BishopKnightAI firstBoss)
            {
                Need(firstBoss.TerritoryEngaged && firstBoss.HudEligible && HudVisible(firstBoss), "territorial Knight actual native contact has its live boss HUD");
                Need(Mathf.Abs(firstBoss.GetComponent<CapsuleCollider>().height - 3.15f) < .025f && firstBoss.MaximumHealth == 240,
                    "actual first-contact evidence uses the 3.15m 240HP miniboss beside the native Hero");
            }
            var voice = actor.GetComponent<AdventureCreatureVoice>();
            Need(voice && voice.Source && voice.PlayedCount > 0, name + " autonomous encounter plays its generated non-verbal voice (playback event, not perceptual audition): " + (voice ? voice.LastEvent : "none"));
            Need(voice.Source.spatialBlend == 1f && !voice.Source.loop && Mathf.Abs(voice.Source.maxDistance - 22f) < .01f, name + " voice owns a bounded non-looping 3D source");
            if (actor is SmilingMushroomAI awake) Need(awake.WakeCount > 0 && !awake.IsDisguised, "mushroom visibly wakes before its attack");
            Capture("attack-" + name + ".png", actor.transform.position);
            int swung = actor.Swings; yield return Wait(() => actor.StateName == "Recover" || actor.Swings > swung, 3, name + " leaves first attack");
            Need(hitEvents == 1, name + " first accepted native hit does not repeat damage within its swing");
            if (actor is BishopKnightAI contactBoss) yield return KnightNativeContactChecks(contactBoss);

            actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor)); hitEvents = 0;
            yield return Wait(() => Committed(actor), 10, name + " reaches its native committed precontact attack phase");
            Place(actor.Home + Vector3.right * 5f); before = hp.CurrentHealth;
            yield return new WaitForSeconds(1.45f);
            Need(hp.CurrentHealth == before && hitEvents == 0, name + " committed attack genuinely whiffs after a sideways escape");
            if (actor is BishopKnightAI) yield return KnightReachChecks();

            actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor)); hitEvents = 0;
            yield return Wait(() => Committed(actor), 10, name + " starts cover scenario through native AI");
            float wallDistance = actor is BishopKnightAI ? 1.45f : .85f;
            var wall = Box("QA actual blade occluder", actor.transform.position + actor.transform.forward * wallDistance + Vector3.up * 1.7f, new Vector3(7, 4.2f, .18f));
            Need(!wall.bounds.Intersects(actor.GetComponent<CapsuleCollider>().bounds) && !wall.bounds.Intersects(hero.GetComponent<CharacterController>().bounds),
                name + " actual cover starts clear of both physical bodies");
            before = hp.CurrentHealth; yield return new WaitForSeconds(1.35f);
            Need(hp.CurrentHealth == before && hitEvents == 0, name + " real wall blocks actual native attack damage"); Object.Destroy(wall.gameObject); yield return null;

            actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor)); hitEvents = 0;
            yield return Wait(() => Committed(actor), 10, name + " starts interrupt scenario through native AI");
            Need(Hurt(5, actor is BishopKnightAI ? AttackImpactKind.Heavy : AttackImpactKind.Normal) && actor.StateName == "Stagger" && !actor.HitWindow, name + " controlled accepted player hit (typed heavy for Knight) interrupts the pending attack");
            before = hp.CurrentHealth; yield return new WaitForSeconds(.24f);
            Need(hp.CurrentHealth == before && hitEvents == 0, name + " interrupted native attack has no delayed damage");
            if (actor is BishopKnightAI)
            {
                var feedback = actor.GetComponent<CombatFeedback>();
                Need(feedback && !feedback.IsFlashing,
                    "Knight normal accepted-hit flash expires on its own before palette-restoration inspection");
                CheckStylePropertyBlocks(actor, "Knight after native accepted-hit feedback restores its ordinary tint");
            }
            for (int menu = 0; menu < 3; menu++) yield return MenuCheck(name, menu);
            yield return StyleInputLockCheck(name);
            if (actor is BishopKnightAI knight) yield return KnightChecks(knight);
            if (actor is SmilingMushroomAI sleeper)
            {
                Place(actor.Home + Vector3.forward * 20f);
                yield return Wait(() => sleeper.IsDisguised, 8, "mushroom gives up, returns home and disguises again");
                Need(Vector3.ProjectOnPlane(actor.transform.position - actor.Home, Vector3.up).magnitude < .2f, "mushroom only replants at its safe home");
            }
            yield return StyleHeroDeathCheck(name);
            actor.ResetForJourney(); Place(actor.Home + Vector3.right * 10f);
            var enemyHP = actor.GetComponent<Health>(); Need(Hurt(10000), name + " controlled lethal Health event accepted");
            Need(actor.IsDead && !actor.GetComponent<Collider>().enabled && actor.CurrentAction == "Die", name + " death disables collision and plays native Die");
            if (actor is BishopKnightAI deadBoss)
                Need(!deadBoss.TerritoryEngaged && !deadBoss.HudEligible && !HudVisible(deadBoss), "dead miniboss immediately hides its territory HUD");
            yield return null; // The style receives its normal death gate update.
            var deadStyle = actor.GetComponent<AdventureCreatureStyle>();
            float deathClock = deadStyle.StyleClock;
            yield return new WaitForSeconds(.85f);
            Need(Mathf.Abs(deadStyle.StyleClock - deathClock) < .001f, name + " actual style clock stops throughout native death");
            CheckStylePropertyBlocks(actor, name + " native death", 0);
            var loot = actor.GetComponent<StoneScoutLootDrop>(); var drop = loot ? Field(loot, "activeDrop") as StoneScoutVitalPickup : null;
            Need(drop, name + " native death produces its one existing heal drop"); yield return new WaitForSeconds(.15f);
            Need(ReferenceEquals(drop, Field(loot, "activeDrop")) && actor.CurrentAction == "Die", name + " death holds without replay or duplicate drop");
            actor.ResetForJourney(); Need(!actor.IsDead && enemyHP.CurrentHealth == enemyHP.MaxHealth && actor.GetComponent<Collider>().enabled && Vector3.Distance(actor.transform.position, actor.Home) < .05f, name + " journey reset restores full health, collider and exact home");
            Need(deadStyle.StyleClock == 0 && deadStyle.TechPulse == 0,
                name + " direct native journey reset synchronously clears its instance clock and tech anticipation");
            CheckStylePropertyBlocks(actor, name + " synchronous journey reset", 0);
            if (actor is BishopKnightAI resetBoss)
                Need(!resetBoss.TerritoryEngaged && !resetBoss.HudEligible && !HudVisible(resetBoss), "journey reset clears the miniboss territory HUD before reacquisition");
            Need(Field(loot, "activeDrop") == null, name + " journey reset clears previous loot");
            Object.Destroy(instance); actor = null; yield return null;
        }
        if (!SessionState.GetBool(Prefix + "knightOnly", false)) yield return DemonVoiceCheck();
        Need(missingActions == 0, "all requested native actions exist; no missing-action runtime errors");
    }

    static IEnumerator DemonVoiceCheck()
    {
        StoneScoutRoadsideAI ai = null;
        foreach (var candidate in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None))
            if (candidate.GetComponentInChildren<DemonKingCombatVisual>()) { ai = candidate; break; }
        Need(ai, "existing authored DemonKing is available for voice regression");
        demon = ai.gameObject; demonVisual = demon.GetComponentInChildren<DemonKingCombatVisual>();
        var voice = demon.GetComponent<AdventureCreatureVoice>();
        Need(voice && voice.Source, "authored DemonKing automatically owns its new creature voice");
        Vector3 home = Origin + Vector3.up * .025f;
        Set(ai, "home", home); Set(ai, "homeFacing", Quaternion.identity);
        demon.GetComponent<StoneScoutCombatTarget>().ResetForJourney();
        Place(home + Vector3.forward * 4f); demonEvents = 0; int before = hp.CurrentHealth;
        Capture("fixture-start-DemonKing.png", home); ai.enabled = true; Physics.SyncTransforms();
        yield return Wait(() => demonEvents > 0, 17, "authored DemonKing autonomously approaches and lands its real native blade contact");
        Need(demonEvents == 1 && before - hp.CurrentHealth == 8 && hpAtContact == 8 &&
            ageAtContact >= demonVisual.contactStart - .035f && ageAtContact <= demonVisual.contactEnd + .035f,
            "DemonKing keeps one 8 HP native contact within its blade window: " + ageAtContact.ToString("F3"));
        Need(voice.PlayedCount > 0 && voice.Source.spatialBlend == 1f && !voice.Source.loop && Mathf.Abs(voice.Source.maxDistance - 22f) < .01f,
            "DemonKing autonomous encounter actually triggers its bounded generated 3D voice: " + voice.LastEvent + " (not a perceptual audition)");
        Capture("attack-DemonKing.png", demon.transform.position);
        yield return Wait(() => demonVisual.AttackElapsed >= 0 && (float)Field(voice, "attackDelay") > .12f, 8,
            "DemonKing next native attack has a real pending relative voice delay");
        float delay = (float)Field(voice, "attackDelay"), attackAge = demonVisual.AttackElapsed;
        int delayedCount = voice.PlayedCount; movement.SetGameplayInputLocked(true);
        yield return new WaitForSecondsRealtime(.24f);
        Need(Time.timeScale > 0 && !vitals.EnemyCombatActive && Mathf.Abs((float)Field(voice, "attackDelay") - delay) < .001f &&
            Mathf.Abs(demonVisual.AttackElapsed - attackAge) < .025f && voice.PlayedCount == delayedCount,
            "native external input lock freezes pending attack voice countdown without altering time scale");
        movement.SetGameplayInputLocked(false);
        yield return Wait(() => voice.PlayedCount > delayedCount && voice.LastEvent.StartsWith("Attack", StringComparison.Ordinal), 2,
            "native external unlock resumes, rather than expires, the pending attack voice");
        ai.enabled = false; // Controlled Health cases below are not extra native swings.
        int played = voice.PlayedCount;
        Need(demon.GetComponent<Health>().TryApplyDamage(new DamageInfo(1, hero.gameObject, demon.transform.position + Vector3.up, demon.transform.position - hero.position)) &&
            voice.PlayedCount == played + 1 && voice.LastEvent.StartsWith("Hurt", StringComparison.Ordinal),
            "controlled accepted DemonKing Health hit plays its own generated hurt voice");
        yield return null;
        Need(voice.VoicePlaying, "DemonKing hurt clip is actually playing before the real Esc pause test");
        flow.SetMenu(true); Need(flow.MenuOpen, "real Esc handler opens during DemonKing voice playback");
        yield return null; float time = voice.Source.time; int health = hp.CurrentHealth;
        yield return new WaitForSecondsRealtime(.18f);
        Need(!vitals.EnemyCombatActive && Mathf.Abs(voice.Source.time - time) < .03f && hp.CurrentHealth == health,
            "DemonKing real menu pause freezes the actually playing voice clock and combat");
        flow.SetMenu(false); yield return Wait(() => vitals.EnemyCombatActive && Time.timeScale > 0, 5, "native menu close restores the world after DemonKing audio pause");
        played = voice.PlayedCount;
        Need(demon.GetComponent<Health>().TryApplyDamage(new DamageInfo(10000, hero.gameObject, demon.transform.position + Vector3.up, demon.transform.position - hero.position)) &&
            voice.PlayedCount == played + 1 && voice.LastEvent == "Death1", "controlled lethal DemonKing Health event plays its generated death voice");
        demon.GetComponent<StoneScoutCombatTarget>().ResetForJourney();
        yield return new WaitForSeconds(.22f); // Existing hurt-voice cooldown, not an injected voice reset.
        var combat = hero.GetComponent<PlayerCombat>(); Need(combat && combat.enabled, "real PlayerCombat hit-stop owner is active");
        played = voice.PlayedCount; combat.RegisterHeroHit(false, demon.transform.position + Vector3.up);
        Need(combat.IsApplyingHitStop && Time.timeScale == 0 && vitals.EnemyCombatActive, "real RegisterHeroHit owns a short combat hit-stop, distinct from menus");
        Need(demon.GetComponent<Health>().TryApplyDamage(new DamageInfo(1, hero.gameObject, demon.transform.position + Vector3.up, demon.transform.position - hero.position)) && voice.PlayedCount == played &&
            ((string)Field(voice, "queuedReaction")).StartsWith("Hurt", StringComparison.Ordinal), "controlled hit during actual owned hit-stop queues hurt without premature playback");
        yield return Wait(() => !combat.IsApplyingHitStop && Time.timeScale > 0 && voice.PlayedCount == played + 1 && voice.LastEvent.StartsWith("Hurt", StringComparison.Ordinal), 1,
            "native hit-stop expiry flushes exactly one queued hurt voice");
        yield return new WaitForSeconds(.22f);
        played = voice.PlayedCount; combat.RegisterHeroHit(true, demon.transform.position + Vector3.up);
        Need(combat.IsApplyingHitStop && Time.timeScale == 0 && vitals.EnemyCombatActive, "real fatal-hit feedback owns its bounded combat hit-stop");
        Need(demon.GetComponent<Health>().TryApplyDamage(new DamageInfo(1, hero.gameObject, demon.transform.position + Vector3.up, demon.transform.position - hero.position)) &&
            demon.GetComponent<Health>().TryApplyDamage(new DamageInfo(10000, hero.gameObject, demon.transform.position + Vector3.up, demon.transform.position - hero.position)) &&
            voice.PlayedCount == played && (string)Field(voice, "queuedReaction") == "Death1", "controlled hurt then death during actual owned stop keeps only the stronger Death1 reaction");
        yield return Wait(() => !combat.IsApplyingHitStop && Time.timeScale > 0 && voice.PlayedCount == played + 1 && voice.LastEvent == "Death1", 1,
            "native hit-stop expiry plays queued Death1 once even though the enemy is already dead");
        yield return new WaitForSeconds(.12f); Need(voice.PlayedCount == played + 1, "queued fatal reaction is not replayed on later dead updates");
        demon.GetComponent<StoneScoutCombatTarget>().ResetForJourney(); demon = null; demonVisual = null;
    }

    static IEnumerator MenuCheck(string name, int menu)
    {
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        var knight = actor as BishopKnightAI;
        if (knight) Need(Hurt(5), "controlled pre-menu miniboss damage is accepted while the reset shield is down");
        yield return Wait(() => actor is SmilingMushroomAI ? actor.StateName == "Wake" : Committed(actor), 10, name + " native phase ready for pause case " + menu);
        var style = actor.GetComponent<AdventureCreatureStyle>();
        Need(style, name + " owns its instance style component");
        int bossHealth = knight ? knight.CurrentHealth : 0, bossResets = knight ? knight.TerritoryResetCount : 0;
        if (knight) Need(knight.TerritoryEngaged && knight.HudEligible && HudVisible(knight) && bossHealth < knight.MaximumHealth,
            "damaged miniboss encounter and boss HUD are live before menu " + menu);
        if (menu == 0) { world.SetJournal(true); Need(world.JournalOpen, "real J journal handler opens"); }
        else if (menu == 1) Need(flow.ShowMap() && flow.MapOpen, "real M map handler opens");
        else { flow.SetMenu(true); Need(flow.MenuOpen, "real Esc menu handler opens"); }
        string phase = actor.StateName; float age = Clock(actor); int swings = actor.Swings, health = hp.CurrentHealth; Vector3 position = actor.transform.position;
        var voice = actor.GetComponent<AdventureCreatureVoice>(); bool voiceWasPlaying = voice && voice.VoicePlaying;
        yield return null; // Native voice Update gets its own normal suspension tick.
        float voiceTime = voice && voice.Source ? voice.Source.time : 0f;
        float styleClock = style.StyleClock, techPulse = style.TechPulse;
        yield return new WaitForSecondsRealtime(.18f);
        Need(!vitals.EnemyCombatActive && actor.StateName == phase && Mathf.Abs(Clock(actor) - age) < .001f && actor.Swings == swings && hp.CurrentHealth == health && Vector3.Distance(position, actor.transform.position) < .002f,
            name + " menu " + menu + " freezes phase, native brain clock, swings, damage and movement");
        Need(Mathf.Abs(style.StyleClock - styleClock) < .001f && Mathf.Abs(style.TechPulse - techPulse) < .001f,
            name + " menu " + menu + " freezes the actual style clock and tech pulse");
        CheckStylePropertyBlocks(actor, name + " suspended menu " + menu);
        if (knight) Need(knight.TerritoryEngaged && !knight.HudEligible && !HudVisible(knight) && knight.CurrentHealth == bossHealth && knight.TerritoryResetCount == bossResets,
            "menu " + menu + " hides boss HUD while preserving damaged territory memory and reset counter");
        if (voiceWasPlaying && voice.Source.clip && voiceTime > .001f)
            Need(Mathf.Abs(voice.Source.time - voiceTime) < .03f, name + " menu " + menu + " pauses the actually playing creature voice clock");
        else Log("INFO " + name + " menu " + menu + " had no active voice sample; no audio pause claim for this case.");
        if (menu == 0) world.SetJournal(false); else flow.SetMenu(false);
        yield return Wait(() => vitals.EnemyCombatActive && Time.timeScale > 0, 5, "native menu close restores combat gates");
        yield return Wait(() => actor.StateName != phase || Clock(actor) > age + .03f, 1, name + " menu " + menu + " resumes its existing brain clock");
        yield return Wait(() => style.StyleClock > styleClock + .025f, 1, name + " menu " + menu + " resumes its existing instance style clock");
        Need(actor.StateName != "Return", name + " menu " + menu + " does not cancel encounter into Return");
        CheckStylePropertyBlocks(actor, name + " resumed menu " + menu);
        if (knight) Need(knight.TerritoryEngaged && knight.HudEligible && HudVisible(knight) && knight.CurrentHealth == bossHealth && knight.TerritoryResetCount == bossResets,
            "menu " + menu + " restores live boss HUD without healing or resetting the encounter");
    }

    static IEnumerator StyleInputLockCheck(string name)
    {
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        yield return Wait(() => Committed(actor), 10, name + " native committed attack starts the style input-lock case");
        var style = actor.GetComponent<AdventureCreatureStyle>(); Need(style, name + " real style component exists for external input lock");
        movement.SetGameplayInputLocked(true); yield return null;
        float clock = style.StyleClock, pulse = style.TechPulse, brain = Clock(actor);
        int swings = actor.Swings, playerHealth = hp.CurrentHealth;
        yield return new WaitForSecondsRealtime(.18f);
        Need(Time.timeScale > 0 && !vitals.EnemyCombatActive && Mathf.Abs(style.StyleClock - clock) < .001f &&
            Mathf.Abs(style.TechPulse - pulse) < .001f && Mathf.Abs(Clock(actor) - brain) < .001f && actor.Swings == swings && hp.CurrentHealth == playerHealth,
            name + " external input lock freezes real MPB clock/pulse and pending native attack with global time still running");
        CheckStylePropertyBlocks(actor, name + " external input lock");
        if (actor is BishopKnightAI lockedBoss)
            Need(lockedBoss.TerritoryEngaged && !lockedBoss.HudEligible && !HudVisible(lockedBoss), "external input lock suspends boss HUD and retains territory memory");
        movement.SetGameplayInputLocked(false);
        yield return Wait(() => vitals.EnemyCombatActive && style.StyleClock > clock + .025f, 2, name + " external unlock resumes the existing style clock");
    }

    static IEnumerator StyleHeroDeathCheck(string name)
    {
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        yield return Wait(() => Committed(actor), 10, name + " native committed phase prepares actual Hero-death style lifecycle case");
        var style = actor.GetComponent<AdventureCreatureStyle>();
        Need(style.StyleClock > 0, name + " style clock was genuinely advancing before actual Hero death");
        Need(hp.TryApplyDamage(new DamageInfo(10000, actor.gameObject, hero.position + Vector3.up, hero.position - actor.transform.position)) && hp.IsDead,
            name + " controlled accepted lethal Hero Health event starts the native Hero death animation");
        yield return null;
        Need(!adapter.DeathAnimationFinished && Time.timeScale > 0,
            name + " lifecycle check runs during the native Hero death animation before its delayed death overlay");
        yield return null;
        Need(style.StyleClock == 0 && style.TechPulse == 0,
            name + " actual Hero death clears style clock and anticipation before the death overlay appears");
        CheckStylePropertyBlocks(actor, name + " actual Hero death", 0);
        if (actor is BishopKnightAI knight)
            Need(!knight.HudEligible && !HudVisible(knight), "actual Hero death immediately hides the territorial boss HUD");
        // Use the existing world restoration helpers after observing the native
        // death gate; no replacement player, fake combat gate, or scene save.
        hero.GetComponent<MainWorldDeathUI>().DismissForWorldRestore();
        Place(actor.Home + Vector3.forward * FixtureDistance(actor)); actor.ResetForJourney();
        yield return Wait(() => !hp.IsDead && vitals.EnemyCombatActive && Time.timeScale > 0 && style.StyleClock > 0, 2,
            name + " real Hero restoration and native actor reset resume a fresh instance style clock");
    }

    static IEnumerator KnightChecks(BishopKnightAI knight)
    {
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        yield return Wait(() => knight.ShieldRaised, 10, "knight autonomously raises actual shield");
        var health = actor.GetComponent<Health>(); int before = health.CurrentHealth;
        Need(!Hurt(5) && health.CurrentHealth == before && knight.Blocks == 1, "controlled frontal light Health contact is blocked without HP loss");
        Need(!Hurt(80) && !Hurt(80) && health.CurrentHealth == before && knight.ShieldRaised && knight.GuardBreaks == 0,
            "three frontal normal hits, including 80-damage weapon hits, do not break guard or bypass HP protection");
        Need(Mathf.Abs(knight.GuardStrain-.36f)<.01f,"light pressure is independent of weapon damage");
        Need(Hurt(1,AttackImpactKind.Charged) && health.CurrentHealth==before-1 && knight.GuardBroken,
            "one low-damage typed charged strike breaks guard and applies its original HP damage");
        yield return new WaitForSeconds(.12f); Need(knight.CurrentAction == "Dizzy" && !knight.ShieldRaised, "typed charged shield break remains a real punish opening");
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        yield return Wait(() => knight.ShieldRaised, 10, "knight shields for flank case");
        Place(actor.transform.position - actor.transform.forward * 1.6f); before = health.CurrentHealth;
        Need(Hurt(5) && health.CurrentHealth == before - 5 && knight.StateName == "Stagger", "controlled rear Health contact bypasses shield and enters GetHit");
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        yield return Wait(() => knight.ShieldRaised, 10, "knight shields for heavy break case"); before = health.CurrentHealth;
        Need(!Hurt(1,AttackImpactKind.Heavy) && health.CurrentHealth==before && knight.ShieldRaised && knight.GuardStrain>.59f,
            "first typed heavy builds sixty percent guard pressure without HP bypass");
        Need(Hurt(1,AttackImpactKind.Heavy) && health.CurrentHealth == before - 1 && knight.StateName == "Dizzy", "second typed heavy breaks guard independent of raw damage");
        yield return new WaitForSeconds(.10f); Need(knight.StateName == "Dizzy" && knight.CurrentAction == "Dizzy", "accepted heavy-hit callback does not overwrite Dizzy with GetHit");
        yield return Wait(() => knight.StateName == "Recover", 3, "native heavy-break punish period reaches shield-down recovery"); before = health.CurrentHealth;
        Need(Hurt(5) && health.CurrentHealth == before - 5 && !knight.ShieldRaised, "controlled recovery Health hit is not blocked by an invisible shield");
        yield return KnightConceptChecks(knight);
        yield return KnightContinuousApproachCheck(knight);
        yield return KnightTerritoryChecks(knight);
    }

    static IEnumerator KnightConceptChecks(BishopKnightAI knight)
    {
        yield return KnightCoreRulesChecks(knight);
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        var vfx=knight.GetComponent<BishopKnightCombatVfx>();
        Need(vfx, "Knight automatically installs reusable skill presentation on existing prefab");
        yield return Wait(()=>knight.ShieldRaised && vfx.GuardVisible,10,"real shield phase displays its frontal mint energy");
        Capture("skill-01-guard.png",knight.transform.position, new Vector3(6,4,7));
        var shieldVfx=knight.GetComponent<BishopKnightShieldVfx>();
        Need(shieldVfx && shieldVfx.OverlayVisible,"native shield has its independent energy engraving overlay");
        Need(!Hurt(5),"light hit is blocked by the real guard entry");yield return null;
        Need(shieldVfx.ShieldState=="Impact","light contact enters distinct shield impact state");
        Capture("skill-08-light-contact.png",knight.transform.position,new Vector3(5,2,6));
        yield return new WaitForSeconds(.24f);
        Need(!Hurt(5,AttackImpactKind.Heavy),"typed heavy after light reaches critical guard pressure");yield return null;
        Need(knight.GuardCritical && shieldVfx.ShieldState=="Critical","guard strain shows the distinct near-break state");
        Capture("skill-08-critical.png",knight.transform.position,new Vector3(5,2,6));
        Need(Hurt(5,AttackImpactKind.Heavy),"further typed heavy breaks energy guard");yield return null;
        Need(shieldVfx.ShieldState=="Broken" && shieldVfx.BreakVisible && !shieldVfx.OverlayVisible,"break scatters light fragments and removes guard overlay");
        Capture("skill-05-broken.png",knight.transform.position,new Vector3(5,2,6));
        Need(Hurt(5),"guard-break opening accepts normal player damage");
        actor.ResetForJourney(); Place(actor.Home+Vector3.forward*FixtureDistance(actor));
        yield return Wait(()=>knight.ShieldRaised,10,"reset restores guard before attack-stage checks");
        yield return Wait(()=>knight.Telegraphing && vfx.GroundVisible,5,"native sweep windup displays its ground envelope");
        Need(!knight.IsThrust && !vfx.GuardVisible,"sweep telegraph never retains a phantom shield");
        Need(knight.InsideAttackEnvelope(knight.AttackOrigin+knight.AttackFacing*new Vector3(2,0,2)) &&
             !knight.InsideAttackEnvelope(knight.AttackOrigin+knight.AttackFacing*new Vector3(0,0,5)) &&
             !knight.InsideAttackEnvelope(knight.AttackOrigin-knight.AttackFacing*Vector3.forward),
             "shared sweep envelope contains front arc and excludes rear and out-of-reach points");
        Capture("skill-03-sweep-warning.png",knight.transform.position,new Vector3(6,5,7));
        float age=knight.PhaseElapsed;
        flow.SetMenu(true);yield return null;yield return new WaitForSecondsRealtime(.25f);
        Need(!vfx.GroundVisible && !vfx.GuardVisible && !vfx.TrailVisible && Mathf.Abs(knight.PhaseElapsed-age)<.025f,
            "real Esc pause suppresses all cues and freezes native skill clock");
        flow.SetMenu(false);yield return Wait(()=>vfx.GroundVisible,2,"closing menu restores the same pending warning");
        yield return Wait(()=>knight.Attacking && vfx.TrailVisible,2,"actual native sweep emits golden spear-tip trail");
        yield return new WaitForSeconds(.08f);
        Capture("skill-03-sweep-active.png",knight.transform.position,new Vector3(6,5,7));
        yield return Wait(()=>knight.Recovering,3,"sweep ends with shield-down punish window");
        yield return null;
        Need(!vfx.GroundVisible && !vfx.GuardVisible && !vfx.TrailVisible,"recovery clears guard, warning and trail emission");

        // Controlled damage only sets phase-two HP; attacks and contacts remain autonomous.
        Need(Hurt(knight.MaximumHealth/2+1) && knight.Enraged,"controlled half-health damage unlocks phase-two pursuit");
        Place(knight.transform.position+knight.transform.forward*3.1f);
        yield return Wait(()=>knight.Rallying,4,"half health plays one native phase-transition taunt at a safe boundary");
        Need(!knight.HitWindow && !knight.ShieldRaised && knight.RallyCount==1,"phase transition does not deal damage or raise an invisible shield");
        yield return new WaitForSeconds(.2f);
        Capture("skill-06-half-health.png",knight.transform.position,new Vector3(6,4,7));
        yield return Wait(()=>knight.Telegraphing && knight.IsThrust && vfx.GroundVisible,8,"half-health sentinel natively starts its thrust");
        Need(!knight.InsideAttackEnvelope(knight.AttackOrigin+knight.AttackFacing*new Vector3(1.5f,0,2)),
            "thrust footprint excludes a lateral dodge outside the lane");
        Capture("skill-02-thrust-warning.png",knight.transform.position,new Vector3(6,5,7));
        int swings=knight.Swings;
        yield return Wait(()=>knight.ChainPreparing,3,"phase-two thrust enters the explicit follow-up preparation");
        yield return Wait(()=>knight.SecondInPair && knight.Telegraphing && vfx.GroundVisible,2,"second strike has its own visible sweep windup");
        Need(knight.Swings==swings+1 && !knight.IsThrust,"pursuit is exactly thrust then sweep");
        Capture("skill-04-followup-warning.png",knight.transform.position,new Vector3(6,5,7));
        yield return Wait(()=>knight.Recovering,3,"pair ends in recovery without a third attack");
        yield return new WaitForSeconds(1f);
        Need(knight.Recovering && knight.Swings==swings+1,"pair leaves a recovery longer than one second");
        Need(knight.RallyCount==1,"phase transition does not repeat on each new attack");
        Need(Hurt(10000),"controlled lethal damage closes presentation lifecycle");yield return null;
        Need(!vfx.GroundVisible && !vfx.GuardVisible && !vfx.TrailVisible,"death clears all skill effects");
        knight.ResetForJourney();yield return null;
        Need(!knight.Enraged && !knight.SecondInPair && !vfx.GroundVisible,"journey reset clears phase two and queued pursuit");
    }

    static IEnumerator KnightCoreRulesChecks(BishopKnightAI knight)
    {
        knight.ResetForJourney();Place(knight.Home+Vector3.forward*3.1f);
        yield return Wait(()=>knight.TerritoryEngaged && knight.Awareness.Engaged,10,"engaged encounter ready for repeated-light interruption check");
        Need(Hurt(1) && knight.StateName=="Stagger","uncommitted knight can still stagger from a normal hit");
        yield return new WaitForSeconds(.2f);
        float age=knight.PhaseElapsed;
        Need(Hurt(1) && knight.PhaseElapsed>=age && knight.StateName=="Stagger",
            "repeated light hit applies HP damage without restarting the stagger clock");
        yield return new WaitForSeconds(.25f);
        Need(knight.StateName!="Stagger","light-hit repetition cannot indefinitely extend the same stagger");
        float pressureUntil=Time.time+6f;
        float nextLight=Time.time;
        while (!Committed(knight) && Time.time<pressureUntil)
        {
            // A realistic light cadence, below the separate intentional guard
            // break threshold during one guard interval. Observe every frame.
            if(Time.time>=nextLight) { Hurt(1); nextLight=Time.time+.4f; }
            yield return null;
        }
        Need(Committed(knight),"sustained light pressure allows recovery into a committed native attack");
        knight.ResetForJourney();Place(knight.Home+Vector3.forward*3.1f);
        yield return Wait(()=>knight.ShieldRaised,10,"guard is ready for real Hero damage-construction pipeline");
        // Controlled capsule contact through the production Hero Sweep method:
        // validates action-tag propagation, not a manual-input animation playtest.
        var sweep=typeof(HeroWorldAdapter).GetMethod("Sweep",BindingFlags.Instance|BindingFlags.NonPublic);
        var clear=typeof(HeroWorldAdapter).GetMethod("ClearHitSamples",BindingFlags.Instance|BindingFlags.NonPublic);
        Need(sweep!=null && clear!=null,"production hero sweep and per-attack reset are available");
        adapter.combat.Begin("ATK_HEAVY");clear.Invoke(adapter,null);
        Vector3 p=knight.GetComponent<CapsuleCollider>().bounds.center;
        sweep.Invoke(adapter,new object[]{p-Vector3.right*.05f,p+Vector3.right*.05f});
        adapter.CancelForTransition();
        Need(knight.ShieldRaised && Mathf.Abs(knight.GuardStrain-.6f)<.01f,
            "production Hero Sweep carries heavy action identity into shield pressure");
        float pressure=knight.GuardStrain;
        Place(knight.Home+Vector3.forward*6f);
        yield return new WaitForSeconds(2.5f);
        Need(knight.GuardStrain>0 && knight.GuardStrain<pressure-.02f,
            "guard pressure slowly recovers after two seconds without contact");
        knight.ResetForJourney();Place(knight.Home+Vector3.forward*3.1f);
        yield return Wait(()=>Committed(knight),10,"committed attack ready for low-damage heavy interruption");
        Need(Hurt(1,AttackImpactKind.Heavy) && knight.StateName=="Stagger" && !knight.HitWindow,
            "typed heavy with only one HP damage still interrupts the committed attack");
    }

    static IEnumerator KnightContinuousApproachCheck(BishopKnightAI knight)
    {
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        yield return Wait(() => knight.TerritoryEngaged && knight.Awareness.Engaged, 10,
            "actual near perception establishes the Knight encounter before the long continuous approach");
        Place(knight.Home + Vector3.forward * 8.5f);
        yield return Wait(() => knight.StateName == "Approach" || knight.ReturningHome, 3,
            "engaged Knight starts a real approach to the grounded Hero at 8.5m");
        Need(knight.StateName == "Approach" && !knight.ReturningHome,
            "long native territory approach starts without an immediate home return");
        float approachStart = Time.time, until = Time.realtimeSinceStartup + 10;
        Vector3 start = knight.transform.position, previous = start;
        bool progressedAfterThreshold = false;
        while (!knight.ShieldRaised && !knight.ReturningHome && Time.realtimeSinceStartup < until)
        {
            if (knight.StateName == "Approach" && Time.time - approachStart > 1.35f &&
                Vector3.ProjectOnPlane(knight.transform.position - previous, Vector3.up).sqrMagnitude > .000001f)
                progressedAfterThreshold = true;
            previous = knight.transform.position;
            yield return null;
        }
        if (!knight.ShieldRaised || knight.ReturningHome) TimeoutDiagnostics("continuous native 8.5m territory approach");
        Need(knight.ShieldRaised && !knight.ReturningHome && knight.TerritoryEngaged && progressedAfterThreshold &&
            Time.time - approachStart > 1.35f && Vector3.ProjectOnPlane(knight.transform.position - start, Vector3.up).magnitude > 4f,
            "actual native 8.5m approach progresses beyond 1.35s, travels over 4m and reaches Guard without a false stalled return");
    }

    static IEnumerator KnightNativeContactChecks(BishopKnightAI knight)
    {
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor)); hitEvents = 0;
        yield return Wait(() => Committed(knight) && knight.CurrentAction == "Attack01", 10,
            "enlarged Knight natively commits its first Attack01 sweep before actual target placement");
        int firstSwing = knight.Swings;
        int knightBefore=knight.CurrentHealth;
        Need(Hurt(35) && knight.CurrentHealth==knightBefore-35 && knight.Telegraphing && knight.AimLocked,
            "high-damage normal hit damages HP but cannot cancel the committed spear");
        // Original native samples at normalized .45-.60 put this enlarged
        // diagonal shaft near X=2.6m/Z=1.5m. Move the real grounded Hero into
        // that actual lane after commitment, without changing damage or pose.
        Vector3 sweepLane = Quaternion.AngleAxis(60f, Vector3.up) * knight.transform.forward;
        Place(knight.transform.position + sweepLane * 3f); int before = hp.CurrentHealth; contactImage = null;
        Capture("fixture-native-Attack01-BishopKnight.png", knight.transform.position,
            knight.transform.forward * 8.5f + knight.transform.right * 4f + Vector3.up * 1.35f);
        yield return Wait(() => hitEvents > 0 || knight.Recovering || knight.Swings != firstSwing, 3,
            "actual grounded Hero meets the committed native Attack01 shaft before that first sweep ends");
        Need(hitEvents == 1 && before - hp.CurrentHealth == 16 && hpAtContact == 16 && actionAtContact == "Attack01" &&
            ageAtContact >= .58f - .025f && ageAtContact <= .82f + .025f && knight.Swings == firstSwing,
            "actual native Attack01 deals exactly 16HP once inside .58-.82s, on the same committed swing with no later-attack fallback");
        yield return Wait(() => !string.IsNullOrEmpty(contactImage) && File.Exists(contactImage) && new FileInfo(contactImage).Length > 1000, 3,
            "actual accepted Attack01 Game-view screenshot records native sweep, grounded Hero and live miniboss HUD");
        yield return Wait(() => knight.Recovering, 3, "first real Attack01 contact reaches shield-down recovery");

        Place(knight.transform.position + knight.transform.forward * FixtureDistance(knight)); hitEvents = 0; before = hp.CurrentHealth; contactImage = null;
        yield return Wait(() => Committed(knight) && knight.CurrentAction == "Attack02", 8,
            "same native encounter alternates directly from Attack01 into its next Attack02 thrust");
        int secondSwing = knight.Swings;
        Need(secondSwing == firstSwing + 1, "native Attack02 is the immediately next committed swing without reset or skipped attacks");
        yield return Wait(() => hitEvents > 0 || knight.Recovering || knight.Swings != secondSwing, 3,
            "actual grounded frontal Hero meets the immediately next native Attack02 thrust");
        Need(hitEvents == 1 && before - hp.CurrentHealth == 20 && hpAtContact == 20 && actionAtContact == "Attack02" &&
            ageAtContact >= .68f - .025f && ageAtContact <= .94f + .025f && knight.Swings == secondSwing,
            "immediately alternating native Attack02 deals exactly 20HP once inside .68-.94s on that same second swing");
        Need(Mathf.Abs(knight.StrikeDuration - 1.55f) < .001f && !knight.ShieldRaised,
            "alternating native Attack02 retains its 1.55s readable animation and shield-down commitment");
        yield return Wait(() => !string.IsNullOrEmpty(contactImage) && File.Exists(contactImage) && new FileInfo(contactImage).Length > 1000, 3,
            "actual accepted alternating Attack02 Game-view screenshot records its native thrust contact");
        yield return Wait(() => knight.Recovering, 3, "second real spear attack reaches its shield-down recovery");
    }

    static IEnumerator KnightReachChecks()
    {
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor)); hitEvents = 0;
        yield return Wait(() => Committed(actor), 10, "enlarged Knight commits its real spear before the distant escape");
        Place(actor.transform.position + actor.transform.forward * 7f); int before = hp.CurrentHealth;
        yield return new WaitForSeconds(1.5f);
        Need(hitEvents == 0 && hp.CurrentHealth == before, "enlarged real spear does not reach a player who escapes 7m away inside the territory");

        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor)); hitEvents = 0;
        yield return Wait(() => Committed(actor), 10, "enlarged Knight commits its real spear before the behind-shoulder escape");
        Vector3 rearShoulder = Quaternion.AngleAxis(105f, Vector3.up) * actor.transform.forward;
        Place(actor.transform.position + rearShoulder * 2.6f); before = hp.CurrentHealth;
        yield return new WaitForSeconds(1.5f);
        Need(hitEvents == 0 && hp.CurrentHealth == before, "enlarged native spear cannot damage a player behind its committed shoulder");
    }

    static IEnumerator KnightTerritoryChecks(BishopKnightAI knight)
    {
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        yield return Wait(() => knight.ShieldRaised && knight.TerritoryEngaged, 10, "territorial miniboss starts a fresh native guard encounter");
        float guardStarted = Time.time;
        yield return Wait(() => Committed(knight), 3, "untouched native guard ends in a committed spear attack");
        Need(Time.time - guardStarted < 2.3f && !knight.ShieldRaised && (knight.Telegraphing || knight.Attacking),
            "native shield guard has a finite bounded duration and gives way to the spear");
        Quaternion facing = knight.transform.rotation;
        Place(knight.transform.position + knight.transform.right * 5f);
        yield return new WaitForSeconds(.95f);
        Need(Quaternion.Angle(facing, knight.transform.rotation) < 1f && !knight.ShieldRaised,
            "committed enlarged spear retains its original facing through a genuine sideways escape");
        yield return Wait(() => knight.Recovering, 2, "native spear enters a real shield-down recovery");
        Need(!knight.ShieldRaised && knight.TerritoryStatus == "RECOVERY", "boss status reports the actual shield-down recovery opening");

        yield return BeginTerritoryApproach(knight, "lost-sight case");
        Need(Hurt(19), "controlled accepted damage prepares the real inside-territory lost-sight case");
        int insideDamaged = knight.CurrentHealth;
        var sightWall = Box("QA actual territory sight occluder", (knight.transform.position + hero.position) * .5f + Vector3.up * 1.7f,
            new Vector3(7f, 4.2f, .18f));
        Need(!sightWall.bounds.Intersects(knight.GetComponent<CapsuleCollider>().bounds) &&
            !sightWall.bounds.Intersects(hero.GetComponent<CharacterController>().bounds), "territory sight wall starts clear of both physical bodies");
        yield return Wait(() => knight.ReturningHome, 8, "actual wall occlusion makes the damaged Knight return without moving the player outside its territory");
        Need(knight.CurrentHealth == insideDamaged, "inside-territory lost sight does not heal at return start");
        yield return Wait(() => !knight.ReturningHome && Vector3.ProjectOnPlane(knight.transform.position - knight.Home, Vector3.up).magnitude < .2f, 8,
            "damaged Knight physically reaches its safe home while the occluded player remains inside the territory");
        Need(knight.CurrentHealth == insideDamaged, "actual safe-home return preserves damage when the player is still inside the territory");
        Object.Destroy(sightWall.gameObject); yield return null;
        yield return Wait(() => knight.TerritoryEngaged && knight.HudEligible && HudVisible(knight), 8,
            "removing actual sight cover resumes the damaged territory encounter with its boss HUD");
        Need(knight.CurrentHealth == insideDamaged, "native reacquisition inside the territory retains all miniboss HP loss");

        yield return BeginTerritoryApproach(knight, "leash return");
        Need(Hurt(19) && knight.CurrentHealth == knight.MaximumHealth - 19, "controlled accepted territory hit produces persistent miniboss HP loss");
        int damaged = knight.CurrentHealth, resets = knight.TerritoryResetCount;
        Place(knight.Home + Vector3.forward * (knight.leashRange + 4f));
        yield return Wait(() => knight.ReturningHome, 4, "leaving the actual territory makes the damaged miniboss return home");
        Need(knight.CurrentHealth == damaged && !knight.HudEligible && !HudVisible(knight), "starting leash return hides boss HUD without instant healing");
        flow.SetMenu(true); yield return null;
        Vector3 returningPosition = knight.transform.position;
        yield return new WaitForSecondsRealtime(.18f);
        Need(knight.ReturningHome && knight.CurrentHealth == damaged && knight.TerritoryResetCount == resets &&
            Vector3.Distance(knight.transform.position, returningPosition) < .002f && !HudVisible(knight),
            "real Esc during return preserves HP loss, home-return phase and reset count");
        flow.SetMenu(false);
        yield return Wait(() => vitals.EnemyCombatActive && Time.timeScale > 0, 5, "Esc close resumes the native home return");
        yield return Wait(() => !knight.TerritoryEngaged && !knight.ReturningHome && knight.TerritoryResetCount == resets + 1, 10,
            "completed native disengagement at safe home resets the territory exactly once");
        Need(knight.CurrentHealth == knight.MaximumHealth && Vector3.ProjectOnPlane(knight.transform.position - knight.Home, Vector3.up).magnitude < .2f &&
            !knight.HudEligible && !HudVisible(knight), "completed safe-home return restores miniboss health with its HUD hidden");
        yield return new WaitForSeconds(.25f);
        Need(knight.TerritoryResetCount == resets + 1 && knight.CurrentHealth == knight.MaximumHealth,
            "disengaged territory does not repeat its health-reset event every idle frame");
        Place(knight.Home + Vector3.forward * FixtureDistance(knight));
        yield return Wait(() => knight.TerritoryEngaged && knight.HudEligible && HudVisible(knight), 8,
            "real return to the territory starts a fresh encounter and restores the boss HUD");
    }

    static IEnumerator BeginTerritoryApproach(BishopKnightAI knight, string context)
    {
        actor.ResetForJourney(); Place(actor.Home + Vector3.forward * FixtureDistance(actor));
        // At seven metres recognition takes about three seconds, longer than
        // the native .8s idle pause before its randomized patrol can turn away.
        // Acquire through actual near perception, then move the real Hero to
        // seven metres so this case tests the already-engaged territory return.
        yield return Wait(() => knight.TerritoryEngaged && knight.Awareness.Engaged, 10,
            "native near perception establishes the actual territory encounter before " + context);
        Place(knight.Home + Vector3.forward * 7f);
        yield return Wait(() => knight.TerritoryEngaged && Vector3.ProjectOnPlane(knight.transform.position - knight.Home, Vector3.up).magnitude > .65f, 10,
            "native enlarged miniboss physically approaches the actual 7m target before " + context);
    }

    static float FixtureDistance(AdventureCreatureAI ai) => ai is BishopKnightAI ? 3.1f : 2.05f;

    static bool HudVisible(BishopKnightAI knight)
    {
        var hud = knight.GetComponent<BishopKnightTerritoryHud>();
        return hud && hud.Visible;
    }

    static void CheckStylePropertyBlocks(AdventureCreatureAI ai, string context, float live = 1)
    {
        var style = ai.GetComponent<AdventureCreatureStyle>();
        Need(style && style.targetRenderers != null && style.targetRenderers.Length > 0, context + " has explicit instance renderer bindings");
        var block = new MaterialPropertyBlock();
        foreach (var renderer in style.targetRenderers)
        {
            Need(renderer, context + " renderer binding remains alive"); renderer.GetPropertyBlock(block);
            Need(Mathf.Abs(block.GetFloat("_StyleClock") - style.StyleClock) < .001f && Mathf.Abs(block.GetFloat("_TechPulse") - style.TechPulse) < .001f &&
                Mathf.Abs(block.GetFloat("_StyleLive") - live) < .001f,
                context + " actual renderer MPB contains its clock, tech pulse and lifecycle live gate: " + renderer.name);
        }
        if (ai is BishopKnightAI && AdventureTrioStyleInstaller.IsRemodeledKnight(ai.gameObject))
            CheckSentinelPalette(style.targetRenderers, context);
    }

    static void CheckSentinelPalette(Renderer[] renderers, string context)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        var block = new MaterialPropertyBlock();
        var indexedBlock = new MaterialPropertyBlock();
        foreach (var renderer in renderers)
        {
            renderer.GetPropertyBlock(block);
            Need(!block.HasColor("_BaseColor") || SameSentinelColor(block.GetColor("_BaseColor"), Color.white),
                context + " renderer feedback tint is absent or restored neutral white: " + renderer.name);
            Need(!block.HasColor("_ArmorColor"),
                context + " renderer MPB does not replace independent submesh palettes: " + renderer.name);
            if (renderer.name == "Bishop_Shield")
            {
                SilverSentinelRemodelInstaller.ValidateOriginalShield(renderer.GetComponentInParent<BishopKnightAI>().gameObject);
                KnightShieldFinishInstaller.Validate(renderer.sharedMaterial);
                Need(renderer.sharedMaterials.Length==1 && renderer.sharedMaterial.GetColor("_BaseColor")==Color.white,
                    context + " original shield mesh/material and neutral feedback tint survive runtime lifecycle");
                continue;
            }
            if (renderer.name == "Bishop_Spear")
            {
                CheckOriginalSpearRenderer(renderer, context);
                renderer.GetPropertyBlock(indexedBlock, 0);
                Need(!indexedBlock.HasColor("_ArmorColor") &&
                    (!indexedBlock.HasColor("_BaseColor") || SameSentinelColor(indexedBlock.GetColor("_BaseColor"), Color.white)),
                    context + " original spear indexed feedback tint remains neutral without a remodeled palette override");
                continue;
            }
            var materials = renderer.sharedMaterials; // Read only: never instantiate renderer.material(s).
            Need(materials.Length > 0, context + " remodeled renderer retains material assignments: " + renderer.name);
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                Need(material && material.shader && material.shader.name == "FoldedRealm/SilverSentinelToon" &&
                    SentinelPalette.ContainsKey(material.name), context + " submesh retains its approved independent material role: " + renderer.name + "/" + i);
                roles.Add(material.name);
                Need(material.HasProperty("_ArmorColor") && SameSentinelColor(material.GetColor("_ArmorColor"), SentinelPalette[material.name]) &&
                    material.HasProperty("_BaseColor") && SameSentinelColor(material.GetColor("_BaseColor"), Color.white),
                    context + " shared material role survives feedback and lifecycle without palette mutation: " + material.name);
                renderer.GetPropertyBlock(indexedBlock, i);
                Need(!indexedBlock.HasColor("_ArmorColor") &&
                    (!indexedBlock.HasColor("_BaseColor") || SameSentinelColor(indexedBlock.GetColor("_BaseColor"), Color.white)),
                    context + " indexed material MPB cannot flatten its authored palette: " + renderer.name + "/" + i);
            }
        }
        Need(roles.SetEquals(SentinelPalette.Keys), context + " actual bound renderers retain all five distinct approved palette roles");
    }

    static void CheckOriginalSpearRenderer(Renderer renderer, string context)
    {
        var native = AssetDatabase.LoadAssetAtPath<GameObject>(NativeKnightPrefab);
        Need(native && renderer is MeshRenderer, context + " original spear renderer type and native reference are available");
        var original = native.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Bishop_Spear");
        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        Need(mesh && mesh == original.GetComponent<MeshFilter>().sharedMesh &&
            AssetDatabase.GetAssetPath(mesh) == OriginalSpearMeshPath && mesh.vertexCount == 377,
            context + " original spear keeps its exact native mesh object and vertices through the runtime lifecycle");
        var material = AssetDatabase.LoadAssetAtPath<Material>(OriginalSpearMaterialPath);
        Need(material && renderer.sharedMaterials.Length == 1 && renderer.sharedMaterial == material &&
            AssetDatabase.GetAssetPath(material) == OriginalSpearMaterialPath && material.shader && material.shader.name == "FoldedRealm/AdventureCreatureToon" &&
            material.HasProperty("_BaseColor") && SameSentinelColor(material.GetColor("_BaseColor"), Color.white),
            context + " original spear keeps its exact pre-remodel single material, shader and white feedback tint");
    }

    static Color SentinelColor(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    static bool SameSentinelColor(Color actual, Color expected) =>
        Mathf.Abs(actual.r - expected.r) < .002f && Mathf.Abs(actual.g - expected.g) < .002f &&
        Mathf.Abs(actual.b - expected.b) < .002f && Mathf.Abs(actual.a - expected.a) < .002f;

    static bool Committed(AdventureCreatureAI ai)
    {
        string state = ai.StateName; float age = Clock(ai);
        if (ai is FishmanAI) return state == "Attack" && age >= .22f && age < .35f;
        if (ai is BishopKnightAI) return state == "Windup" && age >= .18f && age < .38f;
        return state == "Attack" && age >= .04f && age < .15f;
    }
    static float Clock(AdventureCreatureAI ai) => Convert.ToSingle(Field(ai, ai is BishopKnightAI ? "elapsed" : "age"));
    static void PlayerDamaged(Health target, DamageInfo hit)
    {
        if (demon && hit.Source == demon)
        { demonEvents++; hpAtContact = hit.Amount; ageAtContact = demonVisual.AttackElapsed; return; }
        if (!actor || hit.Source != actor.gameObject) return;
        hitEvents++; hpAtContact = hit.Amount; ageAtContact = (float)Field(actor, "strikeAge"); actionAtContact = actor.CurrentAction;
        if (string.IsNullOrEmpty(contactImage))
        {
            contactImage = Path.Combine(Output, "native-first-contact-" + actor.GetType().Name + "-" + actionAtContact + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");
            ScreenCapture.CaptureScreenshot(contactImage);
            Log("CAPTURE actual Game view at native accepted contact: " + contactImage);
        }
    }
    static bool Hurt(int damage, AttackImpactKind kind=AttackImpactKind.Normal) => actor.GetComponent<Health>().TryApplyDamage(new DamageInfo(damage, hero.gameObject, actor.transform.position + Vector3.up, actor.transform.position - hero.position,kind));
    static void Place(Vector3 position)
    {
        hp.RestoreFullHealth(); adapter.ResetForRespawn(); movement.enabled = true;
        movement.Relocate(position, Quaternion.Euler(0, 180, 0)); vitals.Refill(); movement.enabled = false; Physics.SyncTransforms();
    }
    static void Disable<T>() where T : Behaviour
    { foreach (var item in Object.FindObjectsByType<T>(FindObjectsSortMode.None)) item.enabled = false; }
    static Collider Box(string name, Vector3 position, Vector3 scale)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube); root.name = name; root.transform.position = position; root.transform.localScale = scale;
        Physics.SyncTransforms(); return root.GetComponent<Collider>();
    }
    static object Field(object owner, string name)
    {
        for (Type type = owner.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); if (field != null) return field.GetValue(owner); }
        throw new MissingFieldException(owner.GetType().Name, name);
    }
    static void Set(object owner, string name, object value)
    {
        for (Type type = owner.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); if (field != null) { field.SetValue(owner, value); return; } }
        throw new MissingFieldException(owner.GetType().Name, name);
    }
    static IEnumerator Wait(Func<bool> condition, float seconds, string name)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        bool success = condition();
        if (!success && actor) TimeoutDiagnostics(name);
        Need(success, name + (actor ? " [" + actor.StateName + ", " + actor.CurrentAction + ", " + Clock(actor).ToString("F2") + "]" : ""));
    }
    static void TimeoutDiagnostics(string context)
    {
        var rb = actor.GetComponent<Rigidbody>(); var awareness = actor.Awareness;
        Vector3 toward = hero ? Vector3.ProjectOnPlane(hero.position - actor.transform.position, Vector3.up) : Vector3.zero;
        Log("TIMEOUT " + context + " actor=" + actor.name + " phase=" + actor.StateName + " action=" + actor.CurrentAction +
            " age=" + Clock(actor).ToString("F3") + " position=" + actor.transform.position.ToString("F4") + " home=" + actor.Home.ToString("F4") +
            " forward=" + actor.transform.forward.ToString("F4") + " rbPosition=" + rb.position.ToString("F4") + " rbFacing=" + rb.rotation.eulerAngles.ToString("F3") +
            " interpolation=" + rb.interpolation + " enabled=" + actor.isActiveAndEnabled + " actorHP=" + actor.GetComponent<Health>().CurrentHealth +
            " hero=" + (hero ? hero.position.ToString("F4") : "missing") + " heroHP=" + (hp ? hp.CurrentHealth.ToString() : "missing") +
            " flatDistance=" + toward.magnitude.ToString("F3") + " forwardDot=" + (toward.sqrMagnitude > .0001f ? Vector3.Dot(actor.transform.forward, toward.normalized).ToString("F3") : "n/a") +
            " combatActive=" + (vitals && vitals.EnemyCombatActive) + " timeScale=" + Time.timeScale + " flowBlocked=" + (flow && flow.BlockWorldInput) +
            " externalInputLock=" + (movement && movement.HasExternalInputLock) + " heroInput=" + (adapter && adapter.AcceptInput) + " swings=" + actor.Swings + " contacts=" + actor.Contacts);
        if (awareness) Log("TIMEOUT awareness state=" + awareness.State + " evidence=" + awareness.Evidence.ToString("F4") +
            " visible=" + awareness.Visible + " engaged=" + awareness.Engaged + " ready=" + awareness.Ready +
            " confirmedAge=" + awareness.ConfirmationAge.ToString("F3") + " interest=" + awareness.InterestPosition.ToString("F4"));
        var targetBody = Field(actor, "playerBody") as CharacterController;
        var nativeSight = typeof(AdventureCreatureAI).GetMethod("HasSight", BindingFlags.Instance | BindingFlags.NonPublic);
        Log("TIMEOUT actual native HasSight=" + (nativeSight != null ? nativeSight.Invoke(actor, null).ToString() : "missing method") +
            " cachedPlayerMatchesHero=" + ReferenceEquals(Field(actor, "player"), vitals));
        if (targetBody)
        {
            Vector3 from = actor.GetComponent<CapsuleCollider>().bounds.center, to = targetBody.bounds.center, delta = to - from;
            var blockers = Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider && !h.collider.transform.IsChildOf(actor.transform) && !h.collider.transform.IsChildOf(targetBody.transform))
                .OrderBy(h => h.distance);
            foreach (var blocker in blockers) Log("TIMEOUT LOS blocker=" + blocker.collider.name + " position=" + blocker.collider.transform.position.ToString("F4") +
                " distance=" + blocker.distance.ToString("F4") + " point=" + blocker.point.ToString("F4"));
            Log("TIMEOUT LOS from=" + from.ToString("F4") + " to=" + to.ToString("F4"));
        }
        Capture("timeout-" + actor.GetType().Name + ".png", actor.transform.position);
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
        if (hp) hp.Damaged -= PlayerDamaged; Application.logMessageReceived -= CheckActionError;
        SessionState.SetBool(Prefix + "passed", passed); EditorApplication.ExitPlaymode();
    }
    static void CheckActionError(string text, string trace, LogType kind)
    { if (kind == LogType.Error && text.Contains("Missing native creature action")) missingActions++; }
    static void Watch()
    {
        if (!SessionState.GetBool(Prefix + "active", false) || !long.TryParse(SessionState.GetString(Prefix + "deadline", ""), out long end) || DateTime.UtcNow.Ticks < end) return;
        Log("FAIL overall Play watchdog timeout"); SessionState.SetBool(Prefix + "passed", false);
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); else { SessionState.SetBool(Prefix + "active", false); EditorApplication.Exit(1); }
    }
    static string Hash(string path) { using (var digest = SHA256.Create()) return Convert.ToBase64String(digest.ComputeHash(File.ReadAllBytes(path))); }
    static void Need(bool success, string message) { if (!success) throw new InvalidOperationException(message); Log("PASS " + message); }
    static void Log(string message) { File.AppendAllText(Report, message + "\n"); Debug.Log("ADVENTURE_TRIO_QA " + message); }
    static void Capture(string name, Vector3 focus, Vector3? offset = null, float fieldOfView = 42f)
    {
        var camera = Camera.main; if (!camera) return;
        focus += Vector3.up * (name.Contains("Mushroom") ? .4f : name.Contains("Bishop") || name.Contains("SilverSentinel") ? 1.65f : 1f);
        camera.transform.position = focus + (offset ?? new Vector3(6, 3.3f, 5.5f)); camera.transform.LookAt(focus); camera.fieldOfView = fieldOfView;
        var target = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; float oldAspect = camera.aspect;
        try { camera.targetTexture = target; camera.aspect = 16f / 9f; camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(Output, name), texture.EncodeToPNG()); }
        finally { camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
    }
}
