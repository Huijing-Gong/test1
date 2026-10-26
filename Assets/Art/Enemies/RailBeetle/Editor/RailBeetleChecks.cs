using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Read-only asset checks plus disposable, far-away physics fixtures. Never saves a real scene.
public static class RailBeetleChecks
{
    private const string PrefabPath = "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab";
    private const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 TestOrigin = new Vector3(12000f, 500f, 12000f);
    private static int physicsCases;

    [MenuItem("Tools/Art/Check Rail Beetle")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before running the disposable Rail Beetle checks.");
        PrepareSavedSceneForFixtures();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check(prefab != null, "Rail Beetle prefab is missing; build it first.");
        CheckPrefab(prefab);
        CheckGeometry();
        CheckVisualReset(prefab);
        CheckPhysics();
        CheckPlacedGuard();
        Debug.Log("Rail Beetle PASS: " + physicsCases + " physics/lifecycle cases plus lightweight prefab, shared assets, visual death/reset, locked heading, " +
                  "swept one-hit charge, off-axis dodge, wall occlusion, initial overlaps, cliff stop, death and home reset. " +
                  "These disposable checks do not replace a gameplay/feel test in Play Mode.");
    }

    private static void PrepareSavedSceneForFixtures()
    {
        bool untitled = false, dirty = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            untitled |= string.IsNullOrEmpty(scene.path);
            dirty |= scene.isDirty;
        }
        if (dirty)
            throw new InvalidOperationException("Save your modified scenes before running Rail Beetle checks. Existing edits were left untouched.");
        if (!untitled) return;
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Rail Beetle checks need a saved scene before creating disposable additive fixtures. " +
                "Save your untitled scene or open OverworldBaseline first; existing edits were left untouched.");
        Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null,
            "A clean batch startup needs the saved OverworldBaseline scene for disposable fixtures.");
        // A clean batch startup creates an empty untitled scene. Read the saved map, never save or discard dirty edits.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static void CheckPrefab(GameObject prefab)
    {
        Health health = prefab.GetComponent<Health>();
        CapsuleCollider capsule = prefab.GetComponent<CapsuleCollider>();
        BoxCollider clearance = prefab.GetComponent<BoxCollider>();
        Rigidbody body = prefab.GetComponent<Rigidbody>();
        RailBeetleAI ai = prefab.GetComponent<RailBeetleAI>();
        RailBeetleVisual visual = prefab.GetComponentInChildren<RailBeetleVisual>(true);
        Check(health != null && health.Team == HealthTeam.Enemy && health.MaxHealth == 80,
            "Expected 80 HP enemy Health.");
        Check(capsule != null && capsule.enabled && !capsule.isTrigger && capsule.direction == 2 &&
              capsule.radius >= .3f && capsule.radius <= .5f && capsule.height <= 1.8f,
            "Expected a compact, solid Z-aligned body capsule.");
        Check(body != null && body.isKinematic && !body.useGravity,
            "Movement must use a kinematic, gravity-free body.");
        Check(clearance != null && clearance.enabled && !clearance.isTrigger &&
              Vector3.Distance(clearance.center, new Vector3(0f, .465f, 0f)) < .001f &&
              clearance.size.x >= 1.48f && clearance.size.x <= 1.75f &&
              Mathf.Abs(clearance.size.y - .82f) < .001f && clearance.size.z >= 1.68f && clearance.size.z <= 1.8f,
            "The feet and shell need their compact solid clearance box.");
        Check(ai != null && visual != null && prefab.GetComponent<StoneScoutLootDrop>() != null &&
              prefab.GetComponent<NonClimbableSurface>() != null,
            "Combat, cosmetic, feedback, loot or non-climbable component is missing.");
        var aiData = new SerializedObject(ai);
        Check(aiData.FindProperty("visual")?.objectReferenceValue == visual, "AI visual reference is missing.");
        Check(aiData.FindProperty("clearanceBox")?.objectReferenceValue == clearance,
            "The AI must sweep its physical foot/shell clearance box, not only its damage capsule.");
        Check(aiData.FindProperty("noticeRange").floatValue == 14f && aiData.FindProperty("leashRange").floatValue == 18f &&
              aiData.FindProperty("chargeSpeed").floatValue == 14f && aiData.FindProperty("attackDamage").intValue == 14 &&
              aiData.FindProperty("windupSeconds").floatValue >= .8f && aiData.FindProperty("aimLockSeconds").floatValue >= .2f &&
              aiData.FindProperty("recoverSeconds").floatValue >= 2.2f && aiData.FindProperty("wallStunSeconds").floatValue >= 2.8f &&
              Vector3.Distance(prefab.transform.localScale, Vector3.one * 1.2f) < .001f,
            "Charge tuning must preserve visible warning, one-hit damage and the counterattack window.");
        var visualData = new SerializedObject(visual);
        foreach (string field in new[] { "visualBody", "warningLine", "impactLine" })
            Check(visualData.FindProperty(field)?.objectReferenceValue != null, "Missing visual reference: " + field);
        CheckArray(visualData, "legs", 4);
        CheckArray(visualData, "shells", 2);
        CheckArray(visualData, "coreRenderers", 1);
        foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                "Missing script on " + child.name);

        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        Check(renderers.Length <= 13 && renderers.Length >= 8, "Compact mesh and reused charge-line renderer budget exceeded or visual incomplete.");
        var materials = new HashSet<Material>();
        int triangles = 0;
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            Check(filter.sharedMesh != null, "Missing mesh: " + filter.name);
            for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                triangles += (int)filter.sharedMesh.GetIndexCount(sub) / 3;
        }
        foreach (Renderer renderer in renderers)
            foreach (Material material in renderer.sharedMaterials)
            {
                Check(material != null && material.shader != null, "Missing material/shader: " + renderer.name);
                materials.Add(material);
            }
        Check(triangles > 0 && triangles <= 4500, "Triangle budget exceeded: " + triangles);
        Check(materials.Count <= 5, "Expected shared materials, not one unique material per part.");
        Check(prefab.GetComponentsInChildren<Light>(true).Length == 0 &&
              prefab.GetComponentsInChildren<Animator>(true).Length == 0,
            "Compact beetle should not add real-time lights or an animation-package dependency.");
        var loot = new SerializedObject(prefab.GetComponent<StoneScoutLootDrop>());
        Check(loot.FindProperty("shardMaterial").objectReferenceValue != null &&
              loot.FindProperty("healAmount").intValue == 15, "Expected the shared 15 HP healing shard.");
    }

    private static void CheckArray(SerializedObject value, string field, int count)
    {
        SerializedProperty array = value.FindProperty(field);
        Check(array != null && array.isArray && array.arraySize == count, "Incorrect array: " + field);
        for (int i = 0; i < count; i++)
            Check(array.GetArrayElementAtIndex(i).objectReferenceValue != null, "Unassigned " + field + "[" + i + "]");
    }

    private static void CheckGeometry()
    {
        Vector3 direction = RailBeetleAI.ChargeHeading(Vector3.zero, new Vector3(3f, 20f, 4f), Vector3.forward);
        Check(Mathf.Abs(direction.y) < .0001f && Mathf.Abs(direction.magnitude - 1f) < .0001f &&
              Vector3.Dot(direction, new Vector3(.6f, 0f, .8f)) > .999f,
            "Charge must lock to a normalized horizontal direction.");
        Check(Vector3.Dot(RailBeetleAI.ChargeHeading(Vector3.zero, Vector3.up, Vector3.forward), Vector3.forward) > .999f,
            "A vertically coincident target must use the stable forward fallback.");
        Check(Mathf.Abs(RailBeetleAI.LimitedChargeStep(9f, .1f, .2f) - .2f) < .0001f &&
              RailBeetleAI.LimitedChargeStep(9f, .1f, 0f) == 0f,
            "Charge step must never exceed its remaining travel budget.");
    }

    private static void CheckVisualReset(GameObject prefab)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            RailBeetleVisual visual = instance.GetComponentInChildren<RailBeetleVisual>(true);
            Set(visual, "impactClip", null); // No preview audio while sampling cosmetic motion.
            Call(visual, "Awake");
            Transform body = (Transform)Get(visual, "visualBody");
            Vector3 rest = body.localPosition;
            visual.SetChargeTrail(instance.transform.position + Vector3.up * .4f,
                instance.transform.position + Vector3.back * 2f + Vector3.up * .4f, true);
            Check(visual.ChargeTrailVisible, "A charge must show its reused straight blue trail.");
            int blueLines = 0;
            foreach (LineRenderer line in instance.GetComponentsInChildren<LineRenderer>())
                if (line.enabled && line != (LineRenderer)Get(visual, "warningLine") &&
                    line != (LineRenderer)Get(visual, "impactLine"))
                {
                    Check(line.positionCount >= 2 && line.startColor.b > line.startColor.r * 2f &&
                        Mathf.Max(line.widthCurve.Evaluate(0f), line.widthCurve.Evaluate(1f)) * line.widthMultiplier <= .12f,
                        "Charge FX must be thin blue lines, not a broad damage area.");
                    blueLines++;
                }
            Check(blueLines == 3, "Expected three reused blue charge lines.");
            visual.PlayDeath();
            Call(visual, "AnimateDeath", .55f);
            Check(body.localPosition.y < rest.y - .15f, "Death must visibly collapse the beetle body.");
            visual.ResetVisual();
            Check(Vector3.Distance(body.localPosition, rest) < .0001f && !(bool)Get(visual, "dead"),
                "Reset must restore the cosmetic body and clear death state.");
            Check(!((LineRenderer)Get(visual, "warningLine")).enabled &&
                  !((LineRenderer)Get(visual, "impactLine")).enabled && !visual.ChargeTrailVisible,
                  "Death and reset must hide all reusable attack effects.");
        }
        finally { Close(scene, previous); }
    }

    private static void CheckPhysics()
    {
        physicsCases = 0;
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            using (var fixture = new Fixture())
            {
                fixture.PlayerAt(new Vector3(0f, .015f, 4f));
                fixture.Charge();
                fixture.Move(Vector3.forward, 5f, out _);
                fixture.Move(Vector3.forward, .2f, out _); // A skin-distance stop can defer actual contact to the next tick.
                int afterHit = fixture.Player.CurrentHealth;
                Check(afterHit == fixture.Player.MaxHealth - 14, "A swept charge must hit a crossed player once.");
                fixture.Move(Vector3.forward, .2f, out _);
                Check(fixture.Player.CurrentHealth == afterHit, "One charge cannot damage a player twice.");
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(2f, .015f, 4f)); fixture.Charge();
                fixture.Move(Vector3.forward, 4f, out _);
                CheckFixture(fixture.Player.CurrentHealth == fixture.Player.MaxHealth && fixture.LocalPosition.z > 3f,
                    "A player who steps outside the visible body path must dodge the charge.", fixture);
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(0f, .015f, 4f)); fixture.Charge();
                GameObject wall = fixture.Wall(new Vector3(0f, 1.5f, 2f), new Vector3(6f, 3f, .15f));
                fixture.Move(Vector3.forward, 5f, out bool wallHit);
                Check(wallHit && fixture.Player.CurrentHealth == fixture.Player.MaxHealth && fixture.LocalPosition.z < 1.4f,
                    "A thin wall must stop the sweep before it can damage a player behind it.");
                UnityEngine.Object.DestroyImmediate(wall);
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(2f, .015f, 5f)); fixture.Charge();
                wall = fixture.Wall(new Vector3(.70f, 1.5f, 2f), new Vector3(.12f, 3f, .12f));
                fixture.Move(Vector3.forward, 5f, out wallHit);
                Check(wallHit && fixture.LocalPosition.z < 1.2f &&
                      !Physics.ComputePenetration(fixture.Clearance, fixture.Body.position, fixture.Body.rotation,
                          wall.GetComponent<Collider>(), wall.transform.position, wall.transform.rotation, out _, out _),
                    "A thin post outside the damage capsule must still stop the outer feet/shell without penetration.");
                UnityEngine.Object.DestroyImmediate(wall);
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(.95f, .015f, 2f)); fixture.Charge();
                fixture.Move(Vector3.forward, 4f, out _);
                fixture.Move(Vector3.forward, .2f, out _);
                Check(fixture.Player.CurrentHealth == fixture.Player.MaxHealth - 14 && fixture.LocalPosition.z < 2f,
                    "Charging outer-foot physical contact must stop passing through a player and inflict one hit.");
                fixture.Move(Vector3.forward, .2f, out _);
                Check(fixture.Player.CurrentHealth == fixture.Player.MaxHealth - 14,
                    "Compound foot and body contacts must share one damage attempt per charge.");
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(.95f, .015f, 2f));
                fixture.Move(Vector3.forward, 4f, out _, false);
                Check(fixture.Player.CurrentHealth == fixture.Player.MaxHealth && fixture.LocalPosition.z < 2f,
                    "The same physical contact outside an attack must block movement without dealing damage.");
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(1.3f, .015f, 4f)); fixture.Charge();
                fixture.Move(Vector3.forward, 4f, out _);
                Check(fixture.Player.CurrentHealth == fixture.Player.MaxHealth && fixture.LocalPosition.z > 3f,
                    "A player who dodges outside the feet's physical box must not receive expanded-radius damage.");
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(5f, .015f, 5f));
                float sideEdge = fixture.Clearance.size.x * .5f;
                float frontEdge = fixture.Clearance.size.z * .5f;
                wall = fixture.Wall(new Vector3((sideEdge + frontEdge) * .5f, .465f, .7f), new Vector3(.01f, .5f, .02f));
                Call(fixture.AI, "TryFace", Vector3.right, .5f);
                Check(Quaternion.Angle(fixture.Body.rotation, Quaternion.identity) < .01f,
                    "Turning must not rotate the outer-foot clearance box through a thin obstacle.");
                UnityEngine.Object.DestroyImmediate(wall);
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(0f, .015f, 1.8f));
                Set(fixture.AI, "state", RailBeetleAI.BehaviourState.Seek);
                Set(fixture.AI, "closePressureTime", 1.19f);
                Call(fixture.AI, "Seek", fixture.Player.transform.position, true, .02f);
                Check(fixture.AI.CurrentState == RailBeetleAI.BehaviourState.Windup &&
                      Mathf.Abs((float)Get(fixture.AI, "timer") - .9f) < .001f &&
                      (float)Get(fixture.AI, "activeChargeDistance") >= 1.2f &&
                      (float)Get(fixture.AI, "activeChargeDistance") < 3f && fixture.Player.CurrentHealth == fixture.Player.MaxHealth,
                    "Sustained close pressure must trigger a short charge with the full visible warning, not instant contact damage.");
                Call(fixture.AI, "TickWindup", .7f); Call(fixture.AI, "TickWindup", .25f);
                for (int i = 0; i < 10 && fixture.AI.IsCharging; i++) Call(fixture.AI, "TickCharge", .1f);
                Check(fixture.Player.CurrentHealth == fixture.Player.MaxHealth - 14 && !fixture.AI.IsCharging,
                    "The warned short charge must make one real contact and leave a recovery window.");
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(0f, .015f, .8f)); fixture.Charge();
                fixture.Move(Vector3.forward, .2f, out _);
                Check(fixture.Player.CurrentHealth == fixture.Player.MaxHealth - 14,
                    "Initially overlapping player must be handled, not missed by CapsuleCast.");
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(0f, .015f, .95f));
                bool backedAway = fixture.Move(Vector3.back, .3f, out _, false);
                Check(backedAway && fixture.LocalPosition.z < -.2f && fixture.Player.CurrentHealth == fixture.Player.MaxHealth,
                    "A touching player must not trap a non-attacking beetle which moves safely away.");
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(0f, .015f, 4f)); fixture.Charge();
                wall = fixture.Wall(new Vector3(0f, 1.5f, .4f), new Vector3(6f, 3f, .15f));
                Vector3 overlappingStart = fixture.LocalPosition;
                fixture.Move(Vector3.forward, 4f, out wallHit);
                Check(wallHit && fixture.Player.CurrentHealth == fixture.Player.MaxHealth &&
                      Vector3.Distance(fixture.LocalPosition, overlappingStart) < .001f,
                    "Initially overlapping wall must block damage without driving the body deeper into it.");
                UnityEngine.Object.DestroyImmediate(wall);
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(4f, .015f, 8f)); fixture.Charge();
                fixture.Floor.transform.localScale = new Vector3(20f, 1f, 6f);
                fixture.Floor.transform.position = TestOrigin + new Vector3(0f, -.5f, -1f);
                Physics.SyncTransforms();
                bool moved = fixture.Move(Vector3.forward, 5f, out _);
                bool advancedAgain = fixture.Move(Vector3.forward, 1f, out _);
                Check(moved && !advancedAgain && fixture.LocalPosition.z < 1.5f,
                    "Charge must advance only along supported ground and then brake before the cliff.");
                physicsCases++;

                fixture.Reset(); fixture.PlayerAt(new Vector3(4f, .015f, 4f));
                Set(fixture.AI, "state", RailBeetleAI.BehaviourState.Windup);
                Set(fixture.AI, "chargeHeading", Vector3.forward);
                Set(fixture.AI, "aimLocked", true); Set(fixture.AI, "timer", .1f);
                Call(fixture.AI, "TickWindup", .2f);
                Check(Vector3.Dot((Vector3)Get(fixture.AI, "chargeHeading"), Vector3.forward) > .999f,
                    "After lock-in, moving the player must not turn the charge into homing.");
                physicsCases++;

                fixture.Enemy.TryApplyDamage(new DamageInfo(10000, null, fixture.Enemy.transform.position, Vector3.back));
                Call(fixture.AI, "OnDied", fixture.Enemy, new DamageInfo(10000, null, fixture.Enemy.transform.position, Vector3.back));
                fixture.PlayerAt(new Vector3(0f, .015f, 2f));
                Vector3 corpse = fixture.LocalPosition; Call(fixture.AI, "TickCharge", .5f);
                Check(fixture.AI.CurrentState == RailBeetleAI.BehaviourState.Dead &&
                      !fixture.Capsule.enabled && !fixture.Clearance.enabled && fixture.Player.CurrentHealth == fixture.Player.MaxHealth &&
                      Vector3.Distance(fixture.LocalPosition, corpse) < .001f,
                    "A dead charge must not move or inflict delayed damage.");
                physicsCases++;
                fixture.Body.position += new Vector3(4f, 2f, 2f);
                Set(fixture.AI, "blockedRetryTimer", .5f); Set(fixture.AI, "closePressureTime", .5f);
                Set(fixture.AI, "activeChargeDistance", 2f); fixture.AI.ResetForJourney();
                Check(fixture.Enemy.CurrentHealth == 80 && fixture.Capsule.enabled && fixture.Clearance.enabled &&
                      fixture.AI.CurrentState != RailBeetleAI.BehaviourState.Dead &&
                      Vector3.Distance(fixture.LocalPosition, new Vector3(0f, .015f, 0f)) < .001f &&
                      (float)Get(fixture.AI, "blockedRetryTimer") == 0f && (float)Get(fixture.AI, "closePressureTime") == 0f &&
                      (float)Get(fixture.AI, "activeChargeDistance") == 16f,
                    "New-journey reset must restore HP, home position, body and live AI state.");
                physicsCases++;
                fixture.Reset();
                fixture.Floor.transform.localScale = new Vector3(100f, 1f, 100f);
                fixture.Body.position = TestOrigin + new Vector3(0f, .015f, 20f);
                fixture.PlayerAt(new Vector3(0f, .015f, 38f));
                Set(fixture.AI, "state", RailBeetleAI.BehaviourState.Seek);
                Set(fixture.AI, "engaged", true); Set(fixture.AI, "cooldown", 1f);
                Set(fixture.AI, "lastKnownTarget", fixture.Player.transform.position);
                Call(fixture.AI, "Seek", fixture.Player.transform.position, true, .1f);
                Check(fixture.AI.CurrentState == RailBeetleAI.BehaviourState.Seek &&
                      fixture.LocalPosition.z > 20.05f && (bool)Get(fixture.AI, "engaged"),
                    "An acquired player must still be pursued beyond both the old home leash and acquisition range.");
                Call(fixture.AI, "SuspendCombat");
                Check(!(bool)Get(fixture.AI, "engaged") && !fixture.AI.IsCharging,
                    "Gameplay gating must release pursuit and cancel any pending charge.");
                Set(fixture.AI, "engaged", true); Set(fixture.AI, "pursuitFailureTime", 3f);
                fixture.AI.ResetForJourney();
                Check(!(bool)Get(fixture.AI, "engaged") && (float)Get(fixture.AI, "pursuitFailureTime") == 0f &&
                      Vector3.Distance(fixture.LocalPosition, new Vector3(0f, .015f, 0f)) < .001f,
                    "New-game reset must clear persistent pursuit and return to the original home.");
                physicsCases++;
            }
        }
        finally { Close(scene, previous); }
    }

    private static void CheckPlacedGuard()
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Transform guard = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "_RailBeetleEncounters") guard = root.transform.Find("RailBeetle_PathGuard");
            Check(guard != null, "Placed sample guard is missing; run Build and Place Rail Beetle first.");
            RailBeetleAI ai = guard.GetComponent<RailBeetleAI>();
            Health health = guard.GetComponent<Health>();
            Rigidbody body = guard.GetComponent<Rigidbody>();
            Check(ai != null && health != null && body != null, "Placed guard has incomplete combat components.");
            Transform[] transforms = guard.GetComponentsInChildren<Transform>(true);
            var poses = new TransformPose[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) poses[i] = new TransformPose(transforms[i]);
            Vector3 start = body.position; Quaternion startRotation = body.rotation;
            int previousHealth = health.CurrentHealth;
            try
            {
                Physics.SyncTransforms(); Call(ai, "Awake"); health.RestoreFullHealth();
                object[] ground = { start, 0f };
                bool supported = (bool)Call(ai, "TryGround", ground);
                float rootY = (float)ground[1];
                string diagnostic = " Placed position=" + start + "; suggested nine-point root Y=" + rootY.ToString("F4") + ".";
                Check(supported && Mathf.Abs(start.y - rootY) <= .035f,
                    "Placed guard is not fitted to its full walking footprint." + diagnostic);
                bool clear = (bool)Call(ai, "BodyClear", start, startRotation, false, Vector3.zero);
                Check(clear, "Placed feet/shell overlap actual terrain or an obstacle." + diagnostic +
                    (clear ? string.Empty : DiagnosePlaced(ai, start, startRotation)));
                object[] step = { Vector3.forward, .1f, false, false };
                bool moved = (bool)Call(ai, "TryMove", step);
                Check(moved && body.position.z > start.z + .05f,
                    "The placed guard cannot take a short +Z step on the real map; check slope fit or clearance." + diagnostic);
                Debug.Log("Rail Beetle placed-guard PASS: full-footprint ground fit, compound body clear and actual short movement at " + start + ".");
            }
            finally
            {
                try { ai.ResetForJourney(); }
                finally
                {
                    for (int i = 0; i < transforms.Length; i++) poses[i].Restore(transforms[i]);
                    body.position = start; body.rotation = startRotation;
                    typeof(Health).GetProperty(nameof(Health.CurrentHealth)).SetValue(health, previousHealth);
                    Physics.SyncTransforms();
                }
            }
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (openedHere && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static string DiagnosePlaced(RailBeetleAI ai, Vector3 position, Quaternion rotation)
    {
        var detail = new System.Text.StringBuilder(" Compound overlaps:");
        Collider[] shapes = { ai.GetComponent<CapsuleCollider>(), (BoxCollider)Get(ai, "clearanceBox") };
        int printed = 0;
        foreach (Collider shape in shapes)
        {
            if (!shape) continue;
            int count = (int)Call(ai, "ShapeOverlaps", shape, position, rotation);
            Collider[] hits = (Collider[])Get(ai, "overlaps");
            detail.Append(" [").Append(shape.GetType().Name).Append(" count=").Append(count).Append("]");
            for (int i = 0; i < count && i < hits.Length && printed < 16; i++, printed++)
            {
                Collider obstacle = hits[i];
                if (!obstacle) { detail.Append(" null"); continue; }
                string path = obstacle.name;
                for (Transform ancestor = obstacle.transform.parent; ancestor; ancestor = ancestor.parent)
                    path = ancestor.name + "/" + path;
                bool support = (bool)Call(ai, "IsSupportSurface", obstacle);
                bool penetration = Physics.ComputePenetration(shape, position, rotation, obstacle,
                    obstacle.transform.position, obstacle.transform.rotation, out Vector3 push, out float depth);
                detail.Append(" {path=").Append(path).Append(", type=").Append(obstacle.GetType().Name)
                    .Append(", self=").Append(obstacle.transform.IsChildOf(ai.transform))
                    .Append(", support=").Append(support).Append(", penetration=").Append(penetration)
                    .Append(", push=").Append(push.ToString("F4")).Append(", depth=").Append(depth.ToString("F6"))
                    .Append(", enabled=").Append(obstacle.enabled).Append("}");
            }
        }
        if (printed >= 16) detail.Append(" [limited to 16 overlap records]");
        return detail.ToString();
    }

    private readonly struct TransformPose
    {
        private readonly Vector3 position, scale;
        private readonly Quaternion rotation;
        public TransformPose(Transform value)
        { position = value.localPosition; rotation = value.localRotation; scale = value.localScale; }
        public void Restore(Transform value)
        { value.localPosition = position; value.localRotation = rotation; value.localScale = scale; }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly RailBeetleAI AI;
        public readonly Health Enemy, Player;
        public readonly Rigidbody Body;
        public readonly CapsuleCollider Capsule;
        public readonly BoxCollider Clearance;
        public readonly GameObject Floor;
        private readonly CharacterController playerBody;
        private readonly GameObject enemyRoot, playerRoot;
        private Vector3 lastMoveStart;
        private bool lastMoveSucceeded, lastMoveWall;
        public Vector3 LocalPosition => Body.position - TestOrigin;
        public Fixture()
        {
            Floor = Wall(new Vector3(0f, -.5f, 0f), new Vector3(30f, 1f, 30f));
            Floor.name = "QA walkable ground";
            enemyRoot = new GameObject("QA Rail Beetle"); enemyRoot.transform.position = TestOrigin + new Vector3(0f, .015f, 0f);
            Enemy = enemyRoot.AddComponent<Health>(); ConfigureHealth(Enemy, 80, HealthTeam.Enemy);
            Capsule = enemyRoot.AddComponent<CapsuleCollider>(); Capsule.direction = 2;
            Capsule.center = Vector3.up * .45f; Capsule.radius = .4f; Capsule.height = 1.65f;
            BoxCollider authored = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<BoxCollider>();
            Clearance = enemyRoot.AddComponent<BoxCollider>(); Clearance.center = authored.center;
            Clearance.size = authored.size;
            Body = enemyRoot.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
            AI = enemyRoot.AddComponent<RailBeetleAI>(); Set(AI, "clearanceBox", Clearance); Call(AI, "Awake");
            playerRoot = new GameObject("QA player target");
            Player = playerRoot.AddComponent<Health>(); ConfigureHealth(Player, 100, HealthTeam.Player);
            playerBody = playerRoot.AddComponent<CharacterController>(); playerBody.height = 2f;
            playerBody.center = Vector3.up; playerBody.radius = .35f;
            Set(AI, "playerHealth", Player); Set(AI, "playerBody", playerBody);
            PlayerAt(new Vector3(0f, .015f, 4f));
        }
        public void Reset()
        {
            Floor.transform.position = TestOrigin + Vector3.down * .5f; Floor.transform.localScale = new Vector3(30f, 1f, 30f);
            AI.ResetForJourney(); Player.RestoreFullHealth();
            Set(AI, "playerHealth", Player); Set(AI, "playerBody", playerBody); Physics.SyncTransforms();
        }
        public void Charge()
        {
            Set(AI, "state", RailBeetleAI.BehaviourState.Charge); Set(AI, "chargeHeading", Vector3.forward);
            Set(AI, "chargeTravel", 0f); Set(AI, "chargeHitAttempted", false); Set(AI, "currentSpeed", 9f);
            Set(AI, "activeChargeDistance", 8f);
            Physics.SyncTransforms();
        }
        public void PlayerAt(Vector3 position) { playerRoot.transform.position = TestOrigin + position; Physics.SyncTransforms(); }
        public bool Move(Vector3 direction, float length, out bool wallHit, bool attacking = true)
        {
            lastMoveStart = Body.position;
            object[] args = { direction, length, attacking, false };
            bool result = (bool)Call(AI, "TryMove", args); wallHit = (bool)args[3];
            lastMoveSucceeded = result; lastMoveWall = wallHit;
            Physics.SyncTransforms(); return result;
        }
        public string Diagnose()
        {
            object[] atBody = { Body.position, 0f };
            bool bodyGround = (bool)Call(AI, "TryGround", atBody);
            bool bodyClear = (bool)Call(AI, "BodyClear", Body.position, Body.rotation, false, Vector3.zero);
            Bounds floor = Floor.GetComponent<Collider>().bounds, cc = playerBody.bounds;
            return " move=" + lastMoveSucceeded + ", wall=" + lastMoveWall + ", rbStart=" + (lastMoveStart - TestOrigin).ToString("F4") +
                ", rbNow=" + LocalPosition.ToString("F4") + ", transform=" + (AI.transform.position - TestOrigin).ToString("F4") +
                ", HP=" + Player.CurrentHealth + ", ground=" + bodyGround + ":" + ((float)atBody[1] - TestOrigin.y).ToString("F4") +
                ", bodyClear=" + bodyClear + ", floorCenter=" + (floor.center - TestOrigin).ToString("F4") +
                ", floorSize=" + floor.size.ToString("F4") + ", playerBoundsCenter=" + (cc.center - TestOrigin).ToString("F4") +
                ", playerBoundsSize=" + cc.size.ToString("F4");
        }
        public GameObject Wall(Vector3 position, Vector3 size)
        {
            GameObject value = new GameObject("QA blocker"); value.transform.position = TestOrigin + position;
            value.transform.localScale = size; value.AddComponent<BoxCollider>(); Physics.SyncTransforms(); return value;
        }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(enemyRoot); UnityEngine.Object.DestroyImmediate(playerRoot); UnityEngine.Object.DestroyImmediate(Floor); }
    }

    private static void ConfigureHealth(Health health, int amount, HealthTeam team)
    {
        var data = new SerializedObject(health); data.FindProperty("maxHealth").intValue = amount;
        data.FindProperty("team").enumValueIndex = (int)team; data.ApplyModifiedPropertiesWithoutUndo(); health.RestoreFullHealth();
    }
    private static void Close(Scene scene, Scene previous)
    {
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        Physics.SyncTransforms();
    }
    private static object Call(object target, string method, params object[] args)
    {
        MethodInfo member = target.GetType().GetMethod(method, Private);
        if (member == null) throw new InvalidOperationException("Missing test hook: " + method);
        try { return member.Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void CheckFixture(bool passed, string message, Fixture fixture)
    { if (!passed) Check(false, message + fixture.Diagnose()); }
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException("Rail Beetle check failed: " + message); }
}
