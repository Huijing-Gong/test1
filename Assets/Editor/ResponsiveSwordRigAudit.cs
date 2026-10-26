using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
public static class ResponsiveSwordRigAudit {
 [MenuItem("Tools/Hero/Audit red hero rig")]
 public static void Run(){if(EditorApplication.isPlaying)return;
 var a=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldHero/RedHeroAuthoredVisual.prefab"));
 try{var p=a.GetComponent<HeroPoseDriver>();var log=new StringBuilder();var anim=p.animator;anim.Rebind();
 foreach(var part in new[]{HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.RightHand})log.AppendLine(part+"="+AnimationUtility.CalculateTransformPath(anim.GetBoneTransform(part),a.transform));
 foreach(var skin in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(skin.enabled)log.AppendLine("SKIN "+skin.name+" hasHips="+skin.bones.Contains(anim.GetBoneTransform(HumanBodyBones.Hips))+" root="+(skin.rootBone?AnimationUtility.CalculateTransformPath(skin.rootBone,a.transform):"null"));
 File.WriteAllText("LocalDocs/ResponsiveSword/rig-audit.txt",log.ToString());
 }finally{Object.DestroyImmediate(a);}}
}
