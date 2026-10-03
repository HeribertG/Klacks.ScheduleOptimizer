// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Requires FreeDays free days after the last day of a block of one kind (e.g. two free days after a
/// night block). The block ends on a day of the kind whose next day is not of the kind; every worked
/// day in the following FreeDays days counts as excess. A break day counts as free.
/// </summary>
/// <param name="ShiftKind">Kind of the block</param>
/// <param name="FreeDays">Free days required after the block</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record RestAfterKindRule(
    Guid RuleId,
    RuleSeverity Severity,
    double Weight,
    RuleShiftKind ShiftKind,
    int FreeDays,
    IReadOnlySet<string>? AgentScope = null) : PlanRule(RuleId, Severity, Weight, AgentScope)
{
    public override PlanRuleKind Kind => PlanRuleKind.RestAfterKind;
}
