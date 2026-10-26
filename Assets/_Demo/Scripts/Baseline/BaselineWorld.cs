using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Gameplay identities stay in the host scene; BaselineFlow owns explicit save/load.
public sealed class BaselineWorld : MonoBehaviour
{
    public Transform player;
    public Transform[] objectives;
    public Vector3[] routeNodes;
    public Vector2Int[] routeEdges;
    public Material routeMaterial;
    public Renderer hologram;
    public readonly bool[] accepted = new bool[3];
    public readonly bool[] visited = new bool[3];
    public readonly bool[] completed = new bool[3];
    public readonly bool[] opened = new bool[3];
    public int Tracked { get; private set; } = -1;
    public bool PuzzleSolved { get; private set; }
    public int RuneProgress { get; private set; }
    public bool JournalOpen { get; private set; }
    public int LastJournalChangedFrame { get; private set; } = -1;
    BaselineMap guidanceMap;
    BaselineMap GuidanceMap => guidanceMap ? guidanceMap : (guidanceMap=GetComponent<BaselineMap>());
    public bool CustomGuidanceActive => GuidanceMap && GuidanceMap.HasWaypoint;
    public int GuidingTask => !CustomGuidanceActive && Tracked>=0 && Tracked<accepted.Length && accepted[Tracked] && !completed[Tracked] && objectives!=null && Tracked<objectives.Length && objectives[Tracked] ? Tracked : -1;
    public int LastJournalClosedFrame { get; private set; } = -1;
    public event System.Action StateChanged;
    public BaselineFlow Flow { get; private set; }
    internal bool IsNotifyingSavedChest { get; private set; }
    private readonly string[] titles = { "A lost survey", "The island inscription", "The silent relay" };
    private readonly string[] descriptions = {
        "Recover five blue survey traces in the western grove, then open the record chest.",
        "Read the stone at the centre of the lake. Follow the shore ramp and swim to the island.",
        "Carry the two loose star orbs into the eastern array's empty marked positions, then open the record chest." };
    private readonly string[] equipment = { "Island relic", "Ridge charm", "Runic guard" };
    private readonly int[] sequence = { 2, 0, 1 };
    private LineRenderer route;
    private BaselineGoldenGuidance goldGuidance;
    private Vector3 lastRoutePosition;
    private int lastRouteTarget = -2;
    private PlayerMovement movement;
    private ThirdPersonCamera look;
    private DialogueUI dialogue;
    private string notice = "";
    float noticeRemaining;
    readonly Queue<string> noticeQueue=new Queue<string>();
    public string CurrentNotice => noticeRemaining>0?notice:"";
    public float NoticeRemaining => noticeRemaining;
    void TickNotices()
    {
        if(Flow && Flow.WorldHudHidden)return;
        noticeRemaining=Mathf.Max(0,noticeRemaining-Time.unscaledDeltaTime);
        if(noticeRemaining<=0 && noticeQueue.Count>0){notice=noticeQueue.Dequeue();noticeRemaining=7;}
    }
    private float nextRoute;
    private CursorLockMode previousCursor;
    private bool previousVisible;
    private GUIStyle text, heading;

    private void Start()
    {
        movement = player.GetComponent<PlayerMovement>();
        look = Camera.main.GetComponent<ThirdPersonCamera>();
        dialogue = FindFirstObjectByType<DialogueUI>();
        Flow = GetComponent<BaselineFlow>();
        route = new GameObject("Tracked task route").AddComponent<LineRenderer>();
        route.transform.SetParent(transform);
        route.sharedMaterial = routeMaterial;
        route.startWidth = .14f; route.endWidth = .14f;
        route.numCornerVertices = 3; route.useWorldSpace = true;
        route.positionCount = 0; route.enabled = false;
        var gold = new GameObject("Tracked task golden wisps");
        gold.transform.SetParent(transform);
        goldGuidance = gold.AddComponent<BaselineGoldenGuidance>();
    }
    private void Update()
    {
        TickNotices();
        var keys = Keyboard.current;
        if (LastJournalChangedFrame==Time.frameCount) { ClearGuidanceVisuals(); return; }
        if (JournalOpen)
        {
            if (keys!=null && (WorldInputBindings.Pressed("Journal") || keys.escapeKey.wasPressedThisFrame)) SetJournal(false);
            ClearGuidanceVisuals(); return;
        }
        if (Flow && Flow.BlockWorldInput) { ClearGuidanceVisuals(); return; }
        if (keys != null && WorldInputBindings.Pressed("Journal") &&
            (JournalOpen || (movement.CanStartInteraction && !dialogue.IsDialogueActive && !dialogue.IsRewardPresentationActive)))
            SetJournal(!JournalOpen);
        if (JournalOpen) { ClearGuidanceVisuals(); return; }
        if (CustomGuidanceActive) { ClearGuidanceVisuals(); if(hologram) hologram.enabled=Vector3.Distance(player.position,hologram.transform.position)<11f; return; }
        if (Time.unscaledTime >= nextRoute && (Tracked != lastRouteTarget || Vector3.Distance(player.position, lastRoutePosition) > .7f || (Tracked >= 0 && goldGuidance && goldGuidance.VisibleParticleCount == 0))) { nextRoute = Time.unscaledTime + .75f; UpdateRoute(); }
        if (hologram) hologram.enabled = Vector3.Distance(player.position, hologram.transform.position) < 11f;
    }
    public void SetJournal(bool open)
    {
        if (JournalOpen == open || (open && Flow && !Flow.CanOpenJournal)) return;
        JournalOpen = open;
        if(open){selectedJournalTask=GuidingTask>=0?GuidingTask:Tracked;journalScroll=Vector2.zero;}
        LastJournalChangedFrame=Time.frameCount;
        if (!open) LastJournalClosedFrame=Time.frameCount;
        if (Flow) Flow.JournalPresentationChanged(open);
        else
        {
            movement.SetGameplayInputLocked(open); look.SetLookInputLocked(open);
            if (open) { previousCursor=Cursor.lockState; previousVisible=Cursor.visible; }
            Cursor.lockState=open?CursorLockMode.None:previousCursor; Cursor.visible=open||previousVisible;
        }
        RefreshGuidancePresentation();
    }
    private void OnDisable() { if (JournalOpen && movement && look) SetJournal(false); }
    public void Track(int index)
    {
        Tracked = index >= 0 && index < 3 && accepted[index] && !completed[index] ? index : -1;
        if (Tracked>=0 && GuidanceMap) GuidanceMap.ClearWaypoint();
        UpdateRoute();
        NotifyStateChanged();
    }
    public void Interact(BaselineInteraction item)
    {
        if(!item)return;
        var actions=player.GetComponent<WorldHeroActions>();
        if(actions && (item.action==BaselineInteraction.ActionKind.Chest || item.action==BaselineInteraction.ActionKind.Rune || item.action==BaselineInteraction.ActionKind.Objective))
        {
            string pose=item.action==BaselineInteraction.ActionKind.Chest?"OpenChest":item.action==BaselineInteraction.ActionKind.Objective?"Pickup":"Touch";
            actions.TryPlay(pose,()=>{if(item&&Vector3.Distance(player.position,item.transform.position)<4&&movement.CanStartInteraction)CommitInteraction(item);});return;
        }
        var hero=player.GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
        if(hero&&(item.action==BaselineInteraction.ActionKind.Chest||item.action==BaselineInteraction.ActionKind.Rune)){
            hero.PresentInteraction(item.action==BaselineInteraction.ActionKind.Chest?"INTERACT_CHEST":"INTERACT_TOUCH",()=>{if(item&&Vector3.Distance(player.position,item.transform.position)<4&&movement.CanStartInteraction)CommitInteraction(item);});return;
        }
        CommitInteraction(item);
    }
    private void CommitInteraction(BaselineInteraction item)
    {
        int i = item.index;
        if (i < 0 || i >= 3) return;
        bool savedChest = false;
        switch (item.action)
        {
            case BaselineInteraction.ActionKind.AcceptTask:
                if (!accepted[i]) { accepted[i] = true; if (Tracked < 0) Track(i); Notify("Task recorded: " + titles[i] + ". Follow the golden wisps; J changes tracking."); }
                else Notify("Already recorded: " + titles[i]);
                CompleteIfReady(i);
                break;
            case BaselineInteraction.ActionKind.Objective:
                visited[i] = true;
                Notify("Discovery recorded. You can read its related clue later.");
                CompleteIfReady(i);
                break;
            case BaselineInteraction.ActionKind.Rune:
                if (PuzzleSolved) { Notify("The seal is already open."); break; }
                if (i == sequence[RuneProgress]) RuneProgress++;
                else RuneProgress = i == sequence[0] ? 1 : 0;
                PuzzleSolved = RuneProgress == sequence.Length;
                Notify(PuzzleSolved ? "The nearby chest seal opens." : "Rune sequence: " + RuneProgress + "/3");
                break;
            case BaselineInteraction.ActionKind.Chest:
                if (opened[i]) { Notify("Already collected: " + equipment[i]); break; }
                if (i == 2 && !PuzzleSolved) { Notify("Sealed. Look for the weathered inscription nearby."); break; }
                if (!Flow || !Flow.Started)
                {
                    Notify("Could not collect equipment. Start or continue a journey first.");
                    return;
                }
                bool previousSavedOpened = Flow.Progress.opened[i];
                opened[i] = true;
                // SaveNow copies world state into Progress before attempting persistence.
                // Roll back both receipts before any reward, visual or progress event escapes.
                if (!Flow.SaveNow())
                {
                    opened[i] = false;
                    Flow.Progress.opened[i] = previousSavedOpened;
                    Notify(Flow.Status);
                    return;
                }
                savedChest = true;
                if (item.lid) item.lid.localRotation = Quaternion.Euler(-65f, 0, 0);
                Notify("Equipment found: " + equipment[i] + ". Recorded in J.");
                var visual = item.GetComponent<BaselineChestVisual>();
                var presentation = GetComponent<BaselineRewardPresentation>();
                var loadout=player.GetComponent<BaselineLoadout>();
                string rewardDescription=loadout?"Equipped. "+loadout.EquipmentEffect(i)+". Manage equipment in the journey menu.":"Discovery recorded. This relic is kept in your journal.";
                if (!string.IsNullOrEmpty(Flow.SaveWarning)) rewardDescription += "\n" + Flow.SaveWarningSummary;
                if (!presentation || !presentation.Show(equipment[i], rewardDescription, false,
                    () => { if (visual) visual.PlayAfterReward(); }))
                    if (visual) visual.PlayAfterReward();
                break;
        }
        NotifyStateChanged(savedChest);
    }
    private void NotifyStateChanged(bool savedChest = false)
    {
        bool previous = IsNotifyingSavedChest;
        IsNotifyingSavedChest = savedChest;
        try { StateChanged?.Invoke(); }
        finally { IsNotifyingSavedChest = previous; }
    }
    private void CompleteIfReady(int i)
    {
        if (!accepted[i] || !visited[i] || completed[i]) return;
        completed[i] = true;
        if (Tracked == i) Track(-1);
        Notify("Completed: " + titles[i] + ". Discovery recorded; no return trip needed.");
    }
    private void Notify(string value)
    {
        if(string.IsNullOrEmpty(value) || (noticeRemaining>0 && notice==value) || noticeQueue.Contains(value))return;
        if(noticeRemaining<=0 && noticeQueue.Count==0){notice=value;noticeRemaining=7;}
        else {if(noticeQueue.Count>=4)noticeQueue.Dequeue();noticeQueue.Enqueue(value);}
    }
    public void ShowNotice(string value) => Notify(value);

    public void RestoreProgress(BaselineSaveData data)
    {
        notice="";noticeRemaining=0;noticeQueue.Clear();
        SetJournal(false);
        System.Array.Copy(data.accepted, accepted, 3);
        System.Array.Copy(data.visited, visited, 3);
        System.Array.Copy(data.completed, completed, 3);
        System.Array.Copy(data.opened, opened, 3);
        PuzzleSolved = data.puzzleSolved;
        RuneProgress = data.runeProgress;
        Tracked = data.tracked >= 0 && data.tracked < 3 && accepted[data.tracked] && !completed[data.tracked] ? data.tracked : -1;
        foreach (var item in FindObjectsByType<BaselineInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (item.action == BaselineInteraction.ActionKind.Chest && item.index >= 0 && item.index < 3)
            {
                var visual = item.GetComponent<BaselineChestVisual>();
                if (visual) visual.Restore(opened[item.index]);
                else if (item.lid) item.lid.localRotation = opened[item.index] ? Quaternion.Euler(-65,0,0) : Quaternion.identity;
            }
        RefreshNavigation(); UpdateRoute();
    }

    public void CopyProgressTo(BaselineSaveData data)
    {
        System.Array.Copy(accepted, data.accepted, 3); System.Array.Copy(visited, data.visited, 3);
        System.Array.Copy(completed, data.completed, 3); System.Array.Copy(opened, data.opened, 3);
        data.tracked=Tracked; data.puzzleSolved=PuzzleSolved; data.runeProgress=RuneProgress;
    }

    // Existing hand-authored graph, with collision-checked access and complete segments.
    // No route is displayed when the player cannot safely join the walking graph.
    public string NavigationStatus { get; private set; }
    public void RefreshNavigation() { validEdges = null; navWaters = null; navLayout = GetComponent<OverworldV2Layout>(); }
    private List<Vector2Int> validEdges;
    private bool retryingWalkingRoute;
    private WaterVolume navWater;
    private WaterVolume[] navWaters;
    private OverworldV2Layout navLayout;
    private bool cachedBridgeOpen;
    private void OnEnable() { RefreshNavigation(); }
    private bool IsWalkingSurface(Collider surface, bool segment = false)
    {
        if (navLayout) return navLayout.IsNavigationSurface(surface);
        string n = surface.name;
        // Keep the pre-V2 scene's original ground and segment filters unchanged.
        return n == "LakeTerrain_REPLACE_OR_SCULPT" || n.StartsWith("Causeway") || n.StartsWith("TowerWalkway") ||
            (!segment && (n.StartsWith("TowerRoof") || n.StartsWith("FutureEnding")));
    }
    private bool IsSubmergedGround(Vector3 point)
    {
        if (!navLayout)
        {
            if (!navWater) navWater = FindFirstObjectByType<WaterVolume>();
            return navWater && navWater.VolumeCollider.bounds.Contains(new Vector3(point.x, navWater.WaterSurfaceY - .1f, point.z)) && point.y < navWater.WaterSurfaceY - .12f;
        }
        if (navWaters == null) navWaters = FindObjectsByType<WaterVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var water in navWaters)
        {
            if (!water || water.gameObject.scene != gameObject.scene || !water.IsValid || point.y >= water.WaterSurfaceY - .12f) continue;
            // Bounds alone also cover dry corners of rotated or curved water volumes.
            var probe = new Vector3(point.x, water.WaterSurfaceY - .1f, point.z);
            var volume = water.VolumeCollider;
            if (volume.bounds.Contains(probe) && (volume.ClosestPoint(probe) - probe).sqrMagnitude < .0001f && water.ContainsSurfaceXZ(point)) return true;
        }
        return false;
    }
    public bool NavigationGround(Vector3 at, out Vector3 ground)
    {
        ground = default;
        var hits = Physics.RaycastAll(new Vector3(at.x, 60, at.z), Vector3.down, 90, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
        int hitCount = hits.Length;
        if (navLayout)
        {
            // A portal lintel or a higher tower stair is not the floor beneath this query.
            // Pick its nearest authored walking level, then keep all existing clearance checks.
            int closest = -1;
            float heightDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var candidate = hits[i];
                if ((player && candidate.collider.transform.IsChildOf(player)) || !IsWalkingSurface(candidate.collider)) continue;
                float difference = Mathf.Abs(at.y - candidate.point.y);
                if (difference < heightDistance) { heightDistance = difference; closest = i; }
            }
            if (closest < 0) return false;
            // A shallow paving slab or first stair can overlap the ground beneath it.
            // Use one fixed step-height window, never chain upward through other storeys.
            var controller = player ? player.GetComponent<CharacterController>() : null;
            float stepHeight = controller ? Mathf.Max(0, controller.stepOffset) : .3f;
            float layerBase = hits[closest].point.y;
            float highest = layerBase;
            for (int i = 0; i < hits.Length; i++)
            {
                var candidate = hits[i];
                if ((player && candidate.collider.transform.IsChildOf(player)) || !IsWalkingSurface(candidate.collider)) continue;
                if (candidate.point.y > highest && candidate.point.y <= layerBase + stepHeight + .001f)
                { highest = candidate.point.y; closest = i; }
            }
            hits[0] = hits[closest]; hitCount = 1;
            // Do not retry another storey if this one is wet, steep or physically obstructed.
        }
        for (int i = 0; i < hitCount; i++)
        {
            var h = hits[i];
            if (player && h.collider.transform.IsChildOf(player)) continue;
            if (!IsWalkingSurface(h.collider)) return false;
            if (Vector3.Angle(h.normal, Vector3.up) > 45) return false;
            if (IsSubmergedGround(h.point)) return false;
            foreach (var c in Physics.OverlapCapsule(h.point + Vector3.up * .45f, h.point + Vector3.up * 1.55f, .28f, ~0, QueryTriggerInteraction.Ignore))
                if (c != h.collider && !(player && c.transform.IsChildOf(player))) return false;
            ground = h.point + Vector3.up * .15f; return true;
        }
        return false;
    }
    public bool NavigationSegment(Vector3 a, Vector3 b, List<Vector3> samples = null)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(new Vector2(a.x-b.x,a.z-b.z).magnitude / .75f));
        if (steps > 400) return false;
        Vector3 previous = default;
        for (int i=0;i<=steps;i++)
        {
            if (!NavigationGround(Vector3.Lerp(a,b,(float)i/steps), out var p)) return false;
            if (i>0)
            {
                if (Mathf.Abs(p.y-previous.y) > Vector2.Distance(new Vector2(p.x,p.z),new Vector2(previous.x,previous.z)) + .08f) return false;
                foreach (var hit in Physics.CapsuleCastAll(previous+Vector3.up*.35f,previous+Vector3.up*1.35f,.25f,(p-previous).normalized,(p-previous).magnitude,~0,QueryTriggerInteraction.Ignore))
                    if (!(player && hit.collider.transform.IsChildOf(player)) && !IsWalkingSurface(hit.collider, true)) return false;
            }
            samples?.Add(p); previous=p;
        }
        return true;
    }
    public Vector3[] WalkingRoute(Vector3 from, Vector3 to)
    {
        NavigationStatus = "No safe walking access from this position.";
        if (!navLayout) navLayout = GetComponent<OverworldV2Layout>();
        if (navLayout && cachedBridgeOpen != navLayout.BridgeOpen) { cachedBridgeOpen = navLayout.BridgeOpen; RefreshNavigation(); }
        int count=routeNodes == null ? 0 : routeNodes.Length;
        // A swimmer below a bridge cannot join the walk graph on its deck.
        if (count==0 || count>96 || !NavigationGround(from,out var startGround) || Mathf.Abs(from.y-startGround.y)>.8f) return System.Array.Empty<Vector3>();
        if (Vector3.Distance(from, to) < 32)
        {
            for (int k = 0; k < 8; k++)
            {
                float angle = k * Mathf.PI / 4;
                var directAccess = to + new Vector3(Mathf.Sin(angle) * 2, 0, Mathf.Cos(angle) * 2);
                var direct = new List<Vector3>();
                if (NavigationGround(directAccess, out directAccess) && NavigationSegment(from, directAccess, direct))
                { NavigationStatus = "Direct walking route available."; return direct.ToArray(); }
            }
        }
        if (validEdges==null)
        {
            Physics.SyncTransforms();
            validEdges=new List<Vector2Int>();
            if (routeEdges != null) foreach(var e in routeEdges) if(e.x>=0 && e.y>=0 && e.x<count && e.y<count && NavigationSegment(routeNodes[e.x],routeNodes[e.y])) validEdges.Add(e);
        }
        var distance=new float[count];var previous=new int[count];var used=new bool[count];
        for(int i=0;i<count;i++) {distance[i]=float.PositiveInfinity;previous[i]=-1;
            if(Vector3.Distance(from,routeNodes[i])<26 && NavigationSegment(from,routeNodes[i])) distance[i]=Vector3.Distance(from,routeNodes[i]);}
        for(int step=0;step<count;step++)
        {
            int u=-1;for(int i=0;i<count;i++)if(!used[i]&&(u<0||distance[i]<distance[u]))u=i;
            if(u<0||float.IsPositiveInfinity(distance[u]))break;used[u]=true;
            foreach(var e in validEdges){int v=e.x==u?e.y:e.y==u?e.x:-1;if(v<0)continue;float d=distance[u]+Vector3.Distance(routeNodes[u],routeNodes[v]);if(d<distance[v]){distance[v]=d;previous[v]=u;}}
        }
        int end=-1;float best=float.PositiveInfinity;Vector3 access=default;
        // Finish beside the interaction, not inside its solid visual prop.
        for(int k=0;k<8;k++)
        {
            float angle=k*Mathf.PI/4;var p=to+new Vector3(Mathf.Sin(angle)*2,0,Mathf.Cos(angle)*2);
            if(!NavigationGround(p,out p))continue;
            for(int i=0;i<count;i++)if(!float.IsPositiveInfinity(distance[i])&&Vector3.Distance(p,routeNodes[i])<24)
            {float cost=distance[i]+Vector3.Distance(p,routeNodes[i]);if(cost<best&&NavigationSegment(routeNodes[i],p)){best=cost;end=i;access=p;}}
        }
        if(end<0) return RetryWalkingRoute(from, to, "No connected approach to the task.");
        var corners=new List<Vector3>{access};for(int n=end,guard=0;n>=0&&guard<count;n=previous[n],guard++)corners.Add(routeNodes[n]);corners.Add(from);corners.Reverse();
        var points=new List<Vector3>();for(int i=1;i<corners.Count;i++)if(!NavigationSegment(corners[i-1],corners[i],points)) { return RetryWalkingRoute(from, to, "Blocked final segment: " + corners[i-1] + " -> " + corners[i]); }
        NavigationStatus = "Walking route available."; return points.ToArray();
    }
    private Vector3[] RetryWalkingRoute(Vector3 from, Vector3 to, string failure)
    {
        NavigationStatus = failure;
        // A temporary actor may have blocked an edge when the graph was first sampled.
        // Rebuild only on failure, and retry at most once with all clearance checks intact.
        if (!Application.isPlaying || retryingWalkingRoute) return System.Array.Empty<Vector3>();
        validEdges = null;
        retryingWalkingRoute = true;
        try { return WalkingRoute(from, to); }
        finally { retryingWalkingRoute = false; }
    }
    readonly Collider[] guidanceOverlaps = new Collider[24];
    public Vector3[] GuidanceRoute(Vector3 from, Vector3 to, int task)
    {
        var walking = WalkingRoute(from, to);
        if (walking.Length > 0 || !navLayout) return walking;
        if (task != 1)
        {
            bool onIsland = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(objectives[1].position.x, objectives[1].position.z)) < 18;
            if ((!movement || !movement.SwimmingActive) && !onIsland) return walking;
            var volumes = FindObjectsByType<WaterVolume>(FindObjectsSortMode.None);
            foreach (var volume in volumes)
            {
                if (volume.gameObject.scene != gameObject.scene || !volume.IsValid || !volume.ContainsSurfaceXZ(from)) continue;
                var crossing = new List<Vector3>();
                var waterFrom = from;
                if (onIsland && (!movement || !movement.SwimmingActive))
                {
                    if (!AddWaterGuidance(from, navLayout.IslandSwimExit, volume, crossing) ||
                        !AddWaterGuidance(navLayout.IslandSwimExit, navLayout.IslandSwimEntry, volume, crossing)) continue;
                    waterFrom = navLayout.IslandSwimEntry;
                }
                bool clear = AddWaterGuidance(waterFrom, navLayout.IntroShorePosition, volume, crossing);
                if (!clear)
                {
                    crossing.Clear();
                    if (onIsland && (!movement || !movement.SwimmingActive))
                    {
                        if (!AddWaterGuidance(from, navLayout.IslandSwimExit, volume, crossing) ||
                            !AddWaterGuidance(navLayout.IslandSwimExit, navLayout.IslandSwimEntry, volume, crossing)) continue;
                    }
                    clear = AddWaterGuidance(waterFrom, navLayout.LakePosition, volume, crossing) && AddWaterGuidance(navLayout.LakePosition, navLayout.IntroShorePosition, volume, crossing);
                }
                if (!clear) continue;
                var land = WalkingRoute(navLayout.IntroShorePosition, to);
                if (land.Length == 0) continue;
                crossing.AddRange(land);
                NavigationStatus = "Golden guidance leads out of the water toward the tracked task.";
                return crossing.ToArray();
            }
            return walking;
        }
        var entry = navLayout.IslandSwimEntry;
        var exit = navLayout.IslandSwimExit;
        bool swimming = movement && movement.SwimmingActive;
        float shoreDistance = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(navLayout.IntroShorePosition.x, navLayout.IntroShorePosition.z));
        if (!swimming && shoreDistance > 6 && Vector3.Distance(from, exit) > 12)
        {
            var shore = WalkingRoute(from, navLayout.IntroShorePosition);
            if (shore.Length > 0) NavigationStatus = "Golden guidance leads to the swimming shore.";
            return shore;
        }
        var water = FindObjectsByType<WaterVolume>(FindObjectsSortMode.None);
        WaterVolume lake = null;
        foreach (var volume in water)
            if (volume.gameObject.scene == gameObject.scene && volume.IsValid && volume.VolumeCollider.bounds.Contains(new Vector3(entry.x, volume.WaterSurfaceY - .1f, entry.z))) { lake = volume; break; }
        if (!lake) return System.Array.Empty<Vector3>();
        var points = new List<Vector3>(80);
        if (!AddWaterGuidance(from, entry, lake, points) || !AddWaterGuidance(entry, exit, lake, points))
        { NavigationStatus = "The swimming approach is obstructed."; return System.Array.Empty<Vector3>(); }
        var remaining = WalkingRoute(exit, to);
        if (remaining.Length == 0)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                var access = to + new Vector3(Mathf.Sin(angle) * 2, 0, Mathf.Cos(angle) * 2);
                var dry = new List<Vector3>();
                if (NavigationGround(access, out access) && NavigationSegment(exit, access, dry)) { remaining = dry.ToArray(); break; }
            }
        }
        if (remaining.Length > 0) points.AddRange(remaining);
        NavigationStatus = "Golden guidance follows the water crossing and island ramp.";
        return points.ToArray();
    }
    bool AddWaterGuidance(Vector3 from, Vector3 to, WaterVolume lake, List<Vector3> points)
    {
        int steps = Mathf.CeilToInt(new Vector2(from.x-to.x, from.z-to.z).magnitude / .6f);
        if (steps > 220) return false;
        for (int i = 0; i <= steps; i++)
        {
            var at = Vector3.Lerp(from, to, steps == 0 ? 0 : (float)i / steps);
            var hits = Physics.RaycastAll(new Vector3(at.x, 60, at.z), Vector3.down, 90, ~0, QueryTriggerInteraction.Ignore);
            bool found = false; RaycastHit floor = default;
            foreach (var hit in hits)
                if (IsWalkingSurface(hit.collider) && (!found || hit.point.y > floor.point.y)) { floor = hit; found = true; }
            if (!found) return false;
            bool wet = floor.point.y < lake.WaterSurfaceY - .55f && lake.ContainsSurfaceXZ(at);
            if (!wet && Vector3.Angle(floor.normal, Vector3.up) > 45 && floor.point.y >= lake.WaterSurfaceY) return false;
            float rootY = wet ? Mathf.Max(floor.point.y + .08f, lake.WaterSurfaceY - .9f) : floor.point.y + .08f;
            var root = new Vector3(at.x, rootY, at.z);
            int count = Physics.OverlapCapsuleNonAlloc(root + Vector3.up * .4f, root + Vector3.up * 1.4f, .27f, guidanceOverlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == guidanceOverlaps.Length) return false;
            for (int n = 0; n < count; n++)
                if (!(player && guidanceOverlaps[n].transform.IsChildOf(player)) && !IsWalkingSurface(guidanceOverlaps[n])) return false;
            points.Add(new Vector3(at.x, Mathf.Max(floor.point.y + .15f, wet ? lake.WaterSurfaceY + .05f : floor.point.y + .15f), at.z));
        }
        return true;
    }
    void ClearGuidanceVisuals()
    {
        if(route) route.positionCount=0;
        if(goldGuidance) goldGuidance.Clear();
        lastRouteTarget=-2;
    }
    public void RefreshGuidancePresentation()
    {
        ClearGuidanceVisuals();
        if (!Flow || !Flow.WorldHudHidden) UpdateRoute();
    }
    private void UpdateRoute()
    {
        if (!route) return;
        lastRoutePosition = player.position; lastRouteTarget = Tracked;
        if (GuidingTask < 0 || (Flow && Flow.WorldHudHidden)) { ClearGuidanceVisuals(); return; }
        var points = GuidanceRoute(player.position, objectives[Tracked].position, Tracked);
        route.positionCount = 0;
        if (goldGuidance) goldGuidance.SetPath(points);
    }
    Vector2 journalScroll;
    Texture2D[] journalRelics;
    string JournalObjective(int index) => index==1 && navLayout
        ? "Read the stone on the lake island. Follow the golden wisps along the shore ramp, water crossing and island steps." : descriptions[index];
    int selectedJournalTask=-1;
    GUIStyle journalTitle, journalBody, journalSmall, journalAction;
    public string NextTaskAction(int i) => completed[i]?"Discovery recorded. No return trip needed.":
        i==0?"Recover the western survey records.":i==1?"Read the inscription on the lake island.":"Restore the eastern relay.";
    public static string ElevationLabel(float delta) => Mathf.Abs(delta)<1.5f?"Same level":
        delta>0?"Above +"+Mathf.CeilToInt(delta)+" m":"Below "+Mathf.CeilToInt(Mathf.Abs(delta))+" m";
    void JournalStyles()
    {
        if(journalTitle!=null)return;
        journalBody=new GUIStyle(WayfarerUITheme.JournalBodyStyle){fontSize=17,wordWrap=true};journalBody.normal.textColor=WayfarerUITheme.Ink;
        journalTitle=new GUIStyle(journalBody){fontSize=26,fontStyle=FontStyle.Bold};
        journalAction=new GUIStyle(journalBody){fontSize=21,fontStyle=FontStyle.Bold};
        journalSmall=new GUIStyle(journalBody){fontSize=13};journalSmall.normal.textColor=new Color32(93,107,86,255);
        foreach(var style in new[]{journalTitle,journalBody,journalAction,journalSmall})WayfarerUITheme.KeepLabelColor(style);
    }
    private void DrawDiscoveries(Rect area)
    {
        JournalStyles();
        if(journalRelics==null)journalRelics=new[]{Resources.Load<Texture2D>("P3Rewards/IslandRelicIconV6"),Resources.Load<Texture2D>("P3Rewards/RidgeCharmIconV6"),Resources.Load<Texture2D>("P3Rewards/RunicGuardIconV6")};
        float left=area.x+28,leftWidth=(area.width-24)*.36f-34,right=area.x+12+(area.width-24)*.36f+30,rightWidth=area.xMax-28-right;
        GUI.Label(new Rect(left,area.y+38,leftWidth,40),"Tasks",journalTitle);
        GUI.Label(new Rect(left,area.y+80,leftWidth,24),"YOUR DISCOVERIES",journalSmall);
        if(selectedJournalTask<0 || selectedJournalTask>=3 || !accepted[selectedJournalTask]){selectedJournalTask=-1;for(int i=0;i<3;i++)if(accepted[i]){selectedJournalTask=i;break;}}
        int row=0,hidden=0;
        for(int i=0;i<3;i++)
        {
            if(!accepted[i]){hidden++;continue;}
            string state=completed[i]?"RECORDED":GuidingTask==i?"TRACKING":"AVAILABLE";
            var rect=new Rect(left,area.y+117+row*77,leftWidth,68);
            if(selectedJournalTask==i){Color c=GUI.color;GUI.color=c*new Color(.78f,.67f,.42f,.32f);GUI.DrawTexture(new Rect(rect.x-6,rect.y-3,rect.width+9,rect.height+6),Texture2D.whiteTexture);GUI.color=c;}
            if(GUI.Button(rect,titles[i]+"\n"+state)) {selectedJournalTask=i;journalScroll=Vector2.zero;}
            if(selectedJournalTask==i){Color c=GUI.color;GUI.color*=WayfarerUITheme.Gold;GUI.DrawTexture(new Rect(rect.x-5,rect.y,3,rect.height),Texture2D.whiteTexture);GUI.color=c;}
            row++;
        }
        if(hidden>0)GUI.Label(new Rect(left,area.y+120+row*77,leftWidth,45),hidden+" clues remain undiscovered",journalSmall);
        float relicY=area.yMax-183;
        GUI.Label(new Rect(left,relicY,leftWidth,25),"RELIC COLLECTION",journalSmall);
        for(int i=0;i<3;i++)
        {
            var rect=new Rect(left+i*(leftWidth/3),relicY+31,leftWidth/3-6,78);
            Color c=GUI.color;if(!opened[i])GUI.color=c*new Color(.28f,.34f,.26f,.7f);
            if(journalRelics[i])GUI.DrawTexture(new Rect(rect.x+4,rect.y,rect.width-8,52),journalRelics[i],ScaleMode.ScaleToFit,true);GUI.color=c;
            GUI.Label(new Rect(rect.x,rect.y+54,rect.width,22),opened[i]?"Collected":"?",journalSmall);
            if(rect.Contains(Event.current.mousePosition))
            {
                var loadout=player.GetComponent<BaselineLoadout>();
                string detail=opened[i]?(loadout?loadout.EquipmentName(i)+"\n"+loadout.EquipmentEffect(i):equipment[i]):"Undiscovered relic";
                GUI.Label(new Rect(right,area.yMax-93,rightWidth,45),detail,journalSmall);
            }
        }
        if(selectedJournalTask>=0)
        {
            int i=selectedJournalTask;
            GUI.Label(new Rect(right,area.y+38,rightWidth,40),titles[i],journalTitle);
            GUI.Label(new Rect(right,area.y+87,rightWidth,25),completed[i]?"DISCOVERY RECORDED":"CURRENT ACTION",journalSmall);
            float actionHeight=journalAction.CalcHeight(new GUIContent(NextTaskAction(i)),rightWidth);
            GUI.Label(new Rect(right,area.y+123,rightWidth,actionHeight),NextTaskAction(i),journalAction);
            float y=area.y+133+actionHeight;
            string status=completed[i]?"This discovery is complete.":GuidingTask==i?"Follow the golden wisps to your objective.":CustomGuidanceActive?"A map destination is guiding you. Track this task to switch back.":"Select Track to show this objective in the world.";
            float statusHeight=journalBody.CalcHeight(new GUIContent(status),rightWidth);
            GUI.Label(new Rect(right,y,rightWidth,statusHeight),status,journalBody);y+=statusHeight+24;
            GUI.Label(new Rect(right,y,rightWidth,25),"FIELD NOTES",journalSmall);Color rule=GUI.color;GUI.color=WayfarerUITheme.Gold;GUI.DrawTexture(new Rect(right,y+26,rightWidth,1),Texture2D.whiteTexture);GUI.color=rule;y+=36;
            float viewport=Mathf.Max(55,area.yMax-164-y),content=journalBody.CalcHeight(new GUIContent(JournalObjective(i)),rightWidth-18);
            journalScroll=GUI.BeginScrollView(new Rect(right,y,rightWidth,viewport),journalScroll,new Rect(0,0,rightWidth-18,content));
            GUI.Label(new Rect(0,0,rightWidth-18,content),JournalObjective(i),journalBody);GUI.EndScrollView();
            if(!completed[i] && GUI.Button(new Rect(right,area.yMax-140,rightWidth,38),GuidingTask==i?"Stop tracking":"Track task"))Track(GuidingTask==i?-1:i);
        }
        else GUI.Label(new Rect(right,area.y+95,rightWidth,150),"Speak to the guides and read the notices to discover a task.",journalAction);
        WayfarerUITheme.Footer(area,"J / Esc  Close     ·     M  Map     ·     PAUSED");
        if(GUI.Button(new Rect(left,area.yMax-35,150,28),GUIContent.none,GUIStyle.none))SetJournal(false);
    }

    static void HudLabel(Rect rect,string value,GUIStyle style)
    {
        Color previous=GUI.color;GUI.color=new Color(0,0,0,.85f);GUI.Label(new Rect(rect.x+1,rect.y+1,rect.width,rect.height),value,style);
        GUI.color=previous;GUI.Label(rect,value,style);
    }
    private void OnGUI()
    {
        WayfarerUITheme.Apply();
        if (Flow && Flow.BlockWorldInput && !JournalOpen) return;
        if (text == null) { text = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true }; heading = new GUIStyle(text) { fontSize = 21, fontStyle = FontStyle.Bold }; }
        float scale = Mathf.Clamp(Screen.height / 800f, .65f, 1.5f);
        var savedMatrix=GUI.matrix;
        try
        {
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale;
        bool showWorld=!(Flow && Flow.WorldHudHidden);
        int guidingTask=GuidingTask;
        if (showWorld && (guidingTask>=0 || CustomGuidanceActive) && !(Flow && Flow.Tutorial && Flow.Tutorial.Active))
        {
            float canvasScale=Mathf.Sqrt(Screen.width/1280f*Screen.height/720f);
            float taskY=(BaselineMinimap.TopInset+BaselineMinimap.Diameter+34)*canvasScale/scale;
            string name=CustomGuidanceActive?"Destination: "+GuidanceMap.WaypointName:titles[guidingTask];
            string action=CustomGuidanceActive?"Direction only · No route":NextTaskAction(guidingTask);
            Vector3 destination=CustomGuidanceActive?GuidanceMap.WaypointPosition:objectives[guidingTask].position;
            Vector3 delta=destination-player.position;
            string elevation=CustomGuidanceActive&&!GuidanceMap.WaypointHasElevation?"Height unknown":ElevationLabel(delta.y);delta.y=0;
            float inner=Mathf.Min(280,width*.30f),x=32;
            float nameH=heading.CalcHeight(new GUIContent(name),inner),actionH=text.CalcHeight(new GUIContent(action),inner);
            Color prior=GUI.color;GUI.color=WayfarerUITheme.Gold;GUI.DrawTexture(new Rect(21,taskY+7,2,nameH+actionH+26),Texture2D.whiteTexture);GUI.color=prior;
            HudLabel(new Rect(x,taskY,inner,nameH),name,heading);taskY+=nameH+2;
            HudLabel(new Rect(x,taskY,inner,actionH),action,text);taskY+=actionH+4;
            HudLabel(new Rect(x,taskY,inner,38),Mathf.RoundToInt(delta.magnitude)+" m · "+elevation,text);
        }
        if (showWorld && noticeRemaining > 0)
        {
            const float noticeWidth = 500f;
            float textHeight = WayfarerUITheme.NoticeTextStyle.CalcHeight(new GUIContent(notice), noticeWidth - 48f);
            float noticeHeight = Mathf.Max(66f, textHeight + 28f);
            var panelRect = new Rect(width / 2f - noticeWidth / 2f, 190f, noticeWidth, noticeHeight);
            GUI.Box(panelRect, GUIContent.none, WayfarerUITheme.NoticePanelStyle);
            GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 14f, panelRect.width - 48f, panelRect.height - 28f), notice, WayfarerUITheme.NoticeTextStyle);
        }
        if (showWorld && guidingTask >= 0 && Camera.main)
        {
            var p = Camera.main.WorldToScreenPoint(objectives[guidingTask].position + Vector3.up * 2.7f);
            if (p.z > 0) GUI.Label(new Rect(Mathf.Clamp(p.x/scale-70,12,width-182),Mathf.Clamp((Screen.height-p.y)/scale-20,90,Screen.height/scale-90),170,45),"TASK LOCATION",text);
        }
        float bookScale=Mathf.Min(Screen.width/1000f,Screen.height/700f);
        GUI.matrix=Matrix4x4.Scale(Vector3.one*bookScale);
        var journalRect=new Rect((Screen.width/bookScale-880)/2,(Screen.height/bookScale-600)/2,880,600);
        if (!WayfarerUITheme.BeginPanel("discoveries",journalRect,JournalOpen,.36f)) return;
        DrawDiscoveries(journalRect);
        WayfarerUITheme.EndPanel();
        }
        finally { GUI.matrix=savedMatrix; }
    }
}
