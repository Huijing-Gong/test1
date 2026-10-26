using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class DialogueUI : MonoBehaviour
{
    [SerializeField] private GameObject interactionPrompt;
    [SerializeField] private Text promptText;
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private Text npcNameText;
    [SerializeField] private Text dialogueText;
    [SerializeField] private Text continueHintText;
    [SerializeField] private GameObject itemAcquiredPanel;
    [SerializeField] private Text itemAcquiredText;
    [SerializeField, Min(0f)] private float itemAcquiredDuration = 2f;

    private NPCDialogue activeNPC;
    private PlayerMovement lockedPlayerMovement;
    private ThirdPersonCamera lockedCamera;
    private int lineIndex;
    BaselineFlow presentationFlow;
    bool previousMovementLock, previousLookLock;
    int closeGeneration;
    bool ExplorationObscured => presentationFlow && presentationFlow.WorldHudHidden;
    public float ItemNoticeRemaining => itemAcquiredRemaining;
    private float itemAcquiredRemaining;
    private string npcPrompt;
    private string contextPrompt;
    private int contextFrame = -1;
    private int contextPriority;
    private NPCDialogue promptNPC;
    private RioDialoguePresentation rioPresentation;
    private bool closingAnimated;
    private CanvasGroup standardDialogueOpacity;
    private RectTransform standardDialogueRect;
    private float standardDialogueOpenedAt;
    public bool IsRewardPresentationActive { get; private set; }
    public int LastDialogueClosedFrame { get; private set; } = -1;

    public Font PresentationFont => promptText != null ? promptText.font : null;

    public bool BeginRewardPresentation()
    {
        if (IsDialogueActive || IsRewardPresentationActive) return false;
        IsRewardPresentationActive = true;
        HidePrompt();
        SetDialoguePanelVisible(false);
        SetItemAcquiredVisible(false);
        return true;
    }

    public void HideRewardPresentation()
    {
        if (!IsRewardPresentationActive) return;
        IsRewardPresentationActive = false;
        SetDialoguePanelVisible(false);
    }

    // All nearby interactions use the existing Talk panel and its Canvas scaling.
    public void OfferPrompt(string text, int priority = 0)
    {
        if (contextFrame != Time.frameCount || priority > contextPriority)
        {
            contextPrompt = text;
            contextPriority = priority;
            contextFrame = Time.frameCount;
        }
    }

    private void LateUpdate()
    {
        string text = IsDialogueActive || IsRewardPresentationActive || ExplorationObscured ? null : npcPrompt
            ?? (contextFrame == Time.frameCount ? contextPrompt : null);
        if (promptText != null) promptText.text = text ?? string.Empty;
        bool rioPrompt = !string.IsNullOrEmpty(text) && rioPresentation != null && rioPresentation.Supports(promptNPC);
        if (rioPresentation != null) rioPresentation.SetPrompt(rioPrompt, promptNPC);
        if (interactionPrompt != null) interactionPrompt.SetActive(!string.IsNullOrEmpty(text) && !rioPrompt);
    }

    public bool IsInteractionPromptVisible => (interactionPrompt != null && interactionPrompt.activeInHierarchy) || (rioPresentation != null && rioPresentation.PromptVisible);
    public bool IsDialogueActive => activeNPC != null;
    public NPCDialogue ActiveNPC => activeNPC;
    public int CurrentLineIndex => lineIndex;

    private void Awake()
    {
        presentationFlow=FindFirstObjectByType<BaselineFlow>();
        HidePrompt();
        SetDialoguePanelVisible(false);
        SetItemAcquiredVisible(false);
        ConfigureReadableFallback();
        rioPresentation = RioDialoguePresentation.Attach(this);
    }

    private void ConfigureReadableFallback()
    {
        // Notes, inscriptions and chests share one restrained field-note panel.
        var scaler = GetComponentInParent<CanvasScaler>();
        float scale = scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
            ? scaler.referenceResolution.y / 720f : 1f;
        ConfigureInteractionPrompt(scale);
        if (dialoguePanel != null)
        {
            standardDialogueRect = dialoguePanel.GetComponent<RectTransform>();
            standardDialogueRect.anchorMin = standardDialogueRect.anchorMax = new Vector2(.5f, 0);
            standardDialogueRect.pivot = new Vector2(.5f, 0);
            standardDialogueRect.sizeDelta = new Vector2(900 * scale, 210 * scale);
            standardDialogueRect.anchoredPosition = new Vector2(0,28*scale);

            var oldBackground = dialoguePanel.GetComponent<Image>();
            if (oldBackground != null)
            {
                oldBackground.enabled = true;
                oldBackground.color = new Color32(18, 49, 43, 184);
                oldBackground.raycastTarget = false;
            }
            AddBorder("Top border", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -2 * scale), Vector2.zero);
            AddBorder("Bottom border", Vector2.zero, new Vector2(1, 0),
                Vector2.zero, new Vector2(0, 2 * scale));
            AddBorder("Left border", Vector2.zero, new Vector2(0, 1),
                Vector2.zero, new Vector2(2 * scale, 0));
            AddBorder("Right border", new Vector2(1, 0), Vector2.one,
                new Vector2(-2 * scale, 0), Vector2.zero);
            AddRule("Header rule", .735f, scale);
            AddRule("Footer rule", .265f, scale);
            standardDialogueOpacity = dialoguePanel.GetComponent<CanvasGroup>();
            if (standardDialogueOpacity == null) standardDialogueOpacity = dialoguePanel.AddComponent<CanvasGroup>();
        }
        LayoutDialogueText(npcNameText, .79f, .93f, Mathf.RoundToInt(15 * scale));
        LayoutDialogueText(dialogueText, .32f, .68f, Mathf.RoundToInt(18 * scale));
        LayoutDialogueText(continueHintText, .07f, .22f, Mathf.RoundToInt(12 * scale));
        if (npcNameText != null) { npcNameText.color = WayfarerUITheme.Paper; npcNameText.fontStyle = FontStyle.Bold; }
        if (dialogueText != null) dialogueText.color = new Color32(240, 232, 208, 255);
        if (continueHintText != null) continueHintText.color = new Color32(194, 177, 130, 255);
        if (dialogueText != null) dialogueText.lineSpacing = 1.12f;
        if (continueHintText != null) continueHintText.alignment = TextAnchor.MiddleRight;
    }

    private void ConfigureInteractionPrompt(float scale)
    {
        if (interactionPrompt == null) return;
        var rect = interactionPrompt.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(430f * scale, 54f * scale);
            rect.anchoredPosition = new Vector2(0, 115f * scale);
        }
        var background = interactionPrompt.GetComponent<Image>();
        if (background != null)
        {
            background.color = new Color32(18, 49, 43, 184);
            background.raycastTarget = false;
        }
        if (promptText != null)
        {
            var textRect = promptText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f * scale, 5f * scale);
            textRect.offsetMax = new Vector2(-18f * scale, -5f * scale);
            promptText.color = WayfarerUITheme.Paper;
            promptText.fontSize = Mathf.RoundToInt(20f * scale);
            promptText.alignment = TextAnchor.MiddleCenter;
            promptText.resizeTextForBestFit = false;
            promptText.raycastTarget = false;
        }
        AddPromptEdge("Top edge", new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -2f * scale), Vector2.zero);
        AddPromptEdge("Bottom edge", Vector2.zero, new Vector2(1, 0),
            Vector2.zero, new Vector2(0, 2f * scale));
        AddPromptEdge("Left edge", Vector2.zero, new Vector2(0, 1),
            Vector2.zero, new Vector2(2f * scale, 0));
        AddPromptEdge("Right edge", new Vector2(1, 0), Vector2.one,
            new Vector2(-2f * scale, 0), Vector2.zero);
    }

    private void AddPromptEdge(string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var edge = new GameObject(name, typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(interactionPrompt.transform, false);
        edge.transform.SetAsFirstSibling();
        var rect = edge.GetComponent<RectTransform>();
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
        var image = edge.GetComponent<Image>();
        image.color = new Color32(186, 155, 87, 210);
        image.raycastTarget = false;
    }

    private void AddRule(string name, float height, float scale)
    {
        var line = new GameObject(name, typeof(RectTransform), typeof(Image));
        line.transform.SetParent(dialoguePanel.transform, false);
        line.transform.SetSiblingIndex(1);
        var rect = line.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(.06f, height);
        rect.anchorMax = new Vector2(.94f, height);
        rect.sizeDelta = new Vector2(0, Mathf.Max(1, scale));
        rect.anchoredPosition = Vector2.zero;
        var image = line.GetComponent<Image>();
        image.color = new Color32(186, 155, 87, 125);
        image.raycastTarget = false;
    }

    private void AddBorder(string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var edge = new GameObject(name, typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(dialoguePanel.transform, false);
        edge.transform.SetAsFirstSibling();
        var rect = edge.GetComponent<RectTransform>();
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
        var image = edge.GetComponent<Image>();
        image.color = new Color32(186, 155, 87, 210);
        image.raycastTarget = false;
    }

    private static void LayoutDialogueText(Text text, float bottom, float top, int size)
    {
        if (text == null) return;
        text.rectTransform.anchorMin = new Vector2(.06f, bottom);
        text.rectTransform.anchorMax = new Vector2(.94f, top);
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.fontSize = size;
        text.resizeTextForBestFit = false;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    private void Update()
    {
        if (standardDialogueOpacity != null && dialoguePanel.activeSelf)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - standardDialogueOpenedAt) / .22f);
            float eased = 1 - (1 - t) * (1 - t);
            standardDialogueOpacity.alpha = eased;
            standardDialogueRect.localScale = Vector3.one * Mathf.Lerp(.97f, 1, eased);
        }
        if(itemAcquiredRemaining>0)
        {
            bool visible=!IsDialogueActive && !IsRewardPresentationActive && !ExplorationObscured;
            if(visible)itemAcquiredRemaining=Mathf.Max(0,itemAcquiredRemaining-Time.unscaledDeltaTime);
            SetItemAcquiredVisible(visible && itemAcquiredRemaining>0);
        }

        if (!ReferenceEquals(activeNPC, null) &&
            (activeNPC == null || !activeNPC.isActiveAndEnabled))
        {
            EndDialogue();
        }
    }

    public void ShowPrompt(string text, NPCDialogue npc = null)
    {
        promptNPC = npc;
        npcPrompt = text;
        if (IsDialogueActive || IsRewardPresentationActive)
        {
            HidePrompt();
            return;
        }

        if (promptText != null)
        {
            promptText.text = text;
        }

        if (interactionPrompt != null)
        {
            interactionPrompt.SetActive(true);
        }
    }

    public void HidePrompt()
    {
        npcPrompt = null;
        promptNPC = null;
        if (interactionPrompt != null)
        {
            interactionPrompt.SetActive(false);
        }
    }

    public bool BeginDialogue(
        NPCDialogue npc,
        PlayerMovement playerMovement,
        ThirdPersonCamera thirdPersonCamera)
    {
        if (IsRewardPresentationActive || npc == null || !npc.IsInteractable || playerMovement == null ||
            !playerMovement.CanStartInteraction)
        {
            return false;
        }

        EndDialogue();
        activeNPC = npc;
        lockedPlayerMovement = playerMovement;
        lockedCamera = thirdPersonCamera;
        previousMovementLock=lockedPlayerMovement.IsGameplayInputLocked;
        previousLookLock=lockedCamera && lockedCamera.IsLookInputLocked;
        lineIndex = 0;

        lockedPlayerMovement.SetGameplayInputLocked(true);
        if (lockedCamera != null)
        {
            lockedCamera.SetLookInputLocked(true);
        }

        HidePrompt();
        bool useRio = rioPresentation != null && rioPresentation.Supports(npc);
        SetDialoguePanelVisible(!useRio);
        if (useRio) rioPresentation.Open(npc);
        RefreshDialogueText();
        return true;
    }

    public void AdvanceDialogue()
    {
        if (!IsDialogueActive || closingAnimated)
        {
            return;
        }

        if (rioPresentation != null && rioPresentation.IsPresenting && rioPresentation.RevealIfBusy()) return;
        lineIndex++;
        if (lineIndex >= activeNPC.LineCount)
        {
            CompleteDialogue();
            return;
        }

        RefreshDialogueText();
    }

    public void EndDialogue()
    {
        CloseDialogue();
    }

    public void CancelDialogueAnimated() => RequestAnimatedClose(false, null);

    public bool OpenRioJournal()
    {
        if (closingAnimated || lineIndex < 1 || rioPresentation == null || !rioPresentation.SupportsJournal(activeNPC)) return false;
        var world = FindFirstObjectByType<BaselineWorld>();
        if (world == null) return false;
        RequestAnimatedClose(true, () => { if (world != null && world.isActiveAndEnabled) world.SetJournal(true); });
        return true;
    }

    public void ShowItemAcquired(string displayName)
    {
        if (itemAcquiredText != null) itemAcquiredText.text = "Acquired: " + displayName;
        itemAcquiredRemaining = Mathf.Max(0f, itemAcquiredDuration);
        SetItemAcquiredVisible(itemAcquiredRemaining>0 && !IsDialogueActive && !IsRewardPresentationActive && !ExplorationObscured);
    }

    private void CompleteDialogue()
    {
        RequestAnimatedClose(true, null);
    }

    private void RequestAnimatedClose(bool complete, System.Action afterClose)
    {
        if (closingAnimated) return;
        NPCDialogue completedNPC = activeNPC;
        int generation=closeGeneration;
        System.Action finish = () =>
        {
            if(generation!=closeGeneration)return;
            CloseDialogue();
            if (complete && completedNPC != null) completedNPC.NotifyDialogueCompleted(this);
            afterClose?.Invoke();
        };
        if (rioPresentation != null && rioPresentation.IsPresenting && isActiveAndEnabled)
        {
            closingAnimated = true;
            rioPresentation.CloseAnimated(finish);
        }
        else finish();
    }

    private void CloseDialogue()
    {
        closeGeneration++;
        bool keepLocked=presentationFlow && presentationFlow.BlockWorldInput;
        closingAnimated = false;
        if (rioPresentation != null) rioPresentation.HideImmediately();
        if (activeNPC != null) LastDialogueClosedFrame = Time.frameCount;
        if (lockedPlayerMovement != null)
        {
            lockedPlayerMovement.SetGameplayInputLocked(previousMovementLock || keepLocked);
        }

        if (lockedCamera != null)
        {
            lockedCamera.SetLookInputLocked(previousLookLock || keepLocked);
        }

        activeNPC = null;
        lockedPlayerMovement = null;
        lockedCamera = null;
        lineIndex = 0;
        SetDialoguePanelVisible(false);
    }

    private void SetItemAcquiredVisible(bool visible)
    {
        if (itemAcquiredPanel != null) itemAcquiredPanel.SetActive(visible);
    }

    private void RefreshDialogueText()
    {
        if (activeNPC == null)
        {
            EndDialogue();
            return;
        }

        if (rioPresentation != null && rioPresentation.IsPresenting) rioPresentation.SetPage(lineIndex);

        if (npcNameText != null)
        {
            npcNameText.text = activeNPC.NPCName;
        }

        if (dialogueText != null)
        {
            dialogueText.text = activeNPC.GetLine(lineIndex);
        }

        if (continueHintText != null)
        {
            continueHintText.text = "[E] Continue    [Esc] Close";
        }
    }

    private void SetDialoguePanelVisible(bool visible)
    {
        if (dialoguePanel != null)
        {
            if (visible && !dialoguePanel.activeSelf)
            {
                standardDialogueOpenedAt = Time.unscaledTime;
                if (standardDialogueOpacity != null) standardDialogueOpacity.alpha = 0;
            }
            dialoguePanel.SetActive(visible);
        }
    }

    private void OnDisable()
    {
        contextFrame = -1;
        HideRewardPresentation();
        EndDialogue();
        HidePrompt();
        itemAcquiredRemaining = 0f;
        SetItemAcquiredVisible(false);
    }
}
