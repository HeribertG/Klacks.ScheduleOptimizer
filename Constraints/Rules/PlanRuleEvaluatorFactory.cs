// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Entry point for engines: binds a rule set to an evaluation context. An empty (or null) rule set yields
/// the shared no-op evaluator, so callers never need a separate "no rules" branch.
/// </summary>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public static class PlanRuleEvaluatorFactory
{
    public static IPlanRuleEvaluator Create(IReadOnlyList<PlanRule>? rules, RuleEvaluationContext context)
        => CreateIncremental(rules, context);

    /// <summary>As <see cref="Create"/>, typed for engines that evaluate moves row by row.</summary>
    public static IIncrementalPlanRuleEvaluator CreateIncremental(IReadOnlyList<PlanRule>? rules, RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return rules is null || rules.Count == 0
            ? NoOpPlanRuleEvaluator.Instance
            : new PlanRuleEvaluator(rules, context);
    }
}
