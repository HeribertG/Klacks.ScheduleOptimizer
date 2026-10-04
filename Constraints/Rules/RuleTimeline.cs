// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Read view on one agent row across boundary and plan, addressed by day index relative to the period
/// start (negative = before, at or beyond DayCount = after). One day can be overridden with a candidate, so
/// the slot-incremental check sees the plan as it would be after the placement without mutating it.
/// </summary>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal readonly ref struct RuleTimeline
{
    public const int NoOverride = int.MinValue;

    private readonly RulePlan _plan;
    private readonly RuleBoundaryIndex _boundary;
    private readonly int _agentIndex;
    private readonly int _overrideDay;
    private readonly RuleDay _override;

    public RuleTimeline(RulePlan plan, RuleBoundaryIndex boundary, int agentIndex, int overrideDay, RuleDay overrideValue)
    {
        _plan = plan;
        _boundary = boundary;
        _agentIndex = agentIndex;
        _overrideDay = overrideDay;
        _override = overrideValue;
    }

    public int AgentIndex => _agentIndex;

    public int DayCount => _plan.DayCount;

    public int NeighborDays => _boundary.NeighborDays;

    public RuleDay this[int dayIndex]
    {
        get
        {
            if (dayIndex == _overrideDay)
            {
                return _override;
            }

            return dayIndex >= 0 && dayIndex < _plan.DayCount
                ? _plan.Get(_agentIndex, dayIndex)
                : _boundary.Get(_agentIndex, dayIndex, _plan.DayCount);
        }
    }

    public static RuleTimeline Of(RulePlan plan, RuleBoundaryIndex boundary, int agentIndex)
        => new(plan, boundary, agentIndex, NoOverride, RuleDay.Free);
}
