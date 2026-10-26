using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction.BossArena
{
    /// <summary>
    /// One reusable ground ring for a confirmed perfect dodge. No material instances,
    /// camera effects, global time changes or imported assets. HUD/audio remain optional
    /// consumers of the same confirmed event in the integration owner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaDodgeFeedback : MonoBehaviour
    {
        public ArenaCombatFx effects;
        public Material cyan;
        public bool automaticTick = true;
        [Min(.01f)] public float lifetime = .32f;
        [Min(.01f)] public float finalRadius = .9f;
        [Min(.001f)] public float width = .045f;
        [Min(0)] public float groundOffset = .035f;

        public bool Visible => ring != null && ring.enabled;

        private const int Segments = 32;
        private LineRenderer ring;
        private Vector3 centre;
        private float age, activeLifetime, activeRadius, activeWidth;
        private long lastDodgeId;

        private void Awake() { Warmup(); }
        private void Update() { if (automaticTick) Tick(Time.deltaTime); }
        private void OnDisable() { ResetFeedback(); }
        private void OnDestroy() { if (ring != null) Destroy(ring.gameObject); }

        /// <summary>Assign cyan or effects first; call before combat to allocate the ring.</summary>
        public bool Warmup()
        {
            Material material = cyan != null ? cyan : effects != null ? effects.cyan : null;
            if (material == null) return false;
            if (ring == null)
            {
                var root = new GameObject("Perfect dodge ground ring");
                root.transform.SetParent(transform, false);
                ring = root.AddComponent<LineRenderer>();
                ring.useWorldSpace = true;
                ring.loop = true;
                ring.positionCount = Segments;
                ring.numCapVertices = 0;
                ring.shadowCastingMode = ShadowCastingMode.Off;
                ring.receiveShadows = false;
                ring.enabled = false;
            }
            ring.sharedMaterial = material;
            return true;
        }

        /// <summary>
        /// Call only after ArenaDodgeState.TryConfirmPerfect returns true, passing its
        /// DodgeId. Duplicate feedback for that dodge is ignored. Returns false if no
        /// existing cyan material is assigned; it never creates a fallback material.
        /// </summary>
        public bool PlayPerfect(ArenaHit hit, long dodgeId)
        {
            if (!isActiveAndEnabled || dodgeId <= 0 || dodgeId == lastDodgeId ||
                hit.target == null || hit.target.Dead || hit.target.team != ArenaTeam.Player ||
                !Warmup()) return false;
            lastDodgeId = dodgeId;
            centre = hit.target.transform.position + Vector3.up * Mathf.Max(0, groundOffset);
            age = 0;
            activeLifetime = Mathf.Max(.01f, lifetime);
            activeRadius = Mathf.Max(.01f, finalRadius);
            activeWidth = Mathf.Max(.001f, width);
            ring.enabled = true;
            RenderRing();
            return true;
        }

        /// <summary>
        /// For arena-driven stepping, disable automaticTick and call once per combat
        /// substep. ResetFeedback must also be called on restart/death/takeover.
        /// </summary>
        public void Tick(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0)
                throw new System.ArgumentOutOfRangeException(nameof(delta));
            if (!Visible) return;
            age += delta;
            if (age >= activeLifetime) { ring.enabled = false; return; }
            RenderRing();
        }

        public void ResetFeedback()
        {
            if (ring != null) ring.enabled = false;
            lastDodgeId = 0;
            age = 0;
        }

        private void RenderRing()
        {
            float progress = Mathf.Clamp01(age / activeLifetime);
            float radius = Mathf.Lerp(activeRadius * .35f, activeRadius, progress);
            ring.widthMultiplier = activeWidth * (1 - progress);
            Color colour = new Color(.35f, 1, 1, 1 - progress);
            ring.startColor = ring.endColor = colour;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2 / Segments;
                ring.SetPosition(i, centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
            }
        }
    }
}
