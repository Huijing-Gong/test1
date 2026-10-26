using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// A lightweight quick view of gear already recorded by the baseline save.
[DisallowMultipleComponent]
public sealed class BaselineQuickGearBar : MonoBehaviour
{
    struct Item
    {
        public string name, description, detail, mark;
        public Texture icon;
        public int equipmentIndex;
        public bool equipped;
    }

    readonly List<Item> items = new List<Item>();
    readonly Dictionary<Vector3Int, Texture2D> roundedMasks = new Dictionary<Vector3Int, Texture2D>();
    BaselineFlow flow;
    BaselineWorld world;
    BaselineLoadout loadout;
    Texture swordIcon;
    Texture[] equipmentIcons;
    Texture2D pixel;
    GUIStyle title, hint, tooltipTitle, tooltipBody, glyph;
    bool open;
    bool confirmingUnequip;
    int pendingEquipmentIndex;
    string pendingName;
    float motion;
    ThirdPersonCamera look;
    CursorLockMode previousCursorLock;
    bool previousCursorVisible;

    public void Initialize(BaselineFlow owner, BaselineWorld host)
    {
        flow = owner;
        world = host;
        loadout = world && world.player ? world.player.GetComponent<BaselineLoadout>() : null;
        look = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        swordIcon = Resources.Load<Texture2D>("P3Rewards/PracticeSwordIcon");
        equipmentIcons = new Texture[] {
            Resources.Load<Texture2D>("P3Rewards/IslandRelicIconV6"),
            Resources.Load<Texture2D>("P3Rewards/RidgeCharmIconV6"),
            Resources.Load<Texture2D>("P3Rewards/RunicGuardIconV6"),
            Resources.Load<Texture2D>("FinalEquipment/Sword"),
            Resources.Load<Texture2D>("FinalEquipment/WaterPendant") };
        pixel = Texture2D.whiteTexture;
    }

    void OnDestroy()
    {
        if (open && flow && !flow.BlockWorldInput) SetOpen(false, true);
        foreach (var mask in roundedMasks.Values) if (mask) Destroy(mask);
        roundedMasks.Clear();
    }

    void Update()
    {
        if (!flow || !world) return;
        bool blocked = !flow.Started || flow.WorldHudHidden || flow.MenuOpen || flow.MapOpen || flow.VictoryOpen ||
            flow.BlockWorldInput || world.JournalOpen ||
            (SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy));
        if (blocked && open) SetOpen(false, false);
        else if (!blocked && WorldInputBindings.Pressed("Bag"))
        {
            if (confirmingUnequip) confirmingUnequip = false;
            else SetOpen(!open, true);
        }
        if (confirmingUnequip && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            confirmingUnequip = false;
        motion = Mathf.MoveTowards(motion, open ? 1f : 0f, Time.unscaledDeltaTime * 6f);
    }

    void SetOpen(bool value, bool restoreGameplayCursor)
    {
        if (open == value) return;
        open = value;
        if (!open) confirmingUnequip = false;
        if (open)
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            if (look) look.SetLookInputLocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (restoreGameplayCursor)
        {
            if (look) look.SetLookInputLocked(false);
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
        // When a modal menu takes over, BaselineFlow owns the cursor and camera lock.
    }

    public bool TryInterfaceBack()
    {
        if(!open)return false;
        if(confirmingUnequip)confirmingUnequip=false;else SetOpen(false,true);
        return true;
    }
    public void CloseForTransition()
    {
        if (open) SetOpen(false, true);
    }

    void RefreshItems()
    {
        items.Clear();
        if (GameSession.Instance && GameSession.Instance.HasSword)
            items.Add(new Item { name = "Practice sword", description = "A dependable blade for the first steps of your journey.", detail = GameSession.Instance.SwordEquipped ?
                "Equipped · click to remove" : "Unequipped · click to equip", mark = "I", icon = swordIcon,
                equipmentIndex = -1, equipped = GameSession.Instance.SwordEquipped });
        if (!loadout) return;
        for (int i = 0; i < 5; i++)
        {
            if (!loadout.OwnsEquipment(i)) continue;
            string name = loadout.EquipmentName(i);
            items.Add(new Item {
                name = name,
                description = loadout.EquipmentDescription(i),
                detail = loadout.EquipmentEffect(i) + " · " +
                    (loadout.IsEquipped(i) ? "Equipped" : "Unequipped"),
                mark = (i + 1).ToString(), icon = equipmentIcons != null && i < equipmentIcons.Length ? equipmentIcons[i] : null,
                equipmentIndex = i, equipped = loadout.IsEquipped(i)
            });
        }
    }

    void Styles()
    {
        if (title != null) return;
        title = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        hint = new GUIStyle(title) { fontSize = 12 };
        tooltipTitle = new GUIStyle(title) { fontSize = 17, alignment = TextAnchor.MiddleLeft };
        tooltipBody = new GUIStyle(title) { fontSize = 12, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft, wordWrap = true };
        glyph = new GUIStyle(title) { fontSize = 26 };
        title.normal.textColor = tooltipTitle.normal.textColor = glyph.normal.textColor = new Color32(248, 238, 208, 255);
        hint.normal.textColor = new Color32(245, 232, 196, 255);
        tooltipBody.normal.textColor = new Color32(205, 225, 202, 255);
    }

    void Fill(Rect r, Color color)
    {
        var previous = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, color.a * motion);
        GUI.DrawTexture(r, pixel);
        GUI.color = previous;
    }

    Texture2D RoundedMask(int width, int height, int radius)
    {
        var key = new Vector3Int(width, height, radius);
        if (roundedMasks.TryGetValue(key, out var cached)) return cached;
        var mask = new Texture2D(width, height, TextureFormat.RGBA32, false);
        mask.filterMode = FilterMode.Bilinear;
        mask.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[width * height];
        float r = Mathf.Min(radius, height * .5f, width * .5f);
        for (int py = 0; py < height; py++) for (int px = 0; px < width; px++)
        {
            float cx = Mathf.Clamp(px + .5f, r, width - r);
            float cy = Mathf.Clamp(py + .5f, r, height - r);
            float distance = Vector2.Distance(new Vector2(px + .5f, py + .5f), new Vector2(cx, cy));
            pixels[py * width + px] = new Color32(255, 255, 255,
                (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(r + .5f - distance)));
        }
        mask.SetPixels32(pixels);
        mask.Apply(false, true);
        roundedMasks[key] = mask;
        return mask;
    }

    void Rounded(Rect r, float radius, Color color)
    {
        if (r.width <= 0 || r.height <= 0) return;
        Color previous = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, color.a * motion);
        GUI.DrawTexture(r, RoundedMask(Mathf.CeilToInt(r.width), Mathf.CeilToInt(r.height), Mathf.CeilToInt(radius)));
        GUI.color = previous;
    }

    void Frame(Rect r, Color color, float thickness = 1f)
    {
        Fill(new Rect(r.x, r.y, r.width, thickness), color);
        Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
        Fill(new Rect(r.x, r.y, thickness, r.height), color);
        Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
    }

    void OnGUI()
    {
        if (motion <= .001f || !flow || !flow.Started || flow.WorldHudHidden || flow.MenuOpen || flow.MapOpen || flow.VictoryOpen ||
            flow.BlockWorldInput || world.JournalOpen ||
            (SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy))) return;
        Styles();
        RefreshItems();
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale, height = Screen.height / scale;
        int count = items.Count;
        float barWidth = Mathf.Max(88f, 26f + count * 61f + Mathf.Max(0, count - 1) * 7f);
        float x = (width - barWidth) * .5f, y = height - 128f + (1f - motion) * 24f;
        var tint = new Color(1, 1, 1, motion);
        GUI.color = tint;
        // A quiet translucent backing: the individual wells carry the structure.
        Rounded(new Rect(x, y + 7, barWidth, 60), 12, new Color(.03f, .15f, .15f, .34f));
        int hover = -1;
        // IMGUI applies GUI.matrix to its mouse coordinates as well as drawing.
        Vector2 mouse = Event.current.mousePosition;
        float slotsWidth = count * 61f + Mathf.Max(0, count - 1) * 7f;
        float slotStart = x + (barWidth - slotsWidth) * .5f;
        for (int i = 0; i < count; i++)
        {
            var slot = new Rect(slotStart + i * 68, y + 11, 61, 51);
            bool selected = open && !confirmingUnequip && slot.Contains(mouse);
            if (selected) hover = i;
            Rounded(slot, 9, selected ? new Color(.70f, .76f, .57f, .58f) : new Color(.81f, .82f, .64f, .28f));
            Rounded(new Rect(slot.x + 1, slot.y + 1, slot.width - 2, slot.height - 2), 8,
                selected ? new Color(.07f, .31f, .28f, .75f) : new Color(.04f, .20f, .20f, .62f));
            Fill(new Rect(slot.x + 12, slot.yMax - 4, slot.width - 24, 1), new Color(.85f, .76f, .49f, selected ? .72f : .35f));
            // The icon art sits in a square centered vertically in the 51-pixel well.
            float iconFloat = items[i].equipped ? Mathf.Sin(Time.unscaledTime * 2.1f) * 1.5f : 0f;
            if (items[i].icon) GUI.DrawTexture(new Rect(slot.x + 9, slot.y + 5 + iconFloat, 43, 41), items[i].icon, ScaleMode.ScaleToFit, true);
            else GUI.Label(slot, items[i].mark, glyph);
            if (!items[i].equipped) Rounded(new Rect(slot.x + 3, slot.y + 3, slot.width - 6, slot.height - 6), 7,
                new Color(.02f, .11f, .11f, .42f));
            if (open && !confirmingUnequip && GUI.Button(slot, GUIContent.none, GUIStyle.none))
            {
                if (items[i].equipped)
                {
                    pendingEquipmentIndex = items[i].equipmentIndex;
                    pendingName = items[i].name;
                    confirmingUnequip = true;
                }
                else if (items[i].equipmentIndex < 0) flow.TogglePracticeSword();
                else flow.ToggleEquipment(items[i].equipmentIndex);
            }
        }
        Rounded(new Rect(width * .5f - 69, y + 81, 17, 17), 3, new Color(.88f, .80f, .60f, .88f));
        var keyStyle = new GUIStyle(hint) { fontSize = 10 };
        keyStyle.normal.textColor = new Color32(28, 65, 53, 255);
        GUI.Label(new Rect(width * .5f - 69, y + 81, 17, 17), "B", keyStyle);
        GUI.Label(new Rect(width * .5f - 46, y + 78, 125, 22), "CLOSE BACKPACK", hint);
        if (hover >= 0)
        {
            var item = items[hover];
            const float tooltipWidth=224f, innerWidth=200f;
            float titleHeight=Mathf.Max(23,tooltipTitle.CalcHeight(new GUIContent(item.name),innerWidth));
            float descriptionHeight=Mathf.Max(20,tooltipBody.CalcHeight(new GUIContent(item.description),innerWidth));
            float detailHeight=Mathf.Max(20,tooltipBody.CalcHeight(new GUIContent(item.detail),innerWidth));
            float tooltipHeight=12+titleHeight+descriptionHeight+detailHeight+13;
            float tx = Mathf.Clamp(slotStart + hover * 68 - 59, 18, width - tooltipWidth - 18);
            float ty = y - tooltipHeight - 10;
            Rounded(new Rect(tx, ty, tooltipWidth, tooltipHeight), 6, new Color(.07f, .27f, .24f, .96f));
            Frame(new Rect(tx,ty,tooltipWidth,tooltipHeight),new Color(.77f,.65f,.39f,.72f));
            float textY=ty+8;
            GUI.Label(new Rect(tx+12,textY,innerWidth,titleHeight),item.name,tooltipTitle); textY+=titleHeight+3;
            GUI.Label(new Rect(tx+12,textY,innerWidth,descriptionHeight),item.description,tooltipBody); textY+=descriptionHeight+3;
            GUI.Label(new Rect(tx+12,textY,innerWidth,detailHeight),item.detail,tooltipBody);
        }
        if (confirmingUnequip)
        {
            var panel = new Rect(width * .5f - 190, height * .5f - 85, 380, 170);
            Rounded(new Rect(0, 0, width, height), 0, new Color(.01f, .06f, .06f, .42f));
            Rounded(panel, 12, new Color(.06f, .22f, .20f, .98f));
            Frame(panel, new Color(.77f, .65f, .39f, .9f), 2);
            GUI.Label(new Rect(panel.x + 20, panel.y + 19, 340, 31), "UNEQUIP ITEM?", tooltipTitle);
            GUI.Label(new Rect(panel.x + 20, panel.y + 54, 340, 30), pendingName, tooltipBody);
            if (GUI.Button(new Rect(panel.x + 20, panel.y + 110, 162, 40), "Cancel"))
                confirmingUnequip = false;
            if (GUI.Button(new Rect(panel.x + 198, panel.y + 110, 162, 40), "Unequip"))
            {
                bool changed = pendingEquipmentIndex < 0 ? flow.TogglePracticeSword() : flow.ToggleEquipment(pendingEquipmentIndex);
                if (changed) confirmingUnequip = false;
            }
        }
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }
}
