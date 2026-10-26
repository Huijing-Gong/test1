using System;
using UnityEditor;
using UnityEngine;

namespace WayfarerInventory.Editor
{
    public static class WorldHeroChecks
    {
        [MenuItem("Tools/Wayfarer Inventory/Validate world hero bindings")]
        public static void Run()
        {
            var world=UnityEngine.Object.FindFirstObjectByType<BaselineWorld>();
            if(!world || !world.player)throw new Exception("Main world player missing");
            var player=world.player;
            if(player.GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>())
                throw new Exception("Old hero action adapter still owns the player");
            var animator=player.GetComponentInChildren<Animator>();
            if(!animator || !animator.avatar || !animator.avatar.isHuman || !animator.runtimeAnimatorController)
                throw new Exception("New humanoid animator is not bound");
            if(!animator.name.Contains("P09"))throw new Exception("Expected sandbox P09 hero");
            var equipment=player.GetComponent<PlayerEquipment>();
            if(!equipment.WeaponSocket || !equipment.PracticeSword || !equipment.WeaponSocket.IsChildOf(animator.transform))
                throw new Exception("Sword is not bound to the new rig");
            var controller=animator.runtimeAnimatorController;
            var paths=AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(controller),true);
            foreach(var path in paths)if(path.StartsWith("Assets/OwnHero/"))throw new Exception("Old hero animation dependency: "+path);
            Debug.Log("PASS: world hero bindings and no old action ownership");
        }
    }
}
