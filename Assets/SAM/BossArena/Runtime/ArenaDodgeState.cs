using System;
using System.Collections.Generic;

namespace SamProduction.BossArena
{
    /// <summary>
    /// Tracks a dodge that the player controller has already accepted. The controller
    /// retains ownership of input, cooldown, movement and Fighter.Invulnerable.
    /// Advance once per combat substep before the boss performs damage queries.
    /// </summary>
    [Serializable]
    public sealed class ArenaDodgeState
    {
        public float perfectStart = .045f;
        public float perfectEnd = .145f;

        public bool Active { get; private set; }
        public float Age => Active ? (float)elapsed : -1f;
        public long DodgeId { get; private set; }
        public long PerfectStrike { get; private set; }
        public bool IsInvulnerable => Active && InWindow(invulnerableStart, invulnerableEnd);
        public bool PerfectAvailable => IsInvulnerable && !perfectAwarded &&
            InWindow(activePerfectStart, activePerfectEnd);

        private double elapsed;
        private float duration, invulnerableStart, invulnerableEnd;
        private float activePerfectStart, activePerfectEnd;
        private bool perfectAwarded;
        private readonly HashSet<long> observedStrikes = new HashSet<long>();

        /// <summary>
        /// Call only when the existing controller actually starts a dodge, after its
        /// cooldown/action checks. An active dodge cannot be restarted to rearm it.
        /// Window settings are snapshotted so editing them cannot rearm this dodge.
        /// </summary>
        public bool TryBegin(float actionDuration = .54f, float iFrameStart = .045f,
            float iFrameEnd = .32f)
        {
            if (Active) return false;
            ValidateWindow(actionDuration, iFrameStart, iFrameEnd, nameof(iFrameStart));
            ValidateWindow(actionDuration, perfectStart, perfectEnd, nameof(perfectStart));
            duration = actionDuration;
            invulnerableStart = iFrameStart;
            invulnerableEnd = iFrameEnd;
            activePerfectStart = perfectStart;
            activePerfectEnd = perfectEnd;
            elapsed = 0;
            perfectAwarded = false;
            PerfectStrike = 0;
            observedStrikes.Clear();
            DodgeId = checked(DodgeId + 1);
            Active = true;
            return true;
        }

        /// <summary>
        /// canAct must be false for death, a finished round or cinematic takeover.
        /// Cancel does not shorten the controller's existing cooldown.
        /// </summary>
        public void Advance(float delta, bool canAct)
        {
            if (!Finite(delta) || delta < 0)
                throw new ArgumentOutOfRangeException(nameof(delta));
            if (!canAct) { Cancel(); return; }
            if (!Active) return;
            elapsed += delta;
            if (elapsed >= duration) Cancel();
        }

        /// <summary>
        /// Call exclusively from the player's real ArenaFighter.Avoided callback,
        /// after its collision query and strike deduplication. A telegraph, near miss,
        /// block or arbitrary Invulnerable flag is not a dodge-success observation.
        /// Returns true once per dodge, only inside the intersection of both windows.
        /// </summary>
        public bool TryConfirmPerfect(ArenaHit hit)
        {
            if (!IsInvulnerable || hit.source == null || hit.target == null ||
                hit.source == hit.target || hit.source.Dead || hit.target.Dead ||
                hit.target.team != ArenaTeam.Player || hit.source.team == hit.target.team ||
                !hit.target.Invulnerable || hit.strike <= 0 || !Finite(hit.damage) || hit.damage <= 0)
                return false;
            // Remember ordinary avoided hits too: a repeated notification cannot
            // become perfect after the clock changes. Session-wide dedup stays in Fighter.
            if (!observedStrikes.Add(hit.strike) || !PerfectAvailable) return false;
            perfectAwarded = true;
            PerfectStrike = hit.strike;
            return true;
        }

        /// <summary>Call on action cancellation, damage, death or control takeover.</summary>
        public void Cancel()
        {
            Active = false;
            elapsed = 0;
            perfectAwarded = false;
            PerfectStrike = 0;
            observedStrikes.Clear();
        }

        /// <summary>Call together with ResetPlayer / ResetFighter on round restart.</summary>
        public void Reset()
        {
            Cancel();
            DodgeId = 0;
        }

        private bool InWindow(float start, float end) => elapsed >= start && elapsed <= end;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void ValidateWindow(float actionDuration, float start, float end, string name)
        {
            if (!Finite(actionDuration) || actionDuration <= 0 || !Finite(start) ||
                !Finite(end) || start < 0 || end < start || end > actionDuration)
                throw new ArgumentOutOfRangeException(name, "Dodge windows must fit the finite action duration.");
        }
    }
}
