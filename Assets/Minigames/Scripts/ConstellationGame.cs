using UnityEngine;

public sealed class ConstellationGame : MonoBehaviour
{
    public Transform[] movableLamps;
    public Transform[] targets;
    public Transform[] returnAnchors;
    public Transform chestAnchor;
    public MinigameRewardChest chest;
    public float hintRadius = 12;
    public float snapRadius = 1.5f;
    public ConstellationState State { get; } = new ConstellationState();
    bool hintShown;
    void Start() { ResetRun(); MinigamePlayer.Find(gameObject.scene); }
    void Update()
    {
        if (hintShown || State.Solved) return;
        var player = MinigamePlayer.Find(gameObject.scene);
        if (!player || !player.CanAct || Vector3.Distance(player.transform.position, transform.position) > hintRadius) return;
        hintShown = true;
        player.Notify("Observe the seven-star positions. The guardian never left: align the two missing relay lamps.", 7);
    }
    public void RestoreRun(bool known)
    {
        ResetRun();if(!known)return;
        for(int i=0;i<2;i++){State.Place(i,i);movableLamps[i].SetPositionAndRotation(targets[i].position,targets[i].rotation);}
        State.TryClaim();enabled=false;
    }
    public int FindSlot(Vector3 position)
    {
        for (int i = 0; i < targets.Length; i++)
            if (!State.IsOccupied(i) && Vector3.Distance(position, targets[i].position) <= snapRadius) return i;
        return -1;
    }
    public bool Place(int lamp, int slot)
    {
        if (!State.Place(lamp, slot)) return false;
        movableLamps[lamp].SetPositionAndRotation(targets[slot].position, targets[slot].rotation);
        if (State.Solved) chest.Reveal(chestAnchor);
        else MinigamePlayer.Find(gameObject.scene)?.Notify("The orb settles into place. One star remains.");
        return true;
    }
    public void ReturnLamp(int index)
    {
        if (index < 0 || index >= movableLamps.Length || State.IsPlaced(index)) return;
        movableLamps[index].SetPositionAndRotation(returnAnchors[index].position, returnAnchors[index].rotation);
    }
    public void ResetRun()
    {
        var player = MinigamePlayer.Find(gameObject.scene);
        if (player) player.Release(this);
        enabled=true;State.Reset();
        hintShown = false;
        for (int i = 0; i < movableLamps.Length; i++) ReturnLamp(i);
        chest.Prepare(State);
    }
    void OnDisable() { var player = MinigamePlayer.Existing; if (player) player.Release(this); }
}
