using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Authoring entry points are restricted to the disposable integration checkout.
// Source models/materials/clips, existing encounters and navigation data are read-only.
public static class AdventureTrioInstaller
{
    public const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    public const string Folder = "Assets/Art/Enemies/AdventureTrio/Generated";
    public const string Output = "LocalDocs/Enemies/AdventureTrio-20261021";
    const string Source = "Assets/RPGMonsterWave03PBR";
    const string Marker = "_AdventureTrio_v1";
    const string LootSource = "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab";
    static readonly string[] Common = { "IdleNormal", "IdleBattle", "WalkFWD", "WalkBWD", "WalkLFT", "WalkRGT", "RunFWD",
        "SenseSomethingStart", "SenseSomethingMaint", "Attack01", "Attack02", "GetHit", "Dizzy", "Taunting", "Victory", "Die" };
    sealed class Spec
    {
        public string kind, nativePrefab, actor; public Type ai; public float height, radius; public int hp;
        public Vector3[] anchors; public Vector3 site;
        public string[] Actions => kind == "Mushroom" ? Common.Concat(new[] { "IdlePlant", "IdlePlantToBattle", "Attack03", "SenseSomethingMaintain" }).ToArray() :
            kind == "BishopKnight" ? Common.Concat(new[] { "Defend", "DefendHit" }).ToArray() : Common;
    }
    sealed class Original
    {
        public Health health; public Vector3 position, scale; public Quaternion rotation; public string prefab;
    }
    static Spec[] Specs() => new[] {
        new Spec {kind="Fishman",nativePrefab="FishmanPBR",actor="Fishman_LakeBank_01",ai=typeof(FishmanAI),height=1.45f,hp=50,
            anchors=new[]{new Vector3(-2,0,30),new Vector3(-4,0,38)}},
        new Spec {kind="Mushroom",nativePrefab="MushroomSmilePBR",actor="SmilingMushroom_Forest_01",ai=typeof(SmilingMushroomAI),height=.72f,hp=30,
            anchors=new[]{new Vector3(26,0,35),new Vector3(35,0,38)}},
        new Spec {kind="BishopKnight",nativePrefab="BishopKnightPBR",actor="BishopKnight_NorthRuins_01",ai=typeof(BishopKnightAI),height=3.15f,radius=.8f,hp=240,
            anchors=new[]{new Vector3(5,0,88),new Vector3(18,0,86)}} };

    public static void AuditAndBuild() { Audit(); Build(); }
    public static void Audit()
    {
        Guard(); RequireCleanScenes(); Directory.CreateDirectory(Output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var samples = new StringBuilder("kind,action,normalized,seconds,baseX,baseY,baseZ,tipX,tipY,tipZ,bodyMinX,bodyMinY,bodyMinZ,bodyMaxX,bodyMaxY,bodyMaxZ\n");
        var clips = new StringBuilder("kind,action,length,source,weapon\n");
        try
        {
            foreach (var spec in Specs())
            {
                var visual = Object.Instantiate(Native(spec));
                try
                {
                    visual.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); Fit(visual, spec);
                    MeshFilter weapon = Weapon(visual, spec); Vector3 a = Vector3.zero, b = Vector3.zero;
                    if (weapon) Endpoints(weapon.sharedMesh, out a, out b);
                    foreach (string action in spec.Actions)
                    {
                        var clip = Clip(spec, action);
                        clips.AppendLine(FormattableString.Invariant($"{spec.kind},{action},{clip.length:F6},{ClipPath(spec, action)},{(weapon ? weapon.name : "body")}"));
                        for (int i = 0; i <= 20; i++)
                        {
                            SamplePreservingRoot(visual, clip, clip.length * i / 20f);
                            Bounds body = BodyBounds(visual); Vector3 start = weapon ? weapon.transform.TransformPoint(a) : body.center;
                            Vector3 end = weapon ? weapon.transform.TransformPoint(b) : body.center;
                            samples.AppendLine(FormattableString.Invariant($"{spec.kind},{action},{i / 20f:F2},{clip.length * i / 20f:F6},{start.x:F5},{start.y:F5},{start.z:F5},{end.x:F5},{end.y:F5},{end.z:F5},{body.min.x:F5},{body.min.y:F5},{body.min.z:F5},{body.max.x:F5},{body.max.y:F5},{body.max.z:F5}"));
                        }
                    }
                }
                finally { Object.DestroyImmediate(visual); }
            }
            File.WriteAllText(Output + "/native-clips.csv", clips.ToString());
            File.WriteAllText(Output + "/native-samples.csv", samples.ToString());
            Debug.Log("ADVENTURE_TRIO AUDIT: source clip lengths and .05 normalized whole-weapon/body samples written to " + Output);
        }
        finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
    }

    public static void Build()
    {
        Guard(); RequireCleanScenes(); var scene = EditorSceneManager.OpenScene(ScenePath);
        if (scene.GetRootGameObjects().Any(go => go.name == Marker)) { ValidateScene(scene); return; }
        var originals = Enemies(scene).Select(h => new Original { health = h, position = h.transform.position,
            rotation = h.transform.rotation, scale = h.transform.localScale, prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(h.gameObject) }).ToArray();
        Need(originals.Length == 17, "Expected the existing 17 encounters before authoring; found " + originals.Length);
        var layout = Find<OverworldV2Layout>(scene).Single(); var nav = EnemyNavigationWorld.Find(scene);
        Need(nav && nav.Ready(EnemyNavigationWorld.Profile.StoneScout), "Authored StoneScout navigation must already be ready; no bake is performed.");
        var specs = Specs();
        foreach (var spec in specs)
        {
            var preview = Object.Instantiate(Native(spec));
            try { preview.transform.position = new Vector3(24000, 100, 24000); Fit(preview, spec); }
            finally { Object.DestroyImmediate(preview); }
        }
        // Resolve every site before generating assets or adding scene encounters.
        var probe = new GameObject("Temporary adventure placement query"); var chosen = new List<Vector3>();
        try
        {
            var query = new EnemyGroundQuery(probe.transform);
            foreach (var spec in specs) { spec.site = Choose(spec, scene, layout, nav, query, chosen); chosen.Add(spec.site); }
        }
        finally { Object.DestroyImmediate(probe); }
        EnsureFolder(Folder); Directory.CreateDirectory(Output);
        string materialPath = Folder + "/LocalImpact.mat";
        var impact = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!impact)
        {
            var shader = Resources.Load<Shader>("EnemyLocalImpact"); Need(shader, "Shared local-impact shader exists");
            impact = new Material(shader) { name = "Adventure creature local impact" }; AssetDatabase.CreateAsset(impact, materialPath);
        }
        var group = new GameObject(Marker); var placements = new StringBuilder("kind,name,x,y,z,height,radius,health,nativePrefab\n");
        try
        {
            foreach (var spec in specs)
            {
                var prefab = BuildPrefab(spec, impact);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = spec.actor; instance.transform.SetParent(group.transform, true);
                Vector3 toward = spec.kind == "Fishman" ? layout.LakePosition - spec.site : layout.CampPosition - spec.site; toward.y = 0;
                instance.transform.SetPositionAndRotation(spec.site, toward.sqrMagnitude > .01f ? Quaternion.LookRotation(toward) : Quaternion.identity);
                placements.AppendLine(FormattableString.Invariant($"{spec.kind},{spec.actor},{spec.site.x:F5},{spec.site.y:F5},{spec.site.z:F5},{spec.height:F5},{spec.radius:F5},{spec.hp},{NativePath(spec)}"));
            }
            Physics.SyncTransforms();
            foreach (var old in originals)
                Need(old.health && old.health.transform.position == old.position && old.health.transform.rotation == old.rotation && old.health.transform.localScale == old.scale &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.health.gameObject) == old.prefab, "Original encounter placement/reference preserved: " + old.health.name);
            ValidateScene(scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            Need(EditorSceneManager.SaveScene(scene), "Authored integration scene saved");
            File.WriteAllText(Output + "/placements.csv", placements.ToString());
            File.WriteAllLines(Output + "/generated-files.txt", AssetDatabase.GetAllAssetPaths().Where(p => p == Folder || p.StartsWith(Folder + "/", StringComparison.Ordinal)).OrderBy(p => p));
            File.WriteAllLines(Output + "/preserved-encounters.txt", originals.Select(o => o.health.name + " | " + o.position.ToString("F5") + " | " + o.prefab));
            Debug.Log("ADVENTURE_TRIO BUILD PASS: three independent native creatures; 17 original encounters retained, 20 total. " + Output);
        }
        catch { Object.DestroyImmediate(group); throw; }
    }

    public static void RelocateMushroomForReadability()
    {
        Guard(); RequireCleanScenes();
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath);
        ValidateScene(scene);
        var group = scene.GetRootGameObjects().Single(go => go.name == Marker);
        var spec = Specs().Single(s => s.kind == "Mushroom");
        var actor = group.transform.Cast<Transform>().Single(t => t.name == spec.actor).gameObject;
        var shape = actor.GetComponent<CapsuleCollider>(); spec.radius = shape.radius; spec.height = shape.height;
        var protectedActors = Enemies(scene).Where(h => h.gameObject != actor).Select(h => new Original { health = h,
            position = h.transform.position, rotation = h.transform.rotation, scale = h.transform.localScale,
            prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(h.gameObject) }).ToArray();
        Need(protectedActors.Length == 19, "Relocation protects the original 17 plus the Fishman and BishopKnight");
        var layout = Find<OverworldV2Layout>(scene).Single(); var nav = EnemyNavigationWorld.Find(scene);
        Need(nav && nav.Ready(EnemyNavigationWorld.Profile.StoneScout), "Existing navigation is required; relocation does not bake or rebuild assets.");
        string layoutBefore = EditorJsonUtility.ToJson(layout);
        Vector3 originalPosition = actor.transform.position; Quaternion originalRotation = actor.transform.rotation;
        bool saved = false;
        try
        {
            // The prior (39,20) point lies in dense instanced garden foliage. Only
            // the two explicit forest-edge alternatives are eligible for this move.
            spec.site = Choose(spec, scene, layout, nav, new EnemyGroundQuery(actor.transform), new List<Vector3>(), actor);
            actor.transform.position = spec.site; Physics.SyncTransforms();
            foreach (var old in protectedActors)
                Need(old.health && old.health.transform.position == old.position && old.health.transform.rotation == old.rotation &&
                    old.health.transform.localScale == old.scale && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.health.gameObject) == old.prefab,
                    "Protected encounter unchanged during mushroom relocation");
            Need(EditorJsonUtility.ToJson(layout) == layoutBefore, "Terrain/navigation layout references remain unchanged");
            ValidateScene(scene);
            var placements = new StringBuilder("kind,name,x,y,z,height,radius,health,nativePrefab\n");
            foreach (var current in Specs())
            {
                var instance = group.transform.Cast<Transform>().Single(t => t.name == current.actor).gameObject;
                var capsule = instance.GetComponent<CapsuleCollider>(); Vector3 point = instance.transform.position;
                placements.AppendLine(FormattableString.Invariant($"{current.kind},{current.actor},{point.x:F5},{point.y:F5},{point.z:F5},{capsule.height:F5},{capsule.radius:F5},{instance.GetComponent<Health>().MaxHealth},{NativePath(current)}"));
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
            EditorSceneManager.MarkSceneDirty(scene); saved = EditorSceneManager.SaveScene(scene);
            Need(saved, "Validated mushroom-only scene relocation saved");
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/placements.csv", placements.ToString());
            File.WriteAllText(Output + "/mushroom-readability-relocation.txt",
                "Only the new smiling mushroom was relocated. No prefab, original encounter or vegetation rebuilt/removed.\n" +
                "Before: " + originalPosition.ToString("F5") + "; after: " + spec.site.ToString("F5") + ".\n" +
                "Strict footprint, clearance, existing navigation and protected distances revalidated; 19 other encounter placements/references unchanged.\n" +
                "The selected forest-edge site still requires controlled map-view visual review.\n" +
                string.Join("\n", protectedActors.Select(o => o.health.name + " | " + o.position.ToString("F5") + " | " + o.prefab)) + "\n");
            Debug.Log("ADVENTURE_TRIO MUSHROOM RELOCATION PASS: " + originalPosition.ToString("F5") + " -> " + spec.site.ToString("F5") + "; all other 19 encounters retained.");
        }
        catch
        {
            if (!saved) { actor.transform.SetPositionAndRotation(originalPosition, originalRotation); Physics.SyncTransforms(); }
            throw;
        }
    }

    public static void Validate()
    {
        Guard(); var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) { RequireCleanScenes(); scene = EditorSceneManager.OpenScene(ScenePath); }
        ValidateScene(scene); // Deliberately no SaveAssets, SaveScene, or report writes.
    }
    internal static void ValidateScene(Scene scene)
    {
        var group = scene.GetRootGameObjects().SingleOrDefault(go => go.name == Marker);
        Need(group && group.transform.childCount == 3, "One marker owns exactly three new encounter roots");
        Need(Enemies(scene).Length == 20, "Exactly 20 encounters, including the original 17");
        Need(Find<StoneScoutRoadsideAI>(scene).Length == 6 && Find<DemonKingCombatVisual>(scene).Length == 1,
            "Six original Scouts and the single DemonKing trial remain");
        var layout = Find<OverworldV2Layout>(scene).Single(); var nav = EnemyNavigationWorld.Find(scene);
        foreach (var spec in Specs())
        {
            var actor = group.transform.Cast<Transform>().Single(t => t.name == spec.actor).gameObject;
            var ai = actor.GetComponent<AdventureCreatureAI>(); var shape = actor.GetComponent<CapsuleCollider>();
            var style = actor.GetComponent<AdventureCreatureStyle>();
            // The style entry point upgrades this already installed encounter.
            // Keep read-only validation of its known original native form useful
            // before that upgrade, including when a first asset staging fails.
            bool originalKnight = spec.kind == "BishopKnight" && !style && actor.GetComponent<Health>().MaxHealth == 100 &&
                shape && Mathf.Abs(shape.height - 2.15f) < .025f;
            if (originalKnight) { spec.height = 2.15f; spec.radius = .55f; spec.hp = 100; }
            Need(ai && ai.GetType() == spec.ai && actor.GetComponent<Health>().Team == HealthTeam.Enemy && actor.GetComponent<Health>().MaxHealth == spec.hp,
                "Separate species AI and configured enemy Health: " + spec.actor);
            Need(actor.transform.localScale == Vector3.one && actor.GetComponent<Rigidbody>().isKinematic && !actor.GetComponent<Rigidbody>().useGravity &&
                actor.GetComponent<StoneScoutLootDrop>() && shape && Mathf.Abs(shape.height - spec.height) < .025f, "Unit-scale physical root, body capsule and shared loot: " + spec.actor);
            Need(!actor.GetComponent<StoneScoutRoadsideAI>() && !actor.GetComponent<StoneScoutCombatTarget>() && !actor.GetComponentInChildren<StoneScoutVisualMotion>(true),
                "No legacy Scout brain or visual on the new species: " + spec.actor);
            Need(ai.animator && ai.animator.runtimeAnimatorController && !ai.animator.applyRootMotion && ai.impactMaterial, "Native animator and local feedback references: " + spec.actor);
            foreach (string action in spec.Actions) Need(ai.animator.runtimeAnimatorController.animationClips.Any(c => c.name == action), "Native clip " + spec.kind + "/" + action);
            var nativeMaterials = Native(spec).GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).ToArray();
            if (style) AdventureTrioStyleInstaller.ValidateStyledActor(actor, spec.kind);
            else Need(actor.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).All(m => nativeMaterials.Contains(m)),
                "Original material references retained before style installation: " + spec.actor);
            if (spec.kind == "BishopKnight")
                Need(Mathf.Abs(shape.radius - (originalKnight ? .55f : .8f)) < .001f && Mathf.Abs(shape.center.y - spec.height * .5f) < .001f &&
                    Mathf.Abs(ai.noticeRange - (originalKnight ? 8f : 9f)) < .001f && Mathf.Abs(ai.leashRange - (originalKnight ? 9f : 11f)) < .001f &&
                    Mathf.Abs(ai.walkSpeed - (originalKnight ? 1.3f : 1.6f)) < .001f,
                    "Single Knight encounter has its original or styled capsule and territorial movement configuration");
            if (spec.kind != "Mushroom") Need(ai.weapon && Vector3.Distance(ai.weaponBaseLocal, ai.weaponTipLocal) > .01f, "Whole spear endpoints assigned: " + spec.actor);
            else Need(ai.bodyContact && ai.bodyContact.name == "mushroom_head" && Mathf.Abs(ai.bodyContactRadius - .28f) < .001f,
                "Native mushroom head drives body contact with the configured radius");
            spec.radius = shape.radius;
            Need(SafeSite(spec, actor.transform.position, scene, layout, nav, new EnemyGroundQuery(actor.transform), new List<Vector3>(), actor, out string reason),
                "Placement has supported footing, clearance/navigation and protected spacing: " + spec.actor + " (" + reason + ")");
        }
        Debug.Log("ADVENTURE_TRIO VALIDATE PASS (read-only)");
    }

    static GameObject BuildPrefab(Spec spec, Material impact)
    {
        string path = Folder + "/" + spec.kind; EnsureFolder(path);
        string prefabPath = Folder + "/" + (spec.kind == "Mushroom" ? "SmilingMushroom" : spec.kind) + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath); if (existing) return existing;
        string controllerPath = path + "/Native.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        foreach (string action in spec.Actions)
        {
            string clipPath = path + "/" + action + ".anim"; var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (!copy)
            {
                copy = Object.Instantiate(Clip(spec, action)); copy.name = action;
                var settings = AnimationUtility.GetAnimationClipSettings(copy);
                settings.loopTime = action.StartsWith("Idle", StringComparison.Ordinal) && action != "IdlePlantToBattle" || action.StartsWith("Walk", StringComparison.Ordinal) ||
                    action == "RunFWD" || action == "SenseSomethingMaint" || action == "SenseSomethingMaintain" || action == "Defend" || action == "Dizzy";
                settings.loopBlend = settings.loopTime; AnimationUtility.SetAnimationClipSettings(copy, settings); AssetDatabase.CreateAsset(copy, clipPath);
            }
            var state = controller.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == action);
            if (!state) state = controller.layers[0].stateMachine.AddState(action);
            state.motion = copy;
            if (action == (spec.kind == "Mushroom" ? "IdlePlant" : "IdleNormal")) controller.layers[0].stateMachine.defaultState = state;
        }
        var root = new GameObject(spec.kind);
        try
        {
            var health = root.AddComponent<Health>(); var data = new SerializedObject(health);
            data.FindProperty("maxHealth").intValue = spec.hp; data.FindProperty("team").enumValueIndex = (int)HealthTeam.Enemy; data.ApplyModifiedPropertiesWithoutUndo();
            var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            var shape = root.AddComponent<CapsuleCollider>(); root.AddComponent<NonClimbableSurface>();
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(Native(spec), root.transform); visual.name = spec.nativePrefab + " native visual"; Fit(visual, spec);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            shape.height = spec.height; shape.radius = spec.radius; shape.center = Vector3.up * (spec.height * .5f);
            var animator = visual.GetComponentInChildren<Animator>(); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            var ai = (AdventureCreatureAI)root.AddComponent(spec.ai); ai.animator = animator; ai.impactMaterial = impact;
            if (spec.kind == "Mushroom")
            {
                ai.bodyContact = visual.GetComponentsInChildren<Transform>(true).Single(t => t.name == "mushroom_head");
                ai.bodyContactRadius = .28f;
            }
            ai.noticeRange = spec.kind == "Mushroom" ? 5f : spec.kind == "Fishman" ? 7f : 9f;
            ai.leashRange = spec.kind == "Mushroom" ? 6.5f : spec.kind == "Fishman" ? 8f : 11f;
            ai.walkSpeed = spec.kind == "Mushroom" ? 1.25f : spec.kind == "Fishman" ? 1.8f : 1.6f;
            ai.warningColor = spec.kind == "Mushroom" ? new Color(.6f,.9f,.2f) : spec.kind == "Fishman" ? new Color(.2f,.75f,.85f) : new Color(1f,.75f,.2f);
            var weapon = Weapon(visual, spec);
            if (weapon) { ai.weapon = weapon.transform; Endpoints(weapon.sharedMesh, out ai.weaponBaseLocal, out ai.weaponTipLocal); }
            var loot = root.AddComponent<StoneScoutLootDrop>(); EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<GameObject>(LootSource).GetComponent<StoneScoutLootDrop>(), loot);
            var feedback = root.AddComponent<CombatFeedback>(); var fields = new SerializedObject(feedback); var renderers = visual.GetComponentsInChildren<Renderer>(true);
            var list = fields.FindProperty("targetRenderers"); list.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i]; fields.ApplyModifiedPropertiesWithoutUndo();
            return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static Vector3 Choose(Spec spec, Scene scene, OverworldV2Layout layout, EnemyNavigationWorld nav, EnemyGroundQuery query, List<Vector3> chosen, GameObject self = null)
    {
        var failures = new Dictionary<string, int>(); var diagnosed = new HashSet<int>();
        foreach (var anchor in spec.anchors)
            foreach (var offset in Enumerable.Range(-4, 9).SelectMany(x => Enumerable.Range(-4, 9).Select(z => new Vector3(x * 2, 0, z * 2))).Where(v => v.sqrMagnitude <= 64).OrderBy(v => v.sqrMagnitude))
            {
                Vector3 above = anchor + offset; above.y = 100;
                if (!query.TrySample(above, 0, 250, 25, out var hit)) { Count(failures, "floor/water/slope"); continue; }
                Vector3 point = hit.point + Vector3.up * .015f;
                if (SafeSite(spec, point, scene, layout, nav, query, chosen, self, out string reason, obstacle =>
                {
                    if (diagnosed.Count < 16 && diagnosed.Add(obstacle.GetInstanceID()))
                        Debug.Log("ADVENTURE_TRIO BLOCKER " + spec.kind + " candidate=" + point.ToString("F5") + " " + BlockerDiagnostic(obstacle, point, query));
                }))
                {
                    Debug.Log("ADVENTURE_TRIO CHOSEN " + spec.kind + " point=" + point.ToString("F5") + " anchor=" + anchor.ToString("F3") +
                        " height=" + spec.height.ToString("F4") + " radius=" + spec.radius.ToString("F4") + " rejected=" + string.Join(",", failures.Select(p => p.Key + "=" + p.Value)));
                    return point;
                }
                Count(failures, reason);
            }
        throw new InvalidOperationException("No safe authored site within 8m of " + spec.kind + " anchors; no broad fallback. " + string.Join(", ", failures.Select(p => p.Key + "=" + p.Value)));
    }
    static bool SafeSite(Spec spec, Vector3 point, Scene scene, OverworldV2Layout layout, EnemyNavigationWorld nav, EnemyGroundQuery query, List<Vector3> chosen, GameObject self, out string reason, Action<Collider> onBlocker = null)
    {
        reason = "footprint";
        if (!query.TryFootprint(point + Vector3.up * .2f, Quaternion.identity, Vector2.one * (spec.radius + .10f), 25, .5f, .9f, out float y, true) || Mathf.Abs(y + .015f - point.y) > .16f) return false;
        reason = "navigation";
        if (!nav || !nav.Sample(EnemyNavigationWorld.Profile.StoneScout, point, .75f, out var mapped) || Flat(mapped - point) > .55f || Mathf.Abs(mapped.y - point.y) > .25f) return false;
        reason = "safe anchor";
        var protectedSites = new[] { layout.ArrivalPosition, layout.CampPosition, layout.IslandDestination, layout.IslandRestPosition, layout.IntroShorePosition, layout.IslandSwimEntry, layout.IslandSwimExit };
        if (protectedSites.Any(p => Flat(p - point) < 15f)) return false;
        reason = "other enemy";
        if (Enemies(scene).Any(h => h.gameObject != self && Flat(h.transform.position - point) < 12f) || chosen.Any(p => Flat(p - point) < 12f)) return false;
        reason = "interaction";
        foreach (var component in Find<MonoBehaviour>(scene))
        {
            if (component is NPCDialogue || component is BaselineInteraction || component is SideWorldEntrance || component is ChestDissolveController || component is BaselineWaystation)
            {
                Vector3 anchor = component is BaselineWaystation station ? station.InteractionPosition : component.transform.position;
                if (Flat(anchor - point) < 9f) return false;
            }
        }
        reason = "road/pier/exit";
        foreach (var floor in Physics.OverlapSphere(point + Vector3.up * .1f, .75f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (RoadName(floor.name)) return false;
        }
        reason = "body clearance";
        float half = Mathf.Max(0, spec.height * .5f - spec.radius); Vector3 centre = point + Vector3.up * (spec.height * .5f);
        foreach (var obstacle in Physics.OverlapCapsule(centre - Vector3.up * half, centre + Vector3.up * half, spec.radius + .035f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (obstacle.gameObject.scene != scene || (self && obstacle.transform.IsChildOf(self.transform))) continue;
            // The shared query verifies the local supported patch, including its
            // narrow downward-ray policy for concave meshes and TerrainColliders.
            if (query.AllowsSupportContact(obstacle, point)) continue;
            onBlocker?.Invoke(obstacle); return false;
        }
        reason = "supported"; return true;
    }
    internal static bool StyledKnightSiteSafe(Scene scene, GameObject actor, Vector3 point, out string reason)
    {
        var spec = Specs().Single(s => s.kind == "BishopKnight");
        return SafeSite(spec, point, scene, Find<OverworldV2Layout>(scene).Single(), EnemyNavigationWorld.Find(scene),
            new EnemyGroundQuery(actor.transform), new List<Vector3>(), actor, out reason);
    }
    internal static Vector3 FindStyledKnightSite(Scene scene, GameObject actor)
    {
        var spec = Specs().Single(s => s.kind == "BishopKnight");
        spec.anchors = new[] { new Vector3(actor.transform.position.x, 0, actor.transform.position.z) };
        return Choose(spec, scene, Find<OverworldV2Layout>(scene).Single(), EnemyNavigationWorld.Find(scene),
            new EnemyGroundQuery(actor.transform), new List<Vector3>(), actor);
    }
    internal static void FitStyledKnight(GameObject visual)
    {
        Need(visual && visual.transform.parent, "Styled Knight visual belongs to its unit-scale physical root");
        Fit(visual, Specs().Single(s => s.kind == "BishopKnight"), visual.transform.parent.position);
    }
    static bool SupportsClosestPoint(Collider collider) => collider is BoxCollider || collider is SphereCollider ||
        collider is CapsuleCollider || collider is CharacterController || collider is MeshCollider mesh && mesh.convex;
    static string BlockerDiagnostic(Collider collider, Vector3 feet, EnemyGroundQuery query)
    {
        bool closestSupported = SupportsClosestPoint(collider), surface = query.IsSurface(collider);
        string closest = closestSupported ? collider.ClosestPoint(feet + Vector3.up * .1f).ToString("F5") : "unsupported (concave/other collider)";
        bool supported = query.AllowsSupportContact(collider, feet);
        bool ray = collider.Raycast(new Ray(feet + Vector3.up * .6f, Vector3.down), out RaycastHit hit, 1.2f);
        string path = collider.name; for (var parent = collider.transform.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
        return "name=" + path + " type=" + collider.GetType().Name + " surface=" + surface + " supportContact=" + supported +
            " closestFeet=" + closest + " feetY=" + feet.y.ToString("F5") + " boundsMinY=" + collider.bounds.min.y.ToString("F5") +
            " boundsMaxY=" + collider.bounds.max.y.ToString("F5") + " convex=" + (collider is MeshCollider m ? m.convex.ToString() : "n/a") +
            " diagnosticDownRay=" + ray + (ray ? " rayY=" + hit.point.y.ToString("F5") + " rayNormalY=" + hit.normal.y.ToString("F5") : "");
    }

    static string NativePath(Spec spec) => Source + "/Prefab/Characters/" + spec.nativePrefab + ".prefab";
    static GameObject Native(Spec spec)
    { var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NativePath(spec)); Need(prefab, "Native prefab missing: " + NativePath(spec)); return prefab; }
    static string ClipPath(Spec spec, string action)
    {
        string stem = action;
        if (spec.kind == "Mushroom")
        {
            if (stem.StartsWith("Walk", StringComparison.Ordinal)) stem = "walk" + stem.Substring(4);
            if (stem == "RunFWD") stem = "runFWD";
            if (stem == "SenseSomethingMaint") stem = "SenseSomethingMaintain";
            if (stem != "IdlePlant") stem += "Smile";
        }
        return Source + "/Animation/" + spec.kind + "/" + spec.kind + "_" + stem + ".fbx";
    }
    static AnimationClip Clip(Spec spec, string action)
    { var clip = AssetDatabase.LoadAllAssetsAtPath(ClipPath(spec, action)).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)); Need(clip, "Native clip missing: " + ClipPath(spec, action)); return clip; }
    static void Fit(GameObject visual, Spec spec, Vector3? feetAnchor = null)
    {
        Vector3 feet = feetAnchor ?? visual.transform.position;
        var idle = Clip(spec, "IdleNormal"); SamplePreservingRoot(visual, idle, 0); Bounds bounds = BodyBounds(visual);
        Need(bounds.size.y > .001f, "Native skinned body has measurable height: " + spec.kind);
        float nativeHeight = bounds.size.y;
        Debug.Log("ADVENTURE_TRIO FIT BEFORE " + spec.kind + " target=" + spec.height.ToString("F6") + " bakedHeight=" + nativeHeight.ToString("F6") + " " + ScaleDiagnostic(visual));
        visual.transform.localScale *= spec.height / nativeHeight;
        SamplePreservingRoot(visual, idle, 0); Physics.SyncTransforms(); bounds = BodyBounds(visual);
        float scaledHeight = bounds.size.y;
        Debug.Log("ADVENTURE_TRIO FIT SCALED " + spec.kind + " nativeHeight=" + nativeHeight.ToString("F6") + " bakedHeight=" + scaledHeight.ToString("F6") + " " + ScaleDiagnostic(visual));
        visual.transform.position += new Vector3(feet.x - bounds.center.x, feet.y - bounds.min.y, feet.z - bounds.center.z);
        Physics.SyncTransforms();
        bounds = BodyBounds(visual);
        spec.radius = spec.kind == "BishopKnight" ? .8f : Mathf.Min(spec.height * .45f,
            Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * .48f, spec.kind == "Mushroom" ? .20f : .25f, .45f));
        Debug.Log("ADVENTURE_TRIO FIT FINAL " + spec.kind + " bakedHeight=" + bounds.size.y.ToString("F6") + " bounds=" + bounds.ToString("F6") + " " + ScaleDiagnostic(visual));
        Need(Mathf.Abs(bounds.size.y - spec.height) < .02f, "Computed native visual height mismatch: " + spec.kind +
            " target=" + spec.height.ToString("F6") + " native=" + nativeHeight.ToString("F6") + " afterScale=" + scaledHeight.ToString("F6") +
            " final=" + bounds.size.y.ToString("F6") + " " + ScaleDiagnostic(visual));
    }
    static void SamplePreservingRoot(GameObject visual, AnimationClip clip, float time)
    {
        var root = visual.transform; Vector3 position = root.localPosition, scale = root.localScale; Quaternion rotation = root.localRotation;
        clip.SampleAnimation(visual, time);
        // The encounter root owns world placement and authored scale; source clip
        // root curves must not undo those while evaluating the skeleton for fitting.
        root.localPosition = position; root.localRotation = rotation; root.localScale = scale;
    }
    static string ScaleDiagnostic(GameObject visual)
    {
        return "visualLocal=" + visual.transform.localScale.ToString("F6") + " visualLossy=" + visual.transform.lossyScale.ToString("F6") + " " +
            string.Join(";", visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s =>
                s.name + " local=" + s.transform.localScale.ToString("F6") + " lossy=" + s.transform.lossyScale.ToString("F6") +
                " localBounds=" + s.localBounds.size.ToString("F6") + " renderedBounds=" + s.bounds.size.ToString("F6")));
    }
    static Bounds BodyBounds(GameObject visual)
    {
        bool found = false; Bounds result = default; var mesh = new Mesh();
        try
        {
            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Unity 6000: true compensates the renderer Transform's scale;
                // TransformPoint then applies that scale exactly once to the snapshot.
                skin.BakeMesh(mesh, true);
                foreach (var vertex in mesh.vertices)
                { Vector3 p = skin.transform.TransformPoint(vertex); if (!found) { result = new Bounds(p, Vector3.zero); found = true; } else result.Encapsulate(p); }
                mesh.Clear();
            }
        }
        finally { Object.DestroyImmediate(mesh); }
        Need(found, "Native source needs a skinned body"); return result;
    }
    static MeshFilter Weapon(GameObject visual, Spec spec)
    {
        if (spec.kind == "Mushroom") return null;
        var weapon = visual.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh &&
            (f.name.IndexOf("harpoon", StringComparison.OrdinalIgnoreCase) >= 0 || f.name.IndexOf("spear", StringComparison.OrdinalIgnoreCase) >= 0));
        Need(weapon, "Whole native harpoon/spear mesh missing: " + spec.kind); return weapon;
    }
    static void Endpoints(Mesh mesh, out Vector3 a, out Vector3 b)
    { Bounds bounds = mesh.bounds; int axis = bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z ? 0 : bounds.size.y >= bounds.size.z ? 1 : 2; a = b = bounds.center; a[axis] = bounds.min[axis]; b[axis] = bounds.max[axis]; }
    static T[] Find<T>(Scene scene) where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.gameObject.scene == scene).ToArray();
    static Health[] Enemies(Scene scene) => Find<Health>(scene).Where(h => h.Team == HealthTeam.Enemy).ToArray();
    static float Flat(Vector3 delta) => new Vector2(delta.x, delta.z).magnitude;
    static bool RoadName(string name) => name.ToLowerInvariant().Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(word => word.TrimEnd('0','1','2','3','4','5','6','7','8','9'))
        .Any(word => word == "road" || word == "roads" || word == "path" || word == "paths" || word == "route" || word == "routes" ||
            word == "pier" || word == "piers" || word == "bridge" || word == "bridges" || word == "stair" || word == "stairs");
    static void Count(Dictionary<string, int> counts, string key) { counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1; }
    static void EnsureFolder(string path)
    { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
    static void Guard()
    {
        if (!Application.dataPath.StartsWith("/private/tmp/adventure-trio-", StringComparison.Ordinal)) throw new InvalidOperationException("Disposable adventure-trio scratch project only.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before native creature authoring.");
    }
    static void RequireCleanScenes()
    { for (int i = 0; i < SceneManager.sceneCount; i++) Need(!SceneManager.GetSceneAt(i).isDirty, "Save existing scene edits before authoring; none were discarded."); }
    static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
