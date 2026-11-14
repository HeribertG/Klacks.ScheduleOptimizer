// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.Objective;

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Rank-order check for the wheel run of Wizard 1. Every person is measured by the share of the guaranteed hours the plan
/// credits (hours before the period, planned hours with surcharges, absence hours inside the period; the Stage-1 rule of
/// the fitness), so part-time contracts and absences compare fairly. The wheel plan may only replace the first run's plan
/// when no lower rank is ahead of a higher rank by more than <see cref="ToleranceShifts"/> typical shift, counted in the
/// higher rank's hours, or when its largest such lead is not larger than the first run's own. The typical shift is the
/// median length of the period's shifts. Placeholders, people without guaranteed hours and people without shift work are
/// left out.
/// </summary>
public static class WheelRunRankGuard
{
    /// <summary>Typical shifts by which a lower rank may be ahead of a higher rank.</summary>
    public const double ToleranceShifts = 1;

    private const int MedianDivisor = 2;

    private const double ComparisonEpsilon = 1e-9;

    /// <summary>True when the wheel plan keeps the rank order as defined above.</summary>
    /// <param name="wheel">End plan of the wheel run</param>
    /// <param name="first">End plan of the first run</param>
    /// <param name="context">Context whose agent order is the rank order</param>
    public static bool Accepts(CoreScenario wheel, CoreScenario first, CoreWizardContext context)
    {
        var excess = LargestExcess(wheel, context);
        return excess <= ToleranceShifts + ComparisonEpsilon || excess <= LargestExcess(first, context) + ComparisonEpsilon;
    }

    /// <summary>
    /// Largest lead of a lower rank over a higher rank, in typical shifts of the higher rank: (credited share of the lower
    /// rank − credited share of the higher rank) × guaranteed hours of the higher rank / typical shift length. 0 when no
    /// lower rank is ahead or the period has no shifts.
    /// </summary>
    /// <param name="plan">Plan to measure</param>
    /// <param name="context">Context whose agent order is the rank order</param>
    public static double LargestExcess(CoreScenario plan, CoreWizardContext context)
    {
        var typicalShift = TypicalShiftHours(context);
        if (typicalShift <= 0)
        {
            return 0;
        }

        var planned = plan.Tokens
            .Where(t => t.Date >= context.PeriodFrom && t.Date <= context.PeriodUntil)
            .GroupBy(t => t.AgentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(t => (double)(t.TotalHours + t.Surcharges)), StringComparer.Ordinal);
        var absence = CanonicalHours.BreakHoursByAgent(context);
        var ranked = context.Agents
            .Where(a => !a.IsStandIn && a.PerformsShiftWork && a.GuaranteedHours > 0)
            .Select(a => (Target: a.GuaranteedHours,
                Share: (a.CurrentHours + planned.GetValueOrDefault(a.Id) + absence.GetValueOrDefault(a.Id)) / a.GuaranteedHours))
            .ToList();

        var largest = 0d;
        for (var lower = 1; lower < ranked.Count; lower++)
        {
            for (var higher = 0; higher < lower; higher++)
            {
                var lead = (ranked[lower].Share - ranked[higher].Share) * ranked[higher].Target / typicalShift;
                largest = Math.Max(largest, lead);
            }
        }

        return largest;
    }

    private static double TypicalShiftHours(CoreWizardContext context)
    {
        var lengths = context.Shifts.Where(s => s.Hours > 0).Select(s => s.Hours).Order().ToArray();
        if (lengths.Length == 0)
        {
            return 0;
        }

        var middle = lengths.Length / MedianDivisor;
        return lengths.Length % MedianDivisor == 1 ? lengths[middle] : (lengths[middle - 1] + lengths[middle]) / MedianDivisor;
    }
}
