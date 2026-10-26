using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class DemonKingTrialInstaller
{
    public const string ScenePath="Assets/_Demo/Scenes/OverworldBaseline.unity";
    public const string Folder="Assets/Art/Enemies/DemonKingTrial";
    public const string Output="LocalDocs/Enemies/DemonKingTrial-20261020";
    public const string ActorName="StoneScout_Forest_01";
    public const string Source="Assets/RPGMonsterWave03PBR";
    public static AnimationClip Clip(string name)=>AssetDatabase.LoadAllAssetsAtPath(Source+"/Animation/DemonKing/DemonKing_"+name+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
    public static void Regression()
    {
        EditorSceneManager.OpenScene(ScenePath);
        StoneScoutCombatPrototypeChecks.Run();
        RepairWispChecks.Run();
        File.AppendAllText(Output+"/validation.txt","PASS original StoneScout combat prefab and scene placement regression; PASS RepairWisp authoring regression.\n");
    }
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first.");
        Directory.CreateDirectory(Folder);Directory.CreateDirectory(Output);AssetDatabase.Refresh();
        if(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/DemonKingScoutTrial.prefab"))throw new InvalidOperationException("Trial already installed.");
        var controller=AnimatorController.CreateAnimatorControllerAtPath(Folder+"/DemonKingScout.controller");
        foreach(string name in new[]{"Idle","Walk","Attack","Hit","Death"})
        {
            string source=name=="Idle"?"IdleNormal":name=="Walk"?"WalkFWD":name=="Attack"?"Attack01":name=="Hit"?"GetHit":"Die";
            var clip=Object.Instantiate(Clip(source));clip.name="DemonKing_"+name;
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=name=="Idle"||name=="Walk";settings.loopBlend=settings.loopTime;AnimationUtility.SetAnimationClipSettings(clip,settings);
            AssetDatabase.CreateAsset(clip,Folder+"/"+name+".anim");
            var state=controller.layers[0].stateMachine.AddState(name);state.motion=clip;
            state.speed=name=="Attack"?clip.length/1.5f:name=="Hit"?clip.length/.42f:1;
            if(name=="Idle")controller.layers[0].stateMachine.defaultState=state;
        }
        var sourceMaterial=AssetDatabase.LoadAssetAtPath<Material>(Source+"/Material/PBR_Default.mat");
        var armour=new Material(sourceMaterial){name="DemonKing dark armour"};armour.SetColor("_EmissionColor",Color.white*.65f);armour.SetFloat("_Smoothness",.3f);AssetDatabase.CreateAsset(armour,Folder+"/Armour.mat");
        var weapon=new Material(armour){name="DemonKing charge blade"};weapon.SetTexture("_EmissionMap",null);weapon.SetColor("_EmissionColor",Color.black);weapon.EnableKeyword("_EMISSION");AssetDatabase.CreateAsset(weapon,Folder+"/Blade.mat");
        var energy=new Material(Resources.Load<Shader>("EnemyAttackEnergy")){name="DemonKing red short wake"};energy.SetColor("_Tint",new Color(1,.12f,.045f));AssetDatabase.CreateAsset(energy,Folder+"/BladeWake.mat");
        var hit=new Material(Resources.Load<Shader>("EnemyLocalImpact")){name="DemonKing local hit flash"};AssetDatabase.CreateAsset(hit,Folder+"/LocalHit.mat");
        DemonKingPaletteTools.ApplyMaterials();
        var scene=EditorSceneManager.OpenScene(ScenePath);var world=Object.FindFirstObjectByType<BaselineWorld>();
        var actor=Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None).Single(a=>a.name==ActorName).gameObject;
        Vector3 before=actor.transform.position;
        var sourcePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Source+"/Prefab/Characters/DemonKingPBR.prefab");
        var go=(GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab,actor.transform);go.name="DemonKing combat visual";go.transform.SetAsFirstSibling();go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;
        var old=actor.GetComponentsInChildren<StoneScoutVisualMotion>(true).FirstOrDefault();if(old){old.gameObject.SetActive(false);old.gameObject.name="Original stone visual - disabled trial backup";}
        var animator=go.GetComponentInChildren<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        Clip("IdleNormal").SampleAnimation(go,0);
        var skin=go.GetComponentInChildren<SkinnedMeshRenderer>();skin.sharedMaterial=armour;
        var playerBody=world.player.GetComponent<CharacterController>();float height=Mathf.Clamp(playerBody.height*world.player.lossyScale.y*1.3f,2.1f,2.6f);
        go.transform.localScale*=height/skin.bounds.size.y;Physics.SyncTransforms();
        go.transform.position+=Vector3.up*(actor.transform.position.y+.01f-skin.bounds.min.y);
        var blade=go.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="DemonKingBlade");blade.GetComponent<Renderer>().sharedMaterial=weapon;
        Vector3 tip=blade.sharedMesh.vertices.OrderByDescending(v=>v.sqrMagnitude).First();
        var visual=go.AddComponent<DemonKingCombatVisual>();visual.pose=animator;visual.blade=blade.transform;visual.bladeTipLocal=tip;visual.bladeBaseLocal=tip*.08f;
        visual.bodySkin=skin;visual.weaponRenderer=blade.GetComponent<Renderer>();visual.chest=go.GetComponentsInChildren<Transform>().First(t=>t.name=="demonKing_spine03");visual.energyMaterial=energy;visual.hitMaterial=hit;
        var capsule=actor.GetComponent<CapsuleCollider>();Undo.RecordObject(capsule,"Fit DemonKing body");capsule.height=height/actor.transform.lossyScale.y;capsule.center=Vector3.up*(capsule.height*.5f);capsule.radius=.48f/Mathf.Max(actor.transform.lossyScale.x,actor.transform.lossyScale.z);
        var ai=new SerializedObject(actor.GetComponent<StoneScoutRoadsideAI>());ai.FindProperty("attackRange").floatValue=2.1f;ai.FindProperty("windupSeconds").floatValue=.66f;ai.FindProperty("hitWindowSeconds").floatValue=.2f;ai.FindProperty("recoverSeconds").floatValue=.85f;ai.FindProperty("staggerSeconds").floatValue=.42f;ai.ApplyModifiedPropertiesWithoutUndo();
        var feedback=new SerializedObject(actor.GetComponent<CombatFeedback>());var renderers=go.GetComponentsInChildren<Renderer>();var list=feedback.FindProperty("targetRenderers");list.arraySize=renderers.Length;for(int i=0;i<renderers.Length;i++)list.GetArrayElementAtIndex(i).objectReferenceValue=renderers[i];feedback.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(capsule);PrefabUtility.RecordPrefabInstancePropertyModifications(actor.GetComponent<StoneScoutRoadsideAI>());PrefabUtility.RecordPrefabInstancePropertyModifications(actor.GetComponent<CombatFeedback>());
        var trial=Object.Instantiate(actor);trial.name="DemonKingScoutTrial";trial.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);PrefabUtility.SaveAsPrefabAsset(trial,Folder+"/DemonKingScoutTrial.prefab");Object.DestroyImmediate(trial);
        if(actor.transform.position!=before)throw new InvalidOperationException("Actor home moved.");
        var scouts=Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None);if(scouts.Length!=6 || Object.FindObjectsByType<DemonKingCombatVisual>(FindObjectsSortMode.None).Length!=1)throw new InvalidOperationException("Expected one DemonKing among six existing Scouts.");
        Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save trial scene.");
        File.WriteAllText(Output+"/installed.txt","Single trial actor: "+ActorName+" home="+before+" height="+height+"; health/damage/loot unchanged. Attack01 uses .66s windup, .20s actual blade contact, .85s stationary recovery.\n");
        Debug.Log("DEMON_TRIAL_INSTALLED "+before+" height="+height);
    }
    public static void Audit()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Directory.CreateDirectory(Output);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Source+"/Prefab/Characters/DemonKingPBR.prefab");
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);var blade=go.GetComponentsInChildren<MeshFilter>().First(r=>r.name=="DemonKingBlade");
        var bone=blade.transform;var vertices=blade.sharedMesh.vertices;
        var tip=vertices.OrderByDescending(v=>v.sqrMagnitude).First();
        File.WriteAllText(Output+"/source-audit.csv","clip,t,bladeRootX,bladeRootY,bladeRootZ,tipX,tipY,tipZ\n");
        foreach(string action in new[]{"Attack01","Attack02","GetHit","Die"})
        {
            var clip=Clip(action);Debug.Log("DEMON_CLIP "+action+" length="+clip.length+" bladeBone="+bone.name+" tipLocal="+tip);
            for(int i=0;i<=20;i++)
            {
                clip.SampleAnimation(go,i/20f*clip.length);Vector3 p=bone.TransformPoint(tip),q=bone.position;
                File.AppendAllText(Output+"/source-audit.csv",FormattableString.Invariant($"{action},{i/20f:F2},{q.x:F3},{q.y:F3},{q.z:F3},{p.x:F3},{p.y:F3},{p.z:F3}\n"));
            }
        }
        Object.DestroyImmediate(go);
    }
}
