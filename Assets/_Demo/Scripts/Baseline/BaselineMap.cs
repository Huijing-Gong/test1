using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
// Both maps use the same orthographic scene capture and linear X/Z projection.
public sealed class BaselineMap : MonoBehaviour
{
    const float Extent = 85f;
    [SerializeField] bool showEnemyDotsForPlaytest = true;
    BaselineFlow flow;
    SideWorldEntrance[] entrances;
    BaselineWaystation[] waystations;
    OverworldV2Layout layout;
    GUIStyle label, mapLabel, heading, subhead, eyebrow, body, caption, statValue, northLabel;
    Texture2D cardTexture, insetTexture, ruleTexture, markerPlateTexture;
    Texture2D treeIcon, waterIcon, ruinIcon, campIcon, towerIcon, playerIcon, portalAIcon, portalBIcon;
    RenderTexture sceneAtlas;
    Camera atlasCamera;
    float nextCapture;
    BaselineWorld sceneWorld;
    readonly List<Rect> frameMarkerLabels = new List<Rect>();
    readonly List<Vector2> frameMarkerPoints = new List<Vector2>();
    int iconVersion;
    Health[] enemyMarkers;
    Texture2D enemyDot;
    float nextEnemyRefresh;
    public bool HasWaypoint { get; private set; }
    public bool WaypointHasElevation { get; private set; }
    public bool SelectedHasElevation { get; private set; }
    float nextArrivalCheck;
    public bool CanArriveAtWaypoint(Vector3 position, bool grounded)
    {
        if(!HasWaypoint || !WaypointHasElevation || !sceneWorld || !grounded)return false;
        Vector3 delta=WaypointPosition-position;
        if(new Vector2(delta.x,delta.z).sqrMagnitude>6.25f || Mathf.Abs(delta.y)>1.2f)return false;
        return sceneWorld.NavigationGround(position,out var floor) && Mathf.Abs(floor.y-WaypointPosition.y)<1.2f && sceneWorld.NavigationSegment(position,WaypointPosition);
    }
    public Vector3 WaypointPosition { get; private set; }
    public string WaypointName { get; private set; }
    public Texture2D WaypointIcon { get; private set; }
    Texture2D pinIcon, restIcon;
    readonly List<(Rect hit, Vector3 position, string name, Texture2D icon)> destinations = new List<(Rect, Vector3, string, Texture2D)>();
    public bool HasSelection { get; private set; }
    public Vector3 SelectedPosition { get; private set; }
    public string SelectedName { get; private set; }
    public Texture2D SelectedIcon { get; private set; }
    public bool SelectionIsGuided => HasSelection && HasWaypoint && SelectedPosition==WaypointPosition && SelectedName==WaypointName;
    public void ClearWaypoint()
    {
        HasWaypoint=false;
        var world=sceneWorld?sceneWorld:GetComponent<BaselineWorld>();
        if(world)world.RefreshGuidancePresentation();
    }
    public bool GuideToSelection()
    {
        if(!HasSelection || !flow || !flow.MapOpen)return false;
        WaypointPosition=SelectedPosition; WaypointName=SelectedName; WaypointIcon=SelectedIcon; WaypointHasElevation=SelectedHasElevation; HasWaypoint=true;
        if(sceneWorld)sceneWorld.RefreshGuidancePresentation();
        return true;
    }
    public Vector3 AtlasUVToWorld(Vector2 uv) => new Vector3(MapCenter.x+(uv.x-.5f)*2*CaptureHalfExtent,0,MapCenter.y+(uv.y-.5f)*2*CaptureHalfExtent);
    void SelectWaypoint(Vector3 position, string name, Texture2D icon, bool knownLandmark=true)
    {
        SelectedHasElevation=false;
        if(knownLandmark && sceneWorld && sceneWorld.NavigationGround(position,out var floor))
        {position=floor;SelectedHasElevation=true;}
        SelectedPosition=position;SelectedName=name;SelectedIcon=icon;HasSelection=true;
    }
    public void TrackTower(BaselineFlow owner)
    {
        Prepare(owner);
        SelectWaypoint(TowerLandmarkPosition, "Tower", towerIcon);
        WaypointPosition=SelectedPosition; WaypointName=SelectedName; WaypointIcon=SelectedIcon;
        WaypointHasElevation=SelectedHasElevation; HasWaypoint=true;
        if(sceneWorld)sceneWorld.RefreshGuidancePresentation();
    }
    void Destination(Rect map,Vector3 position,string name,Texture2D icon)
    {
        Vector2 p=MapGuiPoint(map,position);
        destinations.Add((new Rect(p.x-18,p.y-18,36,36),position,name,icon));
        DrawMapIcon(map,position,icon,26);
    }
    void LegendRow(Rect row,string name,Texture2D icon,Vector3? target)
    {
        if(icon) GUI.DrawTexture(new Rect(row.x,row.y,22,22),icon,ScaleMode.ScaleToFit,true);
        GUI.Label(new Rect(row.x+32,row.y,row.width-32,25),name,body);
        if(target.HasValue && GUI.Button(row,GUIContent.none,GUIStyle.none)) SelectWaypoint(target.Value,name,icon);
    }
    float MapExtent => layout ? Mathf.Max(10, layout.MapHalfExtent) : Extent;
    Vector2 MapCenter => layout ? layout.MapCenter : Vector2.zero;

    // HUD and full atlas share the actual terrain and exact world registration.
    public Texture TerrainTexture => sceneAtlas;
    public Texture2D PlayerMarkerTexture => playerIcon;
    public Texture2D CampMarkerTexture => campIcon;
    public Texture2D PortalMarkerTexture(bool second) => second ? portalBIcon : portalAIcon;
    // Mark the real entrance, including when a teammate moves the tower.
    public Vector3 TowerLandmarkPosition => flow && flow.endingEntrance ?
        flow.endingEntrance.position : new Vector3(61f, 0f, 61f);
    float CaptureHalfExtent => MapExtent * 1.15f;
    public Vector2 WorldToAtlasUV(Vector3 position)
    {
        return new Vector2(.5f + (position.x - MapCenter.x) / (2f * CaptureHalfExtent),
            .5f + (position.z - MapCenter.y) / (2f * CaptureHalfExtent));
    }
    public float WorldDistanceToAtlasUV(float distance) => distance / (2f * CaptureHalfExtent);
    public Vector2 ClampLocalViewCenter(Vector2 focusUV, float viewSpan)
    {
        // The atlas includes a little capture padding outside the playable square. Keep
        // the minimap window inside the playable region so edge travel does not reveal
        // a large strip of capture background or distant water.
        float playableMargin = .5f - MapExtent / (2f * CaptureHalfExtent);
        float halfView = Mathf.Clamp(viewSpan * .5f, 0f, .5f - playableMargin);
        float minimum = playableMargin + halfView;
        float maximum = 1f - playableMargin - halfView;
        return new Vector2(Mathf.Clamp(focusUV.x, minimum, maximum),
            Mathf.Clamp(focusUV.y, minimum, maximum));
    }

    public void Prepare(BaselineFlow owner)
    {
        flow = owner;
        layout = owner.GetComponent<OverworldV2Layout>();
        sceneWorld = owner.GetComponent<BaselineWorld>();
        RefreshEnemyMarkers();
        entrances = FindObjectsByType<SideWorldEntrance>(FindObjectsSortMode.None);
        waystations = FindObjectsByType<BaselineWaystation>(FindObjectsSortMode.None);
        EnsureAtlas();
    }

    public void Draw(BaselineFlow owner, BaselineWorld world)
    {
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            label.normal.textColor = WayfarerUITheme.Paper;
            heading = new GUIStyle(label) { fontSize = 25, fontStyle = FontStyle.Bold, wordWrap = false };
            heading.normal.textColor = WayfarerUITheme.Ink;
            subhead = new GUIStyle(heading) { fontSize = 21 };
            subhead.normal.textColor=WayfarerUITheme.Paper;
            eyebrow = new GUIStyle(label) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false };
            eyebrow.normal.textColor = WayfarerUITheme.Gold;
            body = new GUIStyle(label) { fontSize = 14, wordWrap = true };
            body.normal.textColor = new Color32(222,218,198,255);
            caption = new GUIStyle(label) { fontSize = 11, wordWrap = true };
            caption.normal.textColor = new Color32(177,190,168,255);
            northLabel = new GUIStyle(label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            northLabel.normal.textColor = new Color32(250,240,213,255);
            statValue = new GUIStyle(label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            statValue.normal.textColor = WayfarerUITheme.Paper;
            foreach (var style in new[] { label, heading, subhead, eyebrow, body, caption, northLabel, statValue })
                WayfarerUITheme.KeepLabelColor(style);
        }
        if (northLabel == null)
        {
            northLabel = new GUIStyle(label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            northLabel.normal.textColor = new Color32(250,240,213,255);
            WayfarerUITheme.KeepLabelColor(northLabel);
        }
        // Unity can retain older serialized editor instances through a script
        // reload, so initialize each newly introduced runtime visual separately.
        if (mapLabel == null)
        {
            mapLabel = new GUIStyle(label) { fontSize = 10, fontStyle = FontStyle.Bold, wordWrap = false, padding = new RectOffset(5,5,1,1), alignment = TextAnchor.MiddleLeft };
            mapLabel.normal.textColor = WayfarerUITheme.Paper;
            WayfarerUITheme.KeepLabelColor(mapLabel);
        }
        if (!cardTexture) cardTexture = Swatch(new Color32(28,62,53,250));
        if (!insetTexture) insetTexture = Swatch(new Color32(19,45,41,255));
        if (!ruleTexture) ruleTexture = Swatch(new Color32(113,116,78,255));
        if (!markerPlateTexture) markerPlateTexture = Swatch(new Color32(17,39,35,242));
        EnsureAtlas();
        var oldMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / 1000f, Screen.height / 700f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float uiWidth = Screen.width / scale, uiHeight = Screen.height / scale;
        // Match the pause journal's visible 8:5 footprint instead of the old near-fullscreen map.
        const float panelWidth = 880f, panelHeight = 584f;
        var panel = new Rect((uiWidth-panelWidth)*.5f, (uiHeight-panelHeight)*.5f, panelWidth, panelHeight);
        if (!WayfarerUITheme.BeginPanel("region-map",panel,true,.565f)) { GUI.matrix=oldMatrix; return; }

        GUI.Label(new Rect(panel.x+26,panel.y+29,400,34),"REGION MAP",heading);
        DrawRule(new Rect(panel.x+26,panel.y+64,panel.width-52,1));

        var mapCard = new Rect(panel.x+24,panel.y+66,464,464);
        // Borderless, full overhead view: preserve the whole atlas instead of cropping it to a medallion.
        var map = mapCard;
        destinations.Clear();
        if (TerrainTexture) GUI.DrawTexture(map,TerrainTexture,ScaleMode.StretchToFill,true);
        DrawMapFrame(map);

        frameMarkerLabels.Clear(); frameMarkerPoints.Clear();
        Vector3 towerLocation=TowerLandmarkPosition;
        Color32 campColor=new Color32(235,196,100,255), towerColor=new Color32(169,213,198,255);
        Mark(map,towerLocation,"TOWER",towerColor,false);
        if (entrances != null) foreach (var entry in entrances)
            if (entry && entry.gameObject.scene == gameObject.scene)
                Mark(map, entry.transform.position, entry.isSideWorldB ? "B" : "A", entry.isSideWorldB ? new Color32(190,174,235,255) : new Color32(92,221,198,255));
        if (waystations != null) foreach (var station in waystations)
            if (station && station.isActiveAndEnabled && station.gameObject.scene == gameObject.scene)
                Mark(map, station.InteractionPosition, station.OffersSupplies ? "REST" : "RETURN", new Color32(92,221,198,255));
        Vector3 campLocation=layout?layout.CampPosition:BaselineSaveData.DefaultSafePosition;
        Mark(map,campLocation,"CAMP",campColor);
        if (world.GuidingTask >= 0)
            Mark(map, world.objectives[world.GuidingTask].position, "TASK", new Color32(239,151,83,255));
        if (entrances != null) foreach (var entry in entrances)
            if (entry && entry.gameObject.scene == gameObject.scene)
                DrawMapIcon(map,entry.transform.position,entry.isSideWorldB?portalBIcon:portalAIcon,32);
        DrawMapIcon(map,campLocation,campIcon,28);
        Destination(map,campLocation,"Camp",campIcon);
        Destination(map,towerLocation,"Tower",towerIcon);
        Vector3? sideA=null,sideB=null,rest=null,task=null;
        if(entrances!=null) foreach(var entry in entrances)
            if(entry && entry.gameObject.scene==gameObject.scene)
            {
                if(entry.isSideWorldB) sideB=entry.transform.position; else sideA=entry.transform.position;
                Destination(map,entry.transform.position,entry.isSideWorldB?"Side World B":"Side World A",entry.isSideWorldB?portalBIcon:portalAIcon);
            }
        if(waystations!=null) foreach(var station in waystations)
            if(station && station.isActiveAndEnabled && station.gameObject.scene==gameObject.scene)
            {
                if(!rest.HasValue || (station.InteractionPosition-world.player.position).sqrMagnitude<(rest.Value-world.player.position).sqrMagnitude) rest=station.InteractionPosition;
                Destination(map,station.InteractionPosition,station.OffersSupplies?"Rest":"Return",restIcon);
            }
        if(world.GuidingTask>=0)
        { task=world.objectives[world.GuidingTask].position; Destination(map,task.Value,"Tracked task",pinIcon); }
        if(GUI.enabled && Event.current.type==EventType.MouseDown && map.Contains(Event.current.mousePosition))
        {
            if(Event.current.button==0)
            {
                Vector2 mouse=Event.current.mousePosition;
                SelectWaypoint(AtlasUVToWorld(new Vector2((mouse.x-map.x)/map.width,1-(mouse.y-map.y)/map.height)),"Waypoint",pinIcon,false);
                float closest=float.MaxValue;
                foreach(var destination in destinations)
                {
                    float distance=(mouse-destination.hit.center).sqrMagnitude;
                    if(destination.hit.Contains(mouse) && distance<closest)
                    { closest=distance; SelectWaypoint(destination.position,destination.name,destination.icon); }
                }
            }
            Event.current.Use();
        }
        if(HasWaypoint) DrawMapIcon(map,WaypointPosition,WaypointIcon,28);
        if(HasSelection)
        {
            Vector2 p=MapGuiPoint(map,SelectedPosition);
            DrawMapFrame(new Rect(p.x-18,p.y-18,36,36));
            DrawMapIcon(map,SelectedPosition,SelectedIcon,28);
        }
        DrawEnemyMarkers(map);
        DrawPlayerArrow(map,world.player.transform.position,world.player.transform.eulerAngles.y);
        GUI.Label(new Rect(map.xMax-32,map.y+8,24,24),"N",northLabel);
        float rightX=map.xMax+24, rightW=panel.xMax-24-rightX;
        var legendCard=new Rect(rightX,map.y,rightW,map.height);
        DrawCard(legendCard,cardTexture);
        GUI.Label(new Rect(rightX+18,map.y+12,rightW-36,30),"Destination",subhead);
        if(HasSelection)
        {
            var destinationCard=new Rect(rightX+12,map.y+50,rightW-24,154);
            DrawCard(destinationCard,insetTexture);
            if(SelectedIcon)GUI.DrawTexture(new Rect(destinationCard.x+12,destinationCard.y+12,30,30),SelectedIcon,ScaleMode.ScaleToFit,true);
            GUI.Label(new Rect(destinationCard.x+52,destinationCard.y+8,destinationCard.width-62,26),SelectedName,body);
            Vector3 distance=SelectedPosition-world.player.position;distance.y=0;
            GUI.Label(new Rect(destinationCard.x+12,destinationCard.y+43,destinationCard.width-24,21),Mathf.RoundToInt(distance.magnitude)+" m · Direction only",caption);
            GUI.Label(new Rect(destinationCard.x+12,destinationCard.y+65,destinationCard.width-24,21),SelectedHasElevation?BaselineWorld.ElevationLabel(SelectedPosition.y-world.player.position.y):"Height unknown · Direction only",caption);
            GUI.Label(new Rect(destinationCard.x+12,destinationCard.y+87,destinationCard.width-24,21),SelectionIsGuided?"Active destination":"Selected · Guide here to follow",caption);
            if(!SelectionIsGuided&&GUI.Button(new Rect(destinationCard.x+12,destinationCard.y+118,(destinationCard.width-30)*.52f,29),"Guide here"))GuideToSelection();
            if(HasWaypoint&&GUI.Button(new Rect(destinationCard.x+12+(destinationCard.width-30)*.54f,destinationCard.y+118,(destinationCard.width-30)*.46f,29),"Clear marker"))ClearWaypoint();
        }
        else GUI.Label(new Rect(rightX+18,map.y+62,rightW-36,100),"Select a place on the map.\n\nYour task stays tracked until Guide here.",body);
        DrawRule(new Rect(rightX+16,map.y+217,rightW-32,1));
        GUI.Label(new Rect(rightX+18,map.y+225,rightW-36,26),"Places",subhead);
        string[] names={"You","Camp","Side World A","Side World B","Rest / Return","Tracked task","Tower"};
        Texture2D[] icons={playerIcon,campIcon,portalAIcon,portalBIcon,restIcon,pinIcon,towerIcon};
        Vector3?[] targets={world.player.position,campLocation,sideA,sideB,rest,task,towerLocation};
        for(int i=0;i<names.Length;i++)LegendRow(new Rect(rightX+18,map.y+257+i*25,rightW-36,25),names[i],icons[i],targets[i]);
        GUI.Label(new Rect(rightX+18,map.y+437,rightW-36,23),"Beacons  "+owner.Progress.RecallBeaconsRemaining,caption);
        DrawRule(new Rect(panel.x+24,panel.yMax-42,panel.width-48,1));
        if(GUI.Button(new Rect(panel.x+24,panel.yMax-35,180,28),"M / Esc   Close")) owner.SetMenu(false);
        if(GUI.Button(new Rect(panel.x+340,panel.yMax-35,140,28),"J   Tasks")) owner.CloseMapToTasks();
        if(GUI.Button(new Rect(panel.xMax-200,panel.yMax-35,176,28),"Journey")) owner.CloseMapToMenu();
        WayfarerUITheme.EndPanel();
        GUI.matrix = oldMatrix;
    }

    Texture2D Swatch(Color color)
    {
        var tex=new Texture2D(1,1,TextureFormat.RGBA32,false){name="Region map UI swatch",hideFlags=HideFlags.DontSave};
        tex.SetPixel(0,0,color); tex.Apply(); return tex;
    }
    void EnsureAtlas()
    {
        if(!pinIcon) pinIcon=MakeMapIcon(IconKind.Pin);
        if(!restIcon) restIcon=MakeMapIcon(IconKind.Rest);
        if(iconVersion!=7)
        {
            if(treeIcon)Destroy(treeIcon); if(waterIcon)Destroy(waterIcon); if(ruinIcon)Destroy(ruinIcon);
            if(campIcon)Destroy(campIcon); if(towerIcon)Destroy(towerIcon); if(playerIcon)Destroy(playerIcon);
            if(portalAIcon)Destroy(portalAIcon); if(portalBIcon)Destroy(portalBIcon);
            treeIcon=MakeMapIcon(IconKind.Tree); waterIcon=MakeMapIcon(IconKind.Water);
            ruinIcon=MakeMapIcon(IconKind.Ruins); campIcon=MakeMapIcon(IconKind.Camp);
            towerIcon=MakeMapIcon(IconKind.Tower); playerIcon=MakeMapIcon(IconKind.Player);
            portalAIcon=MakeMapIcon(IconKind.PortalA); portalBIcon=MakeMapIcon(IconKind.PortalB); iconVersion=7;
        }
        EnsureSceneAtlas();
    }

    void EnsureSceneAtlas()
    {
        if (atlasCamera && sceneAtlas) return;
        sceneAtlas = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32)
        {
            name = "Shared world atlas", filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp, antiAliasing = 1
        };
        sceneAtlas.Create();
        var cameraObject = new GameObject("World atlas camera (north up)", typeof(Camera));
        cameraObject.transform.SetParent(transform, false);
        atlasCamera = cameraObject.GetComponent<Camera>();
        atlasCamera.enabled = false;
        atlasCamera.orthographic = true;
        atlasCamera.aspect = 1f;
        atlasCamera.clearFlags = CameraClearFlags.SolidColor;
        atlasCamera.backgroundColor = new Color32(24, 102, 125, 255);
        // UI and isolated reward/tutorial previews must never enter the map.
        atlasCamera.cullingMask = ~((1 << 5) | (1 << 31));
        atlasCamera.allowHDR = false;
        atlasCamera.allowMSAA = false;
        atlasCamera.useOcclusionCulling = false;
        atlasCamera.nearClipPlane = .1f;
        atlasCamera.farClipPlane = 1000f;
        var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.Off;
        nextCapture = 0f;
    }

    void LateUpdate()
    {
        if (!flow || !sceneWorld || !sceneWorld.player) return;
        var travel = SideWorldTravel.Instance;
        if ((travel && (travel.IsAway || travel.Busy)) ||
            (flow.endingRoom && flow.endingRoom.IsInside) || (flow.SamBridge && flow.SamBridge.IsAway)) return;
        if(HasWaypoint && flow && flow.Started && !flow.BlockWorldInput && Time.unscaledTime>=nextArrivalCheck)
        {
            nextArrivalCheck=Time.unscaledTime+.25f;
            var movement=sceneWorld.player.GetComponent<PlayerMovement>();
            if(CanArriveAtWaypoint(sceneWorld.player.position,movement && movement.IsStableGrounded))
            {string name=WaypointName;ClearWaypoint();sceneWorld.ShowNotice("Arrived: "+name+". Previous task guidance restored if available.");}
        }
        // A flat map click has no resolved walkable layer. Only resolved landmarks
        // may auto-clear; horizontal proximity alone must not claim arrival.
        EnsureSceneAtlas();
        if (Time.unscaledTime < nextCapture) return;
        nextCapture = Time.unscaledTime + .5f;
        atlasCamera.transform.SetPositionAndRotation(new Vector3(MapCenter.x, 500f, MapCenter.y),
            Quaternion.Euler(90f, 0f, 0f));
        atlasCamera.orthographicSize = CaptureHalfExtent;
        // Omit the player model: the live heading marker is its only map representation.
        // Restore each renderer even if rendering fails; never affect the gameplay camera.
        var renderers = sceneWorld.player.GetComponentsInChildren<Renderer>();
        var previous = new bool[renderers.Length];
        bool previousFog = RenderSettings.fog;
        try
        {
            RenderSettings.fog = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                previous[i] = renderers[i].forceRenderingOff;
                renderers[i].forceRenderingOff = true;
            }
            RenderPipeline.SubmitRenderRequest(atlasCamera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = sceneAtlas });
        }
        finally
        {
            RenderSettings.fog = previousFog;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i]) renderers[i].forceRenderingOff = previous[i];
        }
    }
    const int AtlasSize=512;
    static readonly Color32 Ink=new Color32(43,57,40,255);
    Vector2 MarkerAtlasPoint(Vector3 world)
    {
        Vector2 uv = WorldToAtlasUV(world);
        return new Vector2(uv.x, 1f - uv.y) * AtlasSize;
    }
    void DrawCard(Rect rect,Texture2D fill)
    {
        if(fill) GUI.DrawTexture(rect,fill);
        Color old=GUI.color; GUI.color=new Color32(97,124,91,220);
        GUI.DrawTexture(new Rect(rect.x,rect.y,rect.width,1),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x,rect.yMax-1,rect.width,1),Texture2D.whiteTexture);
        GUI.color=old;
    }
    void DrawRule(Rect rect){if(ruleTexture)GUI.DrawTexture(rect,ruleTexture);}
    void DrawMapFrame(Rect map)
    {
        Color old=GUI.color; GUI.color=new Color32(190,157,88,255);
        GUI.DrawTexture(new Rect(map.x-1,map.y-1,map.width+2,1),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(map.x-1,map.yMax,map.width+2,1),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(map.x-1,map.y,1,map.height),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(map.xMax,map.y,1,map.height),Texture2D.whiteTexture);
        GUI.color=old;
    }
    void DrawLegendItem(float x,float y,string text,Color color)
    {
        Color old=GUI.color; GUI.color=color;
        GUI.DrawTexture(new Rect(x,y+4,9,9),Texture2D.whiteTexture); GUI.color=old;
        GUI.Label(new Rect(x+16,y,135,20),text,body);
    }
    void DrawSideWorldLegendItem(float x,float y)
    {
        // Use the same silhouettes as the live markers. Plain colour squares
        // looked like a broken colour key and did not explain which gate was A/B.
        if (portalAIcon) GUI.DrawTexture(new Rect(x-2,y-2,23,23),portalAIcon,ScaleMode.ScaleToFit,true);
        GUI.Label(new Rect(x+18,y,14,20),"A",body);
        if (portalBIcon) GUI.DrawTexture(new Rect(x+35,y-2,23,23),portalBIcon,ScaleMode.ScaleToFit,true);
        GUI.Label(new Rect(x+56,y,14,20),"B",body);
        GUI.Label(new Rect(x+75,y,40,20),"GATES",caption);
    }
    enum IconKind { Tree, Water, Ruins, Camp, Tower, Player, PortalA, PortalB, Pin, Rest }
    Texture2D MakeMapIcon(IconKind kind)
    {
        if(kind==IconKind.Player)return MakePlayerMapIcon();
        const int size=32;
        var tex=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Map landmark icon",hideFlags=HideFlags.DontSave};
        var pixels=new Color32[size*size];
        Color32 ink=new Color32(20,48,40,255), cream=new Color32(244,229,184,255);
        void RectPx(int x0,int y0,int w,int h,Color32 c)
        { for(int y=y0;y<y0+h;y++) for(int x=x0;x<x0+w;x++) if(x>=0&&x<size&&y>=0&&y<size)pixels[y*size+x]=c; }
        void Circle(int cx,int cy,int r,Color32 c)
        { for(int y=-r;y<=r;y++) for(int x=-r;x<=r;x++) if(x*x+y*y<=r*r&&cx+x>=0&&cx+x<size&&cy+y>=0&&cy+y<size)pixels[(cy+y)*size+cx+x]=c; }
        void Line(int x0,int y0,int x1,int y1,int thickness,Color32 c)
        { int steps=Mathf.Max(Mathf.Abs(x1-x0),Mathf.Abs(y1-y0)); for(int i=0;i<=steps;i++){float t=steps==0?0:(float)i/steps;Circle(Mathf.RoundToInt(Mathf.Lerp(x0,x1,t)),Mathf.RoundToInt(Mathf.Lerp(y0,y1,t)),thickness,c);} }
        void Polygon(Color32 color,params Vector2[] points)
        {
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {bool inside=false;for(int i=0,j=points.Length-1;i<points.Length;j=i++)if(((points[i].y>y)!=(points[j].y>y))&&(x<(points[j].x-points[i].x)*(y-points[i].y)/(points[j].y-points[i].y)+points[i].x))inside=!inside;if(inside)pixels[y*size+x]=color;}
        }
        switch(kind)
        {
            case IconKind.Pin:
                Polygon(ink,new Vector2(16,1),new Vector2(31,16),new Vector2(16,31),new Vector2(1,16));
                Polygon(new Color32(239,151,83,255),new Vector2(16,5),new Vector2(27,16),new Vector2(16,27),new Vector2(5,16)); break;
            case IconKind.Rest:
                Circle(16,16,13,ink); Circle(16,16,10,new Color32(92,221,198,255)); break;
            case IconKind.Tree:
                RectPx(14,3,4,10,ink); Circle(16,19,11,ink); Circle(10,18,7,ink); Circle(21,18,7,ink);
                Circle(16,20,8,new Color32(68,112,63,255)); Circle(10,18,5,new Color32(89,136,70,255)); Circle(21,18,5,new Color32(76,125,64,255)); Circle(13,23,3,new Color32(125,157,79,255)); break;
            case IconKind.Water:
                Circle(16,16,14,ink); Circle(16,16,11,new Color32(62,147,167,255));
                for(int y=9;y<=22;y+=6){Line(7,y,12,y,1,cream);Line(17,y,24,y,1,cream);} break;
            case IconKind.Ruins:
                RectPx(4,9,24,17,ink); RectPx(7,12,18,11,new Color32(177,157,113,255));
                RectPx(12,16,5,3,ink); RectPx(20,16,3,3,ink); RectPx(4,25,24,3,cream); break;
            case IconKind.Camp:
                Line(7,5,16,24,3,ink); Line(16,24,25,5,3,ink); Line(7,5,25,5,3,ink);
                Line(7,5,16,24,1,cream); Line(16,24,25,5,1,cream); Line(7,5,25,5,1,cream);
                Line(16,11,16,5,1,new Color32(203,163,75,255)); break;
            case IconKind.Tower:
                RectPx(6,4,20,22,ink); RectPx(9,7,14,15,new Color32(177,157,113,255));
                RectPx(5,25,22,5,cream); RectPx(8,27,3,3,ink); RectPx(15,27,3,3,ink); RectPx(22,27,3,3,ink);
                RectPx(13,9,6,10,ink); break;
            case IconKind.PortalA:
                // A: rounded, cool-lit gate with a diamond glyph.
                Line(6,3,6,15,2,ink); Line(6,15,8,22,2,ink); Line(8,22,12,27,2,ink);
                Line(12,27,16,29,2,ink); Line(16,29,20,27,2,ink); Line(20,27,24,22,2,ink);
                Line(24,22,26,15,2,ink); Line(26,15,26,3,2,ink); Line(3,3,29,3,2,ink);
                Line(10,5,10,15,1,new Color32(92,221,198,255)); Line(10,15,12,21,1,new Color32(92,221,198,255));
                Line(12,21,16,24,1,new Color32(92,221,198,255)); Line(16,24,20,21,1,new Color32(92,221,198,255));
                Line(20,21,22,15,1,new Color32(92,221,198,255)); Line(22,15,22,5,1,new Color32(92,221,198,255));
                Polygon(cream,new Vector2(16,17),new Vector2(20,13),new Vector2(16,9),new Vector2(12,13)); break;
            case IconKind.PortalB:
                // B: squared stone gate with a violet cross-rune, distinct from A's arch.
                RectPx(4,4,5,24,ink); RectPx(23,4,5,24,ink); RectPx(4,24,24,5,ink);
                RectPx(8,7,2,18,new Color32(190,174,235,255)); RectPx(22,7,2,18,new Color32(190,174,235,255));
                RectPx(9,24,14,2,new Color32(190,174,235,255)); RectPx(7,28,18,2,cream);
                Line(16,11,16,21,1,cream); Line(12,16,20,16,1,cream);
                Circle(16,16,2,new Color32(190,174,235,255)); break;
        }
        tex.SetPixels32(pixels); tex.filterMode=FilterMode.Point; tex.Apply(); return tex;
    }
    Texture2D MakePlayerMapIcon()
    {
        // A deep open notch in the tail makes the cyan tip unambiguously forward
        // even when the marker is small or rotated over detailed terrain.
        const int size=96;
        var tex=new Texture2D(size,size,TextureFormat.RGBA32,false)
            {name="Player navigation triangle",hideFlags=HideFlags.DontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        var pixels=new Color32[size*size];
        var shadow=new[]{new Vector2(16,31),new Vector2(1,3),new Vector2(10,3),new Vector2(16,12),new Vector2(22,3),new Vector2(31,3)};
        var outline=new[]{new Vector2(16,28),new Vector2(4,5),new Vector2(11,5),new Vector2(16,14),new Vector2(21,5),new Vector2(28,5)};
        var fill=new[]{new Vector2(16,23),new Vector2(8,7.5f),new Vector2(12,7.5f),new Vector2(16,16),new Vector2(20,7.5f),new Vector2(24,7.5f)};
        bool Inside(Vector2 p,Vector2[] polygon)
        {
            bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
                if((polygon[i].y>p.y)!=(polygon[j].y>p.y) &&
                    p.x<(polygon[j].x-polygon[i].x)*(p.y-polygon[i].y)/(polygon[j].y-polygon[i].y)+polygon[i].x)
                    inside=!inside;
            return inside;
        }
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            var p=new Vector2((x+.5f)/3f,(y+.5f)/3f);
            Color32 color=new Color32(0,0,0,0);
            if(Inside(p,shadow))color=new Color32(12,42,54,255);
            if(Inside(p,outline))color=new Color32(250,255,255,255);
            if(Inside(p,fill))color=new Color32(44,211,245,255);
            pixels[y*size+x]=color;
        }
        tex.SetPixels32(pixels);tex.Apply(false,true);return tex;
    }
    void DrawMapIcon(Rect map,Vector3 point,Texture2D icon,float size=29)
    {
        if(!icon)return;
        var p=MapGuiPoint(map,point);
        var rect=new Rect(p.x-size*.5f,p.y-size*.5f,size,size);
        GUI.DrawTexture(rect,icon,ScaleMode.StretchToFill,true);
    }
    void DrawPlayerArrow(Rect map,Vector3 point,float yaw)
    {
        if(!playerIcon)return;
        var p=MapGuiPoint(map,point);float s=32;Matrix4x4 old=GUI.matrix;
        // The capture includes an outer margin. Never move a marker inward:
        // clamping the centre makes an edge/water position appear on land.
        // Rotate in atlas GUI coordinates, then apply the outer UI scale once.
        // RotateAroundPivot composes its pivot with the already scaled matrix,
        // which can displace this marker off the map near the tower boundary.
        var pivot=new Vector3(p.x,p.y,0f);
        GUI.matrix=old*Matrix4x4.Translate(pivot)*Matrix4x4.Rotate(Quaternion.Euler(0f,0f,yaw))*Matrix4x4.Translate(-pivot);
        GUI.DrawTexture(new Rect(p.x-s*.5f,p.y-s*.5f,s,s),playerIcon,ScaleMode.StretchToFill,true);GUI.matrix=old;
    }
    Vector2 MapGuiPoint(Rect rect,Vector3 point)
    {
        Vector2 atlas=MarkerAtlasPoint(point);
        return new Vector2(rect.x+atlas.x/AtlasSize*rect.width,
            rect.y+atlas.y/AtlasSize*rect.height);
    }
    void Mark(Rect rect, Vector3 point, string text, Color color, bool showDot=true)
    {
        var p = MapGuiPoint(rect,point);
        var previous = GUI.color;
        float dotSize=text=="YOU"?9:8;
        if (showDot)
        {
            GUI.color=new Color32(17,39,35,230);
            GUI.DrawTexture(new Rect(p.x-dotSize*.5f-1,p.y-dotSize*.5f-1,dotSize+2,dotSize+2),Texture2D.whiteTexture);
            GUI.color=color;
            GUI.DrawTexture(new Rect(p.x-dotSize*.5f,p.y-dotSize*.5f,dotSize,dotSize),Texture2D.whiteTexture);
        }
        float width = Mathf.Min(94, mapLabel.CalcSize(new GUIContent(text)).x + 10);
        const float badgeHeight=16, gap=8;
        var candidates=new[]{
            new Rect(p.x+gap,p.y-badgeHeight*.5f,width,badgeHeight),
            new Rect(p.x-gap-width,p.y-badgeHeight*.5f,width,badgeHeight),
            new Rect(p.x-width*.5f,p.y-gap-badgeHeight,width,badgeHeight),
            new Rect(p.x-width*.5f,p.y+gap,width,badgeHeight)
        };
        Rect badge=default; float bestCost=float.MaxValue;
        foreach(var candidate in candidates)
        {
            var c=candidate;
            c.x=Mathf.Clamp(c.x,rect.x+2,rect.xMax-c.width-2);
            c.y=Mathf.Clamp(c.y,rect.y+2,rect.yMax-c.height-2);
            float cost=0;
            foreach(var placed in frameMarkerLabels)if(c.Overlaps(placed))cost+=1000;
            foreach(var marker in frameMarkerPoints)if(c.Contains(marker))cost+=750;
            if(cost<bestCost){bestCost=cost;badge=c;}
        }
        frameMarkerLabels.Add(badge); frameMarkerPoints.Add(p);
        Texture2D destinationIcon=text=="A"?portalAIcon:text=="B"?portalBIcon:text=="CAMP"?campIcon:text=="TOWER"?towerIcon:text=="TASK"?pinIcon:restIcon;
        string destinationName=text=="A"?"Side World A":text=="B"?"Side World B":text;
        destinations.Add((badge,point,destinationName,destinationIcon));
        GUI.color=Color.white;
        if(markerPlateTexture) GUI.DrawTexture(badge,markerPlateTexture);
        GUI.color=color; GUI.DrawTexture(new Rect(badge.x,badge.y,2,badge.height),Texture2D.whiteTexture);
        GUI.color=Color.white;
        GUI.Label(new Rect(badge.x+2,badge.y,badge.width-2,badge.height),text,mapLabel);
        GUI.color = previous;
    }
    void RefreshEnemyMarkers()
    {
        enemyMarkers = FindObjectsByType<Health>(FindObjectsSortMode.None);
        nextEnemyRefresh = Time.unscaledTime + .4f;
    }
    void DrawEnemyMarkers(Rect map)
    {
        // This is an internal playtest overlay: live positions, no labels or saved map state.
        if (!showEnemyDotsForPlaytest) return;
        if (enemyMarkers == null || Time.unscaledTime >= nextEnemyRefresh) RefreshEnemyMarkers();
        if (!enemyDot) CreateEnemyDot();
        Vector2 centre = MapCenter;
        float extent = MapExtent;
        foreach (Health enemy in enemyMarkers)
        {
            if (!enemy || !enemy.isActiveAndEnabled || enemy.IsDead || enemy.Team != HealthTeam.Enemy ||
                enemy.gameObject.scene != gameObject.scene) continue;
            Vector3 point = enemy.transform.position;
            if (Mathf.Abs(point.x - centre.x) > extent || Mathf.Abs(point.z - centre.y) > extent) continue;
            Vector2 mapped = MapGuiPoint(map, point);
            float x = mapped.x, y = mapped.y;
            Color old = GUI.color;
            GUI.color = new Color(.03f, .07f, .09f, .95f);
            GUI.DrawTexture(new Rect(x - 6, y - 6, 12, 12), enemyDot);
            GUI.color = EnemyColor(enemy);
            GUI.DrawTexture(new Rect(x - 4, y - 4, 8, 8), enemyDot);
            GUI.color = old;
        }
    }
    static Color EnemyColor(Health enemy)
    {
        if (enemy.GetComponent<GelBlobAI>()) return new Color(.28f,.46f,1f);
        if (enemy.GetComponent<RepairWispAI>()) return new Color(.8f,1f,.2f);
        if (enemy.GetComponent<RailBeetleAI>()) return new Color(1f,.66f,.16f);


        DomainPrototypeEnemy domain = enemy.GetComponent<DomainPrototypeEnemy>();
        if (domain) return domain.EnemyKind == DomainPrototypeEnemy.Kind.LeafWisp
            ? new Color(.22f, .93f, 1f) : new Color(1f, .45f, .87f);
        // Cosmetic variants share one marker colour so the map communicates enemy type.
        if (enemy.GetComponent<StoneScoutAppearance>()) return new Color(.35f, .97f, .44f);
        return new Color(1f, .38f, .31f);
    }
    void CreateEnemyDot()
    {
        enemyDot = new Texture2D(12, 12, TextureFormat.RGBA32, false) { name = "Playtest enemy map dot", hideFlags = HideFlags.DontSave };
        enemyDot.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 12; x++)
        {
            float radius = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(6, 6));
            enemyDot.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(6f - radius)));
        }
        enemyDot.Apply();
    }
    void OnDestroy()
    {
        if(pinIcon) Destroy(pinIcon);
        if(restIcon) Destroy(restIcon);
        if (atlasCamera) Destroy(atlasCamera.gameObject);
        if (sceneAtlas) { sceneAtlas.Release(); Destroy(sceneAtlas); }
        if (cardTexture) Destroy(cardTexture);
        if (insetTexture) Destroy(insetTexture);
        if (ruleTexture) Destroy(ruleTexture);
        if (markerPlateTexture) Destroy(markerPlateTexture);
        if (treeIcon) Destroy(treeIcon);
        if (waterIcon) Destroy(waterIcon);
        if (ruinIcon) Destroy(ruinIcon);
        if (campIcon) Destroy(campIcon);
        if (towerIcon) Destroy(towerIcon);
        if (playerIcon) Destroy(playerIcon);
        if (portalAIcon) Destroy(portalAIcon);
        if (portalBIcon) Destroy(portalBIcon);
        if (enemyDot) Destroy(enemyDot);
    }
}
