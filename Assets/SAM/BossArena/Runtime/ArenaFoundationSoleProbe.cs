using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SamProduction.BossArena
{
    // Explicitly constructed by foundation review or headless motion contracts. No Update, registration,
    // rendering changes, socket fallback, or effect on ordinary combat/benchmark.
    public sealed class ArenaFoundationSoleProbe : IDisposable
    {
        public const string Method = "baked_mesh_dominant_foot_weight_lowest_5mm_v1";
        private const float PatchDepth = .005f;

        [Serializable] public sealed class Sample
        {
            public int schema = 1;
            public string method = Method;
            public bool reviewOnly = true, reliable;
            public string reason = "", meshName = "";
            public int sequenceIndex, vertexCount, boneCount, rendererInstanceId, meshInstanceId;
            public float sourceTime, patchDepthM = PatchDepth;
            public double sampleCostMs;
            public Vector3 rendererLossyScale;
            public bool sourceMeshReadable, hasPreviousSameClip, hasPreviousAcrossClip;
            public float previousSourceTime;
            public int leftCandidateCount, rightCandidateCount, leftPatchCount, rightPatchCount;
            public float leftMinimumDominantWeight, rightMinimumDominantWeight;
            public float leftMinimumY, rightMinimumY;
            public Vector3 leftMinimumPoint, rightMinimumPoint, leftSolePatch, rightSolePatch;
            public Vector3 leftPatchBoundsMin, leftPatchBoundsMax, rightPatchBoundsMin, rightPatchBoundsMax;
            public int groundLayerMask = 1 << 8;
            public bool leftGroundFound, rightGroundFound;
            public Vector3 leftGroundPoint, rightGroundPoint;
            public float leftGroundClearanceM, rightGroundClearanceM;
            public float leftPreviousMinimumY, rightPreviousMinimumY;
            public int leftContactPairVertexCount, rightContactPairVertexCount;
            public bool leftContactPairFallback, rightContactPairFallback;
            public float leftMedianPatchTravelM, rightMedianPatchTravelM, leftMaximumPatchTravelM, rightMaximumPatchTravelM;
        }

        private sealed class Foot
        {
            public int[] indices;
            public Vector3[] current, previous;
            public float[] distances;
            public float minimumDominantWeight = 1, previousMinimum;
            public Foot(List<int> selected, float weight)
            {
                indices = selected.ToArray(); minimumDominantWeight = weight;
                current = new Vector3[indices.Length]; previous = new Vector3[indices.Length];
                distances = new float[indices.Length];
            }
        }

        private readonly SkinnedMeshRenderer renderer;
        private Mesh source, baked;
        private List<Vector3> vertices;
        private Foot left, right;
        private string failure = "";
        private bool disposed, previousValid;
        private int previousSequence, expectedBoneCount;
        private float previousTime;

        public ArenaFoundationSoleProbe(SkinnedMeshRenderer target)
        {
            renderer = target;
            var arguments = Environment.GetCommandLineArgs();
            bool foundationCheck = Array.IndexOf(arguments, "--sam-foundation-review") >= 0
                || (Array.IndexOf(arguments, "--sam-battle-contracts") >= 0 && Array.IndexOf(arguments, "--sam-battle-sole") >= 0)
                || Array.IndexOf(arguments, "--sam-patrol-contact-contracts") >= 0
                || Array.IndexOf(arguments, "--sam-patrol-near-endpoint-contracts") >= 0
                || Array.IndexOf(arguments, "--sam-stomp-contracts") >= 0
                || Array.IndexOf(arguments, "--sam-ignite-contracts") >= 0
                || Array.IndexOf(arguments, "--sam-dive-contracts") >= 0 || Array.IndexOf(arguments, "--sam-meteor-contracts") >= 0
                || Array.IndexOf(arguments, "--sam-foundation-contracts") >= 0
                || Array.IndexOf(arguments, "--sam-dash-contracts") >= 0;
            if (!Application.isPlaying || !foundationCheck
                || Array.IndexOf(arguments, "--sam-benchmark") >= 0)
            {
                failure = "CPU sole probe is permitted only in explicit foundation or dash motion checks, never benchmark.";
                return;
            }
            try
            {
                if (renderer == null || renderer.sharedMesh == null) throw new InvalidOperationException("No actual skinned SAM mesh.");
                source = renderer.sharedMesh;
                if (!source.isReadable) throw new InvalidOperationException("Actual SAM mesh is not readable; enable Read/Write on its source importer for review.");
                var bones = renderer.bones; expectedBoneCount = bones.Length;
                if (expectedBoneCount != 49) throw new InvalidOperationException("Expected the preserved 49-bone SAM export.");
                int leftBone = BoneIndex(bones, "foot.L"), rightBone = BoneIndex(bones, "foot.R");
                var leftIds = new List<int>(); var rightIds = new List<int>();
                float leftWeight = 1, rightWeight = 1;
                // Mesh-owned NativeArrays (Allocator.None): do not dispose them.
                var counts = source.GetBonesPerVertex(); var weights = source.GetAllBoneWeights();
                if (counts.Length != source.vertexCount) throw new InvalidOperationException("Incomplete per-vertex skin weights.");
                int cursor = 0;
                for (int vertex = 0; vertex < counts.Length; vertex++)
                {
                    int dominant = -1; float maximum = -1, runnerUp = -1;
                    for (int j = 0; j < counts[vertex]; j++)
                    {
                        var weight = weights[cursor++];
                        if (!Finite(weight.weight) || weight.weight < 0 || weight.boneIndex < 0 || weight.boneIndex >= bones.Length)
                            throw new InvalidOperationException("Invalid exported skin weight.");
                        if (weight.weight > maximum) { runnerUp = maximum; maximum = weight.weight; dominant = weight.boneIndex; }
                        else runnerUp = Mathf.Max(runnerUp, weight.weight);
                    }
                    if (dominant != leftBone && dominant != rightBone) continue;
                    if (maximum <= 0 || Mathf.Abs(maximum - runnerUp) < .000001f)
                        throw new InvalidOperationException("Ambiguous dominant foot weight; source boot selection cannot be trusted.");
                    if (dominant == leftBone) { leftIds.Add(vertex); leftWeight = Mathf.Min(leftWeight, maximum); }
                    else { rightIds.Add(vertex); rightWeight = Mathf.Min(rightWeight, maximum); }
                }
                if (cursor != weights.Length || leftIds.Count < 3 || rightIds.Count < 3)
                    throw new InvalidOperationException("Insufficient dominant foot-weighted boot vertices.");
                left = new Foot(leftIds, leftWeight); right = new Foot(rightIds, rightWeight);
                baked = new Mesh { name = "Foundation review CPU sole snapshot (not gameplay)" };
                baked.MarkDynamic(); vertices = new List<Vector3>(source.vertexCount);
            }
            catch (Exception exception) { failure = exception.GetType().Name + ": " + exception.Message; }
        }

        // Call after the current animation pose has been evaluated. Store this
        // newly allocated result in the review row; the heavy mesh/vertex/history
        // buffers are reused. A clip index prevents comparisons across restarts.
        public void ResetHistory() { previousValid=false; }

        public Sample Capture(int sequenceIndex, float sourceTime, bool allowAcrossClip=false)
        {
            long started = Stopwatch.GetTimestamp();
            var result = new Sample { sequenceIndex = sequenceIndex, sourceTime = sourceTime };
            try
            {
                if (disposed) throw new ObjectDisposedException(nameof(ArenaFoundationSoleProbe));
                if (failure.Length > 0) throw new InvalidOperationException(failure);
                if (!Finite(sourceTime) || sourceTime < 0 || sequenceIndex < 0) throw new InvalidOperationException("Invalid source sample time/index.");
                if (renderer == null || renderer.sharedMesh != source || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Reviewed mesh was changed, hidden or destroyed.");
                result.meshName = source.name; result.vertexCount = source.vertexCount; result.boneCount = expectedBoneCount;
                result.rendererInstanceId = renderer.GetInstanceID(); result.meshInstanceId = source.GetInstanceID();
                result.sourceMeshReadable = source.isReadable; result.rendererLossyScale = renderer.transform.lossyScale;
                result.leftCandidateCount = left.indices.Length; result.rightCandidateCount = right.indices.Length;
                result.leftMinimumDominantWeight = left.minimumDominantWeight; result.rightMinimumDominantWeight = right.minimumDominantWeight;
                // BakeMesh(false) yields vertices relative to the renderer; apply
                // its localToWorld once. CPU skinning includes current shapes.
                renderer.BakeMesh(baked, false); baked.GetVertices(vertices);
                if (vertices.Count != source.vertexCount) throw new InvalidOperationException("Baked topology differs from selected source mesh.");
                var world = renderer.localToWorldMatrix;
                result.hasPreviousSameClip = previousValid && sequenceIndex == previousSequence && sourceTime > previousTime;
                result.hasPreviousAcrossClip = allowAcrossClip && previousValid && sequenceIndex != previousSequence;
                result.previousSourceTime = previousTime;
                Measure(left, world, result.hasPreviousSameClip || result.hasPreviousAcrossClip, true, result);
                Measure(right, world, result.hasPreviousSameClip || result.hasPreviousAcrossClip, false, result);
                previousValid = true; previousSequence = sequenceIndex; previousTime = sourceTime;
                result.reliable = true;
            }
            catch (Exception exception) { result.reason = exception.GetType().Name + ": " + exception.Message; previousValid = false; }
            result.sampleCostMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            return result;
        }

        private void Measure(Foot foot, Matrix4x4 world, bool pair, bool isLeft, Sample result)
        {
            float minimum = float.PositiveInfinity; Vector3 minimumPoint = Vector3.zero;
            for (int i = 0; i < foot.indices.Length; i++)
            {
                var p = world.MultiplyPoint3x4(vertices[foot.indices[i]]);
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) throw new InvalidOperationException("Non-finite baked world vertex.");
                foot.current[i] = p;
                if (p.y < minimum) { minimum = p.y; minimumPoint = p; }
            }
            int patchCount = 0, pairCount = 0;
            Vector3 centre = Vector3.zero, boundsMin = Vector3.positiveInfinity, boundsMax = Vector3.negativeInfinity;
            for (int i = 0; i < foot.indices.Length; i++)
            {
                var p = foot.current[i];
                if (p.y > minimum + PatchDepth) continue;
                patchCount++; centre += p; boundsMin = Vector3.Min(boundsMin, p); boundsMax = Vector3.Max(boundsMax, p);
                if (pair && foot.previous[i].y <= foot.previousMinimum + PatchDepth)
                    foot.distances[pairCount++] = PlanarDistance(p, foot.previous[i]);
            }
            if (patchCount == 0) throw new InvalidOperationException("No actual lowest boot patch.");
            bool fallback = pair && pairCount == 0;
            // Match author_motion_review.py: intersect stable low vertices at
            // both endpoints; if empty use this frame's patch, and expose that.
            if (fallback)
                for (int i = 0; i < foot.indices.Length; i++)
                    if (foot.current[i].y <= minimum + PatchDepth)
                        foot.distances[pairCount++] = PlanarDistance(foot.current[i], foot.previous[i]);
            float median = 0, maximum = 0;
            if (pairCount > 0)
            {
                Array.Sort(foot.distances, 0, pairCount); maximum = foot.distances[pairCount - 1];
                int middle = pairCount / 2;
                median = pairCount % 2 == 1 ? foot.distances[middle] : (foot.distances[middle - 1] + foot.distances[middle]) * .5f;
            }
            if (isLeft)
            {
                result.leftMinimumY = minimum; result.leftMinimumPoint = minimumPoint; result.leftSolePatch = centre / patchCount;
                result.leftPatchCount = patchCount; result.leftPatchBoundsMin = boundsMin; result.leftPatchBoundsMax = boundsMax;
                result.leftPreviousMinimumY = foot.previousMinimum; result.leftContactPairVertexCount = pairCount;
                result.leftContactPairFallback = fallback; result.leftMedianPatchTravelM = median; result.leftMaximumPatchTravelM = maximum;
                result.leftGroundFound = Physics.Raycast(minimumPoint + Vector3.up * 2, Vector3.down, out var hit, 8,
                    result.groundLayerMask, QueryTriggerInteraction.Ignore);
                if (result.leftGroundFound) { result.leftGroundPoint = hit.point; result.leftGroundClearanceM = minimum - hit.point.y; }
            }
            else
            {
                result.rightMinimumY = minimum; result.rightMinimumPoint = minimumPoint; result.rightSolePatch = centre / patchCount;
                result.rightPatchCount = patchCount; result.rightPatchBoundsMin = boundsMin; result.rightPatchBoundsMax = boundsMax;
                result.rightPreviousMinimumY = foot.previousMinimum; result.rightContactPairVertexCount = pairCount;
                result.rightContactPairFallback = fallback; result.rightMedianPatchTravelM = median; result.rightMaximumPatchTravelM = maximum;
                result.rightGroundFound = Physics.Raycast(minimumPoint + Vector3.up * 2, Vector3.down, out var hit, 8,
                    result.groundLayerMask, QueryTriggerInteraction.Ignore);
                if (result.rightGroundFound) { result.rightGroundPoint = hit.point; result.rightGroundClearanceM = minimum - hit.point.y; }
            }
            foot.previousMinimum = minimum;
            var reuse = foot.previous; foot.previous = foot.current; foot.current = reuse;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float PlanarDistance(Vector3 a, Vector3 b) { float x = a.x - b.x, z = a.z - b.z; return Mathf.Sqrt(x * x + z * z); }
        private static int BoneIndex(Transform[] bones, string name)
        {
            int index = -1;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].name == name)
                {
                    if (index >= 0) throw new InvalidOperationException("Duplicate bone: " + name);
                    index = i;
                }
            if (index < 0) throw new InvalidOperationException("Missing original export bone: " + name);
            return index;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; previousValid = false;
            if (baked != null) UnityEngine.Object.Destroy(baked);
        }
    }
}
