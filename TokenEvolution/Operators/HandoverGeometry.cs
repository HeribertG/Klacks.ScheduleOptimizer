// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// The geometry both hour-moving passes share: which day of a donor may lose a shift, how badly a
/// shift damages the receiver's package, how long the block around a day is, and which days an open
/// carried-in package still owes. Extracted so the top-down handover and the surplus return answer
/// these questions identically — two passes that move the same tokens under two different rules must
/// not drift apart in what they consider legal geometry.
/// </summary>
internal static class HandoverGeometry
{
    /// <summary>Penalty for a shift that starts an isolated new block for the receiver instead of extending one.</summary>
    internal const int NewBlockPenalty = 1;

    /// <summary>Penalty for a shift whose kind differs from the neighbouring day of the receiver's block.</summary>
    internal const int MixedKindPenalty = 1;

    /// <summary>
    /// Hours per agent including surcharges and the hours already worked in the period, so the two
    /// passes measure the guaranteed-hours target against the same account.
    /// </summary>
    /// <param name="tokens">Plan whose assignments are summed</param>
    /// <param name="context">Wizard context supplying the roster and the hours already worked</param>
    internal static Dictionary<string, double> BuildHours(
        IReadOnlyList<CoreToken> tokens, CoreWizardContext context)
    {
        var hours = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            hours[agent.Id] = agent.CurrentHours;
        }

        foreach (var token in tokens)
        {
            hours[token.AgentId] = hours.GetValueOrDefault(token.AgentId, 0)
                + (double)(token.TotalHours + token.Surcharges);
        }

        return hours;
    }

    /// <summary>
    /// How badly a shift would damage the receiver's package structure: it costs when it opens an
    /// isolated new block, and it costs when it extends a block with a foreign shift kind.
    /// </summary>
    /// <param name="receiverDays">Shift kind per worked day of the receiver</param>
    /// <param name="token">Shift the receiver would take over</param>
    internal static int ReceiverPenalty(IReadOnlyDictionary<DateOnly, int> receiverDays, CoreToken token)
    {
        var hasPrevious = receiverDays.TryGetValue(token.Date.AddDays(-1), out var previousKind);
        var hasNext = receiverDays.TryGetValue(token.Date.AddDays(1), out var nextKind);

        if (!hasPrevious && !hasNext)
        {
            return NewBlockPenalty + MixedKindPenalty;
        }

        var matches = (hasPrevious && previousKind == token.ShiftTypeIndex)
            || (hasNext && nextKind == token.ShiftTypeIndex);

        return matches ? 0 : MixedKindPenalty;
    }

    /// <summary>
    /// True when the donor may lose this shift without breaking its own block structure: either the
    /// donor keeps another shift on that day, or the day sits at the edge of its work block. Releasing
    /// a day enclosed by two worked days splits the block and leaves a gap of exactly one free day,
    /// which is below MinRestDays for every contract that asks for two. The rule is deliberately
    /// conservative for a contract with MinRestDays of one or zero: such a split would be legal there,
    /// and neither pass uses it.
    /// </summary>
    /// <param name="donorDays">Number of shifts the donor holds per calendar day</param>
    /// <param name="date">Day the donor would give away</param>
    internal static bool MayRelease(IReadOnlyDictionary<DateOnly, int> donorDays, DateOnly date)
    {
        if (donorDays.GetValueOrDefault(date, 0) > 1)
        {
            return true;
        }

        return !donorDays.ContainsKey(date.AddDays(-1)) || !donorDays.ContainsKey(date.AddDays(1));
    }

    /// <summary>Length in days of the uninterrupted work block that contains the given day.</summary>
    /// <param name="occupiedDays">Number of shifts the agent holds per calendar day</param>
    /// <param name="date">Day inside the block</param>
    internal static int BlockLengthAt(IReadOnlyDictionary<DateOnly, int> occupiedDays, DateOnly date)
    {
        var length = 1;
        for (var probe = date.AddDays(-1); occupiedDays.ContainsKey(probe); probe = probe.AddDays(-1))
        {
            length++;
        }

        for (var probe = date.AddDays(1); occupiedDays.ContainsKey(probe); probe = probe.AddDays(1))
        {
            length++;
        }

        return length;
    }

    /// <summary>Shift kind per worked day of one agent; a day with several kinds keeps the first in date order.</summary>
    /// <param name="agentTokens">Assignments of a single agent</param>
    internal static Dictionary<DateOnly, int> BuildKindByDay(IReadOnlyList<CoreToken> agentTokens)
    {
        var kinds = new Dictionary<DateOnly, int>();
        foreach (var token in agentTokens.OrderBy(t => t.Date).ThenBy(t => t.StartAt))
        {
            kinds.TryAdd(token.Date, token.ShiftTypeIndex);
        }

        return kinds;
    }

    /// <summary>
    /// Number of shifts the agent holds per calendar day, including the fixed work of the previous
    /// period: a carried-in day is as real a block neighbour as a planned one, and ignoring it would
    /// let a pass split a block across the period boundary.
    /// </summary>
    /// <param name="tokens">Plan to read the assignments from</param>
    /// <param name="agentId">Agent whose days are counted</param>
    /// <param name="context">Wizard context supplying the fixed work of the previous period</param>
    internal static Dictionary<DateOnly, int> BuildOccupiedDays(
        IReadOnlyList<CoreToken> tokens, string agentId, CoreWizardContext context)
    {
        var days = new Dictionary<DateOnly, int>();

        foreach (var token in tokens)
        {
            if (string.Equals(token.AgentId, agentId, StringComparison.Ordinal))
            {
                days[token.Date] = days.GetValueOrDefault(token.Date, 0) + 1;
            }
        }

        foreach (var locked in context.BoundaryLockedWorks)
        {
            if (string.Equals(locked.AgentId, agentId, StringComparison.Ordinal))
            {
                days[locked.Date] = days.GetValueOrDefault(locked.Date, 0) + 1;
            }
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            if (string.Equals(blocker.AgentId, agentId, StringComparison.Ordinal))
            {
                days[blocker.Date] = days.GetValueOrDefault(blocker.Date, 0) + 1;
            }
        }

        return days;
    }

    /// <summary>
    /// The days an open carried-in package still owes, per employee and order. No pass may take these
    /// away: the last day of such a package sits at the edge of the donor's block, so the structural
    /// donor protection would wave it through and the pass would undo the construction the seeding
    /// strategies just performed.
    /// </summary>
    /// <param name="context">Wizard context supplying the roster, the period and the fixed works</param>
    internal static HashSet<(string AgentId, DateOnly Date, Guid ShiftRefId)> BuildContinuationDays(
        CoreWizardContext context)
    {
        var days = new HashSet<(string, DateOnly, Guid)>();
        var anchor = CarryInContinuation.FirstPlannableDay(context);

        foreach (var package in CarryInContinuation.Detect(context, anchor))
        {
            for (var offset = 0; offset < package.RemainingDays; offset++)
            {
                days.Add((package.AgentId, anchor.AddDays(offset), package.ShiftRefId));
            }
        }

        return days;
    }

    /// <summary>Deterministic tie-break over two candidate shifts: earlier day, then earlier start, then order id.</summary>
    /// <param name="candidate">Shift under consideration</param>
    /// <param name="incumbent">Best shift found so far</param>
    internal static bool IsEarlier(CoreToken candidate, CoreToken incumbent)
    {
        if (candidate.Date != incumbent.Date)
        {
            return candidate.Date < incumbent.Date;
        }

        if (candidate.StartAt != incumbent.StartAt)
        {
            return candidate.StartAt < incumbent.StartAt;
        }

        return candidate.ShiftRefId.CompareTo(incumbent.ShiftRefId) < 0;
    }
}
