using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;

// Isolated source-motion review. Never writes the live action library or scene.
[InitializeOnLoad]
public static class ResponsiveClimbReview
{
    static ResponsiveClimbReview(){EditorApplication.update+=()=>{
        if(!File.Exists("Temp/climb-review.txt")||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete("Temp/climb-review.txt");Run();
    };}
    static void Run(){
        string folder="LocalDocs/ClimbReview";Directory.CreateDirectory(folder);
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();GameObject actor=null,cameraObject=null,lightObject=null;RenderTexture rt=null;HeroActionLibrary library=null;Material wallMaterial=null,treeMaterial=null;
        GameObject carrier=null;
        var meshes=new System.Collections.Generic.List<Tuple<SkinnedMeshRenderer,Mesh>>();
        var report=new StringBuilder("Raw humanoid motion review; no gameplay movement/contact IK changes.\n");
        try{
            actor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldHero/RedHeroAuthoredVisual.prefab"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor,scene);actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var script in actor.GetComponentsInChildren<MonoBehaviour>(true))if(!(script is HeroPoseDriver))script.enabled=false;
            carrier=new GameObject("Review capsule root");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(carrier,scene);actor.transform.SetParent(carrier.transform,false);
            var pose=actor.GetComponentInChildren<HeroPoseDriver>();library=UnityEngine.Object.Instantiate(pose.library);pose.library=library;
            foreach(var t in actor.GetComponentsInChildren<Transform>(true))if(t.name.Contains("PracticeSword"))t.gameObject.SetActive(false);
            var surface=new GameObject("Reference surface");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(surface,scene);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(surface.transform);wall.transform.position=new Vector3(0,.84f,1.45f);wall.transform.localScale=new Vector3(2.1f,1.68f,2f);
            var tree=GameObject.CreatePrimitive(PrimitiveType.Cylinder);tree.transform.SetParent(surface.transform);tree.transform.position=new Vector3(0,1.5f,.40f);tree.transform.localScale=new Vector3(.6f,1.5f,.6f);tree.SetActive(false);
            wallMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));wallMaterial.color=new Color(.17f,.22f,.28f);wall.GetComponent<Renderer>().sharedMaterial=wallMaterial;treeMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));treeMaterial.color=new Color(.26f,.15f,.08f);tree.GetComponent<Renderer>().sharedMaterial=treeMaterial;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.GetComponent<Renderer>().sharedMaterial=wallMaterial;floor.transform.SetParent(surface.transform);floor.transform.position=new Vector3(0,-.06f,0);floor.transform.localScale=new Vector3(5,.1f,5);
            cameraObject=new GameObject("Motion review camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.105f,.14f);camera.transform.position=new Vector3(2.5f,1.8f,-3.1f);camera.transform.LookAt(new Vector3(0,1.15f,.10f));camera.fieldOfView=37;camera.farClipPlane=25;
            lightObject=new GameObject("Motion review light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;lightObject.transform.rotation=Quaternion.Euler(35,-30,0);
            rt=new RenderTexture(520,520,24);camera.targetTexture=rt;
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(skin.enabled&&skin.gameObject.activeInHierarchy){var go=new GameObject("Baked reviewed body");go.transform.SetParent(skin.transform,false);var mesh=new Mesh();go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;meshes.Add(Tuple.Create(skin,mesh));skin.enabled=false;}
            string[] names={"WallGrab","WallHold","ClimbOver"};
            foreach(string name in names){
                string path=name=="ProjectWallUp"?"Assets/_Staging/Climb/X Bot@Climbing Up Wall.fbx":name=="ProjectWallTop"?"Assets/_Staging/Climb/X Bot@Climbing To Top.fbx":name=="ProjectPole"?"Assets/OwnHero/Animations/POLE_CLIMB.anim":"Assets/WorldHero/ClimbReview/Source/"+name+".FBX";
                var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__"));if(!clip)throw new Exception("Missing source "+path);
                pose.ResetPresentation();var d=library.Find("ATK_A1");d.clip=clip;d.authoredMotion=true;d.authoredOffset=0;d.authoredRate=1;d.authoredFootIK=false;d.duration=clip.length;d.hitOpen=d.hitClose=-1;d.loop=false;d.supportCompensationClip=null;
                var live=library.Find(name=="WallGrab"?"WALL_LEDGE_GRAB":name=="WallHold"?"WALL_LEDGE_HANG":"WALL_LEDGE_TOP");d.authoredClimbRootClip=live.authoredClimbRootClip;d.authoredClimbRoot=live.authoredClimbRoot;
                wall.SetActive(true);tree.SetActive(false);camera.transform.position=new Vector3(2.5f,1.8f,-3.1f);camera.transform.LookAt(new Vector3(0,1.15f,.10f));
                for(int i=0;i<28;i++){
                    float time=clip.length*i/27;carrier.transform.position=name=="ClimbOver"?Vector3.Lerp(Vector3.zero,new Vector3(0,1.73f,1.05f),Mathf.SmoothStep(0,1,(float)i/27)):Vector3.zero;pose.Sample("ATK_A1",time,.016f,false,0);
                    foreach(var skin in meshes)skin.Item1.BakeMesh(skin.Item2);
                    if(name=="ClimbOver"||name=="ProjectWallTop"){
                        Vector3 aim=pose.animator.GetBoneTransform(HumanBodyBones.Hips).position+Vector3.up*.2f;
                        camera.transform.position=aim+new Vector3(2.5f,1.4f,-3.1f);camera.transform.LookAt(aim);
                    }
                    camera.Render();var prior=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(520,520,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,520,520),0,0);image.Apply();File.WriteAllBytes(folder+"/"+name+"-"+i.ToString("D2")+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=prior;
                }
                report.AppendLine(name+" | "+clip.length.ToString("F3")+"s | humanoid="+clip.isHumanMotion+" | "+path);
            }
            File.WriteAllText(folder+"/sources.txt",report.ToString());Debug.Log("CLIMB_MOTION_REVIEW_RENDERED");
        }finally{
            foreach(var skin in meshes)if(skin.Item2)UnityEngine.Object.DestroyImmediate(skin.Item2);
            if(wallMaterial)UnityEngine.Object.DestroyImmediate(wallMaterial);if(treeMaterial)UnityEngine.Object.DestroyImmediate(treeMaterial);if(actor)UnityEngine.Object.DestroyImmediate(actor);if(carrier)UnityEngine.Object.DestroyImmediate(carrier);if(library)UnityEngine.Object.DestroyImmediate(library);if(cameraObject)UnityEngine.Object.DestroyImmediate(cameraObject);if(lightObject)UnityEngine.Object.DestroyImmediate(lightObject);if(rt)UnityEngine.Object.DestroyImmediate(rt);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
