using System;
using System.Collections.Generic;
using System.Linq;

namespace SamProduction.BossArena
{
    // Assertions over recorded public combat events, shared only by opt-in QA.
    // This verifies the requested complete executions and their interval; fixed endpoint geometry
    // and foot contact require their separate actual-path/sole analysis.
    internal static class ArenaDashReviewEvidence
    {
        public static void ValidateExecutions(IReadOnlyList<ArenaEventRecord> events, int required = 3)
        {
            var attacks = events.Where(e => e.kind == "boss_attack_started" && e.skill == "dash_punch").Take(required).ToArray();
            Need(attacks.Length == required, "All requested actual default dash executions are required.");
            var completed = new ArenaEventRecord[required];
            for (int i = 0; i < attacks.Length; i++)
            {
                long strike = attacks[i].strike;
                var rows = events.Where(e => e.strike == strike && e.skill == "dash_punch").ToArray();
                Need(!rows.Any(e => e.kind == "dash_abort" || e.kind == "dash_cancelled" || e.kind == "dash_abort_landing"), "Default execution aborted: " + strike);
                double previous = double.NegativeInfinity;
                foreach (string kind in new[] { "dash_charge_started", "dash_slide_started", "dash_heading_locked", "dash_strike_open", "dash_strike_closed", "boss_recovery_started", "dash_takeoff", "dash_landed", "dash_recovery_complete" })
                {
                    var matches = rows.Where(e => e.kind == kind).ToArray();
                    Need(matches.Length == 1, "Execution " + strike + " requires one " + kind + "; actual " + matches.Length);
                    Need(matches[0].time >= previous, "Execution event order is reversed: " + kind);
                    previous = matches[0].time;
                }
                var charge = rows.Single(e => e.kind == "dash_charge_started");
                var slide = rows.Single(e => e.kind == "dash_slide_started");
                Need(Math.Abs(slide.time - charge.time - 2) < .018, "Default charge did not consume its complete two seconds.");
                completed[i] = rows.Single(e => e.kind == "dash_recovery_complete");
            }
            var patrolStarts = events.Where(e => e.kind == "boss_patrol_started").ToArray();
            var patrolEnds = events.Where(e => e.kind == "boss_patrol_completed").ToArray();
            Need(patrolStarts.Length >= required && patrolEnds.Length >= required, "All requested complete movement intervals are required.");
            for (int i = 0; i < required; i++)
            {
                double duration = patrolEnds[i].time - patrolStarts[i].time;
                Need(duration >= 3.991 && duration <= 6.009, "Actual movement/return/facing duration: " + duration);
                Need(attacks[i].time >= patrolEnds[i].time && attacks[i].time - patrolEnds[i].time <= .05,
                    "Charge does not follow its completed movement interval.");
            }
            for(int i=1;i<required;i++)
                Need(patrolStarts[i].time >= completed[i-1].time && patrolStarts[i].time - completed[i-1].time <= .05,
                "Return/patrol must begin after the full first recovery, without an uncounted idle interval.");
        }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
