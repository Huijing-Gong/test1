using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Explicit P0 authoring only. Preserves all 17 identities, parents, prefabs and AI values.
public static class EnemyDistributionInstaller
{
    public const string ScenePath="Assets/_Demo/Scenes/OverworldBaseline.unity";
    public const string Marker="_RegionalEnemyLayout_P0_17";
    public const string Output="LocalDocs/Enemies/EncounterDistribution-20261018/Implemented_P0";
    struct SafeArea {
        public Vector3 p;public float radius,height;
        public SafeArea(Vector3 point,float r,float h=5){p=point;radius=r;height=h;}
    }
    sealed class Spec {
        public string name,id;public Vector3 hint;public float search;
        public Spec(string n,string g,float x,float z,float r=9){name=n;id=g;hint=new Vector3(x,0,z);search=r;}
    }
    static Spec[] Specs()=>new[]{
        new Spec("StoneScout_Forest_01","01a",-39,-56),
        new Spec("GelBlob_LakeShoreSouth","01b",-65,-68),
        new Spec("RuinPrism_WestRuins","03",-93,-17),
        new Spec("RepairWisp_HiddenChest","03",-96,-17,6),
        new Spec("RuinPrism_SideA","04",-85,-59),
        new Spec("GelBlob_DampForest","05a",6,10,12),
        new Spec("GelBlob_LakeShoreNorth","05b",-9,54,12),
        new Spec("StoneScout_Forest_02","06a",37,-45,8),
        new Spec("StoneScout_Sand_01","07",59,-13,8),
        new Spec("StoneScout_Sand_02","07",64,-13,8),
        new Spec("LeafWisp_EastForest","07",63,-19,8),
        new Spec("RailBeetle_PathGuard","08",85,33,18),
        new Spec("RuinPrism_SideB","09",66,-69,8),
        new Spec("StoneScout_Awakened_01","10a",-30,97,10),
        new Spec("RuinPrism_TowerStairs","11",94,77,9),
        new Spec("RepairWisp_Tower","11",97,77,6),
        new Spec("StoneScout_Awakened_02","11",93,71,8)
    };
    public static bool HasLayout(Scene scene)=>scene.IsValid() && scene.isLoaded && scene.GetRootGameObjects().Any(r=>r.name==Marker);
    public static string Role(Health h) {
        if(h.GetComponent<StoneScoutRoadsideAI>())return "StoneScout";
        var d=h.GetComponent<DomainPrototypeEnemy>();if(d)return d.EnemyKind.ToString();
        if(h.GetComponent<RailBeetleAI>())return "RailBeetle";
        if(h.GetComponent<RepairWispAI>())return "RepairWisp";
        return h.GetComponent<GelBlobAI>()?"GelBlob":"";
    }
    static Health[] Actors(Scene s)=>Object.FindObjectsByType<Health>(FindObjectsSortMode.None).Where(h=>h.gameObject.scene==s && Role(h)!="").ToArray();
    [MenuItem("Tools/Enemies/Apply Regional Layout P0 (17 existing enemies)")]
    public static void Apply() {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save scene edits first.");
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        if(HasLayout(scene)){ValidateAuthoring();Export();return;}
        var layout=Object.FindFirstObjectByType<OverworldV2Layout>();var nav=EnemyNavigationWorld.Find(scene);
        if(!layout || !nav)throw new InvalidOperationException("Map/navigation missing.");
        var actors=Actors(scene).ToDictionary(h=>h.name);
        if(actors.Count!=17 || Specs().Any(s=>!actors.ContainsKey(s.name)))throw new InvalidOperationException("Expected original seventeen enemies.");
        var safe=Safety(layout,scene);var disabled=new Dictionary<Collider,bool>();
        foreach(var h in actors.Values)foreach(var c in h.GetComponentsInChildren<Collider>(true)){disabled[c]=c.enabled;c.enabled=false;}
        var placed=new List<Health>();var ids=Specs().ToDictionary(s=>s.name,s=>s.id);
        try {
            foreach(var spec in Specs()) {
                var h=actors[spec.name];
                if(!TryPlace(h,spec,layout,nav,safe,placed,ids,out var point,out var facing))throw new InvalidOperationException("No supported spawn/lane near "+spec.name+" "+spec.hint);
                Undo.RecordObject(h.transform,"Regional layout P0");h.transform.SetPositionAndRotation(point,facing);
                PrefabUtility.RecordPrefabInstancePropertyModifications(h.transform);placed.Add(h);
                Debug.Log("REGIONAL_P0_PLACED "+spec.id+" "+spec.name+" "+point.ToString("F3"));
            }
            var healer=actors["RepairWisp_Tower"].GetComponent<RepairWispAI>();
            var group=new SerializedObject(healer).FindProperty("group").objectReferenceValue as RepairWispGroup;
            if(!group)throw new InvalidOperationException("Tower support group missing.");
            group.Configure(new[]{actors["RuinPrism_TowerStairs"],actors["StoneScout_Awakened_02"]});
            PrefabUtility.RecordPrefabInstancePropertyModifications(group);
        }
        finally {foreach(var pair in disabled)if(pair.Key)pair.Key.enabled=pair.Value;Physics.SyncTransforms();}
        var marker=new GameObject(Marker);SceneManager.MoveGameObjectToScene(marker,scene);
        ValidateAuthoring();EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed.");
        Export();Debug.Log("REGIONAL_P0_APPLIED 17 existing enemies, six types; no prefab, stats, AI or terrain changes.");
    }
    static SafeArea[] Safety(OverworldV2Layout l,Scene scene) {
        var list=new List<SafeArea>{
            new SafeArea(l.CampPosition,14),new SafeArea(l.ArrivalPosition,8),new SafeArea(l.IntroShorePosition,10),
            new SafeArea(l.IslandPosition,13,8),new SafeArea(l.JumpPracticePosition,5),new SafeArea(l.DropLandingPosition,5),new SafeArea(l.ClimbTopPosition,6)};
        foreach(var n in Object.FindObjectsByType<NPCDialogue>(FindObjectsSortMode.None))if(n.gameObject.scene==scene)list.Add(new SafeArea(n.transform.position,3.8f));
        foreach(var e in Object.FindObjectsByType<SideWorldEntrance>(FindObjectsSortMode.None))if(e.gameObject.scene==scene)list.Add(new SafeArea(e.transform.position,10));
        foreach(var e in Object.FindObjectsByType<BaselineWaystation>(FindObjectsSortMode.None))if(e.gameObject.scene==scene)list.Add(new SafeArea(e.InteractionPosition,12));
        foreach(var g in Object.FindObjectsByType<FlameCollectionGame>(FindObjectsSortMode.None))if(g.gameObject.scene==scene) {
            list.Add(new SafeArea(g.transform.position,9));
            foreach(var f in g.flames)if(f)list.Add(new SafeArea(f.transform.position,2.7f));
            foreach(var f in g.chestAnchors)if(f)list.Add(new SafeArea(f.position,3.5f));
        }
        foreach(var g in Object.FindObjectsByType<ConstellationGame>(FindObjectsSortMode.None))if(g.gameObject.scene==scene)list.Add(new SafeArea(g.transform.position,12));
        var flow=Object.FindFirstObjectByType<BaselineFlow>();if(flow && flow.endingEntrance)list.Add(new SafeArea(flow.endingEntrance.position,8));
        return list.ToArray();
    }
    static bool Protected(SafeArea[] safe,Vector3 p,float margin=0)=>safe.Any(a=>Mathf.Abs(p.y-a.p.y)<a.height && Flat(p-a.p)<a.radius+margin);
    static bool TryPlace(Health actor,Spec spec,OverworldV2Layout layout,EnemyNavigationWorld nav,SafeArea[] safe,List<Health> selected,Dictionary<string,string> ids,out Vector3 point,out Quaternion facing) {
        point=default;facing=Quaternion.Euler(0,Quaternion.LookRotation((layout.CampPosition-spec.hint).normalized).eulerAngles.y,0);
        var partners=selected.Where(h=>ids[h.name]==spec.id).ToArray();var hint=spec.hint;
        if(partners.Length>0 && actor.GetComponent<RepairWispAI>())hint=partners[0].transform.position+Vector3.left*2.8f;
        for(int t=0;t<1400;t++) {
            float r=t==0?0:Mathf.Sqrt(t)*.5f;if(r>spec.search)break;
            var candidate=hint+new Vector3(Mathf.Cos(t*2.399963f)*r,0,Mathf.Sin(t*2.399963f)*r);
            if(!Floor(layout,candidate,out var ground))continue;candidate=ground+Vector3.up*.015f;
            if(Protected(safe,candidate,1.8f) || candidate.y<layout.LakePosition.y+.2f)continue;
            if(selected.Any(h=>Flat(h.transform.position-candidate)<(ids[h.name]==spec.id?2.2f:12)))continue;
            if(partners.Length>0 && (Mathf.Abs(candidate.y-partners[0].transform.position.y)>1.5f || Flat(candidate-partners[0].transform.position)>(actor.GetComponent<RepairWispAI>()?3.5f:9.5f)))continue;
            var domain=actor.GetComponent<DomainPrototypeEnemy>();
            var profile=actor.GetComponent<RailBeetleAI>()?EnemyNavigationWorld.Profile.RailBeetle:domain && domain.EnemyKind==DomainPrototypeEnemy.Kind.RuinPrism?EnemyNavigationWorld.Profile.RuinPrism:domain || actor.GetComponent<RepairWispAI>()?EnemyNavigationWorld.Profile.LeafWisp:EnemyNavigationWorld.Profile.StoneScout;
            if(!nav.Sample(profile,candidate,.6f,out var sample) || Flat(sample-candidate)>.45f || Mathf.Abs(sample.y-candidate.y)>.45f)continue;
            if(actor.GetComponent<RailBeetleAI>()) {
                bool lane=false;for(int n=0;n<8;n++){var q=Quaternion.Euler(0,n*45,0);actor.transform.rotation=q;
                    if(Fits(actor,candidate,out var fit) && Lane(actor,fit,q,layout,safe)){candidate=fit;facing=q;lane=true;break;}}
                if(!lane)continue;
            } else if(!Fits(actor,candidate,out candidate))continue;
            if(actor.GetComponent<StoneScoutRoadsideAI>() && partners.Any(h=>h.GetComponent<StoneScoutRoadsideAI>() && Flat(candidate-h.transform.position)<3.8f))continue;
            if(actor.GetComponent<GelBlobAI>() && !new EnemyGroundQuery(actor.transform).TryFootprint(candidate,Quaternion.identity,Vector2.one*2.7f,28,.65f,.75f,out _,true))continue;
            point=candidate;return true;
        }
        return false;
    }
    static bool Lane(Health h,Vector3 start,Quaternion q,OverworldV2Layout layout,SafeArea[] safe) {
        for(float z=-3;z<=16;z+=1)foreach(float x in new[]{-2.6f,0,2.6f}) {
            var p=start+q*new Vector3(x,0,z);
            if(!Floor(layout,p,out var floor) || Mathf.Abs(floor.y-start.y)>1.6f || Protected(safe,floor,2) || !Fits(h,floor+Vector3.up*.015f,out _))return false;
        }
        return true;
    }
    public static bool Floor(OverworldV2Layout l,Vector3 p,out Vector3 floor) {
        foreach(var hit in Physics.RaycastAll(new Vector3(p.x,140,p.z),Vector3.down,200,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
            if(l.IsNavigationSurface(hit.collider) && hit.normal.y>.85f){floor=hit.point;return true;}
        floor=default;return false;
    }
    public static bool Fits(Health h,Vector3 candidate,out Vector3 point) {
        if(!h.GetComponent<GelBlobAI>()) {
            // The existing preflight is local-pose based. Evaluate the actual candidate pose,
            // with its physical bodies enabled; preserve the original authoring state.
            var original=h.transform.position;var bodies=h.GetComponentsInChildren<Collider>(true);
            var enabled=bodies.Select(c=>c.enabled).ToArray();
            try {
                h.transform.position=candidate;for(int i=0;i<bodies.Length;i++)bodies[i].enabled=true;
                Physics.SyncTransforms();return EnemyFoundationPlacementChecks.TryFitSpawn(h,candidate,out point);
            } finally {
                h.transform.position=original;for(int i=0;i<bodies.Length;i++)bodies[i].enabled=enabled[i];Physics.SyncTransforms();
            }
        }
        point=candidate;var q=new EnemyGroundQuery(h.transform);
        if(!q.TryFootprint(candidate,Quaternion.identity,Vector2.one*1.3f,28,.65f,.75f,out float y,true))return false;
        point.y=y+.025f;var feet=point;var half=new Vector3(.96f,.564f,.96f);
        return Physics.OverlapBox(point+Vector3.up*(half.y+.03f),half,Quaternion.identity,~0,QueryTriggerInteraction.Ignore).All(c=>c.transform.IsChildOf(h.transform)||q.AllowsSupportContact(c,feet));
    }
    static float Flat(Vector3 v)=>new Vector2(v.x,v.z).magnitude;
    [MenuItem("Tools/Enemies/Validate Regional Layout P0")]
    public static void ValidateAuthoring() {
        var scene=SceneManager.GetSceneByPath(ScenePath);if(!HasLayout(scene))throw new InvalidOperationException("Regional P0 layout missing.");
        var actors=Actors(scene);var byName=actors.ToDictionary(h=>h.name);
        var layout=Object.FindFirstObjectByType<OverworldV2Layout>();var safe=Safety(layout,scene);
        if(actors.Length!=17)throw new InvalidOperationException("Expected 17 actors.");
        var roles=new[]{"StoneScout","LeafWisp","RuinPrism","RailBeetle","RepairWisp","GelBlob"};var counts=new[]{6,1,4,1,2,3};
        for(int i=0;i<roles.Length;i++)if(actors.Count(h=>Role(h)==roles[i])!=counts[i])throw new InvalidOperationException("Wrong count "+roles[i]);
        foreach(var h in actors) {
            if(Protected(safe,h.transform.position,1.25f))throw new InvalidOperationException("Protected spawn "+h.name);
            if(!Fits(h,h.transform.position,out var fit)||Vector3.Distance(fit,h.transform.position)>.055f)throw new InvalidOperationException("Unsupported spawn "+h.name);
        }
        float gap=Flat(byName["StoneScout_Sand_01"].transform.position-byName["StoneScout_Sand_02"].transform.position);
        if(gap<3.7f||gap>10.8f)throw new InvalidOperationException("Scout spacing invalid.");
        foreach(var name in new[]{"RepairWisp_HiddenChest","RepairWisp_Tower"}) {
            var healer=byName[name].GetComponent<RepairWispAI>();
            var group=new SerializedObject(healer).FindProperty("group").objectReferenceValue as RepairWispGroup;
            var expected=name=="RepairWisp_Tower"?new[]{byName["RuinPrism_TowerStairs"],byName["StoneScout_Awakened_02"]}:new[]{byName["RuinPrism_WestRuins"]};
            if(!group||group.Guards.Length!=expected.Length||expected.Any(h=>!group.Contains(h))||expected.Any(h=>Vector3.Distance(h.transform.position,healer.transform.position)>9))throw new InvalidOperationException("Repair membership invalid "+name);
        }
        Debug.Log("REGIONAL_P0_VALIDATED 17 supported spawns, six counts, safe-site clearance, Scout pair and explicit repair memberships.");
    }
    public static void Export() {
        Directory.CreateDirectory(Output);var actors=Actors(SceneManager.GetSceneByPath(ScenePath)).ToDictionary(h=>h.name);
        var csv=new List<string>{"encounter,role,name,x,y,z"};
        foreach(var s in Specs()){var h=actors[s.name];var p=h.transform.position;csv.Add(string.Join(",",s.id,Role(h),h.name,p.x.ToString("F3",CultureInfo.InvariantCulture),p.y.ToString("F3",CultureInfo.InvariantCulture),p.z.ToString("F3",CultureInfo.InvariantCulture)));}
        File.WriteAllLines(Output+"/actual-layout.csv",csv);
    }
}
