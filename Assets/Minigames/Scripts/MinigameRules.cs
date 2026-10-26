using System;

public abstract class MinigameReceipt
{
    public abstract bool Solved { get; }
    public bool Claimed { get; protected set; }
    public bool TryClaim()
    {
        if (!Solved || Claimed) return false;
        Claimed = true;
        return true;
    }
}

public sealed class FlameCollectionState : MinigameReceipt
{
    readonly bool[] collected;
    public bool Active { get; private set; }
    public int Count { get; private set; }
    public int LastCollected { get; private set; } = -1;
    public override bool Solved => Count == collected.Length;
    public FlameCollectionState(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        collected = new bool[count];
    }
    public bool Activate()
    {
        if (Active) return false;
        Active = true;
        return true;
    }
    public bool Collect(int index)
    {
        if (!Active || index < 0 || index >= collected.Length || collected[index]) return false;
        collected[index] = true;
        Count++;
        LastCollected = index;
        return true;
    }
    public void Reset()
    {
        Array.Clear(collected, 0, collected.Length);
        Active = Claimed = false;
        Count = 0;
        LastCollected = -1;
    }
}

public sealed class ConstellationState : MinigameReceipt
{
    readonly int[] slots = { -1, -1 };
    public override bool Solved => slots[0] >= 0 && slots[1] >= 0;
    public bool IsPlaced(int lamp) => lamp >= 0 && lamp < 2 && Array.IndexOf(slots, lamp) >= 0;
    public bool IsOccupied(int slot) => slot >= 0 && slot < 2 && slots[slot] >= 0;
    public bool Place(int lamp, int slot)
    {
        if (lamp < 0 || lamp > 1 || slot < 0 || slot > 1 || IsPlaced(lamp) || IsOccupied(slot)) return false;
        slots[slot] = lamp;
        return true;
    }
    public void Reset() { slots[0] = slots[1] = -1; Claimed = false; }
}
