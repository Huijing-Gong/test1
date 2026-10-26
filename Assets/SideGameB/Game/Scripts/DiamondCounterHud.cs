using UnityEngine;
using UnityEngine.UI;

public sealed class DiamondCounterHud : MonoBehaviour
{
    public static DiamondCounterHud Instance { get; private set; }

    [SerializeField] private Text counterText;
    private int count;
    private int total;

    public int CollectedCount => count;
    public int TotalCount => total;
    public bool HasCollectedAll => total > 0 && count >= total;

    public void Configure(Text text)
    {
        counterText = text;
        count = 0;
        CountDiamonds();
        Refresh();
    }

    private void Awake()
    {
        Instance = this;
        EnsureCounterAlwaysFits();
        CountDiamonds();
        Refresh();
    }

    public void AddDiamond()
    {
        count++;
        Refresh();
    }

    private void Refresh()
    {
        if (counterText != null)
        {
            EnsureCounterAlwaysFits();
            counterText.text = $"{count} / {total}";
        }
    }

    private void EnsureCounterAlwaysFits()
    {
        if (counterText == null) return;
        RectTransform rect = counterText.rectTransform;
        rect.sizeDelta = new Vector2(140f, Mathf.Max(48f, rect.sizeDelta.y));
        rect.anchoredPosition = new Vector2(-16f, rect.anchoredPosition.y);
        rect.pivot = new Vector2(1f, 1f);
        counterText.alignment = TextAnchor.MiddleRight;
        counterText.horizontalOverflow = HorizontalWrapMode.Overflow;
        counterText.verticalOverflow = VerticalWrapMode.Overflow;
    }

    private void CountDiamonds()
    {
        total = 0;
        foreach (DiamondCollectible diamond in FindObjectsByType<DiamondCollectible>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (diamond != null && diamond.gameObject.scene == gameObject.scene &&
                diamond.gameObject.activeInHierarchy)
                total++;
        }
    }
}
