using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Explicit source-asset pass. No import hooks or automatic destructive operations.
public static class AssetReuse130Pass
{
    public const string Evidence = "ToolReports/AssetsReuse130";
    public const string Screens = "Docs/AssetsReuse130";
    public const string Backup = "SourceArchives/AssetReuse130-20260928";
    public static readonly string[] Scenes = { "Assets/_Demo/Scenes/OverworldBaseline.unity", "Assets/Scenes/BattleScene.unity", "Assets/SideGameB/Game/Scenes/SideGameB.unity", "Assets/StartScene.unity" };
    [MenuItem("Tools/Asset Budget/Check 130 MB Working Budget")]
    public static void CheckBudget()
    {
        long total=Directory.GetFiles("Assets","*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);
        long b=Directory.GetFiles("Assets/SideGameB","*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);
        string message="Assets="+total+" bytes; B="+b+" bytes. Decimal MB, includes .meta; excludes Library/builds/archives.";
        if(total>130000000 || b>20000000)throw new InvalidOperationException(message+" Working budget exceeded.");
        Debug.Log(message);
    }
    // Command-line entry for a disposable project copied without Library, so cached imports cannot mask missing dependencies.
    public static void BuildCleanWebGL()
    {
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-reuseOutput");
        if(at<0 || at+1>=args.Length)throw new ArgumentException("-reuseOutput absolute-directory required");
        string output=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(Evidence);
        try
        {
            CheckBudget();
            if(PlayerSettings.WebGL.compressionFormat!=WebGLCompressionFormat.Gzip)throw new InvalidOperationException("Keep existing Gzip settings.");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=Scenes.Take(3).ToArray(),target=BuildTarget.WebGL,locationPathName=output,options=BuildOptions.DetailedBuildReport});
            var s=report.summary;
            File.WriteAllText(Evidence+"/clean-build-summary.txt",s.result+"\nErrors="+s.totalErrors+" Warnings="+s.totalWarnings+"\nBytes="+s.totalSize+"\nDuration="+s.totalTime+"\nOutput="+output);
            File.WriteAllLines(Evidence+"/clean-build-messages.txt",report.steps.SelectMany(x=>x.messages).Select(m=>m.type+" "+m.content));
            File.WriteAllLines(Evidence+"/clean-packed-assets.tsv",report.packedAssets.SelectMany(p=>p.contents).GroupBy(x=>x.sourceAssetPath).Select(g=>g.Key+"\t"+g.Sum(x=>(long)x.packedSize)));
            if(s.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)EditorApplication.Exit(1);
        }
        catch(Exception e){File.WriteAllText(Evidence+"/clean-build-failure.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Guard()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Requires stable Edit mode.");
        for(int i=0;i<SceneManager.sceneCount;i++) if(SceneManager.GetSceneAt(i).isDirty)
            throw new InvalidOperationException("Unsaved scene: "+SceneManager.GetSceneAt(i).path);
        Directory.CreateDirectory(Evidence); Directory.CreateDirectory(Screens);
    }
    public static string Capture(string phase)
    {
        Guard();
        if(phase!="before" && phase!="after")throw new ArgumentException("phase");
        string original=SceneManager.GetActiveScene().path;
        try
        {
            if(original!=Scenes[0])EditorSceneManager.OpenScene(Scenes[0]);
            Vector3[] p={new Vector3(0,6.45f,-82),new Vector3(31,16,-4.5f),new Vector3(29,16,-6),new Vector3(13,6,-24),new Vector3(45,30,47)};
            Vector3[] t={new Vector3(0,9,-43),new Vector3(32,12.5f,1.5f),new Vector3(34,13,10),new Vector3(-15,3,2),new Vector3(54,34,63)};
            string[] n={"spawn","cliff","forest","lake","tower"};
            for(int i=0;i<p.Length;i++)OverworldShowcaseApply.Render(Screens+"/"+phase+"-"+n[i]+".png",p[i],t[i]);
            EditorSceneManager.OpenScene(Scenes[2]);
            var camera=Camera.main;
            if(camera)
            {
                var old=RenderTexture.active; var rt=RenderTexture.GetTemporary(1280,720,24);
                var target=camera.targetTexture; var pix=new Texture2D(1280,720,TextureFormat.RGB24,false);
                try{camera.targetTexture=rt; camera.Render();RenderTexture.active=rt;pix.ReadPixels(new Rect(0,0,1280,720),0,0);pix.Apply();File.WriteAllBytes(Screens+"/"+phase+"-b-scene.png",pix.EncodeToPNG());}
                finally{camera.targetTexture=target;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(pix);}
            }
            File.WriteAllText(Screens+"/"+phase+"-source.txt","Unity game-camera offscreen rendering in Edit mode; fixed transforms; not Game View or manual play.\n");
        }
        finally{EditorSceneManager.OpenScene(original);}
        return "Captured "+phase;
    }
    public static string Audit(string phase)
    {
        Guard();string original=SceneManager.GetActiveScene().path;
        var colliders=new List<string>();var refs=new List<string>();var sprites=new List<string>();var identities=new List<string>();
        try
        {
            foreach(string path in Scenes)
            {
                var s=EditorSceneManager.OpenScene(path);
                foreach(var root in s.GetRootGameObjects())foreach(var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    string id=GlobalObjectId.GetGlobalObjectIdSlow(tr.gameObject).ToString();
                    foreach(var c in tr.GetComponents<Component>())
                    {
                        if(!c){refs.Add(path+"|"+id+"|MissingScript");continue;}
                        if(c is Collider || c is Collider2D)colliders.Add(path+"|"+GlobalObjectId.GetGlobalObjectIdSlow(c)+"|"+tr.position.ToString("F6")+"|"+tr.lossyScale.ToString("F6")+"|"+EditorJsonUtility.ToJson(c));
                        if(c is SpriteRenderer sr && sr.sprite)
                        {AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sr.sprite,out string guid,out long fid);sprites.Add(path+"|"+id+"|"+guid+"|"+fid+"|"+sr.bounds.center.ToString("F5")+"|"+sr.bounds.size.ToString("F5"));}
                        if(c is BaselineInteraction bi)identities.Add(path+"|"+GlobalObjectId.GetGlobalObjectIdSlow(c)+"|"+bi.action+"|"+bi.index);
                        var so=new SerializedObject(c);var it=so.GetIterator();
                        while(it.NextVisible(true))if(it.propertyType==SerializedPropertyType.ObjectReference && it.objectReferenceValue==null && it.objectReferenceInstanceIDValue!=0)refs.Add(path+"|"+id+"|"+c.GetType().Name+"|"+it.propertyPath);
                    }
                }
            }
        }
        finally{EditorSceneManager.OpenScene(original);}
        File.WriteAllLines(Evidence+"/"+phase+"-colliders.txt",colliders.OrderBy(x=>x));
        File.WriteAllLines(Evidence+"/"+phase+"-sprites.txt",sprites.OrderBy(x=>x));
        File.WriteAllLines(Evidence+"/"+phase+"-identities.txt",identities.OrderBy(x=>x));
        File.WriteAllLines(Evidence+"/"+phase+"-missing.txt",refs);
        return "Colliders="+colliders.Count+" sprites="+sprites.Count+" identities="+identities.Count+" missing="+refs.Count;
    }
    public static string ExportTextureInventory()
    {
        Guard();var lines=new List<string>{"path\tbytes\twidth\theight\ttype\tspriteMode\tppu"};
        foreach(var p in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/")&&File.Exists(p)&&new FileInfo(p).Length>150000))
        {
            var imp=AssetImporter.GetAtPath(p) as TextureImporter;if(imp==null)continue;
            imp.GetSourceTextureWidthAndHeight(out int w,out int h);
            lines.Add(p+"\t"+new FileInfo(p).Length+"\t"+w+"\t"+h+"\t"+imp.textureType+"\t"+imp.spriteImportMode+"\t"+imp.spritePixelsPerUnit);
        }
        File.WriteAllLines(Evidence+"/texture-inventory.tsv",lines);return "Textures="+(lines.Count-1);
    }
    public static string ShareCliffSurface()
    {
        Guard();const string root="Assets/Art/V2Candidates/OverworldCollection/";
        const string tex=root+"CollectedSources/Pack2/PureNature/Models/Rocks/Cliffs/Textures/";
        string path=root+"OverworldCollection/Materials/ead2f807086d68d42a0c1f85f28af90e_Mesh.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        var albedo=AssetDatabase.LoadAssetAtPath<Texture2D>(tex+"Cliff01_a.png");
        var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(tex+"Cliff01_n.png");
        if(!material||!albedo||!normal)throw new Exception("Cliff resource missing");
        material.SetTexture("_BaseMap",albedo);material.SetTexture("_MainTex",albedo);material.SetTexture("_BumpMap",normal);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        return "Cliff02 mesh material now reuses Cliff01 surface; geometry unchanged.";
    }
    public static string ExportTowerPng()
    {
        Guard();const string source="Assets/Art/V2Candidates/UltraTower/Lowpoly Style/Shared Materials and Textures/Main.psd";
        string target=Path.ChangeExtension(source,"png");
        if(File.Exists(target))return "Tower PNG already exported.";
        var imp=(TextureImporter)AssetImporter.GetAtPath(source);string meta=File.ReadAllText(source+".meta");
        try
        {
            imp.isReadable=true;imp.textureCompression=TextureImporterCompression.Uncompressed;
            var web=imp.GetPlatformTextureSettings("WebGL");web.name="WebGL";web.overridden=true;web.format=TextureImporterFormat.RGBA32;web.maxTextureSize=2048;web.crunchedCompression=false;imp.SetPlatformTextureSettings(web);imp.SaveAndReimport();
            var imported=AssetDatabase.LoadAssetAtPath<Texture2D>(source);
            var copy=new Texture2D(imported.width,imported.height,TextureFormat.RGBA32,false);
            byte[] png;
            try{copy.SetPixels32(imported.GetPixels32());copy.Apply();png=copy.EncodeToPNG();}
            finally{UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.StartAssetEditing();
            try{string error=AssetDatabase.MoveAsset(source,target);if(!string.IsNullOrEmpty(error))throw new Exception(error);File.WriteAllBytes(target,png);File.WriteAllText(target+".meta",meta);}
            finally{AssetDatabase.StopAssetEditing();}
            AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceUpdate);
            return "Tower exported with existing GUID: "+png.Length+" bytes";
        }
        catch{if(File.Exists(source)){File.WriteAllText(source+".meta",meta);AssetDatabase.ImportAsset(source,ImportAssetOptions.ForceUpdate);}throw;}
    }
    // Manifest rows: asset path TAB integer divisor. Source PNG remains PNG, GUID unchanged.
    public static string ResizeManifest(string manifest)
    {
        Guard();var results=new List<string>();
        foreach(var line in File.ReadAllLines(manifest))
        {
            if(string.IsNullOrWhiteSpace(line)||line.StartsWith("#"))continue;
            var a=line.Split('\t');string p=a[0];int divisor=int.Parse(a[1]);
            if(!p.StartsWith("Assets/")||p.Contains("..")||!p.EndsWith(".png")||divisor<2)throw new ArgumentException(p);
            if(!File.Exists(Backup+"/"+p)||!File.Exists(Backup+"/"+p+".meta"))throw new InvalidOperationException("Backup missing: "+p);
            var imp=AssetImporter.GetAtPath(p) as TextureImporter;
            if(imp==null||imp.spriteImportMode==SpriteImportMode.Multiple)throw new InvalidOperationException("Unsupported importer: "+p);
            // Always derive from the preserved original so an interrupted batch is safe to repeat.
            byte[] source=File.ReadAllBytes(Backup+"/"+p);var raw=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            Texture2D output=null;
            try
            {
                if(!ImageConversion.LoadImage(raw,source,false))throw new Exception("Decode failed: "+p);
                int w=raw.width,h=raw.height,nw=(w+divisor-1)/divisor,nh=(h+divisor-1)/divisor;
                var src=raw.GetPixels32();var dst=new Color32[nw*nh];bool normal=imp.textureType==TextureImporterType.NormalMap;
                bool gamma=imp.sRGBTexture&&!normal;float[] linear=new float[256];for(int k=0;k<256;k++)linear[k]=Mathf.GammaToLinearSpace(k/255f);
                for(int y=0;y<nh;y++)for(int x=0;x<nw;x++)
                {
                    float rr=0,gg=0,bb=0,aa=0,weight=0;int count=0;
                    for(int yy=y*divisor;yy<Math.Min(h,(y+1)*divisor);yy++)for(int xx=x*divisor;xx<Math.Min(w,(x+1)*divisor);xx++)
                    {
                        var c=src[yy*w+xx];float cw=normal?1f:c.a/255f;weight+=cw;aa+=c.a;count++;
                        rr+=(gamma?linear[c.r]:c.r/255f)*cw;gg+=(gamma?linear[c.g]:c.g/255f)*cw;bb+=(gamma?linear[c.b]:c.b/255f)*cw;
                    }
                    if(weight>0){rr/=weight;gg/=weight;bb/=weight;}
                    if(normal){var n=new Vector3(rr*2-1,gg*2-1,bb*2-1).normalized;rr=n.x*.5f+.5f;gg=n.y*.5f+.5f;bb=n.z*.5f+.5f;}
                    if(gamma){rr=Mathf.LinearToGammaSpace(rr);gg=Mathf.LinearToGammaSpace(gg);bb=Mathf.LinearToGammaSpace(bb);}
                    dst[y*nw+x]=new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(rr*255),0,255),(byte)Mathf.Clamp(Mathf.RoundToInt(gg*255),0,255),(byte)Mathf.Clamp(Mathf.RoundToInt(bb*255),0,255),(byte)Mathf.RoundToInt(aa/count));
                }
                output=new Texture2D(nw,nh,TextureFormat.RGBA32,false,true);output.SetPixels32(dst);output.Apply();byte[] encoded=output.EncodeToPNG();
                if(encoded.Length>=source.Length){results.Add(p+"\tSKIP_NO_SAVING");continue;}
                var pm=System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Backup+"/"+p+".meta"),@"spritePixelsToUnits:\s*([0-9.eE+-]+)");
                float ppu=pm.Success?float.Parse(pm.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture):imp.spritePixelsPerUnit;float sx=(float)nw/w;float sy=(float)nh/h;
                if(imp.spriteImportMode==SpriteImportMode.Single && Mathf.Abs(sx-sy)>0.001f){results.Add(p+"\tSKIP_ASPECT_ROUNDING");continue;}
                File.WriteAllBytes(p,encoded);
                if(imp.spriteImportMode==SpriteImportMode.Single)
                {
                    imp.spritePixelsPerUnit=ppu*sx;var border=imp.spriteBorder;imp.spriteBorder=new Vector4(border.x*sx,border.y*sy,border.z*sx,border.w*sy);
                    imp.SaveAndReimport();
                }
                else AssetDatabase.ImportAsset(p,ImportAssetOptions.ForceUpdate);
                results.Add(p+"\t"+w+"x"+h+"\t"+nw+"x"+nh+"\t"+source.Length+"\t"+encoded.Length+"\tppu="+ppu+"->"+imp.spritePixelsPerUnit);
            }
            finally{UnityEngine.Object.DestroyImmediate(raw);if(output)UnityEngine.Object.DestroyImmediate(output);File.WriteAllLines(Evidence+"/resize-"+Path.GetFileNameWithoutExtension(manifest)+".tsv",results);}
        }
        File.WriteAllLines(Evidence+"/resize-"+Path.GetFileNameWithoutExtension(manifest)+".tsv",results);
        return string.Join("\n",results);
    }
}
