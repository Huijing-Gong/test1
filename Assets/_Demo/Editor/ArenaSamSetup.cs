using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Places the static SAM model inside the arena's visual barrier.</summary>
public static class ArenaSamSetup
{
    private const string ModelPath = "Assets/Art/SAM/Models/SAM_Character.fbx";
    private const string PrefabPath = "Assets/Art/SAM/SAM_StaticDisplay.prefab";

    [MenuItem("ArtLab/Arena/Install SAM display")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Exit Play Mode and save the current scene before installing.");
        var scene = EditorSceneManager.OpenScene(ChestDemoSetup.ScenePath);
        var platform = GameObject.Find("CircularPlatform").GetComponent<Renderer>().bounds;
        var arena = GameObject.Find("Arena").transform;
        var guide = UnityEngine.Object.FindFirstObjectByType<TeamShaderShowcase>();
        if (!guide || !guide.shield) throw new InvalidOperationException("The existing barrier is missing.");

        var previous = GameObject.Find("SAM_StaticDisplay");
        if (previous) UnityEngine.Object.DestroyImmediate(previous);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!model) throw new InvalidOperationException("SAM FBX has not imported.");
        var display = UnityEngine.Object.Instantiate(model);
        display.name = "SAM_StaticDisplay";
        display.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var component in display.GetComponentsInChildren<Component>(true))
        {
            if (component is Animator || component is Animation || component is MonoBehaviour
                || component is Collider || component is Rigidbody || component is Camera || component is Light)
                UnityEngine.Object.DestroyImmediate(component);
        }
        foreach (var renderer in display.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(original => {
                if (!original) throw new InvalidOperationException("SAM has an empty material slot.");
                string safeName = string.Concat(original.name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/SAM/Settings/MAT_" + safeName + ".mat");
                if (!material || !material.shader || ShaderUtil.ShaderHasError(material.shader))
                    throw new InvalidOperationException("Missing or invalid SAM material: " + safeName);
                return material;
            }).ToArray();
        }
        // Keep the imported rest pose and source scale; no preview controllers or animations.
        PrefabUtility.SaveAsPrefabAsset(display, PrefabPath);
        UnityEngine.Object.DestroyImmediate(display);
        display = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        display.transform.SetParent(arena, false);
        display.transform.rotation = Quaternion.Euler(0, 180, 0);
        Bounds bounds = BoundsOf(display);
        display.transform.position += new Vector3(platform.center.x - bounds.center.x,
            platform.max.y - bounds.min.y + .01f, platform.center.z - bounds.center.z);
        bounds = BoundsOf(display);

        float radius = Mathf.Min(platform.extents.x, platform.extents.z);
        float height = bounds.size.y + .55f;
        guide.shield.transform.SetParent(arena, true);
        guide.shield.transform.SetPositionAndRotation(new Vector3(platform.center.x,
            platform.max.y + height * .5f, platform.center.z), Quaternion.identity);
        guide.shield.transform.localScale = Vector3.one;
        guide.shield.ConfigureGeometry(radius, height, 128);
        guide.shield.RebuildShield();
        if (guide.shield.GetComponentsInChildren<Collider>().Length != 0)
            throw new InvalidOperationException("The visual barrier must remain passable.");
        var oldBoss = GameObject.Find("BossPlaceholder");
        if (oldBoss) oldBoss.SetActive(false);
        var oldPlinth = GameObject.Find("BarrierPlinth");
        if (oldPlinth) oldPlinth.SetActive(false);
        GameInteractionSetup.ConfigureScene();
        EditorUtility.SetDirty(guide.shield);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorSceneManager.SaveScene(scene, TeamShaderSetup.StartScene, true);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("ToolReports/ArenaSAM");
        File.WriteAllText("ToolReports/ArenaSAM/result.txt", "SAM_ARENA_PASS\nBounds: " + bounds
            + "\nBarrier radius: " + radius + " height: " + height
            + "\nRenderers: " + display.GetComponentsInChildren<Renderer>().Length
            + "\nBehaviours: " + display.GetComponentsInChildren<MonoBehaviour>(true).Length
            + "\nAnimators: " + display.GetComponentsInChildren<Animator>(true).Length);
        Selection.activeGameObject = display;
        if (SceneView.lastActiveSceneView)
            SceneView.lastActiveSceneView.LookAt(bounds.center, Quaternion.Euler(15, 0, 0), 9);
        Debug.Log("SAM_ARENA_PASS: static model and encounter barrier installed.");
    }

    private static Bounds BoundsOf(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException("SAM has no renderers.");
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }
}
