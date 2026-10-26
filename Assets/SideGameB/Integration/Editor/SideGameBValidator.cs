using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SideGameBValidator
{
    private const string ScenePath = "Assets/SideGameB/Game/Scenes/SideGameB.unity";

    [MenuItem("Side Game B/Validate Integration")]
    public static void Validate()
    {
        RequireSafeEditor();
        if (!File.Exists(ScenePath)) throw new InvalidOperationException("Side Game B scene is missing.");
        Scene original = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        try
        {
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        Transform[] objects = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        foreach (Transform item in objects)
        {
            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject);
            if (count > 0) Debug.LogError($"SIDE_GAME_B_MISSING_SCRIPT: {GetPath(item)} ({count})");
        }
        int missingScripts = objects.Sum(item => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject));
        if (missingScripts != 0) throw new InvalidOperationException($"Side Game B has {missingScripts} missing scripts.");
        int brokenReferences = 0;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (!component) continue;
            using (var serialized = new SerializedObject(component))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference &&
                        property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                    {
                        brokenReferences++;
                        Debug.LogError($"SIDE_GAME_B_BROKEN_REFERENCE: {GetPath(component.transform)} {property.propertyPath}");
                    }
            }
        }
        if (brokenReferences != 0) throw new InvalidOperationException($"Side Game B has {brokenReferences} broken serialized references.");
        if (!scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<PlayerController2D>(true)))
            throw new InvalidOperationException("Side Game B player is missing.");
        if (!scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<VictoryPortalTrigger2D>(true)))
            throw new InvalidOperationException("Side Game B return portal is missing.");

        string[] dependencies = AssetDatabase.GetDependencies(ScenePath, true);
        Debug.Log($"SIDE_GAME_B_STATIC_VALID: {dependencies.Length} scene dependencies; no missing scripts or broken serialized references; player and victory portal present. Runtime return, completion and WebGL interaction are not tested by this check.");
        }
        finally
        {
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (openedHere && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void RequireSafeEditor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Validation/build requires idle Edit mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save dirty scenes before validation; this command will not discard or save user work.");
    }

    private static string GetPath(Transform item)
    {
        string path = item.name;
        while (item.parent != null)
        {
            item = item.parent;
            path = item.name + "/" + path;
        }
        return path;
    }

    [MenuItem("Side Game B/Build WebGL Test")]
    public static void BuildWebGLTest()
    {
        Validate();
        string[] scenes = { "Assets/_Demo/Scenes/OverworldBaseline.unity", SideWorldTravel.BattlePath, SideWorldTravel.SideGameBPath };
        foreach (string path in scenes)
            if (!File.Exists(path)) throw new InvalidOperationException("Required integration scene is missing: " + path);
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/SideGameBIntegrationWebGL"));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"WebGL build failed with {report.summary.totalErrors} errors.");
        Debug.Log($"SIDE_GAME_B_WEBGL_BUILT: {report.summary.totalSize} bytes at {output}; {report.summary.totalErrors} build errors. Browser gameplay remains a separate check.");
    }
}
