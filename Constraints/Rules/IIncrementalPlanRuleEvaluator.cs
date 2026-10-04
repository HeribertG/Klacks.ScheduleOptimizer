// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>
/// Per-agent decomposition of a plan-wide evaluation for engines that change one or two agent rows per move and
/// must not re-evaluate the whole plan. Every hard rule reports per agent, so the hard delta of a move only needs
/// the rows it touches; the soft penalty splits into a per-agent part plus the team-fairness part, which is
/// combined from per-agent window values. The sums equal what Evaluate reports for the same plan (soft up to
/// floating-point summation order).
/// </summary>
public interface IIncrementalPlanRuleEvaluator : IPlanRuleEvaluator
{
    /// <summary>Number of hard rules; the length <see cref="HardExcessOf"/> writes.</summary>
    int HardRuleCount { get; }

    /// <summary>True when at least one soft rule (per agent or team) exists.</summary>
    bool HasSoftRules { get; }

    /// <summary>Number of team-fairness windows over all team rules; the length of one agent's window values.</summary>
    int TeamWindowCount { get; }

    /// <summary>The hard rule at <paramref name="hardRuleIndex"/> (for diagnostics of a rejected move).</summary>
    PlanRule HardRuleAt(int hardRuleIndex);

    /// <summary>
    /// Writes, per hard rule, the summed Excess of the findings of one agent (0 when the rule does not apply to the
    /// agent) - the (rule, agent) unit the pre-commit delta compares. Allocation-free.
    /// </summary>
    void HardExcessOf(RulePlan plan, int agentIndex, Span<decimal> excessByHardRule);

    /// <summary>
    /// Soft penalty of the per-agent soft rules for one agent; also writes the agent's team-fairness window values
    /// (length <see cref="TeamWindowCount"/>).
    /// </summary>
    double AgentSoftPenalty(RulePlan plan, int agentIndex, Span<decimal> teamWindowValues);

    /// <summary>Soft penalty of the team-fairness rules from the window values of every agent row (index = agent index).</summary>
    double TeamSoftPenalty(IReadOnlyList<decimal[]> teamWindowValuesByAgent);
}
