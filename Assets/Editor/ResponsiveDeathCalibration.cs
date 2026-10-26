using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
[InitializeOnLoad]
public static class ResponsiveDeathCalibration
{
    static ResponsiveDeathCalibration(){EditorApplication.update+=()=>{if(File.Exists("Temp/death-calibrate.txt")&&!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating){File.Delete("Temp/death-calibrate.txt");Run();}};}
    static void Run(){
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldHero/RedHeroAuthoredVisual.prefab"));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor,scene);
        var pose=actor.GetComponentInChildren<HeroPoseDriver>();var library=Object.Instantiate(pose.library);pose.library=library;
        var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/WorldHero/ResponsiveSword/DeathCandidates/StandDeath.FBX").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
        var d=library.Find("DEATH_GROUND");d.clip=clip;d.authoredFootIK=false;d.authoredOffset=0;d.authoredRate=1;d.duration=clip.length;d.supportCompensationClip=null;d.loop=false;
        var skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.enabled&&s.gameObject.activeInHierarchy).ToArray();var mesh=new Mesh();var log=new StringBuilder();
        try{for(int i=0;i<=148;i++){float t=clip.length*i/148;pose.Sample("DEATH_GROUND",t,.02f,false,0);float min=float.PositiveInfinity;foreach(var skin in skins){skin.BakeMesh(mesh);foreach(var v in mesh.vertices)min=Mathf.Min(min,skin.transform.TransformPoint(v).y-actor.transform.position.y);}log.AppendLine(t.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+min.ToString("R",System.Globalization.CultureInfo.InvariantCulture));}Directory.CreateDirectory("LocalDocs/ResponsiveSword/DeathReview");File.WriteAllText("LocalDocs/ResponsiveSword/DeathReview/support.csv",log.ToString());}
        finally{Object.DestroyImmediate(mesh);Object.DestroyImmediate(actor);Object.DestroyImmediate(library);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
    }
}
