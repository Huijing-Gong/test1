using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NPCDialogue : MonoBehaviour
{
    [SerializeField] private string npcName = "Warden";
    [SerializeField] private string[] dialogueLines =
    {
        "You have finally arrived, traveler.",
        "The ruins ahead are dangerous. Get your bearings before you venture inside.",
        "Take this sword. You will need it to defend yourself."
    };
    [SerializeField] private string interactionPrompt = "[E] Talk";
    [SerializeField, Min(0f)] private float interactionDistance = 2.5f;
    [SerializeField, Range(-1f, 1f)] private float forwardDotThreshold = 0.25f;
    [SerializeField] private bool repeatable = true;
    [SerializeField] private bool interactable = true;
    [SerializeField] private bool useSwordRewardDialogue = true;
    [SerializeField] private string postRewardLine = "Use that sword wisely. Greater dangers await you.";

    public event Action<DialogueUI> DialogueCompleted;
    private bool hasCompletedDialogue;

    public string NPCName => npcName;
    public int LineCount => HasRewardSword ? 1 : dialogueLines == null ? 0 : dialogueLines.Length;
    public string InteractionPrompt => interactionPrompt;
    public float InteractionDistance => Mathf.Max(0f, interactionDistance);
    public float ForwardDotThreshold => Mathf.Clamp(forwardDotThreshold, -1f, 1f);
    public bool Repeatable => repeatable;
    public bool IsInteractable => interactable && isActiveAndEnabled && LineCount > 0 && (repeatable || !hasCompletedDialogue);

    // Runtime integration can update an existing guide without rewriting its scene.
    public void ConfigureDialogue(string prompt, string[] lines)
    {
        interactionPrompt = prompt;
        dialogueLines = lines;
        useSwordRewardDialogue = false;
    }

    public void SetNPCName(string value) => npcName = value;

    public void SetRepeatable(bool value) => repeatable = value;

    public void SetInteractionPrompt(string value) => interactionPrompt = value;

    public string GetLine(int index)
    {
        if (HasRewardSword)
        {
            return index == 0 ? postRewardLine : string.Empty;
        }

        return dialogueLines != null && index >= 0 && index < dialogueLines.Length
            ? dialogueLines[index]
            : string.Empty;
    }

    public void NotifyDialogueCompleted(DialogueUI dialogueUI)
    {
        if (dialogueUI == null || (!repeatable && hasCompletedDialogue)) return;
        hasCompletedDialogue = true;
        DialogueCompleted?.Invoke(dialogueUI);
    }

    private bool HasRewardSword => useSwordRewardDialogue && GameSession.Instance != null && GameSession.Instance.HasSword;

    private void OnTriggerEnter(Collider other)
    {
        PlayerInteraction player = other.GetComponent<PlayerInteraction>();
        if (player != null)
        {
            player.RegisterCandidate(this);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        PlayerInteraction player = other.GetComponent<PlayerInteraction>();
        if (player != null)
        {
            player.RegisterCandidate(this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerInteraction player = other.GetComponent<PlayerInteraction>();
        if (player != null)
        {
            player.UnregisterCandidate(this);
        }
    }
}
