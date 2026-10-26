using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Bounded additive authoring, never a map rebuild. Refuses to overwrite existing facilities.
public static class OverworldV2WaystationAuthoring
{
    const string Folder = "Assets/Art/MapV2Study/Waystations/";
    static Collider terrain;
    static Material stone, canvas, trim, wood, portal;
    static Vector3 Ground(float x,float z,float lift=.03f)
    {
        if (!terrain.Raycast(new Ray(new Vector3(x,70,z),Vector3.down),out var hit,120))
            throw new InvalidOperationException("No authored terrain below "+x+","+z);
        return hit.point+Vector3.up*lift;
    }
    static Material Lit(string name,Color color)
    {
        string path=Folder+name+".mat"; var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m)return m;
        m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};
        m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.15f);m.SetFloat("_Cull",0);
        AssetDatabase.CreateAsset(m,path);return m;
    }
    static GameObject Group(string name,Transform parent,Vector3 position)
    {var g=new GameObject(name);g.transform.SetParent(parent,true);g.transform.position=position;return g;}
    static GameObject Box(string name,Transform parent,Vector3 p,Vector3 size,Material mat,bool collision=true)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);
        g.transform.localPosition=p;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=mat;
        if(!collision)Object.DestroyImmediate(g.GetComponent<Collider>());return g;
    }
    static void Rod(string name,Transform parent,Vector3 a,Vector3 b,float width,Material mat,bool collision=false)
    {
        var g=Box(name,parent,(a+b)*.5f,new Vector3(width,width,(b-a).magnitude),mat,collision);
        g.transform.localRotation=Quaternion.LookRotation(b-a,Vector3.up);
    }
    static Mesh MeshAsset(string name,Vector3[] vertices,int[] triangles,Vector2[] uv=null)
    {
        string path=Folder+name+".asset";
        if(AssetDatabase.LoadAssetAtPath<Mesh>(path))throw new InvalidOperationException("Existing authored mesh: "+path);
        var m=new Mesh{name=name,vertices=vertices,triangles=triangles};
        if(uv!=null)m.uv=uv;m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.CreateAsset(m,path);return m;
    }
    static GameObject MeshObject(string name,Transform parent,Mesh mesh,Material mat,bool solid)
    {
        var g=Group(name,parent,parent.position);g.AddComponent<MeshFilter>().sharedMesh=mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial=mat;
        if(solid)g.AddComponent<MeshCollider>().sharedMesh=mesh;return g;
    }
    static void Paving(Transform parent,float radius,string name)
    {
        // Follows the existing collider within 3cm, so it adds no artificial step or navigation surface.
        var v=new List<Vector3>();var ix=new List<int>();var uv=new List<Vector2>();var o=parent.position;
        v.Add(Ground(o.x,o.z,.045f)-o);uv.Add(Vector2.zero);
        const int count=40,rings=6;
        for(int ring=1;ring<=rings;ring++)for(int i=0;i<count;i++)
        {float a=i*Mathf.PI*2/count;float x=Mathf.Sin(a)*radius*ring/rings,z=Mathf.Cos(a)*radius*ring/rings;
            v.Add(Ground(o.x+x,o.z+z,.045f)-o);uv.Add(new Vector2(x,z)*.15f);}
        for(int i=0;i<count;i++){ix.Add(0);ix.Add(i+1);ix.Add((i+1)%count+1);}
        for(int ring=1;ring<rings;ring++)for(int i=0;i<count;i++)
        {int a=1+(ring-1)*count+i,b=1+(ring-1)*count+(i+1)%count,c=a+count,d=b+count;
            ix.AddRange(new[]{a,c,d,a,d,b});}
        MeshObject(name,parent,MeshAsset(name,v.ToArray(),ix.ToArray(),uv.ToArray()),stone,false);
    }
    static void Portal(Transform parent,float scale,Transform player)
    {
        var g=Group("Return relay - existing team portal shader",parent,parent.position);g.transform.localScale=Vector3.one*scale;
        Box("Left stone footing",g.transform,new Vector3(-1.5f,.04f,0),new Vector3(.65f,.4f,.85f),stone);
        Box("Right stone footing",g.transform,new Vector3(1.5f,.04f,0),new Vector3(.65f,.4f,.85f),stone);
        Box("Left post",g.transform,new Vector3(-1.5f,1.1f,0),new Vector3(.42f,2.1f,.48f),stone);
        Box("Right post",g.transform,new Vector3(1.5f,1.1f,0),new Vector3(.42f,2.1f,.48f),stone);
        for(int i=0;i<9;i++)
        {
            float a=i*Mathf.PI/9,b=(i+1)*Mathf.PI/9;
            Rod("Arch stone "+i,g.transform,new Vector3(Mathf.Cos(a)*1.5f,2.1f+Mathf.Sin(a)*1.5f,0),
                new Vector3(Mathf.Cos(b)*1.5f,2.1f+Mathf.Sin(b)*1.5f,0),.42f,stone,true);
        }
        var screen=GameObject.CreatePrimitive(PrimitiveType.Quad);screen.name="Team PortalScan energy surface";
        screen.transform.SetParent(g.transform,false);screen.transform.localPosition=new Vector3(0,1.85f,.015f);
        screen.transform.localScale=new Vector3(2.8f,3.65f,1);Object.DestroyImmediate(screen.GetComponent<Collider>());
        screen.GetComponent<Renderer>().sharedMaterial=portal;
        var controller=screen.AddComponent<PortalScanController>();var so=new SerializedObject(controller);
        so.FindProperty("portalRenderer").objectReferenceValue=screen.GetComponent<Renderer>();so.FindProperty("player").objectReferenceValue=player;
        so.FindProperty("activationDistance").floatValue=14;so.FindProperty("revealDuration").floatValue=1.3f;so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Shelter(Transform parent)
    {
        var g=Group("Small ochre canvas shelter",parent,Ground(47,44));g.AddComponent<NonClimbableSurface>();
        var v=new[]{new Vector3(-2,0,-2.1f),new Vector3(0,2.6f,-2.1f),new Vector3(0,2.6f,2.1f),new Vector3(-2,0,2.1f),
            new Vector3(0,2.6f,-2.1f),new Vector3(2,0,-2.1f),new Vector3(2,0,2.1f),new Vector3(0,2.6f,2.1f),
            new Vector3(-2,0,2.1f),new Vector3(0,2.6f,2.1f),new Vector3(2,0,2.1f)};
        MeshObject("Canvas",g.transform,MeshAsset("CampCanvas",v,new[]{0,2,1,0,3,2,4,6,5,4,7,6,8,10,9}),canvas,true);
        foreach(float z in new[]{-2.1f,2.1f})
        {
            Rod("Canvas hem",g.transform,new Vector3(-2,0,z),new Vector3(0,2.6f,z),.07f,trim);
            Rod("Canvas hem",g.transform,new Vector3(0,2.6f,z),new Vector3(2,0,z),.07f,trim);
        }
        Rod("Ridge",g.transform,new Vector3(0,2.57f,-2.35f),new Vector3(0,2.57f,2.35f),.12f,wood);
        Box("Rear support",g.transform,new Vector3(0,1.15f,1.9f),new Vector3(.12f,2.9f,.12f),wood);
        Box("Rolled bed mat",g.transform,new Vector3(-.6f,.12f,.45f),new Vector3(.8f,.16f,1.8f),trim,false);
        Box("Low provision crate",g.transform,new Vector3(.65f,.3f,1.05f),new Vector3(.6f,.6f,.75f),wood);
    }
    static void Configure(GameObject root,BaselineFlow flow,int role,Vector3 point)
    {
        var anchor=Group("Clear interaction and rest anchor",root.transform,point);
        var station=root.AddComponent<BaselineWaystation>();var so=new SerializedObject(station);
        so.FindProperty("flow").objectReferenceValue=flow;so.FindProperty("role").enumValueIndex=role;
        so.FindProperty("interactionPoint").objectReferenceValue=anchor.transform;
        so.FindProperty("interactionRadius").floatValue=role==0?3.25f:3.6f;so.ApplyModifiedPropertiesWithoutUndo();
    }
    public static string Apply()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!="Assets/_Demo/Scenes/OverworldBaseline.unity" || scene.isDirty || SceneManager.sceneCount!=1)
            throw new InvalidOperationException("Requires one clean V2 main scene in Edit Mode.");
        if(GameObject.Find("V2 return and supply facilities"))throw new InvalidOperationException("Facilities already authored; refine locally, never regenerate.");
        var world=Object.FindFirstObjectByType<BaselineWorld>();var flow=world.GetComponent<BaselineFlow>();
        if(!world.GetComponent<OverworldV2Layout>())throw new InvalidOperationException("Requires migrated map.");
        terrain=GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<Collider>();
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/MapV2Study/TowerLimestone.mat");
        portal=AssetDatabase.LoadAssetAtPath<Material>("Assets/PortalScan/Materials/M_PortalScan.mat");
        canvas=Lit("CampCanvasOchre",new Color(.66f,.32f,.13f));trim=Lit("CampCanvasTrim",new Color(.22f,.13f,.08f));wood=Lit("CampDarkWood",new Color(.25f,.17f,.10f));
        var all=Group("V2 return and supply facilities",null,Vector3.zero);Undo.RegisterCreatedObjectUndo(all,"Add V2 map facilities");
        var north=Group("B6 North return device",all.transform,Ground(-44,71));Paving(north.transform,3.7f,"NorthLandingPaving");Portal(north.transform,1,world.player);
        Configure(north,flow,0,Ground(-44,68.5f,.04f));
        var camp=Group("E5 Tower camp",all.transform,Ground(49,41));Shelter(camp.transform);
        var relay=Group("Small return relay",camp.transform,Ground(50.6f,43.1f));Portal(relay.transform,.55f,world.player);
        Configure(camp,flow,1,Ground(49,40,.04f));
        var fire=GameObject.Find("E5_Campfire_VisualOnly");if(!fire)throw new InvalidOperationException("Original campfire missing.");
        Undo.RecordObject(fire.transform,"Move original campfire off route junction");fire.transform.position=Ground(48,39,.025f);PrefabUtility.RecordPrefabInstancePropertyModifications(fire.transform);
        // Two low benches frame the rest point without closing the road or its camera space.
        var bench=Group("Simple rest bench",camp.transform,Ground(45.8f,40));
        Box("Seat",bench.transform,new Vector3(0,.55f,0),new Vector3(.55f,.15f,1.8f),wood);
        Box("Feet",bench.transform,new Vector3(0,.2f,-.6f),new Vector3(.38f,.6f,.22f),stone);
        Box("Feet",bench.transform,new Vector3(0,.2f,.6f),new Vector3(.38f,.6f,.22f),stone);
        Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Scene save failed.");
        return "Two stations authored; existing campfire moved, original terrain/navigation/progress identities unchanged. Run geometry and actual-input QA next.";
    }
}
