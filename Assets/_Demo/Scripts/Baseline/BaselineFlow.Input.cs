using UnityEngine;

public sealed partial class BaselineFlow
{
    // One right click unwinds only the topmost interface; it never exits a fight.
    public bool TryInterfaceBack()
    {
        if(TitleScreenVisible){if(titlePanel!=0||confirmNew){CancelTitleAction();return true;}return false;}
        if(SideWorldEntrance.ConfirmationOpen){foreach(var entry in FindObjectsByType<SideWorldEntrance>(FindObjectsSortMode.None))entry.CancelConfirmation();return true;}
        if(dialogue&&dialogue.IsDialogueActive){dialogue.CancelDialogueAnimated();return true;}
        if(rewards&&rewards.IsOpen){rewards.Dismiss();return true;}
        if(VictoryOpen){LeaveEnding();return true;}
        if(Tutorial&&Tutorial.TryInterfaceBack())return true;
        var gear=GetComponent<BaselineQuickGearBar>();
        if(gear&&gear.isActiveAndEnabled&&gear.TryInterfaceBack())return true;
        if(MenuOpen){
            if(WorldControlsPanel.TryBack())return true;
            if(confirmNew)confirmNew=false;
            else if(MapOpen)CloseMapToMenu();
            else if(menuPage!=0)BeginP4PageTransition(0);
            else SetMenu(false);
            return true;
        }
        if(world&&world.JournalOpen){world.SetJournal(false);return true;}
        return false;
    }
}
