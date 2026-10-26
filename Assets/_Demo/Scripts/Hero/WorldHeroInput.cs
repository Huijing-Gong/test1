namespace KingOfGraphic.Hero
{
    // Shared game-level input adapter. The reusable Hero assembly has no world/UI dependencies.
    public static class WorldHeroInput
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { HeroCombatInput.GameplayReader=Read; HeroCombatInput.BindingLabel=WorldInputBindings.Label; }
        public static HeroCombatInput Read(bool allowed)
        {
            if (!allowed || WorldInputBindings.GameplaySuppressed) return default;
            return new HeroCombatInput {
                attack=WorldInputBindings.Pressed("Attack"), held=WorldInputBindings.Held("Attack"),
                released=WorldInputBindings.Released("Attack"), toggleLock=false, run=WorldInputBindings.Held("Sprint"),
                guard=WorldInputBindings.Held("Guard"), guardPressed=WorldInputBindings.Pressed("Guard"),
                dodge=WorldInputBindings.Pressed("Dodge")
            };
        }
    }
}
