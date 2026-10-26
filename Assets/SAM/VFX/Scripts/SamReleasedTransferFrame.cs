using UnityEngine;

namespace SamProduction
{
    /// <summary>
    /// Per-release geometry state. The deterministic pose caller supplies the near point;
    /// neither rendering nor diagnostics reads a socket, clock, random generator or pool.
    /// </summary>
    internal sealed class SamReleasedTransferFrame
    {
        internal struct Frame
        {
            internal Vector3 nearLocal, farLocal, rightLocal, upLocal;
            internal float lengthLocal;

            internal Vector3 Point(float along, float across, float height)
            {
                return Vector3.LerpUnclamped(nearLocal, farLocal, along)
                    + rightLocal * across + upLocal * height;
            }
        }

        Transform releasedRoot;
        Vector3 fixedFarWorld, lastNearWorld;
        bool initialized, nearFrozen;

        internal bool Reset(Transform root, Vector3 actualHandWorld, Vector3 releasedCenterWorld)
        {
            Clear();
            if (root == null || !Finite(actualHandWorld) || !Finite(releasedCenterWorld)) return false;
            releasedRoot = root;
            lastNearWorld = actualHandWorld;
            fixedFarWorld = releasedCenterWorld;
            initialized = true;
            return true;
        }

        internal void Sample(Vector3 currentHandWorld, bool releasing)
        {
            if (!initialized) return;
            // Cancel may have recovered the animation already: freeze BEFORE accepting a new pose.
            if (releasing) Freeze();
            if (!nearFrozen && Finite(currentHandWorld)) lastNearWorld = currentHandWorld;
        }

        internal void Freeze() { if (initialized) nearFrozen = true; }

        internal bool TryGetEndpoints(out Vector3 nearWorld, out Vector3 farWorld, out bool frozen)
        {
            nearWorld = lastNearWorld;
            farWorld = fixedFarWorld;
            frozen = nearFrozen;
            return initialized && releasedRoot != null;
        }

        internal bool TryGetFrame(out Frame frame)
        {
            frame = default;
            if (!initialized || releasedRoot == null) return false;
            Vector3 near = releasedRoot.InverseTransformPoint(lastNearWorld);
            Vector3 far = releasedRoot.InverseTransformPoint(fixedFarWorld);
            Vector3 axis = far - near;
            float length = axis.magnitude;
            if (!Finite(near) || !Finite(far) || float.IsInfinity(length) || length < .0001f) return false;
            axis /= length;
            Vector3 up = releasedRoot.InverseTransformDirection(Vector3.up).normalized;
            if (Mathf.Abs(Vector3.Dot(axis, up)) > .98f)
                up = releasedRoot.InverseTransformDirection(Vector3.right).normalized;
            Vector3 right = Vector3.Cross(up, axis).normalized;
            up = Vector3.Cross(axis, right).normalized;
            if (!Finite(right) || !Finite(up) || right.sqrMagnitude < .5f || up.sqrMagnitude < .5f) return false;
            frame = new Frame { nearLocal = near, farLocal = far, rightLocal = right, upLocal = up, lengthLocal = length };
            return true;
        }

        internal void Clear()
        {
            releasedRoot = null;
            fixedFarWorld = lastNearWorld = Vector3.zero;
            initialized = nearFrozen = false;
        }

        static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
