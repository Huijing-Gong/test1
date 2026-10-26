using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Deliberate one-time migration of existing identities, not a scene generator.
// No menu: execute only after inspecting the saved baseline and preservation report.
public static class OverworldV2Migration
{
    const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    const string Study = "Assets/Art/MapV2Study/";
    const string Report = "ToolReports/MapV2Migration/";
    static Transform legacy;
    static GameObject environment;
    static MeshCollider terrain;
    static Material stone;
    static readonly HashSet<Transform> retained = new HashSet<Transform>();
    static readonly List<Collider> surfaces = new List<Collider>();

    static Transform Original(string name)
    {
        var found = legacy.Cast<Transform>().Where(t => t.name == name).ToArray();
        if (found.Length != 1) throw new InvalidOperationException("Expected one original: " + name);
        retained.Add(found[0]); return found[0];
    }
    static float Ground(float x, float z)
    {
        if (!terrain.Raycast(new Ray(new Vector3(x, 70, z), Vector3.down), out var hit, 120))
            throw new InvalidOperationException("No terrain at " + x + "," + z);
        return hit.point.y;
    }
    static Vector3 Point(float x, float z, float lift = .04f) => new Vector3(x, Ground(x, z) + lift, z);
    static void Move(string name, Vector3 position)
    {
        var t = Original(name); var delta = position - t.position;
        // Labels in the legacy scene are siblings, not children of the interaction.
        foreach (Transform label in legacy)
            if (label.name.StartsWith("Label_") && Vector2.Distance(new Vector2(label.position.x, label.position.z), new Vector2(t.position.x, t.position.z)) < .25f)
            { label.position += delta; retained.Add(label); }
        t.position = position;
    }
    static void Set(Object target, string key, Vector3 value)
    { var so = new SerializedObject(target); so.FindProperty(key).vector3Value = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    static void Set(Object target, string key, Object value)
    { var so = new SerializedObject(target); so.FindProperty(key).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    static void Refs(Object target, string key, IEnumerable<Object> values)
    {
        var array = values.ToArray(); var so = new SerializedObject(target); var p = so.FindProperty(key); p.arraySize = array.Length;
        for (int i = 0; i < array.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = array[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static GameObject Box(string name, Transform parent, Vector3 centre, Vector3 scale, bool visible = true)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(parent, true);
        g.transform.position = centre; g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = stone; g.GetComponent<Renderer>().enabled = visible;
        return g;
    }
    static void CopyMesh(string source, Transform target)
    {
        var mesh = target.GetComponent<MeshFilter>().sharedMesh;
        EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(Study + source), mesh);
        mesh.name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(mesh));
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
        var collider = target.GetComponent<MeshCollider>();
        if (collider) { collider.sharedMesh = null; collider.sharedMesh = mesh; }
    }
    [Serializable] sealed class NavigationProposal { public Vector3[] routeNodes; public Vector2Int[] routeEdges; }
    public static string ApplyNavigation()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
        var world=Object.FindFirstObjectByType<BaselineWorld>();var layout=world.GetComponent<OverworldV2Layout>();
        if(!layout)throw new InvalidOperationException("Migration missing");
        bool wasOpen=layout.BridgeOpen;layout.ApplyBridgeState(true);Physics.SyncTransforms();
        var data=JsonUtility.FromJson<NavigationProposal>(File.ReadAllText(Report+"navigation-proposal.json"));
        var lines=new List<string>();
        try
        {
            for(int i=0;i<data.routeNodes.Length;i++)
            {
                if(world.NavigationGround(data.routeNodes[i],out var p))data.routeNodes[i]=p;
                else lines.Add("NODE "+i+" failed "+data.routeNodes[i]);
            }
            world.routeNodes=data.routeNodes;world.routeEdges=data.routeEdges;world.RefreshNavigation();
            for(int i=0;i<data.routeEdges.Length;i++)
            {var e=data.routeEdges[i];if(!world.NavigationSegment(data.routeNodes[e.x],data.routeNodes[e.y]))lines.Add("EDGE "+i+" "+e+" failed "+data.routeNodes[e.x]+" -> "+data.routeNodes[e.y]);}
            File.WriteAllLines(Report+"navigation-first-check.txt",lines);
        }
        finally { layout.ApplyBridgeState(wasOpen); }
        foreach(var path in new[]{"Assets/_Demo/BaselineData/LakeTerrain.asset","Assets/_Demo/BaselineData/ExpandedLakeSurface.asset"})
        {var m=AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(path);m.name=Path.GetFileNameWithoutExtension(path);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);}
        EditorUtility.SetDirty(world);EditorSceneManager.MarkSceneDirty(world.gameObject.scene);EditorSceneManager.SaveScene(world.gameObject.scene);
        return "Authored "+data.routeNodes.Length+" nodes / "+data.routeEdges.Length+" edges; failed entries="+lines.Count+". Inspect navigation-first-check.txt.";
    }
    static void MovePortal(string name, Vector3 destination)
    {
        var guide = GameObject.Find(name).transform; var delta = destination - guide.position;
        foreach (Transform t in legacy)
            if (t.name.StartsWith(name + "Portal_") || t.name == "Label_SIDE WORLD " + (name.Contains("A_") ? "A" : "B"))
            { t.position += delta; retained.Add(t); }
        guide.position = destination;
    }
    static string[] Identities()
    {
        var scene = SceneManager.GetActiveScene(); var list = new List<string>();
        foreach (var root in scene.GetRootGameObjects()) foreach (var c in root.GetComponentsInChildren<Component>(true))
            if (c is BaselineInteraction || c is NPCDialogue || c is BaselineWorld || c is BaselineFlow || c is PlayerMovement || c is SideWorldTravel || c is BaselineEndingRoom)
                list.Add(GlobalObjectId.GetGlobalObjectIdSlow(c) + " | " + c.GetType().Name + " | " + c.name +
                    (c is BaselineInteraction item ? " | " + item.action + " | " + item.index : ""));
        list.Sort(StringComparer.Ordinal); return list.ToArray();
    }
    static GameObject Bridge(Transform parent)
    {
        var g = new GameObject("North bridge - tutorial progress controlled"); g.transform.SetParent(parent, false);
        Vector3 a = Point(-39, 32, .1f), b = Point(-44, 64, .1f), d = b-a;
        var deck = Box("NorthBridge_WalkableDeck", g.transform, (a+b)*.5f-Vector3.up*.18f, new Vector3(3.6f,.36f,d.magnitude+.35f));
        deck.transform.rotation = Quaternion.LookRotation(d, Vector3.up); surfaces.Add(deck.GetComponent<Collider>());
        for (int side = -1; side <= 1; side += 2)
        {
            var rail = Box("NorthBridge_LowParapet", g.transform, deck.transform.position + deck.transform.right * (side*1.7f) + Vector3.up*.63f, new Vector3(.2f,.9f,d.magnitude-.5f));
            rail.transform.rotation = deck.transform.rotation;
        }
        for (int i = 1; i < 5; i++)
        {
            var p = Vector3.Lerp(a,b,i/5f); float bottom = Ground(p.x,p.z);
            Box("NorthBridge_Pier",g.transform,new Vector3(p.x,(bottom+p.y)*.5f-.1f,p.z),new Vector3(2.3f,p.y-bottom,1));
        }
        return g;
    }
    static void Boundary(Transform parent)
    {
        // Match the authored rounded terrain boundary. Closed physical shell, no visual walls.
        var group = new GameObject("V2 continuous physical perimeter"); group.transform.SetParent(parent, false);
        var pts = new Vector3[96];
        for (int i=0; i<pts.Length; i++)
        {
            float angle=i*Mathf.PI*2/pts.Length; var axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)); float lo=60,hi=115;
            for(int n=0;n<14;n++){float rad=(lo+hi)*.5f,x=axis.x*rad,z=axis.y*rad;float outer=Mathf.Pow(Mathf.Pow(Mathf.Abs(x)/82,4)+Mathf.Pow(Mathf.Abs(z)/82,4),.25f)+(Mathf.Sin(x*.13f+z*.085f)+Mathf.Sin(z*.23f))*.012f;if(outer>.985f)hi=rad;else lo=rad;}
            pts[i]=new Vector3(axis.x*lo,25,axis.y*lo);
        }
        for(int i=0;i<pts.Length;i++)
        {
            Vector3 a=pts[i],b=pts[(i+1)%pts.Length]; var wall=Box("V2_Boundary_"+i,group.transform,(a+b)*.5f,new Vector3(.6f,90,(b-a).magnitude+.5f),false);
            wall.transform.rotation=Quaternion.LookRotation(b-a); wall.AddComponent<NonClimbableSurface>();
        }
    }
    public static string Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != ScenePath || scene.isDirty) throw new InvalidOperationException("Requires saved original OverworldBaseline in Edit mode.");
        var world=Object.FindFirstObjectByType<BaselineWorld>();
        if (!world || world.GetComponent<OverworldV2Layout>() || GameObject.Find("Overworld V2 environment")) throw new InvalidOperationException("Not the unmodified migration baseline; do not apply twice.");
        if (!File.Exists(Report+"Before/manifest.json")) throw new InvalidOperationException("Missing preservation snapshot.");
        retained.Clear(); surfaces.Clear(); legacy=GameObject.Find("_A2_Greybox").transform;
        // Resolve every identity before any mutation.
        foreach(var n in new[]{"LakeTerrain_REPLACE_OR_SCULPT","Lake_Surface_REPLACE","Lake_SwimmingVolume","Island notice","Eastern notice","Survey cylinder","Island inscription","Eastern relay","Island hidden chest","Western hidden chest","Sealed eastern chest","Weathered inscription","WAVE rune","LEAF rune","SUN rune","Guardian_Capsule_PLACEHOLDER"}) Original(n);
        var before=Identities(); File.WriteAllLines(Report+"identities-before.txt",before);
        File.WriteAllText(Report+"scene-before.sha256",BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(ScenePath))).Replace("-", ""));
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Study+"OverworldV2_VisualStudy.prefab");
        if(!source || source.GetComponentsInChildren<MonoBehaviour>(true).Length!=0)throw new InvalidOperationException("Environment sample must be script-free.");
        Undo.RegisterFullObjectHierarchyUndo(legacy.gameObject,"Migrate Overworld V2");
        stone=AssetDatabase.LoadAssetAtPath<Material>(Study+"StudyStone.mat");
        var ground=Original("LakeTerrain_REPLACE_OR_SCULPT"); var water=Original("Lake_Surface_REPLACE");
        CopyMesh("StudyGround.asset",ground); CopyMesh("StudyWater.asset",water);
        ground.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Study+"StudyGround.mat");
        water.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Study+"StudyWater.mat");
        terrain=ground.GetComponent<MeshCollider>(); surfaces.Add(terrain); Physics.SyncTransforms();
        environment=(GameObject)PrefabUtility.InstantiatePrefab(source,scene); environment.name="Overworld V2 environment"; Undo.RegisterCreatedObjectUndo(environment,"V2 environment");
        foreach(var t in environment.GetComponentsInChildren<Transform>(true))
        {
            if(t.name=="VISUAL_STUDY_Terrain_NoGameplayMigration" || t.name=="Lake_Visual_NoWaterVolume" || t.name=="ScaleReference_StaticModel_NoDialogue")t.gameObject.SetActive(false);
            if(t.name.Contains("StoneTerrace") || t.name=="C2_Hub_Stone_Dais" || t.name=="E6_ExternalSteps_VISUAL_ROUTE_NOT_PLAYTESTED" || t.name=="E6_TopArrivalTerrace")
                foreach(var c in t.GetComponents<Collider>())surfaces.Add(c);
        }
        var player=world.player; var spawn=GameObject.Find("PlayerSpawnPoint").transform; spawn.position=Point(0,-75); player.position=spawn.position; player.rotation=Quaternion.identity;
        var guide=GameObject.Find("IntroNPC").transform; Vector3 oldGuide=guide.position;guide.position=new Vector3(-2,3.28f,-41); guide.rotation=Quaternion.Euler(0,180,0);
        var guideLabel=Original("Label_GUIDE");guideLabel.position+=guide.position-oldGuide;
        MovePortal("SideWorldA_Guide",Point(-63,-61)); MovePortal("SideWorldB_Guide",Point(64,-62));
        Move("Island notice",Point(-30,-12)); Move("Eastern notice",Point(20,-44));
        Move("Survey cylinder",Point(-61,-23)); Move("Island inscription",new Vector3(-41,2.62f,23)); Move("Eastern relay",Point(47,11));
        Move("Island hidden chest",Point(-43,29)); Move("Western hidden chest",Point(-66,-20));
        Move("Weathered inscription",Point(50,4)); Move("WAVE rune",Point(43,8)); Move("LEAF rune",Point(45.5f,8)); Move("SUN rune",Point(48,8));
        Move("Sealed eastern chest",Point(45,12));
        foreach(var n in new[]{"IslandBrokenArch_Left","IslandBrokenArch_Right","IslandBrokenArch_Lintel","HologramMissingQuarter_REPLACE"})
        { var t=Original(n);t.position+=new Vector3(-40.3f,1.5f,23); }
        // The original endingEntrance Transform remains the same serialized reference.
        var door=Original("Guardian_Capsule_PLACEHOLDER");door.position=new Vector3(55.925f,38.05f,61);
        foreach(var renderer in door.GetComponentsInChildren<Renderer>())renderer.enabled=false;
        foreach(var collider in door.GetComponentsInChildren<Collider>())collider.enabled=false;
        var volume=Original("Lake_SwimmingVolume"); volume.position=new Vector3(-43,-3,21); volume.localScale=Vector3.one;
        var box=volume.GetComponent<BoxCollider>();box.center=Vector3.zero;box.size=new Vector3(70,6.44f,96);box.isTrigger=true;
        var waterSo=new SerializedObject(volume.GetComponent<WaterVolume>());waterSo.FindProperty("waterSurfaceY").floatValue=.22f;waterSo.ApplyModifiedPropertiesWithoutUndo();
        // Keep the team's boundary shader at the actual arrival, separate from old pocket props.
        var pocket=Original("Opening arrival pocket");var barrier=pocket.Find("Opening visible southern barrier");
        barrier.SetParent(legacy,true);barrier.position=new Vector3(0,5.5f,-80);retained.Add(barrier);retained.Remove(pocket);
        foreach(Transform t in legacy)if(!retained.Contains(t))t.gameObject.SetActive(false);
        var bridge=Bridge(environment.transform);Boundary(environment.transform);
        var layout=Undo.AddComponent<OverworldV2Layout>(world.gameObject);
        Set(layout,"arrivalPosition",spawn.position); Set(layout,"campPosition",new Vector3(0,3.29f,-43));
        Set(layout,"introShorePosition",Point(-35,-8));Set(layout,"islandRestPosition",Point(-37,11));Set(layout,"islandDestination",world.objectives[1].position);
        Set(layout,"westRegionPosition",world.objectives[0].position);Set(layout,"eastRegionPosition",world.objectives[2].position);Set(layout,"northBridge",bridge);
        Refs(layout,"navigationSurfaces",surfaces.Cast<Object>());
        var map=new List<MeshFilter>{ground.GetComponent<MeshFilter>(),water.GetComponent<MeshFilter>()};
        map.AddRange(surfaces.Where(c=>c!=terrain).Select(c=>c.GetComponent<MeshFilter>()).Where(m=>m));
        Refs(layout,"mapSurfaces",map.Cast<Object>());layout.ApplyBridgeState(false);
        RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(Study+"StudySky.mat");RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.0025f;RenderSettings.fogColor=new Color(.68f,.82f,.9f);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.65f,.75f,.86f);RenderSettings.ambientEquatorColor=new Color(.45f,.53f,.49f);RenderSettings.ambientGroundColor=new Color(.25f,.29f,.26f);
        Camera.main.transform.SetPositionAndRotation(spawn.position+new Vector3(0,4,-7),Quaternion.Euler(14,0,0));
        Physics.SyncTransforms();world.RefreshNavigation();
        int missing=scene.GetRootGameObjects().Sum(root=>root.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
        if(missing!=0 || world.objectives.Any(t=>!t))throw new InvalidOperationException("Missing reference after migration. Scene left dirty for diagnosis.");
        var after=Identities();File.WriteAllLines(Report+"identities-after.txt",after);
        if(!before.SequenceEqual(after))throw new InvalidOperationException("Identity mismatch. Inspect reports; scene left dirty.");
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return "Migrated same scene; retained "+before.Length+" identity entries; navigation surfaces="+surfaces.Count+"; missing="+missing+". Bridge saved closed; routes require apply/verification next.";
    }
}
