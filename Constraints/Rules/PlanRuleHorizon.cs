// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// How far outside the planning period a rule set looks, so a loader can fetch exactly the boundary
/// segments the evaluator reads: the sequence rules need NeighborDays days on both sides (the same value
/// <see cref="PlanRuleEvaluator"/> sizes its neighbour grids with), a PeriodCountRule needs the rest of every
/// calendar week, month or year touching the period (its carry-in, before AND after the period, because the
/// count covers the whole calendar period). TeamFairness windows are clipped to the period and need nothing.
/// </summary>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public static class PlanRuleHorizon
{
    public static int NeighborDays(IReadOnlyList<PlanRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var neighborDays = 0;
        foreach (var rule in rules)
        {
            neighborDays = Math.Max(neighborDays, NeighborDays(rule));
        }

        return neighborDays;
    }

    /// <summary>
    /// Outermost days (inclusive) the rule set reads. Equal to the period itself when no rule looks outside.
    /// </summary>
    public static (DateOnly From, DateOnly Until) BoundaryWindow(IReadOnlyList<PlanRule> rules, DateOnly periodFrom, DateOnly periodUntil)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var neighborDays = NeighborDays(rules);
        var from = periodFrom.AddDays(-neighborDays);
        var until = periodUntil.AddDays(neighborDays);
        foreach (var rule in rules)
        {
            if (rule is PeriodCountRule count)
            {
                var periodStart = RuleCalendar.PeriodStart(count.Period, periodFrom);
                var periodEnd = RuleCalendar.PeriodEnd(count.Period, periodUntil);
                from = periodStart < from ? periodStart : from;
                until = periodEnd > until ? periodEnd : until;
            }
        }

        return (from, until);
    }

    private static int NeighborDays(PlanRule rule) => rule switch
    {
        MaxConsecutiveOfKindRule run => MaxConsecutiveOfKindCheck.NeighborDays(run),
        ForbiddenTransitionRule transition => ForbiddenTransitionCheck.NeighborDays(transition),
        RestAfterKindRule rest => RestAfterKindCheck.NeighborDays(rest),
        _ => 0,
    };
}
