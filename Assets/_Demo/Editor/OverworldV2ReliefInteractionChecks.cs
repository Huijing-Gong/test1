using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Import this prepared source into Assets/_Demo/Editor only after current Play has ended.
// E/J/Enter/Escape use the original input consumers; no World.Interact or dialogue completion API.
// Existing IMGUI tracking buttons have no keyboard shortcut: their model is checked via explicitly
// labelled Track API calls. Mouse/button operation remains a manual test, never an input-pass claim.
[InitializeOnLoad]
public static class OverworldV2ReliefInteractionChecks
{
    const string PrepareState = "QA.OverworldV2Play.", State = "QA.OverworldV2ReliefInteraction.";
    const string Report = "ToolReports/MapV2Relief/interaction-checks.txt", Images = "Docs/MapV2/Relief/InteractionGameView/";
    static BaselineFlow flow;
    static BaselineWorld world;
    static OverworldV2Layout layout;
    static PlayerMovement player;
    static PlayerInteraction interaction;
    static DialogueUI dialogue;
    static BaselineLoadout loadout;
    static ThirdPersonCamera orbit;
    static MonoBehaviour host;
    static Coroutine coroutine;
    static Keyboard keyboard, oldKeyboard;
    static Mouse mouse, oldMouse;
    static KeyboardState desiredInput;
    static bool oldKeyboardEnabled, oldMouseEnabled, oldBackground, oldCursorVisible, finished;
    static InputSettings.BackgroundBehavior oldBehavior;
    static CursorLockMode oldCursorLock;
    static float oldTimeScale;
    static double deadline;
    static int failures, swordGrantEvents;
    static string phase, qaPrefix, realPrefix, primaryHash, backupHash;
    static readonly BaselineInteraction[] clues = new BaselineInteraction[3], goals = new BaselineInteraction[3], chests = new BaselineInteraction[3], runes = new BaselineInteraction[3];
    public static bool Running { get; private set; }
    public static string Status => Running ? phase + " " + Pose() : SessionState.GetString(State + "status", "Not run");

    static OverworldV2ReliefInteractionChecks()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode && Running) Stop(false); };
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (Running) Fail("Assembly reload interrupted interaction checks."); };
    }
    public static void Begin()
    {
        qaPrefix = SessionState.GetString(PrepareState + "prefix", ""); realPrefix = SessionState.GetString(PrepareState + "realPrefix", "");
        if (!EditorApplication.isPlaying || Running || OverworldV2PlayChecks.Running || OverworldV2TutorialChecks.Running ||
            OverworldV2TraversalChecks.Running || OverworldV2CameraChecks.Running || OverworldV2WaystationChecks.Running || OverworldV2ReliefChecks.Running || OverworldV2ReliefTowerChecks.Running ||
            !SessionState.GetBool(PrepareState + "prepared", false) || SceneManager.sceneCount != 1 ||
            SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene ||
            !qaPrefix.StartsWith("QA.OverworldV2.", StringComparison.Ordinal) || string.IsNullOrEmpty(realPrefix) || qaPrefix == realPrefix)
            throw new InvalidOperationException("Requires exclusive Play in the isolated scene made by OverworldV2PlayChecks.Prepare.");
        flow = Object.FindFirstObjectByType<BaselineFlow>(); world = flow ? flow.GetComponent<BaselineWorld>() : null;
        layout = flow ? flow.GetComponent<OverworldV2Layout>() : null; player = world && world.player ? world.player.GetComponent<PlayerMovement>() : null;
        interaction = player ? player.GetComponent<PlayerInteraction>() : null; loadout = player ? player.GetComponent<BaselineLoadout>() : null;
        dialogue = Object.FindFirstObjectByType<DialogueUI>(); orbit = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null; host = SideWorldTravel.Instance;
        if (!flow || flow.saveKeyPrefix != qaPrefix || !world || !layout || !flow.Tutorial || !flow.rewards || !player || !interaction || !dialogue || !orbit || !loadout || !host || !host.isActiveAndEnabled)
            throw new InvalidOperationException("Wait for scene Start; complete existing V2 dialogue/player/reward components required.");
        Array.Clear(clues, 0, 3); Array.Clear(goals, 0, 3); Array.Clear(chests, 0, 3); Array.Clear(runes, 0, 3);
        foreach (var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None))
        {
            if (item.gameObject.scene != world.gameObject.scene || item.index < 0 || item.index > 2) continue;
            var group = item.action == BaselineInteraction.ActionKind.AcceptTask ? clues : item.action == BaselineInteraction.ActionKind.Objective ? goals : item.action == BaselineInteraction.ActionKind.Chest ? chests : runes;
            if (group[item.index]) throw new InvalidOperationException("Duplicate existing interaction identity " + item.action + " " + item.index);
            group[item.index] = item;
        }
        for (int i = 0; i < 3; i++) if (!clues[i] || !goals[i] || !chests[i] || !runes[i]) throw new InvalidOperationException("Missing existing interaction index " + i);
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); Directory.CreateDirectory(Images);
        File.WriteAllText(Report, "METHOD: isolated Prepare scene/save. Controlled placements use at most eight dry radial approaches; normal physics triggers and facing must select the actual NPC. " +
            "E starts/advances dialogue through PlayerInteraction, Enter dismisses chest rewards, J opens/closes journal, Escape closes dialogue. " +
            "A fresh candidate-array island-prompt probe runs first without a candidate reset. Later controlled placements explicitly reset the existing PlayerInteraction component to isolate retained test candidates; this does not fix or prove a runtime teleport bug. " +
            "No World.Interact, RegisterCandidate, NotifyDialogueCompleted, BeginDialogue or AdvanceDialogue API calls. " +
            "Tracking buttons have no keyboard binding: explicitly labelled Track API probes do not prove GUI-button operation. Save/Continue are explicit API checks. Not continuous human play.\n");
        primaryHash = SlotHash(realPrefix + ".primary"); backupHash = SlotHash(realPrefix + ".backup");
        oldKeyboard = Keyboard.current; oldMouse = Mouse.current; oldKeyboardEnabled = oldKeyboard != null && oldKeyboard.enabled; oldMouseEnabled = oldMouse != null && oldMouse.enabled;
        oldBehavior = InputSystem.settings.backgroundBehavior; oldBackground = Application.runInBackground; oldTimeScale = Time.timeScale; oldCursorLock = Cursor.lockState; oldCursorVisible = Cursor.visible;
        desiredInput = new KeyboardState(); failures = 0; swordGrantEvents = 0; finished = false; phase = "initializing";
        Running = true; deadline = EditorApplication.timeSinceStartup + 240; SessionState.SetBool(State + "completed", false);
        try
        {
            keyboard = InputSystem.AddDevice<Keyboard>("V2ReliefInteractionQAKeyboard"); mouse = InputSystem.AddDevice<Mouse>("V2ReliefInteractionQAMouse");
            if (oldKeyboardEnabled) InputSystem.DisableDevice(oldKeyboard); if (oldMouseEnabled) InputSystem.DisableDevice(oldMouse);
            keyboard.MakeCurrent(); mouse.MakeCurrent(); InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; Application.runInBackground = true;
            EditorApplication.ExecuteMenuItem("Window/General/Game"); EditorApplication.update += Tick; coroutine = host.StartCoroutine(Guarded());
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }
    static IEnumerator Guarded()
    {
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (Running && stack.Count > 0)
        {
            object current = null; bool advanced;
            try { advanced = stack.Peek().MoveNext(); if (advanced) current = stack.Peek().Current; }
            catch (Exception exception) { Fail(exception.ToString()); yield break; }
            if (!advanced) { stack.Pop(); continue; }
            if (current is IEnumerator nested) { stack.Push(nested); continue; } yield return current;
        }
        if (Running) Stop();
    }
    static IEnumerator Run()
    {
        phase = "new journey API fixture"; flow.NewGame(); yield return new WaitForSecondsRealtime(.4f); flow.Tutorial.Cancel(); yield return new WaitForSecondsRealtime(.35f);
        Check(!GameSession.Instance.HasSword && All(world.accepted, false) && All(world.opened, false) && !layout.BridgeOpen && !player.IsGameplayInputLocked,
            "Fresh isolated journey, no sword/tasks/chests, bridge closed, tutorial cancelled without completing it");
        phase = "fresh island prompt probe";
        var candidatesField = typeof(PlayerInteraction).GetField("nearbyNPCs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var initialCandidates = candidatesField != null ? candidatesField.GetValue(interaction) as NPCDialogue[] : null;
        Check(initialCandidates != null && Array.TrueForAll(initialCandidates, npc => !npc), "Fresh QA candidate array is empty before first controlled island placement (read-only observation)");
        var islandNPC = goals[1].GetComponent<NPCDialogue>();
        yield return PlaceFor(islandNPC, false);
        Check(interaction.SelectedNPC == islandNPC && dialogue.IsInteractionPromptVisible && !dialogue.IsDialogueActive &&
            All(world.accepted, false) && All(world.visited, false) && All(world.opened, false),
            "Fresh first-placement island probe selected its real prompt without candidate cleanup, E, dialogue or progress changes");
        Record("FIXTURE_RETURN NewGame/Cancel APIs return to the original fresh journey before the main interaction sequence; no island task was completed by the probe.");
        flow.NewGame(); yield return new WaitForSecondsRealtime(.4f); flow.Tutorial.Cancel(); yield return new WaitForSecondsRealtime(.35f);
        GameSession.SwordStateChanged += SwordState;

        yield return Talk(clues[0].GetComponent<NPCDialogue>(), "guide first conversation", "guide-dialogue-game-view.png");
        Check(GameSession.Instance.HasSword && GameSession.Instance.IntroDialogueCompleted && swordGrantEvents == 1 && world.accepted[0],
            "Real E guide completion grants first sword once and accepts western task");
        Check(!world.accepted[1] && !world.accepted[2] && world.Tracked == -1 && All(world.opened, false), "Guide did not auto-track or open any chest");
        yield return Press(Key.J); Check(world.JournalOpen && player.IsGameplayInputLocked, "Real J opens journal and locks movement");
        Record("API_ONLY tracking: no key binding exists for current IMGUI Track/Stop buttons; mouse-button acceptance remains MANUAL_PENDING.");
        world.Track(0); Check(world.Tracked == 0, "API_ONLY Track(0) selects accepted incomplete task");
        yield return Capture("journal-tracking-game-view.png");
        world.Track(-1); Check(world.Tracked == -1, "API_ONLY Track(-1) stops tracking");
        world.Track(0); yield return Press(Key.J);
        Check(!world.JournalOpen && !player.IsGameplayInputLocked && world.Tracked == 0, "Real J closes journal; API-selected tracking retained");
        yield return Talk(clues[0].GetComponent<NPCDialogue>(), "guide repeated conversation", null);
        Check(GameSession.Instance.HasSword && swordGrantEvents == 1 && world.accepted[0], "Repeated real E guide conversation does not grant another sword event");
        GameSession.SwordStateChanged -= SwordState;

        yield return Talk(clues[1].GetComponent<NPCDialogue>(), "read island task clue", null);
        Check(world.accepted[1] && world.Tracked == 0, "Real E island clue accepts its own optional task without changing tracking");
        yield return Talk(clues[2].GetComponent<NPCDialogue>(), "read eastern task clue", null);
        Check(All(world.accepted, true) && All(world.completed, false) && All(world.opened, false), "Both readable clues plus guide accepted three tasks; objectives/chests remain separate");
        yield return SaveContinue("accepted tasks and explicit tracking");
        Check(world.Tracked == 0 && All(world.accepted, true), "Save/Continue APIs retain actual-E task acceptance and API-selected tracking");

        yield return OpenChest(0, "island hidden chest"); yield return OpenChest(1, "western hidden chest");
        Check(world.opened[0] && world.opened[1] && !world.opened[2] && All(world.completed, false), "Hidden chests open independently before completing tasks");
        yield return Talk(chests[2].GetComponent<NPCDialogue>(), "sealed chest before puzzle", "sealed-chest-game-view.png");
        Check(!world.opened[2] && !flow.rewards.IsOpen && !world.PuzzleSolved, "Real E sealed chest remains locked and grants no reward");
        var inscriptionObject = GameObject.Find("Weathered inscription"); var inscription = inscriptionObject ? inscriptionObject.GetComponent<NPCDialogue>() : null;
        Check(inscription, "Original puzzle inscription remains present");
        yield return Talk(inscription, "read puzzle inscription", null);
        Check(world.RuneProgress == 0 && !world.PuzzleSolved, "Reading observation clue does not itself solve the seal");
        Check(runes[2].name.Contains("SUN") && runes[0].name.Contains("WAVE") && runes[1].name.Contains("LEAF"), "Original SUN=2 WAVE=0 LEAF=1 identities retained");
        yield return Talk(runes[0].GetComponent<NPCDialogue>(), "incorrect first WAVE", null);
        Check(world.RuneProgress == 0 && !world.PuzzleSolved, "Real E wrong first symbol does not solve the seal");
        int[] order = { 2, 0, 1 };
        for (int i = 0; i < order.Length; i++)
        {
            yield return Talk(runes[order[i]].GetComponent<NPCDialogue>(), "correct rune " + runes[order[i]].name, null);
            Check(world.RuneProgress == i + 1 && world.PuzzleSolved == (i == 2), "Real E original SUN-WAVE-LEAF progression step " + (i + 1));
        }
        yield return OpenChest(2, "unsealed eastern chest");
        Check(All(world.opened, true) && All(world.completed, false), "All three chests collected before task objectives; progress remains independent");
        for (int i = 0; i < 3; i++)
        {
            yield return Talk(goals[i].GetComponent<NPCDialogue>(), "task objective " + i, i == 2 ? "eastern-objective-game-view.png" : null);
            Check(world.visited[i] && world.completed[i] && All(world.opened, true), "Real E objective " + i + " completes its task without altering collected chests");
        }
        Check(All(world.completed, true) && world.Tracked == -1 && layout.BridgeOpen && !flow.Tutorial.Active, "Three optional tasks complete; finished tracked task clears; prior Continue keeps skipped tutorial and bridge open");
        yield return SaveContinue("completed objectives and collected equipment");
        Check(All(world.accepted, true) && All(world.visited, true) && All(world.completed, true) && All(world.opened, true) && world.PuzzleSolved && world.RuneProgress == 3 && GameSession.Instance.HasSword,
            "Save/Continue APIs retain actual-E tasks, three chests, symbol solution and sword");
        yield return Press(Key.J); Check(world.JournalOpen, "Real J still opens completed journal after Continue");
        yield return Capture("completed-journal-game-view.png"); yield return Press(Key.J);
        Check(!world.JournalOpen && !player.IsGameplayInputLocked && !dialogue.IsDialogueActive && !flow.rewards.IsOpen, "Input and modal state released at suite end");
        CheckRealSave(); finished = true;
        Record("COMPLETE bounded real E/J/Enter interactions. Tracking GUI buttons, continuous traversal to objects and human targeting feel remain MANUAL_PENDING.");
    }
    static IEnumerator Talk(NPCDialogue npc, string label, string screenshot)
    {
        phase = label; yield return PlaceFor(npc);
        Check(interaction.SelectedNPC == npc && dialogue.IsInteractionPromptVisible, label + " actual Trigger/facing selected expected NPC and prompt");
        yield return Press(Key.E);
        Check(dialogue.IsDialogueActive && dialogue.ActiveNPC == npc, label + " real E opened expected dialogue");
        if (screenshot != null) yield return Capture(screenshot);
        int presses = 0;
        while (dialogue.IsDialogueActive && presses++ < 12)
        {
            Check(dialogue.ActiveNPC == npc, label + " dialogue owner unchanged before advancing");
            yield return Press(Key.E);
        }
        Check(!dialogue.IsDialogueActive && presses <= 12, label + " real E completed the original dialogue lines");
        yield return new WaitForSecondsRealtime(.2f);
    }
    static IEnumerator PlaceFor(NPCDialogue npc, bool resetCandidates = true)
    {
        if (!npc || !npc.IsInteractable) throw new InvalidOperationException("Expected live interactable NPC for " + phase);
        SetKeys(); float radius = Mathf.Min(2f, npc.InteractionDistance * .8f);
        if (radius < .5f) throw new InvalidOperationException("Interaction range too small for bounded existing-map fixture.");
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4; Vector3 wanted = npc.transform.position + new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
            if (!world.NavigationGround(wanted, out var ground)) { Record("APPROACH_REJECT no dry standing space " + i + " for " + npc.name); continue; }
            float yaw = Heading(ground, npc.transform.position);
            if (resetCandidates)
            {
                if (!interaction.enabled || dialogue.IsDialogueActive || flow.rewards.IsOpen)
                    throw new InvalidOperationException("Controlled placement requires the original enabled interaction component and no active dialogue/reward.");
                interaction.enabled = false; interaction.enabled = true;
                Record("FIXTURE_RESET original PlayerInteraction disable/enable clears accumulated controlled-placement candidates; subsequent registration still requires real physics triggers. No runtime teleport fix/proof claim.");
            }
            player.Relocate(ground - Vector3.up * .1f, Quaternion.Euler(0, yaw, 0)); orbit.ResetOrbit(yaw, 14); Physics.SyncTransforms();
            Record("CONTROLLED_PLACEMENT " + npc.name + " approach=" + i + " standing=" + player.transform.position);
            yield return new WaitForSecondsRealtime(.5f);
            if (player.IsStableGrounded && player.CanStartInteraction && !player.SwimmingActive && interaction.SelectedNPC == npc) yield break;
            Record("APPROACH_REJECT settled=" + player.IsStableGrounded + " selected=" + (interaction.SelectedNPC ? interaction.SelectedNPC.name : "none") + " target=" + npc.name);
        }
        throw new InvalidOperationException("No actual selected/grounded approach within eight bounded candidates for " + npc.name);
    }
    static IEnumerator OpenChest(int index, string label)
    {
        int beforeBonus = loadout.DamageBonus; var npc = chests[index].GetComponent<NPCDialogue>();
        yield return Talk(npc, label, null);
        Check(world.opened[index] && flow.rewards.IsOpen && dialogue.IsRewardPresentationActive, label + " real E grants original equipment and opens reward presentation");
        Check(loadout.DamageBonus == beforeBonus + loadout.RelicBonus(index), label + " original equipment bonus applied exactly once");
        if (index == 2) yield return Capture("eastern-chest-reward-game-view.png");
        yield return Press(Key.Enter); yield return new WaitForSecondsRealtime(.35f);
        Check(!flow.rewards.IsOpen && !dialogue.IsRewardPresentationActive && !player.IsGameplayInputLocked, label + " real Enter dismisses reward and releases input");
        Check(!npc.IsInteractable && interaction.SelectedNPC != npc, label + " collected chest disables its dialogue trigger immediately");
        string previous = ProgressWithoutPose(); int grantedBonus = loadout.DamageBonus;
        yield return Press(Key.E);
        // A different nearby NPC may offer dialogue at the same position; cancel it through real Escape.
        if (dialogue.IsDialogueActive)
        {
            Check(dialogue.ActiveNPC != npc, label + " repeated E can only select a different nearby live NPC");
            Record("OBSERVE repeated chest-position E selected nearby " + dialogue.ActiveNPC.name + "; cancelling via Escape without completing it.");
            yield return Press(Key.Escape);
        }
        Check(!flow.rewards.IsOpen && world.opened[index] && loadout.DamageBonus == grantedBonus && ProgressWithoutPose() == previous,
            label + " repeated real E does not award again or alter progress");
    }
    static IEnumerator SaveContinue(string label)
    {
        phase = "API save/continue " + label;
        Check(flow.SaveNow(), "API_ONLY SaveNow " + label);
        var expected = JsonUtility.FromJson<BaselineSaveData>(ProgressWithoutPose());
        // Existing PrepareMapProgress deliberately treats Continue as skipping an unfinished opening.
        // Normalize only those two specified state transitions; all other serialized progress stays exact.
        expected.openingCompleted = true; expected.northBridgeOpen = true;
        string expectedAfterContinue = JsonUtility.ToJson(expected);
        Check(flow.ContinueGame(), "API_ONLY ContinueGame " + label); yield return new WaitForSecondsRealtime(.65f);
        Check(ProgressWithoutPose() == expectedAfterContinue && player.IsStableGrounded && !player.IsGameplayInputLocked,
            "API_ONLY Continue retained other progress and safe standing position, with documented opening skip transition: " + label);
        Check(flow.Progress.openingCompleted && flow.Progress.northBridgeOpen && layout.BridgeOpen && !flow.Tutorial.Active,
            "API_ONLY Continue marks opening skipped, opens bridge and leaves tutorial inactive: " + label);
    }
    static string ProgressWithoutPose()
    {
        // Copy serialized state for comparison without mutating the live progress object.
        var copy = JsonUtility.FromJson<BaselineSaveData>(JsonUtility.ToJson(flow.Progress)); copy.safePosition = Vector3.zero; copy.safeYaw = 0;
        return JsonUtility.ToJson(copy);
    }
    static IEnumerator Press(Key key)
    {
        SetKeys(); yield return new WaitForSecondsRealtime(.1f);
        Record("INJECT " + key + " frame=" + Time.frameCount + " selected=" + (interaction.SelectedNPC ? interaction.SelectedNPC.name : "none"));
        SetKeys(key); yield return new WaitForSecondsRealtime(.16f); SetKeys(); yield return new WaitForSecondsRealtime(.25f);
    }
    static void SetKeys(params Key[] keys) { desiredInput = new KeyboardState(keys); QueueInput(); }
    static void QueueInput()
    {
        if (keyboard == null || !keyboard.added || mouse == null || !mouse.added) return;
        if (!keyboard.enabled) InputSystem.EnableDevice(keyboard); if (!mouse.enabled) InputSystem.EnableDevice(mouse);
        if (Keyboard.current != keyboard) keyboard.MakeCurrent(); if (Mouse.current != mouse) mouse.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, desiredInput); InputSystem.QueueStateEvent(mouse, new MouseState());
    }
    static void Tick()
    {
        if (!Running) return;
        try
        {
            if (!EditorApplication.isPlaying || !flow || flow.saveKeyPrefix != qaPrefix || SceneManager.GetActiveScene().path != OverworldV2PlayChecks.ValidationScene)
                throw new InvalidOperationException("Play/scene/host/save-prefix changed.");
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Interaction suite exceeded240seconds.");
            EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); QueueInput();
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }
    static IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame(); var image = ScreenCapture.CaptureScreenshotAsTexture(); if (!image) throw new InvalidOperationException("Game View capture unavailable.");
        try { File.WriteAllBytes(Images + name, image.EncodeToPNG()); Record("GAME_VIEW " + Images + name + " " + Pose()); } finally { Object.Destroy(image); }
    }
    static void SwordState(bool hasSword) { if (hasSword) swordGrantEvents++; }
    static bool All(bool[] values, bool expected) { for (int i = 0; i < values.Length; i++) if (values[i] != expected) return false; return true; }
    static void Check(bool ok, string message) { Record((ok ? "PASS " : "FAIL ") + message + " | " + Pose()); if (!ok) throw new InvalidOperationException(message); }
    static void Fail(string message) { failures++; finished = false; try { Record("FAIL " + message + " | " + Pose()); } finally { Stop(); } }
    public static void Stop(bool exitPlay = true)
    {
        if (!Running) return; Running = false; EditorApplication.update -= Tick; GameSession.SwordStateChanged -= SwordState;
        var cleanupErrors = new List<string>(); Action<Action> restore = action => { try { action(); } catch (Exception e) { cleanupErrors.Add(e.Message); } };
        restore(() => { if (host && coroutine != null) host.StopCoroutine(coroutine); coroutine = null; });
        restore(() => { if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); keyboard = null; });
        restore(() => { if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); mouse = null; });
        restore(() => { if (oldKeyboard != null && oldKeyboard.added) { if (oldKeyboardEnabled) InputSystem.EnableDevice(oldKeyboard); else InputSystem.DisableDevice(oldKeyboard); oldKeyboard.MakeCurrent(); } });
        restore(() => { if (oldMouse != null && oldMouse.added) { if (oldMouseEnabled) InputSystem.EnableDevice(oldMouse); else InputSystem.DisableDevice(oldMouse); oldMouse.MakeCurrent(); } });
        restore(() => InputSystem.settings.backgroundBehavior = oldBehavior); restore(() => Application.runInBackground = oldBackground); restore(() => Time.timeScale = oldTimeScale);
        restore(() => { Cursor.lockState = oldCursorLock; Cursor.visible = oldCursorVisible; }); restore(CheckRealSave);
        bool ok = finished && failures == 0 && cleanupErrors.Count == 0; SessionState.SetBool(State + "completed", ok);
        SessionState.SetString(State + "status", ok ? "PASS bounded E/J/Enter checks; tracking button remains manual; restored inputs" : "FAIL/INCOMPLETE; inspect interaction-checks.txt");
        try { Record("RESTORED input/background/time/cursor before Play exit; QA prefix retained through teardown, original Prepare owns slot deletion and scene/hash restoration. completed=" + ok + " errors=" + string.Join("; ", cleanupErrors)); }
        finally { if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying = false; }
    }
    static void CheckRealSave()
    {
        if (SlotHash(realPrefix + ".primary") != primaryHash || SlotHash(realPrefix + ".backup") != backupHash) throw new InvalidOperationException("Real save primary/backup changed during isolated interactions.");
        Record("PASS real save primary/backup hashes unchanged");
    }
    static string SlotHash(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(PlayerPrefs.GetString(key))));
    }
    static void Record(string text) => File.AppendAllText(Report, DateTime.UtcNow.ToString("O") + " " + text + "\n");
    static float Heading(Vector3 a, Vector3 b) => Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
    static string Pose() => player ? "pos=" + player.transform.position + " grounded=" + player.IsStableGrounded + " locked=" + player.IsGameplayInputLocked : "no player";
}
