using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
[InitializeOnLoad]
public static class ResponsiveLedgeCalibration
{
    static ResponsiveLedgeCalibration(){EditorApplication.update+=()=>{if(!File.Exists("Temp/ledge-calibrate.txt")||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isUpdating||EditorApplication.isCompiling)return;File.Delete("Temp/ledge-calibrate.txt");Run();};}
    static string V(Vector3 p)=>p.x.ToString("R",CultureInfo.InvariantCulture)+","+p.y.ToString("R",CultureInfo.InvariantCulture)+","+p.z.ToString("R",CultureInfo.InvariantCulture);
    static void Run()
    {
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var actor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldHero/RedHeroAuthoredVisual.prefab"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor,scene);
        var pose=actor.GetComponentInChildren<HeroPoseDriver>();var library=UnityEngine.Object.Instantiate(pose.library);pose.library=library;var report=new StringBuilder();
        try{
            foreach(var name in new[]{"WallGrab","WallHold","ClimbOver"}){
                var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/WorldHero/ClimbReview/Source/"+name+".FBX").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
                var d=library.Find("ATK_A1");d.clip=clip;d.authoredMotion=true;d.authoredOffset=0;d.authoredRate=1;d.authoredFootIK=false;d.authoredClimbRoot=null;d.duration=clip.length;d.loop=false;
                int count=Mathf.CeilToInt(clip.length*60)+1;var hips=new Vector3[count];var hands=new Vector3[count];var feet=new Vector3[count];pose.ResetPresentation();
                for(int i=0;i<count;i++){
                    pose.Sample(d.id,clip.length*i/(count-1),.016f,false,0);
                    Vector3 P(HumanBodyBones b)=>pose.animator.transform.InverseTransformPoint(pose.animator.GetBoneTransform(b).position);
                    hips[i]=P(HumanBodyBones.Hips);hands[i]=(P(HumanBodyBones.LeftHand)+P(HumanBodyBones.RightHand))*.5f;feet[i]=(P(HumanBodyBones.LeftFoot)+P(HumanBodyBones.RightFoot))*.5f;
                }
                bool top=name=="ClimbOver";int anchor=top?0:count-1;
                var begin=hips[anchor]+new Vector3(-hands[anchor].x,1.68f-hands[anchor].y,.43f-hands[anchor].z);
                var end=top?hips[count-1]-new Vector3(feet[count-1].x,feet[count-1].y-.08f,feet[count-1].z):begin;
                var data=new StringBuilder("HCR1\n");
                for(int i=0;i<count;i++){
                    float u=(float)i/(count-1);var baseline=Vector3.Lerp(begin,end,Mathf.SmoothStep(0,1,u));
                    data.AppendLine((clip.length*u).ToString("R",CultureInfo.InvariantCulture)+","+V(baseline-hips[i]));
                }
                string folder="Assets/WorldHero/ClimbReview/Calibrated";Directory.CreateDirectory(folder);File.WriteAllText(folder+"/"+name+"Root.csv",data.ToString());
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip,out string guid,out long id);report.AppendLine(name+"|"+guid+"|"+id+"|"+clip.length.ToString("R",CultureInfo.InvariantCulture)+"|startHip="+V(begin)+"|endHip="+V(end));
            }
            Directory.CreateDirectory("LocalDocs/ClimbReview");File.WriteAllText("LocalDocs/ClimbReview/ledge-calibration.txt",report.ToString());Debug.Log("LEDGE_CALIBRATION_COMPLETE");
        }finally{UnityEngine.Object.DestroyImmediate(actor);UnityEngine.Object.DestroyImmediate(library);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
    }
}
