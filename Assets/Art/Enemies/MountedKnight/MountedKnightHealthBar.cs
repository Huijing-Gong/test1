using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Health))]
public sealed class MountedKnightHealthBar : MonoBehaviour
{
    public Vector3 offset = new Vector3(0, 3.4f, 0);
    const float Width = 220f;
    Transform head;
    Health health;
    Canvas bar;
    Image fill, lost;
    Text number;
    Camera view;
    PlayerVitals playerVitals;
    float anchorHeight, delayedHealth, damageHold;
    bool positioned;
    int displayed = -1, displayedMaximum = -1;
    public float Fill => fill ? fill.rectTransform.rect.width / Width : 0;

    void Awake()
    {
        health = GetComponent<Health>();
        foreach (var bone in GetComponentsInChildren<Transform>(true))
            if (bone.name == "Bip01_Head") { head = bone; break; }
        anchorHeight = head ? head.position.y - transform.position.y + .38f : offset.y * transform.lossyScale.y;
        var root = new GameObject("Knight overhead health", typeof(RectTransform), typeof(Canvas));
        root.transform.SetParent(transform, false);
        // Keep the UI readable without inheriting the creature's model scale.
        Vector3 scale = transform.lossyScale;
        root.transform.localScale = new Vector3(.006f / scale.x, .006f / scale.y, .006f / scale.z);
        bar = root.GetComponent<Canvas>();
        bar.renderMode = RenderMode.WorldSpace; bar.sortingOrder = 25;
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(236, 38);
        Box("Shadow", root.transform, new Vector2(234, 16), new Color(.025f, .035f, .038f, .9f), new Vector2(0, -1));
        Box("Gold rim", root.transform, new Vector2(228, 12), new Color(.66f, .55f, .32f), Vector2.zero);
        Box("Track", root.transform, new Vector2(224, 8), new Color(.13f, .055f, .065f), Vector2.zero);
        lost = Meter("Recent damage", root.transform, new Color(.88f, .67f, .34f));
        fill = Meter("Health", root.transform, new Color(.82f, .16f, .22f));
        var glint = Box("Top glint", fill.transform, new Vector2(Width, 1), new Color(1f, .58f, .48f, .45f), new Vector2(Width / 2, 2)).rectTransform;
        glint.anchorMin = glint.anchorMax = new Vector2(0, .5f);
        Label("Name", root.transform, "IVORY WARDEN", new Vector2(-36, 17), new Vector2(152, 20), 12, TextAnchor.MiddleLeft);
        number = Label("HP", root.transform, "", new Vector2(78, 17), new Vector2(70, 20), 11, TextAnchor.MiddleRight);
        delayedHealth = health.NormalizedHealth;
    }

    static Image Box(string name, Transform parent, Vector2 size, Color color, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = size; rt.anchoredPosition = position;
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
    }
    static Image Meter(string name, Transform parent, Color color)
    {
        var image = Box(name, parent, new Vector2(Width, 6), color, new Vector2(-Width / 2, 0));
        image.rectTransform.pivot = new Vector2(0, .5f); return image;
    }
    static Text Label(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow)); go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = size; rt.anchoredPosition = position;
        var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value; text.fontSize = fontSize; text.alignment = alignment; text.color = new Color(.96f, .91f, .77f); text.raycastTarget = false;
        go.GetComponent<Shadow>().effectDistance = new Vector2(0, -1); return text;
    }
    void LateUpdate()
    {
        if (!view || !view.isActiveAndEnabled) view = Camera.main;
        if (!playerVitals) playerVitals = FindFirstObjectByType<PlayerVitals>();
        bool visible = view && !health.IsDead && Vector3.Distance(view.transform.position, transform.position) < 40;
        if (playerVitals && !playerVitals.EnemyCombatActive) visible = false;
        bar.enabled = visible; if (!visible) { positioned = false; return; }
        Vector3 desired = transform.position + Vector3.up * anchorHeight;
        if (head) desired.y = Mathf.Max(desired.y, head.position.y + .32f);
        bar.transform.position = positioned ? Vector3.Lerp(bar.transform.position, desired, 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime)) : desired;
        positioned = true; bar.transform.rotation = view.transform.rotation; bar.worldCamera = view;
        if (displayed != health.CurrentHealth || displayedMaximum != health.MaxHealth)
        {
            if (displayed > health.CurrentHealth) damageHold = .22f;
            else delayedHealth = health.NormalizedHealth;
            displayed = health.CurrentHealth; displayedMaximum = health.MaxHealth;
            number.text = displayed + " / " + displayedMaximum;
            fill.rectTransform.sizeDelta = new Vector2(Width * health.NormalizedHealth, 6);
            var glint = fill.transform.GetChild(0).GetComponent<RectTransform>();
            glint.sizeDelta = new Vector2(Width * health.NormalizedHealth, 1); glint.anchoredPosition = new Vector2(glint.sizeDelta.x / 2, 2);
        }
        damageHold -= Time.unscaledDeltaTime;
        if (damageHold <= 0) delayedHealth = Mathf.MoveTowards(delayedHealth, health.NormalizedHealth, Time.unscaledDeltaTime * .8f);
        lost.rectTransform.sizeDelta = new Vector2(Width * delayedHealth, 6);
    }
}
