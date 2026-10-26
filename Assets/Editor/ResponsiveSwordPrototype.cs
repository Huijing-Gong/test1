using System;
using System.IO;
using System.Linq;
using System.Text;
using KingOfGraphic.Hero;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ResponsiveSwordPrototype
{
    const string Root="Assets/WorldHero/ResponsiveSword/Source/";
    const string Report="LocalDocs/ResponsiveSword/validation.txt";
    const string Request="Temp/responsive-sword-request.txt";
    static ResponsiveSwordPrototype(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;
        var command=File.ReadAllText(Request).Trim();File.Delete(Request);
        try {if(command=="apply")Apply();else if(command=="check")Check();else if(command=="render")RenderPoses();}
        catch(Exception e){Directory.CreateDirectory(Path.GetDirectoryName(Report));File.WriteAllText(Report,"FAIL\n"+e);Debug.LogException(e);}
    }
    static AnimationClip Clip(string path)
    {
        var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview"));
        if(!clip||!clip.isHumanMotion)throw new Exception("Missing humanoid motion: "+path);
        return clip;
    }
    [MenuItem("Tools/Hero/Apply responsive sword prototype")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode before applying library settings.");
        var lib=AssetDatabase.LoadAssetAtPath<HeroActionLibrary>("Assets/OwnHero/Resources/HeroActions.asset");
        Undo.RecordObject(lib,"Responsive sword prototype");lib.responsiveSwordCombat=true;
        void Attack(string id,AnimationClip clip,float offset,float rate,float duration,float open,float close,float cancel,float junction)
        {
            var a=lib.Find(id);a.clip=clip;a.authoredMotion=true;a.authoredOffset=offset;a.authoredRate=rate;
            a.duration=duration;a.hitOpen=open;a.hitClose=close;a.cancelFrom=cancel;a.junction=junction;
            a.aimEnd=open;a.swingCue=open;a.blendDuration=.055f;a.authoredFootIK=true;
        }
        Attack("ATK_A1",lib.Find("ATK_A1").clip,.18f,1.35f,.62f,.13f,.448f,.46f,.46f);
        Attack("ATK_A2",lib.Find("ATK_A2").clip,.04f,1.15f,.46f,.097f,.270f,.30f,.32f);
        var third=AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath("b0fe9cfaccecbfd48b38e13bb57c4874"));
        Attack("ATK_A3",third,.52f,1.6f,.76f,.10f,.56f,.60f,.64f);
        lib.Find("ATK_A3").title="Longsword downward finisher";
        foreach(string dir in new[]{"F","B","L","R"})
        {
            string source=dir=="F"?"Esc_BoostDash_Front":dir=="B"?"Esc_BoostDash_Back":dir=="L"?"Esc_Move_Left":"Esc_Move_Right";
            var clip=Clip(Root+source+".FBX");
            foreach(string prefix in new[]{"DODGE_","PERFECT_DODGE_"})
            {
                var a=lib.Find(prefix+dir);a.clip=clip;a.authoredMotion=true;a.authoredOffset=.033333f;
                a.authoredRate=dir=="F"||dir=="B"?1.25f:1.2f;a.duration=.42f;
                a.blendDuration=.035f;a.cancelFrom=.27f;a.recoveryJunction=0;
            }
        }
        EditorUtility.SetDirty(lib);AssetDatabase.SaveAssets();
        Check();
        Debug.Log("RESPONSIVE_SWORD_APPLIED: current overworld library updated; scene not rewritten.");
    }
    [MenuItem("Tools/Hero/Check responsive sword prototype")]
    public static void Check()
    {
        var lib=AssetDatabase.LoadAssetAtPath<HeroActionLibrary>("Assets/OwnHero/Resources/HeroActions.asset");
        var log=new StringBuilder();void Need(bool ok,string message){if(!ok)throw new Exception(message);log.AppendLine("PASS "+message);}
        Need(lib&&lib.responsiveSwordCombat,"profile enabled");
        foreach(string id in new[]{"ATK_A1","ATK_A2","ATK_A3","DODGE_F","DODGE_B","DODGE_L","DODGE_R"})
        {
            var a=lib.Find(id);Need(a!=null&&a.clip&&a.clip.isHumanMotion,id+" humanoid source");
            Need(a.authoredOffset+a.duration*a.authoredRate<=a.clip.length+.005f,id+" trimmed sample stays inside source");
            if(id.StartsWith("ATK_"))Need(a.hitOpen>=0&&a.hitOpen<a.hitClose&&a.hitClose<=a.cancelFrom&&a.cancelFrom<=a.duration&&a.junction>=a.hitClose&&a.junction<=a.duration,id+" damage/cancel/chain ordering");
            log.AppendLine(id+" duration="+a.duration+" source="+AssetDatabase.GetAssetPath(a.clip)+" offset="+a.authoredOffset+" rate="+a.authoredRate);
        }
        foreach(float hz in new[]{30f,60f,144f})
        {
            float distance=0;for(float t=0;t<.42f;t+=1/hz)distance+=HeroResponsiveSwordMotion.DodgeStep(t,1/hz);
            Need(Mathf.Abs(distance-HeroResponsiveSwordMotion.DodgeDistance)<.001f,"dodge distance at "+hz+" fps = "+distance);
        }
        var clock=new HeroCombatClock{library=lib};clock.Begin();clock.Tick(.08f,false,false,false);clock.Tick(.01f,true,false,false);
        for(int i=0;i<80&&clock.Id=="ATK_A1";i++)clock.Tick(.01f,false,false,false);
        Need(clock.Id=="ATK_A2","buffered A1 chains into A2");
        clock.Begin();clock.Tick(lib.Find("ATK_A1").hitOpen+.01f,false,false,false);
        Need(!clock.CanCancelIntoDodge,"committed active swing cannot be erased by a dodge");
        clock.Tick(lib.Find("ATK_A1").hitClose-clock.Time+.01f,false,false,false);
        Need(clock.CanCancelIntoDodge,"dodge accepted immediately after active swing");
        clock.Cancel();Need(!clock.Active&&!clock.HasQueuedCombo,"cancel clears queued combo");
        Directory.CreateDirectory(Path.GetDirectoryName(Report));File.WriteAllText(Report,log.ToString());Debug.Log("RESPONSIVE_SWORD_CHECK_PASS\n"+log);
    }
    [MenuItem("Tools/Hero/Render responsive sword poses")]
    public static void RenderPoses()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play before rendering isolated pose previews.");
        string folder="LocalDocs/ResponsiveSword/Poses";Directory.CreateDirectory(folder);
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        GameObject actor=null,cameraObject=null,lightObject=null;RenderTexture rt=null;
        try {
            actor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldHero/RedHeroAuthoredVisual.prefab"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor,scene);actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var c in actor.GetComponentsInChildren<MonoBehaviour>(true))if(!(c is HeroPoseDriver))c.enabled=false;
            var pose=actor.GetComponentInChildren<HeroPoseDriver>();var animator=actor.GetComponentInChildren<Animator>();
            foreach(var t in actor.GetComponentsInChildren<Transform>(true))if(t.name.Contains("PracticeSword"))t.gameObject.SetActive(true);
            cameraObject=new GameObject("Preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.12f,.16f);
            camera.transform.position=new Vector3(3,1.8f,4);camera.transform.LookAt(new Vector3(0,1,0));camera.fieldOfView=32;camera.nearClipPlane=.01f;camera.farClipPlane=30;
            lightObject=new GameObject("Preview light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;lightObject.transform.rotation=Quaternion.Euler(35,-35,0);
            rt=new RenderTexture(640,640,24);camera.targetTexture=rt;
            var baked=new System.Collections.Generic.List<System.Tuple<SkinnedMeshRenderer,Mesh>>();
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(skin.enabled&&skin.gameObject.activeInHierarchy){
                var go=new GameObject("Baked preview skin");go.transform.SetParent(skin.transform,false);
                var mesh=new Mesh();go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                baked.Add(System.Tuple.Create(skin,mesh));skin.enabled=false;
            }
            var report=new StringBuilder();
            foreach(string id in new[]{"ATK_A1","ATK_A2","ATK_A3","DODGE_F","DODGE_B","DODGE_L","DODGE_R"})
            {
                pose.ResetPresentation();var d=pose.library.Find(id);
                for(int i=0;i<8;i++){
                    float t=d.duration*i/7;pose.Sample(id,t,.016f,true,.025f);
                    var hips=animator.GetBoneTransform(HumanBodyBones.Hips);var head=animator.GetBoneTransform(HumanBodyBones.Head);
                    if(float.IsNaN(head.position.y)||Vector3.Distance(head.position,hips.position)<.1f)throw new Exception(id+" invalid retargeted skeleton");
                    foreach(var skin in baked)skin.Item1.BakeMesh(skin.Item2);
                    camera.Render();var prior=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(640,640,TextureFormat.RGB24,false);
                    tex.ReadPixels(new Rect(0,0,640,640),0,0);tex.Apply();File.WriteAllBytes(folder+"/"+id+"-"+i+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);RenderTexture.active=prior;
                }
                report.AppendLine("PASS "+id+" eight retargeted frames rendered on red-haired hero");
            }
            foreach(var skin in baked)UnityEngine.Object.DestroyImmediate(skin.Item2);
            File.WriteAllText("LocalDocs/ResponsiveSword/visual-validation.txt",report.ToString());Debug.Log("RESPONSIVE_SWORD_VISUAL_PASS");
        } finally {
            if(actor)UnityEngine.Object.DestroyImmediate(actor);if(cameraObject)UnityEngine.Object.DestroyImmediate(cameraObject);if(lightObject)UnityEngine.Object.DestroyImmediate(lightObject);if(rt)UnityEngine.Object.DestroyImmediate(rt);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

}
