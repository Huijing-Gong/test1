using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using KingOfGraphic.Hero;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// Disposable-clone Play automation. The native case uses the authored actor's AI;
// other cases deliberately inject Health events to isolate the response contract.
[InitializeOnLoad]
public static class DemonKingHeroHitPlayChecks
{
    const string Prefix = "QA.DemonKingHeroHit.";
    const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new Vector3(25000, 100, 25000);
    static string Output => Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath));
    static string Report => Path.Combine(Output, "hero-hit-validation.txt");
    [Serializable] class Saved { public string key, value; public bool exists; }
    [Serializable] class Snapshot { public Saved[] entries; }
    static BaselineFlow flow;
    static BaselineWorld world;
    static Transform hero;
    static HeroWorldAdapter adapter;
    static PlayerMovement movement;
    static PlayerVitals vitals;
    static Health hp;
    static GameObject actor;
    static StoneScoutRoadsideAI ai;
    static DemonKingCombatVisual visual;
    static Keyboard keyboard;
    static int nativeContacts;
    static float contactAt, contactTime;
    static Vector3 contactPosition, contactImpact;
    static string contactAction;

    static DemonKingHeroHitPlayChecks()
    { EditorApplication.playModeStateChanged += Changed; EditorApplication.update += Watch; }

    public static void RunAutomation()
    {
        if (!Application.dataPath.StartsWith("/private/tmp/demonking-trial-hit-", StringComparison.Ordinal))
            throw new InvalidOperationException("DemonKing hero-hit checks require the disposable scratch clone.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Unsaved scene edits present; checks did not discard them.");
        EditorSceneManager.OpenScene(ScenePath);
        var host = Object.FindFirstObjectByType<BaselineFlow>();
        var saved = new List<Saved>();
        foreach (string prefix in new HashSet<string> { host.saveKeyPrefix, BaselineSaveStore.DefaultKeyPrefix })
            foreach (string suffix in new[] { ".primary", ".backup" })
            {
                string key = prefix + suffix;
                saved.Add(new Saved { key = key, exists = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") });
            }
        SessionState.SetString(Prefix + "saved", JsonUtility.ToJson(new Snapshot { entries = saved.ToArray() }));
        SessionState.SetString(Prefix + "scene", Hash(ScenePath));
        SessionState.SetString(Prefix + "prefix", Prefix + Guid.NewGuid().ToString("N"));
        SessionState.SetString(Prefix + "deadline", DateTime.UtcNow.AddSeconds(180).Ticks.ToString());
        SessionState.SetBool(Prefix + "background", Application.runInBackground);
        SessionState.SetBool(Prefix + "passed", false);
        SessionState.SetBool(Prefix + "active", true);
        host.saveKeyPrefix = SessionState.GetString(Prefix + "prefix", "");
        File.WriteAllText(Report, "DemonKing hero hit: native Unity Play automation in a disposable clone.\n" +
            "Native AI blade contact on a runtime flat floor with PlayerMovement enabled; controlled Health events cover response branches. Not a human feel review.\n");
        Application.runInBackground = true;
        EditorApplication.isPaused = false;
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        FocusGameViewForStartup();
        EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Prefix + "active", false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            Application.runInBackground = true;
            flow = Object.FindFirstObjectByType<BaselineFlow>();
            flow.saveKeyPrefix = SessionState.GetString(Prefix + "prefix", "");
            flow.StartCoroutine(Guard(Tests()));
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        bool passed = SessionState.GetBool(Prefix + "passed", false);
        try
        {
            foreach (var item in JsonUtility.FromJson<Snapshot>(SessionState.GetString(Prefix + "saved", "")).entries)
                if (item.exists != PlayerPrefs.HasKey(item.key) || item.value != PlayerPrefs.GetString(item.key, ""))
                { passed = false; Log("FAIL original save changed: " + item.key); }
            if (Hash(ScenePath) != SessionState.GetString(Prefix + "scene", ""))
            { passed = false; Log("FAIL authored scene bytes changed."); }
            BaselineSaveStore.Clear(SessionState.GetString(Prefix + "prefix", ""));
        }
        catch (Exception error) { passed = false; Log("FAIL cleanup " + error); }
        Application.runInBackground = SessionState.GetBool(Prefix + "background", false);
        SessionState.SetBool(Prefix + "active", false);
        Log(passed ? "FINAL PASS; original save slots and authored scene bytes unchanged; no scene/prefab saved." : "FINAL FAIL");
        EditorApplication.Exit(passed ? 0 : 1);
    }

    static IEnumerator Tests()
    {
        yield return null;
        flow.NewGame();
        if (flow.Tutorial) flow.Tutorial.Skip();
        flow.SetMenu(false);
        GameSession.Instance.GrantSword();
        world = flow.GetComponent<BaselineWorld>(); hero = world.player;
        adapter = hero.GetComponent<HeroWorldAdapter>(); movement = hero.GetComponent<PlayerMovement>();
        vitals = hero.GetComponent<PlayerVitals>(); hp = hero.GetComponent<Health>();
        keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent(); Keys();
        // NewGame/Skip schedule native unlock work for later Update frames. A
        // currently false BlockWorldInput does not imply its movement lock is gone.
        yield return null; yield return null;
        Log("SETUP after native NewGame/Skip: " + GateStatus());
        yield return Wait(() => !flow.BlockWorldInput && Time.timeScale > 0f && !movement.HasExternalInputLock,
            6, "opening and deferred movement unlock release gameplay");
        yield return null;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        yield return WaitForHeroReady();

        foreach (var scout in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None))
        {
            if (scout.name == "StoneScout_Forest_01") actor = scout.gameObject;
            scout.enabled = false;
        }
        foreach (var enemy in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None)) enemy.enabled = false;
        foreach (var enemy in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None)) enemy.enabled = false;
        foreach (var enemy in Object.FindObjectsByType<GelBlobAI>(FindObjectsSortMode.None)) enemy.enabled = false;
        foreach (var enemy in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None)) enemy.enabled = false;
        foreach (var navigation in Object.FindObjectsByType<EnemyNavigationWorld>(FindObjectsSortMode.None)) navigation.enabled = false;
        Need(actor, "authored StoneScout_Forest_01 exists");
        ai = actor.GetComponent<StoneScoutRoadsideAI>(); visual = actor.GetComponentInChildren<DemonKingCombatVisual>();
        Need(visual, "authored trial actor owns the actual DemonKing skeleton and blade");
        var follow = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (follow) follow.enabled = false;
        if (Camera.main) Camera.main.transform.rotation = Quaternion.identity;
        var floor = Box("QA hero hit runtime floor", Origin - Vector3.up * .15f, new Vector3(60, .3f, 60));
        var layout = new SerializedObject(flow.GetComponent<OverworldV2Layout>());
        var surfaces = layout.FindProperty("navigationSurfaces"); surfaces.arraySize++;
        surfaces.GetArrayElementAtIndex(surfaces.arraySize - 1).objectReferenceValue = floor;
        layout.ApplyModifiedPropertiesWithoutUndo();
        Set(ai, "home", Origin + Vector3.up * .025f); Set(ai, "homeFacing", Quaternion.identity);
        actor.GetComponent<StoneScoutCombatTarget>().ResetForJourney();
        Place(Origin + Vector3.forward * 4f + Vector3.up * .025f, Quaternion.Euler(0, 180, 0));
        movement.enabled = true; Physics.SyncTransforms();
        hp.Damaged += NativeHit; ai.enabled = true;
        int before = hp.CurrentHealth;
        yield return Wait(() => nativeContacts > 0, 16, "authored AI autonomously approaches and lands its native blade contact");
        Need(nativeContacts == 1 && before - hp.CurrentHealth == 8 && movement.enabled,
            "native strike removes exactly 8 HP with PlayerMovement enabled");
        Need(contactAt >= visual.contactStart - .035f && contactAt <= visual.contactEnd + .035f,
            "native damage occurs during the authored blade window: " + contactAt.ToString("F3"));
        Need(contactAction == "HIT_HEAVY" && contactImpact.sqrMagnitude > 1f && Mathf.Abs(contactImpact.y) < .0001f,
            "frontal native strike selects heavy recoil with a strictly horizontal controller impulse");
        yield return null; // The incoming event occurs after the adapter's LateUpdate sample.
        Need(adapter.pose.CurrentAction == "HIT_HEAVY", "native incoming event reaches the rendered heavy pose");
        Capture("hero-hit-native-contact.png");
        yield return Wait(() => !adapter.OwnsAction, 1, "native hit releases its owned input lock");
        float stun = Time.time - contactTime;
        Need(stun >= .20f && stun <= .40f, "native hard lock is bounded independently of the full clip: " + stun.ToString("F3") + "s");
        float travel = Vector3.ProjectOnPlane(hero.position - contactPosition, Vector3.up).magnitude;
        Need(travel >= .15f && travel <= .50f && Mathf.Abs(hero.position.y - contactPosition.y) < .04f && !movement.ImpactActive,
            "native impulse decays to a small horizontal displacement: " + travel.ToString("F3") + "m");
        yield return Wait(() => !HitPresentation(), 1, "native cosmetic recovery finishes without input");
        Need(Time.time - contactTime <= .70f && nativeContacts == 1 && hp.CurrentHealth == before - 8,
            "one native swing has one damage event; no-input recovery ends within .70s");
        Capture("hero-hit-native-recovered.png");
        ai.enabled = false; hp.Damaged -= NativeHit;

        string[] expected = { "HIT_HEAVY", "HIT_LIGHT_B", "HIT_LIGHT_L", "HIT_LIGHT_R" };
        Vector3[] incoming = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        for (int i = 0; i < incoming.Length; i++)
        {
            Place(Origin + Vector3.right * 10f + Vector3.up * .025f, Quaternion.identity);
            Source(incoming[i]);
            Need(Damage(8, incoming[i]), "controlled directional Health damage accepted");
            Need((string)Field(adapter, "transient") == expected[i], "incoming direction " + incoming[i] + " selects " + expected[i]);
            Vector3 impulse = (Vector3)Field(movement, "impactVelocity");
            Need(Vector3.Dot(impulse.normalized, -incoming[i]) > .99f && Mathf.Abs(impulse.y) < .0001f,
                "controlled direction uses the matching horizontal impulse");
            yield return new WaitForSeconds(.66f);
            Need(!HitPresentation() && !adapter.OwnsAction && !movement.ImpactActive, "controlled direction leaves no recovery lock/impulse");
        }
        Log("PASS controlled four-direction response branches (Health events; not four autonomous swings).");

        Vector3 start;
        foreach (var key in new[] { Key.W, Key.UpArrow })
        {
            Place(Origin + Vector3.right * 10f + Vector3.up * .025f, Quaternion.identity); Source(Vector3.forward);
            Damage(8, Vector3.forward);
            yield return Wait(() => !adapter.OwnsAction, 1, key + " movement cancellation waits for the short hard lock");
            Need(HitPresentation(), "full clip cosmetic recovery remains after hard lock ends");
            start = hero.position; Keys(key);
            yield return new WaitForSeconds(.12f); Keys();
            Need(!HitPresentation() && Vector3.ProjectOnPlane(hero.position - start, Vector3.up).magnitude > .05f,
                "synthetic " + key + " resumes actual PlayerMovement and cancels remaining cosmetic recovery");
            yield return null;
        }

        Place(Origin + Vector3.right * 10f + Vector3.up * .025f, Quaternion.identity); Source(Vector3.forward);
        Damage(8, Vector3.forward); yield return new WaitForSeconds(.12f);
        var bones = new List<Transform>(); var rotations = new List<Quaternion>();
        foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
        {
            var transform = adapter.pose.animator.GetBoneTransform(bone);
            if (transform) { bones.Add(transform); rotations.Add(transform.localRotation); }
        }
        Need(bones.Count >= 4 && adapter.pose.CurrentTime > .04f, "same-action restart has a sampled, advancing humanoid pose");
        Need(Damage(8, Vector3.forward) && (float)Field(adapter, "transientTime") < .001f,
            "second same-ID Health event resets the gameplay presentation clock");
        yield return null;
        float maxAngle = 0f;
        for (int i = 0; i < bones.Count; i++) maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(rotations[i], bones[i].localRotation));
        Need(adapter.pose.CurrentAction == "HIT_HEAVY" && adapter.pose.CurrentTime < .15f && maxAngle < 65f,
            "same-ID replay restarts with bounded sampled local rotation change: " + maxAngle.ToString("F2") + "deg; not a human visual assessment");
        yield return new WaitForSeconds(.66f);

        Place(Origin + Vector3.right * 10f + Vector3.up * .025f, Quaternion.identity); Source(Vector3.forward);
        var controller = hero.GetComponent<CharacterController>();
        float radius = controller.radius * Mathf.Max(Mathf.Abs(hero.lossyScale.x), Mathf.Abs(hero.lossyScale.z));
        var wall = Box("QA controller recoil wall", hero.position + Vector3.back * (radius + .16f) + Vector3.up,
            new Vector3(3f, 3f, .20f));
        start = hero.position; Damage(8, Vector3.forward); yield return new WaitForSeconds(.35f);
        Need(hero.position.z >= wall.bounds.max.z + radius - controller.skinWidth - .03f &&
            Vector3.ProjectOnPlane(hero.position - start, Vector3.up).magnitude < .20f && Mathf.Abs(hero.position.y - start.y) < .04f,
            "CharacterController constrains recoil against a real wall without a vertical impulse or tunnelling");
        Object.Destroy(wall.gameObject); yield return null;

        Place(Origin + Vector3.right * 10f + Vector3.up * .025f, Quaternion.identity); Source(Vector3.forward);
        hero.GetComponent<PlayerEquipment>().DrawForAttack(); Keys(Key.Q);
        yield return Wait(() => adapter.Guarding, 1, "synthetic Q enters the actual guard handler");
        yield return new WaitForSeconds(.19f);
        before = hp.CurrentHealth; start = hero.position;
        Need(Damage(8, Vector3.forward) && hp.CurrentHealth == before - 3 && !movement.ImpactActive && !HitPresentation(),
            "ordinary frontal guard keeps its 35-percent chip damage and bypasses DemonKing body recoil");
        yield return new WaitForSeconds(.10f);
        Need(Vector3.ProjectOnPlane(hero.position - start, Vector3.up).magnitude < .015f, "blocked hit does not translate the controller");
        Keys(); yield return null;

        Place(Origin + Vector3.right * 10f + Vector3.up * .025f, Quaternion.identity); Source(Vector3.forward);
        Keys(Key.LeftCtrl); yield return Wait(() => adapter.Dodging, 1, "synthetic Ctrl enters the actual dodge handler"); Keys();
        yield return Wait(() => (float)Field(adapter, "dodgeTime") >= .08f, .3f, "actual dodge reaches its invulnerable window");
        before = hp.CurrentHealth;
        Need(!Damage(8, Vector3.forward) && hp.CurrentHealth == before && adapter.Dodging && !movement.ImpactActive && !HitPresentation(),
            "invulnerable dodge rejects the Health event without adding body recoil");
        yield return Wait(() => !adapter.Dodging, 1, "dodge completes");

        for (int menu = 0; menu < 3; menu++)
        {
            Place(Origin + Vector3.right * 10f + Vector3.up * .025f, Quaternion.identity); Source(Vector3.forward);
            Damage(8, Vector3.forward); yield return null;
            Need(movement.ImpactActive, "menu scenario starts during a live controller recoil");
            string name = menu == 0 ? "J journal" : menu == 1 ? "M map" : "Esc pause";
            if (menu == 0) { world.SetJournal(true); Need(world.JournalOpen, "real journal opens"); }
            else if (menu == 1) Need(flow.ShowMap() && flow.MapOpen, "real map opens");
            else { flow.SetMenu(true); Need(flow.MenuOpen, "real pause menu opens"); }
            start = hero.position; before = hp.CurrentHealth;
            yield return new WaitForSecondsRealtime(.20f);
            Need(!vitals.EnemyCombatActive && Vector3.Distance(hero.position, start) < .005f && hp.CurrentHealth == before,
                name + " pauses actual movement and damage");
            Need(!movement.ImpactActive && !HitPresentation(), name + " transition clears the interrupted hit impulse/cosmetic");
            if (menu == 0) world.SetJournal(false); else flow.SetMenu(false);
            yield return Wait(() => !flow.BlockWorldInput && Time.timeScale > 0 && adapter.AcceptInput, 5, name + " closes through the native handler");
            start = hero.position; yield return new WaitForSeconds(.20f);
            Need(!movement.IsGameplayInputLocked && !movement.ImpactActive &&
                Vector3.ProjectOnPlane(hero.position - start, Vector3.up).magnitude < .015f,
                name + " resume leaves no delayed drift or stale hit lock");
        }

        // A different enemy can interrupt during the brief DemonKing impulse.
        // Opening a real map afterwards must not revive that earlier translation.
        Place(Origin + Vector3.right * 10f + Vector3.up * .025f, Quaternion.identity); Source(Vector3.forward);
        Damage(8, Vector3.forward); yield return null;
        Need(movement.ImpactActive, "mixed-source fixture starts during an actual DemonKing impulse");
        var other = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab"),
            hero.position + Vector3.right * 4f, Quaternion.identity);
        other.GetComponent<StoneScoutRoadsideAI>().enabled = false;
        Need(!other.GetComponentInChildren<DemonKingCombatVisual>(true), "mixed-source interruption uses the original nontrial Scout");
        before = hp.CurrentHealth;
        Need(hp.TryApplyDamage(new DamageInfo(8, other, hero.position + Vector3.up, Vector3.left)) && hp.CurrentHealth == before - 8,
            "controlled original Scout Health event interrupts the DemonKing response");
        Need(!(bool)Field(adapter, "compactHit") && !movement.ImpactActive,
            "replacing a DemonKing response immediately clears its previously owned impulse");
        Need(flow.ShowMap() && flow.MapOpen, "real map opens after the mixed-source interruption");
        start = hero.position; before = hp.CurrentHealth;
        yield return new WaitForSecondsRealtime(.20f);
        Need(Vector3.Distance(hero.position, start) < .005f && hp.CurrentHealth == before && !movement.ImpactActive,
            "mixed-source map pause retains no old DemonKing translation");
        flow.SetMenu(false);
        yield return Wait(() => !flow.BlockWorldInput && Time.timeScale > 0 && adapter.AcceptInput, 5,
            "mixed-source map closes through the native handler");
        start = hero.position; yield return new WaitForSeconds(.20f);
        Need(!movement.IsGameplayInputLocked && !movement.ImpactActive &&
            Vector3.ProjectOnPlane(hero.position - start, Vector3.up).magnitude < .015f,
            "mixed-source map resume has no delayed DemonKing slide or stale lock");
        Object.Destroy(other); yield return null;

        Place(Origin + Vector3.right * 10f + Vector3.up * .025f, Quaternion.identity); Source(Vector3.forward);
        start = hero.position;
        Need(Damage(9999, Vector3.forward) && hp.IsDead && !movement.ImpactActive && !HitPresentation(),
            "lethal hit enters the existing death branch without nonlethal recoil");
        yield return new WaitForSeconds(.12f);
        Need(Vector3.ProjectOnPlane(hero.position - start, Vector3.up).magnitude < .015f, "lethal branch adds no horizontal hit translation");
        Place(Origin + Vector3.up * .025f, Quaternion.identity);
        Object.Destroy(floor.gameObject);
        Log("COMPLETE native AI strike + controlled direction, W/UpArrow short-lock movement cancellation, repeated action, wall, guard, dodge, lethal, mixed-source map resume and J/M/Esc cleanup checks. Manual feel, all terrain and arbitrary framerates remain outside this automation.");
    }

    static void NativeHit(Health target, DamageInfo damage)
    {
        if (damage.Source != actor) return;
        nativeContacts++; contactAt = visual.AttackElapsed; contactTime = Time.time; contactPosition = hero.position;
        contactImpact = (Vector3)Field(movement, "impactVelocity"); contactAction = (string)Field(adapter, "transient");
        Log("NATIVE CONTACT " + nativeContacts + ": time=" + contactAt.ToString("F3") + " amount=" + damage.Amount + " action=" + contactAction);
    }
    static bool Damage(int amount, Vector3 incoming) => hp.TryApplyDamage(new DamageInfo(amount, actor, hero.position + Vector3.up, -incoming));
    static bool HitPresentation()
    { string id = (string)Field(adapter, "transient"); return id != null && id.StartsWith("HIT_", StringComparison.Ordinal); }
    static void Place(Vector3 position, Quaternion rotation)
    {
        Keys(); hp.RestoreFullHealth(); adapter.ResetForRespawn();
        movement.Relocate(position, rotation); vitals.Refill(); movement.enabled = true;
        Physics.SyncTransforms();
    }
    static void Source(Vector3 incoming)
    {
        var body = actor.GetComponent<Rigidbody>(); body.position = hero.position + incoming * 2.7f;
        body.rotation = Quaternion.LookRotation(-incoming); Physics.SyncTransforms();
    }
    static Collider Box(string name, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.position = position; go.transform.localScale = scale; Physics.SyncTransforms();
        return go.GetComponent<Collider>();
    }
    static void Keys(params Key[] held)
    { if (keyboard != null) InputSystem.QueueStateEvent(keyboard, new KeyboardState(held)); }
    static object Field(object owner, string field)
    { return owner.GetType().GetField(field, Private).GetValue(owner); }
    static void Set(object owner, string field, object value)
    { owner.GetType().GetField(field, Private).SetValue(owner, value); }
    static IEnumerator Wait(Func<bool> condition, float seconds, string name)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Need(condition(), name);
    }
    static bool HeroReady() => adapter && adapter.isActiveAndEnabled && adapter.pose && adapter.pose.Ready &&
        adapter.AcceptInput && movement && movement.enabled && vitals && vitals.EnemyCombatActive;
    static IEnumerator WaitForHeroReady()
    {
        float deadline = Time.realtimeSinceStartup + 8f, nextLog = 0f; int readyFrames = 0;
        while (Time.realtimeSinceStartup < deadline)
        {
            FocusGameViewForStartup();
            readyFrames = HeroReady() ? readyFrames + 1 : 0;
            if (readyFrames >= 2) break;
            if (Time.realtimeSinceStartup >= nextLog)
            { Log("WAIT hero readiness: " + GateStatus()); nextLog = Time.realtimeSinceStartup + 1f; }
            yield return null;
        }
        Need(readyFrames >= 2 && HeroReady(), "native hero pose, incoming-hit gates and main-world combat are active; " + GateStatus());
    }
    // Cursor locking requires a real focused Game view. This startup-only helper
    // never runs during menu tests; their input gates are restored by native Flow.
    static void FocusGameViewForStartup()
    {
        var type = Type.GetType("UnityEditor.GameView,UnityEditor");
        if (type != null) EditorWindow.GetWindow(type)?.Focus();
        if (Application.isPlaying) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }
    static string GateStatus()
    {
        var pose = adapter ? adapter.pose : null; var animator = pose ? pose.animator : null;
        return "adapter=" + (bool)adapter + ", enabled=" + (adapter && adapter.isActiveAndEnabled) +
            ", pose=" + (bool)pose + ", ready=" + (pose && pose.Ready) + ", library=" + (pose && pose.library) +
            ", animator=" + (bool)animator + ", avatarValid=" + (animator && animator.avatar && animator.avatar.isValid) +
            ", acceptInput=" + (adapter && adapter.AcceptInput) + ", adapterDead=" + (adapter ? Field(adapter, "dead") : null) +
            ", movementEnabled=" + (movement && movement.enabled) + ", externalLock=" + (movement && movement.HasExternalInputLock) +
            ", gameplayLock=" + (movement && movement.IsGameplayInputLocked) +
            ", vitals=" + (bool)vitals + ", vitalsEnabled=" + (vitals && vitals.isActiveAndEnabled) +
            ", enemyCombat=" + (vitals && vitals.EnemyCombatActive) +
            ", vitalsFlowMatches=" + (vitals && (BaselineFlow)Field(vitals, "flow") == flow) +
            ", started=" + flow.Started + ", blocked=" + flow.BlockWorldInput + ", menu=" + flow.MenuOpen +
            ", map=" + flow.MapOpen + ", title=" + flow.TitleScreenVisible + ", death=" + flow.DeathOpen +
            ", tutorialModal=" + (flow.Tutorial && flow.Tutorial.ModalOpen) +
            ", scale=" + Time.timeScale.ToString("F3") + ", cursor=" + Cursor.lockState +
            ", focused=" + Application.isFocused + ", batchMode=" + Application.isBatchMode +
            ", focusedWindow=" + (EditorWindow.focusedWindow ? EditorWindow.focusedWindow.GetType().Name : "none") +
            ", hp=" + (hp ? hp.CurrentHealth : -1);
    }
    static IEnumerator Guard(IEnumerator tests)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(tests); bool passed = true;
        while (stack.Count > 0)
        {
            object item = null; bool more = false;
            try { more = stack.Peek().MoveNext(); if (more) item = stack.Peek().Current; }
            catch (Exception error) { passed = false; Log("FAIL " + error); break; }
            if (!more) { stack.Pop(); continue; }
            if (item is IEnumerator nested) { stack.Push(nested); continue; }
            yield return item;
        }
        if (hp) hp.Damaged -= NativeHit;
        if (keyboard != null) { Keys(); InputSystem.RemoveDevice(keyboard); keyboard = null; }
        SessionState.SetBool(Prefix + "passed", passed);
        EditorApplication.ExitPlaymode();
    }
    static void Watch()
    {
        if (!SessionState.GetBool(Prefix + "active", false) ||
            !long.TryParse(SessionState.GetString(Prefix + "deadline", ""), out long deadline) || DateTime.UtcNow.Ticks < deadline) return;
        Log("FAIL overall Play watchdog timeout"); SessionState.SetBool(Prefix + "passed", false);
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else { SessionState.SetBool(Prefix + "active", false); EditorApplication.Exit(1); }
    }
    static string Hash(string path)
    { using (var digest = SHA256.Create()) return Convert.ToBase64String(digest.ComputeHash(File.ReadAllBytes(path))); }
    static void Need(bool pass, string message)
    { if (!pass) throw new InvalidOperationException(message); Log("PASS " + message); }
    static void Log(string message)
    { File.AppendAllText(Report, message + "\n"); Debug.Log("DEMON_HERO_HIT_QA " + message); }
    static void Capture(string name)
    {
        var camera = Camera.main; if (!camera) return;
        Vector3 focus = hero.position + Vector3.up * 1.1f;
        camera.transform.position = focus + new Vector3(6.5f, 2.5f, 5.5f); camera.transform.LookAt(focus); camera.fieldOfView = 42;
        var target = new RenderTexture(1280, 720, 24);
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previous = RenderTexture.active; var previousTarget = camera.targetTexture; float previousAspect = camera.aspect;
        try
        {
            camera.targetTexture = target; camera.aspect = 16f / 9f; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply(); File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = previousTarget; camera.aspect = previousAspect;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }
}
