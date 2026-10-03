// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Base of every engine-neutral planning rule. Rules are pure data; the evaluator maps each record onto
/// its check strategy. AgentScope restricts the rule to a set of agent ids (null = every agent of the
/// evaluation context); the loader resolves group or industry scoping into this set.
/// </summary>
/// <param name="RuleId">Identifier of the source row (CounterRule or PlanningConstraint)</param>
/// <param name="Severity">Hard vetoes, Soft only adds Weight times the excess to the penalty</param>
/// <param name="Weight">Penalty weight per unit of excess for soft rules</param>
/// <param name="AgentScope">Agent ids the rule applies to; null applies it to every agent</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public abstract record PlanRule(Guid RuleId, RuleSeverity Severity, double Weight, IReadOnlySet<string>? AgentScope)
{
    public abstract PlanRuleKind Kind { get; }
}
