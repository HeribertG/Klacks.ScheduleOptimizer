// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// One worked segment (a Work or a planned slot) as the rule evaluator counts it. Start/End are wall-clock
/// times anchored to Date, exactly like Work.StartTime/EndTime; End not after Start wraps midnight
/// (Start == End means 24 hours, as in CounterRuleEvaluator). Without clock times the duration comes from
/// Hours and night falls back to ShiftTypeIndex 2.
/// </summary>
/// <param name="AgentId">Owning agent</param>
/// <param name="Date">Calendar date the segment is anchored to (its start date)</param>
/// <param name="Start">Wall-clock start, null when unknown</param>
/// <param name="End">Wall-clock end, null when unknown</param>
/// <param name="ShiftTypeIndex">0=Early, 1=Late, 2=Night, -1=unknown</param>
/// <param name="Hours">Paid hours, used as duration only when clock times are missing</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public readonly record struct RuleSegment(
    string AgentId,
    DateOnly Date,
    TimeOnly? Start,
    TimeOnly? End,
    int ShiftTypeIndex,
    decimal Hours)
{
    public bool HasClockTimes => Start.HasValue && End.HasValue;
}
