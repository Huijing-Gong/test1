using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Migrates only the current demo's rendering dependencies. Gameplay components are retained.</summary>
public static class ChestUrpMigration
{
    public const string ScenePath = "Assets/_Demo/Scenes/GameplayFeatureDemoScene.unity";
    public const string PipelinePath = "Assets/_Demo/Settings/ChestDemoURP.asset";
    public const string RendererPath = "Assets/_Demo/Settings/ChestDemoURP_Renderer.asset";
    private const string MaterialFolder = "Assets/_Demo/Materials/URP";
    private const string VendorSwordShader = "Custom/Kevin Iglesias/Human Melee Animations Scene";

    public static string LastReport { get; private set; }

    [MenuItem("Tools/Chest Dissolve/Migrate Demo Rendering to URP")]
    public static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before migrating the demo.");

        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (scene.isDirty)
                throw new InvalidOperationException("Save the active scene before opening the gameplay demo.");
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        EnsureFolder("Assets/_Demo/Settings");
        EnsureFolder(MaterialFolder);
        var report = new StringBuilder();
        var dependencies = AssetDatabase.GetDependencies(ScenePath, true);
        var upgraders = MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));
        var replacements = new Dictionary<Material, Material>();
        ConfigurePipeline(report);

        // External and embedded materials are never written back into imported source assets.
        foreach (var path in dependencies.Where(p => p.StartsWith("Assets/_Demo/", StringComparison.Ordinal)
                     && p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p))
            ConvertMaterial(AssetDatabase.LoadAssetAtPath<Material>(path), replacements, upgraders, report);

        // Save nested demo prefabs before their parents, including the sword instantiated by the reward.
        var prefabPaths = dependencies.Where(p => p.StartsWith("Assets/_Demo/", StringComparison.Ordinal)
                && p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => AssetDatabase.GetDependencies(p, true).Length).ThenBy(p => p).ToArray();
        foreach (var path in prefabPaths)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (MigrateHierarchy(contents, path, replacements, upgraders, report))
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    report.AppendLine("Updated prefab material references: " + path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        foreach (var root in scene.GetRootGameObjects())
            MigrateHierarchy(root, ScenePath, replacements, upgraders, report);

        var renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !IsUrpMaterial(material))
                    throw new InvalidOperationException("Unconverted renderer material at " + HierarchyPath(renderer.transform));
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        report.AppendLine($"Verified URP material assignments on {renderers.Length} scene Renderers (including inactive objects).");
        report.AppendLine("Camera, light transforms/colors, gameplay components, colliders and animation settings retained.");
        LastReport = report.ToString();
        Debug.Log("CHEST_URP_MIGRATION\n" + LastReport);
    }

    private static void ConfigurePipeline(StringBuilder report)
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, RendererPath);
        }
        renderer.renderingMode = RenderingMode.Forward;
        renderer.depthPrimingMode = DepthPrimingMode.Disabled;
        if (renderer.postProcessData == null)
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
        ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
        EditorUtility.SetDirty(renderer);

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
        }
        var pipelineSettings = new SerializedObject(pipeline);
        var rendererList = pipelineSettings.FindProperty("m_RendererDataList");
        rendererList.arraySize = 1;
        rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
        pipelineSettings.FindProperty("m_DefaultRendererIndex").intValue = 0;
        pipelineSettings.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        pipelineSettings.FindProperty("m_AnyShadowsSupported").boolValue = true;
        pipelineSettings.FindProperty("m_SoftShadowsSupported").boolValue = true;
        pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
        pipeline.msaaSampleCount = 2;
        pipeline.supportsDynamicBatching = false;
        pipeline.renderScale = 1f;
        pipeline.mainLightShadowmapResolution = 2048;
        pipeline.shadowDistance = 70f;
        pipeline.shadowCascadeCount = 2;
        EditorUtility.SetDirty(pipeline);

        GraphicsSettings.defaultRenderPipeline = pipeline;
        int currentQuality = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
        QualitySettings.SetQualityLevel(currentQuality, false);
        SetWebGLQuality();
        report.AppendLine("URP Forward, MSAA 2x, 2048 main-light soft shadows, 2 cascades, distance 70; all quality levels use " + PipelinePath);
        report.AppendLine("WebGL default quality: High (index 3).");
    }

    private static void SetWebGLQuality()
    {
        if (QualitySettings.names.Length <= 3 || QualitySettings.names[3] != "High")
            throw new InvalidOperationException("Expected the existing High quality level at index 3.");
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var defaults = settings.FindProperty("m_PerPlatformDefaultQuality");
        if (defaults != null && defaults.isArray)
        {
            for (int i = 0; i < defaults.arraySize; i++)
            {
                var entry = defaults.GetArrayElementAtIndex(i);
                var platform = entry.FindPropertyRelative("first");
                var level = entry.FindPropertyRelative("second");
                if (platform != null && level != null && platform.stringValue == "WebGL")
                {
                    level.intValue = 3;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }
            }
        }
        // Unity versions serialize this map differently; the current project already has this value.
        if (!File.ReadAllLines("ProjectSettings/QualitySettings.asset").Any(line => line.Trim() == "WebGL: 3"))
            throw new InvalidOperationException("Could not set WebGL's default quality through SerializedObject.");
    }

    private static bool MigrateHierarchy(GameObject root, string assetPath, Dictionary<Material, Material> replacements,
        List<MaterialUpgrader> upgraders, StringBuilder report)
    {
        bool changed = false;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool rendererChanged = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var original = materials[i];
                var source = original != null ? original : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                var replacement = ConvertMaterial(source, replacements, upgraders, report);
                if (original == replacement)
                    continue;
                materials[i] = replacement;
                rendererChanged = true;
                report.AppendLine($"Assign {assetPath} :: {HierarchyPath(renderer.transform)} [{i}] -> {AssetDatabase.GetAssetPath(replacement)}");
            }
            if (rendererChanged)
            {
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                changed = true;
            }
        }

        // Preserve runtime material references on existing components, without changing other fields.
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            bool componentChanged = false;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference
                    || !(property.objectReferenceValue is Material original))
                    continue;
                var replacement = ConvertMaterial(original, replacements, upgraders, report);
                if (replacement == original)
                    continue;
                property.objectReferenceValue = replacement;
                componentChanged = true;
                report.AppendLine($"Assign material field {assetPath} :: {HierarchyPath(component.transform)}.{property.propertyPath}");
            }
            if (componentChanged)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                changed = true;
            }
        }
        return changed;
    }

    private static Material ConvertMaterial(Material source, Dictionary<Material, Material> replacements,
        List<MaterialUpgrader> upgraders, StringBuilder report)
    {
        if (source == null || source.shader == null)
            throw new InvalidOperationException("A required material or its shader is missing.");
        if (replacements.TryGetValue(source, out var cached))
            return cached;
        if (IsUrpMaterial(source))
            return source;

        string shaderName = source.shader.name;
        bool isSword = shaderName == VendorSwordShader;
        if (!isSword && !upgraders.Any(u => u.OldShaderPath == shaderName))
            throw new InvalidOperationException($"No verified URP conversion for {source.name}: {shaderName}");

        string sourcePath = AssetDatabase.GetAssetPath(source);
        bool ownedMaterial = sourcePath.StartsWith("Assets/_Demo/", StringComparison.Ordinal)
            && sourcePath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) && !AssetDatabase.IsSubAsset(source);
        Material target;
        string targetPath;
        if (ownedMaterial)
        {
            target = source;
            targetPath = sourcePath;
        }
        else
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
            string identity = string.IsNullOrEmpty(guid) ? sourcePath + "|" + source.name + "|" + shaderName : guid + "|" + localId;
            string safeName = new string(source.name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            targetPath = MaterialFolder + "/" + safeName + "_" + Hash128.Compute(identity).ToString().Substring(0, 10) + ".mat";
            target = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
            if (target != null && IsUrpMaterial(target))
            {
                replacements[source] = target;
                return target;
            }
            if (target == null)
            {
                target = new Material(source) { name = safeName + "_URP" };
                AssetDatabase.CreateAsset(target, targetPath);
            }
        }

        if (isSword)
            ConvertSwordMaterial(target);
        else
        {
            string message = string.Empty;
            if (!MaterialUpgrader.Upgrade(target, upgraders, MaterialUpgrader.UpgradeFlags.LogMessageWhenNoUpgraderFound, ref message))
                throw new InvalidOperationException(message);
        }
        EditorUtility.SetDirty(target);
        replacements[source] = target;
        report.AppendLine($"Material {sourcePath}::{source.name} [{shaderName}] -> {targetPath} [{target.shader.name}]"
            + (isSword ? " (preserved texture, tint, metallic and smoothness; now uses scene lighting)" : string.Empty));
        return target;
    }

    private static void ConvertSwordMaterial(Material material)
    {
        if (material.HasProperty("_UVRotation") && Mathf.Abs(material.GetFloat("_UVRotation")) > 0.001f)
            throw new InvalidOperationException("The vendor sword has rotated UVs; its material needs a dedicated conversion.");
        var texture = material.GetTexture("_MainTex");
        var scale = material.GetTextureScale("_MainTex");
        var offset = material.GetTextureOffset("_MainTex");
        var color = material.GetColor("_Color");
        float metallic = material.GetFloat("_Metallic");
        float smoothness = material.GetFloat("_Smoothness");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("URP Lit is unavailable; resolve the URP package first.");
        material.shader = shader;
        material.shaderKeywords = Array.Empty<string>();
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", scale);
        material.SetTextureOffset("_BaseMap", offset);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_ZWrite", 1f);
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = -1;
    }

    private static bool IsUrpMaterial(Material material)
    {
        if (material == null || material.shader == null)
            return false;
        return material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)
            || material.GetTag("RenderPipeline", false) == "UniversalPipeline";
    }

    private static string HierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
