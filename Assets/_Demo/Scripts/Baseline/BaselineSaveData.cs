using System;
using UnityEngine;

[Serializable]
public sealed class BaselineSaveData
{
    public const int CurrentVersion = 4;
    public const string RecallItemId = "camp.recall-beacon";
    public static readonly Vector3 DefaultSafePosition = new Vector3(0f, 1.05f, -67f);

    public int version = CurrentVersion;
    // Schema and map layout versions are independent. A save from the original
    // world remains map 1 until a V2 layout explicitly migrates its safe position.
    public int mapVersion = 1;
    public bool northBridgeOpen;
    public bool openingCompleted;
    public bool[] accepted = new bool[3];
    public bool[] visited = new bool[3];
    public bool[] completed = new bool[3];
    public bool[] opened = new bool[3];
    public int tracked = -1;
    public int runeProgress;
    public bool puzzleSolved;
    public bool introCompleted;
    public bool hasSword;
    public bool bossDefeated;
    public bool trialACompleted;
    public bool trialBCompleted;
    // Additive field: old saves play any newly available eclipse chapter once.
    public int eclipsePresentedStage;
    // Optional additive v4 field; absent older saves retain0. Highest earned B capacity bonus.
    public int trialBDifficultyBonus;
    // Confluence chapter completion only; grants no inventory items.
    public bool worldSurveyComplete, worldInscriptionRead, worldRelayComplete;
    public bool[] trialRewardClaimed = new bool[2];
    public bool[] taskRewardClaimed = new bool[3];
    public BaselineTaskReward[] taskRewardGrants = new BaselineTaskReward[3];
    public int recallBeaconsUsed;
    // Bits 0-4: five rewards. Bit 5: owned practice sword unequipped.
    public int unequippedMask;
    public int RecallBeaconsGranted
    {
        get
        {
            int total = 0;
            if (taskRewardGrants == null || taskRewardClaimed == null) return 0;
            for (int i = 0; i < taskRewardGrants.Length && i < taskRewardClaimed.Length; i++)
                if (taskRewardClaimed[i] && taskRewardGrants[i] != null && taskRewardGrants[i].itemId == RecallItemId)
                    total += taskRewardGrants[i].quantity;
            return total;
        }
    }
    public int RecallBeaconsRemaining => Mathf.Max(0, RecallBeaconsGranted - recallBeaconsUsed);
    // Decode-only notice: the store cannot know a scene's authored camp anchor.
    [NonSerialized] internal bool safePositionWasReset;
    public Vector3 safePosition = DefaultSafePosition;
    public float safeYaw;
}
