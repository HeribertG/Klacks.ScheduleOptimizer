// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// One employee's assignments in the order the order-loyalty measure reads them, plus the incremental
/// arithmetic that answers what a single exchange at one position would cost. Swapping two assignments
/// of the same day and the same clock span leaves every sequence position untouched — only the order
/// value at that position changes — so the effect on the switch counters is a constant-time neighbour
/// comparison instead of a full rescore.
/// </summary>
internal sealed class AgentSequence
{
    private readonly List<CoreToken> _ordered;

    private AgentSequence(List<CoreToken> ordered)
    {
        _ordered = ordered;
    }

    /// <summary>Sequences of every employee that holds at least one assignment, in roster order.</summary>
    /// <param name="tokens">Assignments of the plan</param>
    /// <param name="rosterPosition">Roster index per employee, for a stable enumeration order</param>
    public static Dictionary<string, AgentSequence> BuildAll(
        IReadOnlyList<CoreToken> tokens, IReadOnlyDictionary<string, int> rosterPosition)
    {
        var byAgent = new Dictionary<string, List<CoreToken>>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (!byAgent.TryGetValue(token.AgentId, out var own))
            {
                own = [];
                byAgent[token.AgentId] = own;
            }

            own.Add(token);
        }

        var result = new Dictionary<string, AgentSequence>(byAgent.Count, StringComparer.Ordinal);
        foreach (var entry in byAgent.OrderBy(e => rosterPosition.GetValueOrDefault(e.Key, int.MaxValue))
                     .ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            entry.Value.Sort(static (left, right) => left.Date != right.Date
                ? left.Date.CompareTo(right.Date)
                : left.StartAt.CompareTo(right.StartAt));
            result[entry.Key] = new AgentSequence(entry.Value);
        }

        return result;
    }

    /// <summary>Order-loyalty counters over all given sequences.</summary>
    /// <param name="sequences">Sequences produced by <see cref="BuildAll"/></param>
    public static PairTotals CountPairs(IReadOnlyDictionary<string, AgentSequence> sequences)
    {
        var withinPairs = 0;
        var withinSwitches = 0;
        var acrossPairs = 0;
        var acrossSwitches = 0;

        foreach (var sequence in sequences.Values)
        {
            var ordered = sequence._ordered;
            for (var i = 1; i < ordered.Count; i++)
            {
                var changed = !string.Equals(
                    ordered[i].LocationContext, ordered[i - 1].LocationContext, StringComparison.Ordinal);

                if (LocationContinuity.IsSamePackage(ordered[i - 1], ordered[i]))
                {
                    withinPairs++;
                    withinSwitches += changed ? 1 : 0;
                }
                else
                {
                    acrossPairs++;
                    acrossSwitches += changed ? 1 : 0;
                }
            }
        }

        return new PairTotals(withinPairs, withinSwitches, acrossPairs, acrossSwitches);
    }

    /// <summary>
    /// Position of an assignment, or false when the exchange arithmetic would not be exact: an
    /// employee holding a second assignment that starts at the very same instant makes the sequence
    /// position of a replacement ambiguous, so such a candidate is skipped instead of estimated.
    /// </summary>
    /// <param name="token">Assignment to locate</param>
    /// <param name="position">Index inside the sequence</param>
    public bool TryPositionOf(CoreToken token, out int position)
    {
        position = -1;
        for (var i = 0; i < _ordered.Count; i++)
        {
            if (ReferenceEquals(_ordered[i], token))
            {
                position = i;
            }
            else if (_ordered[i].StartAt == token.StartAt)
            {
                position = -1;
                return false;
            }
        }

        return position >= 0;
    }

    /// <summary>
    /// Change in the switch counters when the order at <paramref name="position"/> becomes
    /// <paramref name="newLocation"/>, measured against both neighbours.
    /// </summary>
    /// <param name="position">Index inside the sequence</param>
    /// <param name="newLocation">Order the assignment would carry after the exchange</param>
    public SwitchDelta DeltaAt(int position, string? newLocation)
    {
        var oldLocation = _ordered[position].LocationContext;
        var delta = default(SwitchDelta);

        if (position > 0)
        {
            delta += DeltaOfPair(
                _ordered[position - 1], _ordered[position],
                _ordered[position - 1].LocationContext, oldLocation, newLocation);
        }

        if (position + 1 < _ordered.Count)
        {
            delta += DeltaOfPair(
                _ordered[position], _ordered[position + 1],
                _ordered[position + 1].LocationContext, oldLocation, newLocation);
        }

        return delta;
    }

    private static SwitchDelta DeltaOfPair(
        CoreToken earlier, CoreToken later, string? neighbour, string? oldLocation, string? newLocation)
    {
        var before = string.Equals(neighbour, oldLocation, StringComparison.Ordinal) ? 0 : 1;
        var after = string.Equals(neighbour, newLocation, StringComparison.Ordinal) ? 0 : 1;
        var change = after - before;

        return LocationContinuity.IsSamePackage(earlier, later)
            ? new SwitchDelta(change, 0)
            : new SwitchDelta(0, change);
    }
}
