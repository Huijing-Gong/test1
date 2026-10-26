using System.Collections;
using UnityEngine;

public enum PuzzleColor { Red, Blue, Green, Yellow }

public sealed class ColorSequencePuzzle : MonoBehaviour
{
    [SerializeField] private ColorSequenceButton2D[] buttons;
    [SerializeField] private SpriteRenderer[] runeLights;
    [SerializeField] private SpriteRenderer doorRenderer;
    [SerializeField] private Sprite openDoorSprite;
    [SerializeField] private Collider2D doorBlocker;

    private static readonly PuzzleColor[] Sequence =
        { PuzzleColor.Blue, PuzzleColor.Green, PuzzleColor.Yellow, PuzzleColor.Red };
    private int progress;
    private bool busy;
    private bool completed;

    public void Configure(ColorSequenceButton2D[] newButtons, SpriteRenderer[] newRuneLights,
        SpriteRenderer newDoorRenderer, Sprite newOpenDoorSprite, Collider2D newDoorBlocker)
    {
        buttons = newButtons;
        runeLights = newRuneLights;
        doorRenderer = newDoorRenderer;
        openDoorSprite = newOpenDoorSprite;
        doorBlocker = newDoorBlocker;
    }

    public void Press(ColorSequenceButton2D button)
    {
        if (busy || completed || button == null || button.IsLockedDown) return;
        GameAudio.Play(GameSound.ButtonPress);
        if (button.Color == Sequence[progress])
        {
            GameAudio.Play(GameSound.RuneLight);
            button.LockDown();
            if (runeLights != null && progress < runeLights.Length && runeLights[progress] != null)
                runeLights[progress].enabled = true;
            progress++;
            if (progress == Sequence.Length) StartCoroutine(OpenDoor());
        }
        else StartCoroutine(WrongStep(button));
    }

    private IEnumerator WrongStep(ColorSequenceButton2D wrongButton)
    {
        busy = true;
        wrongButton.PressTemporarily();
        yield return new WaitForSeconds(0.22f);
        progress = 0;
        if (runeLights != null)
            foreach (SpriteRenderer light in runeLights) if (light != null) light.enabled = false;
        if (buttons != null)
            foreach (ColorSequenceButton2D button in buttons) if (button != null) button.ResetUp();
        yield return new WaitForSeconds(0.18f);
        busy = false;
    }

    private IEnumerator OpenDoor()
    {
        busy = true;
        yield return new WaitForSeconds(0.25f);
        completed = true;
        GameAudio.Play(GameSound.DoorOpen, 0.9f);
        if (doorRenderer != null && openDoorSprite != null) doorRenderer.sprite = openDoorSprite;
        if (doorBlocker != null) doorBlocker.enabled = false;
        busy = false;
    }
}
