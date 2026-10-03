// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Check for RestAfterKindRule: the block of the kind ends on a day whose next day is not of the kind;
/// every worked day among the FreeDays days after it is excess. Reported at the block end. A break day is
/// a free day. Only rest days inside the period count: a block that ends before the period is a finding only
/// when a worked rest day falls into the period itself.
/// </summary>
/// <param name="rule">The rest rule</param>
/// <param name="context">Evaluation context</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal sealed class RestAfterKindCheck : RuleCheck
{
    private readonly RuleShiftKind _kind;
    private readonly int _freeDays;

    public RestAfterKindCheck(RestAfterKindRule rule, RuleEvaluationContext context)
        : base(rule, context)
    {
        _kind = rule.ShiftKind;
        _freeDays = Math.Max(0, rule.FreeDays);
    }

    public static int NeighborDays(RestAfterKindRule rule) => Math.Max(0, rule.FreeDays) + 1;

    public override void Evaluate(RulePlan plan, RuleBoundaryIndex boundary, RuleFindingCollector collector)
    {
        if (_freeDays == 0)
        {
            return;
        }

        for (var agent = 0; agent < plan.AgentCount; agent++)
        {
            if (!AppliesTo(agent))
            {
                continue;
            }

            var timeline = RuleTimeline.Of(plan, boundary, agent);
            for (var day = -_freeDays; day < plan.DayCount; day++)
            {
                var worked = WorkedDaysInRest(timeline, day);
                if (worked > 0)
                {
                    Report(collector, agent, day, worked, _freeDays, worked);
                }
            }
        }
    }

    public override bool WouldViolate(in RuleTimeline timeline, int dayIndex)
    {
        if (_freeDays == 0)
        {
            return false;
        }

        for (var blockEnd = dayIndex - _freeDays; blockEnd <= dayIndex; blockEnd++)
        {
            if (WorkedDaysInRest(timeline, blockEnd) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private int WorkedDaysInRest(in RuleTimeline timeline, int blockEnd)
    {
        if (!timeline[blockEnd].Has(_kind) || timeline[blockEnd + 1].Has(_kind))
        {
            return 0;
        }

        var worked = 0;
        for (var offset = Math.Max(1, -blockEnd); offset <= _freeDays; offset++)
        {
            if (timeline[blockEnd + offset].IsWork)
            {
                worked++;
            }
        }

        return worked;
    }
}
