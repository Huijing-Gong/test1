using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
using SamProduction.BossArena;

[InitializeOnLoad]
public static class ResponsiveDeathPlayChecks
{
    static ResponsiveDeathPlayChecks(){EditorApplication.update+=()=>{
        if(File.Exists("Temp/death-stop.txt")){File.Delete("Temp/death-stop.txt");EditorApplication.isPlaying=false;return;}
        if(!File.Exists("Temp/death-play-check.txt")||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlaying){UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/SAM/BossArena/Scenes/SAM_BossArena.unity");EditorApplication.isPlaying=true;return;}
        var a=Object.FindFirstObjectByType<ArenaSession>();if(!a||!a.player.Hero)return;
        File.Delete("Temp/death-play-check.txt");new GameObject("Opt-in actual hero death checks").AddComponent<ResponsiveDeathPlayRunner>();
    };}
}
public sealed class ResponsiveDeathPlayRunner:MonoBehaviour
{
    IEnumerator Start(){
        yield return new WaitForSecondsRealtime(1);
        var a=Object.FindFirstObjectByType<ArenaSession>();bool auto=a.automaticTick,decisions=a.boss.decisionEnabled;
        string folder="LocalDocs/ResponsiveSword/DeathFix/ground";Directory.CreateDirectory(folder);string result="FAIL interrupted";
        var mesh=new Mesh();var go=new GameObject("Death review camera");var camera=go.AddComponent<Camera>();camera.fieldOfView=43;camera.cullingMask=~(1<<5);var rt=new RenderTexture(800,600,24);camera.targetTexture=rt;
        try{
            a.automaticTick=false;a.boss.decisionEnabled=false;
            string rows="";
            for(int scenario=0;scenario<2;scenario++){
                a.Restart(true);a.Simulate(default,.2f);
                if(scenario==1)a.player.ResetPlayer(a.playerStart+Vector3.up*3,Quaternion.identity,false);
                Physics.SyncTransforms();a.player.fighter.Receive(new ArenaHit{source=a.boss.fighter,target=a.player.fighter,strike=a.NewStrike(),damage=a.player.fighter.maximumHealth*2,push=0,skill="qa_lethal_hit"});
                int frame=0;bool sawAir=false,sawGround=false;float maxHoldGap=0;float minHoldGap=10;
                for(int step=0;step<105;step++){
                    a.Simulate(default,1f/30);
                    var pose=a.player.Hero;sawAir|=pose.CurrentAction=="DEATH_AIR";sawGround|=pose.CurrentAction=="DEATH_GROUND";
                    yield return new WaitForEndOfFrame();
                    float min=float.PositiveInfinity;
                    foreach(var skin in pose.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(skin.enabled&&skin.gameObject.activeInHierarchy){skin.BakeMesh(mesh);foreach(var vertex in mesh.vertices)min=Mathf.Min(min,skin.transform.TransformPoint(vertex).y);}
                    var cc=a.player.GetComponent<CharacterController>();float floor=0;
                    foreach(var hit in Physics.RaycastAll(a.player.transform.position+Vector3.up*.5f,Vector3.down,2,~0,QueryTriggerInteraction.Ignore))if(!hit.transform.IsChildOf(a.player.transform)&&hit.normal.y>.55f)floor=Mathf.Max(floor,hit.point.y);
                    if(pose.CurrentAction=="DEATH_HOLD"){maxHoldGap=Mathf.Max(maxHoldGap,min-floor);minHoldGap=Mathf.Min(minHoldGap,min-floor);}
                    if(step%2==0){
                        rows+=$"scenario={scenario}, action={pose.CurrentAction}, t={pose.CurrentTime:F3}, root={a.player.transform.position.y:F3}, skinGap={min-floor:F4}, offset={pose.DeathGroundOffset:F4}\n";
                        if(scenario==0){camera.transform.position=a.player.transform.position+new Vector3(2.7f,1.6f,2.6f);camera.transform.LookAt(a.player.transform.position+new Vector3(0,.5f,.4f));camera.Render();var prior=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(800,600,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();File.WriteAllBytes(folder+"/frame-"+(frame++).ToString("D3")+".png",image.EncodeToPNG());Object.Destroy(image);RenderTexture.active=prior;}
                        
                    }
                    if(pose.CurrentAction=="DEATH_HOLD"&&pose.CurrentTime>.4f)break;
                }
                if(!sawGround||scenario==1&&!sawAir||a.player.Hero.CurrentAction!="DEATH_HOLD"||maxHoldGap>.035f||minHoldGap<-.025f||!a.player.GetComponent<ArenaCurrentCharacter>().DeathPresentationComplete)throw new System.Exception($"scenario={scenario}, ground={sawGround}, air={sawAir}, holdGap={minHoldGap}..{maxHoldGap}, final={a.player.Hero.CurrentAction}");
                rows+=$"PASS scenario={scenario}, grounded corpse gap={minHoldGap:F4}..{maxHoldGap:F4}m, death UI ready\n";
            }
            result="PASS actual lethal damage, ground collapse, airborne gravity then collapse, stable grounded hold and UI completion\n"+rows;
        }finally{
            File.WriteAllText(folder+"/validation.txt",result);Debug.Log(result);camera.targetTexture=null;rt.Release();Object.Destroy(rt);Object.Destroy(go);Object.Destroy(mesh);a.Restart(false);a.automaticTick=auto;a.boss.decisionEnabled=decisions;
        }
    }
}
