using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class LivingEnvironmentInstaller
{
    public const string ScenePath="Assets/_Demo/Scenes/OverworldBaseline.unity";
    public const string Folder="Assets/Art/LivingEnvironment";
    public const string Output="LocalDocs/Environment/LivingWorld-20261024";
    static readonly Dictionary<string,Mesh> meshes=new Dictionary<string,Mesh>();
    static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
    static readonly List<Vector4> canopies=new List<Vector4>();
    static readonly List<string> records=new List<string>();
    static string Hash(string path){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static bool Plant(string name)=>new[]{"tree","grass","flower","daisy","leaf","leaves","bush","reed","cypress","broadleaf","blossom","meadow","tuft","fern","apricot","vine"}.Any(name.ToLowerInvariant().Contains);
    static bool KnownMeadowPlant(OwnInstancedMeadow.Batch b)=>Plant(b.mesh.name)||Plant(AssetDatabase.GetAssetPath(b.mesh))||b.material.name=="F12_A"||b.material.name=="F12_C"||b.material.name=="OwnMeadowLowGreen147";
    static bool Excluded(string name)=>new[]{"terrain","ground","stone","stair","wall","banner","flag","paving","cliff","crystal","pillar","bridge","platform","terrace","trunk","bark"}.Any(name.ToLowerInvariant().Contains);
    static int Mode(Material m)
    {
        if(!m||!m.shader)return -1;
        switch(m.shader.name){
            case "KingOfGraphic/Own/GardenInstanced":return 0;
            case "KingOfGraphic/Own/BroadleafSoft144":return 1;
            case "KingOfGraphic/Own/BlossomSoft46":return 2;
            case "KingOfGraphic/Own/FoldedWorldDiffuse":return 3;
            case "KingOfGraphic/Own/ApricotTree":return 4;
            case "OverworldV2/LeanGrass":return 5;
            case "KingOfGraphic/Own/TwoSidedPlantDiffuseQA":return 6;
            case "Universal Render Pipeline/Lit":return 7;
            default:return -1;
        }
    }
    static string Profile(Mesh mesh)
    {
        string n=mesh.name.ToLowerInvariant();
        if(n.Contains("hanging")||n.Contains("trailing"))return "hanging";
        if(n.Contains("tree")||n.Contains("broadleaf")||n.Contains("cypress")||n.Contains("blossom"))return "tree";
        return "plant";
    }
    [MenuItem("Tools/Living Environment/Install In Isolated Project")]
    public static void Install()
    {
        Need(Application.dataPath.StartsWith("/private/tmp/adventure-trio-knight-",StringComparison.Ordinal),"Use the isolated project");
        Need(!Application.isPlaying,"Exit Play before installation");
        for(int s=0;s<SceneManager.sceneCount;s++)Need(!SceneManager.GetSceneAt(s).isDirty,"Save scene before authoring");
        var scene=EditorSceneManager.OpenScene(ScenePath);var host=Object.FindFirstObjectByType<BaselineWorld>();Need(host,"Actual overworld host exists");
        Need(!host.GetComponent<WorldVegetationWind>(),"Already installed; do not overwrite authored wind data");
        Directory.CreateDirectory(Output);
        meshes.Clear();materials.Clear();records.Clear();canopies.Clear();
        var protectedFiles=Directory.GetFiles("Assets","*",SearchOption.AllDirectories)
            .Where(p=>!p.StartsWith(Folder+"/",StringComparison.Ordinal)&&p!=ScenePath&& !p.EndsWith(".cs")&&!p.EndsWith(".cs.meta"))
            .ToDictionary(p=>p,Hash);
        Ensure(Folder+"/Meshes");Ensure(Folder+"/Materials");
        var shader=Shader.Find("FoldedRealm/LivingVegetation");Need(shader&&shader.isSupported,"Wind shader is supported");
        int instances=0,batches=0,renderers=0;
        foreach(var meadow in Object.FindObjectsByType<OwnInstancedMeadow>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(meadow.gameObject.scene!=scene||!meadow.gameObject.activeInHierarchy||!meadow.enabled)continue;
            foreach(var batch in meadow.batches)
            {
                if(!batch.mesh||batch.positions.Length==0)continue;
                if(Mode(batch.material)<0||!KnownMeadowPlant(batch)||Excluded(batch.mesh.name))
                {records.Add("SKIP "+batch.mesh.name+" | "+batch.positions.Length+" | "+(batch.material?batch.material.shader.name:"null"));continue;}
                string profile=Profile(batch.mesh);var source=batch.mesh;var sourceMat=batch.material;
                var basis=batch.meshToRoot;
                batch.mesh=Bake(source,basis,profile);
                if(batch.farMesh)batch.farMesh=Bake(batch.farMesh,basis,profile);
                batch.material=MaterialFor(sourceMat,profile,shader);
                instances+=batch.positions.Length;batches++;
                records.Add("INSTANCE "+meadow.name+" | "+source.name+" | "+profile+" | "+batch.positions.Length+" | "+AssetDatabase.GetAssetPath(sourceMat));
                if(profile=="tree")for(int i=0;i<batch.positions.Length;i++)
                {
                    var matrix=Matrix4x4.TRS(batch.positions[i],Quaternion.Euler(batch.eulers[i]),batch.scales[i])*basis;
                    AddCanopy(matrix.MultiplyPoint3x4(source.bounds.center),matrix.MultiplyVector(Vector3.up*source.bounds.extents.y).magnitude,sourceMat.name);
                }
            }
            EditorUtility.SetDirty(meadow);meadow.Rebuild();
        }
        foreach(var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            var r=filter.GetComponent<MeshRenderer>();var source=filter.sharedMesh;
            if(!r||!source||filter.gameObject.scene!=scene||!filter.gameObject.activeInHierarchy||!r.enabled||!Plant(source.name)||Excluded(source.name)||Excluded(filter.name))continue;
            var old=r.sharedMaterials;if(old.Length!=1||Mode(old[0])<0)continue;
            string profile=Profile(source);var basis=Matrix4x4.Rotate(filter.transform.rotation);
            // The metadata is computed in actual upright world axes, independent of FBX -90° imports.
            filter.sharedMesh=Bake(source,basis,profile);r.sharedMaterials=new[]{MaterialFor(old[0],profile,shader)};
            var flags=GameObjectUtility.GetStaticEditorFlags(filter.gameObject);
            GameObjectUtility.SetStaticEditorFlags(filter.gameObject,flags&~StaticEditorFlags.BatchingStatic);
            EditorUtility.SetDirty(filter);EditorUtility.SetDirty(r);renderers++;
            if(profile=="tree")AddCanopy(r.bounds.center,r.bounds.extents.y,old[0].name);
            records.Add("RENDERER "+filter.name+" | "+source.name+" | "+profile+" | "+AssetDatabase.GetAssetPath(old[0]));
        }
        File.WriteAllLines(Output+"/coverage-attempt.txt",new[]{"instances="+instances+" batches="+batches}.Concat(records));
        Need(instances>20000&&batches>20,"Actual instanced vegetation migrated: "+instances+" / "+batches);
        var wind=host.gameObject.AddComponent<WorldVegetationWind>();
        wind.canopySources=canopies.ToArray();
        BuildLeafAssets(wind);
        EditorUtility.SetDirty(wind);
        AssetDatabase.SaveAssets();
        Need(EditorSceneManager.SaveScene(scene),"Save the authored scene");
        foreach(var entry in protectedFiles)Need(Hash(entry.Key)==entry.Value,"Original asset unchanged: "+entry.Key);
        File.WriteAllLines(Output+"/source-assets-sha256.txt",protectedFiles.Select(p=>p.Value+"  "+p.Key));
        File.WriteAllLines(Output+"/coverage.txt",new[]{"Active and inactive authored vegetation migrated without moving placements.","instances="+instances+" batches="+batches+" renderers="+renderers+" meshes="+meshes.Count+" materials="+materials.Count+" canopies="+canopies.Count}.Concat(records));
        File.WriteAllLines(Output+"/asset-manifest.txt",AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith(Folder+"/",StringComparison.Ordinal)));
        Debug.Log("LIVING ENVIRONMENT INSTALL PASS: "+instances+" instances, "+batches+" batches, "+renderers+" renderers; source assets protected.");
    }
    static Mesh Bake(Mesh source,Matrix4x4 basis,string profile)
    {
        string key=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source))+"_"+source.GetInstanceID()+"_"+profile+"_"+basis.MultiplyVector(Vector3.up).ToString("F3");
        if(meshes.TryGetValue(key,out var cached))return cached;
        string path=Folder+"/Meshes/"+Safe(source.name)+"_"+meshes.Count+".asset";
        var mesh=Object.Instantiate(source);mesh.name=source.name+" wind metadata";
        var vertices=source.vertices;Need(vertices.Length>0,"Readable plant mesh: "+source.name);
        var points=vertices.Select(basis.MultiplyPoint3x4).ToArray();
        var lo=points.Aggregate(Vector3.Min);var hi=points.Aggregate(Vector3.Max);
        float span=Mathf.Max(.02f,hi.y-lo.y);bool hanging=profile=="hanging";
        var center=(lo+hi)*.5f;var inverse=basis.inverse;
        var root=new List<Vector4>(vertices.Length);var detail=new List<Vector4>(vertices.Length);
        var colors=source.colors32;
        if(colors.Length!=vertices.Length){colors=Enumerable.Repeat(new Color32(255,255,255,255),vertices.Length).ToArray();mesh.colors32=colors;}
        if(profile=="plant")VegetationContactRepair.PrepareMetadata(mesh,basis,profile);
        else for(int i=0;i<vertices.Length;i++)
        {
            float height=hanging?hi.y-points[i].y:points[i].y-lo.y;
            float weight=Mathf.Clamp01(height/span);
            Vector3 anchor=profile=="tree"?new Vector3(center.x,hanging?hi.y:lo.y,center.z):new Vector3(points[i].x,hanging?hi.y:lo.y,points[i].z);
            var local=inverse.MultiplyPoint3x4(anchor);
            root.Add(new Vector4(local.x,local.y,local.z,weight));
            Color c=colors[i];bool leaf=source.name.ToLowerInvariant().Contains("broadleaf")||c.g>c.r*1.07f&&c.g>c.b*1.15f||c.r>c.g+.09f&&c.g>.35f;
            float phase=Mathf.Repeat(anchor.x*.73f+anchor.z*1.13f,6.283185f);
            detail.Add(new Vector4(span,profile=="tree"?(leaf?1:.1f):1,phase,0));
        }
        if(profile!="plant"){mesh.SetUVs(2,root);mesh.SetUVs(3,detail);}
        if(mesh.normals.Length!=vertices.Length)mesh.RecalculateNormals();
        var bound=source.bounds;bound.Expand(1.8f);mesh.bounds=bound;
        AssetDatabase.CreateAsset(mesh,path);meshes.Add(key,mesh);return mesh;
    }
    static Material MaterialFor(Material source,string profile,Shader shader)
    {
        string key=AssetDatabase.GetAssetPath(source)+"|"+profile;
        if(materials.TryGetValue(key,out var cached))return cached;
        var mat=new Material(source);mat.shader=shader;mat.name=source.name+" Living "+profile;mat.enableInstancing=true;
        mat.SetFloat("_StyleMode",Mode(source));mat.SetFloat("_VertexDataValid",1);
        mat.SetFloat("_WindAmplitude",profile=="tree"?.048f:profile=="hanging"?.075f:.16f);
        mat.SetFloat("_WindDetail",profile=="tree"?.018f:.016f);
        mat.SetFloat("_InteractionStrength",profile=="tree"?0:1);
        mat.SetFloat("_Cull",source.HasProperty("_Cull")?source.GetFloat("_Cull"):0);
        mat.SetFloat("_AlphaClip",source.HasProperty("_AlphaClip")?source.GetFloat("_AlphaClip"):0);
        mat.SetFloat("_Cutoff",source.HasProperty("_Cutoff")?source.GetFloat("_Cutoff"):.5f);
        AssetDatabase.CreateAsset(mat,Folder+"/Materials/"+Safe(source.name)+"_"+profile+".mat");materials.Add(key,mat);return mat;
    }
    static void AddCanopy(Vector3 center,float radius,string name){if(radius<.1f)return;canopies.Add(new Vector4(center.x,center.y,center.z,Mathf.Clamp(radius,.5f,3)*(name.ToLowerInvariant().Contains("pink")||name.ToLowerInvariant().Contains("blossom")?-1:1)));}
    public static void PruneUnusedGenerated()
    {
        Need(Application.dataPath.StartsWith("/private/tmp/adventure-trio-knight-",StringComparison.Ordinal)&&!Application.isPlaying,"Only isolated generated assets");
        var used=new HashSet<string>(AssetDatabase.GetDependencies(ScenePath,true));
        int removed=0;
        foreach(string path in AssetDatabase.FindAssets("",new[]{Folder+"/Meshes",Folder+"/Materials"}).Select(AssetDatabase.GUIDToAssetPath))
            if((path.EndsWith(".asset")||path.EndsWith(".mat"))&&!used.Contains(path))
            {Need(AssetDatabase.DeleteAsset(path),"Delete unused isolated generated asset "+path);removed++;}
        AssetDatabase.SaveAssets();Debug.Log("LIVING ENVIRONMENT removed "+removed+" unreferenced generated trial assets");
    }
    static void BuildLeafAssets(WorldVegetationWind wind)
    {
        var mat=new Material(Shader.Find("FoldedRealm/DriftingLeaf"));AssetDatabase.CreateAsset(mat,Folder+"/Materials/DriftingLeaf.mat");wind.driftingLeafMaterial=mat;
        var mesh=new Mesh{name="Small folded drifting leaf"};mesh.vertices=new[]{new Vector3(0,0,-.5f),new Vector3(-.28f,0,0),new Vector3(0,.10f,.5f),new Vector3(.28f,0,0)};
        mesh.triangles=new[]{0,1,2,0,2,3};mesh.colors32=Enumerable.Repeat(new Color32(255,255,255,255),4).ToArray();mesh.RecalculateNormals();
        mesh.uv=new[]{new Vector2(.5f,0),new Vector2(0,.5f),new Vector2(.5f,1),new Vector2(1,.5f)};
        AssetDatabase.CreateAsset(mesh,Folder+"/Meshes/DriftingLeaf.asset");wind.driftingLeafMesh=mesh;
    }
    static string Safe(string text)=>string.Concat(text.Select(c=>char.IsLetterOrDigit(c)||c=='_'?c:'_'));
    static void Ensure(string path){if(AssetDatabase.IsValidFolder(path))return;Ensure(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
}
