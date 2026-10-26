using UnityEngine;

public sealed partial class BaselineFlow
{
    public bool InventoryOpen {get;private set;}
    public int InventoryClosedFrame {get;private set;}=-1;
    public bool InventoryAvailable => isActiveAndEnabled && Started && world &&
        !EclipseCinematicActive && !SideWorldEntrance.ConfirmationOpen && !TitleScreenVisible &&
        !MenuOpen && !MapOpen && !VictoryOpen && !DeathOpen && !openingClosing && !p4Closing &&
        !world.JournalOpen && !(SamBridge && SamBridge.IsAway) &&
        !(endingRoom && endingRoom.IsInside) && !(Tutorial && Tutorial.ModalOpen) &&
        !(rewards && rewards.IsOpen) &&
        !(dialogue && (dialogue.IsDialogueActive || dialogue.IsRewardPresentationActive)) &&
        (!SideWorldTravel.Instance || (!SideWorldTravel.Instance.IsAway && !SideWorldTravel.Instance.Busy)) &&
        (InventoryOpen || (movement && movement.CanStartInteraction && !BlockWorldInput));
    public void SetInventoryOpen(bool value)
    {
        if(value==InventoryOpen)return;
        if(value && !InventoryAvailable)return;
        InventoryOpen=value;
        if(value)Lock();
        else {InventoryClosedFrame=Time.frameCount;unlockAfterFrame=Time.frameCount;}
    }
}
