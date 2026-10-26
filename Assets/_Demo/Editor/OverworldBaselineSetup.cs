using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OverworldBaselineSetup
{
    public const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    private const string Data = "Assets/_Demo/BaselineData";
    private static Transform root;
    private static Material ground, stone, wood, gold, cyan, green, water;
    // Initial 108-unit greybox generator only. Expanded scene is edited in place;
    // this menu does NOT reproduce its 170-unit terrain, regions or routes.
    [MenuItem("ArtLab/Baseline/Create INITIAL A2 greybox (not expanded layout)")]
    public static void Create()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Exit Play Mode and save the current scene first.");
        if (File.Exists(ScenePath)) throw new InvalidOperationException("Baseline already exists; edit it directly rather than overwrite it.");
        Directory.CreateDirectory(Data);
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.OpenScene("Assets/StartScene.unity");
        EditorSceneManager.SaveScene(scene, ScenePath);
        var keep = new HashSet<string> { "Main Camera", "Directional Light", "GameSession", "_Gameplay", "_UI", "SideWorldTravel", "SideWorldA_Guide", "SideWorldB_Guide" };
        foreach (var obj in scene.GetRootGameObjects()) if (!keep.Contains(obj.name)) UnityEngine.Object.DestroyImmediate(obj);
        root = new GameObject("_A2_Greybox").transform;
        ground = Mat("Ground", new Color(.39f, .53f, .35f));
        stone = Mat("Ruins", new Color(.68f, .67f, .59f));
        wood = Mat("Clues", new Color(.42f, .26f, .12f));
        gold = Mat("Equipment", new Color(.85f, .57f, .12f));
        cyan = Mat("HologramAndRoute", new Color(.12f, .85f, .94f), true);
        green = Mat("TreeCanopy", new Color(.20f, .38f, .23f));
        water = Mat("LakeSurface", new Color(.12f, .48f, .64f));
        Terrain();
        var lake = Shape("Lake_Surface_REPLACE", PrimitiveType.Cylinder, new Vector3(0, -.08f, 0), new Vector3(44, .04f, 44), water, false);
        var volume = new GameObject("Lake_SwimmingVolume"); volume.transform.SetParent(root); volume.transform.position = new Vector3(0, -3, 0);
        var box = volume.AddComponent<BoxCollider>(); box.size = new Vector3(44, 6, 44); box.isTrigger = true;
        volume.AddComponent<WaterVolume>();
        for (int z = -23; z <= -7; z += 2)
            Shape("Causeway_Segment_REPLACE", PrimitiveType.Cube, new Vector3(0, .75f, z), new Vector3(3, .5f, 2.03f), stone);
        // Safe fallback walking boundaries; no expensive distant environment.
        Shape("Boundary_W", PrimitiveType.Cube, new Vector3(-54, 5, 0), new Vector3(2, 14, 110), stone);
        Shape("Boundary_E", PrimitiveType.Cube, new Vector3(54, 5, 0), new Vector3(2, 14, 110), stone);
        Shape("Boundary_N", PrimitiveType.Cube, new Vector3(0, 5, 54), new Vector3(110, 14, 2), stone);
        Shape("Boundary_S", PrimitiveType.Cube, new Vector3(0, 5, -54), new Vector3(110, 14, 2), stone);

        var player = GameObject.Find("_Gameplay/Player"); player.transform.position = new Vector3(0, 1.15f, -39); player.transform.rotation = Quaternion.identity;
        GameObject.Find("_Gameplay/PlayerSpawnPoint").transform.position = player.transform.position;
        var camera = Camera.main; camera.transform.position = player.transform.position + new Vector3(0, 4, -7); camera.transform.rotation = Quaternion.Euler(16, 0, 0); camera.farClipPlane = 180;
        var world = new GameObject("BaselineWorld").AddComponent<BaselineWorld>(); world.player = player.transform; world.routeMaterial = cyan;
        var guide = GameObject.Find("_Gameplay/IntroNPC"); guide.transform.position = new Vector3(-3, 1, -34);
        SimplifyNPC(guide, cyan);
        Configure(guide.GetComponent<NPCDialogue>(), "Domain Guide", new[] { "This lake is yours to explore. Take the practice sword; there is no required route.", "A survey cylinder was lost in the western grove. I will record the clue. Press J if you want directions." });
        Action(guide, BaselineInteraction.ActionKind.AcceptTask, 0);
        Label("GUIDE", guide.transform.position + Vector3.up * 3);
        Sign("Island notice", new Vector3(7, 1, -33), "A stone on the lake island records a broken memory. Read it if you wish. This is optional.", BaselineInteraction.ActionKind.AcceptTask, 1);
        Sign("Eastern notice", new Vector3(-9, 1, -32), "The eastern relay has gone quiet. Someone could activate it again. This is optional.", BaselineInteraction.ActionKind.AcceptTask, 2);

        var west = Interactable("Survey cylinder", new Vector3(-32, 1, -5), "You found the lost survey. Its observations are now recorded.", BaselineInteraction.ActionKind.Objective, 0);
        Shape("Survey_PROP_REPLACE", PrimitiveType.Cylinder, west.transform.position + Vector3.up * .45f, new Vector3(.7f, .45f, .7f), cyan, false).transform.SetParent(west.transform, true);
        var island = Interactable("Island inscription", new Vector3(0, 1, 1), "The stone says: 'The sky is rendered. The boundary is maintained.' Nearby broken stone flickers with light.", BaselineInteraction.ActionKind.Objective, 1);
        Shape("InscribedStone_REPLACE", PrimitiveType.Cube, island.transform.position + Vector3.up * .8f, new Vector3(1.6f, 1.6f, .4f), stone).transform.SetParent(island.transform, true);
        var east = Interactable("Eastern relay", new Vector3(31, 1, 3), "You activate the silent relay. The observation has been recorded; no return trip is needed.", BaselineInteraction.ActionKind.Objective, 2);
        Shape("Relay_PROP_REPLACE", PrimitiveType.Cylinder, east.transform.position + Vector3.up * .6f, new Vector3(1, .6f, 1), cyan).transform.SetParent(east.transform, true);
        world.objectives = new[] { west.transform, island.transform, east.transform };

        // One small island ruin: the chest sits behind walls rather than on a quest marker.
        Shape("IslandWall_REPLACE", PrimitiveType.Cube, new Vector3(-3, 2.2f, 2.5f), new Vector3(.5f, 2.4f, 5), stone);
        Shape("IslandBackWall_REPLACE", PrimitiveType.Cube, new Vector3(-4.3f, 2, 2), new Vector3(3, 2, .5f), stone);
        Arch(new Vector3(2.3f, 1, 3), "IslandBrokenArch", false);
        var fragment = Shape("HologramMissingQuarter_REPLACE", PrimitiveType.Cube, new Vector3(3.5f, 3.8f, 3), new Vector3(.75f, .8f, .55f), cyan, false);
        world.hologram = fragment.GetComponent<Renderer>();
        Chest("Island hidden chest", new Vector3(-4.5f, 1, 3.3f), 0);
        Shape("WestHiddenWall_REPLACE", PrimitiveType.Cube, new Vector3(-39, 2.8f, 3), new Vector3(5, 3.6f, 2), stone);
        Chest("Western hidden chest", new Vector3(-40, 1, 6.2f), 1);
        // Optional climb shortcut beside a walkable route around the west wall.
        Shape("ClimbShortcut", PrimitiveType.Cube, new Vector3(-36.5f, 1.65f, 5), new Vector3(2, 1.3f, 2), stone);
        Shape("ClimbLanding", PrimitiveType.Cube, new Vector3(-36.5f, 2.15f, 7), new Vector3(3, 2.3f, 2), stone);
        Chest("Sealed eastern chest", new Vector3(33, 1, 15), 2);
        Sign("Weathered inscription", new Vector3(27, 1, 10), "The old order reads: SUN, then WAVE, then LEAF. A wrong symbol restarts the seal.", null, 0);
        string[] runeNames = { "WAVE", "LEAF", "SUN" };
        for (int i = 0; i < 3; i++)
        {
            var pos = new Vector3(29 + 3 * i, 1, 11);
            var rune = Interactable(runeNames[i] + " rune", pos, "Touch the " + runeNames[i] + " rune.", BaselineInteraction.ActionKind.Rune, i);
            Shape("RunePillar_REPLACE", PrimitiveType.Cylinder, pos + Vector3.up * .65f, new Vector3(.7f, .65f, .7f), stone).transform.SetParent(rune.transform, true);
            Label(runeNames[i], pos + Vector3.up * 1.8f);
        }
        for (int i = 0; i < 5; i++) Shape("EasternRuinWall_REPLACE", PrimitiveType.Cube, new Vector3(38, 1.7f, 1 + 4 * i), new Vector3(.6f, 1.4f, 2.5f), stone);
        SideGuide("SideWorldA_Guide", new Vector3(-31, 1, 14), "SIDE WORLD A");
        SideGuide("SideWorldB_Guide", new Vector3(29, 1, -16), "SIDE WORLD B");
        Tower();
        for (int i = 0; i < 18; i++)
        {
            float angle = i * 2.39996f; float radius = 43 + (i % 3) * 2;
            var pos = new Vector3(Mathf.Cos(angle) * radius, 1, Mathf.Sin(angle) * radius);
            Shape("TreeTrunk_REPLACE", PrimitiveType.Cylinder, pos + Vector3.up * 1.4f, new Vector3(.7f, 1.4f, .7f), wood);
            Shape("TreeCanopy_REPLACE", PrimitiveType.Sphere, pos + Vector3.up * 3.6f, new Vector3(4, 4, 4), green, false);
        }
        Label("LAKE DOMAIN / A2 BASELINE", new Vector3(0, 5, -30));
        Routes(world);
        RenderSettings.fog = true; RenderSettings.fogColor = new Color(.64f, .76f, .82f); RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 95; RenderSettings.fogEndDistance = 175;
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("A2_BASELINE_CREATED: three optional tasks; one sealed and two hidden chests; old scenes preserved.");
    }
    private static Material Mat(string name, Color color, bool unlit = false)
    {
        var m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(m, Data + "/" + name + ".mat"); return m;
    }
    private static GameObject Shape(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material material, bool collision = true)
    {
        var g = GameObject.CreatePrimitive(type); g.name = name; g.transform.SetParent(root); g.transform.position = pos; g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = material;
        if (!collision) UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }
    private static void Terrain()
    {
        const int n = 109; var v = new Vector3[n * n]; var uv = new Vector2[v.Length]; var tris = new int[(n - 1) * (n - 1) * 6]; int t = 0;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            float px = x - 54, pz = z - 54, r = Mathf.Sqrt(px * px + pz * pz);
            float h = r < 6 ? 1 : r < 10 ? Mathf.Lerp(1, -3, (r - 6) / 4) : r < 18 ? -3 : r < 24 ? Mathf.Lerp(-3, 1, (r - 18) / 6) : 1;
            int i = z * n + x; v[i] = new Vector3(px, h, pz); uv[i] = new Vector2(x / 20f, z / 20f);
            if (x == n - 1 || z == n - 1) continue;
            tris[t++] = i; tris[t++] = i + n; tris[t++] = i + 1; tris[t++] = i + 1; tris[t++] = i + n; tris[t++] = i + n + 1;
        }
        var mesh = new Mesh { name = "CompactLakeTerrain", vertices = v, uv = uv, triangles = tris }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Data + "/LakeTerrain.asset");
        var go = new GameObject("LakeTerrain_REPLACE_OR_SCULPT", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider)); go.transform.SetParent(root);
        go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshCollider>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = ground;
    }
    private static void Configure(NPCDialogue dialogue, string name, string[] lines)
    {
        var so = new SerializedObject(dialogue); so.FindProperty("npcName").stringValue = name; so.FindProperty("useSwordRewardDialogue").boolValue = false;
        so.FindProperty("forwardDotThreshold").floatValue = -.2f; so.FindProperty("interactionDistance").floatValue = 3;
        so.FindProperty("interactionPrompt").stringValue = "[E] " + name;
        var a = so.FindProperty("dialogueLines"); a.arraySize = lines.Length;
        for (int i = 0; i < lines.Length; i++) a.GetArrayElementAtIndex(i).stringValue = lines[i]; so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void Action(GameObject g, BaselineInteraction.ActionKind action, int index) { var a = g.AddComponent<BaselineInteraction>(); a.action = action; a.index = index; }
    private static GameObject Interactable(string name, Vector3 pos, string line, BaselineInteraction.ActionKind? action, int index)
    {
        var g = new GameObject(name); g.transform.SetParent(root); g.transform.position = pos;
        var trigger = g.AddComponent<SphereCollider>(); trigger.radius = 3; trigger.isTrigger = true;
        var body = g.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        Configure(g.AddComponent<NPCDialogue>(), name, new[] { line });
        if (action.HasValue) Action(g, action.Value, index); return g;
    }
    private static void Sign(string name, Vector3 pos, string line, BaselineInteraction.ActionKind? action, int index)
    {
        var g = Interactable(name, pos, line, action, index);
        Shape("SignPost_REPLACE", PrimitiveType.Cube, pos + Vector3.up * .6f, new Vector3(.16f, 1.2f, .16f), wood).transform.SetParent(g.transform, true);
        Shape("SignBoard_REPLACE", PrimitiveType.Cube, pos + Vector3.up * 1.2f, new Vector3(1.6f, .75f, .12f), wood).transform.SetParent(g.transform, true);
        Label("READ [E]", pos + Vector3.up * 2);
    }
    private static void Chest(string name, Vector3 pos, int index)
    {
        var g = Interactable(name, pos, index == 2 ? "An old seal holds this chest. Its symbols can be found nearby." : "An unmarked chest. Open it and inspect what is inside.", BaselineInteraction.ActionKind.Chest, index);
        Shape("ChestBody_REPLACE", PrimitiveType.Cube, pos + Vector3.up * .35f, new Vector3(1.1f, .7f, .7f), gold).transform.SetParent(g.transform, true);
        var hinge = new GameObject("LidHinge"); hinge.transform.SetParent(g.transform); hinge.transform.localPosition = new Vector3(0, .7f, .35f);
        Shape("ChestLid_REPLACE", PrimitiveType.Cube, pos + new Vector3(0, .77f, 0), new Vector3(1.15f, .14f, .75f), wood, false).transform.SetParent(hinge.transform, true);
        g.GetComponent<BaselineInteraction>().lid = hinge.transform;
    }
    private static void Arch(Vector3 p, string name, bool complete)
    {
        Shape(name + "_Left", PrimitiveType.Cube, p + new Vector3(-1.2f, 1.4f, 0), new Vector3(.6f, 2.8f, .6f), stone);
        Shape(name + "_Right", PrimitiveType.Cube, p + new Vector3(1.2f, 1.1f, 0), new Vector3(.6f, complete ? 2.8f : 2.2f, .6f), stone);
        Shape(name + "_Lintel", PrimitiveType.Cube, p + new Vector3(complete ? 0 : -.4f, 2.9f, 0), new Vector3(complete ? 3 : 2, .6f, .6f), stone);
    }
    private static void SimplifyNPC(GameObject g, Material material)
    {
        foreach (var r in g.GetComponentsInChildren<Renderer>()) r.enabled = false;
        var visual = Shape("NPC_Capsule_REPLACE", PrimitiveType.Capsule, g.transform.position + Vector3.up, new Vector3(.8f, 1, .8f), material, false); visual.transform.SetParent(g.transform, true);
    }
    private static void SideGuide(string name, Vector3 p, string label)
    {
        var g = GameObject.Find(name); g.transform.position = p; SimplifyNPC(g, cyan); Label(label, p + Vector3.up * 3.3f); Arch(p + new Vector3(0, 0, 2), name + "Portal", true);
        var so = new SerializedObject(g.GetComponent<NPCDialogue>()); so.FindProperty("forwardDotThreshold").floatValue = -.2f; so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void Label(string text, Vector3 pos)
    {
        var g = new GameObject("Label_" + text); g.transform.SetParent(root); g.transform.position = pos;
        var mesh = g.AddComponent<TextMesh>(); mesh.text = text; mesh.fontSize = 48; mesh.characterSize = .08f; mesh.anchor = TextAnchor.MiddleCenter; mesh.color = Color.white;
    }
    private static void Tower()
    {
        // Greybox landmark only: no invented Boss implementation in this task.
        Shape("Tower_REPLACE", PrimitiveType.Cylinder, new Vector3(0, 6, 39), new Vector3(7, 5, 7), stone);
        Shape("FutureEndingArena_REPLACE", PrimitiveType.Cylinder, new Vector3(0, 11, 39), new Vector3(12, .3f, 12), stone);
        for (int i = 0; i < 50; i++) Shape("TowerWalkway", PrimitiveType.Cube, new Vector3(7, 1 + .2f * i - .1f, 16 + .5f * i), new Vector3(3, .2f, .55f), stone);
        Shape("TowerRoofBridge", PrimitiveType.Cube, new Vector3(4.4f, 10.6f, 40), new Vector3(6, .4f, 3), stone);
        Shape("Guardian_Capsule_PLACEHOLDER", PrimitiveType.Capsule, new Vector3(0, 12.6f, 39), new Vector3(1.3f, 1.3f, 1.3f), cyan);
        Label("TOWER / ENDING AREA\nPlaceholder only", new Vector3(0, 15, 39));
    }
    private static void Routes(BaselineWorld world)
    {
        var nodes = new List<Vector3>(); var edges = new List<Vector2Int>();
        for (int i = 0; i < 16; i++) { float a = i * Mathf.PI * 2 / 16; nodes.Add(new Vector3(Mathf.Sin(a) * 31, 1.18f, -Mathf.Cos(a) * 31)); edges.Add(new Vector2Int(i, (i + 1) % 16)); }
        nodes.Add(new Vector3(0, 1.18f, -23)); edges.Add(new Vector2Int(0, 16));
        nodes.Add(new Vector3(0, 1.18f, -14)); edges.Add(new Vector2Int(16, 17));
        nodes.Add(new Vector3(0, 1.18f, -5)); edges.Add(new Vector2Int(17, 18));
        nodes.Add(new Vector3(0, 1.18f, 1)); edges.Add(new Vector2Int(18, 19));
        world.routeNodes = nodes.ToArray(); world.routeEdges = edges.ToArray();
    }
    [MenuItem("ArtLab/Baseline/Build WebGL")]
    public static void Build()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath, SideWorldTravel.BattlePath, SideWorldTravel.SideGameBPath }, locationPathName = "Builds/OverworldBaselineWebGL", target = BuildTarget.WebGL, options = BuildOptions.None });
        Directory.CreateDirectory("ToolReports/Baseline"); File.WriteAllText("ToolReports/Baseline/build.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Baseline build failed.");
    }
}
