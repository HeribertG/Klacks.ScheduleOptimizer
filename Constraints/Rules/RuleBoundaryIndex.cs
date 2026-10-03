// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Boundary occupancy prepared once per evaluator: every boundary segment aggregated per (agent, date),
/// plus two small dense grids holding the days just before and just after the period, as far as the
/// sequence rules of the rule set can look (NeighborDays). Days beyond the grids read as free.
/// </summary>
/// <param name="context">Evaluation context providing agents, period and boundary segments</param>
/// <param name="neighborDays">How many days before and after the period the sequence rules need</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal sealed class RuleBoundaryIndex
{
    private readonly RuleDay[] _before;
    private readonly RuleDay[] _after;

    public RuleBoundaryIndex(RuleEvaluationContext context, int neighborDays)
    {
        NeighborDays = Math.Max(0, neighborDays);
        Days = Aggregate(context);
        _before = new RuleDay[context.AgentCount * NeighborDays];
        _after = new RuleDay[context.AgentCount * NeighborDays];
        FillNeighborGrids(context);
    }

    public int NeighborDays { get; }

    public IReadOnlyDictionary<(int AgentIndex, DateOnly Date), RuleDay> Days { get; }

    public RuleDay Get(int agentIndex, int dayIndex, int dayCount)
    {
        if (dayIndex < 0)
        {
            var back = -dayIndex;
            return back <= NeighborDays ? _before[(agentIndex * NeighborDays) + back - 1] : RuleDay.Free;
        }

        var ahead = dayIndex - dayCount + 1;
        return ahead <= NeighborDays ? _after[(agentIndex * NeighborDays) + ahead - 1] : RuleDay.Free;
    }

    private static Dictionary<(int, DateOnly), RuleDay> Aggregate(RuleEvaluationContext context)
    {
        var days = new Dictionary<(int, DateOnly), RuleDay>();
        foreach (var segment in context.Boundary)
        {
            if (context.IsInPeriod(segment.Date) || !context.TryGetAgentIndex(segment.AgentId, out var agentIndex))
            {
                continue;
            }

            var key = (agentIndex, segment.Date);
            days.TryGetValue(key, out var day);
            days[key] = day.WithSegment(segment, context.Agents[agentIndex]);
        }

        return days;
    }

    private void FillNeighborGrids(RuleEvaluationContext context)
    {
        if (NeighborDays == 0)
        {
            return;
        }

        foreach (var entry in Days)
        {
            var (agentIndex, date) = entry.Key;
            var back = context.PeriodFrom.DayNumber - date.DayNumber;
            if (back >= 1 && back <= NeighborDays)
            {
                _before[(agentIndex * NeighborDays) + back - 1] = entry.Value;
                continue;
            }

            var ahead = date.DayNumber - context.PeriodUntil.DayNumber;
            if (ahead >= 1 && ahead <= NeighborDays)
            {
                _after[(agentIndex * NeighborDays) + ahead - 1] = entry.Value;
            }
        }
    }
}
