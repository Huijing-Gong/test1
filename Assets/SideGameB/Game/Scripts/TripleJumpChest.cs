using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class TripleJumpChest : MonoBehaviour
{
    [SerializeField] private SpriteRenderer chestRenderer;
    [SerializeField] private Sprite openSprite;
    private PlayerController2D nearbyPlayer;
    [SerializeField] private GameObject prompt;
    [SerializeField] private GameObject rewardPanel;
    [SerializeField] private GameObject timerPanel;
    [SerializeField] private Text timerText;
    private bool opened;
    private float rewardHideTime;
    private float chestHideTime;

    public void ConfigurePersistent(Sprite closed, Sprite opened, Sprite potion, Transform canvas)
    {
        openSprite = opened;
        chestRenderer = GetComponent<SpriteRenderer>();
        if (chestRenderer == null)
            chestRenderer = gameObject.AddComponent<SpriteRenderer>();
        chestRenderer.sprite = closed;
        chestRenderer.sortingOrder = 9;
        CircleCollider2D trigger = GetComponent<CircleCollider2D>();
        if (trigger == null)
            trigger = gameObject.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 2.7f;
        trigger.offset = new Vector2(0f, 0.7f);
        if (prompt == null)
            BuildUi(canvas, potion);
    }

    public void ApplyCuteStyle(Sprite panelSprite, Sprite potionSprite)
    {
        if (rewardPanel != null)
        {
            Image panel = rewardPanel.GetComponent<Image>();
            panel.sprite = panelSprite;
            panel.color = Color.white;
            rewardPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(620f, 290f);
            foreach (Text text in rewardPanel.GetComponentsInChildren<Text>(true))
            {
                text.color = new Color(0.29f, 0.20f, 0.11f, 1f);
                text.fontSize = 28;
                SetRect(text.rectTransform, new Vector2(78f, -2f), new Vector2(370f, 185f));
            }
        }

        if (timerPanel != null)
        {
            Image panel = timerPanel.GetComponent<Image>();
            panel.sprite = panelSprite;
            panel.color = Color.white;
            timerPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(380f, 126f);
            foreach (Text text in timerPanel.GetComponentsInChildren<Text>(true))
            {
                text.color = new Color(0.26f, 0.20f, 0.12f, 1f);
                text.fontSize = 24;
                SetRect(text.rectTransform, new Vector2(48f, 0f), new Vector2(260f, 72f));
            }
        }

        if (rewardPanel != null)
            ReplacePotionIcons(rewardPanel.transform, potionSprite);
        if (timerPanel != null)
            ReplacePotionIcons(timerPanel.transform, potionSprite);
    }

    private static void ReplacePotionIcons(Transform root, Sprite potionSprite)
    {
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.gameObject.name != "Potion Icon")
                continue;
            image.sprite = potionSprite;
            image.color = Color.white;
            image.preserveAspect = true;
            if (root.gameObject.name == "Potion Reward")
                SetRect(image.rectTransform, new Vector2(-180f, -4f), new Vector2(132f, 132f));
            else
                SetRect(image.rectTransform, new Vector2(-132f, 0f), new Vector2(66f, 66f));
        }
    }

    private void Update()
    {
        if (!opened && chestRenderer != null)
        {
            float glow = 0.92f + Mathf.PingPong(Time.time * 0.18f, 0.08f);
            chestRenderer.color = new Color(1f, glow, 0.78f, 1f);
        }

        if (!opened && nearbyPlayer != null && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            Open();

        if (opened && chestRenderer.enabled && Time.time >= chestHideTime)
        {
            chestRenderer.enabled = false;
            CircleCollider2D trigger = GetComponent<CircleCollider2D>();
            if (trigger != null)
                trigger.enabled = false;
        }

        if (rewardPanel.activeSelf && Time.time >= rewardHideTime)
            rewardPanel.SetActive(false);

        if (opened && nearbyPlayer != null && nearbyPlayer.HasTripleJump)
        {
            timerPanel.SetActive(true);
            float seconds = nearbyPlayer.TripleJumpSecondsRemaining;
            timerText.text = $"TRIPLE JUMP  {Mathf.FloorToInt(seconds / 60f)}:{Mathf.CeilToInt(seconds % 60f):00}";
        }
        else if (timerPanel.activeSelf)
            timerPanel.SetActive(false);
    }

    private void Open()
    {
        GameAudio.Play(GameSound.ChestOpen);
        GameAudio.Play(GameSound.ItemGet);
        opened = true;
        chestRenderer.color = Color.white;
        chestRenderer.sprite = openSprite;
        prompt.SetActive(false);
        nearbyPlayer.ActivateTripleJump(120f);
        rewardPanel.SetActive(true);
        timerPanel.SetActive(true);
        chestHideTime = Time.time + 1.1f;
        rewardHideTime = Time.time + 2.5f;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
        if (player == null)
            return;
        nearbyPlayer = player;
        if (!opened)
            prompt.SetActive(true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (opened || nearbyPlayer == null || other.GetComponentInParent<PlayerController2D>() != nearbyPlayer)
            return;
        nearbyPlayer = null;
        prompt.SetActive(false);
    }

    private void BuildUi(Transform canvas, Sprite potion)
    {
        prompt = MakePanel(canvas, "Chest Open Prompt", new Vector2(0.5f, 0.28f), new Vector2(0, 0), new Vector2(220, 58));
        MakeText(prompt.transform, "F   OPEN", 27, Color.white);
        prompt.SetActive(false);

        rewardPanel = MakePanel(canvas, "Potion Reward", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 210));
        Image rewardIcon = MakeImage(rewardPanel.transform, potion, new Vector2(-170, 0), new Vector2(135, 135));
        Text reward = MakeText(rewardPanel.transform, "ITEM ACQUIRED\nTRIPLE JUMP POTION\n2 MINUTES", 25,
            new Color(1f, 0.86f, 0.28f));
        SetRect(reward.rectTransform, new Vector2(70, 0), new Vector2(330, 150));
        rewardPanel.SetActive(false);

        timerPanel = MakePanel(canvas, "Triple Jump Timer", new Vector2(0, 0), new Vector2(190, 66), new Vector2(350, 96));
        RectTransform timerRect = timerPanel.GetComponent<RectTransform>();
        timerRect.pivot = new Vector2(0.5f, 0.5f);
        MakeImage(timerPanel.transform, potion, new Vector2(-125, 0), new Vector2(72, 72));
        timerText = MakeText(timerPanel.transform, "TRIPLE JUMP  2:00", 23, Color.white);
        SetRect(timerText.rectTransform, new Vector2(45, 0), new Vector2(250, 60));
        timerPanel.SetActive(false);
    }

    private static GameObject MakePanel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        panel.GetComponent<Image>().color = new Color(0.06f, 0.04f, 0.1f, 0.9f);
        return panel;
    }

    private static Text MakeText(Transform parent, string content, int size, Color color)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        SetRect(text.rectTransform, Vector2.zero, parent.GetComponent<RectTransform>().sizeDelta);
        return text;
    }

    private static Image MakeImage(Transform parent, Sprite sprite, Vector2 position, Vector2 size)
    {
        GameObject imageObject = new GameObject("Potion Icon", typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        SetRect(image.rectTransform, position, size);
        return image;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

}
