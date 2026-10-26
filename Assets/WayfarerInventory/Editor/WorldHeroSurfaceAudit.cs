using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WayfarerInventory.Editor
{
    public static class WorldHeroSurfaceAudit
    {
        const string Folder="tmp/inventory/hero-expansion";
        public static void Run()
        {
            if(!Application.isPlaying)throw new Exception("Run in main world Play Mode");
            Directory.CreateDirectory(Folder);
            var world=Object.FindFirstObjectByType<BaselineWorld>();var player=world.player;
            var movement=player.GetComponent<PlayerMovement>();
            if(!world.NavigationGround(player.position+Vector3.up,out var floor))throw new Exception("No actual world ground");
            var clone=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldHero/P09PlayerVisual.prefab"));
            clone.name="Temporary main-map pose audit";clone.transform.position=floor+player.right*2;
            if(!world.NavigationGround(clone.transform.position+Vector3.up,out floor))throw new Exception("No test platform");
            clone.transform.position=floor;clone.transform.rotation=Quaternion.identity;
            var a=clone.GetComponentInChildren<Animator>();a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();a.Update(0);
            foreach(var skin in clone.GetComponentsInChildren<SkinnedMeshRenderer>())skin.updateWhenOffscreen=true;
            foreach(var b in clone.GetComponentsInChildren<MonoBehaviour>())b.enabled=false;
            foreach(var t in clone.GetComponentsInChildren<Transform>(true))if(t.name=="PracticeSword")t.gameObject.SetActive(false);
            var cameraObject=new GameObject("Temporary pose audit camera");var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(Camera.main);
            camera.enabled=false;camera.fieldOfView=38;camera.nearClipPlane=.05f;camera.farClipPlane=150;
            camera.transform.position=floor+new Vector3(2.6f,1.5f,3.6f);camera.transform.LookAt(floor+Vector3.up*.85f);
            var rt=new RenderTexture(480,480,24);camera.targetTexture=rt;
            var report=new StringBuilder("action,sample,minBodyAboveGround_m,maxBodyAboveGround_m\n");
            string[] names={"Death","DrawSword","SheatheSword","OpenChest","Pickup","Touch","RestBegin","RestLoop","RestEnd","Talk","HeavyLand","WalkStart","WalkStop","RunStart","RunStop","Sprint"};
            var sheet=new Texture2D(1440,1920,TextureFormat.RGB24,false);int row=0,page=0;
            var frozen=new System.Collections.Generic.List<GameObject>();
            try
            {
                foreach(var name in names)
                {
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/WorldHero/Expansion/"+name+".anim");
                    for(int i=0;i<3;i++)
                    {
                        foreach(var baked in frozen){Object.DestroyImmediate(baked.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(baked);}frozen.Clear();
                        float fraction=new[]{.15f,.5f,.95f}[i];a.Play(name,0,fraction);a.Update(0);
                        foreach(var constraint in clone.GetComponentsInChildren<UnityEngine.Animations.ParentConstraint>())
                        {
                            if(constraint.sourceCount!=1)continue;
                            var source=constraint.GetSource(0).sourceTransform;if(!source)continue;
                            constraint.transform.SetPositionAndRotation(source.TransformPoint(constraint.GetTranslationOffset(0)),source.rotation*Quaternion.Euler(constraint.GetRotationOffset(0)));
                        }
                        float min=float.PositiveInfinity,max=float.NegativeInfinity;
                        foreach(var skin in clone.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            var mesh=new Mesh();skin.BakeMesh(mesh);
                            foreach(var v in mesh.vertices){float y=skin.transform.TransformPoint(v).y-floor.y;min=Mathf.Min(min,y);max=Mathf.Max(max,y);}
                            var baked=new GameObject("Evaluated skin");baked.transform.SetParent(skin.transform,false);
                            baked.AddComponent<MeshFilter>().sharedMesh=mesh;baked.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                            skin.enabled=false;frozen.Add(baked);
                        }
                        report.AppendLine(name+","+fraction+","+min.ToString("F4")+","+max.ToString("F4"));
                        camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
                        var image=new Texture2D(480,480,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,480,480),0,0);image.Apply();
                        sheet.SetPixels(i*480,(3-row)*480,480,480,image.GetPixels());Object.DestroyImmediate(image);RenderTexture.active=previous;
                    }
                    row++;
                    if(row==4){sheet.Apply();File.WriteAllBytes(Folder+"/ground-"+(++page)+".png",sheet.EncodeToPNG());row=0;}
                }
                File.WriteAllText(Folder+"/ground-contact.csv",report.ToString());
                File.WriteAllText(Folder+"/ground-sheets.txt","Each row is one action; columns 15%, 50%, 95%.\n"+string.Join("\n",names));
            }
            finally{Object.DestroyImmediate(sheet);camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(clone);}
            Debug.Log("Ground pose audit written: "+Folder);
        }
    }
}
