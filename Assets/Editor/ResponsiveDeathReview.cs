using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
[InitializeOnLoad]
public static class ResponsiveDeathReview
{
    static ResponsiveDeathReview(){EditorApplication.update+=()=>{if(File.Exists("Temp/death-review.txt")&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode){File.Delete("Temp/death-review.txt");Run();}};}
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play before rendering isolated pose previews.");
        string folder="LocalDocs/ResponsiveSword/DeathReview";Directory.CreateDirectory(folder);
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        GameObject actor=null,cameraObject=null,lightObject=null;RenderTexture rt=null;
        try {
            actor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldHero/RedHeroAuthoredVisual.prefab"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor,scene);actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var c in actor.GetComponentsInChildren<MonoBehaviour>(true))if(!(c is HeroPoseDriver))c.enabled=false;
            var pose=actor.GetComponentInChildren<HeroPoseDriver>();var animator=actor.GetComponentInChildren<Animator>();
            pose.library=UnityEngine.Object.Instantiate(pose.library);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.position=new Vector3(0,-.05f,0);floor.transform.localScale=new Vector3(8,.1f,8);
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.color=new Color(.25f,.3f,.34f);floor.GetComponent<Renderer>().sharedMaterial=mat;
            foreach(var t in actor.GetComponentsInChildren<Transform>(true))if(t.name.Contains("PracticeSword"))t.gameObject.SetActive(true);
            cameraObject=new GameObject("Preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.12f,.16f);
            camera.transform.position=new Vector3(3,2.5f,4);camera.transform.LookAt(new Vector3(0,.65f,.6f));camera.fieldOfView=45;camera.nearClipPlane=.01f;camera.farClipPlane=30;
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
            foreach(string id in new[]{"Original","StandDeath","StandDeathRoot","TurnDeath"})
            {
                pose.ResetPresentation();var d=pose.library.Find("DEATH_GROUND");
                var clips=id=="Original"?new[]{AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AuthorMotions/Clips/Core/Death.anim")}:AssetDatabase.LoadAllAssetsAtPath("Assets/WorldHero/ResponsiveSword/DeathCandidates/"+id+".FBX").OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__")).ToArray();
                d.clip=clips[0];d.duration=d.clip.length;d.authoredOffset=0;d.authoredRate=1;d.authoredFootIK=false;d.loop=false;
                report.AppendLine(id+" length="+d.clip.length);
                for(int i=0;i<8;i++){
                    float t=d.duration*i/7;pose.Sample("DEATH_GROUND",t,.05f,false,0);
                    var hips=animator.GetBoneTransform(HumanBodyBones.Hips);var head=animator.GetBoneTransform(HumanBodyBones.Head);
                    if(float.IsNaN(head.position.y)||Vector3.Distance(head.position,hips.position)<.1f)throw new Exception(id+" invalid retargeted skeleton");
                    float min=float.PositiveInfinity;
                    foreach(var skin in baked){skin.Item1.BakeMesh(skin.Item2);foreach(var vertex in skin.Item2.vertices)min=Mathf.Min(min,skin.Item1.transform.TransformPoint(vertex).y);}
                    report.AppendLine(id+" t="+t+" hips="+hips.position.y+" skinMin="+min);
                    camera.Render();var prior=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(640,640,TextureFormat.RGB24,false);
                    tex.ReadPixels(new Rect(0,0,640,640),0,0);tex.Apply();File.WriteAllBytes(folder+"/"+id+"-"+i+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);RenderTexture.active=prior;
                }
                report.AppendLine("PASS "+id+" eight retargeted frames rendered on red-haired hero");
            }
            foreach(var skin in baked)UnityEngine.Object.DestroyImmediate(skin.Item2);
            File.WriteAllText("LocalDocs/ResponsiveSword/DeathReview/review.txt",report.ToString());Debug.Log("RESPONSIVE_SWORD_VISUAL_PASS");
        } finally {
            if(actor)UnityEngine.Object.DestroyImmediate(actor);if(cameraObject)UnityEngine.Object.DestroyImmediate(cameraObject);if(lightObject)UnityEngine.Object.DestroyImmediate(lightObject);if(rt)UnityEngine.Object.DestroyImmediate(rt);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

}
