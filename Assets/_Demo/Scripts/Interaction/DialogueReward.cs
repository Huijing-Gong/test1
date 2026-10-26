using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NPCDialogue))]
public sealed class DialogueReward : MonoBehaviour
{
    [SerializeField] private bool completeIntroDialogue = true;
    [SerializeField] private bool grantSword = true;
    [SerializeField] private string rewardDisplayName = "Practice Sword";

    private NPCDialogue dialogue;

    private void Awake() => dialogue = GetComponent<NPCDialogue>();

    private void OnEnable()
    {
        if (dialogue == null) dialogue = GetComponent<NPCDialogue>();
        if (dialogue != null) dialogue.DialogueCompleted += OnDialogueCompleted;
    }

    private void OnDisable()
    {
        if (dialogue != null) dialogue.DialogueCompleted -= OnDialogueCompleted;
    }

    private void OnDialogueCompleted(DialogueUI dialogueUI)
    {
        GameSession session = GameSession.Instance;
        if (session == null) return;

        if (completeIntroDialogue) session.CompleteIntroDialogue();
        if (grantSword && session.GrantSword())
        {
            BaselineRewardPresentation presentation = FindFirstObjectByType<BaselineRewardPresentation>();
            bool presented = presentation && presentation.Show(
                "PRACTICE SWORD",
                "A simple sword for the first steps of your journey.",
                true,
                null);
            if (!presented) dialogueUI.ShowItemAcquired(rewardDisplayName);
        }
    }
}
