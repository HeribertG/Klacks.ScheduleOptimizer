// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Check for PeriodCountRule with the counting semantics of the API CounterRuleEvaluator: per agent and per
/// calendar period touching the plan, the boundary carry-in plus the planned days are counted and a finding
/// is raised once the count reaches the threshold - one finding per period, anchored to the first plan day
/// of that period. The carry-in is aggregated once at construction.
/// </summary>
/// <param name="rule">The counter rule</param>
/// <param name="context">Evaluation context</param>
/// <param name="boundary">Boundary occupancy providing the carry-in</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal sealed class PeriodCountCheck : RuleCheck
{
    private readonly RuleCounterEvent _event;
    private readonly int _threshold;
    private readonly decimal? _thresholdMinutes;
    private readonly CalendarSlots _slots;
    private readonly int[] _carryIn;

    public PeriodCountCheck(PeriodCountRule rule, RuleEvaluationContext context, RuleBoundaryIndex boundary)
        : base(rule, context)
    {
        _event = rule.Event;
        _threshold = rule.Threshold;
        _thresholdMinutes = rule.HoursThreshold * RuleTimeConstants.MinutesPerHour;
        _slots = new CalendarSlots(context, rule.Period);
        _carryIn = new int[context.AgentCount * _slots.Count];
        foreach (var entry in boundary.Days)
        {
            var slot = _slots.SlotOfDate(entry.Key.Date);
            if (slot != CalendarSlots.NoSlot)
            {
                _carryIn[(entry.Key.AgentIndex * _slots.Count) + slot] += Events(entry.Value);
            }
        }
    }

    public override void Evaluate(RulePlan plan, RuleBoundaryIndex boundary, RuleFindingCollector collector)
    {
        for (var agent = 0; agent < plan.AgentCount; agent++)
        {
            if (AppliesTo(agent))
            {
                Scan(RuleTimeline.Of(plan, boundary, agent), collector);
            }
        }
    }

    public override decimal AgentExcess(in RuleTimeline timeline) => Scan(timeline, collector: null);

    private decimal Scan(in RuleTimeline timeline, RuleFindingCollector? collector)
    {
        var excess = 0m;
        for (var slot = 0; slot < _slots.Count; slot++)
        {
            var count = Count(timeline, timeline.AgentIndex, slot);
            if (count >= _threshold)
            {
                Report(collector, timeline.AgentIndex, _slots.FirstDay(slot), count, _threshold, count - _threshold + 1);
                excess += count - _threshold + 1;
            }
        }

        return excess;
    }

    public override bool WouldViolate(in RuleTimeline timeline, int dayIndex)
    {
        if (Events(timeline[dayIndex]) == 0)
        {
            return false;
        }

        return Count(timeline, timeline.AgentIndex, _slots.SlotOfDay(dayIndex)) >= _threshold;
    }

    private int Count(in RuleTimeline timeline, int agent, int slot)
    {
        var count = _carryIn[(agent * _slots.Count) + slot];
        var last = _slots.LastDay(slot);
        for (var day = _slots.FirstDay(slot); day <= last; day++)
        {
            count += Events(timeline[day]);
        }

        return count;
    }

    private int Events(in RuleDay day) => _event switch
    {
        RuleCounterEvent.NightShift => day.NightSegmentCount,
        RuleCounterEvent.WorkedDayInWeek => day.IsWork ? 1 : 0,
        RuleCounterEvent.ShiftExceedingHours => _thresholdMinutes.HasValue ? day.CountSegmentsLongerThan(_thresholdMinutes.Value) : 0,
        _ => 0,
    };
}
