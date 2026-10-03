// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Static input of a rule evaluation: the planning period, the agents (index = row of every RulePlan built
/// for this context) and the boundary occupancy. Boundary holds every known worked segment OUTSIDE the
/// period - the neighbouring days for the sequence rules and, for PeriodCount, the carry-in of the rest of
/// the counted week, month or year. Segments inside the period, of unknown agents or breaks never belong
/// here: breaks count as free, in-period work is the plan itself.
/// </summary>
/// <param name="periodFrom">First planned day (inclusive)</param>
/// <param name="periodUntil">Last planned day (inclusive)</param>
/// <param name="agents">Agents in plan row order</param>
/// <param name="boundary">Worked segments outside the period</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed class RuleEvaluationContext
{
    private readonly Dictionary<string, int> _agentIndex;

    public RuleEvaluationContext(
        DateOnly periodFrom,
        DateOnly periodUntil,
        IReadOnlyList<RuleAgent> agents,
        IReadOnlyList<RuleSegment> boundary)
    {
        if (periodUntil < periodFrom)
        {
            throw new ArgumentException("The period end must not be before its start.", nameof(periodUntil));
        }

        PeriodFrom = periodFrom;
        PeriodUntil = periodUntil;
        Agents = agents;
        Boundary = boundary;
        DayCount = periodUntil.DayNumber - periodFrom.DayNumber + 1;
        _agentIndex = new Dictionary<string, int>(agents.Count, StringComparer.Ordinal);
        for (var i = 0; i < agents.Count; i++)
        {
            _agentIndex.TryAdd(agents[i].Id, i);
        }
    }

    public DateOnly PeriodFrom { get; }

    public DateOnly PeriodUntil { get; }

    public IReadOnlyList<RuleAgent> Agents { get; }

    public IReadOnlyList<RuleSegment> Boundary { get; }

    public int DayCount { get; }

    public int AgentCount => Agents.Count;

    public bool TryGetAgentIndex(string agentId, out int index) => _agentIndex.TryGetValue(agentId, out index);

    public int DayIndexOf(DateOnly date) => date.DayNumber - PeriodFrom.DayNumber;

    public DateOnly DateAt(int dayIndex) => PeriodFrom.AddDays(dayIndex);

    public bool IsInPeriod(DateOnly date) => date >= PeriodFrom && date <= PeriodUntil;
}
