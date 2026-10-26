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

public static class WorldLifeInstaller
{
    public const string ScenePath="Assets/_Demo/Scenes/OverworldBaseline.unity";
    public const string Folder="Assets/Art/LivingWorldLife";
    public const string Output="LocalDocs/Environment/WorldLife-20261024";
    [MenuItem("Tools/Living World/Install Nature And Wildlife In Isolated Project")]
    public static void Install()
    {
        Need(Application.dataPath.StartsWith("/private/tmp/adventure-trio-knight-",StringComparison.Ordinal)&&!Application.isPlaying,"Isolated authoring project only");
        for(int s=0;s<SceneManager.sceneCount;s++)Need(!SceneManager.GetSceneAt(s).isDirty,"Save scenes before authoring");
        var scene=EditorSceneManager.OpenScene(ScenePath);
        var world=Object.FindFirstObjectByType<BaselineWorld>();var wind=world.GetComponent<WorldVegetationWind>();
        Need(world&&wind,"Existing overworld and wind host are present");
        Need(!world.GetComponent<WorldSmallWildlife>()&&!world.GetComponent<RegionalNatureAudio>(),"Do not overwrite an installed life layer");
        Directory.CreateDirectory(Output);Ensure(Folder+"/Visuals");
        var allSources=Directory.GetFiles("Assets","*",SearchOption.AllDirectories)
            .Where(p=>!p.StartsWith(Folder+"/",StringComparison.Ordinal)&&p!=ScenePath&&!p.EndsWith(".cs")&&!p.EndsWith(".cs.meta"))
            .ToDictionary(p=>p,Hash);
        world.RefreshNavigation();Physics.SyncTransforms();
        var water=Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None).Where(v=>v.IsValid)
            .OrderByDescending(v=>v.VolumeCollider.bounds.size.x*v.VolumeCollider.bounds.size.z).FirstOrDefault();
        Need(water,"Actual lake swimming volume exists");
        var waterBounds=water.VolumeCollider.bounds;
        Vector3 lake=new Vector3(waterBounds.center.x,water.WaterSurfaceY,waterBounds.center.z);
        var flowers=new List<Vector3>();
        foreach(var meadow in Object.FindObjectsByType<OwnInstancedMeadow>(FindObjectsSortMode.None))
            foreach(var batch in meadow.batches)
                if(batch.mesh&&(batch.mesh.name.ToLowerInvariant().Contains("flower")||batch.mesh.name.ToLowerInvariant().Contains("daisy")))
                    flowers.AddRange(batch.positions);
        Need(flowers.Count>100,"Use actual authored flowers for wildlife anchors");
        var wildlife=world.gameObject.AddComponent<WorldSmallWildlife>();
        var butterflies=new List<Vector3>();var birds=new List<Vector3>();
        AddFlowerAnchors(flowers,world.player.position,4,butterflies,3);
        AddFlowerAnchors(flowers,lake,4,butterflies,5);
        AddBirdAnchors(world,flowers,world.player.position,3,birds);
        AddBirdAnchors(world,flowers,lake,3,birds);
        Need(birds.Count>=4&&butterflies.Count>=6,"Dry reachable wildlife pockets selected");
        wildlife.butterflyAnchors=butterflies.Take(8).Select(p=>p+Vector3.up*.36f).ToArray();wildlife.birdAnchors=birds.Take(6).ToArray();
        var wingShader=Shader.Find("FoldedRealm/WildlifeWing");Need(wingShader&&wingShader.isSupported,"Wildlife shader supported");
        wildlife.birdMaterial=new Material(wingShader){name="Small woodland bird",enableInstancing=true};
        wildlife.butterflyMaterial=new Material(wingShader){name="Meadow butterfly",enableInstancing=true};
        AssetDatabase.CreateAsset(wildlife.birdMaterial,Folder+"/Visuals/Bird.mat");
        AssetDatabase.CreateAsset(wildlife.butterflyMaterial,Folder+"/Visuals/Butterfly.mat");
        wildlife.birdBodyMesh=WorldSmallWildlife.CreateBirdBodyMesh();
        wildlife.birdWingMesh=WorldSmallWildlife.CreateBirdWingMesh();
        wildlife.butterflyMesh=WorldSmallWildlife.CreateButterflyMesh();
        AssetDatabase.CreateAsset(wildlife.birdBodyMesh,Folder+"/Visuals/BirdBody.asset");
        AssetDatabase.CreateAsset(wildlife.birdWingMesh,Folder+"/Visuals/BirdWing.asset");
        AssetDatabase.CreateAsset(wildlife.butterflyMesh,Folder+"/Visuals/Butterfly.asset");
        var audio=world.gameObject.AddComponent<RegionalNatureAudio>();
        audio.waterAnchors=WaterAnchors(water,lake);
        audio.treeAnchors=wind.canopySources.Select(v=>new Vector3(v.x,v.y,v.z)).ToArray();
        var clips=AssetDatabase.FindAssets("t:AudioClip",new[]{Folder}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        foreach(var path in clips)
        {
            var importer=AssetImporter.GetAtPath(path) as AudioImporter;if(!importer)continue;
            bool loop=path.Contains("LakeShore")||path.Contains("CanopyBreeze");
            importer.forceToMono=true;importer.loadInBackground=loop;
            var settings=importer.defaultSampleSettings;settings.loadType=loop?AudioClipLoadType.Streaming:AudioClipLoadType.CompressedInMemory;
            settings.preloadAudioData=!loop;settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.7f;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
        }
        audio.waterLoop=LoadClip(clips,"lakeshore");audio.foliageLoop=LoadClip(clips,"canopybreeze");
        audio.birdCalls=clips.Where(p=>p.ToLowerInvariant().Contains("bird")).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
        Need(audio.waterLoop&&audio.foliageLoop&&audio.birdCalls.Length>=1,"Licensed nature clips assigned");
        EditorUtility.SetDirty(audio);EditorUtility.SetDirty(wildlife);
        AssetDatabase.SaveAssets();Need(EditorSceneManager.SaveScene(scene),"Save installed life components");
        foreach(var entry in allSources)Need(Hash(entry.Key)==entry.Value,"Existing source asset preserved: "+entry.Key);
        File.WriteAllLines(Output+"/source-assets-sha256.txt",allSources.Select(p=>p.Value+"  "+p.Key));
        File.WriteAllLines(Output+"/anchors.txt",new[]{"Lake center "+lake.ToString("F3"),"birds="+wildlife.birdAnchors.Length+" butterflies="+wildlife.butterflyAnchors.Length+" audio clips="+clips.Length}
            .Concat(birds.Select(v=>"BIRD "+v.ToString("F3"))).Concat(butterflies.Select(v=>"BUTTERFLY "+v.ToString("F3"))).Concat(audio.waterAnchors.Select(v=>"WATER "+v.ToString("F3"))));
        Debug.Log("WORLD LIFE INSTALL PASS; two host-owned components, authored dry anchors and licensed nature audio; original resource hashes preserved.");
    }
    static AudioClip LoadClip(string[] paths,string word)
    {
        var path=paths.FirstOrDefault(p=>p.ToLowerInvariant().Contains(word));return path==null?null:AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
    static void AddFlowerAnchors(List<Vector3> candidates,Vector3 near,int count,List<Vector3> result,float spacing)
    {
        int added=0;foreach(var p in candidates.OrderBy(v=>(v-near).sqrMagnitude))
        {if(result.Any(v=>Vector3.Distance(v,p)<spacing))continue;result.Add(p);if(++added>=count)return;}
    }
    static void AddBirdAnchors(BaselineWorld world,List<Vector3> candidates,Vector3 near,int count,List<Vector3> result)
    {
        var enemies=Object.FindObjectsByType<AdventureCreatureAI>(FindObjectsSortMode.None);
        var solids=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&r.bounds.size.y>.7f&&r.bounds.size.y<20&&r.bounds.size.x<12&&r.bounds.size.z<12&&
            !r.transform.IsChildOf(world.player)&&!r.sharedMaterials.Any(m=>m&&m.shader&&m.shader.name=="FoldedRealm/LivingVegetation")).Select(r=>r.bounds).ToArray();
        int added=0;
        foreach(var candidate in candidates.OrderBy(p=>(p-near).sqrMagnitude))
        {
            Vector3 query=candidate+new Vector3(.8f,0,.8f);
            if(!world.NavigationGround(query,out var ground)||Mathf.Abs(ground.y-candidate.y)>.5f)continue;
            if(result.Any(p=>Vector3.Distance(p,ground)<4)||enemies.Any(e=>Vector3.Distance(e.transform.position,ground)<11))continue;
            Vector3 body=ground+Vector3.up*.5f;
            if(solids.Any(b=>(b.ClosestPoint(body)-body).sqrMagnitude<.8f*.8f))continue;
            if(Physics.OverlapSphere(ground+Vector3.up*.8f,.65f,~0,QueryTriggerInteraction.Ignore).Any(c=>c&&!c.transform.IsChildOf(world.player)&&c.bounds.max.y>ground.y+.5f))continue;
            result.Add(ground+Vector3.up*.025f);if(++added>=count)return;
        }
    }
    static Vector3[] WaterAnchors(WaterVolume volume,Vector3 center)
    {
        var property=new SerializedObject(volume).FindProperty("surfaceFootprint");var filter=property.objectReferenceValue as MeshFilter;
        if(filter&&filter.sharedMesh)
        {
            var points=filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
            return Enumerable.Range(0,12).Select(i=>
            {
                var d=new Vector3(Mathf.Cos(i*Mathf.PI/6),0,Mathf.Sin(i*Mathf.PI/6));
                var p=points.OrderByDescending(v=>Vector3.Dot(v-center,d)).First();return new Vector3(p.x,volume.WaterSurfaceY,p.z);
            }).Distinct().ToArray();
        }
        var b=volume.VolumeCollider.bounds;
        return Enumerable.Range(0,12).Select(i=>center+new Vector3(Mathf.Cos(i*Mathf.PI/6)*b.extents.x,0,Mathf.Sin(i*Mathf.PI/6)*b.extents.z)).ToArray();
    }
    static string Hash(string path){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static void Ensure(string path){if(AssetDatabase.IsValidFolder(path))return;Ensure(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
    static void Need(bool success,string message){if(!success)throw new InvalidOperationException(message);}
}
