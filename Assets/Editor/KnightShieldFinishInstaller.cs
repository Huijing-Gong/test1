using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// A planar material mask derived from the native mesh; no vertex, UV or rig edits.
public static class KnightShieldFinishInstaller
{
    public const string Folder="Assets/Art/Enemies/AdventureTrio/Styled/Knight";
    public const string ShaderName="FoldedRealm/KnightShieldFinish";
    public const string MaskPath=Folder+"/KnightShield_Perimeter.png";
    public static void Install()
    {
        if(!Application.dataPath.StartsWith("/private/tmp/adventure-trio-knight-",StringComparison.Ordinal)||Application.isPlaying)
            throw new InvalidOperationException("Isolated Knight editor project only");
        var native=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPGMonsterWave03PBR/Prefab/Characters/BishopKnightPBR.prefab");
        var mesh=native.GetComponentsInChildren<MeshFilter>(true).Single(r=>r.name=="Bishop_Shield").sharedMesh;
        var vertices=mesh.vertices;var triangles=mesh.triangles;
        var min=new Vector2(mesh.bounds.min.x-.01f,mesh.bounds.min.z-.01f);
        var size=new Vector2(mesh.bounds.size.x+.02f,mesh.bounds.size.z+.02f);
        const int n=512;var occupied=new bool[n*n];
        for(int i=0;i<triangles.Length;i+=3)
        {
            Vector2 a=Project(vertices[triangles[i]],min,size,n),b=Project(vertices[triangles[i+1]],min,size,n),c=Project(vertices[triangles[i+2]],min,size,n);
            if(Mathf.Abs(Cross(b-a,c-a))<.001f)continue;
            int x0=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x,Mathf.Min(b.x,c.x))),0,n-1),x1=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x,Mathf.Max(b.x,c.x))),0,n-1);
            int y0=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y,Mathf.Min(b.y,c.y))),0,n-1),y1=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y,Mathf.Max(b.y,c.y))),0,n-1);
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {
                Vector2 p=new Vector2(x+.5f,y+.5f);float ab=Cross(b-a,p-a),bc=Cross(c-b,p-b),ca=Cross(a-c,p-c);
                if((ab>=0&&bc>=0&&ca>=0)||(ab<=0&&bc<=0&&ca<=0))occupied[y*n+x]=true;
            }
        }
        // Chamfer distance in mesh-local metres keeps the gold band equally wide
        // along long and short edges, including the native pointed upper outline.
        var distance=occupied.Select(v=>v?100f:0f).ToArray();
        float dx=size.x/n,dy=size.y/n,diag=Mathf.Sqrt(dx*dx+dy*dy);
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            int k=y*n+x;if(x>0)Relax(distance,k,k-1,dx);if(y>0)Relax(distance,k,k-n,dy);
            if(x>0&&y>0)Relax(distance,k,k-n-1,diag);if(x<n-1&&y>0)Relax(distance,k,k-n+1,diag);
        }
        for(int y=n-1;y>=0;y--)for(int x=n-1;x>=0;x--)
        {
            int k=y*n+x;if(x<n-1)Relax(distance,k,k+1,dx);if(y<n-1)Relax(distance,k,k+n,dy);
            if(x<n-1&&y<n-1)Relax(distance,k,k+n+1,diag);if(x>0&&y<n-1)Relax(distance,k,k+n-1,diag);
        }
        var pixels=new Color32[n*n];
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            float rim=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.036f,.045f,distance[y*n+x]));
            pixels[y*n+x]=new Color(rim,(float)y/(n-1),0,1);
        }
        var tex=new Texture2D(n,n,TextureFormat.RGBA32,false,true);
        try { tex.SetPixels32(pixels);tex.Apply();File.WriteAllBytes(MaskPath,tex.EncodeToPNG()); }
        finally { Object.DestroyImmediate(tex); }
        AssetDatabase.ImportAsset(MaskPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(MaskPath);
        importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;
        importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.maxTextureSize=512;importer.isReadable=false;importer.SaveAndReimport();
        var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/KnightShield.mat");
        var shader=Shader.Find(ShaderName);if(!shader||!shader.isSupported)throw new InvalidOperationException("Shield shader unavailable");
        material.shader=shader;
        material.SetTexture("_ShieldMask",AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath));
        material.SetVector("_ShieldBounds",new Vector4(min.x,min.y,size.x,size.y));
        material.SetColor("_ShieldBlue",new Color(.25f,.38f,.57f,1));
        material.SetColor("_ShieldGold",new Color(.88f,.67f,.31f,1));
        material.SetColor("_BaseColor",Color.white);material.SetColor("_ArmorColor",Color.white);
        material.SetColor("_ShadowColor",new Color(.50f,.57f,.68f,1));material.SetColor("_DeepShadowColor",new Color(.30f,.36f,.47f,1));
        material.SetColor("_SpecularColor",new Color(.96f,.94f,.87f,1));
        material.SetFloat("_SatinReflection",.26f);material.SetFloat("_VertexAoStrength",0);
        material.SetFloat("_AmbientStrength",.4f);material.SetFloat("_ShadeSoftness",.24f);
        material.SetFloat("_Metallic",.55f);material.SetFloat("_Smoothness",.48f);material.SetFloat("_SpecularStrength",.75f);
        material.SetFloat("_OcclusionStrength",0);material.SetFloat("_EmissionStrength",0);material.SetFloat("_TechStrength",0);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();Validate(material);
        Debug.Log("SHIELD FINISH PASS: native geometry unchanged; continuous projected gold border and independent steel blue material response.");
    }
    public static void Validate(Material material)
    {
        if(!material||material.shader.name!=ShaderName||AssetDatabase.GetAssetPath(material.GetTexture("_ShieldMask"))!=MaskPath||material.GetColor("_BaseColor")!=Color.white)
            throw new InvalidOperationException("Shield finish material/mask binding invalid");
        if(ShaderUtil.GetShaderMessages(material.shader).Any(m=>m.severity.ToString()=="Error"))throw new InvalidOperationException("Shield shader compilation failed");
        Vector4 bounds=material.GetVector("_ShieldBounds");if(bounds.z<=0||bounds.w<=0)throw new InvalidOperationException("Invalid shield projection");
    }
    static void Relax(float[] d,int to,int from,float cost){d[to]=Mathf.Min(d[to],d[from]+cost);}
    static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
    static Vector2 Project(Vector3 p,Vector2 min,Vector2 size,int n)=>new Vector2((p.x-min.x)/size.x*n,(p.z-min.y)/size.y*n);
}
