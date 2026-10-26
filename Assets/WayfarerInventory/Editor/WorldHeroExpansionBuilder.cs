using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WayfarerInventory.Editor
{
    public static class WorldHeroExpansionBuilder
    {
        const string Root="Assets/WorldHero/Expansion/";
        public static AnimationClip Clip(string file,string take=null)
        {
            var clips=AssetDatabase.LoadAllAssetsAtPath(Root+"Clips/"+file+".fbx").OfType<AnimationClip>().Where(x=>!x.name.StartsWith("__preview__"));
            return take==null?clips.First():clips.First(x=>x.name==take);
        }
        static AnimatorController controller;
        static AnimationClip Add(string name,string file,string take=null,bool loop=false)
        {
            var motion=UnityEngine.Object.Instantiate(Clip(file,take));motion.name=name;
            var settings=AnimationUtility.GetAnimationClipSettings(motion);settings.loopTime=loop;settings.loopBlend=loop;AnimationUtility.SetAnimationClipSettings(motion,settings);
            var path=Root+name+".anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(existing){EditorUtility.CopySerialized(motion,existing);UnityEngine.Object.DestroyImmediate(motion);motion=existing;}else AssetDatabase.CreateAsset(motion,path);
            var machine=controller.layers[0].stateMachine;
            var state=machine.states.Select(x=>x.state).FirstOrDefault(x=>x.name==name)??machine.AddState(name);
            state.motion=motion;state.writeDefaultValues=true;
            return motion;
        }
        public static void Run()
        {
            if(EditorApplication.isPlaying)throw new Exception("Exit Play Mode first");
            controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/WorldHero/XBotLocomotion.controller");
            Add("Death","HumanM@CombatDeath01");Add("Drown","HumanM@SwimDrown01");Add("Drowned","HumanM@SwimDrowned01",loop:true);
            Add("DrawSword","HumanM@UnsheatheHips01_R");Add("SheatheSword","HumanM@SheatheHips01_R");
            Add("OpenChest","HumanM@Opening01 - Begin");Add("Pickup","HumanM@PickUpObject01_R");Add("Touch","MovementAnimsetPro","ButtonPush_LH");
            Add("RestBegin","HumanM@SitGround01 - Begin");Add("RestLoop","HumanM@SitGround01 - Loop",loop:true);Add("RestEnd","HumanM@SitGround01 - Stop");
            Add("Talk","HumanM@HeadNod01");Add("HeavyLand","MovementAnimsetPro","JumpIdleLandHard");
            Add("WalkStart","MovementAnimsetPro","WalkFwdStart");Add("WalkStop","MovementAnimsetPro","WalkFwdStop_LU");
            Add("RunStart","MovementAnimsetPro","RunFwdStart");Add("RunStop","MovementAnimsetPro","RunFwdStop_LU");
            Add("TurnLeft","MovementAnimsetPro","TurnLt90_Loop");Add("TurnRight","MovementAnimsetPro","TurnRt90_Loop");
            var sprint=Add("Sprint","HumanM@Sprint01_Forward",loop:true);
            Add("StrafeLeft","HumanM@StrafeWalk01_Left",loop:true);Add("StrafeRight","HumanM@StrafeWalk01_Right",loop:true);Add("WalkBack","HumanM@Walk01_Backward",loop:true);
            var swim=Add("SurfaceSwim","HumanM@Swim01_Forward",loop:true);var tread=Add("SurfaceTread","HumanM@SwimIdle01",loop:true);
            foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)))
            {
                if(asset is BlendTree tree)
                {
                    var children=tree.children;
                    if(tree.name=="XBotSwimmingBlend")
                    {
                        children[0].motion=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath("058cb50f031bdad428a4a42e4804e26e")).OfType<AnimationClip>().First(x=>!x.name.StartsWith("__preview__"));
                        children[1].motion=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath("4fc7ea93dfa0e1c4587c1d347e4b15b9")).OfType<AnimationClip>().First(x=>!x.name.StartsWith("__preview__"));
                        tree.children=children;
                    }
                    // Keep the source walk/run blend; sprint is a distinct presentation above its run threshold.
                }
            }
            AssetDatabase.SaveAssets();WorldHeroExpansionChecks.Run();
        }
    }
}
