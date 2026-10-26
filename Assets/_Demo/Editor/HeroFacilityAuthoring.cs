using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using KingOfGraphic.Hero;
public static class HeroFacilityAuthoring
{
    static Transform root;static Material wood,rope,stone;
    static GameObject Box(string name,Vector3 position,Vector3 scale,Material mat,Transform parent=null)
    {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent?parent:root);go.transform.position=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=mat;return go;}
    static HeroTraversalSurface Surface(string name,HeroTraversalKind kind,Vector3 bottom,Vector3 top,Vector3 forward)
    {
        var go=new GameObject(name);go.transform.SetParent(root);go.transform.position=bottom;go.transform.rotation=Quaternion.LookRotation(forward);
        var s=go.AddComponent<HeroTraversalSurface>();s.kind=kind;
        s.bottom=new GameObject("Entry feet").transform;s.bottom.SetParent(go.transform);s.bottom.position=bottom;
        s.top=new GameObject("Top feet").transform;s.top.SetParent(go.transform);s.top.position=top;return s;
    }
    static void Rod(string name,Vector3 from,Vector3 to,float radius,Material material,Transform parent)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=name;go.transform.SetParent(parent);go.transform.position=(from+to)*.5f;go.transform.up=(to-from).normalized;go.transform.localScale=new Vector3(radius*2,Vector3.Distance(from,to)*.5f,radius*2);go.GetComponent<Renderer>().sharedMaterial=material;
    }
    public static void Build()
    {
        if(EditorApplication.isPlaying)throw new System.Exception("Editor mode required");
        if(GameObject.Find("Own hero traversal facilities"))throw new System.Exception("Facilities already exist; inspect rather than duplicate");
        root=new GameObject("Own hero traversal facilities").transform;
        wood=AssetDatabase.LoadAssetAtPath<Material>("Assets/OwnWorld/FoldedRealm_20261009/Materials/Own honey timber.mat");
        rope=AssetDatabase.LoadAssetAtPath<Material>("Assets/OwnWorld/FoldedRealm_20261009/Materials/Own pier hemp and endgrain.mat");
        stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/OwnWorld/FoldedRealm_20261009/Materials/OwnR1FoldedStone.mat");
        if(!wood||!rope||!stone)throw new System.Exception("Missing own materials");
        // Side-mounted ladder leaves the original swimming slipway unobstructed.
        var ladder=Surface("Island dock ladder",HeroTraversalKind.Ladder,new Vector3(-54.75f,3.65f,20.05f),new Vector3(-54.75f,5.85f,20.05f),Vector3.right);ladder.reach=1.7f;
        for(int i=-1;i<=1;i+=2)Box("Own ladder stile",new Vector3(-54.47f,5.25f,20.05f+i*.28f),new Vector3(.09f,3.15f,.09f),wood,ladder.transform);
        for(int i=0;i<10;i++)Box("Own ladder rung",new Vector3(-54.47f,3.9f+i*.3f,20.05f),new Vector3(.1f,.075f,.63f),wood,ladder.transform);
        // Rear-bank optional lookout, beside the existing route, with a physical landing.
        var climbRope=Surface("Rear-bank lookout rope",HeroTraversalKind.Rope,new Vector3(-82.45f,11.75f,65.65f),new Vector3(-82.45f,14.2f,65.65f),Vector3.forward);
        Rod("Own climbing hemp",new Vector3(-82.45f,11.75f,65.9f),new Vector3(-82.45f,15.7f,65.9f),.035f,rope,climbRope.transform);
        Box("Own lookout landing",new Vector3(-82.45f,14.09f,66.55f),new Vector3(1.9f,.16f,1.8f),wood,climbRope.transform);
        for(int i=-1;i<=1;i+=2)Box("Own lookout support",new Vector3(-82.45f+i*.8f,12.9f,67.15f),new Vector3(.16f,2.4f,.16f),wood,climbRope.transform);
        Box("Own rope crossbeam",new Vector3(-82.45f,15.7f,66.3f),new Vector3(1.9f,.14f,.9f),wood,climbRope.transform);
        var pole=Surface("Tutorial hub climbing post",HeroTraversalKind.Pole,new Vector3(-6.8f,11.49f,-59.2f),new Vector3(-6.8f,13.55f,-59.2f),Vector3.forward);
        Rod("Own climbable timber post",new Vector3(-6.8f,11.45f,-58.91f),new Vector3(-6.8f,15.05f,-58.91f),.17f,wood,pole.transform);
        // A wall alongside the island landing gives the climbing lesson real contact.
        Box("Own island climbing practice face",new Vector3(-50.8f,4.84f,18.5f),new Vector3(1.6f,1.82f,1.1f),stone);
        Box("Own island climbing foot ledge",new Vector3(-50.8f,3.95f,17.35f),new Vector3(1.6f,.2f,1.3f),stone);
        Box("Own island climbing top step",new Vector3(-50.8f,5.67f,19.45f),new Vector3(1.6f,.16f,.9f),stone);
        var ledge=Surface("Island ledge grip",HeroTraversalKind.Ledge,new Vector3(-50.8f,3.85f,17.68f),new Vector3(-50.8f,5.82f,18.25f),Vector3.forward);ledge.lateralExtent=.46f;
        var step=Surface("Hub low step",HeroTraversalKind.LowStep,new Vector3(6.7f,11.49f,-56.8f),new Vector3(6.7f,11.8f,-56.1f),Vector3.forward);
        Box("Own low stepping stone",new Vector3(6.7f,11.6f,-55.8f),new Vector3(1.3f,.3f,1.3f),stone,step.transform);
        var vault=Surface("Hub practice rail vault",HeroTraversalKind.Vault,new Vector3(8.5f,11.49f,-56.8f),new Vector3(8.5f,11.49f,-55.2f),Vector3.forward);
        Box("Own low vault rail",new Vector3(8.5f,11.95f,-56),new Vector3(1.2f,.12f,.18f),wood,vault.transform);
        for(int i=-1;i<=1;i+=2)Box("Own vault rail foot",new Vector3(8.5f+i*.5f,11.7f,-56),new Vector3(.1f,.5f,.1f),wood,vault.transform);
        var mantle=Surface("Island low rock scramble",HeroTraversalKind.Mantle,new Vector3(-49.4f,5.82f,21.5f),new Vector3(-49.4f,6.5f,22.5f),Vector3.forward);
        Box("Own scramble rock",new Vector3(-49.4f,6.1f,22.7f),new Vector3(1.4f,.7f,1.4f),stone,mantle.transform);
        // Existing bench: animated pelvis is .77m above the root and .25m behind it.
        var bench=GameObject.Find("Simple rest bench");if(bench){var forward=bench.transform.forward;var seat=new Vector3(60.211765f,29.14f,52.058826f);Surface("Tower camp bench rest",HeroTraversalKind.Seat,seat-Vector3.up*.77f+forward*.25f,seat-Vector3.up*.58f+forward*1.2f,forward);}
        var layout=Object.FindFirstObjectByType<OverworldV2Layout>();var so=new SerializedObject(layout);
        so.FindProperty("islandRestPosition").vector3Value=new Vector3(-53.06f,4.28f,14);
        so.FindProperty("climbBasePosition").vector3Value=new Vector3(-50.8f,4.1f,17.25f);
        so.FindProperty("climbTopPosition").vector3Value=new Vector3(-50.8f,5.82f,19.45f);so.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);
        Debug.Log("Own optional facilities saved. Contact/clearance routes still require physics validation.");
    }
}
