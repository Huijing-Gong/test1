using UnityEngine;
using System;
using Dot.EndingCandidate;

// Narrow original-owner adapter. No reward, ending UI, scene, or persistent schema.
public sealed partial class BaselineFlow : IEndingProgressOwner
{
    EndingConsumerCandidate endingOutcomes;
    public EndingConsumerCandidate EndingOutcomes
    {
        get { return endingOutcomes ?? (endingOutcomes = new EndingConsumerCandidate(this)); }
    }

    bool IEndingProgressOwner.BossDefeated { get { return Progress != null && Progress.bossDefeated; } }

    void IEndingProgressOwner.RecordBossDefeated()
    {
        if (!Started || !world || Progress == null || !GameSession.Instance)
            throw new InvalidOperationException("No active original main-world progress owner.");
        GameSession.Instance.MarkBossDefeated();
        Progress.bossDefeated = true;
    }

    OwnerSaveResult IEndingProgressOwner.SaveCompletionWithoutRememberingPosition()
    {
        bool accepted = SaveProgress(false, out var message);
        return new OwnerSaveResult(accepted, message);
    }

    void NotifyEndingOwnerSaveResult(bool accepted, string message)
    {
        // Only an actual completed save containing both original mirrors may
        // resolve a pending ending save. Consumer rejects notifications in commit.
        if (endingOutcomes != null && Progress != null && Progress.bossDefeated &&
            GameSession.Instance && GameSession.Instance.BossDefeated)
            endingOutcomes.ObserveOwnerSaveResult(new OwnerSaveResult(accepted, message));
    }

    void ResetEndingOutcomeSession()
    {
        if (endingOutcomes != null) endingOutcomes.ResetAfterProgressRestore();
    }
    internal void PresentSamReturn(bool won, bool completionSavedAtEntry, string message)
    {
        nextSave = Time.unscaledTime + 8;
        if (!string.IsNullOrEmpty(message)) { Status = message; world.ShowNotice(message); }
        if (!won) return;
        if (endingRoom) endingRoom.SetCompleted(Progress.bossDefeated);
        victorySaved = completionSavedAtEntry || (!EndingOutcomes.PendingSave && EndingOutcomes.SaveState == SaveKnowledge.Accepted);
        VictoryOpen = true; MenuOpen = false; menuScroll = Vector2.zero;
        Status = completionSavedAtEntry ? "Victory. Your existing ending is kept."
            : victorySaved ? "Victory. The final guardian has fallen. Progress saved."
            : "Victory reached. Saving is not confirmed. Return and retry Save progress.";
        Lock(); // Only after Sam has been unloaded, never during authored DeathHold/audio.
    }
}
