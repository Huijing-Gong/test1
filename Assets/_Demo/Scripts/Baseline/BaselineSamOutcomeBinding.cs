using System;
using Dot.EndingCandidate;
using SamProduction.BossArena;

// One host control binding. Scene loading/unloading belongs to the caller.
// Subscribe before first battle input. Other listeners must not unload first.
public sealed class BaselineSamOutcomeBinding : IDisposable
{
    readonly ArenaSession session;
    readonly EndingConsumerCandidate consumer;
    readonly Action<ArenaRoundOutcome> handler;
    readonly Action<ArenaRoundOutcome> afterConsumed;
    long consumedAttempt;
    bool disposed;

    public long Visit { get; private set; }
    public bool IsDisposed { get { return disposed; } }

    public BaselineSamOutcomeBinding(BaselineFlow owner, ArenaSession exactSession,
        Action<ArenaRoundOutcome> afterConsumed = null)
    {
        if (owner == null || !owner.Started) throw new InvalidOperationException("Host progress must be restored before binding.");
        if (exactSession == null) throw new ArgumentNullException("exactSession");
        session = exactSession;
        consumer = owner.EndingOutcomes;
        this.afterConsumed = afterConsumed;
        Visit = consumer.BeginVisit(session);
        long capturedVisit = Visit;
        handler = outcome =>
        {
            if (disposed) return;
            TerminalOutcome terminal;
            if (outcome.State == ArenaRoundState.Victory) terminal = TerminalOutcome.Victory;
            else if (outcome.State == ArenaRoundState.Defeat) terminal = TerminalOutcome.Defeat;
            else return;
            // Acceptance uses ONLY captured identity and immutable message.
            // Never compare the message to a possibly already restarted Session.
            if (!consumer.Consume(session, capturedVisit, outcome.AttemptId, terminal)) return;
            consumedAttempt = outcome.AttemptId;
            this.afterConsumed?.Invoke(outcome);
        };
        session.RoundEnded += handler;
    }

    public bool TryRetry()
    {
        if (disposed || !CanControlCurrentRound()) return false;
        session.Restart(true);
        return true;
    }

    public bool TryReturn(Action unloadOwnedScene = null)
    {
        if (disposed || !CanControlCurrentRound()) return false;
        session.automaticTick = false;
        if (session.view != null) session.view.automaticTick = false;
        session.RoundEnded -= handler;
        consumer.EndVisit(Visit);
        disposed = true;
        unloadOwnedScene?.Invoke();
        return true;
    }

    public void Dispose()
    {
        if (disposed) return;
        if (!TryReturn()) throw new InvalidOperationException("Consume the current terminal before retry, return, or unbinding.");
    }

    bool CanControlCurrentRound()
    {
        // This guards control order, not outcome acceptance or victory inference.
        bool terminal = session.State == ArenaRoundState.Victory || session.State == ArenaRoundState.Defeat;
        return !terminal || consumedAttempt == session.AttemptId;
    }
}
