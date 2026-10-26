using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }
    public static event Action<bool> SwordStateChanged;
    public static event Action<bool> SwordEquipChanged;

    public bool IntroDialogueCompleted { get; private set; }
    public bool HasSword { get; private set; }
    public bool SwordEquipped { get; private set; }
    public bool BossGateUnlocked { get; private set; }
    public bool BossDefeated { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    public void CompleteIntroDialogue() => IntroDialogueCompleted = true;

    public bool GrantSword()
    {
        if (HasSword)
        {
            return false;
        }

        HasSword = true;
        SwordEquipped = true;
        SwordStateChanged?.Invoke(true);
        SwordEquipChanged?.Invoke(true);
        return true;
    }

    public void SetSwordEquipped(bool equipped)
    {
        bool next = HasSword && equipped;
        if (SwordEquipped == next) return;
        SwordEquipped = next;
        SwordEquipChanged?.Invoke(next);
    }

    public void UnlockBossGate() => BossGateUnlocked = true;
    public void MarkBossDefeated() => BossDefeated = true;

    // Explicit single-slot restore/new-game entry point; preserves the singleton.
    public void RestoreProgress(bool introCompleted, bool hasSword, bool bossDefeated, bool swordEquipped = true)
    {
        IntroDialogueCompleted = introCompleted;
        HasSword = hasSword;
        SwordEquipped = hasSword && swordEquipped;
        BossGateUnlocked = false; // The baseline ending is optional, not task-gated.
        BossDefeated = bossDefeated;
        SwordStateChanged?.Invoke(HasSword);
        SwordEquipChanged?.Invoke(SwordEquipped);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
