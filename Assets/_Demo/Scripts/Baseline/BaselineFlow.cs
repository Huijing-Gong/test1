using UnityEngine;
using UnityEngine.InputSystem;

// Host-only menus and durable progress. Side worlds keep their original controls.
[DefaultExecutionOrder(300)]
[RequireComponent(typeof(BaselineWorld))]
public sealed partial class BaselineFlow : MonoBehaviour
{
    public bool useSamEnding; // Use the authored Sam arena at the existing final-room entrance.
    public BaselineSamBridge SamBridge { get; private set; }
    public string saveKeyPrefix = "LakeDomain.Baseline.Save";
    public BaselineEndingRoom endingRoom;
    public BaselineRewardPresentation rewards;
    public Transform endingEntrance;
    [Tooltip("One definition for each existing task. Empty IDs grant nothing. Effects are not inferred from text.")]
    public BaselineTaskReward[] taskRewards = { new BaselineTaskReward(), new BaselineTaskReward(), new BaselineTaskReward() };
    public bool Started { get; private set; }
    public bool EclipseCinematicActive => eclipse && eclipse.IsPlaying;
    BaselineEclipseSequence eclipse;
    public bool MenuOpen { get; private set; }
    public bool VictoryOpen { get; private set; }
    public bool MapOpen { get; private set; }
    // Transient roadside encounters are session state, not part of the save slot.
    public event System.Action WorldEncountersReset;
    public BaselineCampTutorial Tutorial { get; private set; }
    BaselineMap overview;
    public bool DeathOpen => movement && movement.GetComponent<PlayerVitals>() is PlayerVitals vitals && vitals.DeathOpen;
    public bool BlockWorldInput => InventoryOpen || EclipseCinematicActive || SideWorldEntrance.ConfirmationOpen || TitleScreenVisible || (SamBridge && SamBridge.IsAway) || !Started || MenuOpen || (world && world.JournalOpen) || openingClosing || p4Closing || VictoryOpen || DeathOpen || (Tutorial && Tutorial.ModalOpen) || (endingRoom && endingRoom.IsInside) || (rewards && rewards.IsOpen);
    // Presentation overlays replace the exploration HUD, including the health
    // canvas and the IMGUI stamina gauge. Do not use BlockWorldInput here:
    // the final room also blocks overworld input while its gameplay remains visible.
    public bool WorldHudHidden => InventoryOpen || EclipseCinematicActive || SideWorldEntrance.ConfirmationOpen || TitleScreenVisible || (SamBridge && SamBridge.IsAway) || MenuOpen || openingClosing || p4Closing || MapOpen || VictoryOpen || DeathOpen ||
        (endingRoom && endingRoom.IntroDialogueActive) ||
        (world && world.JournalOpen) || (Tutorial && Tutorial.ModalOpen) ||
        (rewards && rewards.IsOpen) ||
        (dialogue && (dialogue.IsDialogueActive || dialogue.IsRewardPresentationActive));
    public bool NormalWorldPause => !(SamBridge && SamBridge.IsAway) && Started && MenuOpen && !MapOpen && !VictoryOpen &&
        !(endingRoom && endingRoom.IsInside) && !(Tutorial && Tutorial.ModalOpen) &&
        !(rewards && rewards.IsOpen) &&
        !(dialogue && (dialogue.IsDialogueActive || dialogue.IsRewardPresentationActive));
    public BaselineSaveData Progress { get; private set; } = new BaselineSaveData();
    public string Status { get; private set; } = "Explore freely. Your progress can be saved in this browser or Editor.";
    // Retain an accepted-save notice independently of ordinary success Status.
    // Acknowledgement never revokes progress or grants a reward.
    public string SaveWarning { get; private set; } = "";
    public string SaveWarningSummary => string.IsNullOrEmpty(SaveWarning) ? "" :
        (SaveWarning.IndexOf("backup", System.StringComparison.OrdinalIgnoreCase) >= 0
            ? "Saved. A backup was not confirmed. Rewards kept. Open Esc for details."
            : "Saved with a storage notice. Rewards kept. Open Esc for details.");
    BaselineWorld world;
    OverworldV2Layout layout;
    Vector3 CampPosition => layout ? layout.CampPosition : BaselineSaveData.DefaultSafePosition;
    PlayerMovement movement;
    ThirdPersonCamera look;
    DialogueUI dialogue;
    bool hasSave, slotExists, confirmNew, ownsLock;
    float oldTimeScale = 1, nextSave;
    int unlockAfterFrame = -1;
    bool retryBossAfterDeath;
    BaselineSaveData availableSave;
    SideWorldTravel travel;
    bool victorySaved;
    bool announcedAReward, announcedBReward;
    readonly bool[] announcedTaskRewards = new bool[3];
    Vector2 menuScroll;
    GUIStyle titleStyle, bodyStyle, confirmPromptStyle;
    bool p4Closing;
    int p4MotionTargetPage;
    int[] p4MotionFrames, p4MotionDurations;
    float p4MotionStarted;
    int p4PageFrom=-1, p4PageTo=-1;
    int p4PageInputBlockedFrame=-1;
    bool openingClosing;
    int p4TransientFrame=-1;
    Texture2D p4TransientTexture;
    readonly Texture2D[] p4PageTextures=new Texture2D[3];
    static GUIStyle p4HitStyle;
    bool p4CollectedRewards;
    int p4HoveredGear = -1;
    float p4GearHoverStarted;
    static Texture2D[] p4FinalEquipmentIcons;
    static Texture2D p4BookmarkTop;
    static Texture2D p4BossJourneyPage;

    void Start()
    {
        layout = GetComponent<OverworldV2Layout>();
        world = GetComponent<BaselineWorld>(); movement = world.player.GetComponent<PlayerMovement>();
        look = Camera.main.GetComponent<ThirdPersonCamera>(); dialogue = FindFirstObjectByType<DialogueUI>();
        if (!rewards) rewards = GetComponent<BaselineRewardPresentation>();
        if (!endingRoom) endingRoom = GetComponent<BaselineEndingRoom>();
        Tutorial = GetComponent<BaselineCampTutorial>();
        overview = GetComponent<BaselineMap>();
        if (!overview) overview = gameObject.AddComponent<BaselineMap>();
        var minimap = GetComponent<BaselineMinimap>();
        if (!minimap) minimap = gameObject.AddComponent<BaselineMinimap>();
        minimap.Initialize(this, world, overview);
        var quickGear = GetComponent<BaselineQuickGearBar>();
        if (!quickGear) quickGear = gameObject.AddComponent<BaselineQuickGearBar>();
        quickGear.Initialize(this, world);
        if(GetComponent<WayfarerInventory.InventoryLab>())quickGear.enabled=false;
        if (endingRoom) endingRoom.Won += OnWon;
        world.StateChanged += OnProgressChanged;
        GameSession.SwordStateChanged += OnSwordChanged;
        travel=SideWorldTravel.Instance;
        if(travel)
        {
            travel.TrialACompletionRecorded+=OnTrialACompleted;
            travel.TrialBCompletionRecorded+=OnTrialBCompleted;
        }
        hasSave = BaselineSaveStore.TryLoad(saveKeyPrefix, out availableSave, out var message);
        if (!string.IsNullOrEmpty(message)) Status = message;
        RefreshSlotPresence();
        if (useSamEnding)
        {
            var host = new GameObject("Main world Sam host");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, gameObject.scene);
            SamBridge = host.AddComponent<BaselineSamBridge>(); SamBridge.Initialize(this);
        }
        MenuOpen = true; Lock();
        InitializeCloudTitle();
        eclipse = GetComponent<BaselineEclipseSequence>();
        if (!eclipse) eclipse = gameObject.AddComponent<BaselineEclipseSequence>();
    }
    void Update()
    {
        if(WorldInputBindings.ConsumedInterfaceInput)return;
        if (EclipseCinematicActive || (SamBridge && SamBridge.Busy)) return;
        if (SideWorldEntrance.ConfirmationOpen || SideWorldEntrance.InputConsumedFrame == Time.frameCount) return;
        if (!world) return;
        if (TickCloudTitle()) return;
        if (InventoryOpen || InventoryClosedFrame==Time.frameCount) return;
        if (DeathOpen) return;
        if (Tutorial && (Tutorial.ModalOpen || Tutorial.LastSkipInputFrame == Time.frameCount)) return;
        if (Started && SideWorldTravel.Instance && SideWorldTravel.Instance.TrialACompleted && !Progress.trialACompleted)
        { Progress.trialACompleted = true; SaveNow(); Status = "Trial A complete. Open the menu to claim your reward."; }
        if (Started && SideWorldTravel.Instance && SideWorldTravel.Instance.TrialBCompleted && (!Progress.trialBCompleted || SideWorldTravel.Instance.TrialBBonus>Progress.trialBDifficultyBonus))
        { OnTrialBCompleted(); Status = "Trial B complete. Capacity reward updated; claim the B equipment reward once in the menu."; }
        var keys=Keyboard.current;
        if (world.LastJournalChangedFrame==Time.frameCount) return;
        if (world.JournalOpen && keys!=null && WorldInputBindings.Pressed("Map")) { ShowMap(); return; }
        if (MenuOpen && !confirmNew && keys!=null && WorldInputBindings.Pressed("Journal")) { CloseMapToTasks(); return; }
        bool occupied=(dialogue && (dialogue.IsDialogueActive || dialogue.IsRewardPresentationActive)) ||
            (endingRoom && endingRoom.IntroDialogueActive);
        if (Started && !VictoryOpen && !occupied && !world.JournalOpen && world.LastJournalClosedFrame!=Time.frameCount && (!dialogue || dialogue.LastDialogueClosedFrame!=Time.frameCount) && keys!=null && keys.escapeKey.wasPressedThisFrame)
            {if(!MenuOpen||!WorldControlsPanel.TryBack())SetMenu(!MenuOpen);}
        else if (Started && !VictoryOpen && !occupied && !world.JournalOpen && keys != null && WorldInputBindings.Pressed("Map"))
        {
            if (MapOpen) SetMenu(false); else ShowMap();
        }
        if (!Started || MenuOpen || VictoryOpen || occupied || world.JournalOpen) return;
        if(Progress.trialACompleted && !Progress.trialRewardClaimed[0] && !announcedAReward)
        {
            announcedAReward=true;
            Status="Trial A complete. Press Esc to claim your reward.";
            world.ShowNotice(Status);
        }
        if(Progress.trialBCompleted && !Progress.trialRewardClaimed[1] && !announcedBReward)
        {
            announcedBReward=true;
            Status="Trial B complete. Press Esc to claim your reward.";
            world.ShowNotice(Status);
        }
        for (int i = 0; i < 3; i++)
            if (TaskRewardAvailable(i) && !announcedTaskRewards[i])
            {
                announcedTaskRewards[i] = true;
                world.ShowNotice("Task reward ready. Press Esc to collect it; no return trip needed.");
                break;
            }
        if (endingEntrance && endingRoom && !endingRoom.IsInside && Vector3.Distance(world.player.position,endingEntrance.position)<3.4f)
        {
            dialogue.OfferPrompt("[E] Enter the final room (optional)",20);
            if (keys!=null && WorldInputBindings.Pressed("Interact")) EnterEnding();
        }
        if (Time.unscaledTime>=nextSave) { nextSave=Time.unscaledTime+8; SaveNow(); }
    }
    public bool EnterEnding()
    {
        if (EclipseCinematicActive || (SamBridge && SamBridge.Busy)) return false;
        if ((Tutorial && Tutorial.ModalOpen) || !Started || MenuOpen || VictoryOpen || !endingRoom || (dialogue && (dialogue.IsDialogueActive || dialogue.IsRewardPresentationActive))) return false;
        if (!GameSession.Instance || !GameSession.Instance.HasSword) { Status="Speak to the guide for your practice sword first."; world.ShowNotice(Status); return false; }
        if (!GameSession.Instance.SwordEquipped)
        {
            int previous = Progress.unequippedMask; Progress.unequippedMask &= ~(1 << 5);
            if (!SaveNow()) { Progress.unequippedMask = previous; return false; }
            GameSession.Instance.SetSwordEquipped(true);
        }
        world.SetJournal(false);
        var quickGear = GetComponent<BaselineQuickGearBar>(); if (quickGear) quickGear.CloseForTransition();
        if (SamBridge)
        {
            bool accepted = SamBridge.TryEnter();
            if (!accepted && !string.IsNullOrEmpty(SamBridge.LastMessage)) world.ShowNotice(SamBridge.LastMessage);
            return accepted;
        }
        RememberSafePosition(); SaveNow();
        bool entered=endingRoom.Enter(); if (!entered) world.ShowNotice(endingRoom.LastMessage);
        return entered;
    }
    void LateUpdate()
    {
        if (DeathOpen || (SamBridge && SamBridge.Busy)) return;
        if (unlockAfterFrame>=0 && Time.frameCount>unlockAfterFrame && !InventoryOpen && !MenuOpen && !MapOpen && !world.JournalOpen && !VictoryOpen && !p4Closing && !openingClosing && !TitleScreenVisible && !(Tutorial && Tutorial.ModalOpen))
        {
            unlockAfterFrame=-1; Unlock();
            if (retryBossAfterDeath)
            {
                retryBossAfterDeath=false;
                if (endingRoom) endingRoom.Enter();
            }
        }
    }
    public void SetMenu(bool open)
    {
        if(!open)WorldControlsPanel.ShowOverview();
        if(InventoryOpen)return;
        if (EclipseCinematicActive || (SamBridge && SamBridge.Busy)) return;
        if (TitleScreenVisible) return;
        if (DeathOpen) return;
        if (Tutorial && Tutorial.ModalOpen) return;
        if (endingRoom && endingRoom.IntroDialogueActive) return;
        bool wasMapOpen=MapOpen;
        if (!Started && !open) return;
        MenuOpen=open; confirmNew=false;
        if (!open) MapOpen = false;
        if(open) { p4Closing=false; p4MotionFrames=null; p4MotionDurations=null; menuScroll=Vector2.zero; world.SetJournal(false); SaveNow(); Lock(); if(Started && !wasMapOpen && menuPage==0) BeginP4Motion(new[]{1,2,3,4,5,6,7,8,9,10},new[]{80,80,80,80,80,80,80,90,90,90},menuPage); }
        else
        {
            unlockAfterFrame=Time.frameCount;
            if(Started && !wasMapOpen && !VictoryOpen)
            {
                if(menuPage==0) BeginP4Motion(new[]{24,25,26,27,28,29,30,31,32},new[]{80,80,80,80,80,80,80,80,80},menuPage);
                else BeginP4Motion(new[]{24},new[]{180},menuPage);
            }
        }
    }
    public bool ShowMap()
    {
        if(InventoryOpen)return false;
        if (EclipseCinematicActive || (SamBridge && SamBridge.Busy)) return false;
        if (DeathOpen) return false;
        if (Tutorial && Tutorial.ModalOpen) return false;
        if (!isActiveAndEnabled || !Started || !world || VictoryOpen || MapOpen ||
            (endingRoom && endingRoom.IsInside) || (rewards && rewards.IsOpen) ||
            (dialogue && (dialogue.IsDialogueActive || dialogue.IsRewardPresentationActive)) ||
            (SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy))) return false;
        SetMenu(true); MapOpen = true; overview.Prepare(this); return true;
    }
    bool MapTasksAvailable() => isActiveAndEnabled && Started && world && !TitleScreenVisible && !DeathOpen && !VictoryOpen &&
        !openingClosing && !p4Closing && !(Tutorial && Tutorial.ModalOpen) &&
        !(SamBridge && SamBridge.IsAway) && !(endingRoom && endingRoom.IsInside) && !(rewards && rewards.IsOpen) &&
        !(dialogue && (dialogue.IsDialogueActive || dialogue.IsRewardPresentationActive)) &&
        !(SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy));
    public bool CanOpenJournal => MapTasksAvailable() && !MenuOpen;
    public void JournalPresentationChanged(bool open)
    {
        if (open) { Lock(); return; }
        // A map, reward or tutorial may have taken over the same Flow lock.
        if (!BlockWorldInput) unlockAfterFrame=Time.frameCount;
    }
    public void CloseMapToTasks()
    {
        if ((!MapOpen && !MenuOpen) || confirmNew || !MapTasksAvailable()) return;
        // Transfer ownership without one unpaused frame between M and J.
        MapOpen=false; MenuOpen=false; unlockAfterFrame=-1;
        p4Closing=false; p4MotionFrames=null; p4MotionDurations=null;
        world.SetJournal(true);
    }
    public void CloseMapToMenu() { MapOpen = false; }
    public void HoldTutorialCard() { if (Started && !MenuOpen) Lock(); }
    public void ReleaseTutorialCard()
    {
        if (Started && !MenuOpen && !VictoryOpen) unlockAfterFrame=Time.frameCount;
    }
    void Lock()
    {
        unlockAfterFrame=-1;
        var combat=world.player.GetComponent<PlayerCombat>(); if(combat)combat.CancelForTransition();
        if(!ownsLock) {oldTimeScale=Time.timeScale; ownsLock=true;}
        movement.SetGameplayInputLocked(true); look.SetLookInputLocked(true); Time.timeScale=0;
        Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
    }
    void Unlock()
    {
        if(!ownsLock)return;
        ownsLock=false; Time.timeScale=oldTimeScale>0?oldTimeScale:1;
        if(movement)movement.SetGameplayInputLocked(false); if(look)look.SetLookInputLocked(false);
        Cursor.lockState=CursorLockMode.Locked; Cursor.visible=false;
    }
    public void NewGame()
    {
        if(Started && MenuOpen) BeginP4Motion(new[]{24,25,26,27,28,29,30,31,32},new[]{80,80,80,80,80,80,80,80,80},menuPage);
        try { BaselineSaveStore.Clear(saveKeyPrefix); }
        catch(System.Exception exception)
        {
            Status="Could not start a new journey: "+exception.Message;
            slotExists=true; // Replacement could not be confirmed; do not apply fresh progress.
            return;
        }
        slotExists=hasSave=false; SaveWarning="";
        Apply(new BaselineSaveData(), false); if (Tutorial) Tutorial.Begin(); Status="New journey. Explore freely."; SaveNow();
    }
    public bool ContinueGame()
    {
        hasSave=BaselineSaveStore.TryLoad(saveKeyPrefix,out var data,out var message);
        Status=message; RefreshSlotPresence();
        if(!hasSave)return false;
        bool layoutChanged = Apply(data, true);
        // Reading the store is side-effect free. Persist the approved migration
        // only once the world has restored; SaveStore keeps the previous raw copy.
        if (layoutChanged) SaveProgress(false);
        return true;
    }
    void RefreshSlotPresence()
    {
        try { slotExists=BaselineSaveStore.HasAnyData(saveKeyPrefix); }
        catch(System.Exception exception)
        {
            slotExists=true; // A failed presence check must not bypass replacement confirmation.
            Status="Could not inspect saved progress: "+exception.Message;
        }
    }
    bool Apply(BaselineSaveData data, bool continuing)
    {
        // An accepted restore supersedes the previous death presentation.
        var deathUI = movement.GetComponent<MainWorldDeathUI>();
        if (deathUI) deathUI.DismissForWorldRestore();
        if (Tutorial) Tutorial.Cancel(); // Continue never replays this temporary introduction.
        Started=false; // Restoration events must not autosave partially applied state.
        Lock(); // Public load/new-game calls also need to own the lock released next frame.
        if(dialogue)dialogue.EndDialogue();
        if(endingRoom && endingRoom.IsInside)endingRoom.ReturnToTower();
        bool layoutChanged = false;
        if (layout)
        {
            // The first spawn is owned by the flow, even if tutorial validation fails.
            // Camp remains the destination for old-map migration and recovery.
            layoutChanged = PrepareMapProgressForLayout(data, continuing ? layout.CampPosition : layout.ArrivalPosition, continuing, layout.AuthoredMapVersion, layout.PositionScaleFromVersion2);
            layout.ApplyBridgeState(data.northBridgeOpen);
        }
        Progress=data; GameSession.Instance.RestoreProgress(data.introCompleted,data.hasSword,data.bossDefeated,(data.unequippedMask & (1 << 5)) == 0);
        if(SideWorldTravel.Instance)
        {
            SideWorldTravel.Instance.RestoreTrialACompletion(data.trialACompleted);
            SideWorldTravel.Instance.RestoreTrialBCompletion(data.trialBCompleted,data.trialBDifficultyBonus);
        }
        world.RestoreProgress(data); if(endingRoom)endingRoom.SetCompleted(data.bossDefeated);
        var position=data.safePosition;
        // Saved positions are main-world standing points, never side-world or room coordinates.
        if(!world.NavigationGround(position,out var floor))
        {
            position=CampPosition;
            if(world.NavigationGround(position,out floor)) position.y=floor.y-.10f;
        }
        else position=new Vector3(position.x,floor.y-.10f,position.z);
        var combat=world.player.GetComponent<PlayerCombat>(); if(combat)combat.CancelForTransition();
        movement.Relocate(position,Quaternion.Euler(0,data.safeYaw,0)); Physics.SyncTransforms();
        look.ResetOrbit(data.safeYaw, 14);
        var vitals = movement.GetComponent<PlayerVitals>(); if (vitals) { vitals.SetTrialBBonus(data.trialBDifficultyBonus); vitals.Refill(); }
        movement.SetGameplayInputLocked(true); look.SetLookInputLocked(true);
        Started=true; MenuOpen=false; MapOpen=false; VictoryOpen=false; confirmNew=false; announcedAReward=false; announcedBReward=false;
        menuScroll=Vector2.zero;
        System.Array.Clear(announcedTaskRewards, 0, announcedTaskRewards.Length);
        WorldEncountersReset?.Invoke();
        Physics.SyncTransforms();
        unlockAfterFrame=Time.frameCount; nextSave=Time.unscaledTime+8;
        ResetEndingOutcomeSession();
        return layoutChanged;
    }
    // Kept independent of scene state so migration tests can verify that every
    // existing progress identity and reward receipt survives unchanged.
    static bool PrepareMapProgress(BaselineSaveData data, Vector3 landing, bool continuing)
    {
        return PrepareMapProgressForLayout(data, landing, continuing, OverworldV2Layout.CurrentMapVersion, 1f);
    }
    // Pure authored-map policy, exposed for isolated regression without inspecting live state.
    public static bool PrepareMapProgressForLayout(BaselineSaveData data, Vector3 landing, bool continuing, int targetVersion, float scaleFromVersion2)
    {
        bool migrated = data.mapVersion != targetVersion;
        bool changed = migrated || data.safePositionWasReset;
        if (migrated || data.safePositionWasReset)
        {
            if (data.mapVersion == 2 && targetVersion == 3 && !data.safePositionWasReset && continuing)
                data.safePosition = new Vector3(data.safePosition.x * scaleFromVersion2, data.safePosition.y, data.safePosition.z * scaleFromVersion2);
            else
            {
                data.safePosition = landing;
                data.safeYaw = 0;
            }
            data.mapVersion = targetVersion;
            data.safePositionWasReset = false;
        }
        bool open = continuing; // Continue treats an unfinished opening as skipped.
        if (data.openingCompleted != open || data.northBridgeOpen != open) changed = true;
        data.openingCompleted = open;
        data.northBridgeOpen = open;
        return changed;
    }
    // Tutorial completion and explicit skip share the approved gate transition.
    // Cancel/scene suspension must never call this. Failed disk writes keep the
    // bridge available in-session and are retried by the existing autosave path.
    public bool CompleteOpeningTutorial()
    {
        if (!Started || !layout || Progress == null) return false;
        Progress.openingCompleted = true;
        Progress.northBridgeOpen = true;
        layout.ApplyBridgeState(true);
        return SaveNow();
    }
    public bool RespawnPlayerAtCamp() => RespawnAfterDeath(CampPosition, false);

    public bool RetryAfterDeath()
    {
        bool bossAttempt = endingRoom && endingRoom.IsInside;
        return RespawnAfterDeath(Progress.safePosition, bossAttempt);
    }

    bool RespawnAfterDeath(Vector3 destination, bool retryBoss)
    {
        if (!Started || !world || !movement || (SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy))) return false;
        if (Tutorial && Tutorial.CameraTour) Tutorial.CameraTour.Abort();
        world.SetJournal(false);
        if (dialogue) dialogue.EndDialogue();
        if (endingRoom && endingRoom.IsInside) endingRoom.ReturnToTower();
        var combat = movement.GetComponent<PlayerCombat>(); if (combat) combat.CancelForTransition();
        bool safeDestination = world.NavigationGround(destination, out var floor);
        if (!safeDestination)
        {
            destination = CampPosition;
            safeDestination = world.NavigationGround(destination, out floor);
            retryBoss = false;
        }
        if (safeDestination) destination = new Vector3(destination.x, floor.y - .1f, destination.z);
        Lock();
        movement.Relocate(destination, Quaternion.identity); Physics.SyncTransforms();
        var vitals = movement.GetComponent<PlayerVitals>(); if (vitals) vitals.Refill();
        WorldEncountersReset?.Invoke();
        Physics.SyncTransforms();
        movement.SetGameplayInputLocked(true); look.SetLookInputLocked(true);
        MenuOpen = MapOpen = VictoryOpen = false;
        Progress.safePosition = destination; Progress.safeYaw = 0;
        bool saved = SaveProgress(false);
        retryBossAfterDeath = retryBoss;
        Status = (retryBoss ? "The Guardian awaits a rematch. " : "Recovered at a safe point. ") +
            "Quest, chest and equipment progress retained." + (saved ? "" : " Save failed; progress remains in this session.");
        world.ShowNotice(Status); unlockAfterFrame = Time.frameCount;
        return true;
    }
    void RememberSafePosition()
    {
        // Scene teardown can destroy the player before this host is disabled.
        // Preserve the last standing point while still allowing progress to be saved.
        if(!Started || !world || !world.player || (endingRoom && endingRoom.IsInside) || (SideWorldTravel.Instance && SideWorldTravel.Instance.IsAway))return;
        var p=world.player.position;
        float safeExtent = layout ? Mathf.Max(1f, layout.MapHalfExtent - 1f) : 84f;
        if(Mathf.Abs(p.x)>safeExtent || Mathf.Abs(p.z)>safeExtent || !world.NavigationGround(p,out var ground) || Mathf.Abs(p.y-ground.y)>.8f)return;
        Progress.safePosition=new Vector3(p.x,ground.y-.1f,p.z); Progress.safeYaw=world.player.eulerAngles.y;
    }
    public bool SaveNow()
    {
        bool accepted = SaveProgress(true, out var message);
        NotifyEndingOwnerSaveResult(accepted, message);
        return accepted;
    }
    bool SaveProgress(bool rememberPosition)
    {
        return SaveProgress(rememberPosition, out _);
    }
    bool SaveProgress(bool rememberPosition, out string message)
    {
        message = "No active main-world progress.";
        if(!Started || !world)return false;
        if (rememberPosition) RememberSafePosition();
        world.CopyProgressTo(Progress);
        var session=GameSession.Instance;
        if(session){Progress.introCompleted=session.IntroDialogueCompleted;Progress.hasSword=session.HasSword;Progress.bossDefeated=session.BossDefeated;}
        if(SideWorldTravel.Instance)
        {
            Progress.trialACompleted|=SideWorldTravel.Instance.TrialACompleted;
            Progress.trialBCompleted|=SideWorldTravel.Instance.TrialBCompleted;
            Progress.trialBDifficultyBonus=Mathf.Max(Progress.trialBDifficultyBonus,SideWorldTravel.Instance.TrialBBonus);
        }
        bool ok=BaselineSaveStore.Save(saveKeyPrefix,Progress,out message);
        if(!ok)
        {
            Status=message;
            if (!string.IsNullOrEmpty(message) &&
                message.IndexOf("Persistent storage could not be confirmed.", System.StringComparison.Ordinal) >= 0)
                Status="Save not confirmed. Current session kept; retry Save progress before leaving. Reloaded progress may differ.\n"+message;
        }
        else
        {
            hasSave=slotExists=true;
            // Keep an unacknowledged backup notice ahead of later routine adjustments.
            // Other notices keep the first pending text; identical repeats add nothing.
            if (!string.IsNullOrEmpty(message) && message != "Progress saved." &&
                (string.IsNullOrEmpty(SaveWarning) ||
                 (SaveWarning.IndexOf("backup", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                  message.IndexOf("backup", System.StringComparison.OrdinalIgnoreCase) >= 0)))
                SaveWarning=message;
        }
        return ok;
    }
    void OnProgressChanged(){if(Started && !(world && world.IsNotifyingSavedChest))SaveNow();}
    void OnSwordChanged(bool acquired){if(Started)SaveNow();}
    void OnTrialACompleted(){if(!Started)return; Progress.trialACompleted=true;SaveNow();}
    void OnTrialBCompleted(){if(!Started)return; Progress.trialBCompleted=true;
        Progress.trialBDifficultyBonus=Mathf.Max(Progress.trialBDifficultyBonus,SideWorldTravel.Instance?SideWorldTravel.Instance.TrialBBonus:0);
        var vitals=world.player.GetComponent<PlayerVitals>();if(vitals)vitals.SetTrialBBonus(Progress.trialBDifficultyBonus);SaveNow();}
    void OnWon()
    {
        GameSession.Instance.MarkBossDefeated(); Progress.bossDefeated=true; victorySaved=SaveNow();
        VictoryOpen=true; MenuOpen=false;
        menuScroll=Vector2.zero;
        if(victorySaved)Status="Victory. The final guardian has fallen. Progress saved.";
        Lock();
    }
    public void LeaveEnding()
    {
        Lock();
        if(endingRoom)endingRoom.ReturnToTower(); VictoryOpen=false; MenuOpen=false;
        // Relocate clears movement locks. Retain ours until the dismissal frame is over.
        movement.SetGameplayInputLocked(true); look.SetLookInputLocked(true);
        SaveNow();unlockAfterFrame=Time.frameCount;
    }
    public bool ClaimTrialReward(int index)
    {
        if(!CanClaimReward() ||
            index<0 || index>1 || Progress.trialRewardClaimed[index] || !(index==0?Progress.trialACompleted:Progress.trialBCompleted))return false;
        return GrantTrialReward(index);
    }
    // Compatibility entry for old tools: entering B no longer grants a reward.
    // Existing claimed gifts remain owned through the same saved reward bit.
    public bool ClaimSideBEntranceReward(SideWorldEntrance entrance)
    {
        return false;
    }
    bool GrantTrialReward(int index)
    {
        Progress.trialRewardClaimed[index]=true;
        if(!SaveNow()) {Progress.trialRewardClaimed[index]=false;return false;}
        MenuOpen=false;confirmNew=false;unlockAfterFrame=-1; Unlock();
        Status="Trial reward saved.";
        var loadout=world.player.GetComponent<BaselineLoadout>();
        string description=loadout?"Equipped. "+loadout.EquipmentEffect(index+3)+". Manage equipment in the journey menu.":"Your trial reward is recorded.";
        if (!string.IsNullOrEmpty(SaveWarning)) description += "\n" + SaveWarningSummary;
        if(!rewards || !rewards.Show("Trial "+(index==0?"A":"B")+" reward",description,true,()=>{})) world.ShowNotice(Status);
        return true;
    }
    bool CanClaimReward() => isActiveAndEnabled && Started && world && !VictoryOpen && !MapOpen && !world.JournalOpen &&
        (!endingRoom || !endingRoom.IsInside) && (!rewards || !rewards.IsOpen) &&
        (!dialogue || (!dialogue.IsDialogueActive && !dialogue.IsRewardPresentationActive)) &&
        (!SideWorldTravel.Instance || (!SideWorldTravel.Instance.IsAway && !SideWorldTravel.Instance.Busy)) &&
        (MenuOpen || InventoryOpen || (movement && movement.CanStartInteraction));

    // Fixed map stations are ordinary world interactions, never menu actions or checkpoints.
    public bool CanUseWaystations => isActiveAndEnabled && world && world.player && movement &&
        !BlockWorldInput && !MapOpen && !world.JournalOpen && Time.timeScale > 0 &&
        movement.CanStartInteraction && world.LastJournalClosedFrame != Time.frameCount &&
        (!Tutorial || Tutorial.LastSkipInputFrame != Time.frameCount) &&
        (!dialogue || (!dialogue.IsDialogueActive && !dialogue.IsRewardPresentationActive &&
            dialogue.LastDialogueClosedFrame != Time.frameCount)) &&
        (!SideWorldTravel.Instance || (!SideWorldTravel.Instance.IsAway && !SideWorldTravel.Instance.Busy));

    public bool UseWaystationReturn(BaselineWaystation station)
    {
        if (!station || !station.CanInteract(this)) return false;
        var camp = CampPosition;
        if (!world.NavigationGround(camp, out var floor))
        {
            Status = "The camp arrival point is blocked. The return device is unavailable.";
            world.ShowNotice(Status); return false;
        }
        camp = new Vector3(camp.x, floor.y - .1f, camp.z);
        var oldPosition = Progress.safePosition;
        float oldYaw = Progress.safeYaw;
        Progress.safePosition = camp; Progress.safeYaw = 0;
        // Persist only the existing safe destination. No reward or consumable receipt changes.
        // As with recall, a failed save must leave both player and previous saved pose intact.
        if (!SaveProgress(false))
        {
            Progress.safePosition = oldPosition; Progress.safeYaw = oldYaw;
            world.ShowNotice(Status); return false;
        }
        Lock();
        movement.Relocate(camp, Quaternion.identity); Physics.SyncTransforms();
        movement.SetGameplayInputLocked(true); look.SetLookInputLocked(true);
        MenuOpen = MapOpen = false; confirmNew = false; unlockAfterFrame = Time.frameCount;
        Status = "Returned to the central camp. No recall beacon used.";
        world.ShowNotice(Status);
        return true;
    }

    public bool TaskRewardAvailable(int index) => Started && world && index >= 0 && index < 3 &&
        world.completed[index] && !Progress.taskRewardClaimed[index] && taskRewards != null &&
        index < taskRewards.Length && taskRewards[index] != null && taskRewards[index].IsConfigured;

    public bool ClaimTaskReward(int index)
    {
        if (!CanClaimReward() || !TaskRewardAvailable(index)) return false;
        var previous = Progress.taskRewardGrants[index];
        var receipt = taskRewards[index].Snapshot();
        Progress.taskRewardGrants[index] = receipt;
        Progress.taskRewardClaimed[index] = true;
        if (!SaveNow())
        {
            Progress.taskRewardGrants[index] = previous;
            Progress.taskRewardClaimed[index] = false;
            return false;
        }
        MenuOpen = false; confirmNew = false; unlockAfterFrame = -1; Unlock();
        Status = "Task reward collected: " + receipt.title + " x" + receipt.quantity + ".";
        string description = receipt.description + "\nReceived: " + receipt.quantity + ". Recorded in your journey menu.";
        if (!string.IsNullOrEmpty(SaveWarning)) description += "\n" + SaveWarningSummary;
        if (!rewards || !rewards.Show(receipt.title, description, false, () => { })) world.ShowNotice(Status);
        return true;
    }
    public bool UseRecallBeacon()
    {
        if (!CanClaimReward() || Progress.RecallBeaconsRemaining < 1) return false;
        var camp = CampPosition;
        if (!world.NavigationGround(camp, out var floor))
        {
            Status = "The camp arrival point is blocked. Your beacon was kept.";
            return false;
        }
        camp = new Vector3(camp.x, floor.y - .1f, camp.z);
        if (Vector3.Distance(world.player.position, camp) < 4f)
        {
            Status = "You are already at camp. Your beacon was kept.";
            return false;
        }
        var oldPosition = Progress.safePosition;
        float oldYaw = Progress.safeYaw;
        Progress.recallBeaconsUsed++;
        Progress.safePosition = camp; Progress.safeYaw = 0;
        // Commit the spend and destination together; a failed save neither consumes nor moves.
        if (!SaveProgress(false))
        {
            Progress.recallBeaconsUsed--;
            Progress.safePosition = oldPosition; Progress.safeYaw = oldYaw;
            return false;
        }
        Lock();
        movement.Relocate(camp, Quaternion.identity);
        Physics.SyncTransforms();
        movement.SetGameplayInputLocked(true); look.SetLookInputLocked(true);
        MenuOpen = false; confirmNew = false; unlockAfterFrame = Time.frameCount;
        Status = "Returned to camp. Recall beacons remaining: " + Progress.RecallBeaconsRemaining + ".";
        world.ShowNotice(Status);
        return true;
    }
    public bool ToggleEquipment(int index)
    {
        var loadout = world ? world.player.GetComponent<BaselineLoadout>() : null;
        if (!CanClaimReward() || !loadout || !loadout.OwnsEquipment(index)) return false;
        int previous = Progress.unequippedMask;
        Progress.unequippedMask ^= 1 << index;
        if (!SaveNow()) { Progress.unequippedMask = previous; return false; }
        Status = loadout.EquipmentName(index) + (loadout.IsEquipped(index) ? " equipped." : " unequipped.");
        return true;
    }
    public bool TogglePracticeSword()
    {
        if (!isActiveAndEnabled || !Started || MenuOpen || MapOpen || VictoryOpen || (BlockWorldInput && !InventoryOpen) ||
            !GameSession.Instance || !GameSession.Instance.HasSword) return false;
        int previous = Progress.unequippedMask;
        Progress.unequippedMask ^= 1 << 5;
        if (!SaveNow()) { Progress.unequippedMask = previous; return false; }
        bool equipped = (Progress.unequippedMask & (1 << 5)) == 0;
        GameSession.Instance.SetSwordEquipped(equipped);
        Status = equipped ? "Practice sword equipped." : "Practice sword unequipped.";
        return true;
    }
    void OnApplicationPause(bool pause){if(pause)SaveNow();}
    void OnApplicationQuit(){SaveNow();}
    void OnDisable()
    {
        titleEntering=false; if(titleScreen)titleScreen.Hide();
        if(Started)SaveNow();
        unlockAfterFrame=-1;
        // Never leave TimeScale zero when the host scene is suspended for another world.
        if(ownsLock)Unlock();
    }
    void OnDestroy()
    {
        if (SamBridge) Destroy(SamBridge.gameObject);
        GameSession.SwordStateChanged-=OnSwordChanged;
        if(world)world.StateChanged-=OnProgressChanged;
        if(endingRoom)endingRoom.Won-=OnWon;
        if(travel)
        {
            travel.TrialACompletionRecorded-=OnTrialACompleted;
            travel.TrialBCompletionRecorded-=OnTrialBCompleted;
        }
    }
    int menuPage;
    void OnGUI()
    {
        WayfarerUITheme.Apply();
        if(!world)return;
        if(TitleScreenVisible){DrawCloudTitle();return;}
        if (MapOpen && overview) { overview.Draw(this, world); return; }
        if(titleStyle==null)
        {
            titleStyle=new GUIStyle(GUI.skin.label){fontSize=25,wordWrap=true};
            bodyStyle=new GUIStyle(WayfarerUITheme.JournalBodyStyle){fontSize=19,wordWrap=true};
            confirmPromptStyle=new GUIStyle(bodyStyle){fontSize=19,wordWrap=true};
            confirmPromptStyle.normal.textColor=new Color32(230,224,204,255);
            WayfarerUITheme.KeepLabelColor(confirmPromptStyle);
        }
        var oldMatrix=GUI.matrix; GUI.matrix=Matrix4x4.identity;
        float uiScale=Mathf.Min(Screen.width/1000f,Screen.height/700f);
        GUI.matrix=Matrix4x4.Scale(new Vector3(uiScale,uiScale,1));
        float uiWidth=Screen.width/uiScale,uiHeight=Screen.height/uiScale;
        // The opening menu is a compact single-card layout. The in-game
        // journal keeps its separate larger canvas and page navigation.
        bool compactOpening=!Started && !VictoryOpen;
        bool drawOpening=compactOpening || openingClosing;
        float panelWidth=compactOpening?620f:760f;
        float panelHeight=drawOpening?380f:600f;
        if(openingClosing) panelWidth=620f;
        var area=new Rect((uiWidth-panelWidth)/2f,(uiHeight-panelHeight)/2f,panelWidth,panelHeight);
        if((Started && !VictoryOpen && MenuOpen) || p4Closing)
        {
            DrawP4Journal(area,uiWidth,uiHeight);
            GUI.matrix=oldMatrix;
            return;
        }
        if(drawOpening)
        {
            if(WayfarerUITheme.BeginPanel("opening-menu",area,!openingClosing && MenuOpen))
            {
                WayfarerUITheme.OpeningHeader(area,Status);
                WayfarerUITheme.BeginOpeningReveal(.13f,.12f);
                WayfarerUITheme.Footer(area,"A new place. A path of your own.");
                WayfarerUITheme.EndOpeningReveal();
                GUILayout.BeginArea(new Rect(area.x+36,area.y+172,area.width-72,area.height-218));
                GUILayout.BeginVertical();
                WayfarerUITheme.BeginOpeningReveal(.14f,.12f);
                WayfarerUITheme.Section("YOUR JOURNEY");
                WayfarerUITheme.EndOpeningReveal();
                GUILayout.Space(3);
                WayfarerUITheme.BeginOpeningReveal(.2f,.15f);
                bool continueClicked=hasSave && !confirmNew && WayfarerUITheme.Button("Continue saved journey",GUILayout.Height(46));
                WayfarerUITheme.EndOpeningReveal();
                if(continueClicked && ContinueGame())openingClosing=true;
                if(confirmNew)
                {
                    GUILayout.Label(slotExists && !hasSave?"Replace the existing unreadable save and start a new journey?":"Replace this slot and start a new journey?",confirmPromptStyle);
                    WayfarerUITheme.BeginOpeningReveal(.27f,.15f);
                    bool confirmClicked=WayfarerUITheme.Button("Confirm new game",GUILayout.Height(42));
                    bool cancelClicked=WayfarerUITheme.Button("Cancel",GUILayout.Height(38));
                    WayfarerUITheme.EndOpeningReveal();
                    if(confirmClicked){NewGame();if(Started)openingClosing=true;}
                    if(cancelClicked)confirmNew=false;
                }
                else
                {
                    WayfarerUITheme.BeginOpeningReveal(.27f,.15f);
                    bool newGameClicked=WayfarerUITheme.Button("New game",GUILayout.Height(46));
                    WayfarerUITheme.EndOpeningReveal();
                    if(newGameClicked){RefreshSlotPresence();if(slotExists)confirmNew=true;else {NewGame();if(Started)openingClosing=true;}}
                }
                GUILayout.EndVertical();
                GUILayout.EndArea();
                WayfarerUITheme.EndPanel();
            }
            else openingClosing=false;
            GUI.matrix=oldMatrix;
            return;
        }
        if(WayfarerUITheme.BeginPanel("journey-menu",area,MenuOpen || VictoryOpen))
        {
            WayfarerUITheme.Header(area,VictoryOpen?"Journey complete":Started?"Journey journal":"Lake Domain","THE WAYFARER'S JOURNAL");
            WayfarerUITheme.Footer(area,Started?"ESC  Return to world     ·     M  Map     ·     J  Tasks":"A new place. A path of your own.");
            float contentTop=area.y+116;
            float contentHeight=compactOpening?area.height-166:area.height-167;
            GUILayout.BeginArea(new Rect(area.x+28,contentTop,area.width-56,contentHeight));
            GUILayout.BeginHorizontal();
            float leftColumnWidth=compactOpening?250f:205f;
            GUILayout.BeginVertical(GUILayout.Width(leftColumnWidth));
            if(Started && !VictoryOpen)
            {
                string[] pages={"Journey","Equipment","Controls"};
                for(int p=0;p<pages.Length;p++)
                    if(WayfarerUITheme.TabButton(pages[p],menuPage==p,GUILayout.Height(42)))
                    {menuPage=p;menuScroll=Vector2.zero;}
            }
            else WayfarerUITheme.Section(VictoryOpen?"JOURNEY COMPLETE":"WELCOME");
            GUILayout.Space(compactOpening?24:20);
            WayfarerUITheme.Section("CURRENT NOTE");
            GUILayout.Label(Status,bodyStyle);
            GUILayout.EndVertical();
            // Align the right column inside the second paper page, with its
            // own inset instead of letting text/buttons sit under the spine.
            if(compactOpening) GUILayout.Space(34f);
            else GUILayout.Space(Mathf.Max(0f,(area.width-24f)*.36f+8f-205f));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            menuScroll = GUILayout.BeginScrollView(menuScroll);
            if (!string.IsNullOrEmpty(SaveWarning))
            {
                GUILayout.Label("SAVE NOTICE | " + SaveWarning, bodyStyle);
                if (GUILayout.Button("Dismiss save notice")) SaveWarning="";
                GUILayout.Space(8);
            }
            if(VictoryOpen)
            {
                GUILayout.Label(victorySaved?"Your ending is recorded. You may return to keep exploring.":"Victory reached. Saving failed; return and retry Save progress.",bodyStyle);
                if(WayfarerUITheme.Button("Return to the tower",GUILayout.Height(38)))LeaveEnding();
            }
            else
            {
                if(!Started || menuPage==0)
                {
                if(compactOpening)
                {
                    WayfarerUITheme.Section("YOUR JOURNEY");
                    GUILayout.Space(5);
                }
                else WayfarerUITheme.Section("YOUR JOURNEY");
                if(Started && WayfarerUITheme.Button("Resume",GUILayout.Height(34)))SetMenu(false);
                if(Started && Tutorial && Tutorial.Active && WayfarerUITheme.Button("Skip camp introduction",GUILayout.Height(30)))Tutorial.Skip();
                if(!Started && hasSave && WayfarerUITheme.Button("Continue saved journey",GUILayout.Height(compactOpening?44:34)))ContinueGame();
                if(confirmNew)
                {
                    GUILayout.Label(slotExists && !hasSave?"Replace the existing unreadable save and start a new journey?":"Replace this slot and start a new journey?",confirmPromptStyle);
                    if(WayfarerUITheme.Button("Confirm new game",GUILayout.Height(32)))NewGame();
                    if(WayfarerUITheme.Button("Cancel"))confirmNew=false;
                }
                else if(WayfarerUITheme.Button("New game",GUILayout.Height(compactOpening?44:34))) {RefreshSlotPresence();if(slotExists || Started)confirmNew=true;else NewGame();}
                if(Started)
                {
                    if(WayfarerUITheme.Button("Save progress",GUILayout.Height(30))) {if(SaveNow())Status="Progress saved on this device.";}
                    if (!(endingRoom && endingRoom.IsInside) && WayfarerUITheme.Button("Region map [M]", GUILayout.Height(30))) ShowMap();
                    if(endingRoom && endingRoom.IsInside && WayfarerUITheme.Button("Leave final room",GUILayout.Height(30)))LeaveEnding();
                    if(Progress.trialACompleted && !Progress.trialRewardClaimed[0] && !(endingRoom && endingRoom.IsInside) && WayfarerUITheme.Button("Claim Trial A reward",GUILayout.Height(34)))ClaimTrialReward(0);
                    GUILayout.Label("Trial A: "+(Progress.trialRewardClaimed[0]?"Reward collected":Progress.trialACompleted?"Completed":"Available"),bodyStyle);
                    if(Progress.trialBCompleted && !Progress.trialRewardClaimed[1] && !(endingRoom && endingRoom.IsInside) && WayfarerUITheme.Button("Claim Trial B reward",GUILayout.Height(34)))ClaimTrialReward(1);
                    GUILayout.Label("Trial B: "+(Progress.trialBCompleted?(Progress.trialRewardClaimed[1]?"Completed; reward collected":"Completed; reward available"):(Progress.trialRewardClaimed[1]?"Available; earlier entrance gift retained":"Available; speak to its guide")),bodyStyle);
                    GUILayout.Space(10);
                    GUILayout.Label("SUPPLIES | Recall beacons: " + Progress.RecallBeaconsRemaining, bodyStyle);
                    if (Progress.RecallBeaconsRemaining > 0 && !(endingRoom && endingRoom.IsInside) &&
                        WayfarerUITheme.Button("Use 1 beacon - return to camp", GUILayout.Height(34))) UseRecallBeacon();
                }
                }
                if(Started && menuPage==1)
                {
                    WayfarerUITheme.Section("EQUIPMENT & REWARDS");
                    var loadout = world.player.GetComponent<BaselineLoadout>();
                    if (loadout)
                    {
                        GUILayout.Space(10);
                        GUILayout.Label("EQUIPMENT | " + loadout.EffectSummary, bodyStyle);
                        GUILayout.Label("Ridge charm aids travel; other equipment strengthens your sword. Equip or remove it here.", bodyStyle);
                        for (int i = 0; i < 5; i++)
                            if (loadout.OwnsEquipment(i))
                            {
                                GUILayout.Label(loadout.EquipmentName(i) + " | " + loadout.EquipmentEffect(i), bodyStyle);
                                if (!(endingRoom && endingRoom.IsInside) && WayfarerUITheme.Button((loadout.IsEquipped(i) ? "Unequip " : "Equip ") + loadout.EquipmentName(i))) ToggleEquipment(i);
                            }
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        if (!(endingRoom && endingRoom.IsInside) && TaskRewardAvailable(i) &&
                            WayfarerUITheme.Button("Collect: " + taskRewards[i].title, GUILayout.Height(32))) ClaimTaskReward(i);
                        if (!Progress.taskRewardClaimed[i]) continue;
                        var receipt = Progress.taskRewardGrants[i];
                        GUILayout.Label(receipt != null ? "Collected: " + receipt.title + " x" + receipt.quantity : "Task " + (i + 1) + " reward previously collected.", bodyStyle);
                    }
                }
                if(Started && menuPage==2)
                {
                    WayfarerUITheme.Section("EXPLORATION GUIDE");
                    WayfarerUITheme.ControlKeys("WASD  Move     ·     Mouse  Look\nShift / RMB  Hold run     ·     Space  Jump\nE  Interact     ·     J  Tasks     ·     M  Map");
                    GUILayout.Space(16);
                    WayfarerUITheme.ControlText("Left click to attack after collecting your sword.");
                    GUILayout.Space(9);
                    WayfarerUITheme.ControlText("Swim toward shore to leave deep water.");
                    GUILayout.Space(8);
                    WayfarerUITheme.ControlText("Hold W toward a climbable wall.");
                    WayfarerUITheme.ControlText("Space releases your grip.");
                    GUILayout.Space(9);
                    WayfarerUITheme.ControlText("Tasks and the final room are optional.");
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            WayfarerUITheme.EndPanel();
        }
        else if (!string.IsNullOrEmpty(SaveWarning) && !(rewards && rewards.IsOpen))
        {
            float width = Mathf.Min(680, Screen.width - 30);
            GUI.Box(new Rect(Screen.width-width-18, 8, width, 48), "");
            GUI.Label(new Rect(Screen.width-width-6, 14, width-24, 36), SaveWarningSummary, bodyStyle);
        }
        GUI.matrix=oldMatrix;
    }

    void BeginP4Motion(int[] frames,int[] durations,int targetPage)
    {
        p4MotionFrames=frames; p4MotionDurations=durations;
        p4MotionStarted=Time.unscaledTime; p4MotionTargetPage=targetPage;
        p4Closing=frames.Length>0 && frames[0]==24;
    }

    int CurrentP4MotionFrame()
    {
        if(p4MotionFrames==null || p4MotionDurations==null) return -1;
        float elapsed=(Time.unscaledTime-p4MotionStarted)*1000f;
        for(int i=0;i<p4MotionFrames.Length;i++)
        {
            if(elapsed<p4MotionDurations[i]) return p4MotionFrames[i];
            elapsed-=p4MotionDurations[i];
        }
        bool closing=p4Closing;
        p4MotionFrames=null; p4MotionDurations=null;
        if(closing) p4Closing=false; else menuPage=p4MotionTargetPage;
        return -1;
    }

    void BeginP4PageTransition(int targetPage)
    {
        WorldControlsPanel.ShowOverview();
        if(targetPage==menuPage) return;
        p4PageFrom=menuPage;
        p4PageTo=Mathf.Clamp(targetPage,0,2);
        menuPage=p4PageTo;
        menuScroll=Vector2.zero;
        // IMGUI can process the same pointer event during multiple layout and
        // repaint passes. Ignore further hotspots until the next frame so a
        // single click can never bounce through another journal page.
        p4PageInputBlockedFrame=Time.frameCount;
    }

    Texture2D LoadP4Frame(int frame)
    {
        if(p4TransientFrame!=frame)
        {
            if(p4TransientTexture) Resources.UnloadAsset(p4TransientTexture);
            p4TransientTexture=Resources.Load<Texture2D>("P4Journal/Frames/frame_"+frame.ToString("00"));
            p4TransientFrame=frame;
        }
        return p4TransientTexture;
    }

    Texture2D LoadP4Page(int pageIndex)
    {
        pageIndex=Mathf.Clamp(pageIndex,0,2);
        if(!p4PageTextures[pageIndex])
        {
            string[] names={"Journey","EquipmentBase","Controls"};
            p4PageTextures[pageIndex]=Resources.Load<Texture2D>("P4Journal/Pages/"+names[pageIndex]);
        }
        return p4PageTextures[pageIndex];
    }

    static Rect P4Rect(Rect image,float x,float y,float w,float h)
    {
        return new Rect(image.x+image.width*x/1600f,image.y+image.height*y/1000f,
            image.width*w/1600f,image.height*h/1000f);
    }

    static Rect P4Region(Rect image,string id)
    {
        switch(id)
        {
            case "journeyTab": return P4Rect(image,200,356,470,98);
            case "equipmentTab": return P4Rect(image,200,452,470,98);
            case "controlsTab": return P4Rect(image,200,548,470,98);
            case "resume": return P4Rect(image,840,254,490,100);
            case "new": return P4Rect(image,840,348,490,90);
            case "save": return P4Rect(image,840,438,490,90);
            case "map": return P4Rect(image,840,526,490,98);
            case "trialA": return P4Rect(image,840,632,500,46);
            case "trialB": return P4Rect(image,840,676,500,46);
            case "beacons": return P4Rect(image,840,724,500,64);
            default: return new Rect();
        }
    }

    bool P4Hotspot(Rect image,string id)
    {
        if(p4HitStyle==null) p4HitStyle=new GUIStyle(GUIStyle.none);
        return GUI.Button(P4Region(image,id),GUIContent.none,p4HitStyle);
    }

    bool P4Hover(Rect image,string id) => P4Region(image,id).Contains(Event.current.mousePosition);

    void DrawP4Journal(Rect area,float uiWidth,float uiHeight)
    {
        // Use the source PNG's native 8:5 proportions; never stretch the artwork.
        float width=Mathf.Min(uiWidth-40,(uiHeight-40)*1.6f),height=width/1.6f;
        var image=new Rect((uiWidth-width)/2f,Mathf.Max(0f,(uiHeight-height)/2f-uiHeight*0.05f),width,height);
        bool wasClosing=p4Closing;
        int motionFrame=CurrentP4MotionFrame(),hoverFrame=-1;
        if(wasClosing && !p4Closing) return;
        if(p4PageFrom>=0 && Time.frameCount>p4PageInputBlockedFrame) p4PageFrom=p4PageTo=-1;
        bool pageChanging=p4PageFrom>=0;
        if(motionFrame<0 && !pageChanging && MenuOpen && !confirmNew && menuPage==0)
        {
            if(P4Hover(image,"new")) hoverFrame=12;
            else if(P4Hover(image,"save")) hoverFrame=13;
            else if(!(endingRoom && endingRoom.IsInside) && P4Hover(image,"map")) hoverFrame=14;
            else if(P4Hover(image,"resume")) hoverFrame=23;
        }
        bool closingOtherPage=p4Closing && menuPage!=0;
        Texture2D art=closingOtherPage?LoadP4Page(menuPage):motionFrame>=0?LoadP4Frame(motionFrame):
            hoverFrame>=0?LoadP4Frame(hoverFrame):LoadP4Page(menuPage);
        if(endingRoom && endingRoom.IsInside && menuPage==0 && motionFrame<0 && !pageChanging)
        {
            if(!p4BossJourneyPage) p4BossJourneyPage=Resources.Load<Texture2D>("P4Journal/Pages/BossJourney");
            if(p4BossJourneyPage) art=p4BossJourneyPage;
        }
        if(!art)art=LoadP4Page(menuPage);
        if(!art)DrawP4Fallback(image);
        if(art)
        {
            Color previous=GUI.color;
            if(closingOtherPage) GUI.color=new Color(1f,1f,1f,1f-Mathf.Clamp01((Time.unscaledTime-p4MotionStarted)/.18f));
            GUI.DrawTexture(image,art,ScaleMode.StretchToFill,true);
            GUI.color=previous;
        }
        if(menuPage==2 && (motionFrame<0 || closingOtherPage)) DrawP4DynamicControls(image);
        if (!string.IsNullOrEmpty(SaveWarning))
        {
            var warning = new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 14 };
            GUI.Box(new Rect(image.x + 20, image.yMax - 64, image.width - 40, 58), "SAVE NOTICE | " + SaveWarning, warning);
        }
        if(Started && MenuOpen && !(endingRoom && endingRoom.IsInside)) DrawP4NavigationFooter(image);
        if(endingRoom && endingRoom.IsInside) DrawP4FinalRoomPause(image);
        if(motionFrame>=0 || pageChanging || !MenuOpen || p4Closing) return;

        // The source page PNGs contain the exact selected-tab artwork. Show the
        // hovered tab from that page without changing the current right page.
        int hoveredTab=P4Hover(image,"journeyTab")?0:
            P4Hover(image,"equipmentTab")?1:
            P4Hover(image,"controlsTab")?2:-1;
        if(hoveredTab>=0 && hoveredTab!=menuPage && !confirmNew)
        {
            Texture2D hoveredPage=LoadP4Page(hoveredTab);
            if(hoveredPage)
            {
                Rect nav=P4Rect(image,190,346,510,308);
                var uv=new Rect(190f/1600f,1f-654f/1000f,510f/1600f,308f/1000f);
                GUI.DrawTextureWithTexCoords(nav,hoveredPage,uv,true);
            }
        }

        if(P4Hotspot(image,"journeyTab") && menuPage!=0)
            BeginP4PageTransition(0);
        else if(P4Hotspot(image,"equipmentTab") && menuPage!=1)
            BeginP4PageTransition(1);
        else if(P4Hotspot(image,"controlsTab") && menuPage!=2)
            BeginP4PageTransition(2);

        if(menuPage==0)
        {
            if(!confirmNew)
            {
                if(P4Hotspot(image,"resume")) SetMenu(false);
                else if(P4Hotspot(image,"new"))
                {
                    RefreshSlotPresence();
                    if(slotExists || Started) confirmNew=true; else NewGame();
                }
                else if(P4Hotspot(image,"save") && SaveNow()) Status="Progress saved on this device.";
                else if(!(endingRoom && endingRoom.IsInside) && P4Hotspot(image,"map")) ShowMap();
                else if(P4Hotspot(image,"trialA")) ClaimTrialReward(0);
                else if(P4Hotspot(image,"trialB")) ClaimTrialReward(1);
                else if(P4Hotspot(image,"beacons") && Progress.RecallBeaconsRemaining>0) UseRecallBeacon();
            }
            DrawP4Confirm(image);
        }
        else if(menuPage==1)
        {
            if(p4CollectedRewards)
            {
                var link=new GUIStyle(GUIStyle.none){fontSize=13,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
                link.normal.textColor=new Color32(43,88,72,255);
                GUI.Label(P4Rect(image,850,207,500,40),"COLLECTED REWARDS",new GUIStyle(link){fontSize=20});
                GUI.Label(P4Rect(image,850,260,540,30),"Equip or remove the rewards you have found.",link);
                GUI.Label(P4Rect(image,850,765,300,36),"←  Back to equipment",link);
                if(GUI.Button(P4Rect(image,850,765,300,36),GUIContent.none,GUIStyle.none)) p4CollectedRewards=false;
                DrawP4EquipmentActions(image);
            }
            else DrawP4EquipmentPage(image,art);
            // Always last: the bookmark belongs above page ink and hover cards.
            if(!p4BookmarkTop) p4BookmarkTop=Resources.Load<Texture2D>("P4Journal/BookmarkTop");
            if(p4BookmarkTop) GUI.DrawTexture(P4Rect(image,1324,112,55,177),p4BookmarkTop,ScaleMode.StretchToFill,true);
        }
    }

    void DrawP4Fallback(Rect image)
    {
        GUI.Box(image,GUIContent.none);WayfarerUITheme.DrawJournalPages(image,.5f);
        var ink=new GUIStyle(WayfarerUITheme.JournalBodyStyle){fontSize=22,fontStyle=FontStyle.Bold};ink.normal.textColor=WayfarerUITheme.Ink;WayfarerUITheme.KeepLabelColor(ink);
        GUI.Label(P4Rect(image,210,195,510,80),"THE WAYFARER'S JOURNAL",ink);
        GUI.Label(P4Region(image,"journeyTab"),"Journey",ink);GUI.Label(P4Region(image,"equipmentTab"),"Equipment",ink);GUI.Label(P4Region(image,"controlsTab"),"Controls",ink);
        if(menuPage==0){GUI.Label(P4Region(image,"resume"),"Resume",ink);GUI.Label(P4Region(image,"new"),"New game",ink);GUI.Label(P4Region(image,"save"),"Save progress",ink);GUI.Label(P4Region(image,"map"),"Region map [M]",ink);}
    }
    GUIStyle p4NavigationHint;
    void DrawP4NavigationFooter(Rect image)
    {
        // Keep the reusable page artwork; replace its baked shortcut wording.
        Color previous=GUI.color;
        GUI.color=new Color32(43,88,72,255);
        GUI.DrawTexture(P4Rect(image,237,904,1160,53),Texture2D.whiteTexture);
        GUI.color=previous;
        if(p4NavigationHint==null)
        {
            p4NavigationHint=new GUIStyle(GUI.skin.label){fontSize=20,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
            p4NavigationHint.normal.textColor=WayfarerUITheme.Paper;
            WayfarerUITheme.KeepLabelColor(p4NavigationHint);
        }
        GUI.Label(P4Rect(image,262,912,1080,36),confirmNew?"Confirm a new journey or keep your current save":"ESC  Return to world     ·     M  Map     ·     J  Tasks",p4NavigationHint);
    }

    void DrawP4FinalRoomPause(Rect image)
    {
        var footer=P4Rect(image,237,904,1160,53);
        Color previous=GUI.color;
        GUI.color=new Color32(35,77,65,255);
        GUI.DrawTexture(footer,Texture2D.whiteTexture);
        GUI.color=previous;
        var hint=new GUIStyle(GUI.skin.label){fontSize=20,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
        hint.normal.textColor=WayfarerUITheme.Paper;
        WayfarerUITheme.KeepLabelColor(hint);
        GUI.Label(P4Rect(image,262,912,760,36),"ESC  Return to duel",hint);
    }

    void DrawP4Confirm(Rect image)
    {
        if(!confirmNew) return;
        Rect box=P4Rect(image,820,265,570,320);
        // A dark inset reads as a modal above the cream paper instead of
        // disappearing into it. The gold outline matches the journal frame.
        Rect shadow=new Rect(box.x+6,box.y+7,box.width,box.height);
        GUI.color=new Color(0.03f,0.10f,0.08f,.34f);
        GUI.DrawTexture(shadow,Texture2D.whiteTexture);
        GUI.color=new Color32(27,67,57,252);
        GUI.DrawTexture(box,Texture2D.whiteTexture);
        GUI.color=new Color32(190,157,88,255);
        const float border=2f;
        GUI.DrawTexture(new Rect(box.x,box.y,box.width,border),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.x,box.yMax-border,box.width,border),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.x,box.y,border,box.height),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.xMax-border,box.y,border,box.height),Texture2D.whiteTexture);
        GUI.color=Color.white;
        var heading=new GUIStyle(titleStyle){fontSize=24,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
        heading.normal.textColor=new Color32(248,238,208,255);
        WayfarerUITheme.KeepLabelColor(heading);
        GUI.Label(P4Rect(image,860,275,500,48),"NEW GAME",heading);
        string prompt=slotExists && !hasSave?"Replace the existing unreadable save and start a new journey?":"Replace this slot and start a new journey?";
        var modalPrompt=new GUIStyle(confirmPromptStyle){wordWrap=true};
        modalPrompt.normal.textColor=new Color32(239,229,201,255);
        WayfarerUITheme.KeepLabelColor(modalPrompt);
        GUI.Label(P4Rect(image,860,325,500,80),prompt,modalPrompt);
        if(GUI.Button(P4Rect(image,870,420,450,52),"Confirm new game")) NewGame();
        if(GUI.Button(P4Rect(image,870,480,450,52),"Cancel")) confirmNew=false;
    }

    void DrawP4EquipmentPage(Rect image,Texture2D art)
    {
        // EquipmentBase already has a continuous, text-free right paper leaf.
        // Draw only ink, separators and icons here; no rectangular page overlay.
        var heading=new GUIStyle(GUIStyle.none){fontSize=18,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
        var name=new GUIStyle(GUIStyle.none){fontSize=17,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
        var small=new GUIStyle(GUIStyle.none){fontSize=13,alignment=TextAnchor.MiddleLeft};
        var tiny=new GUIStyle(GUIStyle.none){fontSize=11,alignment=TextAnchor.MiddleLeft};
        var status=new GUIStyle(tiny){alignment=TextAnchor.MiddleRight,fontStyle=FontStyle.Bold};
        heading.normal.textColor=name.normal.textColor=new Color32(30,73,62,255);
        small.normal.textColor=new Color32(84,113,95,255);
        tiny.normal.textColor=status.normal.textColor=new Color32(115,133,110,255);
        GUI.Label(P4Rect(image,850,208,455,42),"EQUIPMENT & REWARDS",heading);
        GUI.color=new Color32(200,183,146,255);
        GUI.DrawTexture(P4Rect(image,850,254,540,2),Texture2D.whiteTexture);
        GUI.color=Color.white;
        GUI.Label(P4Rect(image,850,272,540,31),"A record of your gear and where to find it.",small);
        bool sword=GameSession.Instance && GameSession.Instance.HasSword;
        var loadout=world.player.GetComponent<BaselineLoadout>();
        string[] titles={"Practice sword","Island relic","Ridge charm","Runic guard"};
        string[] effects={"Basic attack weapon",
            loadout?"Sword damage +"+loadout.EquipmentBonus(0):"Sword damage +2",
            "Sprint stamina -30% (main world)",
            loadout?"Sword damage +"+loadout.EquipmentBonus(2):"Sword damage +4"};
        string[] origins={"Given by the Guide","Island hidden chest","Western hidden chest","Sealed eastern chest"};
        string[] descriptions={"A dependable blade for the first steps of your journey.",
            loadout?loadout.EquipmentDescription(0):"An island-forged relic that strengthens every sword strike.",
            loadout?loadout.EquipmentDescription(1):"A mountain charm carried down from the western ridge.",
            loadout?loadout.EquipmentDescription(2):"An ancient guard etched with a protective rune."};
        if(p4FinalEquipmentIcons==null)
            p4FinalEquipmentIcons=new[]{
                Resources.Load<Texture2D>("P3Rewards/PracticeSwordIcon"),
                Resources.Load<Texture2D>("P3Rewards/IslandRelicIconV6"),
                Resources.Load<Texture2D>("P3Rewards/RidgeCharmIconV6"),
                Resources.Load<Texture2D>("P3Rewards/RunicGuardIconV6"),
                Resources.Load<Texture2D>("FinalEquipment/HealthPendant"),
                Resources.Load<Texture2D>("FinalEquipment/Sword")};
        int hovered=-1;
        int readyCount=0;
        for(int i=0;i<4;i++)
        {
            float y=314+i*88;
            Rect row=P4Rect(image,850,y,540,79);
            bool owned=i==0?sword:loadout && loadout.OwnsEquipment(i-1);
            bool equipped=owned && (i==0?GameSession.Instance.SwordEquipped:loadout.IsEquipped(i-1));
            if(owned)readyCount++;
            if(row.Contains(Event.current.mousePosition)) hovered=i;
            if(owned || hovered==i)
            {
                GUI.color=owned?new Color32(176,143,85,255):new Color32(147,159,135,255);
                GUI.DrawTexture(P4Rect(image,850,y+2,3,64),Texture2D.whiteTexture);
            }
            GUI.color=new Color32(200,183,146,255);
            GUI.DrawTexture(P4Rect(image,850,y+78,540,1),Texture2D.whiteTexture);
            GUI.color=Color.white;
            // Only unlocked gear floats; placeholders remain grounded and quiet.
            float floatOffset=owned?Mathf.Sin(Time.unscaledTime*2.1f)*1.5f:0f;
            Rect icon=P4Rect(image,866,y+10+floatOffset,56,56);
            Texture2D itemIcon=p4FinalEquipmentIcons[i];
            if(itemIcon)
            {
                GUI.color=owned?Color.white:new Color(1f,1f,1f,.42f);
                GUI.DrawTexture(icon,itemIcon,ScaleMode.ScaleToFit,true);
                GUI.color=Color.white;
            }
            else
            {
                var diamond=new GUIStyle(GUIStyle.none){fontSize=24,alignment=TextAnchor.MiddleCenter};
                diamond.normal.textColor=new Color32(132,151,132,255);
                GUI.Label(icon,"◇",diamond);
            }
            GUI.Label(P4Rect(image,940,y+1,300,31),titles[i],name);
            GUI.Label(P4Rect(image,1250,y+5,140,26),owned?(equipped?"EQUIPPED":"UNEQUIPPED"):"NOT FOUND",status);
            GUI.Label(P4Rect(image,940,y+30,400,24),effects[i],small);
            GUI.Label(P4Rect(image,940,y+54,400,21),origins[i],tiny);
        }
        if(hovered<0 && p4HoveredGear>=0)
        {
            float stickyY=397+p4HoveredGear*88;
            if(P4Rect(image,1040,stickyY,350,150).Contains(Event.current.mousePosition)) hovered=p4HoveredGear;
        }
        if(hovered!=p4HoveredGear){p4HoveredGear=hovered;p4GearHoverStarted=Time.unscaledTime;}
        GUI.Label(P4Rect(image,850,688,280,25),"ALSO TO DISCOVER",tiny);
        GUI.Label(P4Rect(image,1240,688,150,25),readyCount+" / 4 READY",status);
        Texture2D healthPendant=p4FinalEquipmentIcons[4];
        Texture2D separateSword=p4FinalEquipmentIcons[5];
        GUI.color=new Color(1f,1f,1f,.48f);
        if(healthPendant)GUI.DrawTexture(P4Rect(image,850,714,30,30),healthPendant,ScaleMode.ScaleToFit,true);
        if(separateSword)GUI.DrawTexture(P4Rect(image,1120,714,30,30),separateSword,ScaleMode.ScaleToFit,true);
        GUI.color=Color.white;
        GUI.Label(P4Rect(image,887,719,225,25),"Max-health pendant",small);
        GUI.Label(P4Rect(image,1157,719,215,25),"Separate sword",small);
        var link=new GUIStyle(tiny){alignment=TextAnchor.MiddleRight,fontStyle=FontStyle.Bold};
        GUI.Label(P4Rect(image,1110,766,285,35),"Collected rewards  →",link);
        if(GUI.Button(P4Rect(image,1110,766,285,35),GUIContent.none,GUIStyle.none))p4CollectedRewards=true;
        if(hovered>=0)
        {
            float appear=Mathf.Clamp01((Time.unscaledTime-p4GearHoverStarted)/.17f);
            float lift=(1-appear)*10f;
            float tx=1040;
            var popupTitle=new GUIStyle(name){wordWrap=true,clipping=TextClipping.Clip};
            var popupBody=new GUIStyle(small){wordWrap=true,clipping=TextClipping.Clip};
            var popupNote=new GUIStyle(tiny){wordWrap=true,clipping=TextClipping.Clip};
            float innerWidth=310;
            float titleH=Mathf.Max(25,popupTitle.CalcHeight(new GUIContent(titles[hovered]),innerWidth));
            float descH=Mathf.Max(35,popupBody.CalcHeight(new GUIContent(descriptions[hovered]),innerWidth));
            float effectH=Mathf.Max(20,popupBody.CalcHeight(new GUIContent("Effect: "+effects[hovered]),innerWidth));
            float sourceH=Mathf.Max(20,popupBody.CalcHeight(new GUIContent("Source: "+origins[hovered]),innerWidth));
            float popupHeight=20+titleH+descH+effectH+sourceH+25;
            float ty=Mathf.Min(397+hovered*88-lift,760-popupHeight);
            Rect shadow=P4Rect(image,tx+6,ty+6,350,popupHeight);
            GUI.color=new Color(0.06f,0.14f,0.12f,.36f*appear);
            GUI.DrawTexture(shadow,Texture2D.whiteTexture);
            GUI.color=new Color(0.13f,0.31f,0.27f,.98f*appear);
            Rect popup=P4Rect(image,tx,ty,350,popupHeight);
            GUI.DrawTexture(popup,Texture2D.whiteTexture);
            GUI.color=new Color(0.78f,0.67f,0.43f,appear);
            GUI.DrawTexture(new Rect(popup.x,popup.y,popup.width,1.5f),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(popup.x,popup.y+popup.height-1.5f,popup.width,1.5f),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(popup.x,popup.y,1.5f,popup.height),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(popup.x+popup.width-1.5f,popup.y,1.5f,popup.height),Texture2D.whiteTexture);
            GUI.color=Color.white;
            popupTitle.normal.textColor=new Color32(250,237,203,255);
            popupBody.normal.textColor=new Color32(221,234,218,255);
            popupNote.normal.textColor=new Color32(221,193,126,255);
            float lineY=ty+12;
            GUI.Label(P4Rect(image,tx+18,lineY,innerWidth,titleH),titles[hovered],popupTitle); lineY+=titleH+3;
            GUI.Label(P4Rect(image,tx+18,lineY,innerWidth,descH),descriptions[hovered],popupBody); lineY+=descH+3;
            GUI.Label(P4Rect(image,tx+18,lineY,innerWidth,effectH),"Effect: "+effects[hovered],popupBody); lineY+=effectH+3;
            GUI.Label(P4Rect(image,tx+18,lineY,innerWidth,sourceH),"Source: "+origins[hovered],popupBody); lineY+=sourceH+3;
            bool hoveredOwned=hovered==0?sword:loadout && loadout.OwnsEquipment(hovered-1);
            bool hoveredEquipped=hoveredOwned && (hovered==0?GameSession.Instance.SwordEquipped:loadout.IsEquipped(hovered-1));
            GUI.Label(P4Rect(image,tx+18,lineY,innerWidth,18),hoveredOwned?(hoveredEquipped?"Currently equipped":"Currently unequipped"):"Not yet obtained",popupNote);
        }
    }

    void DrawP4EquipmentActions(Rect image)
    {
        var loadout=world.player.GetComponent<BaselineLoadout>();
        if(!loadout) return;
        float y=330;
        for(int i=0;i<5;i++)
        {
            if(!loadout.OwnsEquipment(i)) continue;
            Rect row=P4Rect(image,845,y,520,48);
            var gearStyle=new GUIStyle(bodyStyle){fontSize=Mathf.Max(11,Mathf.RoundToInt(18*image.width/1600f)),wordWrap=false};
            string effect=i==1?"Sprint stamina -30%":"Sword damage +"+loadout.EquipmentBonus(i);
            GUI.Label(new Rect(row.x,row.y,row.width*.64f,row.height*.5f),loadout.EquipmentName(i),gearStyle);
            GUI.Label(new Rect(row.x,row.y+row.height*.5f,row.width*.64f,row.height*.5f),effect,gearStyle);
            if(GUI.Button(new Rect(row.x+row.width*.66f,row.y,row.width*.34f,row.height),loadout.IsEquipped(i)?"Unequip":"Equip")) ToggleEquipment(i);
            y+=54;
        }
        for(int i=0;i<3;i++)
        {
            if(!TaskRewardAvailable(i)) continue;
            if(GUI.Button(P4Rect(image,845,y,520,48),"Collect: "+taskRewards[i].title)) ClaimTaskReward(i);
            y+=54;
        }
    }
}
