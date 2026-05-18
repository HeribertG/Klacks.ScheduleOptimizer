// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Metrics;

/// <summary>
/// Computes a <see cref="WizardMetricsSnapshot"/> for a finished scenario. Pure function — given the
/// same scenario, context and escalation count it always returns the same vector.
/// </summary>
/// <param name="scenario">The CoreScenario produced by the wizard (with assigned tokens).</param>
/// <param name="context">The CoreWizardContext that was used to produce the scenario.</param>
/// <param name="stage1EscalationCount">Number of Stage-1 relaxations logged by the auctioneer.</param>
public static class WizardMetricsCalculator
{
    private const int ShiftTypeCount = 3;

    public static WizardMetricsSnapshot Compute(
        CoreScenario scenario,
        CoreWizardContext context,
        int stage1EscalationCount)
    {
        var tokens = scenario.Tokens;
        var totalSlots = context.Shifts.Sum(s => s.RequiredAssignments);
        var assignedSlots = tokens.Count(t => !string.IsNullOrEmpty(t.AgentId));
        var coverage = totalSlots == 0 ? 0.0 : (double)assignedSlots / totalSlots;

        var tokensByAgent = tokens
            .Where(t => !string.IsNullOrEmpty(t.AgentId))
            .GroupBy(t => t.AgentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var targetReached = ComputeTargetReached(context.Agents, tokensByAgent);
        var gini = ComputeSlotGini(context.Agents, tokensByAgent);
        var entropyAvg = ComputeShiftTypeEntropyAvg(context.Agents, tokensByAgent);
        var maxBlock = ComputeMaxConsecutiveBlockLength(tokensByAgent);

        return new WizardMetricsSnapshot(
            CoveragePercent: coverage,
            TargetReachedPercent: targetReached,
            SlotGini: gini,
            ShiftTypeEntropyAvg: entropyAvg,
            Stage1EscalationCount: stage1EscalationCount,
            MaxConsecutiveBlockLen: maxBlock);
    }

    private static double ComputeTargetReached(
        IReadOnlyList<CoreAgent> agents,
        Dictionary<string, List<CoreToken>> tokensByAgent)
    {
        if (agents.Count == 0)
        {
            return 0.0;
        }
        var reached = 0;
        foreach (var agent in agents)
        {
            if (agent.GuaranteedHours <= 0)
            {
                continue;
            }
            var combined = tokensByAgent.TryGetValue(agent.Id, out var ts)
                ? ts.Sum(t => (double)(t.TotalHours + t.Surcharges))
                : 0.0;
            if (combined >= agent.GuaranteedHours)
            {
                reached++;
            }
        }
        return (double)reached / agents.Count;
    }

    private static double ComputeSlotGini(
        IReadOnlyList<CoreAgent> agents,
        Dictionary<string, List<CoreToken>> tokensByAgent)
    {
        if (agents.Count == 0)
        {
            return 0.0;
        }
        var counts = agents
            .Select(a => tokensByAgent.TryGetValue(a.Id, out var ts) ? ts.Count : 0)
            .OrderBy(c => c)
            .ToArray();
        var total = counts.Sum();
        if (total == 0)
        {
            return 0.0;
        }
        double cumulative = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            cumulative += (2.0 * (i + 1) - counts.Length - 1) * counts[i];
        }
        return cumulative / (counts.Length * (double)total);
    }

    private static double ComputeShiftTypeEntropyAvg(
        IReadOnlyList<CoreAgent> agents,
        Dictionary<string, List<CoreToken>> tokensByAgent)
    {
        if (agents.Count == 0)
        {
            return 0.0;
        }
        double sum = 0;
        var counted = 0;
        foreach (var agent in agents)
        {
            if (!tokensByAgent.TryGetValue(agent.Id, out var ts) || ts.Count == 0)
            {
                continue;
            }
            var buckets = new int[ShiftTypeCount];
            foreach (var t in ts)
            {
                if (t.ShiftTypeIndex >= 0 && t.ShiftTypeIndex < ShiftTypeCount)
                {
                    buckets[t.ShiftTypeIndex]++;
                }
            }
            var total = (double)ts.Count;
            double entropy = 0;
            foreach (var bucket in buckets)
            {
                if (bucket == 0)
                {
                    continue;
                }
                var p = bucket / total;
                entropy -= p * Math.Log2(p);
            }
            sum += entropy;
            counted++;
        }
        return counted == 0 ? 0.0 : sum / counted;
    }

    private static int ComputeMaxConsecutiveBlockLength(Dictionary<string, List<CoreToken>> tokensByAgent)
    {
        var max = 0;
        foreach (var agentTokens in tokensByAgent.Values)
        {
            var dates = agentTokens
                .Select(t => t.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToArray();
            if (dates.Length == 0)
            {
                continue;
            }
            var current = 1;
            for (var i = 1; i < dates.Length; i++)
            {
                if (dates[i] == dates[i - 1].AddDays(1))
                {
                    current++;
                    if (current > max)
                    {
                        max = current;
                    }
                }
                else
                {
                    current = 1;
                }
            }
            if (dates.Length == 1 && max == 0)
            {
                max = 1;
            }
            else if (current > max)
            {
                max = current;
            }
        }
        return max;
    }
}
