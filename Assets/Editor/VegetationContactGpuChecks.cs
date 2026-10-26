using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// This is an explicit Editor diagnostic, never a gameplay component. It reads
// the actual production HLSL on the GPU without touching scenes or mesh assets.
public static class VegetationContactGpuChecks
{
    const string IsolatedAssets = "/private/tmp/adventure-trio-knight-20261021.OQzBR1/project/Assets";
    const string Output = "LocalDocs/Environment/VegetationContactFix-20261024/gpu-validation.txt";
    const string MeshPath = "Assets/Art/LivingEnvironment/Meshes/BlueFlowerCluster_3.asset";
    const string MaterialPath = "Assets/Art/LivingEnvironment/Materials/OwnFlowerLit_plant.mat";
    const string ShaderPath = "Assets/Editor/VegetationContactProbe.shader";
    const float PinnedTolerance = .00002f;
    const float ShapeTolerance = .001f;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static readonly int DirectionId = Shader.PropertyToID("_FRWindDirection");
    static readonly int ClockId = Shader.PropertyToID("_FRWindClock");
    static readonly int PlayerId = Shader.PropertyToID("_FRWindPlayer");
    static readonly int TrailId = Shader.PropertyToID("_FRWindTrail");
    static readonly int MotionId = Shader.PropertyToID("_FRWindPlayerDirection");
    static readonly int TrailMotionId = Shader.PropertyToID("_FRWindTrailDirection");
    static readonly int CameraId = Shader.PropertyToID("_FRWindCamera");
    static readonly int RadiusId = Shader.PropertyToID("_FRWindInteractionRadius");

    static void Need(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("GPU vegetation check: " + message);
    }

    [MenuItem("Tools/Living Environment/Check Contact On Actual GPU In Isolated Project")]
    public static void RunStatic()
    {
        Need(string.Equals(Application.dataPath, IsolatedAssets, StringComparison.Ordinal),
            "This diagnostic is restricted to the named isolated project.");
        Need(!Application.isPlaying, "Run the static probe outside Play Mode.");
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        var report = new StringBuilder();
        report.AppendLine("ACTUAL GPU VEGETATION CONTACT VALIDATION");
        report.AppendLine("mesh=" + MeshPath);
        report.AppendLine("productionFunction=FRDeformVegetation; diagnostic=Editor-only six-vertex-per-pixel probe");
        report.AppendLine("device=" + SystemInfo.graphicsDeviceName);
        report.AppendLine("api=" + SystemInfo.graphicsDeviceType + "; version=" + SystemInfo.graphicsDeviceVersion);
        try
        {
            CheckOnGpu(report);
            report.AppendLine("RESULT=PASS");
            Debug.Log(report.ToString());
        }
        catch (Exception exception)
        {
            report.AppendLine("RESULT=FAIL");
            report.AppendLine(exception.ToString());
            throw;
        }
        finally
        {
            File.WriteAllText(Output, report.ToString());
        }
    }

    static void CheckOnGpu(StringBuilder report)
    {
        Need(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
            "A real graphics device is required; do not launch with -nographics.");
        Need(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) &&
            SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat), "32-bit float render/readback support.");
        var source = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        Need(source, "Actual authored flower mesh available for Editor readback.");
        Need(sourceMaterial && sourceMaterial.shader &&
            sourceMaterial.shader.name == "FoldedRealm/LivingVegetation", "Actual authored living flower material.");
        Need(shader && shader.isSupported, "Diagnostic shader compiled and supported.");
        var roots = new List<Vector4>();
        var data = new List<Vector4>();
        source.GetUVs(2, roots);
        source.GetUVs(3, data);
        int count = source.vertexCount;
        Need(count > 0 && count <= SystemInfo.maxTextureSize, "Original vertex count fits one GPU row.");
        Need(roots.Count == count && data.Count == count, "Actual mesh contains UV2 and UV3 metadata.");
        for (int i = 0; i < count; i++) Need(data[i].w > .5f, "Coherent metadata on original vertex " + i);
        report.AppendLine("originalVertices=" + count + "; renderTarget=RGBAFloat " + count + "x1; transformCases=2");

        Mesh probe = null;
        Material material = null;
        RenderTexture target = null;
        Texture2D readback = null;
        var previousTarget = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        var globals = new WindGlobals();
        try
        {
            probe = BuildProbeMesh(source, roots, data);
            material = new Material(shader) { name = "Transient vegetation GPU contact diagnostic", hideFlags = HideFlags.HideAndDontSave };
            foreach (string property in new[] { "_VertexDataValid", "_WindAmplitude", "_WindDetail", "_InteractionStrength" })
            {
                Need(sourceMaterial.HasProperty(property), "Source material has " + property);
                material.SetFloat(property, sourceMaterial.GetFloat(property));
                report.AppendLine(property + "=" + Number(sourceMaterial.GetFloat(property)));
            }
            Need(material.GetFloat("_VertexDataValid") > .5f && material.GetFloat("_InteractionStrength") > .001f,
                "Actual source material enables metadata and contact.");
            material.SetFloat("_ProbeVertexCount", count);
            target = new RenderTexture(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = "Transient vegetation position readback",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            Need(target.Create(), "Float render target creation.");
            readback = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "Transient vegetation GPU position pixels",
                hideFlags = HideFlags.HideAndDontSave
            };
            Shader.SetGlobalVector(DirectionId, new Vector4(.83f, .56f, 0, 1));
            Shader.SetGlobalFloat(ClockId, 17.25f);
            Shader.SetGlobalVector(CameraId, new Vector4(0, 0, 0, 65));
            Shader.SetGlobalFloat(RadiusId, .72f);
            Shader.SetGlobalVector(MotionId, new Vector4(0, 1, 0, 0));
            Shader.SetGlobalVectorArray(TrailId, new Vector4[6]);
            Shader.SetGlobalVectorArray(TrailMotionId, new Vector4[6]);
            report.AppendLine("windEnabled=1; windStrength=0; clock=17.25; radius=.72; motion=(0,1); trails=zero");
            var matrices = new[]
            {
                Matrix4x4.identity,
                Matrix4x4.TRS(new Vector3(2, 1, -3), Quaternion.Euler(18, 37, -12), new Vector3(1.3f, .8f, 1.1f))
            };
            var labels = new[] { "identity", "TRS translation=(2,1,-3) euler=(18,37,-12) scale=(1.3,.8,1.1)" };
            for (int transformIndex = 0; transformIndex < matrices.Length; transformIndex++)
            {
            Matrix4x4 objectToWorld = matrices[transformIndex];
            report.AppendLine("case=" + labels[transformIndex]);
            Vector3 firstRoot = objectToWorld.MultiplyPoint3x4(Root(roots[0]));
            Vector3 foot = firstRoot + new Vector3(-.08f, 0, -.10f);
            report.AppendLine("nearFoot=" + Coordinates(foot));
            Vector3[] baseline = Render(probe, material, target, readback, objectToWorld, foot, 0);
            Vector3[] contact = Render(probe, material, target, readback, objectToWorld, foot, 1);
            Vector3[] sitting = Render(probe, material, target, readback, objectToWorld, foot, .08f);
            float rootSpread = 0;
            foreach (Vector4 localRoot in roots)
                rootSpread = Mathf.Max(rootSpread, Vector3.Distance(firstRoot, objectToWorld.MultiplyPoint3x4(Root(localRoot))));
            Vector3 farFoot = firstRoot + new Vector3(rootSpread + 2.1f, 0, 0);
            Vector3[] far = Render(probe, material, target, readback, objectToWorld, farFoot, 1);
            Vector3[] original = source.vertices;
            float baselineError = 0, maxDisplacement = 0, maxTipDisplacement = 0;
            float baseDisplacement = 0, farDisplacement = 0, sittingDisplacement = 0, sittingRatio = 0;
            float minFarDistance = float.PositiveInfinity;
            int pinnedVertices = 0, respondingUpperVertices = 0;
            var upperGroups = new Dictionary<Vector3, List<int>>();
            for (int i = 0; i < count; i++)
            {
                float displacement = Vector3.Distance(baseline[i], contact[i]);
                float sittingDistance = Vector3.Distance(baseline[i], sitting[i]);
                baselineError = Mathf.Max(baselineError, Vector3.Distance(baseline[i], objectToWorld.MultiplyPoint3x4(original[i])));
                maxDisplacement = Mathf.Max(maxDisplacement, displacement);
                farDisplacement = Mathf.Max(farDisplacement, Vector3.Distance(baseline[i], far[i]));
                sittingDisplacement = Mathf.Max(sittingDisplacement, sittingDistance);
                if (displacement > .001f) sittingRatio = Mathf.Max(sittingRatio, sittingDistance / displacement);
                Vector3 root = Root(roots[i]);
                Vector3 rootWS = objectToWorld.MultiplyPoint3x4(root);
                minFarDistance = Mathf.Min(minFarDistance, Vector2.Distance(
                    new Vector2(rootWS.x, rootWS.z), new Vector2(farFoot.x, farFoot.z)));
                if (roots[i].w <= .0001f)
                {
                    pinnedVertices++;
                    baseDisplacement = Mathf.Max(baseDisplacement, displacement);
                }
                if (roots[i].w < .62f) continue;
                maxTipDisplacement = Mathf.Max(maxTipDisplacement, displacement);
                if (displacement > .001f) respondingUpperVertices++;
                if (!upperGroups.TryGetValue(root, out var group)) upperGroups.Add(root, group = new List<int>());
                group.Add(i);
            }
            float shapeError = 0;
            int checkedPairs = 0;
            foreach (var group in upperGroups.Values)
            {
                Need(group.Count > 1, "At least two upper vertices share every flower root.");
                for (int a = 0; a < group.Count; a++)
                    for (int b = a + 1; b < group.Count; b++)
                    {
                        int ia = group[a], ib = group[b];
                        float before = Vector3.Distance(baseline[ia], baseline[ib]);
                        float after = Vector3.Distance(contact[ia], contact[ib]);
                        shapeError = Mathf.Max(shapeError, Mathf.Abs(after - before));
                        checkedPairs++;
                    }
            }
            report.AppendLine("baselineVsOriginalMaxMetres=" + Number(baselineError));
            report.AppendLine("pinnedBaseVertices=" + pinnedVertices + "; baseMaxDisplacementMetres=" + Number(baseDisplacement));
            report.AppendLine("contactMaxDisplacementMetres=" + Number(maxDisplacement) + "; upperMaxDisplacementMetres=" + Number(maxTipDisplacement) +
                "; respondingUpperVertices=" + respondingUpperVertices);
            report.AppendLine("sharedRootUpperGroups=" + upperGroups.Count + "; upperPairsChecked=" + checkedPairs +
                "; maxPairwiseDistanceChangeMetres=" + Number(shapeError));
            report.AppendLine("farFoot=" + Coordinates(farFoot) + "; minimumRootDistanceMetres=" + Number(minFarDistance) +
                "; outOfRangeMaxDisplacementMetres=" + Number(farDisplacement));
            report.AppendLine("sittingInfluence=.08; sittingMaxDisplacementMetres=" + Number(sittingDisplacement) +
                "; maxSittingToMovingRatio=" + Number(sittingRatio));
            Need(baselineError <= PinnedTolerance, "GPU baseline matches transformed authored positions with wind strength zero.");
            Need(pinnedVertices > 0 && baseDisplacement <= PinnedTolerance, "Actual base vertices remain pinned.");
            Need(respondingUpperVertices > 0 && maxTipDisplacement > .001f, "Actual upper flower vertices respond to nearby passage.");
            Need(maxDisplacement < .17f, "Actual contact displacement stays below .17 metres.");
            Need(upperGroups.Count == 3 && checkedPairs > 0 && shapeError < ShapeTolerance,
                "All three upper flower heads preserve pairwise distances within .001 metres.");
            Need(minFarDistance >= 2 && farDisplacement <= PinnedTolerance, "Out-of-range contact leaves all vertices unchanged.");
            Need(sittingDisplacement <= maxDisplacement * .10f + PinnedTolerance && sittingRatio <= .10f,
                "Stationary .08 influence produces at most ten percent of moving contact.");
            }
        }
        finally
        {
            globals.Restore();
            RenderTexture.active = previousTarget;
            GL.sRGBWrite = previousSrgb;
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (readback) Object.DestroyImmediate(readback);
            if (material) Object.DestroyImmediate(material);
            if (probe) Object.DestroyImmediate(probe);
        }
        report.AppendLine("cleanup=runtime objects destroyed; render/GL matrices, target, sRGB and wind global values restored");
        report.AppendLine("assetsAndScenes=read-only; no scene loads/saves, mesh writes or AssetDatabase.SaveAssets");
    }

    static Mesh BuildProbeMesh(Mesh source, List<Vector4> roots, List<Vector4> data)
    {
        Vector3[] original = source.vertices, normals = source.normals;
        int count = original.Length * 6;
        var positions = new Vector3[count];
        var directions = new Vector3[count];
        var duplicateRoots = new List<Vector4>(count);
        var duplicateData = new List<Vector4>(count);
        var indices = new int[count];
        for (int i = 0; i < count; i++)
        {
            int originalIndex = i / 6;
            positions[i] = original[originalIndex];
            directions[i] = normals.Length == original.Length ? normals[originalIndex] : Vector3.up;
            duplicateRoots.Add(roots[originalIndex]);
            duplicateData.Add(data[originalIndex]);
            indices[i] = i;
        }
        var result = new Mesh
        {
            name = "Transient six-corner copies of actual authored flower vertices",
            hideFlags = HideFlags.HideAndDontSave,
            indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            vertices = positions,
            normals = directions
        };
        result.SetUVs(2, duplicateRoots);
        result.SetUVs(3, duplicateData);
        result.SetIndices(indices, MeshTopology.Triangles, 0, false);
        result.bounds = source.bounds;
        return result;
    }

    static Vector3[] Render(Mesh mesh, Material material, RenderTexture target, Texture2D readback,
        Matrix4x4 objectToWorld, Vector3 foot, float influence)
    {
        Shader.SetGlobalVector(PlayerId, new Vector4(foot.x, foot.y, foot.z, influence));
        var previousTarget = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        GL.PushMatrix();
        try
        {
            Graphics.SetRenderTarget(target);
            GL.sRGBWrite = false;
            GL.LoadProjectionMatrix(Matrix4x4.identity);
            GL.modelview = Matrix4x4.identity;
            GL.Clear(false, true, Color.clear);
            Need(material.SetPass(0), "Actual GPU diagnostic material pass.");
            Graphics.DrawMeshNow(mesh, objectToWorld);
            readback.ReadPixels(new Rect(0, 0, target.width, 1), 0, 0, false);
            readback.Apply(false, false);
            Color[] pixels = readback.GetPixels();
            var positions = new Vector3[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                Need(Mathf.Abs(pixel.a - 1) < .00001f, "Original vertex " + i + " rasterized to its GPU pixel.");
                Need(Finite(pixel.r) && Finite(pixel.g) && Finite(pixel.b), "Finite GPU position for original vertex " + i);
                positions[i] = new Vector3(pixel.r, pixel.g, pixel.b);
            }
            return positions;
        }
        finally
        {
            GL.PopMatrix();
            RenderTexture.active = previousTarget;
            GL.sRGBWrite = previousSrgb;
        }
    }

    // Unity cannot remove an already allocated global vector-array slot. An
    // originally unset trail is restored to the identical zero influence field.
    sealed class WindGlobals
    {
        readonly Vector4 direction = Shader.GetGlobalVector(DirectionId);
        readonly float clock = Shader.GetGlobalFloat(ClockId);
        readonly Vector4 player = Shader.GetGlobalVector(PlayerId);
        readonly Vector4 motion = Shader.GetGlobalVector(MotionId);
        readonly Vector4 camera = Shader.GetGlobalVector(CameraId);
        readonly float radius = Shader.GetGlobalFloat(RadiusId);
        readonly Vector4[] trail = Shader.GetGlobalVectorArray(TrailId);
        readonly Vector4[] trailMotion = Shader.GetGlobalVectorArray(TrailMotionId);
        public void Restore()
        {
            Shader.SetGlobalVector(DirectionId, direction);
            Shader.SetGlobalFloat(ClockId, clock);
            Shader.SetGlobalVector(PlayerId, player);
            Shader.SetGlobalVector(MotionId, motion);
            Shader.SetGlobalVector(CameraId, camera);
            Shader.SetGlobalFloat(RadiusId, radius);
            Shader.SetGlobalVectorArray(TrailId, trail != null && trail.Length > 0 ? trail : new Vector4[6]);
            Shader.SetGlobalVectorArray(TrailMotionId, trailMotion != null && trailMotion.Length > 0 ? trailMotion : new Vector4[6]);
        }
    }

    static Vector3 Root(Vector4 value) => new Vector3(value.x, value.y, value.z);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static string Number(float value) => value.ToString("G9", Invariant);
    static string Coordinates(Vector3 value) => "(" + Number(value.x) + "," + Number(value.y) + "," + Number(value.z) + ")";
}
