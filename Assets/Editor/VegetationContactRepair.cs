using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Repairs only generated plant UV2/UV3 metadata. Mesh topology, visible vertex
// attributes, original source assets, material references and placement stay intact.
public static class VegetationContactRepair
{
    public const string Output="LocalDocs/Environment/VegetationContactFix-20261024";
    const string MeshFolder=LivingEnvironmentInstaller.Folder+"/Meshes/";
    sealed class Entry { public Mesh mesh; public Matrix4x4 basis; public Vector3 up; public float verticalScale; }
    static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}

    public static void RunAndVerify()
    {
        Run();
        LivingEnvironmentChecks.RunStatic();
        VegetationContactGpuChecks.RunStatic();
    }

    public static int PrepareMetadata(Mesh mesh,Matrix4x4 basis,string profile)
    {
        if(profile!="plant")return 0;
        // Serialized meshes may deliberately disable runtime Read/Write. Editor
        // authoring can still read their saved vertices; do not enable CPU mesh
        // copies in the player merely to repair two editor-time UV channels.
        Need(mesh,"Existing generated plant mesh");
        Need(Mathf.Abs(basis.determinant)>1e-8f,"Invertible plant basis: "+mesh.name);
        Vector3[] vertices=mesh.vertices;
        Need(vertices.Length>0,"Nonempty plant geometry: "+mesh.name);
        var points=vertices.Select(basis.MultiplyPoint3x4).ToArray();
        string name=mesh.name.ToLowerInvariant();
        bool knownFlower=(name.Contains("flower")||name.Contains("daisy"))&&
            (vertices.Length==270||vertices.Length==561);
        int groupCount=knownFlower?3:1,perGroup=vertices.Length/groupCount;
        if(knownFlower)
        {
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                Need(mesh.GetTopology(sub)==MeshTopology.Triangles,"Expected triangle flower source");
                int[] indices=mesh.GetIndices(sub);
                for(int t=0;t<indices.Length;t+=3)
                    Need(indices[t]/perGroup==indices[t+1]/perGroup&&indices[t]/perGroup==indices[t+2]/perGroup,
                        "Whole-flower vertex ranges must not split a triangle: "+mesh.name);
            }
        }
        var roots=new List<Vector4>(vertices.Length);
        var details=new List<Vector4>(vertices.Length);
        Matrix4x4 inverse=basis.inverse;
        for(int group=0;group<groupCount;group++)
        {
            int first=group*perGroup,last=group==groupCount-1?vertices.Length:first+perGroup;
            float minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
            for(int i=first;i<last;i++){minY=Mathf.Min(minY,points[i].y);maxY=Mathf.Max(maxY,points[i].y);}
            float span=Mathf.Max(.02f,maxY-minY);
            var unique=new Dictionary<Vector3Int,Vector3>();
            // One pivot shared by stem, disconnected petals, centre and leaves.
            // Deduplicate flat-shaded repeats so face tessellation cannot bias it.
            for(int i=first;i<last;i++)
            {
                Vector3 p=points[i];if(p.y>minY+.015f)continue;
                var key=new Vector3Int(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.y*100000),Mathf.RoundToInt(p.z*100000));
                if(!unique.ContainsKey(key))unique.Add(key,p);
            }
            Need(unique.Count>0,"Stem base candidates exist: "+mesh.name);
            Vector3 pivot=Vector3.zero;foreach(var p in unique.Values)pivot+=p;
            pivot/=unique.Count;pivot.y=minY;
            Vector3 local=inverse.MultiplyPoint3x4(pivot);
            // A constant botanical span vector survives rotated/nonuniformly
            // scaled instances. Reconstructing height from each petal's world Y
            // would give one flower several incompatible bend angles.
            Vector3 stemSpanLocal=inverse.MultiplyVector(Vector3.up*span);
            for(int i=first;i<last;i++)
            {
                float weight=Mathf.Clamp01((points[i].y-minY)/span);
                roots.Add(new Vector4(local.x,local.y,local.z,weight));
                details.Add(new Vector4(stemSpanLocal.x,stemSpanLocal.y,stemSpanLocal.z,1));
            }
        }
        mesh.SetUVs(2,roots);mesh.SetUVs(3,details);
        return groupCount;
    }

    static string Profile(Mesh mesh)
    {
        string n=mesh.name.ToLowerInvariant();
        if(n.Contains("hanging")||n.Contains("trailing"))return "hanging";
        if(n.Contains("tree")||n.Contains("broadleaf")||n.Contains("cypress")||n.Contains("blossom"))return "tree";
        return "plant";
    }

    [MenuItem("Tools/Living Environment/Repair Coherent Plant Contact In Isolated Project")]
    public static void Run()
    {
        Need(Application.dataPath.StartsWith("/private/tmp/adventure-trio-knight-",StringComparison.Ordinal)&&!Application.isPlaying,
            "Use closed, isolated authoring project");
        for(int i=0;i<SceneManager.sceneCount;i++)Need(!SceneManager.GetSceneAt(i).isDirty,"Save scenes before authoring");
        string scenePath=LivingEnvironmentInstaller.ScenePath;
        string beforeScene=File.ReadAllText(scenePath);
        var scene=EditorSceneManager.OpenScene(scenePath);
        var targets=new Dictionary<string,Entry>();
        foreach(var meadow in Object.FindObjectsByType<OwnInstancedMeadow>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(meadow.gameObject.scene!=scene)continue;
            foreach(var batch in meadow.batches)
            {
                if(!batch.material||!batch.material.shader||batch.material.shader.name!="FoldedRealm/LivingVegetation")continue;
                Add(targets,batch.mesh,batch.meshToRoot);
                Add(targets,batch.farMesh,batch.meshToRoot);
            }
        }
        foreach(var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(filter.gameObject.scene!=scene)continue;
            var renderer=filter.GetComponent<MeshRenderer>();
            if(!renderer||!renderer.sharedMaterials.Any(m=>m&&m.shader&&m.shader.name=="FoldedRealm/LivingVegetation"))continue;
            Add(targets,filter.sharedMesh,Matrix4x4.Rotate(filter.transform.rotation));
        }
        Need(targets.Count>5,"Existing generated plant targets discovered");
        var protectedFiles=Directory.GetFiles("Assets","*",SearchOption.AllDirectories)
            .Where(p=>p!=scenePath&&!targets.ContainsKey(p)).ToDictionary(p=>p,Hash);
        var manifest=new List<string>{"Metadata repair only: UV2 shared pivot/weight; coherent UV3 xyz = shared mesh-local botanical span vector, w = 1.",
            "Known flowers have 3 complete groups of 90 or 187 vertices; no source triangle crosses a group.",
            "Other plant meshes use one coherent rooted clump; trees and hanging vines are untouched."};
        int groups=0;
        foreach(var pair in targets.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            var mesh=pair.Value.mesh;string geometryBefore=GeometryHash(mesh);
            string guid=AssetDatabase.AssetPathToGUID(pair.Key);
            int count=PrepareMetadata(mesh,pair.Value.basis,"plant");
            Need(GeometryHash(mesh)==geometryBefore,"Visible geometry and all non-wind attributes unchanged: "+pair.Key);
            Need(AssetDatabase.AssetPathToGUID(pair.Key)==guid,"Generated mesh GUID preserved");
            groups+=count;EditorUtility.SetDirty(mesh);
            var roots=new List<Vector4>();var details=new List<Vector4>();mesh.GetUVs(2,roots);mesh.GetUVs(3,details);
            manifest.Add(pair.Key+" | GUID="+guid+" | vertices="+mesh.vertexCount+" | groups="+count+" | geometrySHA="+geometryBefore);
            for(int group=0;group<count;group++)
            {
                int index=group*(mesh.vertexCount/count);var root=roots[index];
                var spanVector=new Vector3(details[index].x,details[index].y,details[index].z);
                manifest.Add("  ROOT "+group+" meshLocal="+new Vector3(root.x,root.y,root.z).ToString("F6")+
                    " stemSpanOS="+spanVector.ToString("F6")+
                    " uprightSpan="+pair.Value.basis.MultiplyVector(spanVector).magnitude.ToString("F6")+" coherent="+details[index].w);
            }
        }
        var world=Object.FindFirstObjectByType<BaselineWorld>();
        var wind=world?world.GetComponent<WorldVegetationWind>():null;Need(wind,"Authored wind host exists");
        wind.interactionRadius=.72f;wind.trailRecoverySeconds=.65f;EditorUtility.SetDirty(wind);
        AssetDatabase.SaveAssets();Need(EditorSceneManager.SaveScene(scene),"Save only authored contact settings");
        foreach(var entry in protectedFiles)Need(Hash(entry.Key)==entry.Value,"Protected source unchanged: "+entry.Key);
        ValidateSceneDelta(beforeScene,File.ReadAllText(scenePath));
        Directory.CreateDirectory(Output);
        File.WriteAllLines(Output+"/root-group-manifest.txt",manifest);
        File.WriteAllLines(Output+"/protected-assets-sha256.txt",protectedFiles.Select(p=>p.Value+"  "+p.Key));
        string report="VEGETATION CONTACT REPAIR PASS\nmeshes="+targets.Count+" groups="+groups+
            "\nOnly generated UV2/UV3 changed; original geometry, GUIDs, materials, placements, tree and hanging metadata preserved."+
            "\nHost contact radius=.72 recovery=.65; every other existing scene block identical.\nProtected assets="+protectedFiles.Count+"\n";
        File.WriteAllText(Output+"/installation.txt",report);Debug.Log(report);
    }

    static void Add(Dictionary<string,Entry> targets,Mesh mesh,Matrix4x4 basis)
    {
        if(!mesh||Profile(mesh)!="plant")return;
        string path=AssetDatabase.GetAssetPath(mesh);
        if(!path.StartsWith(MeshFolder,StringComparison.Ordinal))return;
        Vector3 axis=basis.transpose.MultiplyVector(Vector3.up);float scale=axis.magnitude;
        Need(scale>1e-6f,"Valid upright plant axis");axis/=scale;
        if(targets.TryGetValue(path,out var old))
        {
            Need(Vector3.Dot(axis,old.up)>.99999f&&Mathf.Abs(scale-old.verticalScale)<.0001f,
                "A shared generated plant must have a matching normalized upright basis: "+path);
            return;
        }
        targets.Add(path,new Entry{mesh=mesh,basis=basis,up=axis,verticalScale=scale});
    }

    public static string GeometryHash(Mesh mesh)
    {
        // Wind UV channels 2/3 intentionally excluded; all rendered source
        // topology, normals, colours, UV0/1 and all other attributes are covered.
        using(var bytes=new MemoryStream())using(var w=new BinaryWriter(bytes))
        {
            foreach(Vector3 v in mesh.vertices){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
            foreach(Vector3 v in mesh.normals){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
            foreach(Vector4 v in mesh.tangents){w.Write(v.x);w.Write(v.y);w.Write(v.z);w.Write(v.w);}
            foreach(Color32 c in mesh.colors32){w.Write(c.r);w.Write(c.g);w.Write(c.b);w.Write(c.a);}
            for(int channel=0;channel<8;channel++)
            {
                if(channel==2||channel==3)continue;
                var values=new List<Vector4>();mesh.GetUVs(channel,values);w.Write(values.Count);
                foreach(var v in values){w.Write(v.x);w.Write(v.y);w.Write(v.z);w.Write(v.w);}
            }
            w.Write(mesh.subMeshCount);
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {w.Write((int)mesh.GetTopology(sub));foreach(int index in mesh.GetIndices(sub))w.Write(index);}
            var b=mesh.bounds;w.Write(b.center.x);w.Write(b.center.y);w.Write(b.center.z);w.Write(b.size.x);w.Write(b.size.y);w.Write(b.size.z);
            w.Flush();using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();
        }
    }
    static string Hash(string path)
    {using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}

    static void ValidateSceneDelta(string before,string after)
    {
        Func<string,Dictionary<string,string>> blocks=text=>Regex.Split(text,@"(?=^--- !u!)",RegexOptions.Multiline)
            .Skip(1).ToDictionary(b=>b.Substring(0,b.IndexOf('\n')),b=>b);
        var a=blocks(before);var b=blocks(after);Need(a.Count==b.Count,"No scene objects added or removed");
        foreach(var pair in a)
        {
            Need(b.TryGetValue(pair.Key,out string next),"Scene object identity preserved");
            string old=pair.Value;
            if(old.Contains("Assembly-CSharp::WorldVegetationWind"))
            {
                old=Regex.Replace(old,@"(?m)^  (interactionRadius|trailRecoverySeconds):.*$","  $1: CONTACT_SETTING");
                next=Regex.Replace(next,@"(?m)^  (interactionRadius|trailRecoverySeconds):.*$","  $1: CONTACT_SETTING");
            }
            Need(old==next,"Only contact radius/recovery scene settings changed: "+pair.Key);
        }
    }
}
