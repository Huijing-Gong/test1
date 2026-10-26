using System;
using UnityEngine;

namespace SamProduction
{
    /// <summary>
    /// Optional passive probe for Root's actual-player harness. This file is not in the
    /// integration patch and has NOT been executed in Unity. Capture never steps the scene.
    /// </summary>
    public static class ImpactConnection12RuntimeProbe
    {
        [Serializable]
        public sealed class Snapshot
        {
            public double semanticTime;
            public int seed, active, pooled, hitCount;
            public bool present, frozen;
            public Vector3 near, far, sampledHand;
            public SamVfxDiagnosticReport effects;
        }

        public static Snapshot Capture(SamSkillRunner runner, int releaseSeed, string socketName, Camera camera)
        {
            var result = new Snapshot
            {
                semanticTime = runner.Clock.Time, seed = releaseSeed,
                active = runner.vfx.ActiveEffectCount, pooled = runner.vfx.PooledEffectCount,
                hitCount = runner.HitCount, sampledHand = runner.Socket(socketName).position,
                effects = runner.vfx.CaptureDiagnostics(camera)
            };
            result.present = runner.vfx.TryGetPunchTransferEndpoints(releaseSeed, out result.near, out result.far, out result.frozen);
            return result;
        }

        public static void RequireEndpoints(Snapshot value, Vector3 releasePosition, Vector3 releaseDirection,
            SamPunchEnergySettings settings, float visualScale, bool followsHand)
        {
            Require(value.present, "Release seed has no endpoint state at " + value.semanticTime);
            // Matches the existing released-root unit-scale convention, also when art scale != 1.
            Vector3 expected = releasePosition + (releaseDirection.normalized * settings.impactDistance
                + settings.impactWorldOffset) * Mathf.Max(.01f, visualScale);
            Require(Vector3.Distance(value.far, expected) < .00002f, "Far point differs from this release's configured mass center");
            if (followsHand)
            {
                Require(!value.frozen, "Transfer froze while skill was still active");
                Require(Vector3.Distance(value.near, value.sampledHand) < .00002f, "Near point missed the deterministic fist sample");
            }
            foreach (SamVfxEffectDiagnostic effect in value.effects.effects)
            {
                if (effect.kind != "PunchImpact" || effect.seed != value.seed) continue;
                Require(Vector3.Distance(effect.position, releasePosition) < .00002f, "Released root followed the fist");
                Require(Vector3.Distance(effect.direction, releaseDirection.normalized) < .00002f, "Released root direction changed");
                foreach (SamVfxRendererDiagnostic renderer in effect.renderers)
                    if (renderer.name == "Energy / short fist transfer" && renderer.enabled)
                    {
                        Require(renderer.nonFiniteVertices == 0, "Transfer contains non-finite geometry");
                        Require(renderer.minVertexAlpha == 0, "Transfer ends do not have zero vertex alpha");
                    }
                return;
            }
            throw new InvalidOperationException("Active transfer has no matching released effect diagnostic");
        }

        public static void RequirePauseUnchanged(Snapshot before, Snapshot after)
        {
            Require(before.semanticTime == after.semanticTime, "Paused semantic clock advanced");
            Require(before.present == after.present && before.frozen == after.frozen
                && before.near.Equals(after.near) && before.far.Equals(after.far), "Repeated sample/review moved transfer endpoints");
            Require(before.active == after.active && before.pooled == after.pooled, "Pause changed effect pool counts");
            Require(before.hitCount == after.hitCount, "Pause changed hit count");
            Require(before.effects.effects.Count == after.effects.effects.Count, "Pause changed active effect diagnostics");
            for (int i = 0; i < before.effects.effects.Count; ++i)
            {
                var a = before.effects.effects[i]; var b = after.effects.effects[i];
                Require(a.kind == b.kind && a.seed == b.seed && a.age == b.age, "Pause changed effect identity, seed or age");
                Require(a.position.Equals(b.position) && a.direction.Equals(b.direction), "Pause moved an effect root");
                Require(a.renderers.Count == b.renderers.Count, "Pause changed renderer pool");
                for (int j = 0; j < a.renderers.Count; ++j)
                {
                    var x = a.renderers[j]; var y = b.renderers[j];
                    Require(x.name == y.name && x.localTime == y.localTime && x.particleCount == y.particleCount,
                        "Pause changed particle count or shader age: " + x.name);
                    Require(x.boundsCenter.Equals(y.boundsCenter) && x.boundsSize.Equals(y.boundsSize),
                        "Repeated diagnostics changed geometry bounds: " + x.name);
                }
            }
        }

        public static void RequireSoftCancelFrozen(Snapshot beforeRecovery, Snapshot afterCancel)
        {
            Require(beforeRecovery.present && afterCancel.present && afterCancel.frozen, "Soft cancel lost/fails to freeze endpoint state");
            Require(beforeRecovery.near.Equals(afterCancel.near), "Soft cancel accepted the recovered hand pose");
            Require(beforeRecovery.far.Equals(afterCancel.far), "Soft cancel moved the released mass endpoint");
        }

        public static void RequireImmediateClear(Snapshot afterCancel)
        {
            Require(!afterCancel.present && afterCancel.active == 0 && afterCancel.effects.effects.Count == 0,
                "Immediate cancel retained an active effect or previous release endpoint state");
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("ImpactConnection12: " + message);
        }
    }
}
