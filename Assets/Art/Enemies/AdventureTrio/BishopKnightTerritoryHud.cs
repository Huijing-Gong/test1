using UnityEngine;

// Display only: encounter state, health, input and pause remain owned by the
// Knight, Health and the existing overworld flow.
[DisallowMultipleComponent]
public sealed class BishopKnightTerritoryHud : MonoBehaviour
{
    BishopKnightAI knight;
    BaselineFlow flow;
    GUIStyle title, subtitle, status;
    readonly GUIContent titleContent = new GUIContent();
    readonly GUIContent subtitleContent = new GUIContent("TERRITORY GUARDIAN");
    readonly GUIContent statusContent = new GUIContent();
    string cachedName, cachedStatus;

    public bool Visible
    {
        get
        {
            if (!isActiveAndEnabled || !knight || !knight.HudEligible || !flow ||
                flow.gameObject.scene != gameObject.scene || !flow.isActiveAndEnabled ||
                !flow.Started || flow.WorldHudHidden || flow.BlockWorldInput) return false;
            var travel = SideWorldTravel.Instance;
            return !travel || (!travel.IsAway && !travel.Busy);
        }
    }

    public static BishopKnightTerritoryHud Ensure(BishopKnightAI owner)
    {
        var hud = owner.GetComponent<BishopKnightTerritoryHud>();
        if (!hud) hud = owner.gameObject.AddComponent<BishopKnightTerritoryHud>();
        hud.knight = owner;
        return hud;
    }

    void Awake()
    {
        knight = GetComponent<BishopKnightAI>();
        foreach (var candidate in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { flow = candidate; break; }
        RefreshContent();
    }

    void LateUpdate() { RefreshContent(); }

    void RefreshContent()
    {
        if (!knight) return;
        subtitleContent.text = knight.Enraged ? "AWAKENED · TWO-HIT PURSUIT" : "TERRITORY GUARDIAN";
        string name = string.IsNullOrWhiteSpace(knight.TerritoryName) ? "Silver Sentinel" : knight.TerritoryName;
        if (cachedName != name)
        {
            cachedName = name;
            titleContent.text = name.ToUpperInvariant();
        }
        string nextStatus = knight.TerritoryStatus;
        if (cachedStatus != nextStatus)
        {
            cachedStatus = nextStatus;
            statusContent.text = nextStatus;
        }
    }

    void Styles()
    {
        if (title != null) return;
        title = new GUIStyle(GUI.skin.label) {
            fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset()
        };
        subtitle = new GUIStyle(title) { fontSize = 10, fontStyle = FontStyle.Normal };
        status = new GUIStyle(subtitle) { fontSize = 11, fontStyle = FontStyle.Bold };
        title.normal.textColor = WayfarerUITheme.Paper;
        subtitle.normal.textColor = WayfarerUITheme.Gold;
        status.normal.textColor = new Color32(214, 225, 202, 255);
        WayfarerUITheme.KeepLabelColor(title);
        WayfarerUITheme.KeepLabelColor(subtitle);
        WayfarerUITheme.KeepLabelColor(status);
    }

    static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }

    void OnGUI()
    {
        if (!Visible || Event.current.type != EventType.Repaint) return;
        Matrix4x4 savedMatrix = GUI.matrix;
        Color savedColor = GUI.color, savedContentColor = GUI.contentColor;
        GUISkin savedSkin = GUI.skin;
        int savedDepth = GUI.depth;
        try
        {
            WayfarerUITheme.Apply();
            Styles();
            // Scale both dimensions together and retain physical screen safe
            // margins. The top centre leaves the minimap and lower gear HUD clear.
            float scale = Mathf.Max(.01f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Rect safe = Screen.safeArea;
            float safeX = safe.x / scale, safeWidth = safe.width / scale;
            float width = Mathf.Min(540, safeWidth - 44);
            if (width <= 0) return;
            float x = safeX + (safeWidth - width) * .5f;
            float y = (Screen.height - safe.yMax) / scale + 22;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.depth = -12;
            GUI.contentColor = Color.white;

            Fill(new Rect(x + 3, y + 4, width, 93), new Color(0, .03f, .03f, .28f));
            Fill(new Rect(x, y, width, 93), new Color32(17, 49, 44, 235));
            Fill(new Rect(x + 12, y, width - 24, 1), new Color32(186, 155, 87, 215));
            Fill(new Rect(x + 12, y + 92, width - 24, 1), new Color32(186, 155, 87, 150));
            Fill(new Rect(x, y + 8, 2, 77), new Color32(87, 121, 105, 205));
            Fill(new Rect(x + width - 2, y + 8, 2, 77), new Color32(87, 121, 105, 205));

            GUI.color = Color.white;
            GUI.Label(new Rect(x + 20, y + 7, width - 40, 26), titleContent, title);
            GUI.Label(new Rect(x + 20, y + 32, width - 40, 15), subtitleContent, subtitle);

            Rect rail = new Rect(x + 24, y + 51, width - 48, 13);
            Fill(rail, new Color32(186, 155, 87, 190));
            Rect inside = new Rect(rail.x + 1, rail.y + 1, rail.width - 2, rail.height - 2);
            Fill(inside, new Color32(9, 28, 28, 255));
            Fill(new Rect(inside.x, inside.y, inside.width * Mathf.Clamp01(knight.HealthNormalized), inside.height),
                knight.Enraged ? new Color32(229, 179, 83, 255) : new Color32(210, 215, 174, 255));
            Fill(new Rect(inside.x, inside.y, inside.width * Mathf.Clamp01(knight.HealthNormalized), 2),
                new Color32(245, 233, 198, 190));
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 20, y + 69, width - 40, 17), statusContent, status);
        }
        finally
        {
            GUI.matrix = savedMatrix;
            GUI.color = savedColor;
            GUI.contentColor = savedContentColor;
            GUI.skin = savedSkin;
            GUI.depth = savedDepth;
        }
    }
}
