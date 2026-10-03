// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Soft-only team fairness: within every window the spread (max - min) of the metric across the agents
/// of the scope must not exceed MaxSpread; the excess is penalised. Hard severity cannot be constructed
/// (owner decision 2026-10-03). With ProRata each value is divided by the agent workload share, and agents
/// with a workload of zero are left out of the comparison.
/// </summary>
/// <param name="Metric">Compared metric</param>
/// <param name="Window">Comparison window</param>
/// <param name="MaxSpread">Tolerated spread between the highest and lowest value</param>
/// <param name="ProRata">True scales each value to full time by the agent workload percent</param>
/// <param name="WeekendDays">Days counted as weekend for the WeekendDays metric; required, no country default</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record TeamFairnessRule(
    Guid RuleId,
    double Weight,
    FairnessMetric Metric,
    FairnessWindow Window,
    decimal MaxSpread,
    bool ProRata,
    IReadOnlySet<DayOfWeek> WeekendDays,
    IReadOnlySet<string>? AgentScope = null) : PlanRule(RuleId, RuleSeverity.Soft, Weight, AgentScope)
{
    public override PlanRuleKind Kind => PlanRuleKind.TeamFairness;
}
