using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Local visual application only. Does not regenerate the map or alter gameplay identities.
public static class OverworldShowcaseApply
{
    const string Root="Assets/Art/OverworldShowcase/";
    const string Report="ToolReports/AssetApplication/";
    static string Nature(string n)=>Root+"OverworldAssetShowcase/SelectedPrefabs/"+n+".prefab";
    static string Props(string n)=>Root+"OverworldExpansion/SelectedPrefabs/"+n+".prefab";
    static Transform worldRoot;
    public static void Capture(string phase)
    {
        Directory.CreateDirectory(Report);
        var positions=new[]{new Vector3(100,145,-140),new Vector3(-48,7,-36),new Vector3(13,5,-10),new Vector3(58,6,2),new Vector3(1,3.2f,-68)};
        var targets=new[]{new Vector3(0,0,0),new Vector3(-53,3,-5),new Vector3(2,2,6),new Vector3(48,4,18),new Vector3(-4,2,-61)};
        var names=new[]{"overview","west","island","east","camp"};
        for(int i=0;i<names.Length;i++) Render(Report+phase+"-"+names[i]+".png",positions[i],targets[i],i==0);
    }
    public static void Render(string path,Vector3 position,Vector3 target,bool overview=false)
    {
        var go=new GameObject("Asset review camera"){hideFlags=HideFlags.HideAndDontSave};
        var rt=RenderTexture.GetTemporary(1440,900,24,RenderTextureFormat.ARGB32);
        var old=RenderTexture.active;Texture2D tex=null;
        try
        {
            var c=go.AddComponent<Camera>(); if(Camera.main)c.CopyFrom(Camera.main);
            c.enabled=false;c.targetTexture=rt;c.aspect=1.6f;c.fieldOfView=60;c.nearClipPlane=.1f;c.farClipPlane=500;
            c.orthographic=overview;c.orthographicSize=98;
            c.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
            var extra=go.AddComponent<UniversalAdditionalCameraData>();extra.renderPostProcessing=false;
            c.Render();RenderTexture.active=rt;tex=new Texture2D(1440,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1440,900),0,0);tex.Apply();
            File.WriteAllBytes(path,tex.EncodeToPNG());
        }
        finally{RenderTexture.active=old;if(tex)Object.DestroyImmediate(tex);Object.DestroyImmediate(go);RenderTexture.ReleaseTemporary(rt);}
    }
    public static string CollisionSnapshot()
    {
        return string.Join("\n",Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(c=>c.gameObject.scene==SceneManager.GetActiveScene()).OrderBy(c=>c.GetInstanceID()).Select(c=>c.GetInstanceID()+"|"+c.name+"|"+c.enabled+"|"+c.isTrigger+"|"+c.transform.position.ToString("F4")+"|"+c.transform.rotation.ToString("F4")+"|"+c.transform.lossyScale.ToString("F4")+"|"+EditorJsonUtility.ToJson(c)));
    }
    public static string Identities()
    {
        return string.Join("\n",Object.FindObjectsByType<BaselineInteraction>(FindObjectsInactive.Include,FindObjectsSortMode.None).OrderBy(x=>x.GetInstanceID()).Select(x=>x.GetInstanceID()+"|"+x.name+"|"+x.action+"|"+x.index+"|"+x.transform.position.ToString("F4")));
    }
    static Bounds BoundsOf(GameObject o)
    {
        var rr=o.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
        if(rr.Length==0)throw new Exception("No visible renderers: "+o.name);
        var b=rr[0].bounds;foreach(var r in rr)b.Encapsulate(r.bounds);return b;
    }
    static GameObject Visual(string path,Transform parent)
    {
        var p=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!p)throw new Exception(path);
        var o=(GameObject)PrefabUtility.InstantiatePrefab(p);
        PrefabUtility.UnpackPrefabInstance(o,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        o.name="Applied_"+p.name;Undo.RegisterCreatedObjectUndo(o,"Apply teammate visual");
        foreach(var c in o.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
        foreach(var b in o.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(b);
        foreach(var b in o.GetComponentsInChildren<NPCSimpleGestureAnimator>(true))b.enabled=false;
        o.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);o.transform.localScale=Vector3.one;
        o.transform.SetParent(parent,true);return o;
    }
    static void Hide(Renderer r){Undo.RecordObject(r,"Replace greybox renderer");r.enabled=false;}
    static GameObject BoxVisual(Transform host,string path,bool wall)
    {
        var original=host.GetComponent<Renderer>();var b=original.bounds;
        var o=Visual(path,host);var s=BoundsOf(o).size;
        float yaw=wall?(host.localScale.z>host.localScale.x?90:0):host.eulerAngles.y;
        var desired=wall?new Vector3(Mathf.Max(host.lossyScale.x,host.lossyScale.z),host.lossyScale.y,Mathf.Min(host.lossyScale.x,host.lossyScale.z)):new Vector3(host.lossyScale.x,host.lossyScale.y,host.lossyScale.z);
        var scale=new Vector3(desired.x/s.x,desired.y/s.y,desired.z/s.z);
        // New visuals inherit nonuniform greybox parents; compute world scale before parenting.
        o.transform.SetParent(null,true);o.transform.localScale=scale;
        var bb=BoundsOf(o);o.transform.position-=bb.center;o.transform.RotateAround(Vector3.zero,Vector3.up,yaw);
        o.transform.position+=b.center;o.transform.SetParent(host,true);Hide(original);return o;
    }
    public static void Apply()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/_Demo/Scenes/OverworldBaseline.unity"||scene.isDirty||EditorApplication.isPlaying)throw new Exception("Requires saved OverworldBaseline in Edit Mode.");
        if(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(t=>t.name.StartsWith("Applied_")))throw new Exception("Visual application already exists. Do not apply twice.");
        worldRoot=GameObject.Find("_A2_Greybox").transform;var all=worldRoot.Cast<Transform>().ToArray();
        Directory.CreateDirectory(Report);File.WriteAllText(Report+"before-colliders.txt",CollisionSnapshot());File.WriteAllText(Report+"before-identities.txt",Identities());
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Apply selected teammate art to baseline");
        int trees=0,rocks=0,walls=0;
        foreach(var trunk in all.Where(t=>t.name=="TreeTrunk_REPLACE"))
        {
            var canopy=all.First(t=>t.name=="TreeCanopy_REPLACE"&&Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(trunk.position.x,trunk.position.z))<.1f);
            var tree=Visual(Nature(trees%4==0?"Birch_Small":trees%4==3?"Pine_01":"Broadleaf_01"),trunk);
            tree.transform.SetParent(null,true);var b=BoundsOf(tree);float height=Mathf.Clamp(canopy.GetComponent<Renderer>().bounds.max.y-trunk.GetComponent<Renderer>().bounds.min.y,5.5f,8);
            tree.transform.localScale=Vector3.one*(height/b.size.y);tree.transform.rotation=Quaternion.Euler(0,(trees*137)%360,0);
            b=BoundsOf(tree);tree.transform.position=new Vector3(trunk.position.x,trunk.GetComponent<Renderer>().bounds.min.y-b.min.y-.08f,trunk.position.z);
            tree.transform.SetParent(trunk,true);Hide(trunk.GetComponent<Renderer>());Hide(canopy.GetComponent<Renderer>());trees++;
        }
        foreach(var t in all.Where(t=>t.name.StartsWith("Expansion_PerimeterRock")||t.name.StartsWith("Expansion_Rock")))
        {BoxVisual(t,Nature(rocks++%2==0?"Rock_L1":"Rock_L2"),false);}
        foreach(var t in all.Where(t=>t.name=="IslandWall_REPLACE"||t.name=="IslandBackWall_REPLACE"||t.name=="WestHiddenWall_REPLACE"||t.name=="EasternRuinWall_REPLACE"||t.name=="Expansion_IslandScreen"))
        {BoxVisual(t,Props("Wall_Full"),true);walls++;}
        // Keep the existing holographic missing quarter and original arch collision exactly in place.
        foreach(var t in all.Where(t=>t.name.StartsWith("IslandBrokenArch_")||t.name.Contains("GuidePortal_")))
        {
            if(t.name.EndsWith("Lintel"))BoxVisual(t,Props("Stone_Beam"),false);
            else BoxVisual(t,Props(t.name.EndsWith("Right")?"Column_Broken":"Column_Full"),false);
        }
        var stone=AssetDatabase.LoadAssetAtPath<Material>(Root+"OverworldExpansion/Materials/M_Surface_Old_Stone___Gravel.mat");
        foreach(var t in all.Where(t=>t.name.StartsWith("Causeway_")||t.name=="TowerWalkway"||t.name=="TowerRoofBridge"||t.name=="Tower_REPLACE"||t.name=="FutureEndingArena_REPLACE"))
        {var r=t.GetComponent<Renderer>();Undo.RecordObject(r,"Apply stone material");r.sharedMaterial=stone;}
        ApplyNPC("IntroNPC","NPC_Survival");ApplyNPC("SideWorldA_Guide","NPC_Magician");ApplyNPC("SideWorldB_Guide","NPC_Cowboy");
        foreach(var c in Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None).Where(x=>x.action==BaselineInteraction.ActionKind.Chest))ApplyChest(c);
        Physics.SyncTransforms();
        File.WriteAllText(Report+"after-colliders.txt",CollisionSnapshot());File.WriteAllText(Report+"after-identities.txt",Identities());
        if(File.ReadAllText(Report+"before-colliders.txt")!=CollisionSnapshot())throw new Exception("Original collider state changed; inspect before saving.");
        if(File.ReadAllText(Report+"before-identities.txt")!=Identities())throw new Exception("Interaction identities changed.");
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);Undo.CollapseUndoOperations(undo);
        Debug.Log("SHOWCASE_APPLIED trees="+trees+" rocks="+rocks+" walls="+walls+" NPCs=3 chests=3; original colliders and interaction identities unchanged.");
    }
    static void ApplyNPC(string name,string prefab)
    {
        var host=GameObject.Find(name);var old=host.transform.Find("Visual");var controller=old.GetComponentInChildren<Animator>(true).runtimeAnimatorController;
        foreach(var r in old.GetComponentsInChildren<Renderer>(true))Hide(r);
        var capsule=host.transform.Find("NPC_Capsule_REPLACE");if(capsule)Hide(capsule.GetComponent<Renderer>());
        var o=Visual(Props(prefab),host.transform);o.transform.SetParent(null,true);
        var b=BoundsOf(o);o.transform.localScale=Vector3.one*(1.85f/b.size.y);b=BoundsOf(o);o.transform.position=new Vector3(host.transform.position.x,host.transform.position.y-b.min.y,host.transform.position.z);
        o.transform.rotation=host.transform.rotation;o.transform.SetParent(host.transform,true);
        var a=o.GetComponentInChildren<Animator>();if(a){a.runtimeAnimatorController=controller;a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;}
    }
    static void ApplyChest(BaselineInteraction host)
    {
        foreach(var r in host.GetComponentsInChildren<Renderer>(true))Hide(r);
        var o=Visual(Props("Chest_Basic_IndependentLid"),host.transform);o.transform.SetParent(null,true);o.transform.position=host.transform.position;o.transform.rotation=host.transform.rotation;o.transform.SetParent(host.transform,true);
        var template=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Demo/Materials/Chest/ChestDissolve_Wood.mat");
        var path=Root+"OverworldExpansion/Materials/Applied_TexturedChestDissolve.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(template);var src=AssetDatabase.LoadAssetAtPath<Material>(Root+"OverworldExpansion/Materials/ChestBasic_9b69eaee.mat");material.SetTexture("_BaseMap",src.GetTexture("_BaseMap"));material.SetColor("_BaseColor",Color.white);material.SetColor("_ShadeColor",new Color(.35f,.4f,.46f));AssetDatabase.CreateAsset(material,path);}
        foreach(var r in o.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=r.sharedMaterials.Select(m=>material).ToArray();
        var lid=o.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Cap");
        Undo.RecordObject(host,"Connect imported lid");host.lid=lid;
        var v=host.GetComponent<BaselineChestVisual>();Undo.RecordObject(v,"Connect imported lid");v.lid=lid;
        var d=host.GetComponent<ChestDissolveController>();Undo.RecordObject(d,"Configure imported chest renderers");Undo.RecordObject(host.GetComponent<ChestScanSpace>(),"Configure chest scan bounds");d.Configure(Object.FindFirstObjectByType<BaselineWorld>().player.GetComponent<PlayerMovement>());d.enabled=false;
    }

    public static void RefineSurfaces()
    {
        var root=GameObject.Find("_A2_Greybox").transform;
        var grass=AssetDatabase.LoadAssetAtPath<Material>(Root+"OverworldExpansion/Materials/M_Surface_Grass.mat");
        grass.SetTextureScale("_BaseMap",new Vector2(4,4));grass.SetColor("_BaseColor",new Color(.75f,.92f,.86f));EditorUtility.SetDirty(grass);
        var ground=GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<Renderer>();Undo.RecordObject(ground,"Apply supplied grass surface");ground.sharedMaterial=grass;
        foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{Root+"OverworldExpansion/Materials"}))
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if(m.name.StartsWith("M_columns")||m.name.StartsWith("M_stoneblocks")||m.name.Contains("Old_Stone"))
            {m.SetColor("_BaseColor",new Color(.65f,.68f,.61f));EditorUtility.SetDirty(m);}
            if(m.name.Contains("Old_Stone"))m.SetTextureScale("_BaseMap",Vector2.one*.6f);
        }
        foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{Root+"OverworldAssetShowcase/Materials"}))
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if(m.name.StartsWith("M_rocks")){m.SetColor("_BaseColor",new Color(.7f,.75f,.68f));EditorUtility.SetDirty(m);}
        }
        foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{Root+"OverworldAssetShowcase/Materials"}))
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if(m.name.StartsWith("M_leaves_3_")){m.SetColor("_BaseColor",new Color(.22f,.5f,.12f));EditorUtility.SetDirty(m);}
        }
        foreach(var host in root.Cast<Transform>().Where(t=>t.name.StartsWith("Expansion_Rock")||t.name=="Expansion_PerimeterRock"))
        {
            var visual=host.Cast<Transform>().First(t=>t.name.StartsWith("Applied_"));
            var original=host.GetComponent<Renderer>().bounds;var b=BoundsOf(visual.gameObject);
            Undo.RecordObject(visual,"Embed rock visual into sloping ground");visual.SetParent(null,true);
            var scale=visual.localScale;scale.y*=original.size.y*1.35f/b.size.y;visual.localScale=scale;
            b=BoundsOf(visual.gameObject);visual.position+=Vector3.up*(original.max.y-b.max.y);visual.SetParent(host,true);
        }
        int i=0;var terrain=ground.GetComponent<MeshCollider>();
        foreach(var trunk in root.Cast<Transform>().Where(t=>t.name=="TreeTrunk_REPLACE").Take(12))
        {
            if(i++%2!=0 || trunk.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Applied_Bush_Low"))continue;
            var bush=Visual(Nature("Bush_Low"),trunk);bush.transform.SetParent(null,true);
            var b=BoundsOf(bush);bush.transform.localScale=Vector3.one*(.85f/b.size.y);b=BoundsOf(bush);
            var p=trunk.position+new Vector3(.55f,0,.25f);RaycastHit hit;
            if(terrain.Raycast(new Ray(p+Vector3.up*25,Vector3.down),out hit,80))p.y=hit.point.y;
            bush.transform.position=new Vector3(p.x,p.y-b.min.y-.12f,p.z);bush.transform.SetParent(trunk,true);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }
}
