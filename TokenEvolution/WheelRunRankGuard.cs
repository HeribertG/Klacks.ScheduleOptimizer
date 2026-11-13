// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Rank-order check for the wheel run of Wizard 1: the wheel plan may only replace the first run's plan when no lower
/// rank ends more than <see cref="ToleranceHours"/> above the lowest planned hours of all ranks above it, or when its
/// largest such excess is not larger than the first run's own. The tolerance is the step a plan of whole shifts moves
/// in, the same band the scenario assertion A6 of the autofill suite uses. Placeholders are left out.
/// </summary>
public static class WheelRunRankGuard
{
    /// <summary>Hours a lower rank may end above the weakest rank above it.</summary>
    public const double ToleranceHours = 8;

    /// <summary>True when the wheel plan keeps the rank order as defined above.</summary>
    /// <param name="wheel">End plan of the wheel run</param>
    /// <param name="first">End plan of the first run</param>
    /// <param name="context">Context whose agent order is the rank order</param>
    public static bool Accepts(CoreScenario wheel, CoreScenario first, CoreWizardContext context)
    {
        var excess = LargestExcess(wheel, context);
        return excess <= ToleranceHours || excess <= LargestExcess(first, context);
    }

    /// <summary>
    /// Largest number of planned hours by which a rank exceeds the lowest planned hours of all ranks above it (0 when
    /// none does), counting the plan's work inside the period.
    /// </summary>
    /// <param name="plan">Plan to measure</param>
    /// <param name="context">Context whose agent order is the rank order</param>
    public static double LargestExcess(CoreScenario plan, CoreWizardContext context)
    {
        var hours = plan.Tokens
            .Where(t => t.Date >= context.PeriodFrom && t.Date <= context.PeriodUntil)
            .GroupBy(t => t.AgentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (double)g.Sum(t => t.TotalHours), StringComparer.Ordinal);
        var largest = 0d;
        var lowestAbove = double.MaxValue;
        foreach (var agent in context.Agents.Where(a => !a.IsStandIn))
        {
            var planned = hours.GetValueOrDefault(agent.Id);
            if (lowestAbove != double.MaxValue)
            {
                largest = Math.Max(largest, planned - lowestAbove);
            }

            lowestAbove = Math.Min(lowestAbove, planned);
        }

        return largest;
    }
}
