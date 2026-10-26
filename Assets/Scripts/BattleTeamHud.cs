using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presentation only: reads damage, never changes attack/physics/reward state.
// Entries are reusable HUD slots, not an implementation of multiplayer combat.
[DisallowMultipleComponent]
public sealed class BattleTeamHud : MonoBehaviour
{
    [Serializable]
    public sealed class Fighter
    {
        public BattleDamageReceiver source;
        public string label = "FIGHTER";
        public Sprite portrait;
        public Color identity = new Color32(80, 180, 213, 255);
        public Color numberTint = new Color32(247, 236, 205, 255);
    }
    public List<Fighter> fighters = new List<Fighter>();
    public TMP_FontAsset font;
    public string stageTitle = "SUNLIT RUINS";
    public string difficultyLabel = "BEGINNER";
    public BattleManager manager;
    public RectTransform legacyHud;
    public Button PauseButton { get; private set; }
    public Button ControlsButton { get; private set; }
    public RectTransform Root { get; private set; }
    public TMP_Text Timer { get; private set; }
    TMP_Text stageText, difficultyText;
    public IReadOnlyList<BattleFighterHud> Widgets => widgets;
    public bool Visible => group && group.alpha > .01f;
    static readonly Color Ink = new Color32(19, 55, 49, 250);
    static readonly Color Cream = new Color32(246, 236, 207, 255);
    static readonly Color Mint = new Color32(149, 205, 189, 255);
    static readonly Color Gold = new Color32(234, 190, 85, 255);
    readonly List<BattleFighterHud> widgets = new List<BattleFighterHud>();
    readonly List<KeyValuePair<GameObject, bool>> oldChildren = new List<KeyValuePair<GameObject, bool>>();
    CanvasGroup group;
    RectTransform controls;
    float lastWidth;
    int lastSecond = -1;
    bool showedPracticeTimer;
    float startedAt;

    IEnumerator Start()
    {
        // Let the existing manager bind its real buttons and menu first.
        yield return null;
        if (!manager || !legacyHud || !font) { Debug.LogError("Team HUD is missing scene bindings.", this); yield break; }
        Root = Rect("Team Gameplay HUD", legacyHud, Vector2.zero, Vector2.zero, Vector2.zero);
        Root.anchorMax = Vector2.one; Root.offsetMin = Root.offsetMax = Vector2.zero;
        group = Root.gameObject.AddComponent<CanvasGroup>();
        for (int i = 0; i < legacyHud.childCount; i++)
        {
            var child = legacyHud.GetChild(i);
            if (child == Root || (manager.countdownText && manager.countdownText.transform.IsChildOf(child))) continue;
            oldChildren.Add(new KeyValuePair<GameObject, bool>(child.gameObject, child.gameObject.activeSelf));
            child.gameObject.SetActive(false);
        }
        // Countdown remains above the presentation, with its original binding.
        Root.SetAsFirstSibling();
        BuildTop(); RebuildFighters();
        startedAt = Time.unscaledTime;
    }

    void BuildTop()
    {
        var pause = Shape("Team ESC Pause", Root, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(40, -28), new Vector2(112, 72), BattleHudShape.Kind.Capsule, Ink, Cream);
        PauseButton = Button(pause);
        PauseButton.onClick.AddListener(() =>
        {
            // Uses the manager's already-bound command; doesn't replace its state machine.
            if (manager.pauseButton && manager.pauseButton.interactable) manager.pauseButton.onClick.Invoke();
        });
        Text("ESC label", pause, "ESC", 28, FontStyles.Bold, Cream,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-12, 0), new Vector2(86, 56));
        Shape("Pause jewel", pause, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(37, 0), new Vector2(14, 19), BattleHudShape.Kind.Diamond, Gold, Gold, 0);
        var timer = Shape("Team Timer", Root, new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -28), new Vector2(236, 64), BattleHudShape.Kind.Capsule, Cream, Ink);
        timer.GetComponent<BattleHudShape>().cornerRatio = .12f;
        Timer = Text("Elapsed time", timer, "00:00", 43, FontStyles.Bold, Ink,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(208, 62));
        var stage = Shape("Team Stage", Root, Vector2.one, Vector2.one, new Vector2(-40, -28),
            new Vector2(274, 54), BattleHudShape.Kind.Capsule, Ink, Mint);
        stageText = Text("Stage name", stage, stageTitle, 23, FontStyles.Bold, Cream,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-9, 0), new Vector2(235, 45));
        stageText.enableAutoSizing = true; stageText.fontSizeMin = 17; stageText.fontSizeMax = 23;
        Shape("Stage jewel", stage, new Vector2(1, .5f), new Vector2(.5f, .5f), new Vector2(-24, 0),
            new Vector2(12, 17), BattleHudShape.Kind.Diamond, Gold, Gold, 0);
        var difficulty = Shape("Team Difficulty", Root, Vector2.one, Vector2.one, new Vector2(-50, -84),
            new Vector2(188, 34), BattleHudShape.Kind.Clipped, Mint, Mint, 0);
        difficultyText = Text("Difficulty", difficulty, difficultyLabel, 19, FontStyles.Bold, Ink,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(170, 30));
        controls = Shape("Team Controls", Root, new Vector2(.5f, 0), new Vector2(.5f, 0),
            new Vector2(0, 56), new Vector2(226, 52), BattleHudShape.Kind.Capsule, Ink, Mint);
        ControlsButton = Button(controls); ControlsButton.onClick.AddListener(manager.ToggleHelp);
        var key = Shape("H key", controls, new Vector2(0, .5f), new Vector2(.5f, .5f), new Vector2(27, 0),
            new Vector2(40, 40), BattleHudShape.Kind.Circle, Cream, Gold, 2);
        Text("Key label", key, "H", 28, FontStyles.Bold, Ink,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(36, 36));
        Text("Controls label", controls, "CONTROLS", 20, FontStyles.Bold, Cream,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(17, 0), new Vector2(124, 40));
        Shape("Controls arrow", controls, new Vector2(1, .5f), new Vector2(.5f, .5f), new Vector2(-20, 0),
            new Vector2(12, 17), BattleHudShape.Kind.Triangle, Mint, Mint, 0);
    }

    public void SetStageLabels(string title, string difficulty)
    {
        stageTitle = title; difficultyLabel = difficulty;
        if (stageText) stageText.text = title;
        if (difficultyText) difficultyText.text = difficulty;
    }

    public void RebuildFighters()
    {
        if (!Root) return;
        foreach (var widget in widgets) if (widget && widget.gameObject) Destroy(widget.gameObject);
        widgets.Clear();
        for (int i = 0; i < fighters.Count; i++)
        {
            var root = Rect("Fighter slot " + (i + 1), Root, Vector2.zero, Vector2.zero, new Vector2(430, 180));
            var widget = root.gameObject.AddComponent<BattleFighterHud>();
            widget.Build(this, fighters[i], fighters.Count == 2 && i == 1);
            widgets.Add(widget);
        }
        lastWidth = -1; Layout();
    }
    void Layout()
    {
        float width = Root.rect.width;
        if (width < 1 || Mathf.Abs(lastWidth - width) < .1f) return;
        lastWidth = width;
        float cardWidth = Mathf.Min(430, (width - 80 - Mathf.Max(0, widgets.Count - 1) * 20) / Mathf.Max(1, widgets.Count));
        for (int i = 0; i < widgets.Count; i++)
        {
            var r = widgets[i].Rect;
            r.anchorMin = r.anchorMax = new Vector2(0, 0); r.pivot = Vector2.zero;
            float x = widgets.Count == 1 ? (width - cardWidth) * .5f :
                Mathf.Lerp(40, width - 40 - cardWidth, i / (float)(widgets.Count - 1));
            r.anchoredPosition = new Vector2(x, 36); r.sizeDelta = new Vector2(cardWidth, 180);
            widgets[i].Layout(cardWidth);
        }
        // More-player UI preview uses one evenly spaced row, no centre overlap.
        controls.anchoredPosition = new Vector2(0, widgets.Count > 2 ? 240 : 56);
    }

    void LateUpdate()
    {
        if (!Root) return;
        bool show = !manager.IsGameOver && !(manager.trialMenu && manager.trialMenu.Visible);
        group.alpha = show ? Mathf.SmoothStep(0, 1, (Time.unscaledTime - startedAt) / .24f) : 0;
        group.interactable = group.blocksRaycasts = show;
        PauseButton.interactable = manager.IsBattleActive && !manager.IsPaused;
        ControlsButton.interactable = manager.IsBattleActive && !manager.IsAwaitingIntroduction;
        int seconds = Mathf.Max(0, Mathf.FloorToInt(manager.BattleDuration));
        if (manager.IsTutorialActive)
        {
            if (!showedPracticeTimer) { Timer.fontSize = 28; Timer.text = "PRACTICE"; showedPracticeTimer = true; }
        }
        else if (seconds != lastSecond || showedPracticeTimer)
        {
            showedPracticeTimer = false; Timer.fontSize = 43;
            lastSecond = seconds; Timer.SetText("{0:00}:{1:00}", seconds / 60, seconds % 60);
        }
        Layout();
    }
    void OnDestroy()
    {
        foreach (var old in oldChildren) if (old.Key) old.Key.SetActive(old.Value);
        if (Root) Destroy(Root.gameObject);
    }
    public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size)
    { return Rect(name, parent, anchor, pivot, Vector2.zero, size); }
    public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false); r.gameObject.layer = parent.gameObject.layer;
        r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size; return r;
    }
    public static RectTransform Shape(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos,
        Vector2 size, BattleHudShape.Kind shape, Color fill, Color rim, float stroke = 2.5f)
    {
        var r = Rect(name, parent, anchor, pivot, pos, size);
        var g = r.gameObject.AddComponent<BattleHudShape>();
        g.shape = shape; g.color = fill; g.rim = rim; g.stroke = stroke; g.raycastTarget = false; return r;
    }
    Button Button(RectTransform rect)
    {
        var graphic = rect.GetComponent<BattleHudShape>(); graphic.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.18f, 1.18f, 1.1f);
        colors.pressedColor = new Color(.8f, .94f, .88f); colors.disabledColor = new Color(1, 1, 1, .45f);
        colors.fadeDuration = .1f; button.colors = colors; return button;
    }
    public TMP_Text Text(string name, Transform parent, string content, float size, FontStyles style, Color color,
        Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 bounds)
    {
        var r = Rect(name, parent, anchor, pivot, pos, bounds);
        var text = r.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.text = content;
        text.fontSize = size; text.fontStyle = style; text.color = color; text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false; text.raycastTarget = false; text.margin = Vector4.zero;
        return text;
    }
}
