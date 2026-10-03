// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Per-agent event counter over a calendar-anchored period with the exact CounterRule (K18) semantics:
/// NightShift counts segments overlapping the agent night window, WorkedDayInWeek counts distinct worked
/// dates, ShiftExceedingHours counts segments strictly longer than HoursThreshold; a finding is raised
/// once the count reaches Threshold, with excess count - Threshold + 1.
/// </summary>
/// <param name="Event">Counted event</param>
/// <param name="Period">Calendar period the count is anchored to</param>
/// <param name="Threshold">Count at which the rule fires</param>
/// <param name="HoursThreshold">Segment length in hours for ShiftExceedingHours; null counts nothing</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record PeriodCountRule(
    Guid RuleId,
    RuleSeverity Severity,
    double Weight,
    RuleCounterEvent Event,
    RuleCalendarPeriod Period,
    int Threshold,
    decimal? HoursThreshold = null,
    IReadOnlySet<string>? AgentScope = null) : PlanRule(RuleId, Severity, Weight, AgentScope)
{
    public override PlanRuleKind Kind => PlanRuleKind.PeriodCount;
}
