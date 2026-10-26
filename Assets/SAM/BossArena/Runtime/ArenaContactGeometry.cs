using UnityEngine;

namespace SamProduction.BossArena
{
    // Narrow-phase validation uses the controller's full body capsule. Movement skin
    // belongs to the motor and does not extend weapon damage beyond the visible body.
    internal static class ArenaContactGeometry
    {
        public static bool Intersects(Vector3 a, Vector3 b, float radius, CharacterController body)
        {
            Vector3 scale = body.transform.lossyScale;
            float bodyRadius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfSegment = Mathf.Max(0, body.height * Mathf.Abs(scale.y) * .5f - bodyRadius);
            Vector3 centre = body.transform.TransformPoint(body.center);
            Vector3 offset = body.transform.up * halfSegment;
            float combinedRadius = radius + bodyRadius;
            return SegmentDistanceSquared(a, b, centre - offset, centre + offset)
                <= combinedRadius * combinedRadius;
        }

        private static float SegmentDistanceSquared(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
        {
            Vector3 u = q - p, v = s - r, w = p - r;
            float uu = Vector3.Dot(u, u), vv = Vector3.Dot(v, v);
            float vw = Vector3.Dot(v, w), t, z;
            const float epsilon = 1e-10f;
            if (uu <= epsilon && vv <= epsilon) return w.sqrMagnitude;
            if (uu <= epsilon) { t = 0; z = Mathf.Clamp01(vw / vv); }
            else
            {
                float uw = Vector3.Dot(u, w);
                if (vv <= epsilon) { z = 0; t = Mathf.Clamp01(-uw / uu); }
                else
                {
                    float uv = Vector3.Dot(u, v), denominator = uu * vv - uv * uv;
                    t = denominator > epsilon ? Mathf.Clamp01((uv * vw - uw * vv) / denominator) : 0;
                    z = (uv * t + vw) / vv;
                    if (z < 0) { z = 0; t = Mathf.Clamp01(-uw / uu); }
                    else if (z > 1) { z = 1; t = Mathf.Clamp01((uv - uw) / uu); }
                }
            }
            return (p + u * t - r - v * z).sqrMagnitude;
        }
    }
}
