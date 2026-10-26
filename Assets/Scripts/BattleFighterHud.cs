using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Every portrait has positive scale and a front-view asset. Never reacts to
// the actor's facing direction, camera angle, or a left/right slot assignment.
public sealed class BattleFighterHud : MonoBehaviour
{
    public RectTransform Rect => (RectTransform)transform;
    public Image Portrait { get; private set; }
    public TMP_Text Damage { get; private set; }
    public TMP_Text Label { get; private set; }
    public BattleTeamHud.Fighter Binding { get; private set; }
    RectTransform face, panel, marker, star, numberRoot;
    TMP_Text caption;
    RectTransform stockRoot;
    TMP_Text stockLabel;
    readonly BattleHudShape[] stockPips = new BattleHudShape[2];
    BattleStockLives stocks;
    BattleHudShape launchIcon;
    readonly BattleHudShape[,] shards = new BattleHudShape[2, 4];
    readonly float[] brokenAt = { -100, -100 };
    int previousLives = -1;
    float stockLostAt = -100;
    public int DisplayedLives { get; private set; } = 2;
    bool right;
    float previous = -1, impactAt = -100;
    float presentationClock,impactPower;
    static readonly Color Ink = new Color32(19, 55, 49, 255);
    static readonly Color Cream = new Color32(247, 236, 205, 255);
    static readonly Color Gold = new Color32(234, 190, 85, 255);
    public void Build(BattleTeamHud owner, BattleTeamHud.Fighter binding, bool outerRight)
    {
        Binding = binding; right = outerRight;
        panel = BattleTeamHud.Shape("Status plate", transform, Vector2.zero, Vector2.zero, new Vector2(0, 13),
            new Vector2(390, 118), BattleHudShape.Kind.Card, Ink, Cream);
        panel.GetComponent<BattleHudShape>().mirrored = right;
        face = BattleTeamHud.Shape("Portrait cream rim", transform, Vector2.zero, Vector2.zero, new Vector2(0, 13),
            new Vector2(118, 118), BattleHudShape.Kind.Circle, binding.identity, Cream, 3.5f);
        var mask = BattleTeamHud.Shape("Portrait clip", face, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            Vector2.zero, new Vector2(106, 106), BattleHudShape.Kind.Circle, binding.identity * .7f, Ink, 0);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var picture = BattleTeamHud.Rect("Front-facing portrait", mask, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            Vector2.zero, new Vector2(106, 106));
        Portrait = picture.gameObject.AddComponent<Image>(); Portrait.sprite = binding.portrait;
        Portrait.preserveAspect = true; Portrait.raycastTarget = false;
        numberRoot = BattleTeamHud.Rect("Damage readout", transform, Vector2.zero, Vector2.zero, new Vector2(170, 118));
        Label = owner.Text("Fighter label", numberRoot, binding.label, 21, FontStyles.Bold, Cream,
            new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -4), new Vector2(170, 26));
        Damage = owner.Text("Damage percent", numberRoot, "0<size=58%>%</size>", 64, FontStyles.Bold, binding.numberTint,
            new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -1), new Vector2(178, 72));
        caption = owner.Text("Damage caption", numberRoot, "LAUNCH RISK", 14, FontStyles.Bold, Cream,
            new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(11, 1), new Vector2(148, 22));
        caption.characterSpacing = 1;
        launchIcon = BattleTeamHud.Shape("Knockback risk icon", numberRoot, new Vector2(.5f,0), new Vector2(.5f,.5f),
            new Vector2(-65,12), new Vector2(20,20), BattleHudShape.Kind.Launch, Cream, Cream, 0).GetComponent<BattleHudShape>();
        Damage.enableAutoSizing = true; Damage.fontSizeMin = 30; Damage.fontSizeMax = 64;
        marker = BattleTeamHud.Shape("Gold identity diamond", transform, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero,
            new Vector2(15, 22), BattleHudShape.Kind.Diamond, Gold, Gold, 0);
        marker.gameObject.SetActive(false); // Avoid a decorative third 'life'.
        stockRoot = BattleTeamHud.Rect("Stock lives", transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(64, 100));
        for (int i = 0; i < 2; i++)
        {
            stockPips[i] = BattleTeamHud.Shape("Life " + (i + 1), stockRoot, new Vector2(.5f,.5f), new Vector2(.5f,.5f),
                new Vector2(i == 0 ? -16 : 16, 12), new Vector2(23, 28), BattleHudShape.Kind.Diamond, Gold, Gold, 2).GetComponent<BattleHudShape>();
            for (int j=0;j<4;j++)
            {
                shards[i,j] = BattleTeamHud.Shape("Life fragment " + i + "-" + j, stockRoot, new Vector2(.5f,.5f),
                    new Vector2(.5f,.5f), Vector2.zero, new Vector2(8,11), BattleHudShape.Kind.Triangle, Gold, Gold, 0).GetComponent<BattleHudShape>();
                shards[i,j].gameObject.SetActive(false);
            }
        }
        stockLabel = owner.Text("Lives label", stockRoot, "LIVES", 14, FontStyles.Bold, Cream,
            new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(0,-23), new Vector2(64,23));
        stocks = binding.source ? binding.source.GetComponent<BattleStockLives>() : null;
        star = BattleTeamHud.Shape("World star medallion", transform, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero,
            new Vector2(30, 30), BattleHudShape.Kind.Circle, Ink, Cream, 2);
        BattleTeamHud.Shape("World four-point star", star, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
            new Vector2(18, 22), BattleHudShape.Kind.Star, Gold, Gold, 0);
    }
    public void Layout(float width)
    {
        panel.sizeDelta = new Vector2(width, 150);
        float diameter = Mathf.Min(156, width * .37f);
        face.sizeDelta = new Vector2(diameter, diameter);
        face.anchoredPosition = new Vector2(right ? width - diameter : 0, 20);
        var clip = (RectTransform)face.GetChild(0); clip.sizeDelta = new Vector2(diameter - 12, diameter - 12);
        // Slightly tighter head crop inside the mask; no additional texture.
        Portrait.rectTransform.sizeDelta = clip.sizeDelta * 1.24f;
        Portrait.rectTransform.anchoredPosition = new Vector2(0, -diameter * .06f);
        // Layout mirrors the plate only. The portrait is not mirrored or rotated.
        Portrait.rectTransform.localScale = Vector3.one;
        Portrait.rectTransform.localRotation = Quaternion.identity;
        float available = width - diameter - 54;
        float stockWidth = Mathf.Clamp(available * .30f, 44, 64);
        float numberWidth = available - stockWidth - 10;
        float startX = right ? 34 : diameter + 12;
        numberRoot.anchoredPosition = new Vector2(startX, 13);
        numberRoot.sizeDelta = new Vector2(numberWidth, 150);
        stockRoot.anchoredPosition = new Vector2(startX + numberWidth + 10, 37);
        stockRoot.sizeDelta = new Vector2(stockWidth, 100);
        for (int i=0;i<2;i++)
        {
            stockPips[i].rectTransform.anchoredPosition = new Vector2((i == 0 ? -1 : 1) * stockWidth * .25f, 12);
            stockPips[i].rectTransform.sizeDelta = new Vector2(stockWidth * .34f, stockWidth * .42f);
        }
        stockLabel.rectTransform.sizeDelta = new Vector2(stockWidth, 23);
        stockLabel.fontSize = Mathf.Min(14, stockWidth * .21f);
        Damage.rectTransform.sizeDelta = new Vector2(numberWidth, 98);
        Damage.rectTransform.anchoredPosition = new Vector2(0,-2);
        Label.rectTransform.sizeDelta = new Vector2(numberWidth, 28);
        caption.rectTransform.sizeDelta = new Vector2(Mathf.Max(1,numberWidth-20), 24);
        caption.rectTransform.anchoredPosition = new Vector2(10,1);
        launchIcon.rectTransform.anchoredPosition = new Vector2(-numberWidth*.5f+8,13);
        Damage.fontSize = Mathf.Min(94, numberWidth * .48f);
        Damage.fontSizeMax = Damage.fontSize;
        Label.fontSize = Mathf.Min(22, available * .14f);
        caption.fontSize = Mathf.Min(14, numberWidth * .105f);
        marker.anchoredPosition = new Vector2(right ? 23 : width - 23, 88);
        star.anchoredPosition = new Vector2(face.anchoredPosition.x + diameter * .5f, 17);
    }
    void LateUpdate()
    {
        if(Time.timeScale>0)presentationClock+=Mathf.Min(Time.unscaledDeltaTime,.033f);
        if (!Binding.source) { if (previous != -2) { Damage.text = "--"; previous = -2; } return; }
        if (!stocks) stocks = Binding.source.GetComponent<BattleStockLives>();
        DisplayedLives = stocks ? stocks.Remaining : 2;
        if (previousLives >= 0 && DisplayedLives < previousLives)
        {
            stockLostAt = presentationClock;
            for (int i=DisplayedLives;i<previousLives;i++) brokenAt[i] = presentationClock;
        }
        previousLives = DisplayedLives;
        for (int i = 0; i < 2; i++)
        {
            float ageLost = presentationClock - stockLostAt;
            float pulse = !BattlePresentationSettings.ReducedMotion && ageLost < 1.2f
                ? Mathf.Sin(Mathf.Clamp01(ageLost/1.2f)*Mathf.PI)*.18f : 0;
            stockPips[i].rectTransform.localScale = Vector3.one * (i < DisplayedLives ? 1+pulse : 1);
            stockPips[i].color = i < DisplayedLives ? Color.Lerp(Gold,Cream,ageLost < 1.2f ? .65f : 0) : Ink;
            stockPips[i].rim = i < DisplayedLives ? Gold : Color.Lerp(Ink, Cream, .45f);
            stockPips[i].SetVerticesDirty();
            float fragmentAge = presentationClock - brokenAt[i];
            bool breaking = i >= DisplayedLives && fragmentAge >= 0 && fragmentAge < .7f && !BattlePresentationSettings.ReducedMotion;
            for (int j=0;j<4;j++)
            {
                var shard = shards[i,j]; shard.gameObject.SetActive(breaking);
                if (!breaking) continue;
                float t = fragmentAge/.7f, angle = (45+j*90)*Mathf.Deg2Rad;
                shard.rectTransform.anchoredPosition = stockPips[i].rectTransform.anchoredPosition +
                    new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(5+24*t) + Vector2.down*(15*t*t);
                shard.rectTransform.localRotation = Quaternion.Euler(0,0,j*90+t*75);
                shard.color = new Color(Gold.r,Gold.g,Gold.b,1-t);
            }
        }
        stockLabel.text = stocks && stocks.IsProtected && DisplayedLives > 0 ? "SAFE" : "LIVES";
        Portrait.color = DisplayedLives == 0 ? new Color(.35f,.35f,.35f,1) : Color.white;
        if (DisplayedLives == 0) { previous = -1; Damage.text = "OUT"; Damage.color = Cream; numberRoot.localScale = Vector3.one; return; }
        float value = Binding.source.damagePercent;
        if (!Mathf.Approximately(previous, value))
        {
            if (previous >= 0 && value > previous) {impactAt = presentationClock;impactPower=Binding.source.PresentationPower;}
            previous = value; Damage.SetText("{0:0}<size=58%>%</size>", Mathf.Max(0, value));
        }
        float age = presentationClock - impactAt;
        float emphasis = age < .24f ? Mathf.Sin(Mathf.Clamp01(age / .24f) * Mathf.PI) : 0;
        numberRoot.localScale = Vector3.one * (1 + Mathf.Lerp(.055f,.08f,impactPower) * emphasis);
        Color baseColor = RiskColor(value);
        Damage.color = Color.Lerp(baseColor, Color.white, emphasis * .65f);
        launchIcon.color = caption.color = baseColor;
    }
    public static Color RiskColor(float percent)
    {
        // Risk is continuous, not a health meter or a guaranteed KO threshold.
        return percent <= 70 ? Color.Lerp(Cream, Gold, Mathf.Clamp01(percent/70f)) :
            Color.Lerp(Gold, new Color32(255,108,76,255), Mathf.Clamp01((percent-70)/90f));
    }
}
