// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Check for ForbiddenTransitionRule: a day of the From kind followed within WithinDays days by a day of
/// the To kind. Pairs crossing the period edge are seen through the boundary days; each From day is
/// reported once, at its own date, with the day gap of the first offending To day.
/// </summary>
/// <param name="rule">The transition rule</param>
/// <param name="context">Evaluation context</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal sealed class ForbiddenTransitionCheck : RuleCheck
{
    private const int MinimumWithinDays = 1;
    private const decimal ExcessPerTransition = 1m;

    private readonly RuleShiftKind _from;
    private readonly RuleShiftKind _to;
    private readonly int _withinDays;

    public ForbiddenTransitionCheck(ForbiddenTransitionRule rule, RuleEvaluationContext context)
        : base(rule, context)
    {
        _from = rule.FromKind;
        _to = rule.ToKind;
        _withinDays = NeighborDays(rule);
    }

    public static int NeighborDays(ForbiddenTransitionRule rule) => Math.Max(MinimumWithinDays, rule.WithinDays);

    public override void Evaluate(RulePlan plan, RuleBoundaryIndex boundary, RuleFindingCollector collector)
    {
        for (var agent = 0; agent < plan.AgentCount; agent++)
        {
            if (!AppliesTo(agent))
            {
                continue;
            }

            var timeline = RuleTimeline.Of(plan, boundary, agent);
            for (var day = -_withinDays; day < plan.DayCount; day++)
            {
                if (!timeline[day].Has(_from))
                {
                    continue;
                }

                for (var gap = 1; gap <= _withinDays; gap++)
                {
                    var next = day + gap;
                    if (next >= 0 && timeline[next].Has(_to))
                    {
                        Report(collector, agent, day, gap, _withinDays, ExcessPerTransition);
                        break;
                    }
                }
            }
        }
    }

    public override bool WouldViolate(in RuleTimeline timeline, int dayIndex)
    {
        var candidate = timeline[dayIndex];
        if (candidate.Has(_from))
        {
            for (var gap = 1; gap <= _withinDays; gap++)
            {
                if (timeline[dayIndex + gap].Has(_to))
                {
                    return true;
                }
            }
        }

        if (candidate.Has(_to))
        {
            for (var gap = 1; gap <= _withinDays; gap++)
            {
                if (timeline[dayIndex - gap].Has(_from))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
