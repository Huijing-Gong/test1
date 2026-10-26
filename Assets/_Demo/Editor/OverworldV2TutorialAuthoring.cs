using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// One bounded addition to the migrated scene; never reruns the initial world generator.
public static class OverworldV2TutorialAuthoring
{
    const string Folder = "Assets/Art/MapV2Study/Tutorial/";
    const string RootName = "V2 optional traversal practice";
    static Collider terrain;
    static Material stone;
    static readonly List<Collider> surfaces = new List<Collider>();
    static Vector3 Ground(float x, float z)
    {
        if (!terrain.Raycast(new Ray(new Vector3(x,70,z),Vector3.down),out var hit,120))
            throw new InvalidOperationException("Missing terrain at " + x + "," + z);
        return hit.point;
    }
    static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Quaternion rotation)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent,true); go.transform.SetPositionAndRotation(position,rotation); go.transform.localScale=size;
        go.GetComponent<Renderer>().sharedMaterial=stone; return go;
    }
    static MeshCollider Ramp(Transform parent,Vector3 a,Vector3 b,float width)
    {
        // Closed wedge, independently faced vertices preserve hard stone edges.
        var forward=Vector3.ProjectOnPlane(b-a,Vector3.up).normalized;
        var right=Vector3.Cross(Vector3.up,forward)*width*.5f;
        var corners=new[]{a-right,a+right,b+right,b-right,
            new Vector3(a.x,Ground(a.x,a.z).y-.15f,a.z)-right,
            new Vector3(a.x,Ground(a.x,a.z).y-.15f,a.z)+right,
            new Vector3(b.x,Ground(b.x,b.z).y-.15f,b.z)+right,
            new Vector3(b.x,Ground(b.x,b.z).y-.15f,b.z)-right};
        var vertices=new List<Vector3>();var indices=new List<int>();var uv=new List<Vector2>();
        Action<int,int,int,int> face=(i,j,k,l)=>{
            int n=vertices.Count;vertices.Add(corners[i]);vertices.Add(corners[j]);vertices.Add(corners[k]);vertices.Add(corners[l]);
            uv.Add(Vector2.zero);uv.Add(new Vector2(width,0));uv.Add(new Vector2(width,(b-a).magnitude));uv.Add(new Vector2(0,(b-a).magnitude));
            indices.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});
        };
        face(0,1,2,3);face(5,4,7,6);face(4,5,1,0);face(1,5,6,2);face(2,6,7,3);face(3,7,4,0);
        var mesh=new Mesh{name="Optional practice stone ramp",vertices=vertices.ToArray(),triangles=indices.ToArray(),uv=uv.ToArray()};
        mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Folder+"PracticeRamp.asset");
        var go=new GameObject("Walkable stone approach ramp",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));
        go.transform.SetParent(parent,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=stone;
        var collider=go.GetComponent<MeshCollider>();collider.sharedMesh=mesh;return collider;
    }
    public static string Apply()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.isDirty||scene.path!="Assets/_Demo/Scenes/OverworldBaseline.unity"||SceneManager.sceneCount!=1)
            throw new InvalidOperationException("One clean migrated main scene in Edit Mode required.");
        var layout=Object.FindFirstObjectByType<OverworldV2Layout>();
        if(!layout||layout.TutorialPracticeReady||GameObject.Find(RootName)||AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"PracticeRamp.asset"))
            throw new InvalidOperationException("Missing V2 layout or practice already authored: refine locally, do not regenerate.");
        terrain=GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<Collider>();
        stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/MapV2Study/TowerLimestone.mat");
        if(!stone)throw new InvalidOperationException("Existing limestone material required.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();surfaces.Clear();
        var root=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(root,"Add optional traversal practice");
        var heading=Quaternion.LookRotation(new Vector3(-10,0,9));
        var hurdleGround=Ground(-17.05f,-27.05f);
        Box(root.transform,"Low jump stone - walk around to bypass",hurdleGround+Vector3.up*.31f,new Vector3(2.6f,.62f,.45f),heading);
        surfaces.Add(Ramp(root.transform,new Vector3(-18.2f,2.58f,-26),new Vector3(-25.2f,6.4f,-19.7f),3.2f));
        var deck=Box(root.transform,"Broad step-off ledge",new Vector3(-26,5.65f,-19),new Vector3(3.2f,1.5f,2.4f),heading);
        surfaces.Add(deck.GetComponent<Collider>());
        // Solid support reaches the ground; the exit is deliberately an actual fall, not a walk route.
        var basePoint=Ground(-26,-19);
        Box(root.transform,"Stone ledge support",new Vector3(-26,(basePoint.y+4.9f)*.5f,-19),new Vector3(2.9f,4.9f-basePoint.y+.1f,2.1f),heading);
        var climb=Box(root.transform,"Island climbable low terrace",new Vector3(-36.5f,1.9f,15.75f),new Vector3(3.4f,2,3.5f),Quaternion.identity);
        surfaces.Add(climb.GetComponent<Collider>());
        // Shallow north-facing exit, leaving the original dry landing and west/east bypasses clear.
        surfaces.Add(RampExit(root.transform));
        Undo.RecordObject(layout,"Set physical teaching anchors");var so=new SerializedObject(layout);
        so.FindProperty("jumpPracticePosition").vector3Value=Ground(-16,-28);
        so.FindProperty("dropPlatformPosition").vector3Value=new Vector3(-26,6.4f,-19);
        so.FindProperty("dropLandingPosition").vector3Value=Ground(-30,-16);
        so.FindProperty("climbBasePosition").vector3Value=Ground(-36.5f,12.8f);
        so.FindProperty("climbTopPosition").vector3Value=new Vector3(-36.5f,2.9f,15.75f);
        var array=so.FindProperty("navigationSurfaces");int old=array.arraySize;array.arraySize=old+surfaces.Count;
        for(int i=0;i<surfaces.Count;i++)array.GetArrayElementAtIndex(old+i).objectReferenceValue=surfaces[i];
        so.FindProperty("tutorialPracticeReady").boolValue=true;so.ApplyModifiedProperties();
        Physics.SyncTransforms();var world=layout.GetComponent<BaselineWorld>();world.RefreshNavigation();
        // The terrace occupies the old direct island shore edge. Keep walking
        // guidance on the open west bypass rather than drawing a line up the wall.
        if(world.routeNodes.Length==79 && world.routeEdges.Length==81 &&
            world.NavigationGround(new Vector3(-39.5f,2.5f,15),out var west) &&
            world.NavigationGround(new Vector3(-39.5f,2.5f,19.5f),out var north) &&
            world.NavigationSegment(world.routeNodes[72],west) && world.NavigationSegment(west,north) &&
            world.NavigationSegment(north,world.routeNodes[73]))
        {
            Undo.RecordObject(world,"Route around tutorial terrace");
            Array.Resize(ref world.routeNodes,81);world.routeNodes[79]=west;world.routeNodes[80]=north;
            bool found=false;
            for(int i=0;i<81;i++) if(world.routeEdges[i]==new Vector2Int(72,73))
            {world.routeEdges[i]=new Vector2Int(72,79);found=true;break;}
            if(!found)throw new InvalidOperationException("Expected original island walking edge missing.");
            Array.Resize(ref world.routeEdges,83);world.routeEdges[81]=new Vector2Int(79,80);world.routeEdges[82]=new Vector2Int(80,73);
            world.RefreshNavigation();
        }
        else throw new InvalidOperationException("Cannot safely adapt the original shore walking graph; inspect locally.");
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Could not save authored practice scene.");
        return "Added optional jump/drop spur and island climbing terrace. Existing terrain, water and identities unchanged. Physical QA required.";
    }
    static Collider RampExit(Transform parent)
    {
        // Short 20 degree bevel, walkable from either side and outside the teaching wall.
        var far=Ground(-36.5f,18.5f);var start=new Vector3(-36.5f,2.9f,17.25f);
        var end=new Vector3(far.x,far.y+.03f,far.z);var midpoint=(start+end)*.5f;
        var go=Box(parent,"North terrace descent",midpoint-Vector3.up*.1f,new Vector3(3.1f,.2f,Vector3.Distance(start,end)+.15f),Quaternion.LookRotation(end-start));
        return go.GetComponent<Collider>();
    }
}
