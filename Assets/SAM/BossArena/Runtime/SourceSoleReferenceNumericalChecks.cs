using System;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Explicit numerical examples for the shared endpoint kernel. Calling this
    // does not prove SampleAnimation/BakeMesh, live mesh isolation or game quality.
    public static class SourceSoleReferenceNumericalChecks
    {
        public static int Run()
        {
            var a = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0) };
            var b = Shift(a, new Vector3(.01f, 0, 0));
            var q = ArenaDashSourceSoleReference.MeasureEndpoints(a, b);
            Need(Mathf.Abs(q.medianPatchTravelM - .01f) < .000001f && !q.contactPairFallback && q.commonPatchCount == 3, "Rigid translation");

            var lowFrom = new[] { new Vector3(0, 0, 0), new Vector3(1, .02f, 0), new Vector3(2, .04f, 0) };
            var lowTo = new[] { new Vector3(.5f, .02f, 0), new Vector3(1.03f, 0, 0), new Vector3(2, .04f, 0) };
            q = ArenaDashSourceSoleReference.MeasureEndpoints(lowFrom, lowTo);
            Need(q.contactPairFallback && q.commonPatchCount == 0 && q.measuredPairCount == 1 && Mathf.Abs(q.medianPatchTravelM - .03f) < .000001f, "Empty intersection uses current patch");

            var c = Shift(a, new Vector3(.03f, 0, 0));
            float pathSum = ArenaDashSourceSoleReference.MeasureEndpoints(a, c).medianPatchTravelM
                + ArenaDashSourceSoleReference.MeasureEndpoints(c, a).medianPatchTravelM;
            Need(pathSum > .059f && ArenaDashSourceSoleReference.MeasureEndpoints(a, a).medianPatchTravelM == 0, "Endpoint displacement differs from path sum");

            var fourA = new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
            var fourB = new[] { Vector3.zero, new Vector3(.002f, 0, 0), new Vector3(.004f, 0, 0), new Vector3(.100f, 0, 0) };
            q = ArenaDashSourceSoleReference.MeasureEndpoints(fourA, fourB);
            Need(Mathf.Abs(q.medianPatchTravelM - .003f) < .000001f && Mathf.Abs(q.maximumPatchTravelM - .1f) < .000001f, "Even median rejects maximum as typical travel");

            q = ArenaDashSourceSoleReference.MeasureEndpoints(a, Shift(a, Vector3.up * .1f));
            Need(q.medianPatchTravelM == 0 && Mathf.Abs(q.toMinimumY - .1f) < .000001f, "Height is recorded separately from planar travel");

            bool rejected = false;
            try { ArenaDashSourceSoleReference.MeasureEndpoints(a, new[] { new Vector3(float.NaN, 0, 0), Vector3.zero, Vector3.zero }); }
            catch (ArgumentException) { rejected = true; }
            Need(rejected, "Nonfinite vertex rejection");
            return 6;
        }

        private static Vector3[] Shift(Vector3[] source, Vector3 delta)
        { var result = new Vector3[source.Length]; for (int i = 0; i < source.Length; i++) result[i] = source[i] + delta; return result; }
        private static void Need(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("Source sole numerical check failed: " + label); }
    }
}
