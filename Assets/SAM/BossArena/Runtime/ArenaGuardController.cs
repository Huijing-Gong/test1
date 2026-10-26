using System;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Driven by ArenaSession's simulation; this component never reads hardware,
    // advances an Update clock, changes health, or subscribes to damage events.
    [DisallowMultipleComponent]
    public sealed class ArenaGuardController : MonoBehaviour
    {
        [Serializable]
        public struct Settings
        {
            [Range(0, 360)] public float arcDegrees;
            [Range(0, 1)] public float damageMultiplier, pushMultiplier, movementMultiplier;
            [Min(0)] public float perfectWindowSeconds;

            public static Settings Default => new Settings
            {
                arcDegrees = 120, damageMultiplier = .35f, pushMultiplier = .25f,
                movementMultiplier = .45f, perfectWindowSeconds = .15f
            };
        }

        // Nested types avoid a second shared combat outcome enum.
        public enum Outcome { Unblocked, Blocked, PerfectBlocked }

        public readonly struct Resolution
        {
            public Outcome Kind { get; }
            public ArenaHit Hit { get; }
            public bool Blocked => Kind != Outcome.Unblocked;
            public bool Perfect => Kind == Outcome.PerfectBlocked;

            internal Resolution(Outcome kind, ArenaHit hit) { Kind = kind; Hit = hit; }
        }

        public Settings settings = Settings.Default;
        private readonly Logic logic = new Logic();
        public bool IsGuarding => logic.IsGuarding;
        public bool PerfectWindowOpen => logic.PerfectWindowOpen;
        public float GuardElapsed => logic.GuardElapsed;
        public float MovementMultiplier => IsGuarding
            ? Mathf.Clamp01(FiniteOr(settings.movementMultiplier, .45f)) : 1;

        // Feed physical held even when allowed=false. Pressed is an edge and
        // must be cleared by ArenaCommand.WithoutEdges; held must be retained.
        // Call after deciding dodge/committed-attack/hurt/cinematic priority,
        // before the boss tick can deliver a hit. A press while disallowed is lost.
        public void TickInput(bool held, bool pressed, bool allowed, float delta)
            => logic.TickInput(held, pressed, allowed && isActiveAndEnabled, delta, settings);

        // The caller owns team/death validation, strike dedup and dodge priority.
        // Call exactly once for a newly resolved strike, BEFORE committing HP.
        // Apply result.Hit, then emit the shared block event. No post-hit healing.
        // Blockability belongs to the shared skill policy, not a name whitelist.
        public Resolution ResolveHit(ArenaHit hit, bool blockable = true)
            => isActiveAndEnabled ? logic.ResolveHit(hit, transform.forward, settings, blockable)
                : new Resolution(Outcome.Unblocked, hit);

        // Restart/death must call this explicitly. Pass the physical held state
        // at restart so a key held across the round cannot gain a perfect window.
        public void ResetGuard(bool heldOnReset = false) => logic.Reset(heldOnReset);

        // Use immediately on death, focus loss, control takeover or action cancel.
        // Restoring control while held permits ordinary guard only, until release.
        public void SuspendGuard() => logic.Suspend();
        private void OnDisable() => SuspendGuard();

        // Managed state is separately constructible for checks without a scene.
        public sealed class Logic
        {
            public bool IsGuarding { get; private set; }
            public bool PerfectWindowOpen => IsGuarding && perfectAvailable;
            public float GuardElapsed { get; private set; }
            private bool wasHeld, mustRelease, perfectAvailable;

            public void Reset(bool heldOnReset = false)
            {
                IsGuarding = perfectAvailable = false;
                GuardElapsed = 0;
                wasHeld = mustRelease = heldOnReset;
            }

            public void Suspend()
            {
                IsGuarding = perfectAvailable = false;
                GuardElapsed = 0;
                mustRelease = true;
            }

            public void TickInput(bool held, bool pressed, bool allowed, float delta, Settings config)
            {
                if (!Finite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
                bool fresh = held && pressed && !wasHeld && !mustRelease;
                if (!held) mustRelease = false;
                if (!allowed || !held)
                {
                    IsGuarding = perfectAvailable = false;
                    GuardElapsed = 0;
                    if (!allowed && held) mustRelease = true;
                    wasHeld = held;
                    return;
                }
                if (!IsGuarding)
                {
                    IsGuarding = true;
                    GuardElapsed = 0;
                    perfectAvailable = fresh;
                }
                GuardElapsed += delta;
                float window = Mathf.Max(0, FiniteOr(config.perfectWindowSeconds, .15f));
                if (window <= 0 || GuardElapsed > window + .000001f) perfectAvailable = false;
                wasHeld = held;
            }

            public Resolution ResolveHit(ArenaHit hit, Vector3 facing, Settings config, bool blockable = true)
            {
                if (!IsGuarding || !blockable || !Finite(hit.damage) || hit.damage <= 0
                    || !Finite(facing) || !Finite(hit.direction))
                    return new Resolution(Outcome.Unblocked, hit);
                Vector3 forward = Vector3.ProjectOnPlane(facing, Vector3.up);
                // Hit.direction points from the danger towards the victim. For
                // rings this MUST use the ring centre, not the boss transform.
                Vector3 incoming = Vector3.ProjectOnPlane(-hit.direction, Vector3.up);
                if (forward.sqrMagnitude < .000001f || incoming.sqrMagnitude < .000001f)
                    return new Resolution(Outcome.Unblocked, hit);
                float arc = Mathf.Clamp(FiniteOr(config.arcDegrees, 120), 0, 360);
                float threshold = Mathf.Cos(arc * .5f * Mathf.Deg2Rad);
                if (Vector3.Dot(forward.normalized, incoming.normalized) + .000001f < threshold)
                    return new Resolution(Outcome.Unblocked, hit);
                if (perfectAvailable)
                {
                    // One perfect resolution per fresh press. Misses, attacks
                    // outside the arc and unblockable skills do not spend it.
                    perfectAvailable = false;
                    hit.damage = hit.push = 0;
                    return new Resolution(Outcome.PerfectBlocked, hit);
                }
                hit.damage *= Mathf.Clamp01(FiniteOr(config.damageMultiplier, .35f));
                hit.push *= Mathf.Clamp01(FiniteOr(config.pushMultiplier, .25f));
                return new Resolution(Outcome.Blocked, hit);
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static float FiniteOr(float value, float fallback) => Finite(value) ? value : fallback;
    }
}
