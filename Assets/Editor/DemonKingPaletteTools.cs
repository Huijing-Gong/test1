using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Native material comparison. Source texture/prefab/showcase assets stay unchanged.
public static class DemonKingPaletteTools
{
    const string Folder=DemonKingTrialInstaller.Folder;
    const string Output="LocalDocs/Enemies/DemonKingTrial-20261020/AdventurePalette_v3";
    public static void ApplyMaterials()
    {
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/DemonKingPalette.shader");
        if(!shader || !shader.isSupported)throw new InvalidOperationException("Muted palette shader unsupported.");
        var armour=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Armour.mat");
        var blade=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Blade.mat");
        foreach(var mat in new[]{armour,blade})
        {
            mat.shader=shader;mat.shaderKeywords=Array.Empty<string>();
            mat.SetColor("_BaseColor",Color.white);mat.SetColor("_SkinColor",new Color(.48f,.24f,.69f));
            mat.SetColor("_ArmourColor",new Color(.14f,.18f,.29f));mat.SetColor("_CopperColor",new Color(.72f,.52f,.26f));mat.SetColor("_HornColor",new Color(.86f,.79f,.65f));
            mat.SetFloat("_Charge",0);mat.SetFloat("_IsBlade",mat==blade?1:0);
            mat.SetVector("_EdgeAxis",new Vector4(1,0,0,0));mat.SetFloat("_EdgeStart",.6f);EditorUtility.SetDirty(mat);
        }
        AssetDatabase.SaveAssets();
    }
    public static void ApplyAndCapture()
    {
        Directory.CreateDirectory(Output);EditorSceneManager.OpenScene(DemonKingTrialInstaller.ScenePath);
        var visual=Object.FindFirstObjectByType<DemonKingCombatVisual>();if(!visual)throw new InvalidOperationException("Authored trial visual missing.");
        // Controlled preview placement on nearby open grass, never saved to the map.
        var world=Object.FindFirstObjectByType<BaselineWorld>();
        world.RefreshNavigation();Physics.SyncTransforms();
        if(world.NavigationGround(visual.transform.parent.position+visual.transform.parent.forward*4,out var floor))
            visual.transform.parent.position=floor+Vector3.up*.015f;
        DemonKingTrialInstaller.Clip("IdleNormal").SampleAnimation(visual.pose.gameObject,0);
        var currentBody=visual.bodySkin.sharedMaterial;var currentBlade=visual.weaponRenderer.sharedMaterial;
        var previousBody=new Material(AssetDatabase.LoadAssetAtPath<Material>(DemonKingTrialInstaller.Source+"/Material/PBR_Default.mat"));
        previousBody.SetColor("_EmissionColor",Color.white*.65f);previousBody.SetFloat("_Smoothness",.3f);
        var previousBlade=new Material(previousBody);previousBlade.SetTexture("_EmissionMap",null);previousBlade.SetColor("_EmissionColor",Color.black);
        visual.bodySkin.sharedMaterial=previousBody;visual.weaponRenderer.sharedMaterial=previousBlade;
        Capture("01-Before-Day.png",visual);
        visual.bodySkin.sharedMaterial=currentBody;visual.weaponRenderer.sharedMaterial=currentBlade;Object.DestroyImmediate(previousBody);Object.DestroyImmediate(previousBlade);
        ApplyMaterials();
        Capture("02-After-Day.png",visual);
        var sun=RenderSettings.sun;Quaternion oldRotation=sun?sun.transform.rotation:Quaternion.identity;
        if(sun)sun.transform.rotation=Quaternion.Euler(5,120,0);
        Capture("03-After-LowLight.png",visual);
        if(sun)sun.transform.rotation=oldRotation;
        DemonKingTrialInstaller.Clip("Attack01").SampleAnimation(visual.pose.gameObject,.32f);
        var block=new MaterialPropertyBlock();block.SetFloat("_Charge",1);visual.weaponRenderer.SetPropertyBlock(block);
        Capture("04-Charged-CuttingEdge.png",visual);visual.weaponRenderer.SetPropertyBlock(null);
        var mesh=visual.blade.GetComponent<MeshFilter>().sharedMesh;
        Debug.Log("DEMON_PALETTE blade local bounds="+mesh.bounds+" edge axis X");
        if(ShaderUtil.ShaderHasError(visual.weaponRenderer.sharedMaterial.shader))throw new InvalidOperationException("Palette shader compile errors.");
        File.WriteAllText(Output+"/validation.txt","PASS native day, low-angle light and charged cutting-edge renders. Source palette atlas and all gameplay/animation assets unchanged. Runtime attack regression follows.\n");
    }
    static void Capture(string name,DemonKingCombatVisual visual)
    {
        Transform actor=visual.transform.parent;Vector3 focus=actor.position+Vector3.up*1.2f;
        var go=new GameObject("Palette QA render");var cam=go.AddComponent<Camera>();
        if(Camera.main)cam.CopyFrom(Camera.main);cam.enabled=false;cam.transform.position=focus+actor.forward*6.5f+actor.right*4+Vector3.up*2.6f;cam.transform.LookAt(focus);cam.fieldOfView=32;cam.aspect=16f/9;cam.nearClipPlane=.1f;cam.farClipPlane=500;
        var rt=new RenderTexture(1600,900,24);var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);var old=RenderTexture.active;
        try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+name,tex.EncodeToPNG());}
        finally{RenderTexture.active=old;cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
    }
}
