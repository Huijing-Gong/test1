using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;

// Opt-in integration exercise in the existing observer's private save scope.
[InitializeOnLoad]
public static class ResponsiveSwordPlayChecks
{
    const string Flag="ResponsiveSword.PlayCheck";
    const string Request="Temp/responsive-sword-playcheck.txt";
    static HeroWorldAdapter hero;static BaselineFlow flow;static Health dummy;
    static Func<bool,HeroCombatInput> previousReader;
    static bool attack,dodge,held,focusReady,focusReturning; static int focusStrikes;static int phase;static double since,deadline;static Vector3 before;
    static readonly HashSet<string> seen=new HashSet<string>();
    static readonly Dictionary<long,int> contacts=new Dictionary<long,int>();
    static ResponsiveSwordPlayChecks(){
        EditorApplication.update+=Watch;
        EditorApplication.playModeStateChanged+=state=>{
            if(state==PlayModeStateChange.ExitingPlayMode&&SessionState.GetBool(Flag,false))Finish(false,"Verification interrupted; test input restored.");
        };
    }
    [MenuItem("Tools/Hero/Play and verify responsive sword")]
    public static void Begin()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first.");
        SessionState.SetBool(Flag,true);SessionState.SetBool("HeroQA.Enabled",true);EditorApplication.isPaused=false;
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Demo/Scenes/OverworldBaseline.unity");
        phase=0;hero=null;flow=null;seen.Clear();contacts.Clear();deadline=EditorApplication.timeSinceStartup+80;
        EditorApplication.isPlaying=true;
    }
    static void Finish(bool success,string details)
    {
        HeroCombatInput.GameplayReader=previousReader;SessionState.SetBool(Flag,false);SessionState.SetBool("HeroQA.Enabled",false);
        File.WriteAllText("LocalDocs/ResponsiveSword/play-validation.txt",(success?"PASS ":"FAIL ")+details+"\nActions: "+string.Join(", ",seen)+"\nDamage sequences: "+contacts.Count+"\nPer-sequence contacts: "+string.Join(", ",contacts));
        if(success)Debug.Log("RESPONSIVE_SWORD_PLAY_PASS "+details);else Debug.LogWarning("RESPONSIVE_SWORD_PLAY_FAIL "+details);
    }
    static void Watch()
    {
        if(File.Exists(Request)&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode){File.Delete(Request);Begin();}
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
        if(deadline==0)deadline=EditorApplication.timeSinceStartup+80;
        try {
            if(EditorApplication.timeSinceStartup>deadline){Finish(false,"Timed out in phase "+phase);return;}
            if(!flow)flow=UnityEngine.Object.FindFirstObjectByType<BaselineFlow>();if(!flow)return;
            if(!flow.saveKeyPrefix.StartsWith("QA.NativePlay.",StringComparison.Ordinal)){Finish(false,"Private save scope was not installed");return;}
            if(!flow.Started){if(flow.TitleScreenVisible&&!flow.TitleTransitionInProgress)flow.RequestTitleEntry(false);return;}
            if(!hero)hero=UnityEngine.Object.FindFirstObjectByType<HeroWorldAdapter>();if(!hero)return;
            if(!string.IsNullOrEmpty(hero.CurrentAction))seen.Add(hero.CurrentAction);
            if(phase==0){
                if(flow.BlockWorldInput||!hero.GetComponent<PlayerMovement>().IsStableGrounded)return;
                Cursor.lockState=CursorLockMode.Locked;GameSession.Instance.GrantSword();GameSession.Instance.SetSwordEquipped(true);
                hero.GetComponent<PlayerEquipment>().DrawForAttack();
                if(!hero.CanAttack||!hero.TryAttack())return;
                var go=GameObject.CreatePrimitive(PrimitiveType.Capsule);go.name="Responsive sword practice target (runtime only)";
                go.transform.position=hero.transform.position+hero.transform.forward*1.4f+Vector3.up;
                dummy=go.AddComponent<Health>();dummy.SetMaximumHealth(1000);
                dummy.Damaged+=(h,d)=>{long seq=hero.combat.Sequence;contacts[seq]=contacts.ContainsKey(seq)?contacts[seq]+1:1;};
                previousReader=HeroCombatInput.GameplayReader;
                HeroCombatInput.GameplayReader=allowed=>{var value=new HeroCombatInput{attack=allowed&&attack,dodge=allowed&&dodge,released=allowed&&attack,held=allowed&&held};attack=dodge=false;return value;};
                phase=1;since=EditorApplication.timeSinceStartup;return;
            }
            if(phase==1&&hero.combat.Id=="ATK_A1"&&hero.combat.Time>.15f){attack=true;phase=2;return;}
            if(phase==2&&hero.combat.Id=="ATK_A2"&&hero.combat.Time>.10f){attack=true;phase=3;return;}
            if(phase==3&&hero.combat.Id=="ATK_A3")phase=4;
            if(phase==4&&!hero.Attacking){
                if(!seen.Contains("ATK_A1")||!seen.Contains("ATK_A2")||!seen.Contains("ATK_A3"))throw new Exception("Three-cut combo was not presented");
                foreach(var count in contacts.Values)if(count>1)throw new Exception("A swing damaged the target more than once");
                if(contacts.Count==0)throw new Exception("Visible blade never contacted the practice target");
                before=hero.transform.position;dodge=true;phase=5;return;
            }
            if(phase==5&&hero.Dodging){phase=6;since=EditorApplication.timeSinceStartup;return;}
            if(phase==6&&!hero.Dodging){
                float distance=Vector3.ProjectOnPlane(hero.transform.position-before,Vector3.up).magnitude;
                if(distance>2.45f)throw new Exception("Dodge exceeded collision-controlled travel: "+distance);
                if(hero.OwnsAction)throw new Exception("Dodge retained movement ownership after recovery");
                phase=7;since=EditorApplication.timeSinceStartup;return;
            }
            if(phase==7&&EditorApplication.timeSinceStartup-since>.55){dodge=true;phase=8;return;}
            if(phase==8&&hero.Dodging){since=EditorApplication.timeSinceStartup;phase=9;return;}
            if(phase==9&&EditorApplication.timeSinceStartup-since>.18){attack=true;phase=10;return;}
            if(phase==10&&hero.Attacking){
                if(hero.Dodging)throw new Exception("Attack and dodge owned the capsule simultaneously");
                phase=11;since=EditorApplication.timeSinceStartup;
            }
            if(phase==11&&!hero.Attacking){
                var platform=GameObject.CreatePrimitive(PrimitiveType.Cube);platform.name="Focus QA flat floor (runtime only)";
                platform.transform.position=hero.transform.position+Vector3.up*3;platform.transform.localScale=new Vector3(16,1,16);
                hero.GetComponent<PlayerMovement>().Relocate(platform.transform.position+Vector3.up*.55f,hero.transform.rotation);
                phase=111;return;
            }
            if(phase==111&&hero.GetComponent<PlayerMovement>().IsStableGrounded){
                typeof(Health).GetField("team",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(dummy,HealthTeam.Enemy);
                dummy.transform.position=hero.transform.position+hero.transform.forward*3+Vector3.up;
                Physics.SyncTransforms();before=hero.transform.position;
                hero.GetComponent<PlayerBattleActions>().QualifyAuthoredPerfectDodge(dummy.gameObject);
                if(!hero.FocusSequenceActive||Mathf.Abs(Time.timeScale-.16f)>.001f)throw new Exception("World focus did not begin at 0.16x");
                held=true;phase=12;since=EditorApplication.timeSinceStartup;
            }
            if(phase==12){
                focusReady|=hero.FocusPromptReady;focusReturning|=hero.FocusPhase==HeroFocusPhase.Return;
                focusStrikes=Mathf.Max(focusStrikes,hero.FocusStrikeCount);
                if(hero.FocusSequenceActive)return;
                held=false;
                if(!focusReady||!focusReturning||focusStrikes!=6)throw new Exception("World focus incomplete: "+focusReady+" / "+focusReturning+" strikes "+focusStrikes);
                float error=Vector3.ProjectOnPlane(hero.transform.position-before,Vector3.up).magnitude;
                if(error>.16f||Mathf.Abs(Time.timeScale-1)>.001f)throw new Exception("World return/speed restore: "+error+" / "+Time.timeScale);
                phase=13;since=EditorApplication.timeSinceStartup;
            }
            if(phase==13&&EditorApplication.timeSinceStartup-since>.85){
                var pose=hero.GetComponentInChildren<HeroPoseDriver>();
                if(hero.GetComponent<PlayerEquipment>().SwordDrawn||!pose.BackSwordVisible)throw new Exception("World idle sword did not return to back");
                if(File.Exists("Temp/world-death-mode.txt")){
                    File.Delete("Temp/world-death-mode.txt");
                    if(!hero.GetComponent<Health>().TryApplyDamage(new DamageInfo(10000,dummy.gameObject,hero.transform.position,Vector3.zero)))throw new Exception("Controlled lethal damage rejected");
                    phase=14;since=EditorApplication.timeSinceStartup;return;
                }
                Finish(true,"Combo and damage deduplication, dodge unlock. Link time: six held-LMB attacks, exact return, speed restored. Idle sword visibly returns to back. Private save unchanged.");
            }
            if(phase==14&&EditorApplication.timeSinceStartup-since>2.2){
                var pose=hero.GetComponentInChildren<HeroPoseDriver>();
                if(!hero.DeathAnimationFinished||pose.CurrentAction!="DEATH_HOLD")throw new Exception("World death did not settle: "+pose.CurrentAction+" ready="+hero.DeathAnimationFinished);
                float min=float.PositiveInfinity;var mesh=new Mesh();
                foreach(var skin in pose.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(skin.enabled&&skin.gameObject.activeInHierarchy){skin.BakeMesh(mesh);foreach(var vertex in mesh.vertices)min=Mathf.Min(min,skin.transform.TransformPoint(vertex).y);}
                UnityEngine.Object.DestroyImmediate(mesh);float floor=float.NegativeInfinity;
                foreach(var hit in Physics.RaycastAll(hero.transform.position+Vector3.up*.5f,Vector3.down,2,~0,QueryTriggerInteraction.Ignore))if(!hit.transform.IsChildOf(hero.transform)&&hit.normal.y>.55f)floor=Mathf.Max(floor,hit.point.y);
                float gap=min-floor;if(gap>.04f||gap<-.03f)throw new Exception("World corpse gap "+gap);
                Finish(true,"World lethal damage completed grounded DEATH_HOLD, corpse gap="+gap+"m, death UI ready. Combat and focus checks also passed; private save scope retained.");
            }
        }catch(Exception e){Finish(false,e.ToString());}
    }
}
