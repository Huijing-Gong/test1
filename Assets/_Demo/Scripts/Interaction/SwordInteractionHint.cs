using UnityEngine;

/// <summary>Nearby hint for the sword's existing H input.</summary>
[DisallowMultipleComponent]
public sealed class SwordInteractionHint : MonoBehaviour
{
    [SerializeField, Min(.1f)] private float distance = 3f;
    private PlayerMovement player;
    private DialogueUI dialogue;

    private void Update()
    {
        if (!player) player = FindFirstObjectByType<PlayerMovement>();
        if (!dialogue) dialogue = FindFirstObjectByType<DialogueUI>();
        if (player && dialogue && player.CanStartInteraction
            && (player.transform.position - transform.position).sqrMagnitude <= distance * distance)
            dialogue.OfferPrompt("[H] Switch Form");
    }
}
