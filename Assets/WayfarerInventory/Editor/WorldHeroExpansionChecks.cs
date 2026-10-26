using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace WayfarerInventory.Editor
{
    public static class WorldHeroExpansionChecks
    {
        public static readonly string[] Required={"Death","Drown","Drowned","DrawSword","SheatheSword","OpenChest","Pickup","Touch","RestBegin","RestLoop","RestEnd","Talk","HeavyLand"};
        public static void Run()
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/WorldHero/XBotLocomotion.controller");
            foreach(var name in Required)
            {
                AnimatorState found=null;
                foreach(var state in controller.layers[0].stateMachine.states)if(state.state.name==name)found=state.state;
                if(!found||!found.motion)throw new Exception("Missing world action: "+name);
            }
            Debug.Log("PASS: supplementary world action states have animation clips");
        }
    }
}
