using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;

// Local component wiring only. Never opens/rebuilds other scenes or alters build settings.
public static class BaselineFunctionalSetup
{
    public static void Install()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlaying || scene.name!="OverworldBaseline" || scene.isDirty)throw new System.Exception("Open the saved baseline in Edit mode first.");
        var world=Object.FindFirstObjectByType<BaselineWorld>();
        var flow=Ensure<BaselineFlow>(world.gameObject);
        var room=Ensure<BaselineEndingRoom>(world.gameObject);
        var presenter=Ensure<BaselineRewardPresentation>(world.gameObject);
        Undo.RecordObject(flow,"Wire baseline flow");Undo.RecordObject(room,"Wire ending room");Undo.RecordObject(presenter,"Wire reward presentation");
        flow.endingRoom=room;flow.rewards=presenter;flow.endingEntrance=GameObject.Find("Guardian_Capsule_PLACEHOLDER").transform;
        // A scaled cylinder's default CapsuleCollider rises far above this flat platform.
        var platform=GameObject.Find("FutureEndingArena_REPLACE");
        var capsule=platform.GetComponent<CapsuleCollider>();
        if(capsule){Undo.RecordObject(capsule,"Match tower platform collision");capsule.enabled=false;}
        var floorCollider=Ensure<MeshCollider>(platform);
        Undo.RecordObject(floorCollider,"Match tower platform collision");floorCollider.sharedMesh=platform.GetComponent<MeshFilter>().sharedMesh;
        // Meet the final stair before crossing it, then rise gently onto the flat platform.
        var roofBridge=GameObject.Find("TowerRoofBridge").transform;
        Undo.RecordObject(roofBridge,"Connect tower stair landing");
        roofBridge.SetPositionAndRotation(new Vector3(6.1f,10.85f,66.7f),Quaternion.Euler(0,0,-10.1f));
        roofBridge.localScale=new Vector3(2.8f,.4f,2.2f);
        room.player=world.player;room.barrierMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Demo/Materials/TeamShaders/PixelBarrier.mat");
        room.floorMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Demo/BaselineData/Ruins.mat");room.targetMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Demo/BaselineData/Equipment.mat");
        room.backdropAccentMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Demo/BaselineData/HologramAndRoute.mat");
        presenter.swordPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Rewards/HologramSword/HologramSword.prefab");
        if(!presenter.swordPrefab || !room.barrierMaterial)throw new System.Exception("Required authored shader resources missing");
        var loadout=Ensure<BaselineLoadout>(world.player.gameObject);Undo.RecordObject(loadout,"Wire loadout");loadout.world=world;loadout.flow=flow;
        var wood=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Demo/Materials/Chest/ChestDissolve_Wood.mat");var brass=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Demo/Materials/Chest/ChestDissolve_Brass.mat");
        foreach(var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None).Where(x=>x.action==BaselineInteraction.ActionKind.Chest))
        {
            foreach(var renderer in item.GetComponentsInChildren<Renderer>(true)){Undo.RecordObject(renderer,"Apply authored dissolve");renderer.sharedMaterials=renderer.sharedMaterials.Select(x=>renderer.name.Contains("Lid")?brass:wood).ToArray();}
            var controller=Ensure<ChestDissolveController>(item.gameObject);controller.Configure(world.player.GetComponent<PlayerMovement>());controller.enabled=false;
            var visual=Ensure<BaselineChestVisual>(item.gameObject);Undo.RecordObject(visual,"Wire chest visual");visual.dissolve=controller;visual.lid=item.lid;
        }
        foreach(var entrance in Object.FindObjectsByType<SideWorldEntrance>(FindObjectsSortMode.None))
        {
            var old=entrance.transform.Find("FunctionalPortal");GameObject portal;
            if(old)portal=old.gameObject;
            else {portal=GameObject.CreatePrimitive(PrimitiveType.Quad);portal.name="FunctionalPortal";Undo.RegisterCreatedObjectUndo(portal,"Add entrance visual");portal.transform.SetParent(entrance.transform,false);Object.DestroyImmediate(portal.GetComponent<Collider>());}
            // Align to the authored arch in world space; the guide faces sideways.
            var left=GameObject.Find(entrance.name+"Portal_Left").GetComponent<Renderer>().bounds;
            var right=GameObject.Find(entrance.name+"Portal_Right").GetComponent<Renderer>().bounds;
            var lintel=GameObject.Find(entrance.name+"Portal_Lintel").GetComponent<Renderer>().bounds;
            float bottom=Mathf.Max(left.min.y,right.min.y), top=lintel.min.y;
            var centre=(left.center+right.center)*.5f;centre.y=(bottom+top)*.5f;
            var normal=Vector3.Cross((right.center-left.center).normalized,Vector3.up).normalized;
            portal.transform.SetPositionAndRotation(centre+normal*.02f,Quaternion.LookRotation(normal,Vector3.up));
            portal.transform.localScale=new Vector3(Mathf.Abs(right.center.x-left.center.x)-left.extents.x-right.extents.x,top-bottom,1);
            portal.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/PortalScan/Materials/M_PortalScan.mat");
            var scan=Ensure<PortalScanController>(portal);var so=new SerializedObject(scan);so.FindProperty("portalRenderer").objectReferenceValue=portal.GetComponent<Renderer>();so.FindProperty("player").objectReferenceValue=world.player;so.FindProperty("activationDistance").floatValue=7;so.ApplyModifiedProperties();
        }
        foreach(var text in Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))if(text.text.Contains("TOWER / ENDING")){Undo.RecordObject(text,"Update ending label");text.text="TOWER / FINAL ROOM\nClimb up, then press E";}
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Debug.Log("BASELINE_FUNCTIONAL_WIRING_SAVED");
    }
    static T Ensure<T>(GameObject g) where T:Component {var c=g.GetComponent<T>();return c?c:Undo.AddComponent<T>(g);}
}
