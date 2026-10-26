using System.Collections.Generic;
using UnityEngine;

public sealed class MinigameTarget : MonoBehaviour
{
    public enum ActionKind { ActivateFlames, CarryLamp, OpenChest }
    public ActionKind action;
    public FlameCollectionGame flames;
    public ConstellationGame constellation;
    public MinigameRewardChest chest;
    public int lampIndex;
    public float distance = 2.5f;
    public static readonly List<MinigameTarget> Active = new List<MinigameTarget>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Clear() => Active.Clear();
    void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() => Active.Remove(this);
    public bool Available => action == ActionKind.ActivateFlames ? flames && !flames.State.Active :
        action == ActionKind.CarryLamp ? constellation && !constellation.State.IsPlaced(lampIndex) :
        chest && chest.Available;
    public string Prompt => action == ActionKind.ActivateFlames ? "[E] Activate" :
        action == ActionKind.CarryLamp ? "[E] Pick up star orb" : "[E] Open chest";
    public void Interact(MinigamePlayer player)
    {
        if (!player || !player.CanAct || player.gameObject.scene!=gameObject.scene || !Available) return;
        if (action == ActionKind.ActivateFlames) flames.Activate();
        else if (action == ActionKind.CarryLamp) player.PickUp(constellation, lampIndex);
        else chest.Open();
    }
}
