// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// One rule violation. Carries only ids and enums - never display names - so it can be logged, stored
/// and sent to an LLM without leaking personal data. Observed/Limit are rule-specific: run length vs.
/// MaxRun, day gap vs. WithinDays, worked days in the rest window vs. FreeDays, count vs. Threshold,
/// spread vs. MaxSpread.
/// </summary>
/// <param name="RuleId">Violated rule</param>
/// <param name="Kind">Rule family</param>
/// <param name="Severity">Effective severity</param>
/// <param name="AgentId">Affected agent; null for team-wide findings</param>
/// <param name="Date">Anchor date of the violation</param>
/// <param name="Observed">Observed value</param>
/// <param name="Limit">Configured limit</param>
/// <param name="Excess">Amount by which the finding exceeds the limit (the unit the soft penalty is weighted with)</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record RuleFinding(
    Guid RuleId,
    PlanRuleKind Kind,
    RuleSeverity Severity,
    string? AgentId,
    DateOnly Date,
    decimal Observed,
    decimal Limit,
    decimal Excess = 0m);
