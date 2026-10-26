using System.IO;
using UnityEditor;
using UnityEngine;

// Authoring helper. One 1024 colour atlas and one 512 shallow-relief normal.
// The normal is an artistic height approximation, not a scanned PBR material.
public static class EclipseSurfaceAssets
{
    public const string Folder="Assets/Art/SideA/Eclipse/Textures";
    public const string ColourPath=Folder+"/EclipseSurfaceAtlas_v002.jpg";
    public const string NormalPath=Folder+"/EclipseSurfaceNormal_v002.png";
    public static void Prepare()
    {
        AssetDatabase.ImportAsset(ColourPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(ColourPath);
        if(!importer)throw new FileNotFoundException("Eclipse surface atlas missing",ColourPath);
        importer.textureType=TextureImporterType.Default;
        importer.sRGBTexture=true;importer.maxTextureSize=1024;importer.mipmapEnabled=true;
        importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;
        importer.anisoLevel=4;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.isReadable=true;importer.SaveAndReimport();
        var source=AssetDatabase.LoadAssetAtPath<Texture2D>(ColourPath);
        if(source.width!=1024 || source.height!=1024)throw new System.InvalidOperationException("Atlas must be 1024 square");
        int size=512,half=size/2;
        // Filtered mip keeps micro-grain quiet at the actual combat distance.
        Color[] pixels=source.GetPixels(1),normal=new Color[pixels.Length];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            int qx=x/half*half,qy=y/half*half;
            int left=qx+(x-qx-1+half)%half,right=qx+(x-qx+1)%half;
            int down=qy+(y-qy-1+half)%half,up=qy+(y-qy+1)%half;
            float dx=(pixels[y*size+right].grayscale-pixels[y*size+left].grayscale)*3.5f;
            float dy=(pixels[up*size+x].grayscale-pixels[down*size+x].grayscale)*3.5f;
            Vector3 n=new Vector3(-dx,-dy,1).normalized;
            normal[y*size+x]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
        }
        var generated=new Texture2D(size,size,TextureFormat.RGB24,false,true);
        generated.SetPixels(normal);generated.Apply();
        File.WriteAllBytes(NormalPath,generated.EncodeToPNG());Object.DestroyImmediate(generated);
        importer.textureCompression=TextureImporterCompression.Compressed;
        importer.isReadable=false;importer.SaveAndReimport();
        AssetDatabase.ImportAsset(NormalPath,ImportAssetOptions.ForceSynchronousImport);
        var ni=(TextureImporter)AssetImporter.GetAtPath(NormalPath);
        ni.textureType=TextureImporterType.NormalMap;ni.convertToNormalmap=false;
        ni.maxTextureSize=512;ni.mipmapEnabled=true;ni.wrapMode=TextureWrapMode.Clamp;
        ni.filterMode=FilterMode.Trilinear;ni.anisoLevel=4;
        ni.textureCompression=TextureImporterCompression.Compressed;ni.isReadable=false;ni.SaveAndReimport();
    }
    public static void Bind(Material m,int kind)
    {
        bool textured=kind<3||kind==5;
        // Image top row is ivory/basalt; Unity texture origin is bottom-left.
        Vector4 rect=kind==0?new Vector4(0,.5f,.5f,.5f):kind==1?new Vector4(.5f,.5f,.5f,.5f):
            kind==5?new Vector4(.5f,0,.5f,.5f):new Vector4(0,0,.5f,.5f);
        m.SetTexture("_SurfaceMap",AssetDatabase.LoadAssetAtPath<Texture2D>(ColourPath));
        m.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        m.SetVector("_AtlasRect",rect);m.SetFloat("_TextureWeight",textured?1:0);
        m.SetFloat("_TileScale",kind==0?.45f:kind==1?.65f:kind==5?1.7f:2.0f);
        m.SetFloat("_NormalStrength",kind==0?.24f:kind==1?.30f:kind==5?.12f:.14f);
        m.SetFloat("_Smoothness",kind==2?.62f:.17f);
    }
}
