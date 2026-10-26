using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace WayfarerInventory.Editor
{
    public static class WorldHeroMigration
    {
        [MenuItem("Tools/Wayfarer Inventory/Install sandbox world hero")]
        public static void Run()
        {
            if(EditorApplication.isPlaying)throw new System.InvalidOperationException("Exit Play Mode first");
            var world=Object.FindFirstObjectByType<BaselineWorld>();
            if(!world || !world.player)throw new System.InvalidOperationException("Open the overworld scene first");
            var player=world.player;
            const string output="Assets/WorldHero/P09PlayerVisual.prefab";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(output);
            if(!prefab)
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WayfarerInventory/SandboxHero/_Demo/Prefabs/Characters/XBotPlayerVisual.prefab");
                var temporary=(GameObject)PrefabUtility.InstantiatePrefab(source);
                temporary.name="P09PlayerVisual";
                var animator=temporary.GetComponentInChildren<Animator>();
                animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/WorldHero/XBotLocomotion.controller");
                animator.applyRootMotion=false;
                foreach(var a in temporary.GetComponentsInChildren<Animator>(true))if(a!=animator)a.enabled=false;
                prefab=PrefabUtility.SaveAsPrefabAsset(temporary,output);
                Object.DestroyImmediate(temporary);
            }
            var adapter=player.GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
            if(adapter)Undo.DestroyObjectImmediate(adapter);
            var traversal=player.GetComponent<KingOfGraphic.Hero.HeroTraversal>();
            if(traversal)Undo.DestroyObjectImmediate(traversal);
            for(int i=player.childCount-1;i>=0;i--)
            {
                var child=player.GetChild(i);
                if(child.name=="HeroPlayerVisual"||child.name=="Visual"||child.name=="P09PlayerVisual")Undo.DestroyObjectImmediate(child.gameObject);
            }
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(prefab,player);
            Undo.RegisterCreatedObjectUndo(visual,"Install sandbox hero");
            visual.transform.localPosition=Vector3.zero;
            visual.transform.localRotation=Quaternion.identity;
            visual.transform.localScale=Vector3.one;
            Transform socket=null;GameObject sword=null;
            foreach(var t in visual.GetComponentsInChildren<Transform>(true))
            {
                if(t.name=="WeaponSocket")socket=t;
                if(t.name=="PracticeSword")sword=t.gameObject;
            }
            var eq=new SerializedObject(player.GetComponent<PlayerEquipment>());
            eq.FindProperty("weaponSocket").objectReferenceValue=socket;
            eq.FindProperty("practiceSword").objectReferenceValue=sword;
            eq.ApplyModifiedProperties();
            if(!player.GetComponent<PlayerBattleActions>())Undo.AddComponent<PlayerBattleActions>(player.gameObject);
            var combat=new SerializedObject(player.GetComponent<PlayerCombat>());
            combat.FindProperty("swordVfx").objectReferenceValue=visual.GetComponentInChildren<KingOfGraphic.SwordVFX.DigitalSwordVfx>(true);
            combat.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            WorldHeroChecks.Run();
            EditorSceneManager.SaveScene(player.gameObject.scene);
            Debug.Log("Saved overworld with sandbox hero and source action controller");
        }
    }
}
