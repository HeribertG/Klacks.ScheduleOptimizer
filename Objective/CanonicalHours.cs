// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Objective;

/// <summary>
/// The single canonical hours definition used by the composite objective: actual worked hours are
/// the sum of assignment <c>TotalHours</c> WITHOUT surcharges, plus paid break hours. Surcharge is
/// premium pay, not working time, so it never enters the numerator — this avoids the legacy
/// Stage1-vs-Stage2 inconsistency. The break-hours computation mirrors the Wizard-1 evaluator's
/// in-period clamp (per-day Break.WorkTime over the planning window) so both layers agree.
/// </summary>
public static class CanonicalHours
{
    /// <summary>Sum of canonical worked hours per agent (assignment TotalHours, no surcharges).</summary>
    public static Dictionary<string, double> WorkHoursByAgent(IReadOnlyList<AssignmentView> assignments)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var a in assignments)
        {
            result[a.AgentId] = result.TryGetValue(a.AgentId, out var existing)
                ? existing + (double)a.TotalHours
                : (double)a.TotalHours;
        }

        return result;
    }

    /// <summary>
    /// Sum of paid break hours per agent inside the planning window. Identical semantics to the
    /// Wizard-1 evaluator: each break blocker is clamped to [PeriodFrom, PeriodUntil] and contributes
    /// Break.WorkTime per day. Break hours count toward target hours but never toward weekly caps.
    /// </summary>
    public static Dictionary<string, double> BreakHoursByAgent(CoreWizardContext context)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var blocker in context.BreakBlockers)
        {
            if (blocker.Hours <= 0m)
            {
                continue;
            }

            var fromDate = blocker.FromInclusive < context.PeriodFrom ? context.PeriodFrom : blocker.FromInclusive;
            var untilDate = blocker.UntilInclusive > context.PeriodUntil ? context.PeriodUntil : blocker.UntilInclusive;
            if (untilDate < fromDate)
            {
                continue;
            }

            var dayCount = untilDate.DayNumber - fromDate.DayNumber + 1;
            var totalHours = (double)blocker.Hours * dayCount;
            result[blocker.AgentId] = result.TryGetValue(blocker.AgentId, out var existing)
                ? existing + totalHours
                : totalHours;
        }

        return result;
    }
}
