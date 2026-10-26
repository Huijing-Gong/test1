using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// One-off migration from the initial greybox, not a final-scene generator.
// Final route/prop corrections are saved in OverworldBaseline.unity (see Docs/OverworldExpansion.md).
// Does not rebuild the scene; Apply refuses an already expanded scene.
public static class OverworldExpansion
{
    const string Report = "ToolReports/MapExpansion/";
    static Transform Root => GameObject.Find("_A2_Greybox").transform;
    static MeshCollider Land => GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<MeshCollider>();
    static Vector3 P(float x,float z,float y=1) => new Vector3(x,y,z);
    public static readonly Vector3[] West = { P(0,-67),P(-10,-58),P(-25,-47),P(-38,-40),P(-52,-30),P(-57,-16),P(-51,-7),P(-52,9),P(-45,27),P(-30,39),P(-12,46),P(0,52),P(0,64) };
    public static readonly Vector3[] East = { P(0,-67),P(12,-55),P(31,-45),P(46,-32),P(58,-20),P(62,-7,2),P(58,4,3),P(49,12,3),P(48,26,3),P(39,36,2),P(20,45),P(0,52) };
    public static readonly Vector3[] Bridge = {P(0,-67),P(0,-48),P(0,-34),P(1,-25),P(2,-16),P(3,-7),P(3,0),P(3,3)};
    static float Smooth(float a,float b,float x) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,x));
    static float Hill(float x,float z,float cx,float cz,float rx,float rz,float h) => h*Mathf.Exp(-((x-cx)*(x-cx)/(rx*rx)+(z-cz)*(z-cz)/(rz*rz)));
    static float LakeRadius(float a) => 27+3*Mathf.Sin(3*a+.4f)+1.8f*Mathf.Cos(5*a);
    public static float Height(float x,float z)
    {
        float a=Mathf.Atan2(z,x), d=Mathf.Sqrt(x*x+z*z)-LakeRadius(a);
        float h=Mathf.Lerp(-3,1,Smooth(-5,3,d));
        if(d>3)
        {
            h+=Hill(x,z,-37,-20,10,17,5)+Hill(x,z,-65,8,9,16,4)+Hill(x,z,36,-15,8,15,5)+Hill(x,z,53,18,21,23,2)+Hill(x,z,18,-48,10,10,3);
            h+=5*Smooth(70,82,Mathf.Max(Mathf.Abs(x),Mathf.Abs(z)));
        }
        float island=Vector2.Distance(new Vector2(x,z),new Vector2(3,3));
        h=Mathf.Max(h,Mathf.Lerp(1,-3,Smooth(9,14,island)));
        foreach(var path in new[]{West,East}) for(int i=1;i<path.Length;i++)
        {float t;float dist=DistanceToSegment(x,z,path[i-1],path[i],out t);if(dist<6)h=Mathf.Lerp(Mathf.Lerp(path[i-1].y,path[i].y,t),h,Smooth(3,6,dist));}
        if(d>3) {float dist=Vector2.Distance(new Vector2(x,z),new Vector2(48,16));h=Mathf.Lerp(3,h,Smooth(12,19,dist));}
        return h;
    }
    static float DistanceToSegment(float x,float z,Vector3 a,Vector3 b,out float t)
    {var v=new Vector2(b.x-a.x,b.z-a.z);var p=new Vector2(x-a.x,z-a.z);t=Mathf.Clamp01(Vector2.Dot(p,v)/v.sqrMagnitude);return (p-v*t).magnitude;}
    static void Guard()
    {var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=OverworldBaselineSetup.ScenePath||scene.isDirty)throw new Exception("Open clean baseline in edit mode first");}
    public static void Measure(string phase)
    {
        Physics.SyncTransforms(); var bounds=Land.bounds; var player=GameObject.Find("_Gameplay/Player");
        var water=UnityEngine.Object.FindFirstObjectByType<WaterVolume>();
        int n=Mathf.RoundToInt(bounds.size.x/2),m=Mathf.RoundToInt(bounds.size.z/2);var kind=new int[n*m];var heights=new float[n*m];
        var rows=new List<string>{"x,z,height,kind,connected"};
        for(int z=0;z<m;z++)for(int x=0;x<n;x++)
        {
            int i=z*n+x;var p=new Vector3(bounds.min.x+1+2*x,60,bounds.min.z+1+2*z);
            var hits=Physics.RaycastAll(p,Vector3.down,100,~0,QueryTriggerInteraction.Ignore).Where(v=>!v.collider.transform.IsChildOf(player.transform)).OrderByDescending(v=>v.point.y).ToArray();
            if(hits.Length==0)continue;var hit=hits[0];float y=hit.point.y;
            bool wet=water.VolumeCollider.bounds.Contains(new Vector3(p.x,-.5f,p.z))&&y<water.WaterSurfaceY-.35f;
            if(!wet&&Vector3.Angle(hit.normal,Vector3.up)>45)continue;
            // Do not count the tops of solid decorations as reachable land.
            if(!wet&&hit.collider!=Land&&!hit.collider.name.StartsWith("Causeway")&&!hit.collider.name.StartsWith("TowerWalkway")&&!hit.collider.name.StartsWith("TowerRoof")&&!hit.collider.name.StartsWith("FutureEnding"))continue;
            y=wet?water.WaterSurfaceY:y;
            bool blocked=Physics.OverlapCapsule(new Vector3(p.x,y+.45f,p.z),new Vector3(p.x,y+1.55f,p.z),.28f,~0,QueryTriggerInteraction.Ignore).Any(c=>c!=hit.collider&&!c.transform.IsChildOf(player.transform));
            if(blocked)continue;kind[i]=wet?2:1;heights[i]=y;
        }
        int sx=Mathf.Clamp((int)((player.transform.position.x-bounds.min.x)/2),0,n-1),sz=Mathf.Clamp((int)((player.transform.position.z-bounds.min.z)/2),0,m-1);
        var seen=new bool[n*m];var q=new Queue<int>();if(kind[sz*n+sx]!=0){q.Enqueue(sz*n+sx);seen[sz*n+sx]=true;}
        while(q.Count>0){int u=q.Dequeue();int ux=u%n,uz=u/n;foreach(var delta in new[]{new Vector2Int(1,0),new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(0,-1)}){int x=ux+delta.x,z=uz+delta.y;if(x<0||x>=n||z<0||z>=m)continue;int v=z*n+x;if(seen[v]||kind[v]==0||Mathf.Abs(heights[u]-heights[v])>2)continue;var a=new Vector3(bounds.min.x+1+2*ux,heights[u]+1,bounds.min.z+1+2*uz);var b=new Vector3(bounds.min.x+1+2*x,heights[v]+1,bounds.min.z+1+2*z);if(Physics.Linecast(a,b,~0,QueryTriggerInteraction.Ignore))continue;seen[v]=true;q.Enqueue(v);}}
        int land=0,wetArea=0;for(int i=0;i<kind.Length;i++){if(seen[i]){if(kind[i]==1)land++;else wetArea++;}rows.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2:F3},{3},{4}",bounds.min.x+1+2*(i%n),bounds.min.z+1+2*(i/n),heights[i],kind[i],seen[i]?1:0));}
        Directory.CreateDirectory(Report);File.WriteAllLines(Report+phase+"-grid.csv",rows);
        string result=$"Bounds={bounds.size.x} x {bounds.size.z}; step=2; land={land*4}; water={wetArea*4}; union={(land+wetArea)*4}; units2";
        File.WriteAllText(Report+phase+"-area.txt",result);Debug.Log(result);
    }
    static void Move(string name,Vector3 dest)
    {var g=GameObject.Find(name);if(!g)throw new Exception(name);Undo.RecordObject(g.transform,"Expand baseline");g.transform.position=dest;}
    static void Shift(Transform t,Vector3 delta){Undo.RecordObject(t,"Expand baseline");t.position+=delta;}
    static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/_Demo/BaselineData/"+name+".mat");
    static GameObject Block(string name,PrimitiveType type,Vector3 pos,Vector3 scale,Material mat)
    {var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(Root);g.transform.position=pos;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=mat;Undo.RegisterCreatedObjectUndo(g,"Expand baseline");return g;}
    static void Migrate(string name,Vector3 target)
    {
        var g=GameObject.Find(name);var old=g.transform.position;var delta=target-old;
        foreach(Transform t in Root)if(t.name.StartsWith("Label_")&&Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(old.x,old.z))<.2f)Shift(t,delta);
        Shift(g.transform,delta);
    }
    public static void Apply()
    {
        Guard();if(GameObject.Find("Expansion_RockWest1"))throw new Exception("Already expanded");
        var mesh=Land.sharedMesh;Undo.RegisterCompleteObjectUndo(mesh,"Expand terrain mesh");
        const int n=171;var v=new Vector3[n*n];var uv=new Vector2[v.Length];var tr=new int[(n-1)*(n-1)*6];int k=0;
        for(int z=0;z<n;z++)for(int x=0;x<n;x++){int i=z*n+x;v[i]=new Vector3(x-85,Height(x-85,z-85),z-85);uv[i]=new Vector2(x/20f,z/20f);if(x==n-1||z==n-1)continue;tr[k++]=i;tr[k++]=i+n;tr[k++]=i+1;tr[k++]=i+1;tr[k++]=i+n;tr[k++]=i+n+1;}
        mesh.Clear();mesh.vertices=v;mesh.uv=uv;mesh.triangles=tr;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);Land.sharedMesh=null;Land.sharedMesh=mesh;
        foreach(var dir in new[]{"W","E","N","S"}){var t=GameObject.Find("Boundary_"+dir).transform;Undo.RecordObject(t,"Boundary expansion");bool horizontal=dir=="N"||dir=="S";t.position=new Vector3(dir=="W"?-85:dir=="E"?85:0,5,dir=="S"?-85:dir=="N"?85:0);t.localScale=horizontal?new Vector3(172,16,2):new Vector3(2,16,172);}
        var wm=new Mesh {name="CurvedLakeSurface"};var wv=new List<Vector3>{Vector3.zero};var wt=new List<int>();
        for(int i=0;i<128;i++){float a=i*Mathf.PI*2/128;float radius=LakeRadius(a)+1.3f;wv.Add(new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}
        for(int i=0;i<128;i++){wt.Add(0);wt.Add((i+1)%128+1);wt.Add(i+1);}wm.SetVertices(wv);wm.SetTriangles(wt,0);wm.RecalculateNormals();wm.RecalculateBounds();AssetDatabase.CreateAsset(wm,"Assets/_Demo/BaselineData/ExpandedLakeSurface.asset");
        var surface=GameObject.Find("Lake_Surface_REPLACE");surface.transform.position=Vector3.zero;surface.transform.localScale=Vector3.one;surface.GetComponent<MeshFilter>().sharedMesh=wm;
        var water=GameObject.Find("Lake_SwimmingVolume");water.transform.position=new Vector3(0,-3,0);water.GetComponent<BoxCollider>().size=new Vector3(70,6,70);
        var bridges=Root.Cast<Transform>().Where(t=>t.name=="Causeway_Segment_REPLACE").OrderBy(t=>t.position.z).ToArray();
        Vector3 start=P(0,-34),end=P(3,-5);var d=end-start;for(int i=0;i<bridges.Length;i++){var t=bridges[i];Undo.RecordObject(t,"Bridge extension");t.position=Vector3.Lerp(start,end,(i+.5f)/bridges.Length)+Vector3.down*.25f;t.rotation=Quaternion.LookRotation(d);t.localScale=new Vector3(3.5f,.5f,d.magnitude/bridges.Length+.1f);}
        Move("_Gameplay/Player",P(0,-67,1.05f));Move("_Gameplay/PlayerSpawnPoint",P(0,-67,1.05f));Migrate("_Gameplay/IntroNPC",P(-4,-61));
        Migrate("Island notice",P(7,-59));Migrate("Eastern notice",P(-11,-57));Migrate("Survey cylinder",P(-51,-7));
        foreach(Transform t in Root.Cast<Transform>().ToArray())if(t.name.StartsWith("IslandWall")||t.name.StartsWith("IslandBack")||t.name.StartsWith("IslandBroken")||t.name.StartsWith("Hologram"))Shift(t,new Vector3(3,0,3));
        Migrate("Island inscription",P(3,4));Migrate("Island hidden chest",P(-1.5f,6.3f));
        Migrate("Western hidden chest",P(-64,10,Height(-64,10)));Move("WestHiddenWall_REPLACE",P(-62,6,Height(-62,6)+1.8f));Move("ClimbShortcut",P(-60,10,Height(-60,10)+.65f));Move("ClimbLanding",P(-59,12,Height(-59,12)+1.15f));
        Migrate("Eastern relay",P(49,12,3));Migrate("Weathered inscription",P(45,7,3));Migrate("Sealed eastern chest",P(50,23,3));
        string[] runes={"WAVE","LEAF","SUN"};for(int i=0;i<3;i++)Migrate(runes[i]+" rune",P(46+3*i,18,3));
        int wallIndex=0;foreach(Transform t in Root)if(t.name=="EasternRuinWall_REPLACE"){t.position=P(39,8+4*wallIndex,4.8f);t.localScale=new Vector3(.8f,3.6f,3.8f);wallIndex++;}
        Portal("SideWorldA_Guide",P(-49,22));Portal("SideWorldB_Guide",P(57,-23,Height(57,-23)));
        foreach(Transform t in Root)if(t.name.StartsWith("Tower")||t.name.StartsWith("FutureEnding")||t.name.StartsWith("Guardian_")||t.name.StartsWith("Label_TOWER"))Shift(t,new Vector3(0,0,25));
        Move("Label_LAKE DOMAIN / A2 BASELINE",P(0,-60,5));
        // Old ring trees become irregular clusters; silhouettes remain primitive shapes.
        var trunks=Root.Cast<Transform>().Where(t=>t.name=="TreeTrunk_REPLACE").ToArray();var crowns=Root.Cast<Transform>().Where(t=>t.name=="TreeCanopy_REPLACE").ToArray();
        Vector2[] trees={new Vector2(-31,-40),new Vector2(-42,-28),new Vector2(-46,-23),new Vector2(-63,-23),new Vector2(-65,-12),new Vector2(-45,-12),new Vector2(-42,0),new Vector2(-66,19),new Vector2(-57,30),new Vector2(-36,29),new Vector2(-20,43),new Vector2(23,-42),new Vector2(38,-29),new Vector2(66,-27),new Vector2(67,5),new Vector2(62,29),new Vector2(29,44),new Vector2(14,63)};
        var paired=trunks.Select(trunk=>crowns.OrderBy(t=>Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(trunk.position.x,trunk.position.z))).First()).ToArray();
        for(int i=0;i<trunks.Length;i++){var old=trunks[i].position;var crown=paired[i];var p=trees[i%trees.Length];var delta=new Vector3(p.x,Height(p.x,p.y)+1.4f,p.y)-old;Shift(trunks[i],delta);Shift(crown,delta);}
        Block("Expansion_RockWest1",PrimitiveType.Cube,P(-43,-20,Height(-43,-20)+2),new Vector3(5,4,11),Mat("Ruins")).transform.rotation=Quaternion.Euler(0,25,0);
        Block("Expansion_RockEastEntrance",PrimitiveType.Cube,P(43,-2,Height(43,-2)+2),new Vector3(5,4,8),Mat("Ruins"));
        Block("Expansion_IslandScreen",PrimitiveType.Cube,P(5,-.5f,2.5f),new Vector3(6,3,.7f),Mat("Ruins"));
        for(int i=0;i<12;i++){float x=i<6?-74:74,z=-57+(i%6)*23;Block("Expansion_PerimeterRock",PrimitiveType.Sphere,P(x,z,Height(x,z)+1.5f),new Vector3(8,5,11),Mat("Ruins"));}
        var world=UnityEngine.Object.FindFirstObjectByType<BaselineWorld>();var nodes=new List<Vector3>();var edges=new List<Vector2Int>();
        Action<Vector3[]> add=path=>{int last=-1;foreach(var p in path){int j=nodes.FindIndex(x=>Vector3.Distance(x,p)<.1f);if(j<0){j=nodes.Count;nodes.Add(p);}if(last>=0)edges.Add(new Vector2Int(last,j));last=j;}};
        add(West);add(East);add(Bridge);add(new[]{P(-57,-16),P(-66,-4,Height(-66,-4)),P(-68,12,Height(-68,12)),P(-57,20,Height(-57,20)),P(-45,27)});
        add(new[]{P(-25,-47),P(-18,-33),P(0,-34)});add(new[]{P(31,-45),P(21,-33),P(0,-34)});
        Undo.RecordObject(world,"Update route graph");world.routeNodes=nodes.Select(p=>p+Vector3.up*.15f).ToArray();world.routeEdges=edges.ToArray();
        var camera=Camera.main;camera.transform.position=P(0,-74,5);camera.transform.rotation=Quaternion.Euler(16,0,0);camera.farClipPlane=240;
        RenderSettings.fogStartDistance=135;RenderSettings.fogEndDistance=240;
        Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(world.gameObject.scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(world.gameObject.scene);
        Debug.Log("EXPANSION_APPLIED");
    }
    static void Portal(string name,Vector3 target)
    {var g=GameObject.Find(name);var delta=target-g.transform.position;foreach(Transform t in Root)if(t.name.StartsWith(name+"Portal"))Shift(t,delta);Migrate(name,target);}
}
