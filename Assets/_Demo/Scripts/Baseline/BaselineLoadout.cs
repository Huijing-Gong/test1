using UnityEngine;

// Small configurable greybox effects. No random rolls, inventory grid or shared prefab edits.
public sealed class BaselineLoadout : MonoBehaviour
{
    public BaselineWorld world;
    public BaselineFlow flow;
    // Preserve the serialized array layout. Ridge charm now aids travel.
    public int[] relicDamageBonuses = { 2, 3, 4 };
    public int[] trialDamageBonuses = { 6, 6 };
    const int RidgeCharmIndex = 1;
    const float RidgeSprintMultiplier = .7f;
    public int RelicBonus(int index) => index == RidgeCharmIndex ? 0 : Value(relicDamageBonuses,index);
    public int TrialBonus(int index) => Value(trialDamageBonuses,index);
    static readonly string[] Names = { "Island relic", "Ridge charm", "Runic guard", "Trial A blade core", "B gate sigil (placeholder)" };
    static readonly string[] Descriptions = {
        "An island-forged relic that strengthens every sword strike.",
        "A mountain charm carried down from the western ridge.",
        "An ancient guard etched with a protective rune.",
        "A blade core awarded for completing Trial A.",
        "A sigil recovered beyond the Side World B gate." };
    static readonly string[] Sources = {
        "Island hidden chest", "Western hidden chest", "Sealed eastern chest", "Trial A", "Side World B" };
    public string EquipmentName(int index) => index >= 0 && index < Names.Length ? Names[index] : "Unknown";
    public string EquipmentDescription(int index) => index >= 0 && index < Descriptions.Length ? Descriptions[index] : "Unknown equipment.";
    public string EquipmentSource(int index) => index >= 0 && index < Sources.Length ? Sources[index] : "Unknown source";
    public int EquipmentBonus(int index) => index < 3 ? RelicBonus(index) : TrialBonus(index - 3);
    public string EquipmentEffect(int index) => index == RidgeCharmIndex
        ? "Sprinting uses 30% less stamina in the main world"
        : "Sword damage +" + EquipmentBonus(index);
    public float SprintDrainMultiplier => IsEquipped(RidgeCharmIndex) ? RidgeSprintMultiplier : 1f;
    public string EffectSummary => "Sword damage +" + DamageBonus +
        (IsEquipped(RidgeCharmIndex) ? " | Sprint stamina -30%" : "");
    public bool OwnsEquipment(int index) => world && flow && flow.Started && index >= 0 && index < 5 &&
        (index < 3 ? world.opened[index] : flow.Progress.trialRewardClaimed[index - 3]);
    public bool IsEquipped(int index) => OwnsEquipment(index) && (flow.Progress.unequippedMask & (1 << index)) == 0;
    static int Value(int[] values,int index) => values!=null && index>=0 && index<values.Length?Mathf.Max(0,values[index]):0;
    public int DamageBonus
    {
        get
        {
            if (!world || !flow || !flow.Started) return 0;
            int bonus=0;
            for(int i=0;i<5;i++) if(IsEquipped(i)) bonus+=EquipmentBonus(i);
            return bonus;
        }
    }
}
