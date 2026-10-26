using System;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Explicit managed checks; no Awake, Editor hook, auto-run, scene mutation,
    // synthetic health change or dependency on the Unity Test Framework.
    public static class ArenaGuardContracts
    {
        public static int RunPureLogicChecks()
        {
            int checks = 0;
            var config = ArenaGuardController.Settings.Default;
            var state = new ArenaGuardController.Logic();
            ArenaHit hit = Hit(0);

            Check(!state.ResolveHit(hit, Vector3.forward, config).Blocked, "Idle blocked a hit"); checks++;

            state.TickInput(true, true, true, .2f, config);
            var result = state.ResolveHit(hit, Vector3.forward, config);
            Check(result.Kind == ArenaGuardController.Outcome.Blocked && Near(result.Hit.damage, 35)
                && Near(result.Hit.push, 2.5f), "Ordinary guard multipliers differ");
            Check(result.Hit.strike == hit.strike && result.Hit.skill == hit.skill
                && result.Hit.point == hit.point && result.Hit.direction == hit.direction
                && hit.damage == 100 && hit.push == 10, "Guard changed identity or original hit"); checks++;

            state.Reset(); state.TickInput(true, true, true, .01f, config);
            result = state.ResolveHit(hit, Vector3.forward, config);
            Check(result.Perfect && result.Hit.damage == 0 && result.Hit.push == 0,
                "Fresh press did not perfect guard");
            Check(!state.ResolveHit(Hit(0, 43), Vector3.forward, config).Perfect,
                "One press spent more than one perfect guard"); checks++;

            state.Reset(); state.TickInput(true, true, true, .15f, config);
            Check(state.ResolveHit(hit, Vector3.forward, config).Perfect, "Window end is not inclusive");
            state.Reset(); state.TickInput(true, true, true, .1501f, config);
            Check(!state.ResolveHit(hit, Vector3.forward, config).Perfect, "Expired window remained open"); checks++;

            state.Reset(); state.TickInput(true, false, true, .01f, config);
            Check(!state.ResolveHit(hit, Vector3.forward, config).Perfect, "Held without pressed gained perfection"); checks++;

            state.Reset();
            for (int i = 0; i < 20; i++) state.TickInput(true, true, true, .01f, config);
            Check(!state.PerfectWindowOpen && !state.ResolveHit(hit, Vector3.forward, config).Perfect,
                "Repeated pressed flag renewed the window");
            state.TickInput(false, false, true, .01f, config);
            state.TickInput(true, true, true, .01f, config);
            Check(state.ResolveHit(hit, Vector3.forward, config).Perfect, "Release/fresh press did not rearm"); checks++;

            state.Reset(); state.TickInput(true, true, true, .01f, config);
            state.TickInput(true, false, false, .01f, config);
            Check(!state.IsGuarding, "Disallowed action left guard active");
            state.TickInput(true, true, true, .01f, config);
            Check(state.IsGuarding && !state.ResolveHit(hit, Vector3.forward, config).Perfect,
                "Control return while held renewed perfection");
            state.TickInput(false, false, true, .01f, config);
            state.TickInput(true, true, true, .01f, config);
            Check(state.ResolveHit(hit, Vector3.forward, config).Perfect, "Interrupt release did not rearm"); checks++;

            state.Reset(true); state.TickInput(true, true, true, .01f, config);
            Check(!state.ResolveHit(hit, Vector3.forward, config).Perfect, "Reset while held renewed perfection");
            state.Suspend();
            Check(!state.IsGuarding && !state.PerfectWindowOpen && state.GuardElapsed == 0,
                "Suspend leaked guard state");
            state.TickInput(true, true, true, .01f, config);
            Check(!state.ResolveHit(hit, Vector3.forward, config).Perfect, "Suspend needs release"); checks++;

            state.Reset(); state.TickInput(true, true, false, .01f, config);
            state.TickInput(true, false, true, .01f, config);
            Check(!state.ResolveHit(hit, Vector3.forward, config).Perfect, "Blocked press was buffered"); checks++;

            state.Reset(); state.TickInput(true, false, true, .01f, config);
            Check(state.ResolveHit(Hit(60), Vector3.forward, config).Blocked
                && state.ResolveHit(Hit(-60), Vector3.forward, config).Blocked
                && !state.ResolveHit(Hit(61), Vector3.forward, config).Blocked
                && !state.ResolveHit(Hit(-61), Vector3.forward, config).Blocked,
                "120 degree boundary differs"); checks++;

            state.Reset(); state.TickInput(true, true, true, .01f, config);
            Check(!state.ResolveHit(Hit(180), Vector3.forward, config).Blocked
                && !state.ResolveHit(hit, Vector3.forward, config, false).Blocked
                && state.ResolveHit(hit, Vector3.forward, config).Perfect,
                "Rear/unblockable hit spent the perfect opportunity"); checks++;

            ArenaHit unknown = hit; unknown.direction = Vector3.zero;
            Check(!state.ResolveHit(unknown, Vector3.forward, config).Blocked
                && !state.ResolveHit(hit, Vector3.up, config).Blocked,
                "Unknown planar direction was treated as frontal"); checks++;

            state.Reset(); state.TickInput(true, false, true, .01f, config);
            // Boss location is intentionally absent. A side-centred expanding
            // ring uses its actual propagation direction and cannot block front.
            Check(!state.ResolveHit(Hit(90), Vector3.forward, config).Blocked,
                "Side ring ignored the actual incoming direction"); checks++;

            var slow = new ArenaGuardController.Logic();
            var fast = new ArenaGuardController.Logic();
            for (int i = 0; i < 4; i++) slow.TickInput(true, i == 0, true, 1f / 30, config);
            for (int i = 0; i < 16; i++) fast.TickInput(true, i == 0, true, 1f / 120, config);
            Check(slow.PerfectWindowOpen && fast.PerfectWindowOpen, "30/120 Hz open window differs");
            slow.TickInput(true, false, true, 1f / 30, config);
            for (int i = 0; i < 4; i++) fast.TickInput(true, false, true, 1f / 120, config);
            Check(!slow.PerfectWindowOpen && !fast.PerfectWindowOpen, "30/120 Hz expiry differs"); checks++;

            var custom = config; custom.arcDegrees = 60; custom.damageMultiplier = .75f;
            custom.pushMultiplier = .5f; custom.perfectWindowSeconds = 0;
            state.Reset(); state.TickInput(true, true, true, .01f, custom);
            result = state.ResolveHit(hit, Vector3.forward, custom);
            Check(!result.Perfect && Near(result.Hit.damage, 75) && Near(result.Hit.push, 5)
                && !state.ResolveHit(Hit(31), Vector3.forward, custom).Blocked,
                "Configurable guard settings were ignored"); checks++;

            var invalid = config; invalid.arcDegrees = float.NaN;
            invalid.damageMultiplier = float.PositiveInfinity; invalid.pushMultiplier = float.NaN;
            invalid.perfectWindowSeconds = float.NaN;
            state.Reset(); state.TickInput(true, true, true, .2f, invalid);
            result = state.ResolveHit(hit, Vector3.forward, invalid);
            Check(Near(result.Hit.damage, 35) && Near(result.Hit.push, 2.5f), "Non-finite config leaked into damage");
            unknown = hit; unknown.direction = new Vector3(float.NaN, 0, 1);
            Check(!state.ResolveHit(unknown, Vector3.forward, config).Blocked, "Non-finite direction blocked"); checks++;

            bool rejected = false;
            try { state.TickInput(true, true, true, float.NaN, config); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Non-finite simulation delta accepted"); checks++;
            return checks;
        }

        private static ArenaHit Hit(float incomingDegrees, long strike = 42)
        {
            float angle = incomingDegrees * Mathf.Deg2Rad;
            return new ArenaHit { strike = strike, damage = 100, push = 10, skill = "dash_punch",
                point = new Vector3(1, 2, 3), direction = -new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) };
        }
        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= .00001f;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Arena guard contract: " + message);
        }
    }
}
