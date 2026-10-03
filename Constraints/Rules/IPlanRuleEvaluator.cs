// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>
/// Single evaluation contract for planning rules, shared by every engine and later by the API.
/// An instance is bound to one rule set and one RuleEvaluationContext; plans passed in must be built for
/// that same context.
/// </summary>
public interface IPlanRuleEvaluator
{
    /// <summary>Plan-wide evaluation: all findings, hard count and soft penalty.</summary>
    RuleEvaluation Evaluate(RulePlan plan);

    /// <summary>
    /// Slot-incremental hard check: true when the plan with <paramref name="candidate"/> placed as the
    /// complete cell (agentIndex, dayIndex) has a hard violation that involves that day. Soft rules never
    /// veto. Allocation-free.
    /// </summary>
    bool WouldViolate(RulePlan plan, int agentIndex, int dayIndex, in RuleDay candidate);
}
