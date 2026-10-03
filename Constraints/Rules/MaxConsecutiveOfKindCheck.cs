// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Check for MaxConsecutiveOfKindRule: every maximal run of the kind that touches the period is measured
/// through the boundary days; a run longer than MaxRun is reported at its first day.
/// </summary>
/// <param name="rule">The run-length rule</param>
/// <param name="context">Evaluation context</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal sealed class MaxConsecutiveOfKindCheck : RuleCheck
{
    private readonly RuleShiftKind _kind;
    private readonly int _maxRun;

    public MaxConsecutiveOfKindCheck(MaxConsecutiveOfKindRule rule, RuleEvaluationContext context)
        : base(rule, context)
    {
        _kind = rule.ShiftKind;
        _maxRun = Math.Max(0, rule.MaxRun);
    }

    public static int NeighborDays(MaxConsecutiveOfKindRule rule) => Math.Max(0, rule.MaxRun);

    public override void Evaluate(RulePlan plan, RuleBoundaryIndex boundary, RuleFindingCollector collector)
    {
        var dayCount = plan.DayCount;
        for (var agent = 0; agent < plan.AgentCount; agent++)
        {
            if (!AppliesTo(agent))
            {
                continue;
            }

            var timeline = RuleTimeline.Of(plan, boundary, agent);
            var day = 0;
            while (day < dayCount)
            {
                if (!timeline[day].Has(_kind))
                {
                    day++;
                    continue;
                }

                var start = day;
                while (start > -boundary.NeighborDays && timeline[start - 1].Has(_kind))
                {
                    start--;
                }

                var end = day;
                while (end < dayCount - 1 + boundary.NeighborDays && timeline[end + 1].Has(_kind))
                {
                    end++;
                }

                var run = end - start + 1;
                if (run > _maxRun)
                {
                    Report(collector, agent, start, run, _maxRun, run - _maxRun);
                }

                day = end + 1;
            }
        }
    }

    public override bool WouldViolate(in RuleTimeline timeline, int dayIndex)
    {
        if (!timeline[dayIndex].Has(_kind))
        {
            return false;
        }

        var run = 1;
        for (var back = 1; back <= _maxRun && timeline[dayIndex - back].Has(_kind); back++)
        {
            run++;
        }

        for (var ahead = 1; run <= _maxRun && timeline[dayIndex + ahead].Has(_kind); ahead++)
        {
            run++;
        }

        return run > _maxRun;
    }
}
