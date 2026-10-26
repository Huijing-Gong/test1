using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One local patch, never a scene generator. Refuses dirty scenes or a second application.
public static class OpeningAreaApply
{
    const string Folder="Assets/_Demo/BaselineData/Opening";
    const string Report="ToolReports/OpeningTutorial";
    public static void Apply()
    {
        var scene=SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.isDirty || scene.path!="Assets/_Demo/Scenes/OverworldBaseline.unity")
            throw new InvalidOperationException("Requires saved OverworldBaseline in Edit Mode.");
        if(GameObject.Find("Opening arrival pocket"))throw new InvalidOperationException("Opening patch already exists; edit it locally.");
        Directory.CreateDirectory(Report);
        File.Copy(scene.path,Report+"/before-scene.unity",true);
        File.WriteAllText(Report+"/before-identities.txt",OverworldShowcaseApply.Identities());
        Capture("before");
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/_Demo/BaselineData","Opening");
        var root=new GameObject("Opening arrival pocket");Undo.RegisterCreatedObjectUndo(root,"Opening local layout");
        root.transform.SetParent(GameObject.Find("_A2_Greybox").transform,false);
        var ground=GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<MeshCollider>();
        Rock(root.transform,ground,new Vector3(-31,0,-73),new Vector3(7,5,13),18,0);
        Rock(root.transform,ground,new Vector3(-14,0,-73),new Vector3(6,3.8f,7),-25,1);
        Rock(root.transform,ground,new Vector3(-25,0,-56),new Vector3(9,5.5f,8),30,0);
        Rock(root.transform,ground,new Vector3(-12,0,-49),new Vector3(8,4.5f,7),-18,1);
        Rock(root.transform,ground,new Vector3(10,0,-48),new Vector3(8,4.3f,10),25,0);
        Rock(root.transform,ground,new Vector3(-32,0,-64),new Vector3(3,1.5f,4),55,1);
        Rock(root.transform,ground,new Vector3(-14,0,-80),new Vector3(3.5f,1.8f,3),10,0);
        var barrier=GameObject.CreatePrimitive(PrimitiveType.Quad);barrier.name="Opening visible southern barrier";
        Undo.RegisterCreatedObjectUndo(barrier,"Opening barrier");barrier.transform.SetParent(root.transform,false);
        barrier.transform.position=new Vector3(-22,5.5f,-83.98f);barrier.transform.localScale=new Vector3(45,11,1);
        UnityEngine.Object.DestroyImmediate(barrier.GetComponent<Collider>());
        var material=new Material(UnityEngine.Object.FindFirstObjectByType<BaselineEndingRoom>().barrierMaterial){name="Opening southern shield"};
        material.SetFloat("_PixelColumns",48);material.SetFloat("_PixelRows",12);material.SetFloat("_GridStrength",.06f);
        AssetDatabase.CreateAsset(material,Folder+"/OpeningBarrier.mat");barrier.GetComponent<Renderer>().sharedMaterial=material;
        // Existing Boundary_S provides solid, nonclimbable collision at exactly this edge.
        BuildTrail(root.transform,ground);
        var tutorial=UnityEngine.Object.FindFirstObjectByType<BaselineCampTutorial>();
        Undo.RecordObject(tutorial,"Opening introduction references");
        var serialized=new SerializedObject(tutorial);
        serialized.FindProperty("guide").objectReferenceValue=GameObject.Find("IntroNPC").GetComponent<NPCDialogue>();
        serialized.ApplyModifiedProperties();
        Physics.SyncTransforms();
        File.WriteAllText(Report+"/after-identities.txt",OverworldShowcaseApply.Identities());
        if(File.ReadAllText(Report+"/before-identities.txt")!=File.ReadAllText(Report+"/after-identities.txt"))
            throw new InvalidOperationException("Unexpected interaction identity change.");
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        Capture("after");
    }
    static float Ground(MeshCollider terrain, Vector3 p)
    {
        if(!terrain.Raycast(new Ray(new Vector3(p.x,40,p.z),Vector3.down),out var hit,80))throw new Exception("No terrain: "+p);
        return hit.point.y;
    }
    static Bounds Bounds(GameObject go)
    {
        var renderers=go.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;
        foreach(var r in renderers)b.Encapsulate(r.bounds);return b;
    }
    static void Rock(Transform parent,MeshCollider terrain,Vector3 p,Vector3 size,float yaw,int variation)
    {
        string path="Assets/Art/OverworldShowcase/OverworldAssetShowcase/SelectedPrefabs/Rock_L"+(variation+1)+".prefab";
        var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        Undo.RegisterCreatedObjectUndo(go,"Opening existing rock");go.name="Opening_Rock";
        foreach(var c in go.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(c);
        go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);go.transform.localScale=Vector3.one;
        var b=Bounds(go);go.transform.localScale=new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z);
        go.transform.rotation=Quaternion.Euler(0,yaw,0);b=Bounds(go);
        go.transform.position=new Vector3(p.x-b.center.x,Ground(terrain,p)-b.min.y-.15f,p.z-b.center.z);
        go.transform.SetParent(parent,true);
        foreach(var f in go.GetComponentsInChildren<MeshFilter>())
            if(f.sharedMesh)f.gameObject.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;
    }
    static void BuildTrail(Transform parent,MeshCollider terrain)
    {
        var points=new[]{new Vector3(-22,0,-80),new Vector3(-22,0,-76),new Vector3(-22,0,-70),new Vector3(-22,0,-66),
            new Vector3(-18,0,-64),new Vector3(-12,0,-62),new Vector3(-5,0,-63),new Vector3(0,0,-58),new Vector3(0,0,-51),new Vector3(0,0,-43)};
        var vertices=new System.Collections.Generic.List<Vector3>();
        var uv=new System.Collections.Generic.List<Vector2>();var triangles=new System.Collections.Generic.List<int>();
        int across=6,row=0;
        for(int segment=0;segment<points.Length-1;segment++)
        {
            var direction=(points[segment+1]-points[segment]).normalized;
            var right=Vector3.Cross(Vector3.up,direction);
            int steps=Mathf.CeilToInt(Vector3.Distance(points[segment],points[segment+1])/.5f);
            for(int step=0;step<steps;step++)
            {
                var centre=Vector3.Lerp(points[segment],points[segment+1],(float)step/steps);
                float half=segment<2?2.8f:1.6f;
                half*=1+.05f*Mathf.Sin(row*1.23f);
                for(int side=0;side<=across;side++)
                {
                    var p=centre+right*half*((float)side/across*2-1);
                    p.y=Ground(terrain,p)+.045f;vertices.Add(p);uv.Add(new Vector2(p.x/9,p.z/9));
                    if(row>0 && side<across)
                    {
                        int v=row*(across+1)+side,n=v-(across+1);
                        triangles.Add(n);triangles.Add(v);triangles.Add(n+1);triangles.Add(n+1);triangles.Add(v);triangles.Add(v+1);
                    }
                }
                row++;
            }
        }
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/OpeningPath.asset");
        if(!mesh){mesh=new Mesh{name="Opening local path surface"};AssetDatabase.CreateAsset(mesh,Folder+"/OpeningPath.asset");}
        Undo.RecordObject(mesh,"Conform opening trail");mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        var existing=GameObject.Find("Opening rough path (visual only)");
        var go=existing?existing:new GameObject("Opening rough path (visual only)",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        if(!existing)Undo.RegisterCreatedObjectUndo(go,"Opening trail");
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/OpeningPath.mat");
        if(!mat)
        {
            mat=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/OverworldShowcase/OverworldExpansion/Materials/M_Surface_Grass.mat")){name="Opening worn earth"};
            mat.SetColor("_BaseColor",new Color(.50f,.34f,.19f));mat.SetTextureScale("_BaseMap",Vector2.one);
            AssetDatabase.CreateAsset(mat,Folder+"/OpeningPath.mat");
        }
        go.GetComponent<Renderer>().sharedMaterial=mat;
    }
    public static void RefineTrail()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Edit Mode only");
        BuildTrail(GameObject.Find("Opening arrival pocket").transform,GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<MeshCollider>());
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());Capture("after");
    }
    public static void Capture(string prefix)
    {
        Directory.CreateDirectory(Report);
        OverworldShowcaseApply.Render(Report+"/"+prefix+"-overview.png",new Vector3(-10,90,-63),new Vector3(-10,0,-63));
        OverworldShowcaseApply.Render(Report+"/"+prefix+"-arrival.png",new Vector3(-22,4,-81),new Vector3(-22,2,-67));
        OverworldShowcaseApply.Render(Report+"/"+prefix+"-guide.png",new Vector3(-12,4,-67),new Vector3(-4,2,-61));
        OverworldShowcaseApply.Render(Report+"/"+prefix+"-vista.png",new Vector3(0,4,-49),new Vector3(0,3,5));
    }
}

