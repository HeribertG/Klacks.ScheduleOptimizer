// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Forbids a day of kind To within WithinDays days after a day of kind From (e.g. no early shift on the
/// day after a night shift: From=Night, To=Early, WithinDays=1).
/// </summary>
/// <param name="FromKind">Kind of the earlier day</param>
/// <param name="ToKind">Kind that must not follow</param>
/// <param name="WithinDays">Number of following days the To kind is forbidden on (at least 1)</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record ForbiddenTransitionRule(
    Guid RuleId,
    RuleSeverity Severity,
    double Weight,
    RuleShiftKind FromKind,
    RuleShiftKind ToKind,
    int WithinDays,
    IReadOnlySet<string>? AgentScope = null) : PlanRule(RuleId, Severity, Weight, AgentScope)
{
    public override PlanRuleKind Kind => PlanRuleKind.ForbiddenTransition;
}
