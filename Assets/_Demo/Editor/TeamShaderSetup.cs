using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class TeamShaderSetup
{
    public const string StartScene = "Assets/StartScene.unity";
    private const string DirectoryPath = "Assets/_Demo/Materials/TeamShaders";

    [MenuItem("ArtLab/Team/Install four shaders")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the open scene first.");
        var scene = EditorSceneManager.OpenScene(ChestDemoSetup.ScenePath);
        Directory.CreateDirectory(DirectoryPath);
        AssetDatabase.Refresh();
        var root = GameObject.Find("TeamShaderTrail");
        if (root == null)
        {
            root = new GameObject("TeamShaderTrail");
            var guide = root.AddComponent<TeamShaderShowcase>();
            guide.chest = UnityEngine.Object.FindFirstObjectByType<ChestDissolveController>();
            Material stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Demo/Materials/Chest/ChestDemoPlinth.mat");
            var sword = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Rewards/HologramSword/HologramSword.prefab"));
            sword.name = "Ceuk_HologramSword";
            sword.transform.SetParent(root.transform);
            sword.transform.position = new Vector3(-3, .65f, -13.8f);
            guide.sword = sword.transform;
            Part(root, "SwordPlinth", new Vector3(-3, .15f, -13.8f), new Vector3(2.2f, .3f, 2), stone);
            var shield = new GameObject("Siyan_PixelBarrier", typeof(MeshFilter), typeof(MeshRenderer));
            shield.transform.SetParent(root.transform);
            shield.transform.position = new Vector3(-7, .12f, -9.5f);
            var controller = shield.AddComponent<PixelShieldController>();
            Material template = new Material(Shader.Find("PixelShield/TransparentBlocks"));
            AssetDatabase.CreateAsset(template, DirectoryPath + "/PixelBarrier.mat");
            controller.ConfigureMaterial(template);
            controller.ConfigureGeometry(1.65f, 2.6f, 64);
            controller.RebuildShield();
            guide.shield = controller;
            // The serialized template retains the dynamic shader in player builds.
            Part(root, "BarrierPlinth", new Vector3(-7, .06f, -9.5f), new Vector3(4, .12f, 4), stone);
            var portal = GameObject.CreatePrimitive(PrimitiveType.Quad);
            portal.name = "Huijing_DomainPortal";
            portal.transform.SetParent(root.transform);
            portal.transform.position = new Vector3(7, 1.9f, -9.5f);
            portal.transform.localScale = new Vector3(3.4f, 3.6f, 1);
            UnityEngine.Object.DestroyImmediate(portal.GetComponent<Collider>());
            portal.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/PortalScan/Materials/M_PortalScan.mat");
            guide.portal = portal.AddComponent<PortalScanController>();
            var properties = new SerializedObject(guide.portal);
            properties.FindProperty("portalRenderer").objectReferenceValue = portal.GetComponent<Renderer>();
            properties.ApplyModifiedPropertiesWithoutUndo();
            Part(root, "PortalPlinth", new Vector3(7, .06f, -9.5f), new Vector3(4.2f, .12f, 2.5f), stone);
        }
        GameObject portalDisplay = GameObject.Find("Huijing_DomainPortal");
        if (portalDisplay == null) throw new InvalidOperationException("Portal display is missing.");
        GameObject oldFrame = GameObject.Find("Huijing_StoneArchFrame");
        if (oldFrame != null) UnityEngine.Object.DestroyImmediate(oldFrame);
        CreateStoneArch(root, portalDisplay.transform.position,
            AssetDatabase.LoadAssetAtPath<Material>("Assets/PortalScan/Materials/M_StoneFrame.mat"));
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        // StartScene is a serialized copy of this exact playable scene, not a separate demo.
        EditorSceneManager.SaveScene(scene, StartScene, true);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(StartScene, true) };
        AssetDatabase.SaveAssets();
        Validate();
        Debug.Log("TEAM_SETUP_COMPLETE");
    }

    private static void Part(GameObject root, string name, Vector3 position, Vector3 size, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(root.transform);
        part.transform.position = position;
        part.transform.localScale = size;
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void CreateStoneArch(GameObject root, Vector3 portalPosition, Material material)
    {
        var frame = new GameObject("Huijing_StoneArchFrame");
        frame.transform.SetParent(root.transform);

        const float archCentreY = -0.27f;
        const float archRadiusX = 1.76f;
        const float archRadiusY = 2.05f;

        for (int side = -1; side <= 1; side += 2)
        {
            for (int level = 0; level < 3; level++)
            {
                Vector3 position = portalPosition + new Vector3(side * archRadiusX, -1.52f + level * .58f, .08f);
                ArchBlock(frame, "PillarBlock", position, new Vector3(.42f, .54f, .48f), 0f, material);
            }
        }

        for (int segment = 0; segment < 9; segment++)
        {
            float angle = Mathf.Lerp(0f, 180f, segment / 8f);
            float radians = angle * Mathf.Deg2Rad;
            Vector3 position = portalPosition + new Vector3(
                Mathf.Cos(radians) * archRadiusX,
                archCentreY + Mathf.Sin(radians) * archRadiusY,
                .08f);
            Vector2 tangent = new Vector2(-archRadiusX * Mathf.Sin(radians), archRadiusY * Mathf.Cos(radians));
            float rotation = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
            ArchBlock(frame, "ArchBlock", position, new Vector3(.66f, .42f, .48f), rotation, material);
        }
    }

    private static void ArchBlock(GameObject root, string name, Vector3 position, Vector3 size, float zRotation, Material material)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(root.transform);
        block.transform.position = position;
        block.transform.localScale = size;
        block.transform.rotation = Quaternion.Euler(0f, 0f, zRotation);
        UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
        block.GetComponent<Renderer>().sharedMaterial = material;
    }

    public static void Validate()
    {
        var scene = EditorSceneManager.OpenScene(StartScene);
        var showcase = UnityEngine.Object.FindFirstObjectByType<TeamShaderShowcase>();
        if (!showcase || !showcase.sword || !showcase.shield || !showcase.portal || !showcase.chest)
            throw new InvalidOperationException("StartScene is missing one of the four effects.");
        if (GameObject.Find("ReclaimedGrass")) throw new InvalidOperationException("Grass is excluded.");
        foreach (string name in new[] { "ArtLab/Chest Dissolve", "SideGameA/Rewards/Sword Hologram URP", "PixelShield/TransparentBlocks" })
        {
            var shader = Shader.Find(name);
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Shader import error: " + name);
        }
        if (ShaderUtil.ShaderHasError(showcase.portal.GetComponent<Renderer>().sharedMaterial.shader)) throw new InvalidOperationException("Portal shader error.");
        string[] dependencies = AssetDatabase.GetDependencies(new[] { StartScene }, true);
        Directory.CreateDirectory("ToolReports/TeamShaders");
        File.WriteAllLines("ToolReports/TeamShaders/scene-dependencies.txt", dependencies);
        Debug.Log("TEAM_VALIDATION_PASS: four referenced effects; StartScene; " + dependencies.Length + " dependencies.");
    }

    [MenuItem("ArtLab/Team/Build final WebGL")]
    public static void BuildWebGL()
    {
        Validate();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(), locationPathName = Path.GetFullPath("Builds/TeamShadersWebGL"),
            target = BuildTarget.WebGL, options = BuildOptions.None
        });
        File.WriteAllText("ToolReports/TeamShaders/build-result.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings + "\nBytes: " + report.summary.totalSize);
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Team WebGL build failed.");
        Debug.Log("TEAM_WEBGL_BUILD_SUCCEEDED");
    }
}
