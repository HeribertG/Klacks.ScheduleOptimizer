// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Caps the length of a run of consecutive days of one kind (e.g. at most 3 night days in a row).
/// A run crossing the period edge is measured through the boundary days.
/// </summary>
/// <param name="ShiftKind">Day kind the run is made of</param>
/// <param name="MaxRun">Longest allowed run; a longer run is a violation with excess run - MaxRun</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record MaxConsecutiveOfKindRule(
    Guid RuleId,
    RuleSeverity Severity,
    double Weight,
    RuleShiftKind ShiftKind,
    int MaxRun,
    IReadOnlySet<string>? AgentScope = null) : PlanRule(RuleId, Severity, Weight, AgentScope)
{
    public override PlanRuleKind Kind => PlanRuleKind.MaxConsecutiveOfKind;
}
