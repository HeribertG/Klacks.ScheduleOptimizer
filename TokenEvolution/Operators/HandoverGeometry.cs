// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;
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

    /// <summary>Weight of a non-ideal block change against a kind change inside a block: rotation before purity.</summary>
    internal const int NonIdealTransitionWeight = 2;

    /// <summary>
    /// Rotation cost of an agent's shifts per <see cref="ShiftRotation"/> (SPEC-ROTATION-2026-10-08): kind changes inside
    /// a block plus <see cref="NonIdealTransitionWeight"/> per non-ideal block change, carry-in shifts as predecessors.
    /// Zero for agents without PerformsShiftWork, whom the rotation does not bind.
    /// </summary>
    /// <param name="agentId">Agent whose shifts are judged</param>
    /// <param name="agentTokens">The agent's shifts in the period</param>
    /// <param name="context">Wizard context supplying the boundary shifts and the allowed kinds</param>
    internal static int RotationCost(string agentId, IEnumerable<CoreToken> agentTokens, CoreWizardContext context)
    {
        if (!context.Agents.Any(a => a.PerformsShiftWork && string.Equals(a.Id, agentId, StringComparison.Ordinal)))
        {
            return 0;
        }

        var rotation = RotationContext.For(context);
        var days = ShiftRotation.DaysOf(agentTokens
            .Select(t => (t.Date, t.ShiftTypeIndex, t.StartAt, t.EndAt))
            .Concat(rotation.BoundaryShiftsOf(agentId)));
        var assessment = ShiftRotation.Assess(
            days, context.PeriodFrom, (kind, blockDays) => rotation.IsAllowedOnAnyDay(agentId, kind, blockDays));
        return assessment.InBlockChanges + (assessment.NonIdealTransitions * NonIdealTransitionWeight);
    }

    /// <summary>
    /// Weight that keeps the package structure strictly ahead of the rotation: package integrity (rule 6) ranks above
    /// the shift-kind rotation, so no rotation gain may make an isolated new block look cheaper than extending one.
    /// </summary>
    internal const int StructureWeight = 1000;

    /// <summary>
    /// How badly moving a shift from its donor to the receiver damages both agents' packages, ordered strictly: opening
    /// an isolated block for the receiver costs <see cref="NewBlockPenalty"/> times <see cref="StructureWeight"/>, and
    /// only below that the change of both agents' <see cref="RotationCost"/> decides (negative when the move improves
    /// the rotation).
    /// </summary>
    /// <param name="receiverId">Agent that would take the shift</param>
    /// <param name="receiverDays">Shift kind per worked day of the receiver</param>
    /// <param name="receiverTokens">The receiver's shifts</param>
    /// <param name="receiverCost">The receiver's rotation cost before the move</param>
    /// <param name="donorTokens">The donor's shifts, the moved one included</param>
    /// <param name="donorCost">The donor's rotation cost before the move</param>
    /// <param name="token">Shift that would move</param>
    /// <param name="context">Wizard context</param>
    internal static int MovePenalty(
        string receiverId,
        IReadOnlyDictionary<DateOnly, int> receiverDays,
        IReadOnlyList<CoreToken> receiverTokens,
        int receiverCost,
        IReadOnlyList<CoreToken> donorTokens,
        int donorCost,
        CoreToken token,
        CoreWizardContext context)
    {
        var isolated = !receiverDays.ContainsKey(token.Date.AddDays(-1)) && !receiverDays.ContainsKey(token.Date.AddDays(1));
        var receiverAfter = RotationCost(receiverId, receiverTokens.Append(token), context);
        var donorAfter = RotationCost(token.AgentId, donorTokens.Where(t => t != token), context);
        return (isolated ? NewBlockPenalty * StructureWeight : 0) + (receiverAfter - receiverCost) + (donorAfter - donorCost);
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
