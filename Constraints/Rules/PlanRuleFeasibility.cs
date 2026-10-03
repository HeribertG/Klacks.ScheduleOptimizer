// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Pre-flight feasibility of the hard sequence rules: for every demanded shift kind it computes an optimistic
/// upper bound of the agent-days of that kind the hard MaxConsecutiveOfKind and RestAfterKind rules allow
/// (rules of the kind itself and of Work), and reports the kind when even that bound misses the demand while the
/// available agent-days alone would cover it. The bound assumes every agent works only that kind and ignores the
/// boundary carry-in, both of which can only lower the real maximum, so an issue is never a false alarm; a plan
/// without an issue can still be infeasible. Per agent the bound is a small dynamic programme over (run length,
/// remaining rest days). ForbiddenTransition, PeriodCount and soft rules are not considered.
/// </summary>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public static class PlanRuleFeasibility
{
    private const int Unreachable = -1;

    /// <param name="rules">Rule set of the plan</param>
    /// <param name="context">Evaluation context fixing agents and days</param>
    /// <param name="demands">Required agents per day and kind</param>
    /// <param name="isAvailable">Whether agent (index) can work on day (index); null means always</param>
    public static IReadOnlyList<RuleFeasibilityIssue> Assess(
        IReadOnlyList<PlanRule> rules,
        RuleEvaluationContext context,
        IReadOnlyList<RuleFeasibilityDemand> demands,
        Func<int, int, bool>? isAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(demands);
        var available = isAvailable ?? ((_, _) => true);
        var issues = new List<RuleFeasibilityIssue>();
        foreach (var demand in demands)
        {
            var limiting = rules.Where(rule => rule.Severity == RuleSeverity.Hard && Limits(rule, demand.Kind)).ToList();
            if (limiting.Count == 0)
            {
                continue;
            }

            var required = demand.SlotsPerDay.Take(context.DayCount).Sum();
            var capacity = 0;
            var availableDays = 0;
            for (var agent = 0; agent < context.AgentCount; agent++)
            {
                var agentId = context.Agents[agent].Id;
                var applying = limiting.Where(rule => rule.AgentScope is null || rule.AgentScope.Contains(agentId)).ToList();
                var days = Enumerable.Range(0, context.DayCount).Select(day => available(agent, day)).ToArray();
                availableDays += days.Count(isFree => isFree);
                capacity += MaxKindDays(days, MaxRun(applying, context.DayCount), RestDays(applying));
            }

            if (capacity < required && availableDays >= required)
            {
                issues.Add(new RuleFeasibilityIssue(demand.Kind, limiting.Select(rule => rule.RuleId).ToList(), required, capacity, availableDays));
            }
        }

        return issues;
    }

    private static bool Limits(PlanRule rule, RuleShiftKind kind) => rule switch
    {
        MaxConsecutiveOfKindRule run => run.ShiftKind == kind || run.ShiftKind == RuleShiftKind.Work,
        RestAfterKindRule rest => rest.ShiftKind == kind || rest.ShiftKind == RuleShiftKind.Work,
        _ => false,
    };

    private static int MaxRun(IReadOnlyList<PlanRule> rules, int dayCount)
    {
        var maxRun = dayCount;
        foreach (var rule in rules.OfType<MaxConsecutiveOfKindRule>())
        {
            maxRun = Math.Min(maxRun, Math.Max(0, rule.MaxRun));
        }

        return maxRun;
    }

    private static int RestDays(IReadOnlyList<PlanRule> rules)
        => rules.OfType<RestAfterKindRule>().Select(rule => Math.Max(0, rule.FreeDays)).DefaultIfEmpty(0).Max();

    // best[run, rest]: most kind-days worked so far ending in a block of length run (0 = off) with rest
    // free days still owed. Working needs rest == 0 and run < maxRun; an off day after a block starts the rest.
    private static int MaxKindDays(bool[] available, int maxRun, int restDays)
    {
        var best = NewGrid(maxRun, restDays);
        best[0, 0] = 0;
        foreach (var canWork in available)
        {
            var next = NewGrid(maxRun, restDays);
            for (var run = 0; run <= maxRun; run++)
            {
                for (var rest = 0; rest <= restDays; rest++)
                {
                    var value = best[run, rest];
                    if (value == Unreachable)
                    {
                        continue;
                    }

                    var restAfterOff = run > 0 ? Math.Max(0, restDays - 1) : Math.Max(0, rest - 1);
                    next[0, restAfterOff] = Math.Max(next[0, restAfterOff], value);
                    if (canWork && rest == 0 && run < maxRun)
                    {
                        next[run + 1, 0] = Math.Max(next[run + 1, 0], value + 1);
                    }
                }
            }

            best = next;
        }

        var result = 0;
        foreach (var value in best)
        {
            result = Math.Max(result, value);
        }

        return result;
    }

    private static int[,] NewGrid(int maxRun, int restDays)
    {
        var grid = new int[maxRun + 1, restDays + 1];
        for (var run = 0; run <= maxRun; run++)
        {
            for (var rest = 0; rest <= restDays; rest++)
            {
                grid[run, rest] = Unreachable;
            }
        }

        return grid;
    }
}
