using UnityEngine;

[RequireComponent(typeof(NPCDialogue))]
public sealed class BaselineInteraction : MonoBehaviour
{
    public enum ActionKind { AcceptTask, Objective, Chest, Rune }
    public ActionKind action;
    public int index;
    public Transform lid;
    private NPCDialogue dialogue;
    private void OnEnable()
    {
        dialogue = GetComponent<NPCDialogue>();
        dialogue.DialogueCompleted += Complete;
    }
    private void OnDisable() { if (dialogue) dialogue.DialogueCompleted -= Complete; }
    private void Complete(DialogueUI ui)
    {
        var world = FindFirstObjectByType<BaselineWorld>();
        if (world) world.Interact(this);
    }
}
