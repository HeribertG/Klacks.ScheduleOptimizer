// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Result of a plan-wide rule evaluation. Empty is a shared instance, so a plan without findings (and an
/// empty rule set) costs no allocation.
/// </summary>
/// <param name="Findings">All findings in rule order</param>
/// <param name="HardCount">Number of hard findings</param>
/// <param name="SoftPenalty">Sum of Weight times excess over the soft findings</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record RuleEvaluation(IReadOnlyList<RuleFinding> Findings, int HardCount, double SoftPenalty)
{
    public static RuleEvaluation Empty { get; } = new([], 0, 0d);
}
