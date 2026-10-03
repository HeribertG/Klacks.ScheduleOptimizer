// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Evaluator for an empty rule set: no findings, no veto, no allocation. This is what keeps every engine
/// byte-identical as long as no planning rule exists.
/// </summary>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed class NoOpPlanRuleEvaluator : IPlanRuleEvaluator
{
    private NoOpPlanRuleEvaluator()
    {
    }

    public static NoOpPlanRuleEvaluator Instance { get; } = new();

    public RuleEvaluation Evaluate(RulePlan plan) => RuleEvaluation.Empty;

    public bool WouldViolate(RulePlan plan, int agentIndex, int dayIndex, in RuleDay candidate) => false;
}
