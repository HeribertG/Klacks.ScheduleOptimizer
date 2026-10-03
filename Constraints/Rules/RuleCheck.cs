// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Strategy base for one planning rule. Resolves the agent scope to a per-row flag once, evaluates the
/// whole plan and - for hard rules - answers whether a single placement would create a violation that
/// involves the placed day.
/// </summary>
/// <param name="rule">The rule this check evaluates</param>
/// <param name="context">Evaluation context fixing agent rows and period</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal abstract class RuleCheck
{
    private readonly bool[] _inScope;

    protected RuleCheck(PlanRule rule, RuleEvaluationContext context)
    {
        Rule = rule;
        Context = context;
        _inScope = new bool[context.AgentCount];
        for (var i = 0; i < context.AgentCount; i++)
        {
            _inScope[i] = rule.AgentScope is null || rule.AgentScope.Contains(context.Agents[i].Id);
        }
    }

    public PlanRule Rule { get; }

    public virtual RuleSeverity Severity => Rule.Severity;

    public bool IsHard => Severity == RuleSeverity.Hard;

    protected RuleEvaluationContext Context { get; }

    public bool AppliesTo(int agentIndex) => _inScope[agentIndex];

    public abstract void Evaluate(RulePlan plan, RuleBoundaryIndex boundary, RuleFindingCollector collector);

    public virtual bool WouldViolate(in RuleTimeline timeline, int dayIndex) => false;

    protected void Report(RuleFindingCollector collector, int? agentIndex, int dayIndex, decimal observed, decimal limit, decimal excess)
    {
        var agentId = agentIndex.HasValue ? Context.Agents[agentIndex.Value].Id : null;
        collector.Add(Rule, Severity, agentId, Context.DateAt(dayIndex), observed, limit, excess);
    }
}
