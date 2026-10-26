using UnityEngine;

// Session-only presentation options: no reward/save schema changes.
public static class BattlePresentationSettings
{
    public static bool ReducedMotion {get;set;}
    public static bool FlashEnabled {get;set;}=true;
    public static bool ContextHintsEnabled {get;set;}=true;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset(){ReducedMotion=false;FlashEnabled=true;ContextHintsEnabled=true;}
}
