using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WayfarerInventory.Editor
{
    public static class InventoryLabBuilder
    {
        [MenuItem("Tools/Wayfarer Inventory/Open test scene")]
        public static void Build()
        {
            const string path="Assets/WayfarerInventory/Scenes/InventoryLab.unity";
            if(System.IO.File.Exists(path)) { EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);return; }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Lab clearing";floor.transform.localScale=Vector3.one*.9f;
            var hero=GameObject.CreatePrimitive(PrimitiveType.Capsule);hero.name="Inventory preview figure";hero.transform.position=new Vector3(1.5f,1,0);
            var cam=new GameObject("Inventory lab camera").AddComponent<Camera>();cam.tag="MainCamera";
            cam.transform.position=new Vector3(0,6,-10);cam.transform.LookAt(new Vector3(0,1,0));cam.orthographic=true;cam.orthographicSize=4.3f;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color32(34,50,47,255);
            var light=new GameObject("Lab sunlight").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(45,-30,0);
            var lab = new GameObject("Inventory preview").AddComponent<InventoryLab>();
            lab.player=hero.transform;
            var preview=lab.gameObject.AddComponent<CharacterPreview>();
            preview.visualPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WayfarerInventory/SandboxHero/_Demo/Prefabs/Characters/XBotPlayerVisual.prefab");
            preview.idlePose=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/WayfarerInventory/SandboxHero/_Demo/Animations/PlayerBattle/BattleIdle.anim");
            EditorSceneManager.SaveScene(scene,path);
            // The previous scene stays loaded and untouched; reopen this saved lab alone for play.
            EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        }
    }
}
