using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Only menu buttons, with no input delay and no dependency on scaled game time.
public sealed class BattleTrialButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, IDeselectHandler
{
    Button button;
    bool over, pressed;
    RectTransform label, arrow;
    Vector2 labelHome, arrowHome;
    BattleTrialButtonSheen sheen;
    float sweepAt = -100, nextSweep;
    public bool Hovered => over && button && button.IsInteractable();
    void Awake()
    {
        button = GetComponent<Button>();
        label = transform.Find("Label") as RectTransform;
        if (label) labelHome = label.anchoredPosition;
    }
    public void AddSheen()
    {
        var go = new GameObject("Gold light sweep", typeof(RectTransform));
        go.transform.SetParent(transform, false); go.transform.SetAsFirstSibling();
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        sheen = go.AddComponent<BattleTrialButtonSheen>(); sheen.raycastTarget = false;
        go.AddComponent<Canvas>(); // Decorative only: no extra GraphicRaycaster.
        nextSweep = Time.unscaledTime + 1.5f;
    }
    public void OnPointerEnter(PointerEventData data)
    { over = true; if (button && button.IsInteractable()) sweepAt = Time.unscaledTime; }
    public void OnPointerExit(PointerEventData data) { over = false; pressed = false; }
    public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) pressed = true; }
    public void OnPointerUp(PointerEventData data) => pressed = false;
    public void OnDeselect(BaseEventData data) => pressed = false;
    void Update()
    {
        bool usable = button && button.IsInteractable();
        float amount = usable ? pressed ? -1.5f : over ? 3f : 0 : 0;
        float damping = 1f - Mathf.Exp(-24f * Time.unscaledDeltaTime);
        // Move only visual children. The button's raycast rectangle never scales
        // or moves on hover, so fast clicks near its edge remain reliable.
        if (label) label.anchoredPosition = Vector2.Lerp(label.anchoredPosition,
            labelHome + new Vector2(amount, pressed && usable ? -1 : 0), damping);
        if (!arrow)
        {
            arrow = transform.Find("Start arrow") as RectTransform;
            if (arrow) arrowHome = arrow.anchoredPosition;
        }
        if (arrow) arrow.anchoredPosition = Vector2.Lerp(arrow.anchoredPosition,
            arrowHome + Vector2.right * (Hovered ? 6 : 0), damping);
        if (!sheen) return;
        if (usable && Time.unscaledTime >= nextSweep)
        { sweepAt = Time.unscaledTime; nextSweep = Time.unscaledTime + 7.5f; }
        float sweep = (Time.unscaledTime - sweepAt) / .65f;
        sheen.SetPhase(usable && !pressed && sweep >= 0 && sweep <= 1 ? sweep : -1);
    }
    void OnDisable()
    {
        over = pressed = false;
        if (label) label.anchoredPosition = labelHome;
        if (arrow) arrow.anchoredPosition = arrowHome;
        if (sheen) sheen.SetPhase(-1);
    }
}
