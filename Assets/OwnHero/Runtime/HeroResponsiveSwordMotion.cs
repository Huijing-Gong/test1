using UnityEngine;

namespace KingOfGraphic.Hero
{
    // Prototype timing, independent of the source animation's run-in/run-out.
    public static class HeroResponsiveSwordMotion
    {
        public const float DodgeDistance = 2.4f;
        public const float DodgeTravelDuration = .31f;

        public static float DodgeTravel(float time)
        {
            float u = Mathf.Clamp01(time / DodgeTravelDuration);
            return DodgeDistance * (1 - Mathf.Pow(1 - u, 2));
        }

        public static float DodgeStep(float time, float delta) =>
            Mathf.Max(0, DodgeTravel(time + Mathf.Max(0, delta)) - DodgeTravel(time));

        public static float AttackTravel(float time, float open, float close)
        {
            float u = Mathf.InverseLerp(Mathf.Max(0, open - .08f), close, time);
            return .38f * Mathf.SmoothStep(0, 1, u);
        }
    }
}
