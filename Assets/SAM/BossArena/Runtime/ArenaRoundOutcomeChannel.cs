using System;

namespace SamProduction.BossArena
{
    // Runtime notification only. The host owns permanent progress and saving.
    public readonly struct ArenaRoundOutcome
    {
        public long AttemptId { get; }
        public ArenaRoundState State { get; }
        public double BattleTime { get; }

        internal ArenaRoundOutcome(long attemptId, ArenaRoundState state, double battleTime)
        {
            AttemptId = attemptId;
            State = state;
            BattleTime = battleTime;
        }
    }

    // Main-thread only; created once per ArenaSession. No polling or save API.
    internal sealed class ArenaRoundOutcomeChannel : IDisposable
    {
        private readonly Action<Exception> reportError;
        private bool fighting, resolved, published, disposed;
        private ArenaRoundOutcome pending;

        public long AttemptId { get; private set; }
        public event Action<ArenaRoundOutcome> RoundEnded;

        public ArenaRoundOutcomeChannel(Action<Exception> reportError = null)
        {
            this.reportError = reportError;
        }

        public bool Restart(bool begin)
        {
            if (disposed) return false;
            if (begin) AttemptId = checked(AttemptId + 1);
            fighting = begin;
            resolved = published = false;
            pending = default(ArenaRoundOutcome);
            return true;
        }

        public bool TryResolve(ArenaRoundState state, double battleTime, out ArenaRoundOutcome outcome)
        {
            outcome = default(ArenaRoundOutcome);
            if (disposed || !fighting || resolved ||
                (state != ArenaRoundState.Victory && state != ArenaRoundState.Defeat)) return false;
            fighting = false;
            resolved = true; // Latch before cleanup or external callbacks.
            pending = outcome = new ArenaRoundOutcome(AttemptId, state, battleTime);
            return true;
        }

        public bool Publish(ArenaRoundOutcome outcome)
        {
            if (disposed || !resolved || published || outcome.AttemptId != AttemptId ||
                outcome.State != pending.State || outcome.BattleTime != pending.BattleTime) return false;
            published = true; // Latch before reentrant retry/return.
            var snapshot = RoundEnded;
            if (snapshot == null) return true;
            // Each listener receives the captured result, even if an earlier
            // listener restarts. Unsubscribe/Dispose affect future dispatches;
            // they do not retract this already-started synchronous dispatch.
            foreach (Action<ArenaRoundOutcome> listener in snapshot.GetInvocationList())
            {
                try { listener(outcome); }
                catch (Exception exception)
                {
                    // A failing observer must not suppress the host's receipt.
                    try { reportError?.Invoke(exception); }
                    catch (Exception) { /* Keep delivering the terminal result. */ }
                }
            }
            return true;
        }

        public void Dispose()
        {
            disposed = true;
            fighting = false;
            RoundEnded = null;
        }
    }
}
