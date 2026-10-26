#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit Editor checks only. Does not create a scene, change progress or run a generator.
public static class OverworldV2GeometryChecks
{
    const string MainScene = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    const string ReportPath = "ToolReports/MapV2Migration/geometry-checks.txt";
    const float GridStep = 2f, GridMinimum = -84f, SwimDepth = .9f;
    const int GridCount = 85;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    sealed class CheckContext
    {
        public Scene scene;
        public OverworldV2Layout layout;
        public BaselineWorld world;
        public MeshCollider terrain;
        public WaterVolume[] waters;
        public readonly StringBuilder report = new StringBuilder();
        public int passed, failed;
        public void Result(bool value, string text)
        {
            if (value) passed++; else failed++;
            report.AppendLine((value ? "PASS " : "FAIL ") + text);
        }
        public void Section(string name, Action action)
        {
            report.AppendLine("\n[" + name + "]");
            try { action(); }
            catch (Exception exception) { Result(false, name + " exception: " + exception); }
        }
    }

    [MenuItem("Tools/Overworld V2/Run static geometry checks (current scene)")]
    public static void RunFromMenu() { Debug.Log(Run()); }

    public static string Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Run only in stable Edit Mode.");
        if (scene.path != MainScene || SceneManager.sceneCount != 1 || scene.isDirty)
            throw new InvalidOperationException("Require the saved, clean OverworldBaseline as the only open scene.");
        var layout = FindInScene<OverworldV2Layout>(scene);
        if (!layout) throw new InvalidOperationException("The active main scene has no OverworldV2Layout.");
        var world = layout.GetComponent<BaselineWorld>();
        if (!world || !world.player) throw new InvalidOperationException("Layout needs the existing world and player.");
        MeshCollider terrain = null;
        foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            if (collider.gameObject.scene == scene && collider.name == "LakeTerrain_REPLACE_OR_SCULPT") terrain = collider;
        if (!terrain || !terrain.sharedMesh) throw new InvalidOperationException("Missing active authored terrain MeshCollider.");

        var c = new CheckContext { scene = scene, layout = layout, world = world, terrain = terrain,
            waters = Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None) };
        bool originalBridge = layout.BridgeOpen;
        float originalTimeScale = Time.timeScale;
        c.report.AppendLine("OVERWORLD V2 STATIC EDITOR GEOMETRY CHECKS");
        c.report.AppendLine("UTC=" + DateTime.UtcNow.ToString("o", Invariant));
        c.report.AppendLine("Scene=" + scene.path + "; cleanAtStart=" + !scene.isDirty + "; Play=" + EditorApplication.isPlaying + "; TimeScale=" + F(originalTimeScale));
        c.report.AppendLine("Method: read-only physics/API queries except a temporary north-bridge active-state toggle. No input injection, no gameplay interactions, no player relocation, no Play Mode.");
        c.report.AppendLine("Static sampling is not continuous character movement, actual E interaction, swim survival, camera comfort or a complete playthrough.");
        try
        {
            layout.ApplyBridgeState(true);
            Physics.SyncTransforms();
            c.Result(layout.BridgeOpen, "Temporary north bridge enabled for open-state checks.");
            c.Section("Mesh and missing references", () => CheckReferences(c));
            c.Section("Navigation edges and three objectives, bridge open", () => CheckNavigation(c));
            c.Section("Eight approach samples for each existing interaction", () => CheckInteractions(c));
            c.Section("Visible lake mesh and water volume coverage", () => CheckWaterMesh(c));
            c.Section("Departure shore to island rest depth profile", () => CheckSwimProfile(c));
            c.Section("Fixed 2 m projected area estimate, bridge open", () => CheckArea(c));
            c.Section("Closed bridge rejects camp-to-island walking route", () =>
            {
                layout.ApplyBridgeState(false);
                Physics.SyncTransforms();
                if (world.objectives == null || world.objectives.Length < 2 || !world.objectives[1])
                { c.Result(false, "Island objective reference is missing."); return; }
                var route = world.WalkingRoute(layout.CampPosition, world.objectives[1].position);
                c.Result(!layout.BridgeOpen && route.Length == 0,
                    "Closed bridge: camp to island has " + route.Length + " walking samples; " + world.NavigationStatus);
            });
        }
        finally
        {
            c.report.AppendLine("\n[Restoration]");
            try
            {
                layout.ApplyBridgeState(originalBridge);
                Physics.SyncTransforms();
                c.Result(layout.BridgeOpen == originalBridge, "North bridge restored to " + originalBridge + ".");
                // Entry required a clean scene. The only serialized mutation made above was
                // this bridge active flag, which has now been restored; queries change caches only.
                if (scene.isDirty)
                    c.Result(EditorSceneManager.SaveScene(scene), "Saved only the restored bridge state in the originally clean main scene.");
                else c.report.AppendLine("INFO Scene stayed clean; no scene save was needed.");
            }
            catch (Exception exception) { c.Result(false, "Restoration error: " + exception); }
            c.Result(!scene.isDirty, "Scene Saved/Clean=" + !scene.isDirty);
            c.Result(!EditorApplication.isPlaying && Mathf.Approximately(Time.timeScale, originalTimeScale),
                "Play=" + EditorApplication.isPlaying + "; TimeScale=" + F(Time.timeScale) + "; originalTimeScale=" + F(originalTimeScale));
            c.report.AppendLine("SUMMARY passed=" + c.passed + "; failed=" + c.failed + "; static evidence only.");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, c.report.ToString(), new UTF8Encoding(false));
        }
        return "Static checks: " + c.passed + " passed, " + c.failed + " failed. " + ReportPath;
    }

    static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (var component in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
            if (component.gameObject.scene == scene) return component;
        return null;
    }
    static string F(float value) { return value.ToString("0.###", Invariant); }
    static string V(Vector3 p) { return "(" + F(p.x) + "," + F(p.y) + "," + F(p.z) + ")"; }

    static void CheckReferences(CheckContext c)
    {
        int missingScripts = 0, brokenFields = 0;
        foreach (var root in c.scene.GetRootGameObjects())
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            if (count > 0) c.report.AppendLine("MISSING_SCRIPT " + transform.name + " count=" + count);
            missingScripts += count;
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                if (!component) continue;
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.NextVisible(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                    { brokenFields++; c.report.AppendLine("BROKEN_REFERENCE " + transform.name + "/" + component.GetType().Name + "/" + property.propertyPath); }
            }
        }
        c.Result(missingScripts == 0, "Missing MonoBehaviour scripts=" + missingScripts + " (all active/inactive main-scene objects).");
        c.Result(brokenFields == 0, "Broken serialized MonoBehaviour object references=" + brokenFields + "; intentional null fields are not considered missing.");
        string asset = AssetDatabase.GetAssetPath(c.terrain.sharedMesh);
        c.Result(!string.IsNullOrEmpty(asset), "Terrain mesh=" + asset + "; GUID=" + AssetDatabase.AssetPathToGUID(asset));
        c.report.AppendLine("Terrain bounds=" + c.terrain.bounds + "; vertices=" + c.terrain.sharedMesh.vertexCount + "; triangles=" + c.terrain.sharedMesh.triangles.Length / 3);
        c.Result(c.terrain.enabled && c.terrain.gameObject.activeInHierarchy, "Authored terrain collider active.");
    }

    static void CheckNavigation(CheckContext c)
    {
        var nodes = c.world.routeNodes; var edges = c.world.routeEdges;
        if (nodes == null || edges == null) { c.Result(false, "Missing route arrays."); return; }
        c.Result(nodes.Length > 0 && nodes.Length <= 96, "Route nodes=" + nodes.Length + "; runtime cap=96.");
        c.report.AppendLine("INFO Checking every current edge; latest migration expected 81, actual=" + edges.Length + ".");
        for (int i = 0; i < edges.Length; i++)
        {
            var e = edges[i]; bool validIndex = e.x >= 0 && e.y >= 0 && e.x < nodes.Length && e.y < nodes.Length;
            bool clear = validIndex && c.world.NavigationSegment(nodes[e.x], nodes[e.y]);
            c.Result(clear, "Edge " + i + " nodes " + e.x + "->" + e.y + (validIndex ? " " + V(nodes[e.x]) + " -> " + V(nodes[e.y]) : " invalid indices"));
        }
        for (int i = 0; i < 3; i++)
        {
            if (c.world.objectives == null || c.world.objectives.Length <= i || !c.world.objectives[i])
            { c.Result(false, "Objective " + i + " missing."); continue; }
            var route = c.world.WalkingRoute(c.layout.CampPosition, c.world.objectives[i].position);
            c.Result(route.Length > 0, "Camp to objective " + i + ": " + route.Length + " validated samples; " + c.world.NavigationStatus);
        }
    }

    static void CheckInteractions(CheckContext c)
    {
        int count = 0;
        foreach (var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (item.gameObject.scene != c.scene) continue;
            count++;
            var npc = item.GetComponent<NPCDialogue>();
            int clear = 0; var bits = new StringBuilder();
            for (int k = 0; k < 8; k++)
            {
                float angle = k * Mathf.PI / 4;
                Vector3 at = item.transform.position + new Vector3(Mathf.Sin(angle) * 2, 0, Mathf.Cos(angle) * 2);
                bool grounded = c.world.NavigationGround(at, out var ground);
                bool withinInteractionRange = npc && npc.InteractionDistance >= 2;
                bool valid = item.gameObject.activeInHierarchy && grounded && withinInteractionRange;
                if (valid) clear++;
                bits.Append(k).Append(valid ? ":clear " : ":blocked ");
            }
            c.Result(clear > 0, "Interaction " + item.name + " kind=" + item.action + " identity=" + item.index + " dry approach samples=" + clear + "/8; " + bits);
        }
        c.Result(count == 12, "Existing BaselineInteraction count=" + count + "; expected=12.");
        c.report.AppendLine("LIMIT: 2 m radial standing-space samples and configured planar interaction range only. Facing, trigger registration, E dialogue, rewards and completion are not executed.");
    }

    static bool TerrainAt(CheckContext c, float x, float z, out RaycastHit hit)
    {
        return c.terrain.Raycast(new Ray(new Vector3(x, 80, z), Vector3.down), out hit, 150);
    }
    static bool WaterAt(CheckContext c, Vector3 point, out float surface)
    {
        bool found = false; surface = float.NegativeInfinity;
        foreach (var water in c.waters)
        {
            if (!water || water.gameObject.scene != c.scene || !water.IsValid) continue;
            Vector3 probe = new Vector3(point.x, water.WaterSurfaceY - .1f, point.z);
            var volume = water.VolumeCollider;
            if (volume.bounds.Contains(probe) && (volume.ClosestPoint(probe) - probe).sqrMagnitude < .0001f)
            { found = true; surface = Mathf.Max(surface, water.WaterSurfaceY); }
        }
        return found;
    }
    static bool ClearBody(CheckContext c, Vector3 feet, Collider floor)
    {
        foreach (var other in Physics.OverlapCapsule(feet + Vector3.up * .45f, feet + Vector3.up * 1.55f, .28f, ~0, QueryTriggerInteraction.Ignore))
            if (other != floor && !(c.world.player && other.transform.IsChildOf(c.world.player))) return false;
        return true;
    }

    static void CheckWaterMesh(CheckContext c)
    {
        MeshFilter visibleWater = null;
        if (c.layout.MapSurfaces != null) foreach (var mesh in c.layout.MapSurfaces)
            if (mesh && mesh.name == "Lake_Surface_REPLACE") visibleWater = mesh;
        if (!visibleWater || !visibleWater.sharedMesh) { c.Result(false, "Visible lake mesh missing from map surfaces."); return; }
        var meshData = visibleWater.sharedMesh; var vertices = meshData.vertices; var triangles = meshData.triangles;
        if (triangles.Length > 300000) { c.Result(false, "Water mesh exceeds this bounded check's 100000-triangle limit."); return; }
        var used = new HashSet<int>(triangles); int tested = 0, uncovered = 0; var examples = new List<string>();
        Action<Vector3> sample = local =>
        {
            Vector3 p = visibleWater.transform.TransformPoint(local); tested++;
            bool covered = WaterAt(c, p, out var height) && Mathf.Abs(height - p.y) <= .05f;
            if (!covered) { uncovered++; if (examples.Count < 30) examples.Add(V(p)); }
        };
        foreach (int index in used) sample(vertices[index]);
        for (int i = 0; i < triangles.Length; i += 3)
            sample((vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f);
        c.Result(uncovered == 0, "Visible lake used vertices=" + used.Count + ", triangle centroids=" + triangles.Length / 3 + ", total samples=" + tested + ", uncovered=" + uncovered + ".");
        if (examples.Count > 0) c.report.AppendLine("UNCOVERED examples=" + string.Join("; ", examples));
        c.report.AppendLine("LIMIT: every referenced mesh vertex and triangle centroid sampled; continuous triangle interior is not proved. Coverage uses enabled water collider bounds+ClosestPoint at surface-0.1 m, and visible-water height tolerance 0.05 m. Player water rules are unchanged.");
    }

    static void CheckSwimProfile(CheckContext c)
    {
        var a = c.layout.IntroShorePosition; var b = c.layout.IslandRestPosition;
        float distance = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        int count = Mathf.CeilToInt(distance / .5f); float step = distance / count;
        var intervals = new List<string>(); float start = -1, deepest = 0, totalDeep = 0; int missing = 0;
        for (int i = 0; i <= count; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, (float)i / count); bool hasGround = TerrainAt(c, p.x, p.z, out var ground);
            bool hasWater = WaterAt(c, p, out var height); float depth = hasGround && hasWater ? height - ground.point.y : 0;
            if (!hasGround) missing++;
            deepest = Mathf.Max(deepest, depth); bool deep = hasGround && hasWater && depth >= SwimDepth;
            float along = step * i;
            c.report.AppendLine("DEPTH along=" + F(along) + " XZ=" + F(p.x) + "," + F(p.z) + " depth=" + F(depth) + " deep=" + deep + " ground=" + hasGround + " water=" + hasWater);
            if (deep && start < 0) start = Mathf.Max(0, along - step / 2);
            if ((!deep || i == count) && start >= 0)
            {
                float end = deep ? along : Mathf.Max(start, along - step / 2);
                intervals.Add(F(start) + ".." + F(end) + "m (length " + F(end - start) + "m)"); totalDeep += end - start; start = -1;
            }
        }
        c.Result(missing == 0, "Shore-rest profile length=" + F(distance) + " m; sample spacing=" + F(step) + " m; missing terrain=" + missing + ".");
        c.report.AppendLine("DEEP_SEGMENTS depth>=0.9 m: " + (intervals.Count > 0 ? string.Join("; ", intervals) : "none") + "; total=" + F(totalDeep) + " m; maximumDepth=" + F(deepest));
        c.report.AppendLine("LIMIT: straight-line terrain depth profile, interval boundaries approximated to half a sample. No real swimmer, stamina, entry/exit state transition or continuous input was tested.");
    }

    sealed class Cell { public bool land, water; public float y; }
    static void CheckArea(CheckContext c)
    {
        var cells = new Cell[GridCount * GridCount]; int rawLand = 0, rawWater = 0, rawOverlap = 0;
        for (int z = 0; z < GridCount; z++) for (int x = 0; x < GridCount; x++)
        {
            int index = z * GridCount + x; var cell = new Cell(); cells[index] = cell;
            float wx = GridMinimum + x * GridStep, wz = GridMinimum + z * GridStep;
            if (!TerrainAt(c, wx, wz, out var terrainHit)) continue;
            bool water = WaterAt(c, terrainHit.point, out var surface);
            float depth = water ? surface - terrainHit.point.y : 0;
            cell.water = water && depth >= SwimDepth && ClearBody(c, new Vector3(wx, surface - .8f, wz), c.terrain);
            cell.y = cell.water ? surface : terrainHit.point.y;
            // Highest clear authored floor supplies the projected land sample; ground itself
            // may be shallow wading ground. A bridge and its deep water share only one cell.
            var hits = Physics.RaycastAll(new Vector3(wx, 80, wz), Vector3.down, 150, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (!c.layout.IsNavigationSurface(hit.collider) || Vector3.Angle(hit.normal, Vector3.up) > 45) continue;
                if (water && surface - hit.point.y >= SwimDepth) continue;
                if (!ClearBody(c, hit.point, hit.collider)) continue;
                if (!cell.land || hit.point.y > cell.y) { cell.land = true; cell.y = hit.point.y; }
            }
            if (cell.land) rawLand++; if (cell.water) rawWater++; if (cell.land && cell.water) rawOverlap++;
        }
        int seed = -1; float nearest = 36;
        for (int z = 0; z < GridCount; z++) for (int x = 0; x < GridCount; x++)
        {
            int index = z * GridCount + x; if (!cells[index].land) continue;
            float distance = new Vector2(GridMinimum + x * GridStep - c.layout.ArrivalPosition.x,
                GridMinimum + z * GridStep - c.layout.ArrivalPosition.z).sqrMagnitude;
            if (distance < nearest) { nearest = distance; seed = index; }
        }
        if (seed < 0) { c.Result(false, "No clear land grid seed within 6 m of the arrival anchor."); return; }
        var reached = new bool[cells.Length]; var queue = new Queue<int>(); reached[seed] = true; queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            int index = queue.Dequeue(), x = index % GridCount, z = index / GridCount;
            foreach (int direction in new[] { -1, 1, -GridCount, GridCount })
            {
                if ((direction == -1 && x == 0) || (direction == 1 && x == GridCount - 1) ||
                    (direction == -GridCount && z == 0) || (direction == GridCount && z == GridCount - 1)) continue;
                int next = index + direction;
                if (reached[next] || (!cells[next].land && !cells[next].water)) continue;
                if (Mathf.Abs(cells[next].y - cells[index].y) > GridStep + .08f) continue;
                reached[next] = true; queue.Enqueue(next);
            }
        }
        int land = 0, waterCount = 0, overlap = 0, total = 0;
        for (int i = 0; i < cells.Length; i++) if (reached[i])
        { total++; if (cells[i].land) land++; if (cells[i].water) waterCount++; if (cells[i].land && cells[i].water) overlap++; }
        c.report.AppendLine("GRID fixed X/Z=-84..84 inclusive, spacing=2 m, cells=" + cells.Length + ", area per sample=4 m2; implied outer square=170x170 m=28900 m2.");
        c.report.AppendLine("RAW land incl shallow wading=" + rawLand * 4 + " m2; deep swim water=" + rawWater * 4 + " m2; overlap=" + rawOverlap * 4 + " m2.");
        c.report.AppendLine("ARRIVAL_CONNECTED land=" + land * 4 + " m2; water=" + waterCount * 4 + " m2; overlap=" + overlap * 4 + " m2; deduplicated union=" + total * 4 + " m2.");
        c.Result(total > 0, "Area estimate has a connected arrival seed; offset=" + F(Mathf.Sqrt(nearest)) + " m. This passes estimator execution, not gameplay traversal.");
        c.report.AppendLine("RULES: terrain-collider raycasts exclude empty exterior; authored walking floors require <=45-degree normal and the original navigation-sized body capsule clear; depth <0.9 m is walk/wade, >=0.9 m is swim. Grid neighbors use four directions and <=2.08 m representative height change. Bridge deck and water under it are deduplicated in XZ.");
        c.report.AppendLine("LIMITS: planning estimate only. No edge sweep between area samples, no continuous WASD, no stamina constraint, no full climbing, no mesh-exact obstacle area. Narrow passages, sub-grid blockers and stacked floors may be misclassified. Terrain-relative water and capsule checks do not prove character water states. No old-map comparison or area multiplier is inferred.");
    }
}
#endif
