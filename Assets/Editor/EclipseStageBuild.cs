using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Explicit menu/batch command only. Never runs on import or changes a saved scene.
public static class EclipseStageBuild
{
    const string Art = "Assets/Art/SideA/Eclipse";
    const string Res = "Assets/Resources/SideA/Eclipse";
    const string Report = "LocalDocs/SideA/EclipseCathedral";
    static readonly string[] Names = { "Ivory", "Basalt", "Gold", "GoldLight", "Crimson", "Banner" };
    static readonly Color[] Colors = {
        new Color(1f,.98f,.94f),new Color(.90f,.95f,1f),new Color(1f,.94f,.80f),
        new Color(.8f,.52f,.20f),new Color(.30f,.015f,.008f),new Color(.80f,.85f,1f)
    };
    static int assertions;
    static void Check(bool ok,string message)
    { if(!ok) throw new Exception("Eclipse validation: "+message); assertions++; Debug.Log("ECLIPSE_CHECK "+message); }

    [MenuItem("Tools/Side A/Build Eclipse Cathedral")]
    public static void Build()
    {
        RequireCleanEditor();
        Directory.CreateDirectory(Art+"/Materials"); Directory.CreateDirectory(Res); Directory.CreateDirectory(Report);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EclipseSurfaceAssets.Prepare();
        var shader=Shader.Find("FoldedRealm/EclipsePlatform"); Check(shader && !ShaderUtil.ShaderHasError(shader),"platform shader imports");
        var materials=new Material[6];
        for(int i=0;i<6;i++)
        {
            string path=Art+"/Materials/Eclipse_"+Names[i]+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
            m.shader=shader; m.SetColor("_BaseColor",Colors[i]);m.SetFloat("_Metallic",i==2?.8f:0);
            m.SetFloat("_Grain",i<2?.07f:i==2?.04f:0);
            m.SetColor("_EmissionColor",i==3?new Color(.52f,.25f,.06f):i==4?new Color(2.4f,.025f,.008f):Color.black);
            m.SetFloat("_Pulse",i==4?.32f:i==3?.10f:0); m.enableInstancing=true;
            EclipseSurfaceAssets.Bind(m,i);
            EditorUtility.SetDirty(m); materials[i]=m;
        }
        foreach(string name in new[]{"Main","Upper"})
        {
            string path=Art+"/Eclipse_"+name+"_v001.fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            Check(importer!=null,name+" FBX exists");
            importer.globalScale=1; importer.useFileScale=true; importer.importAnimation=false;
            importer.addCollider=false; importer.importCameras=false; importer.importLights=false;
            importer.importNormals=ModelImporterNormals.Import; importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            foreach(var id in importer.GetExternalObjectMap().Keys.ToArray()) importer.RemoveRemap(id);
            for(int i=0;i<6;i++) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),"Eclipse_"+Names[i]),materials[i]);
            importer.SaveAndReimport();
        }
        var ti=(TextureImporter)AssetImporter.GetAtPath(Res+"/EclipseBackground.jpg");
        ti.textureType=TextureImporterType.Default;ti.mipmapEnabled=false;ti.wrapMode=TextureWrapMode.Clamp;
        ti.maxTextureSize=2048;ti.npotScale=TextureImporterNPOTScale.None;
        ti.textureCompression=TextureImporterCompression.Compressed;ti.isReadable=false;ti.SaveAndReimport();
        var root=new GameObject("EclipseArena");
        try
        {
            Spawn(root,"Main",new Vector3(-2,0,0),materials);
            Spawn(root,"Upper",BattleArenaStages.StarCourtPlatformPosition(0),materials);
            Spawn(root,"Upper",BattleArenaStages.StarCourtPlatformPosition(2),materials);
            Check(root.GetComponentsInChildren<Collider>().Length==0,"art has no blocking colliders");
            PrefabUtility.SaveAsPrefabAsset(root,Res+"/EclipseArena.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
        ValidateCore();
    }
    static void Spawn(GameObject root,string name,Vector3 position,Material[] materials)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/Eclipse_"+name+"_v001.fbx");
        var go=(GameObject)PrefabUtility.InstantiatePrefab(source);go.transform.SetParent(root.transform,false);go.transform.position=position;
        // Blender's authored -Y facade arrives at Unity +Z in this FBX import.
        // Rotate in world space while retaining the importer's axis conversion.
        go.transform.rotation=Quaternion.AngleAxis(180,Vector3.up)*go.transform.rotation;
        foreach(var renderer in go.GetComponentsInChildren<Renderer>())
        {
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>{
                int i=Array.FindIndex(Names,n=>m && m.name=="Eclipse_"+n);
                Check(i>=0,"known material "+(m?m.name:"null"));return materials[i];}).ToArray();
        }
        var bounds=BoundsOf(go);float width=name=="Main"?24:6.62f;
        Check(Mathf.Abs(bounds.size.x-width)<.35f,name+" metre scale width "+bounds.size.x);
        Check(Mathf.Abs(bounds.max.y-position.y)<.12f,name+" top aligns to collision plane "+bounds.max.y);
        Check(bounds.size.y>2 && bounds.size.y<5,name+" upright axis and hanging details");
    }
    static Bounds BoundsOf(GameObject go)
    {
        var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;
        foreach(var r in rs)b.Encapsulate(r.bounds);return b;
    }
    [MenuItem("Tools/Side A/Validate Eclipse Cathedral")]
    public static void Validate()
    {
        RequireCleanEditor();
        ValidateCore();
    }
    static void RequireCleanEditor()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before building/validating Eclipse.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save your open scenes first; Eclipse validation opens BattleScene.");
    }
    static void ValidateCore()
    {
        assertions=0;
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity",OpenSceneMode.Single);
        var stages=UnityEngine.Object.FindFirstObjectByType<BattleArenaStages>();
        Check(stages,"stage component exists");
        int[] sequence={0,1,2,0,2,1,2};
        foreach(int index in sequence)
        {
            Check(stages.Apply(index),"apply stage "+index);
            Check(stages.ActiveIndex==index,"active index "+index);
            Check(stages.upperPlatforms[1].gameObject.activeSelf==(index!=2),"centre platform toggles "+index);
            Check(stages.upperPlatforms[0].gameObject.activeInHierarchy && stages.upperPlatforms[2].gameObject.activeInHierarchy,"side platforms remain active");
            Check(stages.sunlitArt.activeSelf==(index==0) && stages.starCourtArt.activeSelf==(index==1),"legacy art state "+index);
            var block=new MaterialPropertyBlock();stages.paintedBackdrop.GetPropertyBlock(block);
            Check(block.GetTexture("_BaseMap")== (index==0?stages.sunlitBackground:index==1?stages.starCourtBackground:stages.EclipseBackground),"background binding "+index);
            Check(stages.GetComponent<BattleStageLighting>().IsEclipse==(index==2),"lighting state "+index);
        }
        Check(!stages.Apply(-1)&&!stages.Apply(3),"reject invalid stage indices");
        var goldLight=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/Eclipse_GoldLight.mat");
        Check(stages.GetComponentsInChildren<Renderer>().Any(r=>r.sharedMaterials.Contains(goldLight)),"gold emission material remains distinct from metal");
        Check(stages.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="Eclipse Cathedral Platforms")==1,"single cached art instance after switching");
        Physics.SyncTransforms();
        for(int i=0;i<3;i++)
        {
            var p=stages.upperPlatforms[i];var cols=p.GetComponentsInChildren<Collider>(true);
            Check(cols.Any(c=>c.enabled),"existing collider retained "+p.name);
            Check(cols.All(c=>c.gameObject.activeInHierarchy)==(i!=1),"third-stage collision visibility "+p.name);
        }
        RenderPreview(stages);
        Check(!ShaderUtil.ShaderHasError(Shader.Find("FoldedRealm/EclipsePlatform")),"rendered shader has no compile errors");
        var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(EclipseSurfaceAssets.ColourPath);
        var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(EclipseSurfaceAssets.NormalPath);
        Check(atlas && atlas.width==1024 && normal && normal.width==512,"bounded colour and normal resolutions");
        foreach(string name in Names)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/Eclipse_"+name+".mat");
            Check(material.GetTexture("_SurfaceMap")==atlas && material.GetTexture("_NormalMap")==normal,"shared atlas binding "+name);
            Check(material.GetFloat("_TextureWeight")==((name=="Crimson" || name=="GoldLight")?0:1),"emission isolation "+name);
        }
        File.WriteAllText(Report+"/validation.txt","PASS "+assertions+" checks\nBlender FBX import, metre scale, origin, materials, background, 0/1/2 stage switching, collision activation.\nStatic rendered Unity preview; runtime test recorded separately.\n");
        Debug.Log("ECLIPSE_VALIDATION_PASS "+assertions);
        // Intentionally don't save: runtime art is loaded from Resources.
    }
    static void RenderPreview(BattleArenaStages stages)
    {
        var camera=stages.battleCamera.GetComponent<Camera>();
        camera.transform.position=new Vector3(-2,5,-34);
        camera.transform.rotation=Quaternion.Euler(8,0,0);camera.fieldOfView=35;camera.aspect=16f/9;
        var backdrop=camera.GetComponent<BattlePaintedBackdrop>();if(backdrop)backdrop.FitNow();
        foreach(var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))canvas.gameObject.SetActive(false);
        Capture(camera,Report+"/Eclipse_Unity_Overview.png");
        camera.transform.position=new Vector3(-2,.9f,-15);
        camera.fieldOfView=40;
        if(backdrop)backdrop.FitNow();
        Capture(camera,Report+"/Eclipse_Unity_MaterialDetail_v002.png");
    }
    static void Capture(Camera camera,string path)
    {
        var target=new RenderTexture(1920,1080,24);camera.targetTexture=target;camera.Render();
        var prior=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=prior;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
    }
}
