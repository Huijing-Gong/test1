using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A small, non-modal reminder layer for actual duels. It observes gameplay
// only, never synthesizes input or writes combat/progression state.
[DisallowMultipleComponent]
public sealed class BattleTutorialHints : MonoBehaviour
{
    BattleManager owner;
    BattlePlayerController player;
    BattleAttack attack;
    BattleSpecialMoves specials;
    BattleStockLives stocks;
    BattleMotionPresentation motion;
    Collider deck;
    RectTransform root, card;
    CanvasGroup group;
    TMP_Text title, body;
    int oldCue, oldStocks = 2, misses;
    bool offstageSeen, stockSeen, dropSeen, attackWasLive, missSeen, percentSeen;
    float visibleTime, cooldown;
    public bool Visible => group && group.alpha > .01f;
    public string CurrentHint => title ? title.text : "";

    public void Bind(BattleManager manager, GameObject fighter, GameObject rival)
    {
        if (owner || !manager || !fighter) return;
        owner = manager;
        player = fighter.GetComponent<BattlePlayerController>();
        attack = fighter.GetComponent<BattleAttack>();
        specials = fighter.GetComponent<BattleSpecialMoves>();
        stocks = fighter.GetComponent<BattleStockLives>();
        motion = fighter.GetComponent<BattleMotionPresentation>();
        var arena = manager.GetComponent<BattleArenaStages>();
        if (arena && arena.mainGround) deck = arena.mainGround.GetComponent<Collider>();
        oldStocks = stocks ? stocks.Remaining : 2;
        var canvasObject = new GameObject("Side A Context Hints", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 850;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        root = canvasObject.GetComponent<RectTransform>();
        card = CreateRect("Hint card", root, new Vector2(.5f, 0), new Vector2(0, 245), new Vector2(730, 104));
        group = card.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        var shape = card.gameObject.AddComponent<BattleHudShape>();
        shape.shape = BattleHudShape.Kind.Clipped;
        shape.color = new Color32(19, 55, 49, 245);
        shape.rim = new Color32(132, 205, 196, 255);
        shape.raycastTarget = false;
        title = Label("Hint title", card, new Vector2(0, 22), new Vector2(680, 30), 23, FontStyles.Bold,
            new Color32(255, 208, 102, 255));
        body = Label("Hint instruction", card, new Vector2(0, -18), new Vector2(680, 46), 22, FontStyles.Normal,
            new Color32(246, 236, 207, 255));
        body.enableAutoSizing = true;
        body.fontSizeMin = 18; body.fontSizeMax = 22;
        body.overflowMode = TextOverflowModes.Ellipsis;
        group.alpha = 0;
    }

    void Update()
    {
        if (!owner || !group) return;
        bool playable = BattlePresentationSettings.ContextHintsEnabled && owner.IsBattleActive && !owner.IsGameOver && !owner.IsTutorialActive &&
            !owner.IsPaused && !owner.HelpOpen && !owner.IsAwaitingIntroduction;
        if (!playable) { group.alpha = 0; return; }
        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
        cooldown = Mathf.Max(0, cooldown - dt);
        visibleTime = Mathf.Max(0, visibleTime - dt);
        group.alpha = Mathf.Clamp01(visibleTime / .45f);
        // Fit the card inside narrow windows without entering the stock slots.
        float fit = Mathf.Min(1, root.rect.width / 1000f);
        card.localScale = Vector3.one * fit;
        if (!motion && player) motion = player.GetComponent<BattleMotionPresentation>();

        if (stocks && stocks.Remaining < oldStocks)
        {
            oldStocks = stocks.Remaining;
            if (!stockSeen && stocks.Remaining == 1)
            {
                stockSeen = true;
                Show("ONE LIFE LEFT", "Your damage resets to 0%. The next ring-out ends the duel.", true);
            }
        }
        if (!player || !deck || stocks && stocks.Respawning) return;
        var bounds = deck.bounds;
        bool offstage = player.transform.position.x < bounds.min.x || player.transform.position.x > bounds.max.x;
        if (!offstageSeen && offstage && !player.IsOnGround)
        {
            offstageSeen = true;
            // Recovery is urgent: never suppress the first rescue cue behind
            // a 22-second cooldown from a generic damage/combo reminder.
            bool canRise = specials && !specials.RecoverySpent;
            string recovery = canRise ? (player.HasAirJump
                ? "SPACE: air jump. Then W + RIGHT CLICK (K) to rise; steer A / D toward the stage."
                : "W + RIGHT CLICK (K) to rise. Steer A / D toward the stage.")
                : (player.HasAirJump
                    ? "Rise already used. SPACE: air jump; steer A / D toward the stage. Land to refresh."
                    : "Air jump and rise used. Steer A / D toward the edge. Land to refresh recovery.");
            Show("GET BACK TO THE STAGE", recovery, true);
        }
        if (motion && motion.CueCount != oldCue)
        {
            oldCue = motion.CueCount;
            if (!dropSeen && motion.LastCue == "drop through" && cooldown <= 0)
            {
                dropSeen = true;
                Show("CHANGE LEVELS", "S + SPACE passes through upper platforms, not the main floor.", false);
            }
        }
        if (!attack) return;
        bool live = attack.IsMovementLocked;
        if (attackWasLive && !live && attack.LastAttackResolved)
        {
            if (!attack.LastAttackLanded) misses++;
            else
            {
                misses = 0;
                if (!percentSeen && cooldown <= 0)
                {
                    percentSeen = true;
                    Show("DAMAGE IS NOT HEALTH", "Higher damage means stronger knockback. Win by knocking your rival out twice.", false);
                }
            }
            if (!missSeen && misses >= 3 && cooldown <= 0)
            {
                missSeen = true;
                Show("CLOSE THE DISTANCE", "Release W / S; A / D is fine. Click LEFT (J) again after each hit to continue the combo.", false);
            }
        }
        attackWasLive = live;
    }

    void Show(string heading, string instruction, bool priority)
    {
        if (!priority && (cooldown > 0 || visibleTime > 0)) return;
        title.text = heading; body.text = instruction;
        visibleTime = 7;
        cooldown = 22;
        group.alpha = 1;
    }

    TMP_Text Label(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, FontStyles style, Color tint)
    {
        var rect = CreateRect(name, parent, new Vector2(.5f, .5f), position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = owner.battleTimerText ? owner.battleTimerText.font : TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize; text.fontStyle = style; text.color = tint;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }
}
