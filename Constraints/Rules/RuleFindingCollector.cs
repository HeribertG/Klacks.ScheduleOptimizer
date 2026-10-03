// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Accumulates findings, hard count and soft penalty of one plan-wide evaluation. The findings list is
/// created on the first finding only, so a clean plan returns the shared empty evaluation.
/// </summary>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal sealed class RuleFindingCollector
{
    private List<RuleFinding>? _findings;
    private int _hardCount;
    private double _softPenalty;

    public void Add(PlanRule rule, RuleSeverity severity, string? agentId, DateOnly date, decimal observed, decimal limit, decimal excess)
    {
        _findings ??= [];
        _findings.Add(new RuleFinding(rule.RuleId, rule.Kind, severity, agentId, date, observed, limit, excess));
        if (severity == RuleSeverity.Hard)
        {
            _hardCount++;
            return;
        }

        _softPenalty += rule.Weight * (double)excess;
    }

    public RuleEvaluation ToEvaluation()
        => _findings is null ? RuleEvaluation.Empty : new RuleEvaluation(_findings, _hardCount, _softPenalty);
}
