using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EnemyPerformanceBuildChecks
{
    [MenuItem("Tools/Enemies/Build Isolated Enemy Performance Probe (Mac)")]
    public static void BuildMac() => Build(BuildTarget.StandaloneOSX);
    [MenuItem("Tools/Enemies/Build Isolated Enemy Performance Probe (WebGL)")]
    public static void BuildWebGL() => Build(BuildTarget.WebGL);
    static void Build(BuildTarget target)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target),target))
            throw new InvalidOperationException(target + " build module not installed; no browser/native claims made.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        string scenePath = "Assets/_Demo/Editor/EnemyPerformanceProbe-" + Guid.NewGuid().ToString("N") + ".unity";
        string directory = Path.Combine(Path.GetTempPath(),"anca-enemy-performance",target.ToString());
        Directory.CreateDirectory(directory);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        try
        {
            var probe = new GameObject("Disposable enemy-only native/browser probe").AddComponent<EnemyPerformanceProbe>();
            const string domain = "Assets/Art/Enemies/DomainPrototypes/Generated/";
            probe.dartPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(domain + "LeafSeedProjectile.prefab");
            string[] paths = { "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab", domain + "LeafWisp.prefab", domain + "RuinPrism.prefab",
                "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab",RepairWispBuilder.PrefabPath };
            probe.enemyPrefabs = new GameObject[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                probe.enemyPrefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (!probe.enemyPrefabs[i]) throw new InvalidOperationException("Missing enemy " + paths[i]);
            }
            probe.floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(domain + "IvoryCeramic.mat");
            EditorSceneManager.SaveScene(scene,scenePath);
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { scenePath }, target = target,
                locationPathName = Path.Combine(directory,target == BuildTarget.StandaloneOSX ? "EnemyProbe.app" : "WebGL"),
                options = BuildOptions.Development
            });
            Debug.Log("Enemy isolated build: " + build.summary.result + "; " + build.summary.totalSize + " bytes; " + directory);
            if (build.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Enemy probe build failed.");
        }
        finally
        {
            try
            {
                if (setup.Length > 0 && Array.TrueForAll(setup,item => !string.IsNullOrEmpty(item.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            }
            finally
            {
                // Exact, generated temporary scene only. No authored map/prefab is overwritten.
                AssetDatabase.DeleteAsset(scenePath);
            }
        }
    }
}
