using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Five concise, visual-only control cards. The normal battle remains owned by
// BattleManager's selection/help/pause state; this view never changes time scale.
public sealed partial class BattleTrialMenu
{
    static readonly string[] QuickGuideTitles = {
        "MOVE & JUMP", "J COMBO", "DIRECTION + J", "TAP / HOLD K", "DIRECTION + K"
    };
    static readonly string[] QuickGuideInstructions = {
        "A / D move; press Space to jump, then again for a second jump.\nW / S modify attacks; S + Space drops through upper platforms.",
        "J combos work standing still or while holding A / D; keep W / S released.\nConnect each punch and press J again for the four-hit heavy finisher.",
        "A / D + J keeps the four-hit combo; W + J uppercuts and S + J sweeps.\nEach combo punch must connect before the next hit can continue.",
        "Release directions: tap K for a quick beam, or hold to charge.\nRelease early to fire; full charge fires automatically.",
        "A / D + K: dash kick; W + K: rise once per airtime.\nS + K: grounded front counter; landing refreshes the rise."
    };
    static readonly string[] QuickGuideKeyNames = { "W", "A", "S", "D", "SPACE", "J", "K" };
    static readonly BattleQuickGuidePreview.DemoKeys[] QuickGuideKeyFlags = {
        BattleQuickGuidePreview.DemoKeys.W, BattleQuickGuidePreview.DemoKeys.A,
        BattleQuickGuidePreview.DemoKeys.S, BattleQuickGuidePreview.DemoKeys.D,
        BattleQuickGuidePreview.DemoKeys.Space, BattleQuickGuidePreview.DemoKeys.J,
        BattleQuickGuidePreview.DemoKeys.K
    };
    View quickGuideReturn, quickGuideSavedControlsReturn;
    bool quickGuideEntry;
    BattleQuickGuidePreview quickGuidePreview;
    RawImage quickGuideImage;
    TMP_Text quickGuideAction;
    RectTransform quickGuideProgress;
    BattleTrialPaperGraphic[] quickGuideCaps;
    TMP_Text[] quickGuideCapLabels;
    BattleQuickGuidePreview.DemoKeys quickGuideDisplayedKeys = (BattleQuickGuidePreview.DemoKeys)(-1);
    public bool IsQuickGuideOpen => CurrentView == View.QuickGuide;
    public int QuickGuidePage { get; private set; }
    public BattleQuickGuidePreview QuickGuidePreview => quickGuidePreview;

    public void OpenQuickGuide(bool entry = false)
    {
        // Scene Start can run before additive travel clears Busy. The internal
        // first-entry card may build then; manual navigation still waits.
        if (!manager || !canvasRoot || (!entry && Busy)) return;
        if (!IsQuickGuideOpen)
        {
            quickGuideEntry = entry;
            quickGuideReturn = entry ? View.Selection : CurrentView;
            quickGuideSavedControlsReturn = controlsReturn;
            QuickGuidePage = 0;
        }
        BuildQuickGuidePage();
    }

    void BuildQuickGuidePage()
    {
        // Clear retires old targets before constructing this page. Background
        // choice matches the originating view without exposing the host world.
        Clear(View.QuickGuide, quickGuideEntry || quickGuideReturn == View.Selection);
        var panel = Card("Quick control guide", content, 410, 52, 1100, 976, Cream);
        Heading("Quick guide title", panel, (QuickGuidePage + 1).ToString("00") + "  " + QuickGuideTitles[QuickGuidePage],
            51, 28, 998, 65, 46, Ink, TextAlignmentOptions.Center);
        Text("Quick guide count", panel, "QUICK CONTROLS  /  " + (QuickGuidePage + 1) + " OF 5",
            51, 96, 998, 31, 20, Muted, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;

        // The texture is an isolated live visual demonstration, not footage
        // from the active battle and never an AI-created gameplay replacement.
        Paper("Demo frame", panel, 69, 134, 962, 544, Ink, false, 3).outline = Cyan;
        quickGuideImage = Picture("Looping real-avatar demonstration", panel, null, 72, 137, 956, 538);
        quickGuideImage.color = Ink;
        Panel("Demo label contrast", panel, 89, 153, 309, 33, new Color(.03f, .12f, .10f, .94f));
        Text("Demo source label", panel, "VISUAL DEMONSTRATION", 96, 157, 295, 25, 18, Cream, TextAlignmentOptions.Center)
            .fontStyle = FontStyles.Bold;
        Panel("Demo action contrast", panel, 72, 624, 956, 51, new Color(.03f, .12f, .10f, .90f));
        quickGuideAction = Text("Looping demo action", panel, "Preparing demonstration...", 94, 636, 912, 31, 23, Cream,
            TextAlignmentOptions.Center);
        quickGuideAction.enableAutoSizing = true;
        quickGuideAction.fontSizeMin = 17; quickGuideAction.fontSizeMax = 23;
        Panel("Demo phase track", panel, 72, 670, 956, 5, new Color(.16f, .28f, .23f, 1));
        quickGuideProgress = Panel("Demo phase", panel, 72, 670, 956, 5, Cyan).rectTransform;
        quickGuideProgress.pivot = new Vector2(0, 1);
        quickGuideProgress.localScale = new Vector3(0, 1, 1);

        quickGuideCaps = new BattleTrialPaperGraphic[7]; quickGuideCapLabels = new TMP_Text[7];
        float x = 250;
        for (int i = 0; i < 7; i++)
        {
            float width = i < 4 ? 58 : i == 4 ? 144 : 76;
            var cap = Paper("Quick guide key " + QuickGuideKeyNames[i], panel, x, 693, width, 53, Cream, false, 1.5f);
            cap.outline = Bronze; quickGuideCaps[i] = cap;
            quickGuideCapLabels[i] = Text("Key name", cap.transform, QuickGuideKeyNames[i], 3, 7, width - 6, 39, i < 4 ? 29 : 24,
                Ink, TextAlignmentOptions.Center);
            quickGuideCapLabels[i].fontStyle = FontStyles.Bold;
            x += width + 12;
        }
        var instructionText = Text("Two-sentence lesson", panel, QuickGuideInstructions[QuickGuidePage],
            66, 762, 968, 76, 25, Ink, TextAlignmentOptions.Center);
        instructionText.enableAutoSizing = true; instructionText.fontSizeMin = 20; instructionText.fontSizeMax = 25;

        var previous = Button("Previous quick lesson", panel, "PREVIOUS", 50, 853, 230, 59, PreviousQuickGuide, false);
        previous.interactable = QuickGuidePage > 0;
        Button("Skip quick guide", panel, "SKIP GUIDE", 300, 853, 220, 59, CloseQuickGuide, false);
        bool last = QuickGuidePage == QuickGuideTitles.Length - 1;
        string forward = !last ? "NEXT LESSON" : quickGuideEntry || quickGuideReturn == View.Selection
            ? "CONTINUE TO TRIAL SELECTION" : quickGuideReturn == View.Pause ? "BACK TO PAUSE" : "BACK TO CONTROLS";
        var next = Button("Next quick lesson", panel, forward, 540, 853, 510, 59, NextQuickGuide, true);
        var nextLabel = next.GetComponentInChildren<TMP_Text>();
        nextLabel.enableAutoSizing = true; nextLabel.fontSizeMin = 20; nextLabel.fontSizeMax = 30;
        Text("Mouse alternatives", panel, "J = LEFT CLICK     K = RIGHT CLICK     Hold direction before attacking.",
            50, 921, 1000, 25, 19, Muted, TextAlignmentOptions.Center);
        Text("Paused demo note", panel, "WORLD PAUSED  /  ONLY THE DEMONSTRATION MOVES",
            50, 947, 1000, 22, 17, Muted, TextAlignmentOptions.Center);

        if (!quickGuidePreview)
        {
            quickGuidePreview = manager.GetComponent<BattleQuickGuidePreview>();
            if (!quickGuidePreview) quickGuidePreview = manager.gameObject.AddComponent<BattleQuickGuidePreview>();
        }
        var fighter = FindFirstObjectByType<BattlePlayerController>();
        var source = fighter ? fighter.GetComponentInChildren<Animator>(true) : null;
        quickGuidePreview.Show(source, QuickGuidePage);
        quickGuideDisplayedKeys = (BattleQuickGuidePreview.DemoKeys)(-1);
        TickQuickGuide();
    }

    public void NextQuickGuide()
    {
        if (!IsQuickGuideOpen || Busy) return;
        if (QuickGuidePage >= QuickGuideTitles.Length - 1) { CloseQuickGuide(); return; }
        QuickGuidePage++; BuildQuickGuidePage();
    }

    public void PreviousQuickGuide()
    {
        if (!IsQuickGuideOpen || Busy || QuickGuidePage <= 0) return;
        QuickGuidePage--; BuildQuickGuidePage();
    }

    public void CloseQuickGuide()
    {
        if (!IsQuickGuideOpen || Busy) return;
        if (quickGuidePreview) quickGuidePreview.Hide();
        if (quickGuideEntry || quickGuideReturn == View.Selection) OpenSelection();
        else if (quickGuideReturn == View.Pause) OpenPause();
        else
        {
            OpenControls();
            // OpenControls normally captures its current view; the nested
            // demonstration must preserve the original selection/pause stack.
            controlsReturn = quickGuideSavedControlsReturn;
        }
    }

    internal void TickQuickGuide()
    {
        if (!IsQuickGuideOpen || !quickGuideImage || !quickGuidePreview) return;
        var texture = quickGuidePreview.Texture;
        if (quickGuideImage.texture != texture) quickGuideImage.texture = texture;
        quickGuideImage.color = texture ? Color.white : Ink;
        string action = quickGuidePreview.Ready ? quickGuidePreview.ActionLabel :
            string.IsNullOrEmpty(quickGuidePreview.Error) ? "Preparing demonstration..." : "Preview unavailable. The control instructions still apply.";
        if (quickGuideAction.text != action) quickGuideAction.text = action;
        float phase = Mathf.Clamp01(quickGuidePreview.Progress);
        quickGuideProgress.localScale = new Vector3(phase, 1, 1);
        var pressed = quickGuidePreview.Keys;
        if (quickGuideDisplayedKeys == pressed) return;
        quickGuideDisplayedKeys = pressed;
        for (int i = 0; i < quickGuideCaps.Length; i++)
        {
            bool relevant = QuickGuidePage == 0 ? i <= 4 : QuickGuidePage == 1 ? i == 5 :
                QuickGuidePage == 2 ? i <= 3 || i == 5 : QuickGuidePage == 3 ? i == 6 : i <= 3 || i == 6;
            bool down = relevant && (pressed & QuickGuideKeyFlags[i]) != 0;
            quickGuideCaps[i].color = down ? Gold : relevant ? Cream : new Color32(218, 221, 201, 255);
            quickGuideCaps[i].outline = down ? Ink : relevant ? Bronze : new Color32(169, 184, 165, 255);
            quickGuideCapLabels[i].color = relevant ? Ink : new Color32(134, 153, 138, 255);
        }
    }
}
