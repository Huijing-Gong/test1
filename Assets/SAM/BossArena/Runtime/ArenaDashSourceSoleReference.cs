using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SamProduction.BossArena
{
    // Explicit headless QA utility only. Isolated one-clip manual graph, with no
    // gameplay components, physics, audio, rendering or live animation writes.
    public sealed class ArenaDashSourceSoleReference : IDisposable
    {
        public const string Method = "isolated_single_clip_graph_exact_endpoint_lowest5mm_common_vertices_v2";
        private const float PatchDepth = .005f;

        [Serializable] public sealed class FootPair
        {
            public int candidateCount, fromPatchCount, toPatchCount, commonPatchCount, measuredPairCount;
            public float minimumDominantWeight, fromMinimumY, toMinimumY;
            public Vector3 fromMinimumPoint, toMinimumPoint, fromPatchCentre, toPatchCentre;
            public bool contactPairFallback;
            public float medianPatchTravelM, maximumPatchTravelM;
        }

        [Serializable] public sealed class Sample
        {
            public int schema = 1;
            public string method = Method;
            public bool reviewOnly = true, reliable;
            public string reason = "", clipId = "", clipName = "", sourceSha256 = "", fbxSha256 = "";
            public string scope = "Canonical source endpoint displacement only; independent raw FBX FK, actual sole floor and runtime coverage checks remain required.";
            public int sequenceIndex, clipInstanceId, originalMeshInstanceId, liveRendererInstanceId, referenceRendererInstanceId;
            public int vertexCount, boneCount, shapeCount;
            public float fromSourceTime, toSourceTime, clipLength, patchDepthM = PatchDepth;
            public Vector3 fromRoot, toRoot, referenceVisualLocalPosition, referenceVisualLocalScale;
            public Quaternion referenceVisualLocalRotation;
            public float fromYaw, toYaw;
            public double sampleCostMs;
            public FootPair left, right;
        }

        private sealed class SavedTransform
        {
            public Transform transform;
            public Vector3 position, scale;
            public Quaternion rotation;
            public void Restore()
            {
                transform.localPosition = position;
                transform.localRotation = rotation;
                transform.localScale = scale;
            }
        }

        private sealed class Foot
        {
            public int[] indices;
            public float minimumWeight;
            public Vector3[] from, to;
            public Foot(List<int> values, float weight)
            { indices = values.ToArray(); minimumWeight = weight; from = new Vector3[indices.Length]; to = new Vector3[indices.Length]; }
        }

        private readonly SkinnedMeshRenderer live;
        private readonly Mesh source;
        private readonly GameObject actor;
        private readonly Transform visual;
        private readonly SkinnedMeshRenderer renderer;
        private readonly Mesh baked;
        private readonly Foot left, right;
        private readonly List<Vector3> vertices;
        private readonly List<SavedTransform> initial = new List<SavedTransform>();
        private Animator referenceAnimator;
        private PlayableGraph referenceGraph;
        private AnimationClipPlayable referenceClip;
        private AnimationPlayableOutput referenceOutput;
        private AnimationClip sampledClip;
        private bool disposed;
        public bool DisposedAndObjectsDestroyed => disposed && actor == null && baked == null;

        public static bool Requested
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                return Application.isPlaying
                    && (Array.IndexOf(args, "--sam-dash-contracts") >= 0 || Array.IndexOf(args, "--sam-stomp-contracts") >= 0 || Array.IndexOf(args, "--sam-ignite-contracts") >= 0 || Array.IndexOf(args, "--sam-dive-contracts") >= 0 || Array.IndexOf(args, "--sam-meteor-contracts") >= 0)
                    && Array.IndexOf(args, "--sam-source-sole-reference") >= 0
                    && Array.IndexOf(args, "--sam-benchmark") < 0;
            }
        }

        // Construct once after Fixture has restored the ordinary visual root,
        // before any death roll, residual bridge or patrol IK. All clip bindings
        // are relative to liveVisual, matching the existing ArenaAnimation root.
        public ArenaDashSourceSoleReference(Transform liveVisual, SkinnedMeshRenderer liveRenderer)
        {
            if (!Requested) throw new InvalidOperationException("Source sole reference requires explicit headless QA flags and is forbidden in benchmark.");
            if (liveVisual == null || liveRenderer == null || liveRenderer.sharedMesh == null)
                throw new ArgumentException("The real visual animation root and skinned mesh are required.");
            live = liveRenderer; source = live.sharedMesh;
            if (!source.isReadable || live.bones.Length != 49 || source.blendShapeCount != 4)
                throw new InvalidOperationException("Expected the preserved readable Unity mesh, 49 bones and four corrective shapes.");
            // Source placement uses a unit actor. A scaled ancestor would change
            // actual contact distances and needs an explicitly revised contract.
            if (liveVisual.parent != null && (liveVisual.parent.lossyScale - Vector3.one).sqrMagnitude > 1e-10f)
                throw new InvalidOperationException("Nonunit actor ancestor scale is unsupported by this canonical reference.");
            try
            {
                actor = NewObject("SAM QA isolated source sole reference");
                var map = new Dictionary<Transform, Transform>();
                visual = CopyTransformTree(liveVisual, actor.transform, map);
                referenceAnimator=visual.gameObject.AddComponent<Animator>();
                referenceAnimator.applyRootMotion=false;referenceAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var liveAnimator=liveVisual.GetComponent<Animator>();if(liveAnimator!=null)referenceAnimator.avatar=liveAnimator.avatar;
                referenceGraph=PlayableGraph.Create("SAM QA isolated source clip");referenceGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                referenceOutput=AnimationPlayableOutput.Create(referenceGraph,"Isolated source bones",referenceAnimator);referenceGraph.Play();
                if (!map.TryGetValue(live.transform, out var meshTransform))
                    throw new InvalidOperationException("Live mesh is outside its animation binding root.");
                renderer = meshTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.enabled = false; renderer.updateWhenOffscreen = true;
                renderer.sharedMesh = source; renderer.quality = live.quality;
                renderer.localBounds = live.localBounds;
                var bones = new Transform[live.bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    if (live.bones[i] == null || !map.TryGetValue(live.bones[i], out bones[i]))
                        throw new InvalidOperationException("A live skin bone lies outside its visual root.");
                renderer.bones = bones;
                if (live.rootBone != null)
                {
                    if (!map.TryGetValue(live.rootBone, out var root)) throw new InvalidOperationException("Skin root bone cannot be remapped.");
                    renderer.rootBone = root;
                }
                SelectBootVertices(live.bones, out left, out right);
                baked = new Mesh { name = "SAM QA source endpoint CPU skin snapshot", hideFlags = HideFlags.HideAndDontSave };
                baked.MarkDynamic(); vertices = new List<Vector3>(source.vertexCount);
                AssertIsolation();
            }
            catch
            {
                if(referenceGraph.IsValid())referenceGraph.Destroy();
                if (baked != null) UnityEngine.Object.DestroyImmediate(baked);
                if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
                throw;
            }
        }

        private static GameObject NewObject(string name)
        { return new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; }

        private Transform CopyTransformTree(Transform original, Transform parent, Dictionary<Transform, Transform> map)
        {
            var copy = NewObject(original.name).transform; copy.SetParent(parent, false);
            copy.localPosition = original.localPosition; copy.localRotation = original.localRotation; copy.localScale = original.localScale;
            map.Add(original, copy);
            initial.Add(new SavedTransform { transform = copy, position = copy.localPosition, rotation = copy.localRotation, scale = copy.localScale });
            var siblings = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < original.childCount; i++)
            {
                var child = original.GetChild(i);
                if (!siblings.Add(child.name)) throw new InvalidOperationException("Duplicate sibling names make clip binding ambiguous: " + child.name);
                CopyTransformTree(child, copy, map);
            }
            return copy;
        }

        private void AssertIsolation()
        {
            foreach (var component in actor.GetComponentsInChildren<Component>(true))
                if (!(component is Transform) && component != renderer && component != referenceAnimator)
                    throw new InvalidOperationException("An unexpected component entered the isolated reference: " + component.GetType().Name);
            if (renderer.enabled || renderer.sharedMesh != source || renderer.bones.Length != 49)
                throw new InvalidOperationException("Isolated reference was changed or made visible.");
        }

        private void SelectBootVertices(Transform[] bones, out Foot leftFoot, out Foot rightFoot)
        {
            int li = BoneIndex(bones, "foot.L"), ri = BoneIndex(bones, "foot.R");
            var l = new List<int>(); var r = new List<int>(); float lw = 1, rw = 1;
            // Same dominant selection and ambiguity threshold as the live probe.
            // These are mesh-owned Allocator.None NativeArrays: do not dispose.
            var counts = source.GetBonesPerVertex(); var weights = source.GetAllBoneWeights();
            if (counts.Length != source.vertexCount) throw new InvalidOperationException("Incomplete vertex skin counts.");
            int cursor = 0;
            for (int vertex = 0; vertex < counts.Length; vertex++)
            {
                int dominant = -1; float maximum = -1, runnerUp = -1;
                for (int j = 0; j < counts[vertex]; j++)
                {
                    if (cursor >= weights.Length) throw new InvalidOperationException("Truncated source skin weights.");
                    var weight = weights[cursor++];
                    if (!Finite(weight.weight) || weight.weight < 0 || weight.boneIndex < 0 || weight.boneIndex >= bones.Length)
                        throw new InvalidOperationException("Invalid source skin weight.");
                    if (weight.weight > maximum) { runnerUp = maximum; maximum = weight.weight; dominant = weight.boneIndex; }
                    else runnerUp = Mathf.Max(runnerUp, weight.weight);
                }
                if (dominant != li && dominant != ri) continue;
                if (maximum <= 0 || Mathf.Abs(maximum - runnerUp) < .000001f)
                    throw new InvalidOperationException("Ambiguous dominant foot weight.");
                if (dominant == li) { l.Add(vertex); lw = Mathf.Min(lw, maximum); }
                else { r.Add(vertex); rw = Mathf.Min(rw, maximum); }
            }
            if (cursor != weights.Length || l.Count < 3 || r.Count < 3)
                throw new InvalidOperationException("Invalid complete original boot selection.");
            leftFoot = new Foot(l, lw); rightFoot = new Foot(r, rw);
        }

        // Unordered pairs are supported; no preceding reference measurement is
        // assumed. Caller only uses actual.hasPreviousSameClip (never loop wrap).
        public Sample CapturePair(AnimationClip clip, ArenaMotionClip motion, int sequenceIndex,
            float fromSourceTime, float toSourceTime, string sourceSha256, string fbxSha256)
        {
            long started = Stopwatch.GetTimestamp();
            var result = new Sample { sequenceIndex = sequenceIndex, fromSourceTime = fromSourceTime, toSourceTime = toSourceTime,
                sourceSha256 = sourceSha256, fbxSha256 = fbxSha256, clipId = motion?.id ?? "", clipName = clip != null ? clip.name : "" };
            try
            {
                if (disposed) throw new ObjectDisposedException(nameof(ArenaDashSourceSoleReference));
                if (!Requested) throw new InvalidOperationException("QA opt-in no longer valid.");
                if (live == null || live.sharedMesh != source) throw new InvalidOperationException("Live source mesh changed since reference construction.");
                if (clip == null || clip.humanMotion || motion == null || string.IsNullOrEmpty(motion.id)
                    || clip.name != motion.clip || Mathf.Abs(clip.length - motion.duration) > .002f)
                    throw new InvalidOperationException("Expected the exact verified Generic clip and matching source trajectory.");
                if (!HashText(sourceSha256) || !HashText(fbxSha256)) throw new InvalidOperationException("Caller must supply the verified source and FBX SHA256 identities.");
                if (sequenceIndex < 0 || !Finite(fromSourceTime) || !Finite(toSourceTime)
                    || fromSourceTime < 0 || toSourceTime <= fromSourceTime || toSourceTime > motion.duration + .00001f)
                    throw new InvalidOperationException("Invalid same-clip endpoint times; wrapped/cross-clip pairs must be omitted explicitly.");
                AssertIsolation();
                result.clipInstanceId = clip.GetInstanceID(); result.originalMeshInstanceId = source.GetInstanceID();
                result.liveRendererInstanceId = live.GetInstanceID(); result.referenceRendererInstanceId = renderer.GetInstanceID();
                result.vertexCount = source.vertexCount; result.boneCount = renderer.bones.Length; result.shapeCount = source.blendShapeCount; result.clipLength = clip.length;
                result.referenceVisualLocalPosition = initial[0].position; result.referenceVisualLocalRotation = initial[0].rotation; result.referenceVisualLocalScale = initial[0].scale;
                SampleEndpoint(clip, motion, fromSourceTime, left.from, right.from, out result.fromRoot, out result.fromYaw);
                SampleEndpoint(clip, motion, toSourceTime, left.to, right.to, out result.toRoot, out result.toYaw);
                result.left = MeasureEndpoints(left.from, left.to, left.minimumWeight);
                result.right = MeasureEndpoints(right.from, right.to, right.minimumWeight);
                result.reliable = true;
            }
            catch (Exception exception) { result.reason = exception.GetType().Name + ": " + exception.Message; }
            result.sampleCostMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            return result;
        }

        private void SampleEndpoint(AnimationClip clip, ArenaMotionClip motion, float time,
            Vector3[] leftPositions, Vector3[] rightPositions, out Vector3 root, out float yaw)
        {
            foreach (var saved in initial) saved.Restore();
            for (int i = 0; i < source.blendShapeCount; i++) renderer.SetBlendShapeWeight(i, 0);
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            actor.transform.localScale = Vector3.one;
            // The prior SampleAnimation-only tree did not evaluate all imported
            // Euler rotation bindings. A separate single-clip graph samples the
            // actual imported animation; raw FBX FK remains a distinct check.
            if(sampledClip!=clip)
            {
                if(referenceClip.IsValid())referenceGraph.DestroyPlayable(referenceClip);
                referenceClip=AnimationClipPlayable.Create(referenceGraph,clip);referenceClip.SetApplyFootIK(false);referenceClip.SetApplyPlayableIK(false);
                referenceClip.SetSpeed(0);referenceOutput.SetSourcePlayable(referenceClip);sampledClip=clip;
            }
            referenceClip.SetTime(time);referenceGraph.Evaluate(0);
            motion.Sample(time, out root, out yaw);
            actor.transform.SetPositionAndRotation(root, Quaternion.Euler(0, yaw, 0));
            renderer.BakeMesh(baked, false); baked.GetVertices(vertices);
            if (vertices.Count != source.vertexCount) throw new InvalidOperationException("Source BakeMesh topology changed.");
            var world = renderer.localToWorldMatrix;
            for (int i = 0; i < left.indices.Length; i++) leftPositions[i] = world.MultiplyPoint3x4(vertices[left.indices[i]]);
            for (int i = 0; i < right.indices.Length; i++) rightPositions[i] = world.MultiplyPoint3x4(vertices[right.indices[i]]);
        }

        // Same common-lowest-vertices then current-patch fallback as the actual
        // ArenaFoundationSoleProbe. Each array uses identical original vertex IDs.
        public static FootPair MeasureEndpoints(Vector3[] from, Vector3[] to, float minimumDominantWeight = 1)
        {
            if (from == null || to == null || from.Length != to.Length || from.Length < 3)
                throw new ArgumentException("Endpoint arrays need the same ordered original foot vertices.");
            var result = new FootPair { candidateCount = from.Length, minimumDominantWeight = minimumDominantWeight,
                fromMinimumY = float.PositiveInfinity, toMinimumY = float.PositiveInfinity };
            for (int i = 0; i < from.Length; i++)
            {
                if (!Finite(from[i]) || !Finite(to[i])) throw new ArgumentException("Non-finite source boot vertex.");
                if (from[i].y < result.fromMinimumY) { result.fromMinimumY = from[i].y; result.fromMinimumPoint = from[i]; }
                if (to[i].y < result.toMinimumY) { result.toMinimumY = to[i].y; result.toMinimumPoint = to[i]; }
            }
            var distances = new List<float>(from.Length);
            for (int i = 0; i < from.Length; i++)
            {
                bool lowFrom = from[i].y <= result.fromMinimumY + PatchDepth;
                bool lowTo = to[i].y <= result.toMinimumY + PatchDepth;
                if (lowFrom) { result.fromPatchCount++; result.fromPatchCentre += from[i]; }
                if (lowTo) { result.toPatchCount++; result.toPatchCentre += to[i]; }
                if (lowFrom && lowTo) distances.Add(PlanarDistance(from[i], to[i]));
            }
            result.fromPatchCentre /= result.fromPatchCount; result.toPatchCentre /= result.toPatchCount;
            result.commonPatchCount = distances.Count; result.contactPairFallback = distances.Count == 0;
            if (result.contactPairFallback)
                for (int i = 0; i < from.Length; i++)
                    if (to[i].y <= result.toMinimumY + PatchDepth) distances.Add(PlanarDistance(from[i], to[i]));
            if (distances.Count == 0) throw new InvalidOperationException("Empty current boot patch.");
            distances.Sort(); result.measuredPairCount = distances.Count;
            int middle = distances.Count / 2;
            result.medianPatchTravelM = distances.Count % 2 == 1 ? distances[middle] : (distances[middle - 1] + distances[middle]) * .5f;
            result.maximumPatchTravelM = distances[distances.Count - 1];
            return result;
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        { float x = a.x - b.x, z = a.z - b.z; return Mathf.Sqrt(x * x + z * z); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool HashText(string text)
        {
            if (text == null || text.Length != 64) return false;
            foreach (char c in text) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }
        private static int BoneIndex(Transform[] bones, string name)
        {
            int found = -1;
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == name)
            { if (found >= 0) throw new InvalidOperationException("Duplicate foot bone name."); found = i; }
            if (found < 0) throw new InvalidOperationException("Missing original foot bone: " + name);
            return found;
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if(referenceGraph.IsValid())referenceGraph.Destroy();
            if (baked != null) UnityEngine.Object.DestroyImmediate(baked);
            if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
            // Shared source Mesh, AnimationClip and materials are never destroyed.
        }
    }
}
