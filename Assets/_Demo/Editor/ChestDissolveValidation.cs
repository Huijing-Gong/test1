using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Structural checks; actual colour and shadow rendering is checked by ChestValidationProbe.</summary>
public static class ChestDissolveValidation
{
    private const string ScenePath = "Assets/_Demo/Scenes/GameplayFeatureDemoScene.unity";
    private static int assertions;

    [MenuItem("ArtLab/Chest/Validate Integration")]
    public static void Validate()
    {
        assertions = 0;
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Require(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset,
                "Graphics settings must use URP.");
            for (int index = 0; index < QualitySettings.names.Length; index++)
            {
                RenderPipelineAsset pipeline = QualitySettings.GetRenderPipelineAssetAt(index);
                Require(pipeline == null || pipeline is UniversalRenderPipelineAsset,
                    "Quality level " + QualitySettings.names[index] + " overrides URP.");
            }

            GameObject root = null;
            foreach (GameObject candidate in scene.GetRootGameObjects())
                foreach (Transform child in candidate.GetComponentsInChildren<Transform>(true))
                    if (child.name == "DigitalDissolveChest") root = child.gameObject;
            Require(root != null, "DigitalDissolveChest must exist in the gameplay scene.");
            ChestScanSpace scan = root.GetComponent<ChestScanSpace>();
            ChestDissolveController controller = root.GetComponent<ChestDissolveController>();
            Require(scan != null && controller != null, "The chest root needs both chest components.");
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Require(renderers.Length >= 3, "The test chest must contain separate body, lid and trim renderers.");

            SerializedObject serializedScan = new SerializedObject(scan);
            Require(serializedScan.FindProperty("configured").boolValue, "ChestScanSpace.Configure must be called before saving.");
            SerializedProperty targets = serializedScan.FindProperty("targets");
            HashSet<Renderer> configured = new HashSet<Renderer>();
            for (int index = 0; index < targets.arraySize; index++)
                configured.Add(targets.GetArrayElementAtIndex(index).objectReferenceValue as Renderer);
            Require(targets.arraySize == renderers.Length && configured.Count == renderers.Length && !configured.Contains(null),
                "Scan targets must contain every chest Renderer exactly once.");

            Bounds bounds = ChestDissolveController.CalculateLocalBounds(root.transform, renderers);
            Require(Approximately(bounds.min, serializedScan.FindProperty("boundsMin").vector3Value)
                    && Approximately(bounds.max, serializedScan.FindProperty("boundsMax").vector3Value),
                "Saved scan bounds must match every mesh in the root's local space.");
            Require(Approximately(bounds.center, controller.LocalBounds.center)
                    && Approximately(bounds.size, controller.LocalBounds.size),
                "Controller and scan space must use the same root-local bounds.");
            Require(Mathf.Approximately(new SerializedObject(controller).FindProperty("dissolveDuration").floatValue, 2f),
                "The confirmed dissolve duration is two seconds.");

            HashSet<Shader> shaders = new HashSet<Shader>();
            foreach (Renderer renderer in renderers)
            {
                Require(configured.Contains(renderer), renderer.name + " is missing from the scan targets.");
                Require(renderer.shadowCastingMode == ShadowCastingMode.On, renderer.name + " must cast shadows.");
                Require(renderer.sharedMaterials.Length > 0, renderer.name + " must have materials.");
                foreach (Material material in renderer.sharedMaterials)
                {
                    Require(material != null && material.shader != null && material.shader.name == "ArtLab/Chest Dissolve",
                        renderer.name + " needs ArtLab/Chest Dissolve in every slot.");
                    Require(Mathf.Approximately(material.GetFloat("_Progress"), 0f), "Saved chest materials must begin intact.");
                    Vector4 direction = material.GetVector("_ScanDirection");
                    Require(direction.y < 0 && Mathf.Approximately(direction.x, 0) && Mathf.Approximately(direction.z, 0),
                        "The confirmed scan direction is down the root's local Y axis.");
                    foreach (string pass in new[] { "Forward", "ShadowCaster", "DepthOnly", "DepthNormals" })
                        Require(material.FindPass(pass) >= 0, "Missing chest shader pass: " + pass);
                    shaders.Add(material.shader);
                }
            }
            foreach (Shader shader in shaders)
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Require(message.severity.ToString() != "Error", "Shader compiler error: " + message.message);

            ValidateTransformedScanSpace();
            Debug.Log("CHEST_EDITOR_VALIDATION PASS: " + assertions + " assertions; " + renderers.Length
                + " chest renderers. Pixel/shadow validation still requires a rendered player run.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void ValidateTransformedScanSpace()
    {
        GameObject root = new GameObject("Chest validation temporary root") { hideFlags = HideFlags.HideAndDontSave };
        Material material = null;
        try
        {
            root.transform.SetPositionAndRotation(new Vector3(19, -3, 8), Quaternion.Euler(23, 71, -14));
            root.transform.localScale = new Vector3(2, 0.7f, 1.3f);
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.hideFlags = HideFlags.HideAndDontSave;
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = new Vector3(0.8f, 1.2f, -0.2f);
            part.transform.localRotation = Quaternion.Euler(0, 33, 12);
            part.transform.localScale = new Vector3(1.7f, 0.5f, 0.8f);
            Renderer renderer = part.GetComponent<Renderer>();
            material = new Material(Shader.Find("ArtLab/Chest Dissolve")) { hideFlags = HideFlags.HideAndDontSave };
            renderer.sharedMaterials = new[] { material, material };
            int unrelatedId = Shader.PropertyToID("_ChestValidationUnrelated");
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetFloat(unrelatedId, 17f);
            renderer.SetPropertyBlock(block);
            block.Clear();
            block.SetFloat(unrelatedId, 29f);
            renderer.SetPropertyBlock(block, 1);

            ChestScanSpace scan = root.AddComponent<ChestScanSpace>();
            Bounds bounds = ChestDissolveController.CalculateLocalBounds(root.transform, new[] { renderer });
            Renderer[] input = { renderer };
            scan.Configure(input, bounds.min, bounds.max);
            input[0] = null;
            // A moved/rotated/scaled root must refresh coordinates without replacing other systems' overrides.
            root.transform.SetPositionAndRotation(new Vector3(-8, 5, 14), Quaternion.Euler(-17, 129, 6));
            scan.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
            renderer.GetPropertyBlock(block);
            Require(Mathf.Approximately(block.GetFloat(unrelatedId), 17), "Configure must preserve renderer property blocks.");
            Require(Approximately(block.GetMatrix("_WorldToChest"), root.transform.worldToLocalMatrix), "Root coordinate matrix did not refresh.");
            Require(Approximately(block.GetVector("_BoundsMin"), bounds.min) && Approximately(block.GetVector("_BoundsMax"), bounds.max),
                "Renderer scan bounds must remain in root space after movement.");
            renderer.GetPropertyBlock(block, 1);
            Require(Mathf.Approximately(block.GetFloat(unrelatedId), 29), "Configure must preserve per-slot property blocks.");
            Require(Approximately(block.GetMatrix("_WorldToChest"), root.transform.worldToLocalMatrix), "Per-slot blocks must receive scan coordinates.");
            renderer.GetPropertyBlock(block, 0);
            Require(block.isEmpty, "An empty material slot should keep inheriting the renderer block.");
            Require(Approximately(ChestDissolveController.CalculateLocalBounds(root.transform, new[] { renderer }).size, bounds.size),
                "Root-local bounds must be invariant under root transforms.");
            ExpectArgumentException(() => scan.Configure(null, bounds.min, bounds.max));
            ExpectArgumentException(() => scan.Configure(new Renderer[0], bounds.min, bounds.max));
            ExpectArgumentException(() => scan.Configure(new Renderer[] { null }, bounds.min, bounds.max));
            ExpectArgumentException(() => scan.Configure(new[] { renderer }, bounds.max, bounds.min));
            ExpectArgumentException(() => scan.Configure(new[] { renderer }, bounds.min, bounds.min));
            ExpectArgumentException(() => scan.Configure(new[] { renderer }, new Vector3(float.NaN, 0, 0), bounds.max));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void ExpectArgumentException(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Invalid scan configuration must fail immediately.");
    }

    private static bool Approximately(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.000001f;
    private static bool Approximately(Matrix4x4 a, Matrix4x4 b)
    {
        for (int index = 0; index < 16; index++)
            if (Mathf.Abs(a[index] - b[index]) > 0.0001f) return false;
        return true;
    }

    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("CHEST_EDITOR_VALIDATION FAIL: " + message);
    }
}
