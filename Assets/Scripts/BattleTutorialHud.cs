using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Non-modal, scene-scoped practice overlay. Only real buttons receive raycasts;
/// combat input can continue through every piece of decorative UI.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(230)]
public sealed class BattleTutorialHud : MonoBehaviour
{
    static readonly Color Ink = new Color32(19, 55, 49, 255);
    static readonly Color Cream = new Color32(250, 240, 211, 255);
    static readonly Color Mint = new Color32(135, 216, 199, 255);
    static readonly Color Gold = new Color32(245, 198, 89, 255);
    static readonly Color Quiet = new Color32(186, 212, 196, 255);

    BattleManager manager;
    BattleTutorial tutorial;
    Canvas canvas;
    RectTransform canvasRect, frame, objective, chargeFill;
    CanvasGroup visibility;
    TMP_FontAsset font;
    TMP_Text title, instruction, detail, keys, progress, stepLabel, stageNumber, primaryLabel, retryLabel, skipLabel;
    Image progressFill;
    Button retryButton, skipButton, duelButton;
    GameObject chargeTrack, targetRoot, reviewRoot;
    LineRenderer targetRing, targetPointer;
    Material targetMaterial;
    int displayedStep = -1, displayedStepCount = -1;
    bool displayedComplete, wasVisible;
    float stepChangedAt;
    Vector3? previousTarget;
    int layoutWidth, layoutHeight;
    Rect layoutSafeArea;
    Vector2 layoutCanvasSize;

    public void Bind(BattleManager owner, BattleTutorial lesson)
    {
        if (manager || !owner || !lesson) return;
        manager = owner; tutorial = lesson;
        font = owner.battleTimerText ? owner.battleTimerText.font : TMP_Settings.defaultFontAsset;
        var go = new GameObject("Side A Interactive Practice HUD", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Always below selection, pause and help (1000), above the combat HUD.
        canvas.sortingOrder = 900;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        canvasRect = go.GetComponent<RectTransform>();
        visibility = go.GetComponent<CanvasGroup>();
        frame = Rect("Safe 16 by 9 practice frame", canvasRect, 0, 0, 1920, 1080);
        BuildObjective(); BuildActions(); BuildReviewPicker(); BuildWorldTarget();
        SetVisible(false);
    }

    void BuildObjective()
    {
        objective = Rect("Current objective", frame, 460, 121, 1000, 172);
        Surface("Objective face", objective, 0, 0, 1000, 172, new Color(.055f, .17f, .14f, .95f), Mint, 1.5f);
        Surface("Number shield", objective, 20, 20, 64, 64, Ink, Gold, 1.5f);
        stageNumber = Text("Objective number", objective, "01", 23, 27, 58, 45, 30, Gold, TextAlignmentOptions.Center);
        stageNumber.fontStyle = FontStyles.Bold;
        title = Text("Title", objective, "INTERACTIVE PRACTICE", 105, 16, 688, 34, 27, Cream);
        title.fontStyle = FontStyles.Bold;
        Surface("Lesson count", objective, 816, 17, 162, 32, Ink, new Color(.51f, .73f, .62f, .6f), 1);
        stepLabel = Text("Step count", objective, "STEP 1 / 7", 821, 20, 152, 26, 18, Quiet, TextAlignmentOptions.Center);
        instruction = Text("Objective instruction", objective, "", 105, 55, 865, 49, 29, Cream);
        instruction.enableAutoSizing = true; instruction.fontSizeMin = 19; instruction.fontSizeMax = 29;
        detail = Text("Technique detail", objective, "", 105, 111, 865, 44, 20, Quiet);
        detail.enableAutoSizing = true; detail.fontSizeMin = 17; detail.fontSizeMax = 20;
        var rail = Surface("Overall progress rail", objective, 22, 153, 63, 4, new Color(.21f, .34f, .28f, 1), Ink, 0);
        progressFill = Surface("Overall progress", rail.transform, 0, 0, 63, 4, Gold, Gold, 0);
        progressFill.rectTransform.pivot = new Vector2(0, 1);
        var accent = Rect("Quiet objective ornament", objective, 40, 101, 24, 24).gameObject.AddComponent<BattleTrialOrnament>();
        accent.shape = BattleTrialOrnament.Motif.HollowDiamond; accent.color = Mint; accent.raycastTarget = false;
    }

    void BuildActions()
    {
        // Keep extra clearance above the independently anchored H / CONTROLS
        // pill, including ultrawide windows where this 16:9 frame is inset.
        var action = Rect("Practice controls", frame, 540, 760, 840, 173);
        Surface("Practice controls face", action, 0, 0, 840, 173, new Color(.055f, .17f, .14f, .96f), Quiet, 1);
        keys = Text("Real controls", action, "", 24, 12, 792, 38, 27, Cream, TextAlignmentOptions.Center);
        keys.fontStyle = FontStyles.Bold;
        keys.enableAutoSizing = true; keys.fontSizeMin = 18; keys.fontSizeMax = 27;
        progress = Text("Live objective progress", action, "", 24, 54, 792, 33, 21, Mint, TextAlignmentOptions.Center);
        chargeTrack = Rect("Charge indicator", action, 120, 92, 600, 6).gameObject;
        Surface("Charge rail", chargeTrack.transform, 0, 0, 600, 6, new Color(.21f, .34f, .28f, 1), Ink, 0);
        chargeFill = Surface("Actual charge", chargeTrack.transform, 0, 0, 600, 6, Mint, Mint, 0).rectTransform;
        chargeFill.pivot = new Vector2(0, 1);
        retryButton = Button("Retry lesson", action, "RETRY STEP", 23, 113, 173, 42,
            () => { CloseReviewPicker(); tutorial.RetryStep(); }, false);
        skipButton = Button("Skip objective", action, "SKIP LESSON", 207, 113, 193, 42,
            () => { if (tutorial.IsComplete) ToggleReviewPicker(); else tutorial.SkipStep(); }, false);
        duelButton = Button("Start normal trial", action, "START TRIAL", 414, 113, 403, 42, () => tutorial.StartDuel(), true);
        primaryLabel = duelButton.GetComponentInChildren<TMP_Text>();
        retryLabel = retryButton.GetComponentInChildren<TMP_Text>();
        skipLabel = skipButton.GetComponentInChildren<TMP_Text>();
    }

    void BuildReviewPicker()
    {
        // Optional completion-only picker. It does not exist as an invisible
        // full-screen hit target, and future lessons cannot be selected early.
        reviewRoot = Rect("Completed lesson review picker", frame, 540, 600, 840, 148).gameObject;
        Surface("Review picker face", reviewRoot.transform, 0, 0, 840, 148,
            new Color(.055f, .17f, .14f, .98f), Mint, 1.5f);
        Text("Review heading", reviewRoot.transform, "REVIEW A LESSON", 23, 12, 635, 29, 21, Cream).fontStyle = FontStyles.Bold;
        Button("Close review picker", reviewRoot.transform, "CLOSE", 724, 10, 93, 30, CloseReviewPicker, false);
        string[] labels = { "01  MOVE", "02  JUMP", "03  FIRST HIT", "04  COMBO", "05  ENERGY", "06  RETURN", "07  LIVES" };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button("Review lesson " + (i + 1), reviewRoot.transform, labels[i], 23 + i % 4 * 201,
                51 + i / 4 * 43, 190, 35, () =>
                {
                    if (!tutorial.IsComplete) return;
                    CloseReviewPicker(); tutorial.ReviewStep(index);
                }, false);
        }
        reviewRoot.SetActive(false);
    }

    void ToggleReviewPicker()
    {
        if (reviewRoot && tutorial.IsComplete) reviewRoot.SetActive(!reviewRoot.activeSelf);
    }

    void CloseReviewPicker()
    {
        if (reviewRoot) reviewRoot.SetActive(false);
    }

    void BuildWorldTarget()
    {
        targetRoot = new GameObject("Practice ground destination (visual only)");
        targetRoot.transform.SetParent(transform, false);
        var shader = Resources.Load<Shader>("VFX/BattleJInk");
        if (!shader) { targetRoot.SetActive(false); return; }
        targetMaterial = new Material(shader) { name = "Practice destination ink", hideFlags = HideFlags.DontSave };
        targetRing = WorldLine("Ground target ring", .035f);
        targetRing.loop = true; targetRing.positionCount = 48;
        for (int i = 0; i < 48; i++)
        {
            float t = i / 48f * Mathf.PI * 2;
            targetRing.SetPosition(i, new Vector3(Mathf.Cos(t) * .73f, Mathf.Sin(t) * .13f, 0));
        }
        targetPointer = WorldLine("Ground destination pointer", .045f);
        targetPointer.positionCount = 3;
        targetPointer.SetPositions(new[] { new Vector3(-.18f, .61f, 0), new Vector3(0, .39f, 0), new Vector3(.18f, .61f, 0) });
        targetRoot.SetActive(false);
    }

    LineRenderer WorldLine(string label, float width)
    {
        var go = new GameObject(label, typeof(LineRenderer)); go.transform.SetParent(targetRoot.transform, false);
        var line = go.GetComponent<LineRenderer>();
        line.useWorldSpace = false; line.sharedMaterial = targetMaterial;
        line.widthMultiplier = width; line.numCapVertices = 2; line.numCornerVertices = 2;
        line.startColor = line.endColor = Mint;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        line.sortingOrder = 5;
        return line;
    }

    void LateUpdate()
    {
        if (!manager || !tutorial || !canvas) return;
        bool show = tutorial.IsRunning && !manager.IsPaused && !manager.HelpOpen && !manager.SelectionOpen &&
            !(manager.trialMenu && manager.trialMenu.Visible);
        SetVisible(show);
        if (!show) return;
        FitSafeArea();
        bool complete = tutorial.IsComplete;
        int step = tutorial.CurrentStepIndex;
        int count = Mathf.Max(1, tutorial.StepCount);
        bool stepChanged = step != displayedStep || count != displayedStepCount || complete != displayedComplete;
        if (stepChanged)
        {
            bool previous = displayedStep >= 0;
            displayedStep = step; displayedStepCount = count; displayedComplete = complete;
            stepChangedAt = Time.unscaledTime;
            SetText(stageNumber, complete ? "OK" : (step + 1).ToString("00"));
            SetText(stepLabel, complete ? "COMPLETE" : "STEP " + (step + 1) + " / " + count);
            SetText(primaryLabel, "START DUEL");
            SetText(retryLabel, complete ? "RETRY LAST STEP" : "RETRY STEP");
            SetText(skipLabel, complete ? "REVIEW LESSONS" : "SKIP LESSON");
            skipButton.gameObject.name = complete ? "Review lessons" : "Skip objective";
            // RetryStep also supports the completed final lesson. Never offer
            // a disabled retry while the summary explicitly promises it.
            retryButton.interactable = skipButton.interactable = true;
            if (!complete) CloseReviewPicker();
            progressFill.rectTransform.localScale = new Vector3(complete ? 1 : Mathf.Clamp01((float)step / count), 1, 1);
            stageNumber.color = previous || complete ? Gold : Mint;
        }
        SetText(title, tutorial.Title);
        SetText(instruction, tutorial.Instruction);
        SetText(detail, tutorial.Detail);
        SetText(keys, tutorial.Keys);
        SetText(progress, tutorial.Progress);
        float charge = tutorial.ChargeProgress;
        bool charging = charge >= 0 && !complete;
        if (chargeTrack.activeSelf != charging) chargeTrack.SetActive(charging);
        chargeFill.localScale = new Vector3(Mathf.Clamp01(charge), 1, 1);
        // A gentle settling motion only when advancing objectives. No flashes,
        // no screenshake, and reduced-motion mode remains completely static.
        float age = Time.unscaledTime - stepChangedAt;
        objective.anchoredPosition = new Vector2(460, -121 + (BattlePresentationSettings.ReducedMotion ? 0 :
            6 * (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / .24f)))));
        UpdateWorldTarget();
    }

    void UpdateWorldTarget()
    {
        Vector3? point = tutorial.TargetWorldPosition;
        bool show = point.HasValue && targetRing && !tutorial.IsComplete;
        if (targetRoot.activeSelf != show) targetRoot.SetActive(show);
        if (!show) { previousTarget = null; return; }
        Vector3 position = point.Value;
        position.z = -.94f; position.y += .055f;
        if (!previousTarget.HasValue || (previousTarget.Value - position).sqrMagnitude > .00001f)
        { targetRoot.transform.position = position; previousTarget = position; }
        // The marker never scales or flashes. It cannot suggest a larger jump,
        // damage area or collision surface than the real mechanic provides.
    }

    void SetVisible(bool show)
    {
        if (!visibility) return;
        if (wasVisible == show && visibility.alpha == (show ? 1 : 0)) return;
        wasVisible = show;
        visibility.alpha = show ? 1 : 0; visibility.interactable = show; visibility.blocksRaycasts = show;
        if (targetRoot && !show) targetRoot.SetActive(false);
        if (!show) CloseReviewPicker();
    }

    void FitSafeArea()
    {
        Rect safe = Screen.safeArea;
        // A runtime Canvas/CanvasScaler may settle one frame after a scene
        // reload without a Screen resize. Do not cache its initial dimensions.
        Vector2 available = canvasRect.rect.size;
        if (layoutWidth == Screen.width && layoutHeight == Screen.height && layoutSafeArea == safe &&
            layoutCanvasSize == available) return;
        layoutWidth = Screen.width; layoutHeight = Screen.height; layoutSafeArea = safe;
        layoutCanvasSize = available;
        float sx = available.x / Mathf.Max(1, Screen.width), sy = available.y / Mathf.Max(1, Screen.height);
        float width = safe.width * sx, height = safe.height * sy;
        float scale = Mathf.Min(width / 1920, height / 1080);
        frame.localScale = Vector3.one * scale;
        frame.anchoredPosition = new Vector2(safe.xMin * sx + (width - 1920 * scale) * .5f,
            -((Screen.height - safe.yMax) * sy + (height - 1080 * scale) * .5f));
    }

    void OnDisable()
    {
        SetVisible(false);
        if (targetRoot) targetRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (targetMaterial) Destroy(targetMaterial);
    }

    static void SetText(TMP_Text label, string value)
    {
        if (label && label.text != value) label.text = value ?? "";
    }

    static RectTransform Rect(string label, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(label, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1);
        r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
        return r;
    }

    static Image Surface(string label, Transform parent, float x, float y, float w, float h, Color face, Color edge, float border)
    {
        var graphic = Rect(label, parent, x, y, w, h).gameObject.AddComponent<BattleTrialPaperGraphic>();
        graphic.torn = false; graphic.color = face; graphic.outline = edge; graphic.border = border;
        graphic.raycastTarget = false; return graphic;
    }

    TMP_Text Text(string label, Transform parent, string value, float x, float y, float w, float h, float size,
        Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var text = Rect(label, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.color = color; text.alignment = align;
        text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false; return text;
    }

    Button Button(string label, Transform parent, string value, float x, float y, float w, float h,
        UnityEngine.Events.UnityAction action, bool primary)
    {
        var image = Surface(label, parent, x, y, w, h, primary ? Gold : Ink, primary ? Cream : Quiet, 1);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1, .97f, .87f); colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(.78f, .84f, .76f); colors.disabledColor = new Color(.57f, .67f, .60f, .7f);
        colors.fadeDuration = .08f; button.colors = colors;
        button.onClick.AddListener(SideAAudio.Interface); button.onClick.AddListener(action);
        var text = Text("Label", image.transform, value, 9, 7, w - 18, h - 14, 20, primary ? Ink : Cream, TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold;
        text.enableAutoSizing = true; text.fontSizeMin = 13; text.fontSizeMax = 20;
        return button;
    }
}
