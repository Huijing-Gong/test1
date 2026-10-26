using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Scene-scoped UGUI: small illustrated icons and shared paper grain, real text
// and buttons. Selection/results share lightweight scenery and paper skins.
[DisallowMultipleComponent]
public sealed partial class BattleTrialMenu : MonoBehaviour
{
    public Texture2D deviceBackground;
    public Texture2D resultHero;
    public TMP_FontAsset headingFont;
    public Texture2D[] trialIcons;
    public Texture2D rewardIcon;
    public Texture2D trialIllustration;
    public Texture2D paperGrain;
    public Sprite parchmentFace;
    public Sprite paintedBacking;
    public Sprite paintedGoldButton;
    public enum View { None, Selection, Controls, Pause, Result, QuickGuide }
    public View CurrentView { get; private set; }
    public int SelectedTrial { get; private set; }
    public Button StartButton { get; private set; }
    public Button ClaimButton { get; private set; }
    public Button ResumeButton { get; private set; }
    public Button PracticeButton { get; private set; }
    public bool Visible => CurrentView != View.None;
    static bool replayRequested;
    static readonly string[] Names = { "Sunlit Ruins", "Twilight Ruins", "Eclipse Cathedral" };
    static readonly string[] Difficulties = { "BEGINNER", "INTERMEDIATE", "ADVANCED" };
    static readonly Color Ink = new Color32(23, 58, 50, 255);
    // The paper bitmap already supplies the warm cream tint, so don't tint twice.
    static readonly Color Cream = Color.white;
    static readonly Color Muted = new Color32(72, 99, 85, 255);
    static readonly Color Gold = new Color32(255, 208, 102, 255);
    static readonly Color Cyan = new Color32(130, 207, 197, 255);
    static readonly Color Bronze = new Color32(153, 117, 42, 255);
    BattleManager manager;
    BattleArenaStages stages;
    RectTransform canvasRoot, content;
    GameObject backdrop, shade;
    TMP_FontAsset font;
    CanvasGroup hud;
    float oldHudAlpha;
    bool oldHudInteractable, oldHudRaycasts;
    View controlsReturn;
    Button[] trialButtons;
    RawImage[] badges;
    RectTransform[] trialRows;
    RectTransform illustrationRect;
    RawImage rewardPicture;
    RawImage futureBadge;
    Image[] selectedStrips;
    BattleTrialOrnament[] selectedArrows;
    BattleTrialOrnament startArrow;
    BattleTrialPaperGraphic[] stateChips;
    TMP_Text[] rowNames;
    TMP_Text unavailableArt;
    CanvasGroup entrance;
    BattleTrialMenuMotion motion;
    float openedAt;
    TMP_Text[] trialStates;
    TMP_Text selectedName, description, difficulty, rewardName, rewardEffect, rewardState, notice;
    float refreshAt;
    bool resultReceipt;
    bool starting;
    int viewChangedFrame = -1;
    int quickGuideInputFrame = -1;
    public bool QuickGuideConsumesInput => IsQuickGuideOpen || quickGuideInputFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => replayRequested = false;
    public static void QueueReplay() => replayRequested = true;
    public static void ClearReplay() => replayRequested = false;
    public static bool ConsumeReplayRequest()
    { bool value = replayRequested; replayRequested = false; return value; }

    public void Bind(BattleManager owner)
    {
        if (manager) return;
        manager = owner;
        stages = owner.GetComponent<BattleArenaStages>();
        SelectedTrial = stages ? stages.ActiveIndex : 0;
        font = owner.battleTimerText ? owner.battleTimerText.font : TMP_Settings.defaultFontAsset;
        var hudObject = GameObject.Find("BattleHUD");
        if (hudObject)
        {
            hud = hudObject.GetComponent<CanvasGroup>();
            if (!hud) hud = hudObject.AddComponent<CanvasGroup>();
            oldHudAlpha = hud.alpha; oldHudInteractable = hud.interactable; oldHudRaycasts = hud.blocksRaycasts;
        }
        var root = new GameObject("Side A Trial Menus", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        canvasRoot = root.GetComponent<RectTransform>();
        backdrop = new GameObject("Device background", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        backdrop.transform.SetParent(canvasRoot, false);
        var backgroundRect = backdrop.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero; backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
        var raw = backdrop.GetComponent<RawImage>(); raw.texture = deviceBackground; raw.raycastTarget = false;
        backdrop.AddComponent<Canvas>(); // Moving scenery does not dirty static typography.
        var fitter = backdrop.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = deviceBackground ? (float)deviceBackground.width / deviceBackground.height : 16f / 9f;
        var shadeImage = Panel("Shade", canvasRoot, 0, 0, 1920, 1080, new Color(0, .025f, .025f, .76f));
        shade = shadeImage.gameObject;
        shadeImage.rectTransform.anchorMin = Vector2.zero; shadeImage.rectTransform.anchorMax = Vector2.one;
        shadeImage.rectTransform.offsetMin = shadeImage.rectTransform.offsetMax = Vector2.zero;
        root.SetActive(false);
    }

    void LateUpdate()
    {
        if (!Visible || !canvasRoot || !content) return;
        // Fit the same 16:9 layout inside narrower/taller PC windows. The still
        // image or dimmer independently fills the screen; controls never crop.
        Vector2 available = canvasRoot.rect.size;
        float fit = Mathf.Min(available.x / 1920f, available.y / 1080f);
        content.localScale = Vector3.one * fit;
        content.anchoredPosition = new Vector2((available.x - 1920 * fit) * .5f, -(available.y - 1080 * fit) * .5f);
        float age = Time.unscaledTime - openedAt;
        if (entrance) entrance.alpha = Mathf.Clamp01(age / .12f);
        if (IsQuickGuideOpen) TickQuickGuide();
        if (CurrentView != View.Selection && CurrentView != View.Result) return;
        // The still background moves by only a few pixels, independently of
        // text. Overscan prevents exposing black edges at any supported ratio.
        var backgroundRect = backdrop.GetComponent<RectTransform>();
        backgroundRect.localScale = Vector3.one * 1.025f;
        backgroundRect.anchoredPosition = new Vector2(Mathf.Sin(Time.unscaledTime * .13f) * 5,
            Mathf.Cos(Time.unscaledTime * .11f) * 4);
        if (trialRows != null)
        {
            for (int i = 0; i < trialRows.Length; i++)
            {
                bool selected = i == SelectedTrial;
                bool hover = trialButtons[i].GetComponent<BattleTrialButtonMotion>().Hovered;
                motion.Destination(trialRows[i], new Vector2(selected ? 88 : hover ? 83 : 78, -(348 + i * 188)));
                badges[i].rectTransform.localScale = Vector3.Lerp(badges[i].rectTransform.localScale,
                    Vector3.one * (selected ? 1.04f : hover ? 1.02f : 1f), 1f - Mathf.Exp(-16 * Time.unscaledDeltaTime));
                selectedStrips[i].canvasRenderer.SetAlpha(.83f + Mathf.Sin(Time.unscaledTime * 2f) * .17f);
            }
        }
    }

    void Update()
    {
        if (SideALoadingScreen.IsVisible) return;
        if (!manager || !Visible) return;
        var keys = Keyboard.current;
        bool busy = SideWorldTravel.Instance && SideWorldTravel.Instance.Busy;
        if (!busy && keys != null && Time.frameCount > viewChangedFrame)
        {
            if (IsQuickGuideOpen)
            {
                quickGuideInputFrame = Time.frameCount;
                if (keys.escapeKey.wasPressedThisFrame || keys.hKey.wasPressedThisFrame) CloseQuickGuide();
                else if (keys.leftArrowKey.wasPressedThisFrame) PreviousQuickGuide();
                else if (keys.rightArrowKey.wasPressedThisFrame || keys.enterKey.wasPressedThisFrame) NextQuickGuide();
            }
            else if (CurrentView == View.Selection)
            {
                if (keys.downArrowKey.wasPressedThisFrame || keys.sKey.wasPressedThisFrame)
                    SelectTrial((SelectedTrial + 1) % 3);
                else if (keys.upArrowKey.wasPressedThisFrame || keys.wKey.wasPressedThisFrame)
                    SelectTrial((SelectedTrial + 2) % 3);
                else if (keys.pKey.wasPressedThisFrame) StartPractice();
                else if (keys.enterKey.wasPressedThisFrame) StartSelected();
                else if (keys.escapeKey.wasPressedThisFrame) ReturnToMain();
            }
        }
        if (Time.unscaledTime >= refreshAt)
        {
            refreshAt = Time.unscaledTime + .2f;
            if (CurrentView == View.Selection) RefreshSelection();
            else if (CurrentView == View.Result) RefreshResult();
        }
    }

    void Clear(View next, bool background)
    {
        if (next != View.QuickGuide && QuickGuidePreview) QuickGuidePreview.Hide();
        CurrentView = next;
        viewChangedFrame = Time.frameCount;
        if (content)
        {
            // Retire old hit targets before Destroy's end-of-frame cleanup.
            content.gameObject.SetActive(false); Destroy(content.gameObject);
        }
        canvasRoot.gameObject.SetActive(true);
        content = Rect("Content", canvasRoot, 0, 0, 1920, 1080);
        entrance = content.gameObject.AddComponent<CanvasGroup>();
        entrance.alpha = 0; openedAt = Time.unscaledTime;
        motion = content.gameObject.AddComponent<BattleTrialMenuMotion>();
        trialRows = null; illustrationRect = null;
        backdrop.SetActive(background); shade.SetActive(!background);
        if (hud) { hud.alpha = 0; hud.interactable = false; hud.blocksRaycasts = false; }
        StartButton = ClaimButton = ResumeButton = PracticeButton = null;
        notice = null;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    public void Hide()
    {
        if (QuickGuidePreview) QuickGuidePreview.Hide();
        CurrentView = View.None;
        if (canvasRoot) canvasRoot.gameObject.SetActive(false);
        if (hud) { hud.alpha = oldHudAlpha; hud.interactable = oldHudInteractable; hud.blocksRaycasts = oldHudRaycasts; }
    }

    public void OpenSelection()
    {
        starting = false;
        Clear(View.Selection, true);
        Panel("Background soft wash", content, 0, 0, 1920, 1080, new Color(.78f, .86f, .79f, .12f));
        var atmosphere = Rect("Quiet leaves and dust", content, 0, 0, 1920, 1080).gameObject.AddComponent<BattleTrialMenuAtmosphere>();
        atmosphere.raycastTarget = false;
        atmosphere.gameObject.AddComponent<Canvas>();
        var back = Button("Back", content, "<  MAIN WORLD [RMB]", 98, 27, 312, 66, ReturnToMain, false);
        motion.Reveal(back.GetComponent<RectTransform>(), 0, new Vector2(0, 9), .23f);
        var header = Card("Trial heading paper", content, 104, 118, 770, 195, Cream, false);
        motion.Reveal(header, .015f, new Vector2(-16, 0), .30f);
        var title = Text("Title", header, "RUINS TRIALS", 72, 25, 670, 90, 78, Ink);
        title.fontStyle = FontStyles.Bold;
        Ornament("Title rule", header, BattleTrialOrnament.Motif.Rule, 60, 128, 644, 10, Bronze);
        var subtitle = Text("Subtitle", header, "CHOOSE YOUR CHALLENGE", 62, 150, 642, 35, 25, Muted);
        subtitle.fontStyle = FontStyles.Bold;
        subtitle.characterSpacing = 2;
        trialButtons = new Button[3]; badges = new RawImage[3]; trialStates = new TMP_Text[3];
        trialRows = new RectTransform[3]; rowNames = new TMP_Text[3];
        selectedStrips = new Image[3]; stateChips = new BattleTrialPaperGraphic[3]; selectedArrows = new BattleTrialOrnament[3];
        for (int i = 0; i < 3; i++)
        {
            int index = i; float y = 348 + i * 188;
            var row = Rect("Trial " + (i + 1), content, 78, y, 952, 170);
            trialRows[i] = row;
            motion.Reveal(row, .045f + i * .045f, new Vector2(-25, 0), .30f);
            Backing("Painted card backing", row, -10, -7, 975, 190);
            var button = Button("Select", row, "", 0, 0, 952, 170, () => SelectTrial(index), false);
            trialButtons[i] = button;
            selectedStrips[i] = Panel("Selected aqua strip", row, 9, 12, 8, 146, Cyan);
            Heading("Number", row, (i + 1).ToString("00"), 35, 48, 64, 61, 43, Ink);
            Panel("Divider", row, 115, 26, 1, 115, Bronze);
            Ornament("Number diamond", row, BattleTrialOrnament.Motif.HollowDiamond, 109, 80, 13, 13, Bronze);
            badges[i] = Picture("Trial badge", row, trialIcons != null && trialIcons.Length > i ? trialIcons[i] : null, 137, 23, 130, 130);
            // Preserve the badge's visible bounds while scaling about its centre.
            badges[i].rectTransform.pivot = new Vector2(.5f, .5f);
            badges[i].rectTransform.anchoredPosition += new Vector2(65, -65);
            badges[i].gameObject.AddComponent<Canvas>();
            rowNames[i] = Heading("Name", row, Names[i], 292, 26, 628, 66, 44, Ink);
            Paper("Difficulty chip", row, 293, 103, 238, 40, new Color32(194, 219, 200, 255), false, 0);
            Text("Difficulty", row, Difficulties[i], 303, 108, 218, 31, 20, Ink, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            stateChips[i] = Paper("State chip", row, 624, 103, 226, 40, new Color32(229, 220, 196, 255), false, 0);
            trialStates[i] = Text("State", row, "", 631, 108, 212, 31, 19, Muted, TextAlignmentOptions.Center);
            trialStates[i].fontStyle = FontStyles.Bold;
            selectedArrows[i] = Ornament("Selected arrow", row, BattleTrialOrnament.Motif.Chevron, 870, 66, 25, 33, Ink);
        }
        var detail = Card("Selected trial paper", content, 1068, 87, 790, 926, Cream);
        motion.Reveal(detail, .085f, new Vector2(23, 0), .34f);
        Paper("Trial title tab", detail, 153, 44, 484, 87, Ink, false, 2).outline = Bronze;
        selectedName = Heading("Selected title", detail, "", 165, 51, 460, 78, 47, Cream, TextAlignmentOptions.Center);
        selectedName.enableAutoSizing = true; selectedName.fontSizeMin = 25; selectedName.fontSizeMax = 47;
        selectedName.textWrappingMode = TextWrappingModes.NoWrap;
        Paper("Difficulty chip", detail, 292, 139, 206, 40, new Color32(194, 219, 200, 255), false, 0);
        difficulty = Text("Objective", detail, "", 298, 144, 194, 30, 20, Ink, TextAlignmentOptions.Center);
        difficulty.fontStyle = FontStyles.Bold;
        var illustrationGroup = Rect("Trial illustration", detail, 91, 170, 608, 304);
        var halo = Ornament("Quiet illustration halo", illustrationGroup, BattleTrialOrnament.Motif.Orbit, 167, 37, 274, 232,
            new Color(.4f, .53f, .39f, .18f));
        motion.Float(halo.rectTransform, Vector2.zero, .65f, .035f, 5);
        Picture("Illustration art", illustrationGroup, trialIllustration, 0, 0, 608, 304);
        Ornament("Illustration top diamond", illustrationGroup, BattleTrialOrnament.Motif.HollowDiamond, 297, 10, 14, 14, Bronze);
        illustrationRect = illustrationGroup;
        illustrationRect.gameObject.AddComponent<Canvas>();
        motion.Float(illustrationRect, new Vector2(0, 4), 1.15f);
        futureBadge = Picture("Future trial badge", detail, null, 315, 230, 160, 160);
        futureBadge.gameObject.AddComponent<Canvas>();
        motion.Float(futureBadge.rectTransform, new Vector2(0, 3), 1.1f, .015f);
        unavailableArt = Text("Future arena note", detail, "ARENA IN DEVELOPMENT", 160, 418, 470, 39, 24, Muted, TextAlignmentOptions.Center);
        Ornament("Objective rule", detail, BattleTrialOrnament.Motif.Rule, 113, 473, 564, 11, Bronze);
        description = Text("Description", detail, "", 70, 503, 650, 59, 25, Ink, TextAlignmentOptions.Center);
        var reward = Rect("First clear reward", detail, 76, 552, 638, 214);
        Paper("Reward inset", reward, 0, 0, 638, 214, new Color32(253, 245, 219, 255), false, 1).outline = new Color(.4f, .5f, .37f, .4f);
        Panel("Reward header strip", reward, 5, 5, 628, 42, new Color32(192, 219, 203, 255));
        Text("Heading", reward, "FIRST CLEAR REWARD", 26, 13, 584, 30, 20, Ink).fontStyle = FontStyles.Bold;
        Paper("Icon tile", reward, 32, 63, 133, 129, Cream, false, 3).outline = Bronze;
        rewardPicture = Picture("Reward icon", reward, rewardIcon, 37, 68, 123, 119);
        rewardPicture.gameObject.AddComponent<Canvas>();
        motion.Float(rewardPicture.rectTransform, Vector2.zero, 1.5f, .018f);
        rewardName = Heading("Reward name", reward, "", 195, 67, 411, 55, 36, Ink);
        rewardEffect = Text("Reward effect", reward, "", 197, 119, 409, 40, 21, Muted);
        Paper("Reward state chip", reward, 197, 160, 249, 32, new Color32(221, 215, 197, 255), false, 0);
        rewardState = Text("Reward state", reward, "", 204, 165, 235, 27, 17, Muted);
        rewardState.fontStyle = FontStyles.Bold;
        PracticeButton = Button("Interactive practice", detail, "INTERACTIVE PRACTICE  [P]", 76, 778, 638, 54, StartPractice, false);
        PracticeButton.GetComponent<BattleTrialPaperGraphic>().outline = Cyan;
        PracticeButton.GetComponent<BattleTrialPaperGraphic>().border = 2;
        StartButton = Button("Start", detail, "START TRIAL", 76, 847, 638, 66, StartSelected, true);
        startArrow = Ornament("Start arrow", StartButton.transform, BattleTrialOrnament.Motif.Chevron, 466, 20, 20, 26, Ink);
        foreach (var p in new[] { new Vector2(34, 31), new Vector2(85, 83), new Vector2(33, 850), new Vector2(747, 856) })
            Ornament("Paper corner diamond", detail, BattleTrialOrnament.Motif.HollowDiamond, p.x, p.y, 13, 13, Bronze);
        Ornament("Reward left diamond", reward, BattleTrialOrnament.Motif.HollowDiamond, -5, 112, 12, 12, Bronze);
        Ornament("Reward right diamond", reward, BattleTrialOrnament.Motif.HollowDiamond, 631, 112, 12, 12, Bronze);
        var help = Button("Help", content, "?  CONTROLS", 96, 996, 237, 59, manager.OpenTrialControls, false);
        motion.Reveal(help.GetComponent<RectTransform>(), .12f, new Vector2(0, -8), .23f);
        var footer = Rect("Footer", content, 355, 996, 672, 59);
        Panel("Footer contrast", footer, 0, 0, 672, 59, new Color(.04f, .13f, .10f, .9f));
        Text("Reward rule", footer, "Replay freely. Each trial reward can be claimed once.", 14, 17, 644, 34, 20, Cream);
        motion.Reveal(footer, .12f, new Vector2(0, -8), .23f);
        notice = Text("Notice", content, "", 98, 953, 935, 35, 18, Ink);
        motion.Detail(selectedName.rectTransform);
        motion.Detail(difficulty.rectTransform);
        motion.Detail(illustrationRect, false);
        motion.Detail(futureBadge.rectTransform, false);
        motion.Detail(unavailableArt.rectTransform);
        motion.Detail(description.rectTransform);
        motion.Detail(reward);
        RefreshSelection();
    }

    public void SelectTrial(int index)
    {
        if (CurrentView != View.Selection || index < 0 || index >= 3) return;
        bool changed = index != SelectedTrial;
        SelectedTrial = index; RefreshSelection();
        if (changed && motion) motion.DetailChanged();
    }

    BaselineFlow Host => SideWorldTravel.Instance ? SideWorldTravel.Instance.HostFlow : null;
    bool HasHost => Host && Host.Started;
    bool Claimed => HasHost && Host.Progress.trialRewardClaimed[0];
    bool Completed => HasHost ? Host.Progress.trialACompleted : SideWorldTravel.Instance && SideWorldTravel.Instance.TrialACompleted;
    bool Busy => SideWorldTravel.Instance && SideWorldTravel.Instance.Busy;
    bool Available(int index) => stages ? stages.IsAvailable(index) : index == 0;
    bool PrimaryReward => manager.CurrentTrialIndex == 0;

    void RefreshSelection()
    {
        if (CurrentView != View.Selection || trialStates == null) return;
        for (int i = 0; i < 3; i++)
        {
            var colors = trialButtons[i].colors;
            colors.normalColor = i == SelectedTrial ? Gold : Cream;
            colors.highlightedColor = i == SelectedTrial ? new Color32(255, 225, 138, 255) : new Color32(252, 241, 201, 255);
            colors.selectedColor = colors.normalColor; trialButtons[i].colors = colors;
            badges[i].color = i == 0 || i == SelectedTrial ? Color.white : new Color(.75f, .81f, .77f, .72f);
            selectedStrips[i].gameObject.SetActive(i == SelectedTrial);
            selectedArrows[i].gameObject.SetActive(i == SelectedTrial);
            bool available = Available(i);
            stateChips[i].color = available ? Ink : new Color32(221, 215, 197, 255);
            trialStates[i].color = available ? Cream : Muted;
            trialStates[i].text = i == 0 ? (Claimed ? "COLLECTED" : Completed ? "REWARD READY" : "READY") : available ? "READY / PRACTICE" : "COMING SOON";
        }
        bool ready = Available(SelectedTrial);
        selectedName.text = Names[SelectedTrial].ToUpperInvariant();
        description.text = ready ? "Knock your rival off the stage twice to win.\nHigher damage % means farther knockback."
            : "This arena is still in development.";
        difficulty.text = Difficulties[SelectedTrial];
        illustrationRect.gameObject.SetActive(ready);
        var illustration = illustrationRect.GetComponentInChildren<RawImage>(true);
        if (illustration) illustration.texture = SelectedTrial == 2 && stages ? stages.EclipseBackground : trialIllustration;
        futureBadge.gameObject.SetActive(!ready);
        futureBadge.texture = trialIcons[SelectedTrial];
        unavailableArt.gameObject.SetActive(!ready);
        rewardName.text = SelectedTrial == 0 ? "Trial A blade core" : "Reward planned";
        rewardEffect.text = SelectedTrial == 0 ? (HasHost ? Host.SideATrialRewardEffect : "Preview: rewards are not saved.") : "Practice stage. Reward not yet assigned.";
        rewardState.text = SelectedTrial != 0 ? "NOT YET ASSIGNED" : Claimed ? "ALREADY COLLECTED" : Completed ? "CLAIM IN MAIN WORLD" : "NOT COLLECTED";
        rewardPicture.color = SelectedTrial == 0 ? Color.white : new Color(1, 1, 1, .2f);
        StartButton.interactable = ready && !starting && !Busy;
        if (PracticeButton) PracticeButton.interactable = ready && !starting && !Busy;
        var startText = StartButton.GetComponentInChildren<TMP_Text>();
        startText.text = ready ? "START TRIAL" : "COMING SOON";
        startArrow.gameObject.SetActive(ready);
        startText.color = ready ? Ink : Muted;
        if (notice && string.IsNullOrEmpty(notice.text)) notice.text = "W / S or Up / Down: choose  |  Enter: trial  |  P: practice  |  Esc: return";
    }

    public void StartSelected()
    {
        if (CurrentView != View.Selection || !Available(SelectedTrial) || starting || Busy || Time.frameCount <= viewChangedFrame) return;
        starting = true; manager.BeginSelectedTrial();
    }

    public void StartPractice()
    {
        if (CurrentView != View.Selection || !Available(SelectedTrial) || starting || Busy || Time.frameCount <= viewChangedFrame) return;
        starting = true; manager.BeginSelectedTutorial();
    }

    public void OpenControls()
    {
        if (CurrentView != View.Controls) controlsReturn = CurrentView;
        Clear(View.Controls, false);
        var panel = Card("Controls", content, 470, 65, 980, 950, Cream);
        Heading("Heading", panel, "CONTROLS", 50, 38, 880, 68, 46, Ink);
        Text("Basics", panel, "GOAL: KNOCK YOUR RIVAL OFF THE STAGE\nDamage % is NOT health: higher % means farther knockback.\nEach fighter has 2 lives. First ring-out: respawn at 0%.\nSecond ring-out: defeat. You are blue.", 50, 120, 880, 134, 26, Muted);
        Text("Keys", panel,
            "A / D   Move          Space   Jump (again in the air)\n\n" +
            "LEFT CLICK / J   Four-hit combo (A / D allowed)\nW + LEFT / J   Uppercut    S + LEFT / J   Sweep\n\n" +
            "RIGHT CLICK   Tap / hold, then release. Full charge auto-fires\nA / D + RIGHT   Dash kick    W + RIGHT   Air leap (once per airtime)\nS + RIGHT   Counter: ground only, blocks attacks from the front\n\n" +
            "S + Space   Drop through upper platforms\nH   Controls / pause     Esc   Pause     RMB   Back in menus     J / K   Backup attacks\nMouse position does not aim. Hold direction before clicking.",
            50, 267, 880, 400, 23, Ink);
        Button("Reduced motion",panel,"SHAKE: "+(BattlePresentationSettings.ReducedMotion?"LOW":"NORMAL"),50,681,280,43,
            ()=>{BattlePresentationSettings.ReducedMotion=!BattlePresentationSettings.ReducedMotion;OpenControls();},false);
        Button("Hit flash",panel,"HIT FLASH: "+(BattlePresentationSettings.FlashEnabled?"ON":"OFF"),350,681,280,43,
            ()=>{BattlePresentationSettings.FlashEnabled=!BattlePresentationSettings.FlashEnabled;OpenControls();},false);
        Button("Context tips",panel,"TIPS: "+(BattlePresentationSettings.ContextHintsEnabled?"ON":"OFF"),650,681,280,43,
            ()=>{BattlePresentationSettings.ContextHintsEnabled=!BattlePresentationSettings.ContextHintsEnabled;OpenControls();},false);
        Button("Move cards", panel, "MOVE CARDS", 50, 743, 280, 54, OpenMoveCards, false);
        Button("Quick guide", panel, "QUICK GUIDE", 350, 743, 280, 54, () => OpenQuickGuide(), false);
        Button("Practice", panel, manager.IsTutorialActive ? "RETRY THIS LESSON" : "LIVE PRACTICE", 650, 743, 280, 54,
            manager.BeginTutorialPractice, false);
        Text("Practice note", panel, manager.IsTutorialActive ? "Free retries. Practice never grants trial rewards."
            : "Live practice leaves this duel. No trial rewards in practice.", 50, 809, 880, 35, 21, Muted, TextAlignmentOptions.Center);
        Button("Close", panel, "BACK [RMB]", 50, 860, 880, 58, manager.CloseTrialControls, true);
    }

    public void OpenMoveCards()
    {
        // These are input/reference cards, not invented animation previews.
        // The adjacent live-practice action always uses the actual game actors.
        Clear(View.Controls, false);
        var panel = Card("Move cards", content, 328, 100, 1264, 880, Cream);
        Heading("Heading", panel, "MOVE CARDS", 50, 31, 1164, 65, 45, Ink);
        Text("Subtitle", panel, "Learn the inputs here. Try the real moves in interactive practice.",
            50, 102, 1164, 42, 24, Muted);
        string[] titles = { "FOUR-HIT COMBO", "ENERGY BEAM", "AFTERIMAGE DASH", "AIR RECOVERY", "LOW SWEEP", "FRONT COUNTER" };
        string[] keys = { "LEFT CLICK / J  x4", "RIGHT CLICK / K", "A or D + RIGHT / K", "W + RIGHT / K", "S + LEFT / J", "S + RIGHT / K" };
        string[] notes = {
            "Release W / S; A / D can stay held. Land each punch and click again to continue. Hit four lands hardest.",
            "No direction held. Tap for a quick beam; hold to build charge, then release. Full charge fires automatically.",
            "Hold the direction before clicking. Dash through the gap with real character afterimages.",
            "Use the second jump and air leap to return. One air leap per airtime; land to refresh. Being below an edge is not the end.",
            "A grounded, low strike. Move close enough for the actual sweep to connect.",
            "Ground only. Face the attack and time the brief counter window; it does not block attacks from behind."
        };
        for (int i = 0; i < titles.Length; i++)
        {
            float x = 50 + i % 3 * 394, y = 167 + i / 3 * 240;
            var card = Paper("Move " + (i + 1), panel, x, y, 376, 219, new Color32(232, 234, 211, 255), false, 1.5f);
            Panel("Accent", card.transform, 13, 15, 5, 188, i == 2 || i == 3 ? Cyan : Gold);
            Text("Name", card.transform, titles[i], 29, 17, 327, 35, 24, Ink).fontStyle = FontStyles.Bold;
            Paper("Key strip", card.transform, 29, 63, 318, 39, Ink, false, 0);
            Text("Inputs", card.transform, keys[i], 35, 68, 306, 29, 20, Cream, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            var note = Text("How it works", card.transform, notes[i], 29, 116, 318, 91, 20, Muted);
            note.enableAutoSizing = true; note.fontSizeMin = 17; note.fontSizeMax = 20;
        }
        Button("Live practice", panel, manager.IsTutorialActive ? "RETRY THIS LESSON" : "TRY LIVE PRACTICE", 50, 674, 1164, 65,
            manager.BeginTutorialPractice, true);
        Text("Practice warning", panel, manager.IsTutorialActive ? "Free retries. No trial rewards."
            : "This leaves the current duel and opens free, no-reward practice.", 50, 747, 1164, 32, 21, Muted, TextAlignmentOptions.Center);
        Button("Back to controls", panel, "BACK TO CONTROLS", 50, 797, 1164, 52, OpenControls, false);
    }

    public void CloseControls()
    {
        if (controlsReturn == View.Selection) OpenSelection();
        else if (controlsReturn == View.Pause) OpenPause();
        else Hide();
    }

    public void OpenPause()
    {
        Clear(View.Pause, false);
        var panel = Card("Pause", content, 645, 191, 630, 698, Cream);
        Heading("Heading", panel, manager.IsTutorialActive ? "PRACTICE PAUSED" : "PAUSED", 50, 38, 530, 75, 43, Ink, TextAlignmentOptions.Center);
        ResumeButton = Button("Resume", panel, "CONTINUE  [ESC / RMB]", 50, 143, 530, 76, manager.ResumeTrialPause, true);
        Button("Help", panel, "HELP / CONTROLS  [H]", 50, 235, 530, 65, manager.OpenTrialControls, false);
        Button("Practice", panel, manager.IsTutorialActive ? "RETRY THIS LESSON" : "INTERACTIVE PRACTICE", 50, 315, 530, 65,
            manager.BeginTutorialPractice, false);
        Button("Select", panel, "TRIAL SELECTION", 50, 395, 530, 65, manager.ReturnToTrialSelection, false);
        Button("Return", panel, "MAIN WORLD  ["+WorldInputBindings.Label("ReturnWorld")+"]", 50, 475, 530, 65, ReturnToMain, false);
        Text("Note", panel, manager.IsTutorialActive ? "Free retries. No trial rewards in practice."
            : "Practice or selection leaves this duel.\nPractice does not grant trial rewards.", 50, 570, 530, 75, 21, Muted, TextAlignmentOptions.Center);
    }

    public void OpenResult()
    {
        Clear(View.Result, true);
        resultReceipt = false;
        BuildIllustratedResult();
        RefreshResult();
    }

    void RefreshResult()
    {
        RefreshIllustratedResult();
    }

    public bool ClaimRewardNow()
    {
        if (CurrentView != View.Result || !PrimaryReward || Busy || !HasHost || !manager.PlayerHasWon || Claimed) return false;
        bool granted = Host.ClaimSideATrialReward(manager);
        resultReceipt = true;
        notice.text = granted ? "SAVED. The blade core is now owned in the main world."
            : "SAVE NOT CONFIRMED. No reward was granted. Please retry.";
        RefreshResult();
        return granted;
    }
    void ClaimReward() => ClaimRewardNow();

    public void ReturnToMain()
    {
        if (Busy) return;
        var travel = SideWorldTravel.Instance;
        if (travel && travel.IsAway) { ClearReplay(); travel.ReturnToMain(); return; }
        if (notice) notice.text = "Standalone preview. Enter Side A from the main world to return there.";
    }

    static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1);
        r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
        return r;
    }
    static Image Panel(string name, Transform parent, float x, float y, float w, float h, Color color)
    {
        var r = Rect(name, parent, x, y, w, h); var image = r.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false; return image;
    }
    BattleTrialPaperGraphic Paper(string name, Transform parent, float x, float y, float w, float h,
        Color color, bool torn = true, float border = 5)
    {
        var paper = Rect(name, parent, x, y, w, h).gameObject.AddComponent<BattleTrialPaperGraphic>();
        paper.grain = paperGrain; paper.torn = torn; paper.border = border;
        if (torn && parchmentFace)
        {
            paper.sprite = parchmentFace; paper.type = Image.Type.Sliced;
            paper.pixelsPerUnitMultiplier = .9f;
        }
        paper.color = color; paper.raycastTarget = false;
        return paper;
    }
    Image Backing(string name, Transform parent, float x, float y, float w, float h)
    {
        var r = Rect(name, parent, x, y, w, h);
        var image = r.gameObject.AddComponent<Image>();
        image.sprite = paintedBacking; image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = .9f;
        image.color = paintedBacking ? Color.white : Ink; image.raycastTarget = false;
        return image;
    }
    RectTransform Card(string name, Transform parent, float x, float y, float w, float h, Color color, bool backed = true)
    {
        var card = Rect(name, parent, x, y, w, h);
        if (backed) Backing("Painted paper backing", card, -12, -13, w + 29, h + 32);
        Paper("Paper surface", card, 0, 0, w, h, color);
        if (CurrentView != View.Selection && motion) motion.Reveal(card, 0, new Vector2(0, -15), .24f);
        return card;
    }
    static RawImage Picture(string name, Transform parent, Texture2D texture, float x, float y, float w, float h)
    {
        var picture = Rect(name, parent, x, y, w, h).gameObject.AddComponent<RawImage>();
        picture.texture = texture; picture.raycastTarget = false;
        return picture;
    }
    static BattleTrialOrnament Ornament(string name, Transform parent, BattleTrialOrnament.Motif motif,
        float x, float y, float w, float h, Color color)
    {
        var graphic = Rect(name, parent, x, y, w, h).gameObject.AddComponent<BattleTrialOrnament>();
        graphic.shape = motif; graphic.color = color; graphic.raycastTarget = false;
        return graphic;
    }
    TMP_Text Heading(string name, Transform parent, string value, float x, float y, float w, float h, float size,
        Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var text = Text(name, parent, value, x, y, w, h, size, color, align);
        if (headingFont) text.font = headingFont;
        return text;
    }
    TMP_Text Text(string name, Transform parent, string value, float x, float y, float w, float h, float size,
        Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var r = Rect(name, parent, x, y, w, h); var text = r.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.color = color;
        text.alignment = align; text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
        return text;
    }
    Button Button(string name, Transform parent, string value, float x, float y, float w, float h,
        UnityEngine.Events.UnityAction action, bool primary)
    {
        var image = Paper(name, parent, x, y, w, h, Color.white, string.IsNullOrEmpty(value));
        image.raycastTarget = true; image.insetRim = primary;
        bool painted = primary && paintedGoldButton;
        if (painted) { image.sprite = paintedGoldButton; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 1; }
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = painted ? Color.white : primary ? Gold : Ink;
        colors.highlightedColor = painted ? new Color(1, 1, .94f, 1) : primary ? new Color32(255, 225, 138, 255) : new Color32(45, 83, 67, 255);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = painted ? new Color(.88f, .85f, .75f, 1) : primary ? new Color32(205, 158, 51, 255) : new Color32(17, 50, 44, 255);
        // Set colors immediately: creating buttons on a paused selection screen
        // must not flash default white before their first tint tween completes.
        colors.disabledColor = new Color32(195, 191, 164, 255); colors.fadeDuration = 0f;
        button.colors = colors; button.onClick.AddListener(SideAAudio.Interface); button.onClick.AddListener(action);
        if (!string.IsNullOrEmpty(value))
        {
            Text("Label", image.transform, value, 18, 5, name == "Start" ? w - 74 : w - 36, h - 10, primary ? 40 : 26,
                primary ? Ink : Cream, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
        }
        var buttonMotion = image.gameObject.AddComponent<BattleTrialButtonMotion>();
        if (primary) buttonMotion.AddSheen();
        return button;
    }
}
