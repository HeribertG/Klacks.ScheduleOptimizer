// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Default <see cref="IPlanRuleEvaluator"/> for a non-empty rule set. Everything that does not depend on
/// the plan - agent scopes, calendar slots, boundary neighbour grids and PeriodCount carry-in - is
/// prepared once here, so Evaluate and WouldViolate only index dense arrays.
/// </summary>
/// <param name="rules">Non-empty rule set</param>
/// <param name="context">Evaluation context the plans must be built for</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed class PlanRuleEvaluator : IIncrementalPlanRuleEvaluator
{
    private readonly RuleEvaluationContext _context;
    private readonly RuleBoundaryIndex _boundary;
    private readonly RuleCheck[] _checks;
    private readonly RuleCheck[] _hardChecks;
    private readonly RuleCheck[] _softAgentChecks;
    private readonly TeamFairnessCheck[] _teamChecks;

    internal PlanRuleEvaluator(IReadOnlyList<PlanRule> rules, RuleEvaluationContext context)
    {
        _context = context;
        var neighborDays = 0;
        foreach (var rule in rules)
        {
            neighborDays = Math.Max(neighborDays, NeighborDays(rule));
        }

        _boundary = new RuleBoundaryIndex(context, neighborDays);
        _checks = new RuleCheck[rules.Count];
        for (var i = 0; i < rules.Count; i++)
        {
            _checks[i] = CreateCheck(rules[i], context, _boundary);
        }

        _hardChecks = Array.FindAll(_checks, check => check.IsHard);
        _softAgentChecks = Array.FindAll(_checks, check => !check.IsHard && check.IsPerAgent);
        _teamChecks = _checks.OfType<TeamFairnessCheck>().ToArray();
        TeamWindowCount = _teamChecks.Sum(check => check.WindowCount);
    }

    public int HardRuleCount => _hardChecks.Length;

    public bool HasSoftRules => _softAgentChecks.Length > 0 || _teamChecks.Length > 0;

    public int TeamWindowCount { get; }

    public PlanRule HardRuleAt(int hardRuleIndex) => _hardChecks[hardRuleIndex].Rule;

    public void HardExcessOf(RulePlan plan, int agentIndex, Span<decimal> excessByHardRule)
    {
        EnsureSameContext(plan);
        var timeline = RuleTimeline.Of(plan, _boundary, agentIndex);
        for (var i = 0; i < _hardChecks.Length; i++)
        {
            excessByHardRule[i] = _hardChecks[i].AppliesTo(agentIndex) ? _hardChecks[i].AgentExcess(timeline) : 0m;
        }
    }

    public double AgentSoftPenalty(RulePlan plan, int agentIndex, Span<decimal> teamWindowValues)
    {
        EnsureSameContext(plan);
        var timeline = RuleTimeline.Of(plan, _boundary, agentIndex);
        var penalty = 0d;
        foreach (var check in _softAgentChecks)
        {
            if (check.AppliesTo(agentIndex))
            {
                penalty += check.Rule.Weight * (double)check.AgentExcess(timeline);
            }
        }

        var offset = 0;
        foreach (var check in _teamChecks)
        {
            for (var window = 0; window < check.WindowCount; window++)
            {
                teamWindowValues[offset + window] = check.WindowValue(plan, agentIndex, window);
            }

            offset += check.WindowCount;
        }

        return penalty;
    }

    public double TeamSoftPenalty(IReadOnlyList<decimal[]> teamWindowValuesByAgent)
    {
        var penalty = 0d;
        var offset = 0;
        foreach (var check in _teamChecks)
        {
            penalty += check.PenaltyFromValues(teamWindowValuesByAgent, offset);
            offset += check.WindowCount;
        }

        return penalty;
    }

    public RuleEvaluation Evaluate(RulePlan plan)
    {
        EnsureSameContext(plan);
        var collector = new RuleFindingCollector();
        foreach (var check in _checks)
        {
            check.Evaluate(plan, _boundary, collector);
        }

        return collector.ToEvaluation();
    }

    public bool WouldViolate(RulePlan plan, int agentIndex, int dayIndex, in RuleDay candidate)
    {
        EnsureSameContext(plan);
        var timeline = new RuleTimeline(plan, _boundary, agentIndex, dayIndex, candidate);
        foreach (var check in _hardChecks)
        {
            if (check.AppliesTo(agentIndex) && check.WouldViolate(timeline, dayIndex))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureSameContext(RulePlan plan)
    {
        if (!ReferenceEquals(plan.Context, _context))
        {
            throw new ArgumentException("The plan was built for a different rule evaluation context.", nameof(plan));
        }
    }

    private static int NeighborDays(PlanRule rule) => rule switch
    {
        MaxConsecutiveOfKindRule run => MaxConsecutiveOfKindCheck.NeighborDays(run),
        ForbiddenTransitionRule transition => ForbiddenTransitionCheck.NeighborDays(transition),
        RestAfterKindRule rest => RestAfterKindCheck.NeighborDays(rest),
        _ => 0,
    };

    private static RuleCheck CreateCheck(PlanRule rule, RuleEvaluationContext context, RuleBoundaryIndex boundary) => rule switch
    {
        MaxConsecutiveOfKindRule run => new MaxConsecutiveOfKindCheck(run, context),
        ForbiddenTransitionRule transition => new ForbiddenTransitionCheck(transition, context),
        RestAfterKindRule rest => new RestAfterKindCheck(rest, context),
        PeriodCountRule count => new PeriodCountCheck(count, context, boundary),
        TeamFairnessRule fairness => new TeamFairnessCheck(fairness, context),
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Kind, "Unknown planning rule type."),
    };
}
