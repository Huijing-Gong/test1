using System;

namespace Dot.EndingCandidate
{
    // Temporary engine-independent candidate. Not imported into either Unity project.
    public enum TerminalOutcome { Victory, Defeat }
    public enum SaveKnowledge { Unobserved, Pending, Accepted, Unconfirmed }

    public struct OwnerSaveResult
    {
        public readonly bool Accepted;
        public readonly string Message;
        public OwnerSaveResult(bool accepted, string message)
        {
            Accepted = accepted;
            Message = message ?? String.Empty;
        }
    }

    public interface IEndingProgressOwner
    {
        bool BossDefeated { get; }
        // Existing owner must mark BOTH runtime mirror and Progress. Idempotent.
        void RecordBossDefeated();
        // Existing R3 owner saves full original progress without remembering Sam position.
        OwnerSaveResult SaveCompletionWithoutRememberingPosition();
    }

    // Lifetime: one loaded host progress session. Retain across Sam retries/scene return.
    // Call from one thread. Owner must not restore/replace progress inside a commit.
    public sealed class EndingConsumerCandidate
    {
        readonly IEndingProgressOwner owner;
        object boundSender;
        long visitSequence;
        long activeVisit;
        long lastTerminalAttempt;
        bool needsRecord;
        bool committing;

        public bool HasObservedVictory { get; private set; }
        public bool CompletionPreexisting { get; private set; }
        public bool PendingSave { get; private set; }
        public SaveKnowledge SaveState { get; private set; }
        public string LastSaveMessage { get; private set; }

        public EndingConsumerCandidate(IEndingProgressOwner owner)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            this.owner = owner;
            LastSaveMessage = String.Empty;
        }

        // Capture returned token in the subscription; publisher does not persist it.
        public long BeginVisit(object exactSender)
        {
            if (exactSender == null) throw new ArgumentNullException("exactSender");
            if (visitSequence == Int64.MaxValue) throw new InvalidOperationException("Visit token exhausted.");
            activeVisit = ++visitSequence;
            boundSender = exactSender;
            lastTerminalAttempt = 0;
            return activeVisit;
        }

        public void EndVisit(long visit)
        {
            if (visit != activeVisit) return;
            activeVisit = 0;
            boundSender = null;
            // Completion/save state deliberately survives scene unload and retry.
        }

        // True means a new valid terminal was consumed, NOT that persistence succeeded.
        public bool Consume(object sender, long visit, long attempt, TerminalOutcome outcome)
        {
            if (activeVisit == 0 || visit != activeVisit || !Object.ReferenceEquals(sender, boundSender) ||
                attempt <= 0 || attempt <= lastTerminalAttempt ||
                (outcome != TerminalOutcome.Victory && outcome != TerminalOutcome.Defeat)) return false;

            // Fix dedup BEFORE any owner callback. First terminal wins this attempt.
            lastTerminalAttempt = attempt;
            if (outcome == TerminalOutcome.Defeat || HasObservedVictory) return true;
            HasObservedVictory = true;
            try
            {
                if (owner.BossDefeated)
                {
                    CompletionPreexisting = true;
                    // Legacy completion is retained; do not infer that it was durably saved.
                    return true;
                }
            }
            catch (Exception error)
            {
                needsRecord = true;
                PendingSave = true;
                SetFailure(error);
                return true;
            }
            needsRecord = true;
            PendingSave = true;
            SaveState = SaveKnowledge.Pending;
            RetryPendingSave();
            return true;
        }

        // Explicit host Save action, not an automatic duplicate-event retry.
        public bool RetryPendingSave()
        {
            if (!PendingSave || committing) return false;
            committing = true;
            try
            {
                if (needsRecord)
                {
                    // Retry the idempotent owner record if its prior callback threw.
                    // A Progress bit alone does not prove the runtime mirror was updated.
                    owner.RecordBossDefeated();
                    if (!owner.BossDefeated) throw new InvalidOperationException("Owner did not retain boss completion.");
                    needsRecord = false;
                }
                ApplySaveResult(owner.SaveCompletionWithoutRememberingPosition());
                return !PendingSave;
            }
            catch (Exception error)
            {
                SetFailure(error);
                return false;
            }
            finally { committing = false; }
        }

        // Only report an actual original-owner save that included the recorded boss bit.
        // Preserve R3 warning policy in the owner; this class merely retains its result.
        public bool ObserveOwnerSaveResult(OwnerSaveResult result)
        {
            if (!PendingSave || needsRecord || committing) return false;
            ApplySaveResult(result);
            return true;
        }

        // Only after original New Game/Continue has restored all original progress/mirrors.
        public void ResetAfterProgressRestore()
        {
            if (committing) throw new InvalidOperationException("Cannot replace progress during completion commit.");
            boundSender = null;
            activeVisit = 0;
            lastTerminalAttempt = 0;
            HasObservedVictory = CompletionPreexisting = PendingSave = needsRecord = false;
            SaveState = SaveKnowledge.Unobserved;
            LastSaveMessage = String.Empty;
            // Do not reset visitSequence: stale subscription tokens must stay invalid.
        }

        void ApplySaveResult(OwnerSaveResult result)
        {
            LastSaveMessage = result.Message;
            PendingSave = !result.Accepted;
            SaveState = result.Accepted ? SaveKnowledge.Accepted : SaveKnowledge.Unconfirmed;
        }

        void SetFailure(Exception error)
        {
            PendingSave = true;
            SaveState = SaveKnowledge.Unconfirmed;
            LastSaveMessage = error.GetType().Name + ": " + error.Message;
        }
    }
}
