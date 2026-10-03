// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Dense, mutable agent x day grid of RuleDay aggregates for one plan inside the period of a
/// RuleEvaluationContext. Engines adapt onto it once and then mutate single cells, which keeps plan-wide
/// evaluation and the slot-incremental check free of dictionaries and string keys.
/// </summary>
/// <param name="context">Context that fixes the agent rows and the day columns</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed class RulePlan
{
    private readonly RuleDay[] _cells;

    public RulePlan(RuleEvaluationContext context)
    {
        Context = context;
        _cells = new RuleDay[context.AgentCount * context.DayCount];
    }

    public RuleEvaluationContext Context { get; }

    public int AgentCount => Context.AgentCount;

    public int DayCount => Context.DayCount;

    public RuleDay Get(int agentIndex, int dayIndex) => _cells[(agentIndex * Context.DayCount) + dayIndex];

    public void Set(int agentIndex, int dayIndex, in RuleDay day) => _cells[(agentIndex * Context.DayCount) + dayIndex] = day;

    /// <summary>Adds a segment to its cell; segments of unknown agents or outside the period are ignored.</summary>
    public bool TryAdd(in RuleSegment segment)
    {
        if (!Context.IsInPeriod(segment.Date) || !Context.TryGetAgentIndex(segment.AgentId, out var agentIndex))
        {
            return false;
        }

        var dayIndex = Context.DayIndexOf(segment.Date);
        Set(agentIndex, dayIndex, Get(agentIndex, dayIndex).WithSegment(segment, Context.Agents[agentIndex]));
        return true;
    }

    /// <summary>Returns the cell as it would be after adding the segment, without changing the plan.</summary>
    public RuleDay Preview(int agentIndex, int dayIndex, in RuleSegment segment)
        => Get(agentIndex, dayIndex).WithSegment(segment, Context.Agents[agentIndex]);
}
