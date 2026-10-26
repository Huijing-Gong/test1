using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// North-up local window into BaselineMap's shared scene capture; no second camera.
public sealed class BaselineMinimap : MonoBehaviour
{
    public const float Diameter = 132f;
    public const float TopInset = 30f;
    [SerializeField, Min(10)] float visibleWorldRadius = 34f;
    BaselineFlow flow;
    BaselineWorld world;
    BaselineMap atlas;
    DialogueUI dialogue;
    GameObject canvasObject;
    RectTransform terrainRect, playerRect, taskRect, taskHalo;
    RawImage terrain;
    RectTransform waypointRect, waypointHalo;
    RawImage waypointImage;
    Text taskDistance;
    Font font;
    Vector2 viewCenterUV = Vector2.one * .5f;
    readonly List<Object> ownedAssets = new List<Object>();
    readonly List<Marker> markers = new List<Marker>();
    sealed class Marker
    {
        public RectTransform rect;
        public Transform target;
        public BaselineWaystation station;
        public Vector3 fixedPosition;
        public bool followsTarget;
    }

    public void Initialize(BaselineFlow owner, BaselineWorld host, BaselineMap map)
    {
        flow = owner; world = host; atlas = map;
        if (canvasObject) return;
        atlas.Prepare(owner);
        foreach (var candidate in FindObjectsByType<DialogueUI>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { dialogue = candidate; break; }
        Build();
        canvasObject.SetActive(false);
    }

    void Build()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        canvasObject = new GameObject("Local minimap HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
        var root = Rect("Minimap", canvasObject.transform, new Vector2(Diameter, Diameter));
        root.anchorMin = root.anchorMax = new Vector2(0, 1);
        root.anchoredPosition = new Vector2(Diameter / 2 + 24, -Diameter / 2 - TopInset);

        var maskRect = Rect("Circular viewport", root, Vector2.one * Diameter);
        var maskImage = maskRect.gameObject.AddComponent<Image>();
        var disc = DiscTexture(false);
        var sprite = Sprite.Create(disc, new Rect(0, 0, disc.width, disc.height), Vector2.one * .5f);
        ownedAssets.Add(sprite); maskImage.sprite = sprite; maskImage.raycastTarget = false;
        maskRect.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var background = Rect("Beyond map boundary", maskRect, Vector2.one * Diameter).gameObject.AddComponent<Image>();
        background.color = new Color32(16, 58, 59, 255); background.raycastTarget = false;
        terrainRect = Rect("Shared terrain", maskRect, Vector2.one * Diameter);
        terrain = terrainRect.gameObject.AddComponent<RawImage>();
        terrain.texture = atlas.TerrainTexture; terrain.raycastTarget = false;

        var layout = flow.GetComponent<OverworldV2Layout>();
        var camp = Icon("Camp", maskRect, atlas.CampMarkerTexture, 21);
        markers.Add(new Marker { rect = camp, fixedPosition = layout ? layout.CampPosition : BaselineSaveData.DefaultSafePosition });
        foreach (var entry in FindObjectsByType<SideWorldEntrance>(FindObjectsSortMode.None))
        {
            if (entry.gameObject.scene != gameObject.scene) continue;
            var marker = Icon(entry.isSideWorldB ? "Portal B" : "Portal A", maskRect, atlas.PortalMarkerTexture(entry.isSideWorldB), 23);
            TextAt(entry.isSideWorldB ? "B" : "A", marker, new Vector2(18, 0), new Vector2(15, 18), 11);
            markers.Add(new Marker { rect = marker, target = entry.transform, followsTarget = true });
        }
        if (flow.endingEntrance)
        {
            var tower = TextAt("TOWER", maskRect, Vector2.zero, new Vector2(50, 20), 10);
            markers.Add(new Marker { rect = tower.rectTransform, target = flow.endingEntrance, followsTarget = true });
        }
        foreach (var station in FindObjectsByType<BaselineWaystation>(FindObjectsSortMode.None))
        {
            if (station.gameObject.scene != gameObject.scene) continue;
            var marker = TextAt(station.OffersSupplies ? "+" : "R", maskRect, Vector2.zero, new Vector2(20, 20), 15);
            marker.color = new Color32(92, 221, 198, 255);
            markers.Add(new Marker { rect = marker.rectTransform, target = station.transform, followsTarget = true, station = station });
        }

        taskHalo = Icon("Tracked objective halo", maskRect, disc, 32);
        taskHalo.GetComponent<RawImage>().color = new Color32(255, 185, 58, 65);
        taskRect = Rect("Tracked objective", maskRect, Vector2.one * 16);
        var task = taskRect.gameObject.AddComponent<Image>(); task.raycastTarget = false;
        task.color = new Color32(255, 198, 73, 255);
        taskRect.localEulerAngles = new Vector3(0, 0, 45);
        var centre = Rect("Objective centre", taskRect, Vector2.one * 8).gameObject.AddComponent<Image>();
        centre.color = new Color32(35, 60, 47, 255); centre.raycastTarget = false;
        playerRect = Icon("You", maskRect, atlas.PlayerMarkerTexture, 28);
        waypointHalo=Icon("Selected destination halo",maskRect,disc,35);
        waypointHalo.GetComponent<RawImage>().color=new Color(1f,.78f,.3f,.3f);
        waypointRect=Icon("Selected destination",maskRect,atlas.PlayerMarkerTexture,25);
        waypointImage=waypointRect.GetComponent<RawImage>();
        playerRect.SetAsLastSibling();
        Icon("Cream and gold rim", root, DiscTexture(true), Diameter + 8);
        var north = Icon("North pointer", root, NorthTexture(), 30);
        north.anchoredPosition = new Vector2(0, Diameter * .5f + 1);
        TextAt("N", north, new Vector2(0, -3), new Vector2(24, 22), 13);
        taskDistance = TextAt("M  ·  MAP", root, new Vector2(0, -Diameter * .5f - 13), new Vector2(132, 18), 10);
    }

    void LateUpdate()
    {
        if (!canvasObject || !flow || !world || !world.player) return;
        var travel = SideWorldTravel.Instance;
        bool visible = flow.isActiveAndEnabled && flow.Started && !flow.WorldHudHidden &&
            !(flow.endingRoom && flow.endingRoom.IsInside) &&
            (!travel || (!travel.IsAway && !travel.Busy));
        canvasObject.SetActive(visible);
        if (!visible) return;
        float span = atlas.WorldDistanceToAtlasUV(visibleWorldRadius * 2f);
        float atlasSize = Diameter / Mathf.Max(.001f, span);
        Vector2 playerUV = atlas.WorldToAtlasUV(world.player.position);
        viewCenterUV = atlas.ClampLocalViewCenter(playerUV, span);
        terrain.texture = atlas.TerrainTexture;
        terrainRect.sizeDelta = Vector2.one * atlasSize;
        terrainRect.anchoredPosition = (Vector2.one * .5f - viewCenterUV) * atlasSize;
        playerRect.anchoredPosition = Vector2.ClampMagnitude(LocalPoint(world.player.position), Diameter * .5f - 18f);
        playerRect.localEulerAngles = new Vector3(0, 0, -world.player.eulerAngles.y);
        foreach (var marker in markers)
        {
            bool available = !marker.followsTarget || (marker.target && marker.target.gameObject.activeInHierarchy);
            if (marker.station) available &= marker.station.isActiveAndEnabled;
            Vector3 position = marker.station ? marker.station.InteractionPosition : marker.target ? marker.target.position : marker.fixedPosition;
            Vector2 offset = LocalPoint(position);
            marker.rect.gameObject.SetActive(available && offset.magnitude < Diameter * .5f - 17);
            marker.rect.anchoredPosition = offset;
        }
        int tracked = world.GuidingTask;
        bool hasTask = world.objectives != null && tracked >= 0 && tracked < world.objectives.Length && world.objectives[tracked];
        taskRect.gameObject.SetActive(hasTask); taskHalo.gameObject.SetActive(hasTask);
        taskDistance.text = "M  ·  MAP";
        waypointRect.gameObject.SetActive(atlas.HasWaypoint);
        waypointHalo.gameObject.SetActive(atlas.HasWaypoint);
        if(atlas.HasWaypoint)
        {
            Vector2 offset=LocalPoint(atlas.WaypointPosition);
            waypointRect.anchoredPosition=waypointHalo.anchoredPosition=Vector2.ClampMagnitude(offset,Diameter*.5f-18);
            waypointImage.texture=atlas.WaypointIcon;
            Vector3 delta=atlas.WaypointPosition-world.player.position; delta.y=0;

        }
        if (!hasTask) return;
        Vector3 objective = world.objectives[tracked].position;
        Vector2 taskOffset = LocalPoint(objective);
        bool beyond = taskOffset.magnitude > Diameter * .5f - 16;
        Vector2 pinned = Vector2.ClampMagnitude(taskOffset, Diameter * .5f - 16);
        taskRect.anchoredPosition = taskHalo.anchoredPosition = pinned;
        float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 3f);
        taskHalo.localScale = Vector3.one * Mathf.Lerp(.85f, 1.2f, pulse);
        taskHalo.GetComponent<RawImage>().color = new Color(1f, .72f, .23f, Mathf.Lerp(.16f, .38f, pulse));
        Vector3 taskDelta=objective-world.player.position;taskDelta.y=0;

    }

    Vector2 LocalPoint(Vector3 position) => (atlas.WorldToAtlasUV(position) - viewCenterUV) *
        (Diameter / atlas.WorldDistanceToAtlasUV(visibleWorldRadius * 2f));

    static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = size; return rect;
    }
    static RectTransform Icon(string name, Transform parent, Texture texture, float size)
    {
        var rect = Rect(name, parent, Vector2.one * size);
        var image = rect.gameObject.AddComponent<RawImage>(); image.texture = texture; image.raycastTarget = false;
        return rect;
    }
    Text TextAt(string value, Transform parent, Vector2 position, Vector2 size, int fontSize)
    {
        var rect = Rect(value.Length > 0 ? value : "Map status", parent, size); rect.anchoredPosition = position;
        var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = fontSize;
        text.text = value; text.alignment = TextAnchor.MiddleCenter; text.fontStyle = FontStyle.Bold;
        text.color = new Color32(255, 244, 209, 255); text.raycastTarget = false;
        var outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = new Color32(9, 40, 42, 255); outline.effectDistance = new Vector2(1, -1);
        return text;
    }
    Texture2D DiscTexture(bool rim)
    {
        const int size = 256;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float r = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * 128);
            Color c = Color.white;
            if (rim)
                c = r < 117 ? Color.clear : r < 119 ? new Color32(245, 174, 41, 255) :
                    r < 123 ? new Color32(252, 240, 199, 255) : new Color32(8, 45, 47, 255);
            c.a *= Mathf.Clamp01(127.5f - r);
            pixels[y * size + x] = c;
        }
        return Texture(pixels, size, rim ? "Minimap rim" : "Minimap circular mask");
    }
    Texture2D NorthTexture()
    {
        const int size = 64;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float half = (60 - y) * .52f;
            float d = Mathf.Abs(x - 31.5f);
            if (y < 3 || y > 60 || d > half) continue;
            pixels[y * size + x] = y < 7 || d > half - 4 ? new Color32(8, 45, 47, 255) :
                y < 12 || d > half - 8 ? new Color32(252, 240, 199, 255) : new Color32(8, 45, 47, 255);
        }
        return Texture(pixels, size, "Minimap north badge");
    }
    Texture2D Texture(Color32[] pixels, int size, string name)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        texture.SetPixels32(pixels); texture.Apply(false, true); ownedAssets.Add(texture); return texture;
    }
    void OnDisable() { if (canvasObject) canvasObject.SetActive(false); }
    void OnDestroy()
    {
        if (canvasObject) Destroy(canvasObject);
        foreach (var asset in ownedAssets) if (asset) Destroy(asset);
    }
}
