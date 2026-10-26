using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction.BossArena
{
    // Presentation only. The arena supplies resolved strikes, geometry and elapsed
    // time. This component never queries collisions, applies damage or owns input.
    public sealed class ArenaDefenseFeedback : MonoBehaviour
    {
        public const string TypeDodge = "dodge";
        public const string TypePerfectDodge = "perfect_dodge";
        public const string TypeBlock = "block";
        public const string TypePerfectBlock = "perfect_block";
        public Material warningMaterial, attackMaterial, defenseMaterial;
        // Core may bind the raised weapon/forearm transform. This never guesses a
        // rig hierarchy. Local offset is in anchor space; fallback is in world space.
        public Transform blockContactAnchor;
        public Vector3 blockContactLocalOffset = new Vector3(0, 0, .15f);
        public Vector3 blockContactFallbackOffset = new Vector3(0, .60f, 0);
        public float blockContactOutset = .12f;
        private const int PoolSize = 6, RingSegments = 32, WarningSegments = 64, HistorySize = 64;
        private const float WarningRefreshLifetime = .25f;
        private enum Kind { Dodge, PerfectDodge, Block, PerfectBlock, AttackImpact, FootImpact, Cancelled }
        private sealed class Burst
        {
            public GameObject root;
            public LineRenderer ring, precisionArc;
            public readonly LineRenderer[] rays = new LineRenderer[4];
            public long strike;
            public Kind kind;
            public Vector3 point, right, up;
            public float radius, age, lifetime;
            public bool active;
        }
        private readonly Burst[] bursts = new Burst[PoolSize];
        private readonly long[] recentStrikes = new long[HistorySize];
        private readonly Kind[] recentKinds = new Kind[HistorySize];
        private GameObject ownedRoot;
        private LineRenderer warning;
        private LineRenderer counterBlade;
        public bool CounterBladeVisible => counterBlade != null && counterBlade.enabled;
        private long warningStrike;
        private float warningRemaining;
        private int historyCount, historyNext;

        public bool WarningVisible => warning != null && warning.enabled;
        public int ActiveFeedbackCount
        {
            get { int count = 0; foreach (var burst in bursts) if (burst != null && burst.active) ++count; return count; }
        }

        // References remain shared; existing materials are never edited or destroyed.
        public void UseMaterials(ArenaCombatFx source)
        {
            ResetFeedback();
            warningMaterial = source != null ? source.warning : null;
            attackMaterial = source != null ? source.orange : null;
            defenseMaterial = source != null ? source.cyan : null;
        }

        // Call after UseMaterials during the menu/loading period. Creates the whole
        // fixed pool and line buffers, including the precise-block arc, without a cue.
        public bool Warmup() => Prepare();

        // Cosmetic blade highlight only: caller supplies the real posed blade.
        // It is not a contact, attack boundary, extra iframe, or enemy stagger cue.
        public void SetCounterBlade(bool visible, Vector3 bladeBase, Vector3 bladeTip)
        {
            if (!visible || !Finite(bladeBase) || !Finite(bladeTip) ||
                (bladeTip - bladeBase).sqrMagnitude < .0001f)
            { if (counterBlade != null) counterBlade.enabled = false; return; }
            if (!Prepare()) return;
            counterBlade.sharedMaterial = defenseMaterial;
            counterBlade.SetPosition(0, bladeBase); counterBlade.SetPosition(1, bladeTip);
            counterBlade.enabled = true;
        }

        // Use the four Type* constants above. No guard-break outcome is invented.
        // Call after combat resolves the outcome; unknown types return false.
        // Duplicate (strike, type) events are ignored.
        public bool ShowDefense(long strike, string type, Vector3 point, Vector3 direction)
        {
            Kind kind;
            if (string.Equals(type, TypeDodge, StringComparison.OrdinalIgnoreCase)) kind = Kind.Dodge;
            else if (string.Equals(type, TypePerfectDodge, StringComparison.OrdinalIgnoreCase)) kind = Kind.PerfectDodge;
            else if (string.Equals(type, TypeBlock, StringComparison.OrdinalIgnoreCase)) kind = Kind.Block;
            else if (string.Equals(type, TypePerfectBlock, StringComparison.OrdinalIgnoreCase)) kind = Kind.PerfectBlock;
            else return false;
            if (!Valid(strike, point, direction) || Seen(strike, Kind.Cancelled) || Seen(strike, kind) || !Prepare()) return false;
            if ((kind == Kind.Dodge && Seen(strike, Kind.PerfectDodge)) ||
                (kind == Kind.Block && Seen(strike, Kind.PerfectBlock))) return false;
            Vector3 normal = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude < .0001f) right = Vector3.Cross(normal, Vector3.right);
            right.Normalize();
            if (kind == Kind.Block || kind == Kind.PerfectBlock)
            {
                if (!Finite(blockContactLocalOffset) || !Finite(blockContactFallbackOffset) || !Finite(blockContactOutset)) return false;
                // Hit.direction points from danger to player; push the contact cue
                // slightly towards danger so it is not buried in the blocking arm.
                point = (blockContactAnchor != null ? blockContactAnchor.TransformPoint(blockContactLocalOffset)
                    : point + blockContactFallbackOffset) + normal * -Mathf.Clamp(blockContactOutset, 0, .35f);
                if (!Finite(point)) return false;
            }
            if (kind == Kind.PerfectDodge || kind == Kind.PerfectBlock)
                foreach (var old in bursts)
                    if (old.active && old.strike == strike && old.kind == (kind == Kind.PerfectDodge ? Kind.Dodge : Kind.Block)) Release(old);
            Remember(strike, kind);
            Begin(strike, kind, point, right, Vector3.Cross(right, normal).normalized,
                kind == Kind.Dodge ? .28f : kind == Kind.Block || kind == Kind.PerfectBlock ? .30f : .48f,
                kind == Kind.Dodge ? .18f : kind == Kind.Block ? .24f : .32f);
            return true;
        }

        // The caller passes the current authoritative radius. This is one thin,
        // unfilled ring, with no independent tracking or damage clock. Refresh it
        // while charging; a missed cancel cannot leave a permanent warning behind.
        public bool SetAttackWarning(long strike, Vector3 point, Vector3 direction, float radius, float progress)
        {
            if (!Valid(strike, point, direction) || !Finite(radius) || radius <= 0 || !Finite(progress) ||
                Seen(strike, Kind.Cancelled) || Seen(strike, Kind.AttackImpact) || Seen(strike, Kind.FootImpact) || !Prepare()) return false;
            warningStrike = strike;
            warningRemaining = WarningRefreshLifetime;
            warning.sharedMaterial = warningMaterial;
            warning.enabled = true;
            warning.widthMultiplier = Mathf.Lerp(.025f, .055f, Mathf.Clamp01(progress));
            DrawRing(warning, point + Vector3.up * .032f, Vector3.right, Vector3.forward, radius, Mathf.PI * 2, WarningSegments);
            return true;
        }

        // A short cosmetic contact cue at the supplied radius. It does not expand
        // a hazard. Keep the skill's existing presentation for travelling fire waves.
        public bool ShowAttackImpact(long strike, Vector3 point, Vector3 direction, float radius)
        {
            if (!Valid(strike, point, direction) || !Finite(radius) || radius < 0 || Seen(strike, Kind.Cancelled) ||
                Seen(strike, Kind.AttackImpact) || !Prepare()) return false;
            Remember(strike, Kind.AttackImpact);
            if (warningStrike == strike) HideWarning();
            Begin(strike, Kind.AttackImpact, point + Vector3.up * .035f, Vector3.right, Vector3.forward, radius, .22f);
            return true;
        }

        // Call once from a validated, non-cancelled stomp release. This .16s star
        // uses the supplied real foot contact and four existing orange strokes.
        // Its .55m cosmetic reach is unrelated to the travelling damage wave.
        public bool ShowFootImpact(long strike, Vector3 point, Vector3 direction)
        {
            if (!Valid(strike, point, direction) || Seen(strike, Kind.Cancelled) || Seen(strike, Kind.FootImpact) || !Prepare()) return false;
            Remember(strike, Kind.FootImpact);
            if (warningStrike == strike) HideWarning();
            Vector3 forward = new Vector3(direction.x, 0, direction.z);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
            forward.Normalize();
            Begin(strike, Kind.FootImpact, point + Vector3.up * .025f,
                Vector3.Cross(Vector3.up, forward).normalized, forward, .55f, .16f);
            return true;
        }

        public void CancelStrike(long strike)
        {
            if (strike <= 0) return;
            if (!Seen(strike, Kind.Cancelled)) Remember(strike, Kind.Cancelled);
            if (warningStrike == strike) HideWarning();
            foreach (var burst in bursts) if (burst != null && burst.active && burst.strike == strike) Release(burst);
        }

        // Advance once from the arena's existing presentation clock. There is no
        // Update, Time.deltaTime, timeScale assignment, or second simulation loop.
        public void Advance(float elapsed)
        {
            if (!Finite(elapsed) || elapsed < 0) return;
            if (WarningVisible)
            {
                warningRemaining -= elapsed;
                if (warningRemaining <= 0) HideWarning();
            }
            foreach (var burst in bursts)
            {
                if (burst == null || !burst.active) continue;
                burst.age += elapsed;
                if (burst.age >= burst.lifetime) Release(burst);
                else DrawBurst(burst);
            }
        }

        // Call on restart, death, or encounter cancellation. OnDisable also clears.
        public void ResetFeedback()
        {
            if (counterBlade != null) counterBlade.enabled = false;
            HideWarning();
            foreach (var burst in bursts) if (burst != null) Release(burst);
            historyCount = historyNext = 0;
        }

        private bool Prepare()
        {
            if (!isActiveAndEnabled || warningMaterial == null || attackMaterial == null || defenseMaterial == null) return false;
            if (ownedRoot != null) return true;
            ownedRoot = new GameObject("Defense feedback owned visuals");
            ownedRoot.transform.SetParent(transform, false);
            counterBlade = MakeLine(ownedRoot.transform, "Counter blade decoration", 2);
            counterBlade.sharedMaterial = defenseMaterial;
            counterBlade.widthMultiplier = .035f;
            warning = MakeLine(ownedRoot.transform, "Attack boundary", WarningSegments + 1);
            warning.sharedMaterial = warningMaterial;
            for (int i = 0; i < PoolSize; ++i)
            {
                var burst = new Burst { root = new GameObject("Feedback pool " + i) };
                burst.root.transform.SetParent(ownedRoot.transform, false);
                burst.ring = MakeLine(burst.root.transform, "Contact arc", RingSegments + 1);
                burst.precisionArc = MakeLine(burst.root.transform, "Precise block inner arc", RingSegments + 1);
                burst.ring.sharedMaterial = burst.precisionArc.sharedMaterial = defenseMaterial;
                for (int j = 0; j < burst.rays.Length; ++j) burst.rays[j] = MakeLine(burst.root.transform, "Spark " + j, 2);
                foreach (var ray in burst.rays) ray.sharedMaterial = defenseMaterial;
                burst.root.SetActive(false);
                bursts[i] = burst;
            }
            return true;
        }

        private static LineRenderer MakeLine(Transform parent, string label, int points)
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(parent, false);
            var line = obj.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = points;
            line.numCapVertices = line.numCornerVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private void Begin(long strike, Kind kind, Vector3 point, Vector3 right, Vector3 up, float radius, float lifetime)
        {
            Burst selected = bursts[0];
            foreach (var burst in bursts)
            {
                if (!burst.active) { selected = burst; break; }
                if (burst.age / burst.lifetime > selected.age / selected.lifetime) selected = burst;
            }
            selected.strike = strike; selected.kind = kind; selected.point = point;
            selected.right = right; selected.up = up; selected.radius = radius;
            selected.age = 0; selected.lifetime = lifetime; selected.active = true;
            Material material = kind == Kind.AttackImpact || kind == Kind.FootImpact ? attackMaterial : defenseMaterial;
            selected.ring.sharedMaterial = material;
            selected.precisionArc.sharedMaterial = material;
            foreach (var ray in selected.rays) ray.sharedMaterial = material;
            selected.root.SetActive(true);
            DrawBurst(selected);
        }

        private static void DrawBurst(Burst burst)
        {
            if (burst.kind == Kind.FootImpact) { DrawFootImpact(burst); return; }
            float phase = Mathf.Clamp01(burst.age / burst.lifetime);
            float fade = (1 - phase) * (1 - phase);
            bool attack = burst.kind == Kind.AttackImpact;
            bool dodge = burst.kind == Kind.Dodge;
            bool preciseBlock = burst.kind == Kind.PerfectBlock;
            float arc = dodge ? Mathf.PI * .7f : burst.kind == Kind.Block || preciseBlock ? Mathf.PI : Mathf.PI * 2;
            // Keep attack geometry at the caller's exact radius. Only local
            // defensive decoration grows slightly, never a gameplay boundary.
            float radius = burst.radius * (attack ? 1 : 1 + .12f * phase);
            burst.ring.enabled = radius > 0;
            burst.ring.widthMultiplier = (dodge ? .018f : preciseBlock ? .042f : .032f) * fade;
            DrawRing(burst.ring, burst.point, burst.right, burst.up, radius, arc, RingSegments);
            burst.precisionArc.enabled = preciseBlock;
            if (preciseBlock)
            {
                burst.precisionArc.widthMultiplier = .018f * fade;
                DrawRing(burst.precisionArc, burst.point, burst.right, burst.up, radius * .72f, Mathf.PI, RingSegments);
            }
            for (int i = 0; i < burst.rays.Length; ++i)
            {
                var ray = burst.rays[i];
                ray.enabled = !dodge;
                if (preciseBlock && i < 2)
                {
                    // Two prewarmed strokes give a .14m contact flash for only
                    // .08s. No filled shield, light, texture or additional renderer.
                    float flash = Mathf.Clamp01(1 - burst.age / .08f);
                    Vector3 axis = i == 0 ? burst.right : burst.up;
                    ray.enabled = flash > 0;
                    ray.SetPosition(0, burst.point + axis * (-.07f * flash));
                    ray.SetPosition(1, burst.point + axis * (.07f * flash));
                    ray.widthMultiplier = .028f * flash;
                    continue;
                }
                float angle = i * Mathf.PI * .5f + Mathf.PI * .25f;
                Vector3 outward = burst.right * Mathf.Cos(angle) + burst.up * Mathf.Sin(angle);
                float offset = radius + phase * .12f;
                ray.SetPosition(0, burst.point + outward * offset);
                ray.SetPosition(1, burst.point + outward * (offset + .12f * fade));
                ray.widthMultiplier = .022f * fade;
            }
        }

        private static void DrawFootImpact(Burst burst)
        {
            float phase = Mathf.Clamp01(burst.age / burst.lifetime);
            float fade = (1 - phase) * (1 - phase);
            burst.ring.enabled = burst.precisionArc.enabled = false;
            for (int i = 0; i < burst.rays.Length; ++i)
            {
                float angle = i * Mathf.PI * .5f + Mathf.PI * .25f;
                Vector3 outward = burst.right * Mathf.Cos(angle) + burst.up * Mathf.Sin(angle);
                var ray = burst.rays[i];
                ray.enabled = true;
                ray.SetPosition(0, burst.point + outward * (.025f + .05f * phase));
                ray.SetPosition(1, burst.point + outward * burst.radius);
                ray.widthMultiplier = .07f * fade;
            }
        }

        private static void DrawRing(LineRenderer line, Vector3 point, Vector3 right, Vector3 up, float radius, float arc, int segments)
        {
            for (int i = 0; i <= segments; ++i)
            {
                float angle = -arc * .5f + arc * i / segments;
                line.SetPosition(i, point + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
            }
        }

        private bool Seen(long strike, Kind kind)
        {
            for (int i = 0; i < historyCount; ++i) if (recentStrikes[i] == strike && recentKinds[i] == kind) return true;
            return false;
        }
        private void Remember(long strike, Kind kind)
        {
            recentStrikes[historyNext] = strike; recentKinds[historyNext] = kind;
            historyNext = (historyNext + 1) % HistorySize;
            historyCount = Mathf.Min(historyCount + 1, HistorySize);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Valid(long strike, Vector3 point, Vector3 direction)
            => strike > 0 && Finite(point) && Finite(direction) && Finite(direction.sqrMagnitude);
        private void HideWarning() { if (warning != null) warning.enabled = false; warningStrike = 0; warningRemaining = 0; }
        private static void Release(Burst burst) { burst.active = false; burst.root.SetActive(false); }
        private void OnDisable() => ResetFeedback();
        private void OnDestroy() { if (ownedRoot != null) Destroy(ownedRoot); }
    }
}
