using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Explicit, bounded quality pass. Never runs on import and never rewrites source pixels.
public static class WebGLQualityPass
{
    const string Root = "Assets/Art/V2Candidates/OverworldCollection/CollectedSources/Pack2/PureNature/Models/Rocks/Cliffs/Textures/";
    // Both cliff meshes now share this surface; the redundant Cliff02 images are archived.
    public static readonly string[] Textures = { Root+"Cliff01_a.png", Root+"Cliff01_n.png" };
    const string Evidence = "Docs/SideGameBQuality/";

    public static string Capture(string phase)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty ||
            SceneManager.GetActiveScene().path != "Assets/_Demo/Scenes/OverworldBaseline.unity")
            throw new InvalidOperationException("Capture requires the clean main scene in Edit mode.");
        if (phase != "before" && phase != "after") throw new ArgumentException("Use before or after.");
        Directory.CreateDirectory(Evidence);
        var source = Camera.main;
        if (!source) throw new InvalidOperationException("Main camera missing.");
        var go = new GameObject("Temporary texture comparison camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
        var sourceData = source.GetComponent<UniversalAdditionalCameraData>();
        if (sourceData) EditorUtility.CopySerialized(sourceData, go.AddComponent<UniversalAdditionalCameraData>());
        var rt = RenderTexture.GetTemporary(1600, 1000, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.fieldOfView = 60; camera.farClipPlane = 900;
            Vector3[] positions = { new Vector3(0,6.45f,-82), new Vector3(31,16,-4.5f), new Vector3(29,16,-6) };
            Vector3[] targets = { new Vector3(0,9,-43), new Vector3(32,12.5f,1.5f), new Vector3(34,13,10) };
            string[] names = { "spawn", "cliff-close", "forest-shelf" };
            for (int i=0;i<positions.Length;i++)
            {
                camera.transform.SetPositionAndRotation(positions[i], Quaternion.LookRotation(targets[i]-positions[i]));
                camera.Render(); RenderTexture.active=rt;
                pixels.ReadPixels(new Rect(0,0,1600,1000),0,0); pixels.Apply();
                File.WriteAllBytes(Evidence+phase+"-"+names[i]+".png", pixels.EncodeToPNG());
            }
            File.WriteAllText(Evidence+phase+"-textures.txt", Describe()+"\nSOURCE: Unity game-camera offscreen render in Edit mode; identical fixed poses, FOV60, 1600x1000. No Scene View or continuous player-input claim.\n");
        }
        finally
        {
            camera.targetTexture=null; RenderTexture.active=old; RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(go);
        }
        return Describe();
    }

    public static string ApplyCliffs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            throw new InvalidOperationException("Requires WebGL target in Edit mode.");
        foreach (var path in Textures)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing cliff texture: "+path);
            var settings = importer.GetPlatformTextureSettings("WebGL");
            settings.name="WebGL"; settings.overridden=true; settings.maxTextureSize=2048;
            settings.format=TextureImporterFormat.Automatic; settings.textureCompression=TextureImporterCompression.Compressed;
            settings.compressionQuality=100; settings.crunchedCompression=false;
            importer.SetPlatformTextureSettings(settings); importer.SaveAndReimport();
        }
        return Describe();
    }

    public static string Describe() => string.Join("\n", Textures.Select(path =>
    {
        var t=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        var platform=importer.GetPlatformTextureSettings("WebGL");
        return path+" | "+t.width+"x"+t.height+" "+t.format+" mips="+t.mipmapCount+
            " WebGLOverride="+platform.overridden+" max="+platform.maxTextureSize+" crunch="+platform.crunchedCompression;
    }));

    public static void QueueBuild(string phase)
    {
        if (phase != "native-before" && phase != "native-optimized") throw new ArgumentException("Unknown build phase.");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Build requires stable clean Edit mode.");
        if (PlayerSettings.WebGL.compressionFormat != WebGLCompressionFormat.Gzip)
            throw new InvalidOperationException("This quality pass must retain Gzip.");
        string evidence="ToolReports/SideGameBQuality/"+phase;
        Directory.CreateDirectory(evidence);
        File.WriteAllText(evidence+"/status.txt","Scheduled");
        EditorApplication.delayCall += () =>
        {
            try
            {
                File.WriteAllText(evidence+"/status.txt","Building");
                string output="Builds/OverworldNativeB-20260928-"+phase;
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes=new[]{"Assets/_Demo/Scenes/OverworldBaseline.unity",SideWorldTravel.BattlePath,SideWorldTravel.SideGameBPath},
                    target=BuildTarget.WebGL, locationPathName=output, options=BuildOptions.DetailedBuildReport
                });
                var summary=report.summary;
                File.WriteAllText(evidence+"/summary.txt",summary.result+"\nErrors="+summary.totalErrors+" Warnings="+summary.totalWarnings+
                    "\nBytes="+summary.totalSize+"\nDuration="+summary.totalTime+"\nOutput="+output+"\nUTC="+DateTime.UtcNow.ToString("O"));
                File.WriteAllLines(evidence+"/packed-assets.tsv",report.packedAssets.SelectMany(p=>p.contents)
                    .GroupBy(a=>a.sourceAssetPath).Select(g=>g.Key+"\t"+g.Sum(a=>(long)a.packedSize)));
                File.WriteAllLines(evidence+"/messages.txt",report.steps.SelectMany(s=>s.messages).Select(m=>m.type+" "+m.content));
                File.WriteAllText(evidence+"/status.txt",summary.result.ToString());
            }
            catch(Exception e) { File.WriteAllText(evidence+"/status.txt","Failed: "+e); Debug.LogException(e); }
        };
    }
}
