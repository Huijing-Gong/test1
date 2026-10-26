using System;
using UnityEngine;

namespace SamProduction.BossArena
{
    /// <summary>
    /// Passive audit of the EXISTING pose bridge, not a second animation writer.
    /// Bind once on an unmodified source pose, before ArenaDashPoseResidual.Apply.
    /// BeginTransition before each charge/slide/lock/recover/abort transition.
    /// ObserveAfterPose after the existing motor has finished graph evaluation,
    /// residual correction and CharacterController motion. Pass its unwrapped
    /// phase clock, the motor's independently computed collision-resolved root
    /// position and actual sole contact samples. Supported means a stationary
    /// source support interval, not merely grounded. Phase changes retain anchors
    /// so a planted-foot jump at a seam is not hidden by a diagnostics reset.
    /// Wrapped clip time and socket height alone are not contact evidence.
    /// No Update, graph, Transform writes, events, damage or simulation clock.
    /// Turning Enabled off/ResetDiagnostics releases all cached contact anchors.
    /// A fault stops observation until ResetDiagnostics or a successful Bind.
    /// This intentionally does not add a rotation blend over the existing residual:
    /// that could move the fist used by the unchanged strike collision window.
    /// </summary>
    public sealed class ArenaBossPoseBridge
    {
        public enum AuditState { Disabled, Unbound, Ready, Observing, Faulted }
        [Flags]
        public enum Finding { None = 0, BoneLength = 1, BoneScale = 2, Root = 4, Contact = 8, NonUniformActorScale = 16, BoneOffset = 32 }

        public struct Report
        {
            public Finding findings;
            public int samples;
            public float clock, maximumBoneLengthError, maximumBoneScaleError;
            public float maximumRootError, maximumContactDrift, maximumAngularSpeedDegrees;
            public float maximumBoneOffsetError, maximumRotationStepDegrees;
            public string worstBone;
        }

        // These are diagnostic trial tolerances, not gameplay acceptance claims.
        public float BoneLengthTolerance = .0005f;
        public float BoneScaleTolerance = .0001f;
        public float RootTolerance = .001f;
        public float ContactTolerance = .002f;
        public AuditState State { get; private set; } = AuditState.Disabled;
        public string Error { get; private set; } = "";
        public string Transition { get; private set; } = "";
        public Report CurrentReport => report;

        // Only forearm/hand and shin/foot rigid segments: root/pelvis and authored
        // shoulder/skirt offsets are not incorrectly treated as fixed translations.
        private static readonly string[] Names = { "forearm.L", "hand.L", "forearm.R", "hand.R", "shin.L", "foot.L", "shin.R", "foot.R" };
        private static readonly string[] Parents = { "upper_arm.L", "forearm.L", "upper_arm.R", "forearm.R", "thigh.L", "shin.L", "thigh.R", "shin.R" };
        private Transform actor;
        private SkinnedMeshRenderer mesh;
        private Mesh boundMesh;
        private Transform[] rigid;
        private Transform[] boundParents;
        private Transform[] boundSkeleton;
        private float[] lengths;
        private Vector3[] scales;
        private Vector3[] offsets;
        private Quaternion[] previousRotations;
        private Vector3 leftAnchor, rightAnchor;
        private bool enabled, leftPlanted, rightPlanted, hasPose;
        private Report report;

        public bool Enabled
        {
            get => enabled;
            set { enabled = value; ResetDiagnostics(); }
        }

        public bool Bind(SkinnedMeshRenderer renderer, Transform actorRoot)
        {
            mesh = null; actor = null; rigid = null; boundSkeleton = null;
            ResetDiagnostics();
            if (renderer == null || renderer.sharedMesh == null || actorRoot == null) return Fail("Missing renderer, mesh or actor root.");
            var bones = renderer.bones;
            var candidates = new Transform[Names.Length];
            var candidateLengths = new float[Names.Length];
            var candidateScales = new Vector3[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                int matches = 0;
                foreach (var bone in bones)
                    if (bone != null && bone.name == Names[i]) { candidates[i] = bone; matches++; }
                var value = candidates[i];
                if (matches != 1 || value.parent == null || value.parent.name != Parents[i] || !value.IsChildOf(actorRoot))
                    return Fail("Missing, duplicate or incompatible rigid chain: " + Names[i]);
                if (!Finite(value.localPosition) || !Finite(value.localScale) || value.localPosition.sqrMagnitude < 1e-12f)
                    return Fail("Invalid source rigid segment: " + Names[i]);
                candidateLengths[i] = value.localPosition.magnitude;
                candidateScales[i] = value.localScale;
            }
            mesh = renderer; actor = actorRoot; rigid = candidates;
            boundSkeleton = bones; lengths = candidateLengths; scales = candidateScales;
            boundMesh = renderer.sharedMesh; boundParents = new Transform[Names.Length];
            previousRotations = new Quaternion[Names.Length]; offsets = new Vector3[Names.Length];
            for (int i = 0; i < rigid.Length; i++) { boundParents[i] = rigid[i].parent; offsets[i] = rigid[i].localPosition; }
            ResetDiagnostics();
            return SelfCheck(out string issue) || Fail(issue);
        }

        public bool BeginTransition(string label, float unwrappedPhaseClock = 0)
        {
            if (!enabled) return false;
            if (State == AuditState.Faulted) return false;
            if (!SelfCheck(out string issue)) return Fail(issue);
            if (string.IsNullOrEmpty(label) || !Finite(unwrappedPhaseClock) || unwrappedPhaseClock < 0)
                return Fail("Invalid transition label or clock.");
            report = default; Transition = label; report.clock = unwrappedPhaseClock;
            State = AuditState.Observing;
            return true;
        }

        public bool ObserveAfterPose(float unwrappedPhaseClock, float sampleDelta,
            Vector3 expectedRootWorld, bool leftSupported, Vector3 leftSoleWorld,
            bool rightSupported, Vector3 rightSoleWorld)
        {
            if (!enabled || State != AuditState.Observing) return false;
            // Full SelfCheck is reserved for binding and transitions: mesh.bones
            // returns an array. This hot path neither allocates that array nor bakes.
            if (mesh == null || actor == null || rigid == null || mesh.sharedMesh != boundMesh)
                return Fail("Binding changed; rebind on a source pose.");
            if (!ValidTolerances()) return Fail("Invalid diagnostic tolerances.");
            if (!Finite(unwrappedPhaseClock) || !Finite(sampleDelta) || sampleDelta < 0 || unwrappedPhaseClock < report.clock
                || !Finite(expectedRootWorld) || !Finite(actor.position)
                || (leftSupported && !Finite(leftSoleWorld)) || (rightSupported && !Finite(rightSoleWorld)))
                return Fail("Invalid sample or clock rewind; begin a new transition for a phase reset.");
            for (int i = 0; i < rigid.Length; i++)
            {
                if (rigid[i] == null || rigid[i].parent != boundParents[i]) return Fail("Rigid hierarchy changed: " + Names[i]);
                var rotation = rigid[i].localRotation;
                if (!Finite(rigid[i].localPosition) || !Finite(rigid[i].localScale) || !Finite(rotation)) return Fail("Non-finite rigid pose: " + Names[i]);
                if (hasPose)
                {
                    float step = Quaternion.Angle(previousRotations[i], rotation);
                    report.maximumRotationStepDegrees = Mathf.Max(report.maximumRotationStepDegrees, step);
                    if (sampleDelta > .000001f) report.maximumAngularSpeedDegrees = Mathf.Max(report.maximumAngularSpeedDegrees, step / sampleDelta);
                }
                previousRotations[i] = rotation;
                float lengthError = Mathf.Abs(rigid[i].localPosition.magnitude - lengths[i]);
                float offsetError = Vector3.Distance(rigid[i].localPosition, offsets[i]);
                float scaleError = (rigid[i].localScale - scales[i]).magnitude;
                if (lengthError > report.maximumBoneLengthError) { report.maximumBoneLengthError = lengthError; report.worstBone = Names[i]; }
                report.maximumBoneScaleError = Mathf.Max(report.maximumBoneScaleError, scaleError);
                report.maximumBoneOffsetError = Mathf.Max(report.maximumBoneOffsetError, offsetError);
                if (lengthError > BoneLengthTolerance) report.findings |= Finding.BoneLength;
                if (offsetError > BoneLengthTolerance) report.findings |= Finding.BoneOffset;
                if (scaleError > BoneScaleTolerance) report.findings |= Finding.BoneScale;
            }
            float rootError = Vector3.Distance(actor.position, expectedRootWorld);
            report.maximumRootError = Mathf.Max(report.maximumRootError, rootError);
            if (rootError > RootTolerance) report.findings |= Finding.Root;
            TrackContact(leftSupported, leftSoleWorld, ref leftPlanted, ref leftAnchor);
            TrackContact(rightSupported, rightSoleWorld, ref rightPlanted, ref rightAnchor);
            Vector3 rootScale = actor.lossyScale;
            if (!Finite(rootScale)) return Fail("Non-finite actor scale.");
            if (Mathf.Abs(rootScale.x - rootScale.y) > BoneScaleTolerance || Mathf.Abs(rootScale.y - rootScale.z) > BoneScaleTolerance)
                report.findings |= Finding.NonUniformActorScale;
            report.clock = unwrappedPhaseClock; report.samples++; hasPose = true;
            return true; // Observation succeeded; inspect findings separately.
        }

        public bool SelfCheck(out string issue)
        {
            issue = "";
            if (mesh == null || actor == null || rigid == null) issue = "Not bound to a source skeleton.";
            else if (!ValidTolerances())
                issue = "Invalid diagnostic tolerances.";
            else
            {
                // This method is a deliberate bind/transition check, not an extra
                // mesh bake or graph evaluation. Rebind after model replacement.
                var current = mesh.bones;
                if (mesh.sharedMesh != boundMesh || current.Length != boundSkeleton.Length) issue = "Skeleton changed; rebind on a source pose.";
                else for (int i = 0; i < current.Length; i++)
                    if (current[i] != boundSkeleton[i]) { issue = "Skeleton changed; rebind on a source pose."; break; }
                if (issue.Length == 0) for (int i = 0; i < rigid.Length; i++)
                    if (rigid[i] == null || rigid[i].parent == null || rigid[i].parent.name != Parents[i] || !rigid[i].IsChildOf(actor))
                    { issue = "Rigid hierarchy changed: " + Names[i]; break; }
            }
            return issue.Length == 0;
        }

        public void ResetDiagnostics()
        {
            leftPlanted = rightPlanted = hasPose = false; leftAnchor = rightAnchor = Vector3.zero;
            report = default; Transition = Error = "";
            State = !enabled ? AuditState.Disabled : rigid == null ? AuditState.Unbound : AuditState.Ready;
        }

        public void Release()
        {
            enabled = false; mesh = null; boundMesh = null; actor = null; rigid = null; boundSkeleton = null;
            boundParents = null; previousRotations = null; lengths = null; scales = null; offsets = null; ResetDiagnostics();
        }

        private void TrackContact(bool supported, Vector3 point, ref bool planted, ref Vector3 anchor)
        {
            if (!supported) { planted = false; return; }
            if (!planted) { anchor = point; planted = true; return; }
            float drift = Vector3.Distance(anchor, point);
            report.maximumContactDrift = Mathf.Max(report.maximumContactDrift, drift);
            if (drift > ContactTolerance) report.findings |= Finding.Contact;
        }

        private bool Fail(string issue)
        {
            Error = issue; State = AuditState.Faulted; leftPlanted = rightPlanted = false;
            return false;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        private bool ValidTolerances() => Finite(BoneLengthTolerance) && BoneLengthTolerance >= 0 && Finite(BoneScaleTolerance) && BoneScaleTolerance >= 0
            && Finite(RootTolerance) && RootTolerance >= 0 && Finite(ContactTolerance) && ContactTolerance >= 0;
    }
}
