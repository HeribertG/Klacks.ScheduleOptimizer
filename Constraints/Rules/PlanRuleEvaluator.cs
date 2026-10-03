// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Default <see cref="IPlanRuleEvaluator"/> for a non-empty rule set. Everything that does not depend on
/// the plan - agent scopes, calendar slots, boundary neighbour grids and PeriodCount carry-in - is
/// prepared once here, so Evaluate and WouldViolate only index dense arrays.
/// </summary>
/// <param name="rules">Non-empty rule set</param>
/// <param name="context">Evaluation context the plans must be built for</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed class PlanRuleEvaluator : IPlanRuleEvaluator
{
    private readonly RuleEvaluationContext _context;
    private readonly RuleBoundaryIndex _boundary;
    private readonly RuleCheck[] _checks;
    private readonly RuleCheck[] _hardChecks;

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
