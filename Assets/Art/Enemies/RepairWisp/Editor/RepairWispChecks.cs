using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class RepairWispChecks
{
    [MenuItem("Tools/Enemies/Check Repair Wisps")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before checks.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RepairWispBuilder.PrefabPath);
        Require(prefab && prefab.GetComponent<RepairWispAI>() && prefab.GetComponent<RepairWispVisual>(), "Repair prefab missing.");
        Require(prefab.GetComponent<Health>().MaxHealth == 24 && prefab.GetComponent<Health>().Team == HealthTeam.Enemy, "Wrong health/team.");
        Require(prefab.GetComponent<Rigidbody>().isKinematic && !prefab.GetComponent<Rigidbody>().useGravity, "Unsafe body.");
        CapsuleCollider capsule = prefab.GetComponent<CapsuleCollider>();
        Require(capsule && !capsule.isTrigger && capsule.center.y > .8f && capsule.center.y < 1.2f, "Sword-accessible collider missing.");
        Require(prefab.GetComponent<StoneScoutLootDrop>() && prefab.GetComponent<CombatFeedback>(), "Shared damage/drop missing.");
        Require(prefab.GetComponent<CombatFeedback>().HasConfiguredAudio, "Reusable hit/death sound missing.");
        Require(prefab.GetComponent<NonClimbableSurface>(), "Floating support must not be climbable.");
        Material lime = AssetDatabase.LoadAssetAtPath<Material>(RepairWispBuilder.Folder + "/RepairLime.mat");
        Require(lime && lime.IsKeywordEnabled("_EMISSION"), "Core emission disabled by material import.");
        var data = new SerializedObject(prefab.GetComponent<RepairWispAI>());
        Require(data.FindProperty("maximumRepairs").intValue == 3 && data.FindProperty("healAmount").intValue == 18 &&
            data.FindProperty("failedRetrySeconds").floatValue < data.FindProperty("cooldownSeconds").floatValue &&
            data.FindProperty("repairTriggerRatio").floatValue == .85f, "Finite/timely repair settings changed.");
        Require(data.FindProperty("energyLine").objectReferenceValue && data.FindProperty("visual").objectReferenceValue, "Visual references missing.");
        RepairWispSupportVfx support = prefab.GetComponent<RepairWispSupportVfx>();
        Require(support && support.SupportMaterial && !ShaderUtil.ShaderHasError(support.SupportMaterial.shader), "Repair support overlay shader missing or invalid.");
        Require(support.SupportMaterial.renderQueue >= 2500 && support.SupportMaterial.GetFloat("_SupportStrength") == 0,
            "Support overlay must be transparent and inactive outside channel.");
        Require(prefab.GetComponentsInChildren<Light>(true).Length == 0, "Support effect must not add realtime lights.");
        int vertices = 0; foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true)) vertices += filter.sharedMesh.vertexCount;
        Require(vertices < 5000 && prefab.GetComponentsInChildren<ParticleSystem>(true).Length == 0 &&
            !prefab.GetComponentInChildren<Animator>(true), "Repair prefab unexpectedly heavy.");
        string scenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath); bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(scenePath,
            string.IsNullOrEmpty(SceneManager.GetActiveScene().path) ? OpenSceneMode.Single : OpenSceneMode.Additive);
        try
        {
            int samples = 0;
            foreach (GameObject root in scene.GetRootGameObjects()) foreach (RepairWispAI ai in root.GetComponentsInChildren<RepairWispAI>(true))
            {
                samples++;
                var instance = new SerializedObject(ai); RepairWispGroup group = instance.FindProperty("group").objectReferenceValue as RepairWispGroup;
                bool regionalTower = EnemyDistributionInstaller.HasLayout(scene) && ai.name == "RepairWisp_Tower";
                Require(group && group.Guards.Length == (regionalTower ? 2 : 1) && group.Guards[0], "Support must bind its explicitly authored local guards.");
                Require(group.Guards[0].GetComponent<DomainPrototypeEnemy>().EnemyKind == DomainPrototypeEnemy.Kind.RuinPrism, "Wrong group guard.");
                if (regionalTower) Require(group.Guards[1] && group.Guards[1].name == "StoneScout_Awakened_02" &&
                    group.Guards[1].GetComponent<StoneScoutRoadsideAI>(), "Tower support must also bind its local Scout.");
                EnemyNavigationWorld world = EnemyNavigationWorld.Find(scene);
                Require(world.Sample(EnemyNavigationWorld.Profile.LeafWisp, ai.transform.position, .5f, out Vector3 point) &&
                    Vector3.Distance(point, ai.transform.position) < .2f, "Support not on safe baked ground.");
                Require(!ai.GetComponentInChildren<LineRenderer>(true).enabled, "Beam saved as active.");
                Debug.Log("Repair Wisp placement PASS: " + ai.name + " at " + ai.transform.position + "; guard=" + group.Guards[0].name);
            }
            Require(samples == 2, "Expected exactly two initial repair wisps, not a map-wide spawn.");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        Debug.Log("Repair Wisp native PASS: lightweight shared prefab, finite support tuning, real sword collider and two bound grounded samples; " + vertices + " rendered vertices.");
    }
    public static void RunAll() { Run(); EnemyNavigationChecks.RunAll(); }
    public static void FinalizeAndCheck() { RepairWispBuilder.FinishPresentation(); RunAll(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
