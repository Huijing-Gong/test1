using System;
using System.Linq;
using KingOfGraphic.MechBossMigration;
using SamProduction.BossArena;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StoneBossCombatRevisionSetup
{
    public const string TrialController="Assets/WorldHero/BossCombatTrial/WorldHeroBossTrial.controller";
    public static string Install()
    {
        if(Application.isPlaying)throw new Exception("Use edit mode.");
        var encounter=UnityEngine.Object.FindFirstObjectByType<MechBossEncounter>();
        var player=encounter.player;
        var combatSettings=new SerializedObject(player.GetComponent<PlayerCombat>());
        combatSettings.FindProperty("allowHeavyAttack").boolValue=false;
        combatSettings.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.GetComponent<PlayerCombat>());
        var scene=EditorSceneManager.OpenScene("Assets/SAM/BossArena/Scenes/SAM_BossArena.unity",OpenSceneMode.Additive);
        Material orange=null,cyan=null,warning=null;
        try {
            var roots=scene.GetRootGameObjects();
            var fx=roots.SelectMany(x=>x.GetComponentsInChildren<ArenaCombatFx>(true)).First();
            orange=fx.orange;cyan=fx.cyan;warning=fx.warning;
        } finally {EditorSceneManager.CloseScene(scene,true);}
        var feedback=encounter.boss.GetComponent<MistGardenBossFeedback>();
        Undo.RecordObject(feedback,"Reuse final room hit presentation");
        feedback.chestHitEffect=null;feedback.hitEffect=null;feedback.contactFeedback=null;feedback.externalHitVisuals=true;
        var previousContact=encounter.boss.GetComponent<ArenaCombatFx>();if(previousContact)Undo.DestroyObjectImmediate(previousContact);
        if(!encounter.boss.GetComponent<WardenContactPresentation>())Undo.AddComponent<WardenContactPresentation>(encounter.boss.gameObject);
        StoneBossIslandSetup.ConfigureShoreline(encounter,encounter.boss.GetComponent<MechIslandGroundConstraint>());

        if(!AssetDatabase.LoadAssetAtPath<AnimatorController>(TrialController))
            AssetDatabase.CopyAsset("Assets/WorldHero/XBotLocomotion.controller",TrialController);
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(TrialController);
        var sm=controller.layers[0].stateMachine;
        if(!controller.parameters.Any(p=>p.name=="CombatStepRate"))controller.AddParameter(new AnimatorControllerParameter{name="CombatStepRate",type=AnimatorControllerParameterType.Float,defaultFloat=1});
        foreach(var state in sm.states.Where(s=>s.state.name=="WalkBack"||s.state.name=="StrafeLeft"||s.state.name=="StrafeRight")){state.state.speedParameter="CombatStepRate";state.state.speedParameterActive=true;}
        Grounded(sm,"DodgeBack","Assets/WorldHero/Expansion/WalkBack.anim","GroundedBackStep",1.6f,true);
        Grounded(sm,"DodgeForward","Assets/AuthorMotions/Clips/Core/Run.anim","GroundedAdvance",1.4f,true);
        Grounded(sm,"BattleRush","Assets/AuthorMotions/Clips/Core/Run.anim","GroundedAdvance",1.6f,false);
        Add(sm,"WorldGuardStart","Assets/AuthorMotions/Clips/Core/GuardStart.anim",false);
        Add(sm,"WorldGuardLoop","Assets/AuthorMotions/Clips/Core/Guard.anim",false);
        Add(sm,"WorldGuardHit","Assets/AuthorMotions/Clips/Core/GuardHit.anim",false);
        Add(sm,"WorldGuardExit","Assets/AuthorMotions/Clips/Expanded/GuardExit.anim",true);
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/WorldHero/PlayerBattle/SwordHeavy.anim");
        if(!clip.isHumanMotion)throw new Exception("Great sword finisher is not humanoid.");
        var heavy=sm.states.First(x=>x.state.name=="SwordHeavy").state;
        heavy.motion=clip;heavy.speed=1;
        var animator=player.GetComponentInChildren<Animator>();Undo.RecordObject(animator,"Try great sword finisher and guard states");animator.runtimeAnimatorController=controller;
        if(!player.GetComponent<WorldSwordGrip>())Undo.AddComponent<WorldSwordGrip>(player.gameObject);
        var swordFilter=player.GetComponent<PlayerEquipment>().PracticeSword.GetComponentInChildren<MeshFilter>(true);
        string fittedPath="Assets/WorldHero/BossCombatTrial/PracticeSwordGripFit.asset";
        var fitted=AssetDatabase.LoadAssetAtPath<Mesh>(fittedPath);
        if(!fitted){fitted=UnityEngine.Object.Instantiate(swordFilter.sharedMesh);fitted.name="Practice sword - grip thickness fit";var vertices=fitted.vertices;for(int i=0;i<vertices.Length;i++){var v=vertices[i];float factor=Mathf.Lerp(.55f,1,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.025f,.075f,v.y)));v.x*=factor;v.z*=factor;vertices[i]=v;}fitted.vertices=vertices;fitted.RecalculateBounds();fitted.RecalculateNormals();AssetDatabase.CreateAsset(fitted,fittedPath);}
        Undo.RecordObject(swordFilter,"Fit weapon handle to palm");swordFilter.sharedMesh=fitted;PrefabUtility.RecordPrefabInstancePropertyModifications(swordFilter);
        var guard=player.GetComponent<WorldPlayerGuard>();if(!guard)guard=Undo.AddComponent<WorldPlayerGuard>(player.gameObject);
        var defense=player.GetComponent<ArenaDefenseFeedback>();if(!defense)defense=Undo.AddComponent<ArenaDefenseFeedback>(player.gameObject);
        defense.warningMaterial=warning;defense.attackMaterial=orange;defense.defenseMaterial=cyan;defense.blockContactAnchor=animator.GetBoneTransform(HumanBodyBones.RightHand);guard.feedback=defense;
        foreach(var obj in new UnityEngine.Object[]{animator,guard,defense,feedback}) {
            EditorUtility.SetDirty(obj);PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }
        EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(encounter.gameObject.scene);EditorSceneManager.SaveScene(encounter.gameObject.scene);
        return "Installed flat plateau boundary, Warden source contact adapter, closed-finger Q guard, isolated controller. Heavy="+clip.name+" length="+clip.length+" speed="+heavy.speed;
    }
    static void Add(AnimatorStateMachine sm,string name,string clipPath,bool exit)
    {
        var state=sm.states.FirstOrDefault(x=>x.state.name==name).state;
        if(!state)state=sm.AddState(name);
        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        string graspPath="Assets/WorldHero/BossCombatTrial/"+name+"Grip.anim";
        var grasp=AssetDatabase.LoadAssetAtPath<AnimationClip>(graspPath);
        if(!grasp){grasp=UnityEngine.Object.Instantiate(source);grasp.name=name+"Grip";AssetDatabase.CreateAsset(grasp,graspPath);}
        foreach(var binding in AnimationUtility.GetCurveBindings(source)) {
            if(!binding.propertyName.StartsWith("RightHand."))continue;
            bool thumb=binding.propertyName.Contains("Thumb");
            float value=binding.propertyName.Contains("Spread")?(thumb?-.25f:0):thumb?-.45f:-.7f;
            AnimationUtility.SetEditorCurve(grasp,binding,AnimationCurve.Constant(0,source.length,value));
        }
        EditorUtility.SetDirty(grasp);state.motion=grasp;state.writeDefaultValues=true;
        if(exit&&state.transitions.Length==0){var locomotion=sm.states.First(x=>x.state.name=="Locomotion").state;var transition=state.AddTransition(locomotion);transition.hasExitTime=true;transition.exitTime=.85f;transition.duration=.1f;}
    }
    static void Grounded(AnimatorStateMachine sm,string stateName,string sourcePath,string name,float speed,bool scriptedEnd)
    {
        string path="Assets/WorldHero/BossCombatTrial/"+name+".anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(!clip){clip=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath));clip.name=name;AssetDatabase.CreateAsset(clip,path);}
        var state=sm.states.First(s=>s.state.name==stateName).state;state.motion=clip;state.speed=speed;
        if(scriptedEnd)foreach(var transition in state.transitions.ToArray())state.RemoveTransition(transition);
    }
}

