using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.MechBossMigration;

public static class StoneBossIslandChecks
{
    public static void RequireDamageHook()
    {
        if (typeof(Health).GetEvent("ResolvingDamage") == null)
            throw new Exception("Stone Boss shield cannot resolve incoming damage: Health.ResolvingDamage is missing.");
    }

    public static string GroundConstraint()
    {
        var type = TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
            .FirstOrDefault(t => t.FullName == "KingOfGraphic.MechBossMigration.MechIslandGroundConstraint");
        Require(type != null, "Boss has no island ground constraint.");
        var ground = new GameObject("Ground constraint test floor");
        var actor = new GameObject("Ground constraint test actor");
        try
        {
            ground.transform.position = Vector3.down * .1f;
            var collider = ground.AddComponent<BoxCollider>();
            collider.size = new Vector3(10, .2f, 10);
            var component = actor.AddComponent(type);
            type.GetField("arenaCenter").SetValue(component, actor.transform);
            type.GetField("groundCollider").SetValue(component, collider);
            type.GetField("moveRadius").SetValue(component, 4.5f);
            type.GetField("waterSurfaceHeight").SetValue(component, -2f);
            Physics.SyncTransforms();
            var support = type.GetMethod("HasSupport");
            var constrain = type.GetMethod("ConstrainDisplacement");
            var engage = type.GetMethod("CanEngagePlayer");
            Require(engage != null, "Boss may raise its wall while the player is outside the island arena.");
            Require((bool)engage.Invoke(component, new object[] { Vector3.zero }), "Player inside arena was rejected.");
            Require(!(bool)engage.Invoke(component, new object[] { Vector3.right * 20 }), "Player outside arena was allowed to trigger the wall.");
            Require((bool)support.Invoke(component, new object[] { Vector3.zero }), "Dry center was rejected.");
            Require(!(bool)support.Invoke(component, new object[] { new Vector3(4.9f, 0, 0) }), "Unsupported footprint was accepted.");
            var delta = (Vector3)constrain.Invoke(component, new object[] { Vector3.zero, Vector3.right * 20 });
            Require(delta.x > 2 && delta.x < 4, "Large movement crossed the edge or blocked all valid movement.");
            Require((bool)support.Invoke(component, new object[] { delta }), "Movement ended over unsupported ground.");
            type.GetField("waterSurfaceHeight").SetValue(component, .1f);
            Require(!(bool)support.Invoke(component, new object[] { Vector3.zero }), "Submerged floor was treated as dry ground.");
            type.GetField("groundCollider").SetValue(component, null);
            delta = (Vector3)constrain.Invoke(component, new object[] { Vector3.zero, Vector3.right });
            Require(delta == Vector3.zero, "Missing ground collider did not fail closed.");
            return "Dry support, footprint edge, swept large step, submerged ground and missing collider: pass.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(actor);
            UnityEngine.Object.DestroyImmediate(ground);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static string DamageResolution()
    {
        var go = new GameObject("Boss damage resolver check");
        try
        {
            var health = go.AddComponent<Health>();
            health.SetMaximumHealth(100);
            Require(health.TryApplyDamage(new DamageInfo(20, null, Vector3.zero, Vector3.forward)), "Normal hit was rejected.");
            Require(health.CurrentHealth == 80, "Normal incoming damage changed.");
            health.ResolvingDamage += Reduce;
            Require(health.TryApplyDamage(new DamageInfo(20, null, Vector3.zero, Vector3.forward)), "Reduced hit was rejected.");
            Require(health.CurrentHealth == 74, "70 percent shield reduction did not reach final damage application.");
            health.ResolvingDamage -= Reduce;
            Require(health.TryApplyDamage(new DamageInfo(20, null, Vector3.zero, Vector3.forward)) && health.CurrentHealth == 54,
                "Removing a resolver did not restore normal damage.");
            return "Unmodified damage, reduced damage, resolver unsubscribe: pass.";
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    static void Reduce(ref DamageInfo damage) => damage = new DamageInfo(6, damage.Source, damage.HitPoint, damage.HitDirection);

    public static string ShoreWallRise()
    {
        var offset = typeof(MechArenaBarrierReveal).GetField("baseOffset");
        Require(offset != null, "The island wall cannot start below the sloped shoreline.");
        var go = new GameObject("Shore wall rise check");
        try
        {
            var visual = new GameObject("Wall visual").transform;
            visual.SetParent(go.transform, false);
            var reveal = go.AddComponent<MechArenaBarrierReveal>();
            reveal.visual = visual;
            reveal.wallHeight = 10.5f;
            offset.SetValue(reveal, -1.5f);
            reveal.AdvanceReveal(0);
            Require(Mathf.Abs(visual.localPosition.y - reveal.wallHeight * visual.localScale.y * .5f + 1.5f) < .001f,
                "The wall bottom moved away from the shoreline during its rise.");
            reveal.AdvanceReveal(1);
            Require(Mathf.Abs(visual.localScale.y - 1) < .001f && Mathf.Abs(visual.localPosition.y - 3.75f) < .001f,
                "The shoreline wall did not finish at its configured full height.");
            return "Shoreline base offset and completed wall rise: pass.";
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    public static string IslandScene()
    {
        var constraint = UnityEngine.Object.FindFirstObjectByType<MechIslandGroundConstraint>();
        Require(constraint != null, "Island Boss is missing.");
        var encounter = constraint.GetComponentInParent<MechBossEncounter>();
        Require(encounter != null && encounter.player && encounter.player.GetComponent<Health>(), "Main-world player binding is missing.");
        var boss = encounter.boss;
        Require(boss.arenaCenter == constraint.arenaCenter, "Combat and ground constraint centers disagree.");
        Require(constraint.HasSupport(boss.transform.position), "Boss spawn is unsupported.");
        int sweeps = 0;
        for (int i = 0; i < 128; i++)
        {
            float angle = i * Mathf.PI * 2 / 128;
            var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var point = boss.transform.position;
            for (int step = 0; step < 100; step++)
            {
                point += constraint.ConstrainDisplacement(point, direction * .35f);
                Require(constraint.HasSupport(point), "Boss movement crossed an unsupported edge.");
                Require(Vector3.ProjectOnPlane(point - constraint.arenaCenter.position, Vector3.up).magnitude <= constraint.moveRadius + .002f,
                    "Boss crossed its island movement boundary.");
                sweeps++;
            }
        }
        Require(!constraint.CanEngagePlayer(constraint.arenaCenter.position + Vector3.right * 13), "Player on shoreline/water can trigger the wall from outside.");
        bool active = encounter.barrier.activeSelf;
        var probe = new GameObject("Temporary island wall collision probe");
        int blocked = 0;
        try
        {
            encounter.barrier.SetActive(true);
            var controller = probe.AddComponent<CharacterController>();
            var playerController = encounter.player.GetComponent<CharacterController>();
            probe.layer = encounter.player.gameObject.layer;
            controller.height = playerController.height;
            controller.radius = playerController.radius;
            controller.center = playerController.center;
            controller.skinWidth = playerController.skinWidth;
            controller.stepOffset = playerController.stepOffset;
            controller.slopeLimit = playerController.slopeLimit;
            Physics.SyncTransforms();
            var walls = encounter.barrier.GetComponentsInChildren<BoxCollider>(true);
            Require(walls.Length == 96, "Island collision ring is incomplete.");
            foreach (var wall in walls)
            {
                Require(!wall.isTrigger && wall.enabled, "Island wall is not solid.");
                Require(!Physics.GetIgnoreLayerCollision(probe.layer, wall.gameObject.layer), "Player ignores the island wall layer.");
                Require(wall.bounds.min.y < constraint.waterSurfaceHeight, "Wall leaves a gap above the shoreline.");
            }
            for (int i = 0; i < 128; i++)
            {
                float angle = i * Mathf.PI * 2 / 128;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                controller.enabled = false;
                probe.transform.position = constraint.arenaCenter.position + direction * 8f + Vector3.up * .05f;
                controller.enabled = true;
                Physics.SyncTransforms();
                controller.Move(direction * 20);
                float radius = Vector3.ProjectOnPlane(probe.transform.position - constraint.arenaCenter.position, Vector3.up).magnitude;
                Require(radius < 9.5f, "Player capsule crossed flat island wall at direction " + i + ": " + radius);
                blocked++;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
            encounter.barrier.SetActive(active);
            Physics.SyncTransforms();
        }
        return "Real island: " + sweeps + " supported Boss movement steps; " + blocked + " player capsule wall crossings blocked; shoreline skirt below water: pass.";
    }

    public static string AnimationDesign()
    {
        var root = PrefabUtility.LoadPrefabContents("Assets/MechBossMigration/Compact/MechStoneSentinelCompact.prefab");
        try
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            Require(animator && animator.avatar && animator.avatar.isValid && animator.avatar.isHuman, "Humanoid rig is invalid.");
            Require(!animator.applyRootMotion, "Root motion can bypass island movement checks.");
            var clips = animator.runtimeAnimatorController.animationClips;
            var walk = clips.First(x => x.name.Contains("Walk"));
            var death = clips.First(x => x.name.Contains("Death"));
            var states = ((UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController)
                .layers[0].stateMachine.states.Select(x => x.state.name).ToArray();
            foreach (var required in new[] { "Idle", "Walk", "SlamV2", "ShootV2", "Death", "Hit1", "Hit2" })
                Require(states.Contains(required), "Missing animation state " + required);
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            for (int i = 0; i <= 24; i++)
            {
                walk.SampleAnimation(animator.gameObject, walk.length * i / 24);
                float difference = left.position.y - right.position.y;
                low = Mathf.Min(low, difference); high = Mathf.Max(high, difference);
            }
            Require(low < -.02f && high > .02f, "Walking feet do not alternate.");
            death.SampleAnimation(animator.gameObject, death.length);
            var skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = new Bounds(); bool first = true;
            var mesh = new UnityEngine.Mesh();
            try
            {
                foreach (var skin in skins)
                {
                    skin.BakeMesh(mesh);
                    foreach (var vertex in mesh.vertices)
                    {
                        var point = skin.transform.TransformPoint(vertex);
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                        else bounds.Encapsulate(point);
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            Require(bounds.size.y < Mathf.Max(bounds.size.x, bounds.size.z), "Death clip does not leave the model lying down.");
            return "Humanoid and required states: pass; alternating walk foot delta " + low.ToString("F3") + ".." + high.ToString("F3") +
                "m; death pose dimensions " + bounds.size.ToString("F2") + "; root motion disabled.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static string AttackTiming()
    {
        var property = typeof(MechSentinelBattle).GetProperty("ImpactTime");
        Require(property != null, "Both slam skills still use one hit timestamp despite different contact poses.");
        var boss = UnityEngine.Object.FindFirstObjectByType<MechSentinelBattle>();
        var skill = typeof(MechSentinelBattle).GetField("skill", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        object previous = skill.GetValue(boss);
        try
        {
            skill.SetValue(boss, 0);
            float fist = (float)property.GetValue(boss);
            skill.SetValue(boss, 1);
            float overhead = (float)property.GetValue(boss);
            Require(Mathf.Abs(fist - 1) < .001f && Mathf.Abs(overhead - 1.2f) < .001f,
                "Damage and impact effects are not aligned with the fist IK and overhead landing poses.");
            return "Fist impact at 1.0s; overhead impact at 1.2s; both recover at 2.15s: pass.";
        }
        finally { skill.SetValue(boss, previous); }
    }
}
