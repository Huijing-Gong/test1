using UnityEngine;

// Side A's result menu can claim the same existing reward while the host is
// suspended. No new receipt, inventory item, currency or separate save slot.
public sealed partial class BaselineFlow
{
    public bool ClaimSideATrialReward(BattleManager battle)
    {
        var travel = SideWorldTravel.Instance;
        if (!Started || !world || !world.player || !travel || travel.HostFlow != this ||
            !travel.IsAway || travel.IsSideWorldB || travel.Busy || !battle || battle.CurrentTrialIndex != 0 ||
            battle.gameObject.scene.path != SideWorldTravel.BattlePath || !battle.PlayerHasWon ||
            !Progress.trialACompleted || Progress.trialRewardClaimed[0]) return false;

        Progress.trialRewardClaimed[0] = true;
        if (!SaveNow()) { Progress.trialRewardClaimed[0] = false; return false; }
        // Do not unlock, reactivate or show a host-world reward popup while away.
        // BaselineLoadout reads this exact receipt on return, just as before.
        Status = "Trial A blade core saved. Manage equipment in the journey menu.";
        return true;
    }

    public string SideATrialRewardEffect
    {
        get
        {
            var loadout = world && world.player ? world.player.GetComponent<BaselineLoadout>() : null;
            return loadout ? loadout.EquipmentEffect(3) + " in the main world" : "Main-world equipment reward";
        }
    }
}
