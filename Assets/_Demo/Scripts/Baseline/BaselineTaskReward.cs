using System;

// Immutable reward receipt. BaselineFlow handles the known recall-beacon item;
// other IDs remain receipts without inferred effects. Empty definitions grant nothing.
[Serializable]
public sealed class BaselineTaskReward
{
    public string itemId = "";
    public string title = "";
    public string description = "";
    public int quantity = 1;

    public bool IsConfigured => ValidText(itemId, 64, true) && ValidText(title, 100, true) &&
        ValidText(description, 400, false) && quantity > 0 && quantity <= 99;

    // Unity's inline serializer expands null array entries into default objects.
    public bool IsEmpty => string.IsNullOrEmpty(itemId) && string.IsNullOrEmpty(title) &&
        string.IsNullOrEmpty(description) && (quantity == 0 || quantity == 1);

    public BaselineTaskReward Snapshot() => new BaselineTaskReward
    {
        itemId = itemId, title = title, description = description, quantity = quantity
    };

    static bool ValidText(string value, int maximum, bool required) =>
        value != null && value.Length <= maximum && (!required || !string.IsNullOrWhiteSpace(value));
}
