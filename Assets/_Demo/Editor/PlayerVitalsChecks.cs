using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Controlled integration checks in Play Mode, with a separate save slot and temporary keyboard.
[InitializeOnLoad]
public static class PlayerVitalsChecks
{
    const string Prefix = "QA.PlayerVitals.20260922";
    static readonly List<string> log = new List<string>();
    static IEnumerator routine;
    static double resume;
    static Keyboard keyboard, previousKeyboard;
    static InputSettings.BackgroundBehavior oldBackground;
    static BaselineFlow flow;
    static PlayerMovement movement;
    static PlayerVitals vitals;
    static Health health;
    static int deaths;
    static float lethalStamina;
    static bool lethalSwimming;
    static void CountDeath(Health target, DamageInfo damage) { deaths++; lethalStamina=vitals.Stamina; lethalSwimming=movement.SwimmingActive; }
    static GameObject climbWall;
    static ThirdPersonCamera cameraControl;
    static bool cameraEnabled;
    static Quaternion cameraRotation;
    public static bool Running => routine != null;
    static PlayerVitalsChecks() { AssemblyReloadEvents.beforeAssemblyReload += Stop; }
    public static void Begin()
    {
        if (!EditorApplication.isPlaying || Running) throw new InvalidOperationException("Start Play first, one run at a time.");
        flow = UnityEngine.Object.FindFirstObjectByType<BaselineFlow>();
        if (flow.Started) throw new InvalidOperationException("Start from title to avoid altering a real session.");
        movement = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        vitals = movement.GetComponent<PlayerVitals>(); health = movement.GetComponent<Health>();
        deaths=0; health.Died += CountDeath;
        log.Clear(); Directory.CreateDirectory("ToolReports/PlayerVitals");
        flow.saveKeyPrefix = Prefix; flow.NewGame();
        previousKeyboard = Keyboard.current; oldBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("PlayerVitalsQA"); keyboard.MakeCurrent();
        routine = Run(); resume = 0; EditorApplication.update += Tick;
    }
    static void Check(bool success, string description)
    {
        log.Add((success ? "PASS " : "FAIL ") + description);
        File.WriteAllLines("ToolReports/PlayerVitals/checks.txt", log);
    }
    static void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
    static void Set(string name, float value)
    {
        var so = new SerializedObject(vitals); so.FindProperty(name).floatValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Place(Vector3 position) { movement.Relocate(position, Quaternion.identity); Physics.SyncTransforms(); }
    static IEnumerator Run()
    {
        yield return 1f;
        Check(health.CurrentHealth == 100 && vitals.Stamina == 100 && vitals.AidCharges == 3, "new journey full resources");
        Check(!vitals.TryUseRecovery() || vitals.AidCharges == 3, "full camp recovery consumes nothing");
        Place(new Vector3(0,1.05f,-58)); yield return .6f;
        Check(vitals.ApplyEnvironmentDamage(50) && health.CurrentHealth == 50, "environment damage uses real player Health");
        yield return .55f;
        ScreenCapture.CaptureScreenshot("ToolReports/PlayerVitals/game-health.png");
        yield return .15f;
        Check(vitals.TryUseRecovery() && health.CurrentHealth == 85 && vitals.AidCharges == 2, "field aid heals 35 and spends one");
        vitals.TryUseRecovery();
        Check(health.CurrentHealth == 100 && vitals.AidCharges == 1 && !vitals.TryUseRecovery() && vitals.AidCharges == 1, "healing capped; full health does not consume aid");
        Keys(Key.Space); yield return .15f; Keys(); yield return 1.6f;
        Check(health.CurrentHealth == 100, "injected ordinary jump has no fall damage");
        Place(new Vector3(0,9,-58)); yield return 1.6f;
        Check(health.CurrentHealth < 100 && health.CurrentHealth > 0, "controlled high drop damages on landing, HP="+health.CurrentHealth);
        vitals.Refill(); Place(new Vector3(0,1.05f,-60)); yield return .5f;
        Set("sprintDrain",120); Keys(Key.W,Key.LeftShift); yield return 1.2f;
        Check(vitals.IsExhausted && vitals.Stamina < 20 && !movement.SprintingActive, "injected sprint exhausts and falls back to walking");
        ScreenCapture.CaptureScreenshot("ToolReports/PlayerVitals/game-exhausted.png");
        Keys(); Set("sprintDrain",12); yield return 2.5f;
        Check(vitals.Stamina >= 20 && vitals.CanExert, "standing recovery clears exhaustion threshold");
        // Temporary test wall, never saved in the map; exercises real climb queries and controller.
        vitals.Refill(); Place(new Vector3(0,1.05f,-58));
        climbWall = GameObject.CreatePrimitive(PrimitiveType.Cube); climbWall.name = "QA climb surface";
        climbWall.transform.position = new Vector3(0,4,-56.7f); climbWall.transform.localScale = new Vector3(6,6,.5f);
        cameraControl = Camera.main.GetComponent<ThirdPersonCamera>(); cameraEnabled = cameraControl.enabled;
        cameraRotation = Camera.main.transform.rotation; cameraControl.enabled = false; Camera.main.transform.rotation = Quaternion.identity;
        Physics.SyncTransforms(); Set("climbDrain",70); Keys(Key.W); yield return .6f;
        Check(movement.ClimbingActive && vitals.Stamina < 100, "injected W enters real climbing and drains stamina on temporary wall");
        yield return 1.6f;
        Check(!movement.ClimbingActive && vitals.IsExhausted, "exhausted climber releases wall and cannot immediately reattach");
        Keys(); UnityEngine.Object.Destroy(climbWall); climbWall=null;
        cameraControl.enabled=cameraEnabled; Camera.main.transform.rotation=cameraRotation; cameraControl=null;
        Set("climbDrain",10);
        Place(new Vector3(10,-.9f,-15)); vitals.Refill(); Set("swimDrain",40); Set("treadDrain",40);
        yield return .5f;
        Check(movement.SwimmingActive && vitals.Stamina > 0 && vitals.Stamina < 100 && health.CurrentHealth == 100, "deep water consumes stamina without partial HP damage");
        ScreenCapture.CaptureScreenshot("ToolReports/PlayerVitals/game-water.png"); yield return .15f;
        flow.SetMenu(true); float stamina = vitals.Stamina; int beforeDrowning = deaths;
        yield return 1.2f;
        Check(vitals.Stamina == stamina && health.CurrentHealth == 100 && deaths == beforeDrowning, "pause freezes swimming exhaustion");
        flow.SetMenu(false); yield return 2.8f;
        Check(deaths == beforeDrowning+1 && lethalStamina == 0 && lethalSwimming, "zero swimming stamina emits exactly one lethal health event");
        Check(Vector3.Distance(movement.transform.position,BaselineSaveData.DefaultSafePosition)<2 && health.CurrentHealth==100 && vitals.Stamina==100,
            "water exhaustion returns to camp immediately with restored resources (no four-second grace)");
        Place(new Vector3(0,1.05f,-58)); Set("swimDrain",7); Set("treadDrain",4); yield return .5f;
        int landedHP = health.CurrentHealth; yield return 1.3f;
        Check(!movement.SwimmingActive && health.CurrentHealth == landedHP, "dry land has no lingering water/fall damage");
        flow.GetComponent<BaselineWorld>().accepted[0] = true; flow.GetComponent<BaselineWorld>().opened[1] = true;
        flow.Progress.trialRewardClaimed[0] = true; flow.Progress.unequippedMask = 8;
        flow.GetComponent<BaselineWorld>().Track(0);
        flow.SaveNow(); int tracked = flow.GetComponent<BaselineWorld>().Tracked;
        vitals.ApplyEnvironmentDamage(500); yield return 1f;
        Check(health.CurrentHealth == 100 && vitals.AidCharges == 3 && Vector3.Distance(movement.transform.position,BaselineSaveData.DefaultSafePosition)<2, "death restores resources at camp");
        Check(flow.GetComponent<BaselineWorld>().accepted[0] && flow.GetComponent<BaselineWorld>().Tracked == tracked, "death retains accepted quest and manual tracking");
        Check(flow.GetComponent<BaselineWorld>().opened[1] && flow.Progress.trialRewardClaimed[0] && flow.Progress.unequippedMask==8, "death retains seeded chest and equipment ownership/equip choice");
        Place(new Vector3(0,-12,-58)); yield return .8f;
        Check(health.CurrentHealth == 100 && Vector3.Distance(movement.transform.position,BaselineSaveData.DefaultSafePosition)<2, "falling out of world restores safely at camp");
        Place(new Vector3(0,1.05f,-58)); yield return .4f; vitals.ApplyEnvironmentDamage(30);
        Place(new Vector3(0,1.05f,-67)); yield return .4f;
        Check(vitals.TryUseRecovery() && health.CurrentHealth == 100 && vitals.AidCharges == 3, "camp rest replenishes health and aid");
        Place(new Vector3(0,1.05f,-58)); yield return .4f; vitals.ApplyEnvironmentDamage(30);
        flow.SaveNow(); flow.ContinueGame(); yield return .8f;
        Check(health.CurrentHealth == 100 && vitals.Stamina == 100 && flow.GetComponent<BaselineWorld>().accepted[0], "continue policy refills resources and retains saved progress");
        vitals.ApplyEnvironmentDamage(25); int before = health.CurrentHealth;
        SideWorldTravel.Instance.Enter(false); yield return 5f;
        Check(SideWorldTravel.Instance.IsAway && !movement.gameObject.activeInHierarchy, "native A suspends overworld player");
        SideWorldTravel.Instance.ReturnToMain(); yield return 3f;
        Check(!SideWorldTravel.Instance.IsAway && health.CurrentHealth == before && flow.GetComponent<BaselineWorld>().accepted[0], "native A return preserves host health and quest");
        Place(new Vector3(0,1.05f,-67)); yield return .5f; vitals.TryUseRecovery();
        ScreenCapture.CaptureScreenshot("ToolReports/PlayerVitals/game-camp.png");
        yield return .5f;
        Check(Time.timeScale == 1, "final time scale normal");
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Stop(); return; }
        if (EditorApplication.timeSinceStartup < resume) return;
        try
        {
            if (!routine.MoveNext()) { Stop(); return; }
            resume = EditorApplication.timeSinceStartup + (routine.Current is float delay ? delay : .02);
        }
        catch(Exception e) { Check(false,e.ToString()); Stop(); }
    }
    public static void Stop()
    {
        EditorApplication.update -= Tick; routine = null;
        if (health) health.Died -= CountDeath;
        if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; InputSystem.settings.backgroundBehavior = oldBackground; }
        if (climbWall) UnityEngine.Object.Destroy(climbWall);
        if (cameraControl) { cameraControl.enabled=cameraEnabled; if (Camera.main) Camera.main.transform.rotation=cameraRotation; cameraControl=null; }
        if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        if (log.Count > 0) File.WriteAllLines("ToolReports/PlayerVitals/checks.txt",log);
    }
    public static void ClearTestSlot()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before clearing QA slot.");
        BaselineSaveStore.Clear(Prefix);
    }
}
