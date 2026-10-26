using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Explicit unsaved fixtures only. Does not register Play or import callbacks.
public static class EclipseLevitationChecks
{
    const string Output="LocalDocs/SideA/EclipseCathedral/Levitation_v007";
    static int checks;
    public static void ValidatePreviewAndPlay()
    {
        Validate();RenderMotionPreview();EclipseAtmosphereChecks.RunPlayAutomation();
    }
    static void Check(bool ok,string text)
    {Debug.Log("ECLIPSE_LEVITATION_CHECK "+(ok?"PASS ":"FAIL ")+text);if(!ok)throw new InvalidOperationException(text);checks++;}
    static void Clean()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)
            throw new InvalidOperationException("Save scene edits first; QA never saves or discards them.");
    }
    [MenuItem("Tools/Side A/Validate Eclipse Crimson Levitation")]
    public static void Validate()
    {
        Clean();var setup=EditorSceneManager.GetSceneManagerSetup();bool reduced=BattlePresentationSettings.ReducedMotion;
        Directory.CreateDirectory(Output);checks=0;
        try
        {
            BattlePresentationSettings.ReducedMotion=false;
            Check(EclipseMotionImport.MotionDataVersion>=5,"New pivot payload invalidates imported mesh cache");
            foreach(string model in new[]{"Main","Upper"})CheckPivots(model);
            EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity",OpenSceneMode.Single);
            var stages=Object.FindFirstObjectByType<BattleArenaStages>();
            Check(stages && stages.Apply(2),"Public third-stage API builds effect");
            var atmosphere=stages.GetComponentInChildren<BattleEclipseAtmosphere>();
            var fx=stages.GetComponentInChildren<BattleEclipseLevitation>();
            Check(fx && fx.Ready && fx.Renderers.Length==3,"Exactly three visual-only cached suspension renderers");
            Check(fx.Renderers.Select(r=>r.sharedMaterial).Distinct().Count()==1,"All three platforms share one small procedural material");
            Check(fx.GetComponentsInChildren<Collider>(true).Length==0,"Decorative rocks and gravity currents add no collisions");
            var surface=fx.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.sharedMaterials.Any(m=>m && m.shader.name=="FoldedRealm/EclipsePlatform")).ToArray();
            var matrices=surface.Select(r=>r.localToWorldMatrix).ToArray();
            int colliders=stages.GetComponentsInChildren<Collider>(true).Length;
            int rendererCount=fx.GetComponentsInChildren<Renderer>(true).Length;
            int lightCount=fx.GetComponentsInChildren<Light>(true).Length;
            foreach(var r in fx.Renderers)
            {
                Check(r.sharedMaterial && !ShaderUtil.ShaderHasError(r.sharedMaterial.shader),"Gravity shader compiles for "+r.name);
                Check(r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off && !r.receiveShadows,"Transparent effect never casts shadows");
                var mesh=r.GetComponent<MeshFilter>().sharedMesh;
                Check(mesh.vertexCount<1800 && mesh.triangles.Length/3<1600,"Bounded low-poly procedural stream budget");
                float maxY=mesh.vertices.Max(v=>v.y);
                Check(maxY<-.6f,"All stream geometry stays beneath the walkable deck");
            }
            atmosphere.PreviewAt(3.25f);CheckClock(fx,3.25f,1);
            BattlePresentationSettings.ReducedMotion=true;atmosphere.PreviewAt(3.25f);CheckClock(fx,3.25f,0);
            BattlePresentationSettings.ReducedMotion=false;atmosphere.PreviewAt(7);CheckClock(fx,7,1);
            Check(surface.Select((r,i)=>r.localToWorldMatrix==matrices[i]).All(ok=>ok),"Platforms and gameplay decks never bob or tilt");
            foreach(int stage in new[]{0,1,2,0,2})
            {
                Check(stages.Apply(stage),"Public stage switch "+stage);
                Check(stages.GetComponentsInChildren<BattleEclipseLevitation>(true).Single()==fx &&
                    fx.Renderers.All(r=>r.gameObject.activeInHierarchy==(stage==2)) &&
                    fx.GetComponentsInChildren<Renderer>(true).Length==rendererCount &&
                    fx.GetComponentsInChildren<Light>(true).Length==lightCount &&
                    stages.GetComponentsInChildren<Collider>(true).Length==colliders,
                    "Cached stage visibility, draw, light and collision counts remain stable");
            }
            var shader=Resources.Load<Shader>("SideA/Eclipse/EclipseLevitation");
            string source=File.ReadAllText(AssetDatabase.GetAssetPath(shader));
            Check(!source.Contains("_Time") && source.Contains("ZTest LEqual") && source.Contains("ZWrite Off"),
                "Effect uses only scaled atmosphere clock and normal opaque-depth testing");
            PrepareCamera(stages);
            atmosphere.PreviewAt(0);var first=Capture(stages,"01_Crimson_Overview_0s.png",1920,1080);
            atmosphere.PreviewAt(3.5f);var second=Capture(stages,"02_Crimson_Overview_3p5s.png",1920,1080);
            foreach(var r in fx.Renderers)r.enabled=false;
            var off=Capture(stages,"03_Suspension_Off_3p5s.png",1920,1080);
            foreach(var r in fx.Renderers)r.enabled=true;
            long diff=Difference(second,off),timeDiff=Difference(first,second);
            Check(diff>second.Length/150,"New crimson effects independently produce visible rendered pixels");
            Check(timeDiff>second.Length/150,"Clock changes visibly animate streams and stone silhouettes");
            File.WriteAllText(Path.Combine(Output,"render-difference.txt"),"Actual Unity RGB difference: effect On/Off="+diff+"; time 0/3.5="+timeDiff+"\n");
            var camera=stages.battleCamera.GetComponent<Camera>();
            camera.transform.position=new Vector3(-10.3f,2.5f,-10);camera.transform.rotation=Quaternion.identity;
            camera.GetComponent<BattlePaintedBackdrop>()?.FitNow();
            Capture(stages,"04_Upper_Stones_Detail.png",1920,1080);
            Check(!ShaderUtil.ShaderHasError(shader) && !ShaderUtil.ShaderHasError(Shader.Find("FoldedRealm/EclipsePlatform")),"Actual render compiles gravity, lit, depth and shadow code");
            var ownedMaterial=fx.Renderers[0].sharedMaterial;
            var ownedMeshes=fx.Renderers.Select(r=>r.GetComponent<MeshFilter>().sharedMesh).ToArray();
            Object.DestroyImmediate(fx.gameObject);
            Check(!ownedMaterial && ownedMeshes.All(m=>!m),"Destroy releases all owned stream meshes and material");
            Check(stages.Apply(2) && stages.GetComponentInChildren<BattleEclipseLevitation>().Ready,"Public stage API rebuilds after cleanup");
            File.WriteAllText(Path.Combine(Output,"validation.txt"),"PASS "+checks+" static/render checks\nUnity "+Application.unityVersion+"\nUnsaved Edit-mode fixtures, NOT gameplay recording.\n");
            Debug.Log("ECLIPSE_LEVITATION_PASS "+checks);
        }
        finally
        {
            BattlePresentationSettings.ReducedMotion=reduced;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            if(setup.Length>0 && setup.All(s=>!string.IsNullOrEmpty(s.path)))EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
    static void CheckClock(BattleEclipseLevitation fx,float clock,float motion)
    {
        Check(fx.Renderers.All(r=>{var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);return b.GetFloat("_GravityClock")==clock && b.GetFloat("_Motion")==motion;}),
            "Every current follows shared clock "+clock+" and ReducedMotion gate "+motion);
    }
    static void CheckPivots(string model)
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/SideA/Eclipse/Eclipse_"+model+"_v001.fbx");
        Check(root,model+" original FBX exists");int groups=0;var phases=new HashSet<float>();var agility=new HashSet<float>();
        foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh=f.sharedMesh;var motion=new List<Vector4>();var pivots=new List<Vector4>();
            mesh.GetUVs(1,motion);mesh.GetUVs(3,pivots);
            Check(pivots.Count==mesh.vertexCount,model+" pivot channel matches vertex count");
            var byPhase=new Dictionary<float,Vector4>();bool valid=true,deckStatic=true;
            for(int i=0;i<mesh.vertexCount;i++)
            {
                var p=pivots[i];valid&=!float.IsNaN(p.x+p.y+p.z+p.w) && !float.IsInfinity(p.x+p.y+p.z+p.w) && p.w>=0 && p.w<=1;
                if(motion[i].y<.5f){valid&=p==Vector4.zero;continue;}
                if(byPhase.TryGetValue(motion[i].z,out var first))valid&=p==first;
                else {byPhase.Add(motion[i].z,p);phases.Add(motion[i].z);agility.Add(p.w);}
            }
            var materials=f.GetComponent<Renderer>().sharedMaterials;
            for(int s=0;s<mesh.subMeshCount;s++)if(materials[s].name.StartsWith("Eclipse_Ivory",StringComparison.Ordinal))
                foreach(int vertex in mesh.GetIndices(s))deckStatic&=motion[vertex].y==0 && pivots[vertex]==Vector4.zero;
            Check(valid,model+" each isolated stone has one finite rigid pivot, with no structural vertices selected");
            Check(deckStatic,model+" ivory walking deck remains completely unanimated");groups+=byPhase.Count;
        }
        Check(groups==(model=="Main"?9:4),model+" preserves authored isolated stone count: "+groups);
        Check(phases.Count==groups && agility.Count>1,model+" stones have varied phases and size-dependent inertia");
    }
    static void PrepareCamera(BattleArenaStages stages)
    {
        foreach(var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))canvas.gameObject.SetActive(false);
        var aura=stages.GetComponentInChildren<BattleEclipseFighterAura>();if(aura)aura.enabled=false;
        foreach(string tag in new[]{"Player","Enemy"}){var actor=GameObject.FindGameObjectWithTag(tag);if(actor)actor.SetActive(false);}
        var camera=stages.battleCamera.GetComponent<Camera>();
        camera.transform.position=new Vector3(-2,5,-34);camera.transform.rotation=Quaternion.Euler(8,0,0);camera.fieldOfView=35;camera.aspect=16f/9;
        camera.GetComponent<BattlePaintedBackdrop>()?.FitNow();
    }
    public static void RenderMotionPreview()
    {
        Clean();var setup=EditorSceneManager.GetSceneManagerSetup();bool reduced=BattlePresentationSettings.ReducedMotion;
        string frames=Path.Combine(Output,"MotionFrames");Directory.CreateDirectory(frames);
        try
        {
            BattlePresentationSettings.ReducedMotion=false;
            EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity",OpenSceneMode.Single);
            var stages=Object.FindFirstObjectByType<BattleArenaStages>();stages.Apply(2);PrepareCamera(stages);
            var atmosphere=stages.GetComponentInChildren<BattleEclipseAtmosphere>();
            // 10 seconds of actual Unity fixed-camera material animation.
            for(int i=0;i<150;i++){atmosphere.PreviewAt(i/15f);Capture(stages,Path.Combine("MotionFrames","frame-"+i.ToString("D4")+".png"),1280,720);}
            File.WriteAllText(Path.Combine(Output,"animation-preview.txt"),"Actual Unity render, 150 frames / 15 fps / 10 seconds / 1280x720.\nFixed-camera unsaved Edit-mode VFX fixture, not gameplay. No actors or HUD; no audio.\n");
            Debug.Log("ECLIPSE_LEVITATION_PREVIEW 150");
        }
        finally
        {
            BattlePresentationSettings.ReducedMotion=reduced;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            if(setup.Length>0 && setup.All(s=>!string.IsNullOrEmpty(s.path)))EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
    static long Difference(byte[] a,byte[] b){long d=0;for(int i=0;i<a.Length;i++)d+=Math.Abs(a[i]-b[i]);return d;}
    static byte[] Capture(BattleArenaStages stages,string name,int width,int height)
    {
        var camera=stages.battleCamera.GetComponent<Camera>();var old=camera.targetTexture;var active=RenderTexture.active;
        var rt=new RenderTexture(width,height,24);var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());return image.GetRawTextureData<byte>().ToArray();
        }
        finally{camera.targetTexture=old;RenderTexture.active=active;Object.DestroyImmediate(image);rt.Release();Object.DestroyImmediate(rt);}
    }
}
