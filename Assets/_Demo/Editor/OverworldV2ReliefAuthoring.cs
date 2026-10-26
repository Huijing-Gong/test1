using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Local height revision of the migrated scene, never an initial scene generator.
// Existing object identities and terrain GUID are retained; authored proof is in Docs/MapV2/Relief.
public static class OverworldV2ReliefAuthoring
{
    const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    const string Report = "ToolReports/MapV2Relief/";
    const string AssetsPath = "Assets/Art/MapV2Study/Relief/";
    const string Marker = "V2 relief and distant landscape";
    static MeshCollider terrain;
    static float S(float a,float b,float v) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));
    static float Disc(Vector2 p,Vector2 c,float inner,float outer) => 1-S(inner,outer,Vector2.Distance(p,c));
    static float RectDistance(float x,float z,float xmin,float xmax,float zmin,float zmax)
        => new Vector2(Mathf.Max(xmin-x,0,x-xmax),Mathf.Max(zmin-z,0,z-zmax)).magnitude;
    // Region benches, not a uniform scale. Lake basin and the southern island are unchanged.
    public static float Offset(float x,float z)
    {
        var p=new Vector2(x,z);
        float hub=8*Disc(p,new Vector2(0,-43),11,37)*S(-74,-54,z);
        float central=6*S(-43,-20,z)*(1-S(17,41,Mathf.Abs(x-7)))*(1-S(40,73,z));
        float east=8*S(18,40,x)*S(-62,-15,z);
        float west=4*Disc(p,new Vector2(-61,-24),11,30);
        float delta=Mathf.Max(hub,central,east,west);
        // Preserve the shoreline, both bridge landings and the recent island traversal lesson.
        float basin=(1-S(-15,1,x))*S(-22,-12,z);
        delta*=1-basin;
        // Keep the full southern jump/drop fixture and its ground together at +3 m.
        float practice=1-S(0,7,RectDistance(x,z,-33,-13,-30,-15));
        delta=Mathf.Lerp(delta,3,practice);
        // No height adjustment reaches the visible lake, swimming corridor or approach sign.
        delta*=1-(1-S(-16,0,x))*S(-15,-10,z);
        // Existing rigid ruin floors retain broad level foundations.
        delta=Mathf.Lerp(delta,6,Disc(p,new Vector2(-5,5),6,9));
        delta=Mathf.Lerp(delta,4,Disc(p,new Vector2(-61,-24),7,12));
        return delta;
    }
    static void RequireClean()
    {
        var s=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||s.isDirty||s.path!=ScenePath||SceneManager.sceneCount!=1)
            throw new InvalidOperationException("Requires one clean main scene in stable Edit Mode.");
        terrain=GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<MeshCollider>();
    }
    static float Ground(float x,float z)
    {
        if(!terrain.Raycast(new Ray(new Vector3(x,90,z),Vector3.down),out var hit,150))throw new Exception("No terrain: "+x+","+z);
        return hit.point.y;
    }
    static string[] Identities()
    {
        return SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true))
            .Where(c=>c is BaselineInteraction||c is NPCDialogue||c is BaselineWorld||c is BaselineFlow||c is PlayerMovement||c is SideWorldTravel||c is BaselineEndingRoom)
            .Select(c=>GlobalObjectId.GetGlobalObjectIdSlow(c)+" | "+c.GetType().Name+" | "+c.name+(c is BaselineInteraction i?" | "+i.action+" | "+i.index:""))
            .OrderBy(v=>v,StringComparer.Ordinal).ToArray();
    }
    static void Lift(Transform t,float amount)
    {
        if(Mathf.Abs(amount)<.00001f)return;
        Undo.RecordObject(t,"Reposition with regional relief");t.position+=Vector3.up*amount;
        if(PrefabUtility.IsPartOfPrefabInstance(t))PrefabUtility.RecordPrefabInstancePropertyModifications(t);
    }
    static void SaveMesh(Mesh mesh)
    {mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);}
    [Serializable] sealed class OriginalRoutes { public Vector3[] nodes; public Vector2Int[] edges; }
    static Vector3[] OriginalVertices()
    {
        // Explicit snapshot of this known 171x171, 48-byte stride Unity mesh, not a generic serializer.
        string text=File.ReadAllText(Report+"Before/Assets/_Demo/BaselineData/LakeTerrain.asset");
        string hex=System.Text.RegularExpressions.Regex.Match(text,@"_typelessdata: ([0-9a-fA-F]+)").Groups[1].Value;
        if(hex.Length!=29241*48*2)throw new Exception("Unexpected snapshot vertex layout.");
        var bytes=new byte[hex.Length/2];for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(hex.Substring(i*2,2),16);
        var vertices=new Vector3[29241];for(int i=0;i<vertices.Length;i++)vertices[i]=new Vector3(BitConverter.ToSingle(bytes,i*48),BitConverter.ToSingle(bytes,i*48+4),BitConverter.ToSingle(bytes,i*48+8));
        return vertices;
    }
    public static string GradeAndFinish()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath||SceneManager.sceneCount!=1)throw new Exception("Main scene in Edit Mode required.");
        if(GameObject.Find(Marker))throw new InvalidOperationException("Historical repair step is closed after relief completion; it does not reproduce the final local tutorial approach. Edit the current scene locally.");
        terrain=GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<MeshCollider>();
        var world=Object.FindFirstObjectByType<BaselineWorld>();var layout=world.GetComponent<OverworldV2Layout>();
        var original=OriginalVertices();var routes=JsonUtility.FromJson<OriginalRoutes>(File.ReadAllText(Report+"original-navigation.json"));
        if(routes.nodes.Length!=81||routes.edges.Length!=83)throw new Exception("Unexpected route baseline.");
        var targets=(Vector3[])routes.nodes.Clone();for(int i=0;i<targets.Length;i++){var p=targets[i];p.y+=Offset(p.x,p.z)-.15f;targets[i]=p;}
        var corrections=new Dictionary<Transform,float>();
        Action<Transform> remember=t=>{if(t.gameObject.activeSelf&&Mathf.Abs(t.position.x)<82&&Mathf.Abs(t.position.z)<82)corrections[t]=Ground(t.position.x,t.position.z);};
        foreach(Transform t in GameObject.Find("Overworld V2 environment").transform)
            if(!t.name.StartsWith("E6_")&&!t.name.StartsWith("Continuous")&&!t.name.StartsWith("V2 ")&&t.name!="North bridge - tutorial progress controlled")remember(t);
        foreach(Transform t in GameObject.Find("_A2_Greybox").transform)
            if(t!=terrain.transform&&!t.name.StartsWith("Lake_")&&!t.name.StartsWith("Guardian")&&!t.name.StartsWith("Opening visible"))remember(t);
        remember(GameObject.Find("IntroNPC").transform);
        var vertices=(Vector3[])original.Clone();int adjusted=0;
        for(int i=0;i<vertices.Length;i++)
        {
            var v=vertices[i];v.y+=Offset(v.x,v.z);
            // The water/bridge/upper tower and complete physical teaching structure are protected.
            if(!(v.x< -15&&v.z>0)&&!(v.x>47&&v.z>44)&&original[i].y>.3f)
            {
                float weightSum=0,heightSum=0,nearest=1000;var p=new Vector2(v.x,v.z);
                foreach(var edge in routes.edges)
                {
                    var a=targets[edge.x];var b=targets[edge.y];
                    if(a.y-original[Mathf.Clamp(Mathf.RoundToInt(a.z)+85,0,170)*171+Mathf.Clamp(Mathf.RoundToInt(a.x)+85,0,170)].y-Offset(a.x,a.z)>1)continue;
                    var aa=new Vector2(a.x,a.z);var bb=new Vector2(b.x,b.z);var d=bb-aa;
                    if(d.sqrMagnitude<.001f)continue;float along=Mathf.Clamp01(Vector2.Dot(p-aa,d)/d.sqrMagnitude);float dist=Vector2.Distance(p,aa+d*along);
                    if(dist>6)continue;nearest=Mathf.Min(nearest,dist);float weight=Mathf.Exp(-dist*dist/3);
                    float height=Mathf.Lerp(a.y,b.y,along);
                    // Meet the broad hub bench before its circular paving, rather than making a lip at the rim.
                    if(edge.x==1&&edge.y==2)height=Mathf.Lerp(a.y,11,Mathf.Clamp01(along/.43f));
                    weightSum+=weight;heightSum+=height*weight;
                }
                if(weightSum>.00001f&&nearest<5)
                {
                    float blend=1-S(2.5f,5,nearest);
                    // Original rigid courtyards keep their level foundations.
                    float preserve=Mathf.Max(Disc(p,new Vector2(0,-43),9,11),Disc(p,new Vector2(-5,5),5.5f,7),Disc(p,new Vector2(-61,-24),5.5f,7),Disc(p,new Vector2(45,8),5.2f,7));
                    preserve=Mathf.Max(preserve,Disc(p,new Vector2(-18.2f,-26),1.4f,3),Disc(p,new Vector2(-30,-16),1.4f,3),Disc(p,new Vector2(-16,-28),1.4f,3));
                    v.y=Mathf.Lerp(v.y,heightSum/weightSum,blend*(1-preserve));adjusted++;
                }
            }
            vertices[i]=v;
        }
        var mesh=terrain.sharedMesh;Undo.RecordObject(mesh,"Grade original walking corridors");mesh.vertices=vertices;SaveMesh(mesh);terrain.sharedMesh=null;terrain.sharedMesh=mesh;Physics.SyncTransforms();
        foreach(var pair in corrections)Lift(pair.Key,Ground(pair.Key.position.x,pair.Key.position.z)-pair.Value);
        Undo.RecordObject(layout,"Keep anchor ground offsets after road grading");var so=new SerializedObject(layout);
        foreach(var name in new[]{"introShorePosition","westRegionPosition","eastRegionPosition"})
        {var prop=so.FindProperty(name);var p=prop.vector3Value; p.y=Ground(p.x,p.z)+.04f;prop.vector3Value=p;}
        so.ApplyModifiedProperties();Physics.SyncTransforms();world.RefreshNavigation();
        var lines=new List<string>();Undo.RecordObject(world,"Complete route heights");
        world.routeNodes=targets;world.routeEdges=routes.edges;
        for(int i=0;i<targets.Length;i++)if(world.NavigationGround(targets[i],out var g))world.routeNodes[i]=g;else lines.Add("NODE "+i+" "+targets[i]);
        world.RefreshNavigation();
        for(int i=0;i<routes.edges.Length;i++){var e=routes.edges[i];if(!world.NavigationSegment(world.routeNodes[e.x],world.routeNodes[e.y]))lines.Add("EDGE "+i+" "+e+" "+world.routeNodes[e.x]+" -> "+world.routeNodes[e.y]);}
        // The interrupted first pass had temporarily enabled the bridge; baseline authoring state is closed.
        layout.ApplyBridgeState(false);
        if(!GameObject.Find(Marker)){var marker=new GameObject(Marker);Undo.RegisterCreatedObjectUndo(marker,"Relief stage marker");}
        var before=File.ReadAllLines(Report+"identities-before.txt");var after=Identities();File.WriteAllLines(Report+"identities-after.txt",after);
        if(!before.SequenceEqual(after))throw new Exception("Critical identity changed.");
        File.WriteAllLines(Report+"graded-navigation.txt",lines);
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        return "Graded "+adjusted+" vertices; navigation failures="+lines.Count+"; identities="+after.Length+". Original bridge closed.";
    }
    public static string Apply()
    {
        RequireClean();
        if(GameObject.Find(Marker)||!File.Exists(Report+"Before/Assets/_Demo/Scenes/OverworldBaseline.unity"))
            throw new InvalidOperationException("Already applied or missing pre-change snapshot; refine locally.");
        var scene=SceneManager.GetActiveScene();var layout=Object.FindFirstObjectByType<OverworldV2Layout>();var world=layout.GetComponent<BaselineWorld>();
        var before=Identities();File.WriteAllLines(Report+"identities-before.txt",before);
        var mesh=terrain.sharedMesh;string meshPath=AssetDatabase.GetAssetPath(mesh);
        var consumers=AssetDatabase.FindAssets("t:Scene",new[]{"Assets"}).Concat(AssetDatabase.FindAssets("t:Prefab",new[]{"Assets"}))
            .Select(AssetDatabase.GUIDToAssetPath).Where(p=>AssetDatabase.GetDependencies(p,true).Contains(meshPath)).Distinct().ToArray();
        File.WriteAllLines(Report+"terrain-consumers.txt",consumers);
        if(consumers.Any(p=>p!=ScenePath&&p!=OverworldV2PlayChecks.ValidationScene))throw new Exception("Terrain shared outside main/QA mirror; inspect consumers before altering it.");
        var oldVertices=mesh.vertices;
        Directory.CreateDirectory(AssetsPath);AssetDatabase.Refresh();
        Undo.RegisterCompleteObjectUndo(mesh,"Reshape V2 region heights");
        var vertices=(Vector3[])oldVertices.Clone();
        for(int i=0;i<vertices.Length;i++)vertices[i].y+=Offset(vertices[i].x,vertices[i].z);
        mesh.vertices=vertices;SaveMesh(mesh);terrain.sharedMesh=null;terrain.sharedMesh=mesh;
        var env=GameObject.Find("Overworld V2 environment").transform;
        foreach(Transform t in env)
        {
            if(!t.gameObject.activeSelf||t.name=="V2 continuous physical perimeter")continue;
            if(t.name=="ContinuousFloatingRockShell_VisualOnly_NotBoundary")
            {
                // Clone only this scene's decorative mesh; the original visual-study prefab stays intact.
                var mf=t.GetComponent<MeshFilter>();var shell=Object.Instantiate(mf.sharedMesh);shell.name="ReliefRockShell";
                var sv=shell.vertices;for(int i=0;i<sv.Length;i++){var p=t.TransformPoint(sv[i]);p.y+=Offset(p.x,p.z)*S(-45,0,p.y);sv[i]=t.InverseTransformPoint(p);}
                shell.vertices=sv;AssetDatabase.CreateAsset(shell,AssetsPath+"ReliefRockShell.asset");SaveMesh(shell);
                Undo.RecordObject(mf,"Use locally reshaped rock shell");mf.sharedMesh=shell;PrefabUtility.RecordPrefabInstancePropertyModifications(mf);continue;
            }
            float amount=t.name.StartsWith("C2_")?8:t.name.StartsWith("C4_")?6:t.name.StartsWith("A2_")?4:
                t.name.StartsWith("E3_")||t.name.StartsWith("E5_")||t.name.StartsWith("E6_")?8:Offset(t.position.x,t.position.z);
            Lift(t,amount);
        }
        var legacy=GameObject.Find("_A2_Greybox").transform;
        foreach(Transform t in legacy)
        {
            if(!t.gameObject.activeSelf||t==terrain.transform||t.name=="Lake_Surface_REPLACE"||t.name=="Lake_SwimmingVolume")continue;
            if(t.name=="Opening visible southern barrier")
            {var renderer=t.GetComponent<Renderer>();Undo.RecordObject(renderer,"Remove visible arrival barrier");renderer.enabled=false;continue;}
            Lift(t,t.name=="Guardian_Capsule_PLACEHOLDER"?8:Offset(t.position.x,t.position.z));
        }
        var guide=GameObject.Find("IntroNPC").transform;Lift(guide,8);
        foreach(var name in new[]{"SideWorldA_Guide","SideWorldB_Guide"}){var t=GameObject.Find(name).transform;Lift(t,Offset(t.position.x,t.position.z));}
        var facilities=GameObject.Find("V2 return and supply facilities").transform;
        foreach(Transform t in facilities)Lift(t,Offset(t.position.x,t.position.z));
        var practiceRoot=GameObject.Find("V2 optional traversal practice").transform;
        foreach(Transform t in practiceRoot)
            if(t.name=="Walkable stone approach ramp"||t.name=="Broad step-off ledge"||t.name=="Stone ledge support"||t.name=="Low jump stone - walk around to bypass")Lift(t,3);
        Undo.RecordObject(layout,"Update relief anchors");var so=new SerializedObject(layout);
        foreach(var name in new[]{"arrivalPosition","campPosition","introShorePosition","islandRestPosition","islandDestination","westRegionPosition","eastRegionPosition","jumpPracticePosition","dropPlatformPosition","dropLandingPosition","climbBasePosition","climbTopPosition"})
        {var p=so.FindProperty(name);var v=p.vector3Value;v.y+=Offset(v.x,v.z);p.vector3Value=v;}
        so.ApplyModifiedProperties();
        Physics.SyncTransforms();world.RefreshNavigation();
        // Keep all node/edge identities, recompute heights from the actual walkable collision surfaces.
        bool wasOpen=layout.BridgeOpen;layout.ApplyBridgeState(true);Physics.SyncTransforms();Undo.RecordObject(world,"Reground existing route nodes");
        for(int i=0;i<world.routeNodes.Length;i++)
        {var old=world.routeNodes[i];old.y+=Offset(old.x,old.z);if(!world.NavigationGround(old,out var floor))throw new Exception("Navigation node no longer stands: "+i+" "+old);world.routeNodes[i]=floor;}
        world.RefreshNavigation();var failures=new List<string>();
        for(int i=0;i<world.routeEdges.Length;i++){var e=world.routeEdges[i];if(!world.NavigationSegment(world.routeNodes[e.x],world.routeNodes[e.y]))failures.Add("Edge "+i+" "+e+" "+world.routeNodes[e.x]+" -> "+world.routeNodes[e.y]);}
        layout.ApplyBridgeState(wasOpen);File.WriteAllLines(Report+"initial-navigation.txt",failures);
        var marker=new GameObject(Marker);Undo.RegisterCreatedObjectUndo(marker,"Relief stage marker");
        var after=Identities();File.WriteAllLines(Report+"identities-after.txt",after);
        if(!before.SequenceEqual(after))throw new Exception("Critical identity mismatch; inspect before saving.");
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        return "Relief applied locally, identities="+after.Length+". Full navigation failures="+failures.Count+". No background yet. Lake and bridge original heights retained.";
    }
    public static string Capture(string phase)
    {
        RequireClean();var cam=Camera.main;var world=Object.FindFirstObjectByType<BaselineWorld>();var layout=world.GetComponent<OverworldV2Layout>();
        string dir="Docs/MapV2/Relief/";Directory.CreateDirectory(dir);
        var cp=cam.transform.position;var cr=cam.transform.rotation;var pp=world.player.position;var pr=world.player.rotation;
        var oldTarget=cam.targetTexture;float oldFov=cam.fieldOfView,oldFar=cam.farClipPlane;var active=RenderTexture.active;
        var tex=new Texture2D(1440,900,TextureFormat.RGB24,false);var rt=RenderTexture.GetTemporary(1440,900,24,RenderTextureFormat.ARGB32);
        var log=new List<string>();
        try
        {
            cam.targetTexture=rt;cam.fieldOfView=60;cam.farClipPlane=900;
            var points=new[]{layout.ArrivalPosition,new Vector3(-2,0,-61),layout.CampPosition,new Vector3(10,0,-3),new Vector3(34,0,-7)};
            var names=new[]{"spawn","uphill","hub-lake","central","east"};float[] yaw={0,0,-40,70,30};
            for(int i=0;i<points.Length;i++)
            {
                var feet=points[i];if(world.NavigationGround(feet,out var floor))feet.y=floor.y-.1f;else feet.y=Ground(feet.x,feet.z)+.04f;
                var rot=Quaternion.Euler(14,yaw[i],0);world.player.SetPositionAndRotation(feet,Quaternion.Euler(0,yaw[i],0));
                var pos=feet+Vector3.up*1.05f+rot*new Vector3(0,0,-6);cam.transform.SetPositionAndRotation(pos,rot);
                cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1440,900),0,0);tex.Apply();
                File.WriteAllBytes(dir+phase+"-"+names[i]+".png",tex.EncodeToPNG());log.Add(names[i]+" feet="+feet+" camera="+pos+" yaw="+yaw[i]+" pitch=14 FOV=60; scene camera offscreen render, controlled placement, no physics/input.");
            }
            cam.fieldOfView=58;cam.transform.SetPositionAndRotation(new Vector3(122,153,-170),Quaternion.LookRotation(new Vector3(-122,-148,170)));
            world.player.SetPositionAndRotation(pp,pr);cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1440,900),0,0);tex.Apply();File.WriteAllBytes(dir+phase+"-overview.png",tex.EncodeToPNG());
            log.Add("overview: fixed camera (122,153,-170) target (0,5,0), FOV58; actual main-scene camera offscreen render.");
        }
        finally
        {
            world.player.SetPositionAndRotation(pp,pr);cam.transform.SetPositionAndRotation(cp,cr);cam.targetTexture=oldTarget;cam.fieldOfView=oldFov;cam.farClipPlane=oldFar;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(tex);Physics.SyncTransforms();
        }
        File.WriteAllLines(Report+phase+"-capture.txt",log);
        if(SceneManager.GetActiveScene().isDirty)EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return "Captured five matched eye-level poses and fixed overview (offscreen, not Game View).";
    }
}
