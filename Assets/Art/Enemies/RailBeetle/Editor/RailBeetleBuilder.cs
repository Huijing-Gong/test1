using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Explicit authoring only: importing this script never edits the open scene.
public static class RailBeetleBuilder
{
    public const string PrefabPath = "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab";
    public const string MeshPath = "Assets/Art/Enemies/RailBeetle/RailBeetleMeshes.asset";
    public const string GroupName = "_RailBeetleEncounters";
    public const string InstanceName = "RailBeetle_PathGuard";
    private const string Folder = "Assets/Art/Enemies/RailBeetle";
    private const string SharedMaterials = "Assets/Art/Enemies/DomainPrototypes/Generated/";
    private const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    private const string LootSource = "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab";

    [MenuItem("Tools/Art/Build and Place Rail Beetle")]
    public static void BuildAndPlace()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Leave Play mode before building the Rail Beetle.");
        if (Application.isBatchMode)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Dirty scene detected; batch Rail Beetle setup will not overwrite it.");
        }
        else if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            // Validate placement BEFORE building assets or mutating the scene.
            if (scene.isDirty) throw new InvalidOperationException("Save OverworldBaseline before placing the Rail Beetle.");
            MeshCollider terrain = FindTerrain(scene);
            if (!terrain) throw new InvalidOperationException("V2 walking terrain missing; scene unchanged.");
            Transform group = FindGroup(scene);
            Transform existing = group ? group.Find(InstanceName) : null;
            if (group && group.childCount > 0 && !existing)
                throw new InvalidOperationException("Unrecognized Rail Beetle group; existing objects left unchanged.");
            Physics.SyncTransforms();
            Vector3 place = Vector3.zero;
            if (!existing && !FindClearGround(scene, terrain, new Vector2(41f, 34f), 10, out place))
                throw new InvalidOperationException("No safe, clear Rail Beetle corridor near (41, 34); scene unchanged.");

            GameObject prefab = BuildPrefab();
            if (existing)
            {
                Debug.Log("Rail Beetle shared assets updated; existing placed guard was preserved.");
                return;
            }
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Place Rail Beetle Path Guard");
            try
            {
                if (!group)
                {
                    GameObject holder = new GameObject(GroupName);
                    SceneManager.MoveGameObjectToScene(holder, scene);
                    Undo.RegisterCreatedObjectUndo(holder, "Create Rail Beetle encounter group");
                    group = holder.transform;
                }
                GameObject guard = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(guard, "Place Rail Beetle guard");
                guard.name = InstanceName;
                Undo.SetTransformParent(guard.transform, group, "Parent Rail Beetle guard");
                guard.transform.position = place + Vector3.up * .015f;
                guard.transform.rotation = Quaternion.identity;
                PrefabUtility.RecordPrefabInstancePropertyModifications(guard);
                PrefabUtility.RecordPrefabInstancePropertyModifications(guard.transform);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save Rail Beetle placement; changes remain undoable.");
                Undo.CollapseUndoOperations(undoGroup);
                Debug.Log("Rail Beetle placed on checked northeast forest ground at " + guard.transform.position +
                    ". Find _RailBeetleEncounters / RailBeetle_PathGuard in OverworldBaseline.");
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }
        finally
        {
            if (openedHere && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // Unity -executeMethod entry. Checks are run separately so no hidden reflection or auto-install is needed.
    public static void Run() { BuildAndPlace(); }


    // Explicit tuning migration: never regenerate meshes or touch another encounter.
    [MenuItem("Tools/Art/Apply Rail Beetle Side Dodge Tuning")]
    public static void ApplySideDodgeTuning()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Leave Play Mode before tuning the beetle.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before tuning.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Transform guard = FindGroup(scene)?.Find(InstanceName);
            MeshCollider terrain = FindTerrain(scene);
            if (!guard || !terrain) throw new InvalidOperationException("Placed guard or terrain missing; unchanged.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Vector3 scale = Vector3.one * 1.2f, fitted;
            Physics.SyncTransforms();
            using (var shapes = new PlacementShapes(prefab.GetComponent<CapsuleCollider>(), prefab.GetComponent<BoxCollider>(), scale))
            {
                if (!GroundSupport(scene, terrain, guard.position, guard.rotation, scale,
                        prefab.GetComponent<BoxCollider>().size, out fitted) ||
                    !shapes.Clear(scene, terrain, fitted + Vector3.up * .015f, guard.rotation, guard))
                    throw new InvalidOperationException("Larger beetle has no safe full-footprint placement; unchanged.");
            }
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                contents.transform.localScale = scale;
                ApplyChargeValues(contents.GetComponent<RailBeetleAI>());
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Undo.RecordObject(guard, "Scale and fit Rail Beetle");
            guard.localScale = scale;
            guard.position = fitted + Vector3.up * .015f;
            ApplyChargeValues(guard.GetComponent<RailBeetleAI>());
            PrefabUtility.RecordPrefabInstancePropertyModifications(guard);
            PrefabUtility.RecordPrefabInstancePropertyModifications(guard.GetComponent<RailBeetleAI>());
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save beetle tuning.");
            Debug.Log("Rail Beetle tuned: scale 1.2, charge 14 m/s over up to 16 m; recovery 2.2 / wall 2.8 seconds; fitted at " + guard.position);
        }
        finally { if (openedHere && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true); }
    }

    private static void ApplyChargeValues(RailBeetleAI ai)
    {
        var data = new SerializedObject(ai);
        SetFloat(data, "chargeSpeed", 14f); SetFloat(data, "chargeDistance", 16f);
        SetFloat(data, "recoverSeconds", 2.2f); SetFloat(data, "wallStunSeconds", 2.8f);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // Explicit repair for this encounter's Y only. Ordinary rebuilds never move an existing authored guard.
    [MenuItem("Tools/Art/Fit Placed Rail Beetle To Ground")]
    public static void FitPlacedGuardToGround() { AdjustPlacedGuard(false); }

    // Explicit commissioning action for this single sample; never called by a rebuild or import.
    [MenuItem("Tools/Art/Relocate Rail Beetle Sample To Clear Ground")]
    public static void RelocateSampleToClearGround() { AdjustPlacedGuard(true); }

    private static void AdjustPlacedGuard(bool relocate)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Leave Play mode before fitting the Rail Beetle.");
        if (Application.isBatchMode)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Dirty scene detected; fitting will not overwrite it.");
        }
        else if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            if (scene.isDirty) throw new InvalidOperationException("Save OverworldBaseline before fitting the Rail Beetle.");
            Transform group = FindGroup(scene);
            Transform guard = group ? group.Find(InstanceName) : null;
            MeshCollider terrain = FindTerrain(scene);
            if (!guard || !terrain) throw new InvalidOperationException("Placed Rail Beetle or walking terrain is missing.");
            CapsuleCollider capsule = guard.GetComponent<CapsuleCollider>();
            BoxCollider box = guard.GetComponent<BoxCollider>();
            if (!capsule || !box) throw new InvalidOperationException("Rail Beetle body colliders are missing.");
            Physics.SyncTransforms();
            Vector3 current = guard.position;
            using (PlacementShapes shapes = new PlacementShapes(capsule, box, guard.lossyScale))
            {
                Vector3 ground;
                bool supported = relocate
                    ? FindClearGround(scene, terrain, new Vector2(41f, 34f), 10, out ground, guard)
                    : GroundSupport(scene, terrain, current, guard.rotation, guard.lossyScale, box.size, out ground);
                if (!supported)
                    throw new InvalidOperationException("No safe full-footprint support under the existing Rail Beetle; unchanged.");
                Vector3 fitted = relocate ? ground + Vector3.up * .015f : new Vector3(current.x, ground.y + .015f, current.z);
                if (!shapes.Clear(scene, terrain, fitted, guard.rotation, guard))
                    throw new InvalidOperationException("Ground-fitted Rail Beetle would intersect a solid obstacle; unchanged.");
                if ((current - fitted).sqrMagnitude < .000001f)
                { Debug.Log("Placed Rail Beetle already has safe full-footprint ground clearance."); return; }
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Fit Rail Beetle Height");
                try
                {
                    Undo.RecordObject(guard, "Fit Rail Beetle Height");
                    guard.position = fitted;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(guard);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save fitted guard height.");
                    Undo.CollapseUndoOperations(undoGroup);
                    Debug.Log("Rail Beetle " + (relocate ? "sample explicitly relocated" : "height fitted") +
                        " from " + current + " to " + fitted + "; all other encounters preserved.");
                }
                catch { Undo.RevertAllDownToGroup(undoGroup); throw; }
            }
        }
        finally { if (openedHere && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true); }
    }

    public static GameObject BuildPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Cannot rebuild Rail Beetle assets during Play mode.");
        Material ivory = RequiredMaterial("IvoryCeramic");
        Material graphite = RequiredMaterial("Graphite");
        Material moss = RequiredMaterial("LeafGreen");
        Material cyan = RequiredMaterial("CyanCircuit");
        Material warningMaterial = RequiredMaterial("WarningLine");
        GameObject lootTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(LootSource);
        if (!lootTemplate || !lootTemplate.GetComponent<StoneScoutLootDrop>())
            throw new InvalidOperationException("Existing StoneScout loot source is missing; beetle assets unchanged.");
        EnsureFolder();
        Mesh chassis = SaveMesh("Graphite chassis", BuildChassis());
        Mesh leftShell = SaveMesh("Left ivory shield", BuildShell(-1));
        Mesh rightShell = SaveMesh("Right ivory shield", BuildShell(1));
        Mesh leftLeg = SaveMesh("Left articulated claw", BuildLeg(-1));
        Mesh rightLeg = SaveMesh("Right articulated claw", BuildLeg(1));
        Mesh core = SaveMesh("Circuit spine and visor", BuildCore());

        GameObject root = new GameObject("RailBeetle");
        root.transform.localScale = Vector3.one * 1.2f;
        try
        {
            Health health = root.AddComponent<Health>();
            SerializedObject hp = new SerializedObject(health);
            hp.FindProperty("maxHealth").intValue = 80;
            hp.FindProperty("team").enumValueIndex = (int)HealthTeam.Enemy;
            hp.ApplyModifiedPropertiesWithoutUndo();
            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2;
            collider.center = new Vector3(0f, .45f, 0f);
            collider.radius = .4f;
            collider.height = 1.65f;
            Rigidbody rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.interpolation = RigidbodyInterpolation.None;
            root.AddComponent<NonClimbableSurface>();

            Transform visuals = new GameObject("Visual Body").transform;
            visuals.SetParent(root.transform, false);
            Part(visuals, "Graphite chassis", chassis, new[] { graphite, ivory }, Vector3.zero);
            Transform left = Part(visuals, "Left ivory shield", leftShell,
                new[] { ivory, moss, graphite }, new Vector3(-.235f, .68f, -.04f)).transform;
            Transform right = Part(visuals, "Right ivory shield", rightShell,
                new[] { ivory, moss, graphite }, new Vector3(.235f, .68f, -.04f)).transform;
            Transform[] legs = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                bool isLeft = (i & 1) == 0;
                bool front = i < 2;
                legs[i] = Part(visuals, (isLeft ? "Left " : "Right ") + (front ? "front claw" : "rear claw"),
                    isLeft ? leftLeg : rightLeg, new[] { graphite, ivory },
                    new Vector3(isLeft ? -.37f : .37f, .44f, front ? .45f : -.43f)).transform;
                if (!front) legs[i].localRotation = Quaternion.Euler(-8f, 0f, 0f);
            }
            MeshRenderer lens = Part(visuals, "Cyan visor and spine", core, new[] { cyan }, Vector3.zero);
            Bounds geometryBounds = VisualBounds(visuals);
            BoxCollider clearance = root.AddComponent<BoxCollider>();
            clearance.center = new Vector3(0f, .465f, 0f);
            // Keep gameplay footprint unchanged: the new domed stone silhouette fits the existing body.
            clearance.size = new Vector3(1.62f, .82f, 1.68f);
            // Both colliders follow actual visible geometry: claws block walls and can hit during a charge.
            // Only a real swept contact damages the player; ordinary walking/contact never deals damage.
            // Keep the front close to the .825 nose rather than adding a generous invisible attack reach.
            clearance.isTrigger = false;
            LineRenderer warning = MakeLine(root.transform, "Narrow locked charge warning", warningMaterial, .04f);
            LineRenderer impact = MakeLine(root.transform, "Reusable impact diamond", warningMaterial, .018f);
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 2f;
            audio.maxDistance = 20f;

            RailBeetleVisual visual = root.AddComponent<RailBeetleVisual>();
            SerializedObject view = new SerializedObject(visual);
            view.FindProperty("visualBody").objectReferenceValue = visuals;
            SetReferences(view.FindProperty("legs"), legs);
            SetReferences(view.FindProperty("shells"), new[] { left, right });
            SetReferences(view.FindProperty("coreRenderers"), new Renderer[] { lens });
            view.FindProperty("warningLine").objectReferenceValue = warning;
            view.FindProperty("impactLine").objectReferenceValue = impact;
            view.FindProperty("audioSource").objectReferenceValue = audio;
            AudioClip clip = ExistingImpactClip(lootTemplate);
            view.FindProperty("impactClip").objectReferenceValue = clip;
            StoneScoutFeedback stoneFeedback = lootTemplate.GetComponent<StoneScoutFeedback>();
            if (stoneFeedback)
                view.FindProperty("windupClip").objectReferenceValue =
                    new SerializedObject(stoneFeedback).FindProperty("windupClip").objectReferenceValue;
            view.ApplyModifiedPropertiesWithoutUndo();

            RailBeetleAI ai = root.AddComponent<RailBeetleAI>();
            SerializedObject brain = new SerializedObject(ai);
            brain.FindProperty("visual").objectReferenceValue = visual;
            brain.FindProperty("clearanceBox").objectReferenceValue = clearance;
            SetFloat(brain, "noticeRange", 14f);
            SetFloat(brain, "leashRange", 18f);
            SetFloat(brain, "walkSpeed", 2.5f);
            SetFloat(brain, "chaseSpeed", 5f);
            SetFloat(brain, "unreachableSeconds", 15f);
            SetFloat(brain, "chargeSpeed", 14f);
            SetFloat(brain, "chargeDistance", 16f);
            SetFloat(brain, "windupSeconds", .9f);
            SetFloat(brain, "aimLockSeconds", .25f);
            SetFloat(brain, "recoverSeconds", 2.2f);
            SetFloat(brain, "wallStunSeconds", 2.8f);
            SetFloat(brain, "cooldownSeconds", .8f);
            SetFloat(brain, "chargeMinRange", 3f);
            SetFloat(brain, "chargeMaxRange", 8f);
            brain.FindProperty("attackDamage").intValue = 14;
            brain.ApplyModifiedPropertiesWithoutUndo();
            StoneScoutLootDrop loot = root.AddComponent<StoneScoutLootDrop>();
            EditorUtility.CopySerialized(lootTemplate.GetComponent<StoneScoutLootDrop>(), loot);
            SerializedObject drop = new SerializedObject(loot);
            drop.FindProperty("healAmount").intValue = 15;
            drop.ApplyModifiedPropertiesWithoutUndo();

            // Persist only this builder's shared mesh asset, not unrelated dirty materials/assets.
            EditorUtility.SetDirty(chassis);
            AssetDatabase.SaveAssetIfDirty(chassis);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (!saved) throw new InvalidOperationException("Could not save RailBeetle.prefab.");
            int triangles = 0;
            foreach (MeshFilter filter in saved.GetComponentsInChildren<MeshFilter>(true))
                triangles += filter.sharedMesh.triangles.Length / 3;
            Debug.Log("Rail Beetle shared prefab built: " + triangles +
                " triangles, 8 mesh renderers + 2 reusable lines; no textures, lights or Animator. " +
                "Native visual bounds " + geometryBounds + "; solid claw clearance box " + clearance.size + ".");
            return saved;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Enemies", "RailBeetle");
    }

    private static Material RequiredMaterial(string name)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterials + name + ".mat");
        if (!material) throw new InvalidOperationException("Shared enemy material missing: " + name + "; no duplicate material created.");
        return material;
    }

    private static void SetFloat(SerializedObject data, string name, float value)
    {
        SerializedProperty property = data.FindProperty(name);
        if (property == null) throw new InvalidOperationException("Rail Beetle AI field missing: " + name);
        property.floatValue = value;
    }

    private static void SetReferences<T>(SerializedProperty property, T[] objects) where T : UnityEngine.Object
    {
        property.arraySize = objects.Length;
        for (int i = 0; i < objects.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
    }

    private static AudioClip ExistingImpactClip(GameObject source)
    {
        StoneScoutFeedback stone = source.GetComponent<StoneScoutFeedback>();
        if (stone)
        {
            SerializedProperty hit = new SerializedObject(stone).FindProperty("stoneHitClip");
            if (hit != null && hit.objectReferenceValue) return hit.objectReferenceValue as AudioClip;
        }
        CombatFeedback feedback = source.GetComponent<CombatFeedback>();
        if (feedback)
        {
            SerializedProperty hit = new SerializedObject(feedback).FindProperty("hitClip");
            if (hit != null && hit.objectReferenceValue) return hit.objectReferenceValue as AudioClip;
        }
        // Reuse an existing compact pickup cue if no dedicated hit clip is assigned.
        return new SerializedObject(source.GetComponent<StoneScoutLootDrop>()).FindProperty("pickupClip").objectReferenceValue as AudioClip;
    }

    private static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 position)
    {
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return renderer;
    }

    private static Bounds VisualBounds(Transform body)
    {
        MeshRenderer[] renderers = body.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("Rail Beetle visual geometry is missing.");
        Bounds result = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
        return result;
    }

    private static LineRenderer MakeLine(Transform parent, string name, Material material, float width)
    {
        LineRenderer line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(parent, false);
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.widthMultiplier = width;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.positionCount = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private static Mesh SaveMesh(string name, Mesh generated)
    {
        generated.name = name;
        Mesh target = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(MeshPath))
            if (asset is Mesh mesh && mesh.name == name) { target = mesh; break; }
        if (target)
        {
            EditorUtility.CopySerialized(generated, target);
            EditorUtility.SetDirty(target);
            UnityEngine.Object.DestroyImmediate(generated);
            return target;
        }
        if (!AssetDatabase.LoadMainAssetAtPath(MeshPath)) AssetDatabase.CreateAsset(generated, MeshPath);
        else AssetDatabase.AddObjectToAsset(generated, MeshPath);
        return generated;
    }

    private static Mesh BuildChassis()
    {
        Shape shape = new Shape(2);
        shape.Ellipsoid(new Vector3(0f, .405f, -.035f), new Vector3(.46f, .155f, .635f), 12, 5);
        // Stone cheeks and a broad low wedge jaw surround a small recessed mechanical face.
        // The dark inner face is intentionally shallow: the previous large black snout hid the animal silhouette.
        shape.Wedge(new Vector3(0f, .415f, .650f), new Vector3(.515f, .19f, .29f), .76f, -.050f);
        // Cut the front underside upwards: a sharp low front corner would rotate past the solid nose
        // when the dead body follows a downhill slope. This keeps the jaw's visible width/length intact.
        shape.Wedge(new Vector3(0f, .294f, .668f), new Vector3(.56f, .172f, .312f), .75f, -.035f, 1, .075f);
        for (int side = -1; side <= 1; side += 2)
        {
            shape.Wedge(new Vector3(side * .142f, .492f, .623f), new Vector3(.254f, .128f, .30f),
                .83f, -.035f, 1);
            shape.Wedge(new Vector3(side * .213f, .374f, .733f), new Vector3(.112f, .16f, .150f),
                .66f, -.018f, 1);
        }
        // Exposed mechanical shoulder collars and rear exhaust slits remain in one graphite draw call.
        for (int side = -1; side <= 1; side += 2)
        {
            shape.Cylinder(new Vector3(side * .39f, .435f, .43f), Vector3.forward, .135f, .17f, 10);
            shape.Cylinder(new Vector3(side * .39f, .435f, -.43f), Vector3.forward, .135f, .17f, 10);
            shape.Box(new Vector3(side * .17f, .401f, -.67f), new Vector3(.19f, .09f, .05f), .018f);
        }
        return shape.Mesh();
    }

    private static Mesh BuildShell(int side)
    {
        Shape shape = new Shape(3);
        Vector3 origin = new Vector3(side * .235f, .68f, -.04f);
        const int across = 6, along = 10;
        // An oval stone dome, not bevelled cuboids. The open central seam reveals the cyan spine.
        for (int x = 0; x < across; x++)
        for (int z = 0; z < along; z++)
        {
            float u0 = x / (float)across, u1 = (x + 1f) / across;
            float v0 = z / (float)along, v1 = (z + 1f) / along;
            Vector3 a = ShellSurface(side, u0, v0) - origin;
            Vector3 b = ShellSurface(side, u1, v0) - origin;
            Vector3 c = ShellSurface(side, u1, v1) - origin;
            Vector3 d = ShellSurface(side, u0, v1) - origin;
            shape.QuadFacing(a, b, c, d, Vector3.up, 0);
            Vector3 inward = new Vector3(-side * .015f, -.037f, 0f);
            shape.QuadFacing(a + inward, b + inward, c + inward, d + inward, Vector3.down, 0);
            if (x == 0) shape.QuadFacing(a, d, d + inward, a + inward, Vector3.left * side, 0);
            if (x == across - 1) shape.QuadFacing(b, c, c + inward, b + inward, Vector3.right * side, 0);
            if (z == 0) shape.QuadFacing(a, b, b + inward, a + inward, Vector3.back, 0);
            if (z == along - 1) shape.QuadFacing(d, c, c + inward, d + inward, Vector3.forward, 0);
        }
        // Thin engraved fracture lines and tiny fitted moss patches use existing material submeshes.
        // All decoration is merged into these same two shell renderers; no textures or child objects.
        Crack(shape, side, origin, new[] { new Vector2(.10f, .22f), new Vector2(.39f, .26f),
            new Vector2(.47f, .37f), new Vector2(.85f, .40f) });
        Crack(shape, side, origin, new[] { new Vector2(.43f, .61f), new Vector2(.62f, .68f),
            new Vector2(.56f, .79f), new Vector2(.92f, .84f) });
        Crack(shape, side, origin, new[] { new Vector2(.14f, .49f), new Vector2(.28f, .51f),
            new Vector2(.35f, .60f) });
        MossPatch(shape, side, origin, new Vector2(.50f, .23f), new Vector2(.15f, .095f));
        MossPatch(shape, side, origin, new Vector2(.63f, .31f), new Vector2(.08f, .055f));
        MossPatch(shape, side, origin, new Vector2(.25f, .78f), new Vector2(.13f, .07f));
        return shape.Mesh();
    }

    private static Vector3 ShellSurface(int side, float u, float v)
    {
        float arc = Mathf.Lerp(.068f, 1.43f, u);
        float length = Mathf.Lerp(-1.26f, 1.18f, v);
        float irregular = .009f * Mathf.Sin(u * 22f + v * 29f + side * 2f) * Mathf.Sin(u * 13f - v * 17f);
        return new Vector3(side * (.535f + irregular) * Mathf.Cos(length) * Mathf.Sin(arc),
            .351f + (.459f + irregular) * Mathf.Cos(length) * Mathf.Cos(arc),
            -.085f + (.65f + irregular) * Mathf.Sin(length));
    }

    private static void Crack(Shape shape, int side, Vector3 origin, Vector2[] points)
    {
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 delta = (points[i] - points[i - 1]).normalized;
            Vector2 across = new Vector2(-delta.y, delta.x) * .0035f;
            Vector3 a = ShellSurface(side, points[i - 1].x - across.x, points[i - 1].y - across.y) - origin;
            Vector3 b = ShellSurface(side, points[i - 1].x + across.x, points[i - 1].y + across.y) - origin;
            Vector3 c = ShellSurface(side, points[i].x + across.x, points[i].y + across.y) - origin;
            Vector3 d = ShellSurface(side, points[i].x - across.x, points[i].y - across.y) - origin;
            shape.QuadFacing(a + Vector3.up * .002f, b + Vector3.up * .002f,
                c + Vector3.up * .002f, d + Vector3.up * .002f, Vector3.up, 2);
        }
    }

    private static void MossPatch(Shape shape, int side, Vector3 origin, Vector2 center, Vector2 radius)
    {
        Vector3 middle = ShellSurface(side, center.x, center.y) - origin + Vector3.up * .004f;
        for (int i = 0; i < 8; i++)
        {
            float angleA = i * Mathf.PI * .25f, angleB = (i + 1) * Mathf.PI * .25f;
            float rA = (i & 1) == 0 ? 1f : .67f, rB = ((i + 1) & 1) == 0 ? 1f : .67f;
            Vector2 a = center + new Vector2(Mathf.Cos(angleA) * radius.x, Mathf.Sin(angleA) * radius.y) * rA;
            Vector2 b = center + new Vector2(Mathf.Cos(angleB) * radius.x, Mathf.Sin(angleB) * radius.y) * rB;
            shape.TriangleFacing(middle, ShellSurface(side, a.x, a.y) - origin + Vector3.up * .004f,
                ShellSurface(side, b.x, b.y) - origin + Vector3.up * .004f, Vector3.up, 1);
        }
    }

    private static Mesh BuildLeg(int side)
    {
        Shape shape = new Shape(2);
        // Thick bent forelimbs read as a quadruped even from gameplay-camera distance.
        shape.Cylinder(new Vector3(0f, -.01f, -.012f), Vector3.forward, .116f, .17f, 10);
        shape.Beam(new Vector3(side * .03f, -.025f, .006f), new Vector3(side * .227f, -.143f, .035f), .16f, .17f);
        shape.Box(new Vector3(side * .145f, -.074f, .061f), new Vector3(.22f, .21f, .23f), .052f,
            1, Quaternion.Euler(9f, 0f, side * 52f));
        shape.Cylinder(new Vector3(side * .235f, -.149f, .047f), Vector3.forward, .099f, .205f, 10);
        shape.Beam(new Vector3(side * .236f, -.168f, .052f), new Vector3(side * .258f, -.354f, .105f), .105f, .125f);
        shape.Box(new Vector3(side * .250f, -.278f, .115f), new Vector3(.20f, .195f, .225f), .044f,
            1, Quaternion.Euler(-9f, 0f, side * -4f));
        // The dark boot carries two pale individual toes, rather than the old tiny square shoe.
        shape.Box(new Vector3(side * .254f, -.395f, .121f), new Vector3(.224f, .084f, .286f), .037f);
        for (int toe = -1; toe <= 1; toe += 2)
            shape.Wedge(new Vector3(side * .254f + toe * .059f, -.386f, .177f),
                new Vector3(.094f, .073f, .17f), .69f, -.014f, 1);
        return shape.Mesh();
    }

    private static Mesh BuildCore()
    {
        Shape shape = new Shape(1);
        shape.Box(new Vector3(0f, .413f, .813f), new Vector3(.36f, .036f, .018f), .007f);
        // The illuminated central spine bends over the back like the supplied beetle concept.
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.Lerp(-1.16f, 1.06f, i / 10f), b = Mathf.Lerp(-1.16f, 1.06f, (i + 1f) / 10f);
            Vector3 front = new Vector3(0f, .352f + .459f * Mathf.Cos(a), -.085f + .65f * Mathf.Sin(a));
            Vector3 back = new Vector3(0f, .352f + .459f * Mathf.Cos(b), -.085f + .65f * Mathf.Sin(b));
            shape.QuadFacing(front + Vector3.left * .018f, front + Vector3.right * .018f,
                back + Vector3.right * .018f, back + Vector3.left * .018f, Vector3.up);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            shape.Ring(new Vector3(side * .39f, .435f, .520f), Vector3.forward, .076f, .010f, 12);
            shape.Ring(new Vector3(side * .39f, .435f, -.340f), Vector3.forward, .069f, .009f, 12);
        }
        return shape.Mesh();
    }

    private sealed class Shape
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int>[] indices;
        public Shape(int materials)
        {
            indices = new List<int>[materials];
            for (int i = 0; i < materials; i++) indices[i] = new List<int>();
        }

        public void Beam(Vector3 start, Vector3 end, float width, float depth)
        {
            Vector3 delta = end - start;
            Box((start + end) * .5f, new Vector3(width, delta.magnitude, depth), Mathf.Min(width, depth) * .2f,
                0, Quaternion.FromToRotation(Vector3.up, delta.normalized));
        }

        public void Ellipsoid(Vector3 center, Vector3 radius, int slices, int bands, int material = 0)
        {
            for (int band = 0; band < bands; band++)
            for (int slice = 0; slice < slices; slice++)
            {
                float a = Mathf.PI * band / bands, b = Mathf.PI * (band + 1f) / bands;
                float u = 2f * Mathf.PI * slice / slices, v = 2f * Mathf.PI * (slice + 1f) / slices;
                Vector3 p = EllipsoidPoint(center, radius, a, u), q = EllipsoidPoint(center, radius, b, u);
                Vector3 r = EllipsoidPoint(center, radius, b, v), s = EllipsoidPoint(center, radius, a, v);
                if (band > 0) TriangleFacing(p, q, s, (p + q + s) / 3f - center, material);
                if (band < bands - 1) TriangleFacing(q, r, s, (q + r + s) / 3f - center, material);
            }
        }

        private static Vector3 EllipsoidPoint(Vector3 center, Vector3 radius, float latitude, float longitude)
        {
            return center + Vector3.Scale(radius, new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude),
                Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude)));
        }

        public void Cylinder(Vector3 center, Vector3 axis, float radius, float depth, int sides, int material = 0)
        {
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, axis.normalized);
            Vector3 offset = axis.normalized * depth * .5f;
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides, b = 2f * Mathf.PI * (i + 1f) / sides;
                Vector3 radialA = rotation * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
                Vector3 radialB = rotation * new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f) * radius;
                QuadFacing(center + radialA - offset, center + radialB - offset,
                    center + radialB + offset, center + radialA + offset, radialA + radialB, material);
                TriangleFacing(center + offset, center + radialA + offset, center + radialB + offset, axis, material);
                TriangleFacing(center - offset, center + radialA - offset, center + radialB - offset, -axis, material);
            }
        }

        public void Ring(Vector3 center, Vector3 axis, float radius, float width, int sides)
        {
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, axis.normalized);
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides, b = 2f * Mathf.PI * (i + 1f) / sides;
                Vector3 aRay = rotation * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 bRay = rotation * new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f);
                QuadFacing(center + aRay * radius, center + bRay * radius,
                    center + bRay * (radius - width), center + aRay * (radius - width), axis);
            }
        }

        public void Wedge(Vector3 center, Vector3 size, float frontWidth, float frontDrop,
            int material = 0, float frontBottomRise = 0f)
        {
            float x = size.x * .5f, y = size.y * .5f, z = size.z * .5f;
            Vector3 a = center + new Vector3(-x, -y, -z), b = center + new Vector3(x, -y, -z);
            Vector3 c = center + new Vector3(x * frontWidth, -y + frontBottomRise, z);
            Vector3 d = center + new Vector3(-x * frontWidth, -y + frontBottomRise, z);
            Vector3 e = center + new Vector3(-x, y, -z), f = center + new Vector3(x, y, -z);
            Vector3 g = center + new Vector3(x * frontWidth, y + frontDrop, z), h = center + new Vector3(-x * frontWidth, y + frontDrop, z);
            QuadFacing(e, f, g, h, Vector3.up, material); QuadFacing(a, b, c, d, Vector3.down, material);
            QuadFacing(a, e, h, d, Vector3.left, material); QuadFacing(b, f, g, c, Vector3.right, material);
            QuadFacing(a, b, f, e, Vector3.back, material); QuadFacing(d, c, g, h, Vector3.forward, material);
        }

        public void QuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int material = 0)
        {
            TriangleFacing(a, b, c, outward, material); TriangleFacing(a, c, d, outward, material);
        }

        public void TriangleFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, int material = 0)
        {
            if (Vector3.Cross(b - a, c - a).sqrMagnitude < .000000001f) return;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) Triangle(a, c, b, material);
            else Triangle(a, b, c, material);
        }

        public void Box(Vector3 center, Vector3 size, float bevel, int material = 0, Quaternion rotation = default)
        {
            if (rotation == default) rotation = Quaternion.identity;
            float x = size.x * .5f, z = size.z * .5f;
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(x, z) * .8f);
            Vector2[] ring = {
                new Vector2(-x + b, -z), new Vector2(x - b, -z), new Vector2(x, -z + b),
                new Vector2(x, z - b), new Vector2(x - b, z), new Vector2(-x + b, z),
                new Vector2(-x, z - b), new Vector2(-x, -z + b) };
            float y = size.y * .5f;
            float bevelHeight = Mathf.Min(y * .42f, .045f);
            float[] heights = { -y, -y + bevelHeight, y - bevelHeight, y };
            float[] scales = { .94f, 1f, 1f, .86f };
            Vector3 top = center + rotation * (Vector3.up * y);
            Vector3 bottom = center + rotation * (Vector3.down * y);
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                Vector3 a = RingPoint(center, rotation, ring[i], heights[3], scales[3]);
                Vector3 bTop = RingPoint(center, rotation, ring[next], heights[3], scales[3]);
                Vector3 c = RingPoint(center, rotation, ring[i], heights[0], scales[0]);
                Vector3 d = RingPoint(center, rotation, ring[next], heights[0], scales[0]);
                Triangle(top, bTop, a, material);
                Triangle(bottom, c, d, material);
                for (int level = 0; level < 3; level++)
                {
                    Vector3 lowerA = RingPoint(center, rotation, ring[i], heights[level], scales[level]);
                    Vector3 lowerB = RingPoint(center, rotation, ring[next], heights[level], scales[level]);
                    Vector3 upperA = RingPoint(center, rotation, ring[i], heights[level + 1], scales[level + 1]);
                    Vector3 upperB = RingPoint(center, rotation, ring[next], heights[level + 1], scales[level + 1]);
                    Triangle(upperA, upperB, lowerB, material);
                    Triangle(upperA, lowerB, lowerA, material);
                }
            }
        }

        private static Vector3 RingPoint(Vector3 center, Quaternion rotation, Vector2 ring, float height, float scale)
        {
            return center + rotation * new Vector3(ring.x * scale, height, ring.y * scale);
        }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, int material)
        {
            int first = vertices.Count;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            indices[material].Add(first); indices[material].Add(first + 1); indices[material].Add(first + 2);
        }

        public Mesh Mesh()
        {
            Mesh result = new Mesh();
            result.SetVertices(vertices);
            result.SetNormals(normals);
            result.subMeshCount = indices.Length;
            for (int i = 0; i < indices.Length; i++) result.SetTriangles(indices[i], i);
            result.RecalculateBounds();
            return result;
        }
    }

    private static Transform FindGroup(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == GroupName) return root.transform;
        return null;
    }

    private static MeshCollider FindTerrain(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshCollider candidate in root.GetComponentsInChildren<MeshCollider>(true))
                if (candidate.name == "LakeTerrain_REPLACE_OR_SCULPT" && candidate.enabled && candidate.sharedMesh)
                    return candidate;
        return null;
    }

    private static bool FindClearGround(Scene scene, MeshCollider terrain, Vector2 desired, int radius,
        out Vector3 result, Transform ignore = null)
    {
        using (PlacementShapes shapes = new PlacementShapes(scale: Vector3.one * 1.2f))
        {
        for (int r = 0; r <= radius; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
            float x = desired.x + dx, z = desired.y + dz;
            if (!GroundSupport(scene, terrain, new Vector3(x, 0f, z), Quaternion.identity,
                Vector3.one * 1.2f, new Vector3(1.62f, .82f, 1.68f), out Vector3 ground) ||
                !shapes.Clear(scene, terrain, ground + Vector3.up * .015f, Quaternion.identity, ignore)) continue;
            bool supported = true;
            // This sample guard needs a short readable +Z corridor, not a tree-filled spawn pocket.
            for (int step = 1; step <= 3 && supported; step++)
            {
                if (!GroundSupport(scene, terrain, new Vector3(x, 0f, z + step * 2f), Quaternion.identity,
                    Vector3.one * 1.2f, new Vector3(1.62f, .82f, 1.68f), out Vector3 ahead) ||
                    Mathf.Abs(ahead.y - ground.y) > 1.3f ||
                    !shapes.Clear(scene, terrain, ahead + Vector3.up * .015f, Quaternion.identity, ignore))
                    supported = false;
            }
            if (!supported) continue;
            bool nearEnemy = false;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Health hp in root.GetComponentsInChildren<Health>(true))
                    if (hp.Team == HealthTeam.Enemy && !(ignore && hp.transform.IsChildOf(ignore)) &&
                        Vector3.Distance(hp.transform.position, ground) < 8f) { nearEnemy = true; break; }
            if (nearEnemy) continue;
            result = ground;
            return true;
        }
        }
        result = default;
        return false;
    }

    private static bool GroundSupport(Scene scene, MeshCollider terrain, Vector3 location,
        Quaternion rotation, Vector3 scale, Vector3 boxSize, out Vector3 ground)
    {
        Vector3 absolute = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 right = rotation * Vector3.right * ((boxSize.x * .5f - .03f) * absolute.x);
        Vector3 forward = rotation * Vector3.forward * ((boxSize.z * .5f - .03f) * absolute.z);
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        for (int i = 0; i < 9; i++)
        {
            Vector3 offset = i == 1 ? forward : i == 2 ? -forward : i == 3 ? right : i == 4 ? -right :
                i == 5 ? forward + right : i == 6 ? forward - right : i == 7 ? -forward + right :
                i == 8 ? -forward - right : Vector3.zero;
            if (!Probe(terrain, location.x + offset.x, location.z + offset.z, out RaycastHit support) ||
                InWater(scene, support.point)) { ground = default; return false; }
            low = Mathf.Min(low, support.point.y);
            high = Mathf.Max(high, support.point.y);
        }
        ground = new Vector3(location.x, high, location.z);
        return high - low <= .72f * Mathf.Max(absolute.x, absolute.z);
    }

    private sealed class PlacementShapes : IDisposable
    {
        private readonly GameObject root;
        private readonly CapsuleCollider capsule;
        private readonly BoxCollider box;
        public PlacementShapes(CapsuleCollider sourceCapsule = null, BoxCollider sourceBox = null, Vector3 scale = default)
        {
            root = new GameObject("Temporary Rail Beetle placement probe") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0f, -10000f, 0f);
            root.transform.localScale = scale == default ? Vector3.one : scale;
            capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.center = sourceCapsule ? sourceCapsule.center : new Vector3(0f, .45f, 0f);
            capsule.height = sourceCapsule ? sourceCapsule.height : 1.65f;
            capsule.radius = sourceCapsule ? sourceCapsule.radius : .4f;
            box = root.AddComponent<BoxCollider>();
            box.center = sourceBox ? sourceBox.center : new Vector3(0f, .465f, 0f);
            box.size = sourceBox ? sourceBox.size : new Vector3(1.62f, .82f, 1.68f);
            capsule.enabled = box.enabled = false; // Never enters live scene queries.
        }

        public bool Clear(Scene scene, MeshCollider terrain, Vector3 position, Quaternion rotation, Transform ignore = null)
        {
            Vector3 scale = root.transform.lossyScale;
            Vector3 absolute = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 center = position + rotation * Vector3.Scale(capsule.center, scale);
            float radius = capsule.radius * Mathf.Max(absolute.x, absolute.y);
            Vector3 half = rotation * Vector3.forward * Mathf.Max(0f, capsule.height * absolute.z * .5f - radius);
            foreach (Collider obstacle in Physics.OverlapCapsule(center - half, center + half, radius, ~0, QueryTriggerInteraction.Ignore))
                if (!Allowed(capsule, obstacle, scene, terrain, position, rotation, ignore)) return false;
            center = position + rotation * Vector3.Scale(box.center, scale);
            half = Vector3.Scale(box.size * .5f, absolute);
            foreach (Collider obstacle in Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!Allowed(box, obstacle, scene, terrain, position, rotation, ignore)) return false;
            return true;
        }

        private bool Allowed(Collider shape, Collider obstacle, Scene scene, MeshCollider terrain,
            Vector3 position, Quaternion rotation, Transform ignore)
        {
            if (!obstacle || !obstacle.enabled || obstacle.isTrigger || obstacle.gameObject.scene != scene ||
                obstacle.transform.IsChildOf(root.transform) || (ignore && obstacle.transform.IsChildOf(ignore))) return true;
            if (!Physics.ComputePenetration(shape, position, rotation, obstacle,
                obstacle.transform.position, obstacle.transform.rotation, out Vector3 push, out float depth))
                return false; // Match runtime: an unresolved solid overlap is blocked, never assumed clear.
            return obstacle == terrain && push.y >= .848f && depth <= .04f;
        }

        public void Dispose() { if (root) UnityEngine.Object.DestroyImmediate(root); }
    }

    private static bool InWater(Scene scene, Vector3 ground)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (WaterVolume water in root.GetComponentsInChildren<WaterVolume>(true))
            {
                if (!water.IsValid || ground.y >= water.WaterSurfaceY - .08f) continue;
                Vector3 probe = new Vector3(ground.x, water.WaterSurfaceY - .05f, ground.z);
                Collider volume = water.VolumeCollider;
                if (volume.bounds.Contains(probe) && (volume.ClosestPoint(probe) - probe).sqrMagnitude < .0001f)
                    return true;
            }
        return false;
    }

    private static bool Probe(MeshCollider terrain, float x, float z, out RaycastHit hit)
    {
        return terrain.Raycast(new Ray(new Vector3(x, 150f, z), Vector3.down), out hit, 300f) &&
            Vector3.Angle(hit.normal, Vector3.up) <= 25f;
    }
}
