// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Constructs the strict top-down service of the guaranteed hours: walking the roster from the top,
/// every agent still below its guaranteed hours takes shifts away from the agents below it until it
/// reaches the target or no legal shift is left — "the last ones eat what remains". The pass never
/// adds or drops a token, so coverage is unchanged; it only rewrites the owner of a token.
/// <para>
/// Every handover is gated by <see cref="SlotConstraintFilter.IsValidAssignment"/> for the receiver,
/// which enforces the same block length, rest-day, rest-hour and eligibility rules that seeding,
/// repair and reassign obey. The donor is protected structurally: a shift may only leave a day the
/// donor keeps working, or a day at the edge of the donor's work block — releasing a day from the
/// middle of a block would split it and could leave the donor with fewer than MinRestDays free days.
/// </para>
/// <para>
/// Deterministic by construction: no random source, roster order for receivers, reverse roster order
/// for donors, and a fixed tie-break over the candidate shifts. Package integrity is preserved as far
/// as the rank rule allows by preferring shifts that extend a block the receiver already has, keep
/// that block's shift kind, and come from the donor's shortest block.
/// </para>
/// </summary>
public sealed class TopDownHandover
{
    /// <summary>Upper bound on handovers per receiving agent; a safety net against a non-terminating loop, never reached in a sane context.</summary>
    private const int MaxHandoversPerAgent = 512;

    /// <summary>
    /// Upper bound on repetitions of the roster walk. One walk is not enough: taking the edge day of a
    /// block turns the next day of that block into an edge as well, so a shift a higher rank could not
    /// legally take on the first walk can become available after a lower rank has been served.
    /// </summary>
    private const int MaxRosterWalks = 16;

    /// <summary>Penalty for a shift that starts an isolated new block for the receiver instead of extending one.</summary>
    private const int NewBlockPenalty = 1;

    /// <summary>Penalty for a shift whose kind differs from the neighbouring day of the receiver's block.</summary>
    private const int MixedKindPenalty = 1;

    /// <summary>
    /// Returns a scenario in which the guaranteed hours are served top-down, or the unchanged input
    /// when no legal handover exists.
    /// </summary>
    /// <param name="scenario">Plan to rebalance; never modified</param>
    /// <param name="context">Wizard context supplying the roster order, the rules and the fixed boundary work</param>
    public CoreScenario Apply(CoreScenario scenario, CoreWizardContext context)
    {
        if (context.Agents.Count < 2 || scenario.Tokens.Count == 0)
        {
            return scenario;
        }

        var tokens = scenario.Tokens.ToList();
        var hours = BuildHours(tokens, context);
        var continuation = BuildContinuationDays(context);
        var changed = false;

        for (var walk = 0; walk < MaxRosterWalks; walk++)
        {
            if (!WalkRoster(tokens, hours, context, continuation))
            {
                break;
            }

            changed = true;
        }

        return changed ? TokenSwapMutation.CloneScenario(scenario, tokens) : scenario;
    }

    /// <summary>
    /// One pass over the roster from the top: every agent below its guaranteed hours takes what it may
    /// legally take from the agents below it. Returns true when at least one shift changed owner.
    /// </summary>
    /// <param name="tokens">Working copy of the plan; owners are rewritten in place</param>
    /// <param name="hours">Hours per agent including surcharges; kept in sync with the moves</param>
    /// <param name="context">Wizard context supplying the roster order and the rules</param>
    /// <param name="continuation">Days that belong to an open carried-in package and may not be released</param>
    private static bool WalkRoster(
        List<CoreToken> tokens,
        Dictionary<string, double> hours,
        CoreWizardContext context,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation)
    {
        var changed = false;

        for (var receiverIndex = 0; receiverIndex < context.Agents.Count - 1; receiverIndex++)
        {
            var receiver = context.Agents[receiverIndex];
            if (receiver.GuaranteedHours <= 0)
            {
                continue;
            }

            for (var handover = 0; handover < MaxHandoversPerAgent; handover++)
            {
                if (hours.GetValueOrDefault(receiver.Id, 0) >= receiver.GuaranteedHours)
                {
                    break;
                }

                var index = FindHandover(receiver, receiverIndex, tokens, context, continuation);
                if (index < 0)
                {
                    break;
                }

                var token = tokens[index];
                var surcharges = SurchargeEstimator.Estimate(
                    token.TotalHours, token.ShiftTypeIndex, token.Date, receiver);

                hours[token.AgentId] = hours.GetValueOrDefault(token.AgentId, 0)
                    - (double)(token.TotalHours + token.Surcharges);
                hours[receiver.Id] = hours.GetValueOrDefault(receiver.Id, 0)
                    + (double)(token.TotalHours + surcharges);

                tokens[index] = token with { AgentId = receiver.Id, Surcharges = surcharges };
                changed = true;
            }
        }

        return changed;
    }

    private static Dictionary<string, double> BuildHours(
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
    /// Index of the token the receiver should take next, or -1 when no agent below it holds a shift
    /// the receiver may legally take. Donors are asked from the bottom of the roster upwards, and the
    /// first donor with a legal candidate is used — that is what makes the rule top-down rather than
    /// merely top-biased.
    /// </summary>
    private static int FindHandover(
        CoreAgent receiver,
        int receiverIndex,
        IReadOnlyList<CoreToken> tokens,
        CoreWizardContext context,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation)
    {
        var receiverTokens = new List<CoreToken>();
        foreach (var token in tokens)
        {
            if (string.Equals(token.AgentId, receiver.Id, StringComparison.Ordinal))
            {
                receiverTokens.Add(token);
            }
        }

        var receiverDays = BuildKindByDay(receiverTokens);

        for (var donorIndex = context.Agents.Count - 1; donorIndex > receiverIndex; donorIndex--)
        {
            var donor = context.Agents[donorIndex];
            var donorDays = BuildOccupiedDays(tokens, donor.Id, context);

            var bestIndex = -1;
            var bestPenalty = int.MaxValue;
            var bestBlockLength = int.MaxValue;
            CoreToken bestToken = default!;

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.IsLocked || !string.Equals(token.AgentId, donor.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (continuation.Contains((donor.Id, token.Date, token.ShiftRefId))
                    || !MayRelease(donorDays, token.Date))
                {
                    continue;
                }

                if (!SlotConstraintFilter.IsValidAssignment(
                        receiver,
                        token.Date,
                        token.ShiftTypeIndex,
                        token.ShiftRefId,
                        token.TotalHours,
                        context,
                        receiverTokens,
                        token.StartAt,
                        token.EndAt))
                {
                    continue;
                }

                var penalty = ReceiverPenalty(receiverDays, token);
                var blockLength = BlockLengthAt(donorDays, token.Date);

                if (bestIndex < 0
                    || penalty < bestPenalty
                    || (penalty == bestPenalty && blockLength < bestBlockLength)
                    || (penalty == bestPenalty && blockLength == bestBlockLength && IsEarlier(token, bestToken)))
                {
                    bestIndex = i;
                    bestPenalty = penalty;
                    bestBlockLength = blockLength;
                    bestToken = token;
                }
            }

            if (bestIndex >= 0)
            {
                return bestIndex;
            }
        }

        return -1;
    }

    /// <summary>
    /// How badly a shift would damage the receiver's package structure: it costs when it opens an
    /// isolated new block, and it costs when it extends a block with a foreign shift kind.
    /// </summary>
    private static int ReceiverPenalty(IReadOnlyDictionary<DateOnly, int> receiverDays, CoreToken token)
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
    /// and this pass simply does not use it.
    /// </summary>
    /// <summary>
    /// The days an open carried-in package still owes, per employee and order. The handover may not
    /// take these away: the last day of such a package sits at the edge of the donor's block, so the
    /// structural donor protection would wave it through and the pass would undo the construction the
    /// seeding strategies just performed.
    /// </summary>
    /// <param name="context">Wizard context supplying the roster, the period and the fixed works</param>
    private static HashSet<(string AgentId, DateOnly Date, Guid ShiftRefId)> BuildContinuationDays(
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

    private static bool MayRelease(IReadOnlyDictionary<DateOnly, int> donorDays, DateOnly date)
    {
        if (donorDays.GetValueOrDefault(date, 0) > 1)
        {
            return true;
        }

        return !donorDays.ContainsKey(date.AddDays(-1)) || !donorDays.ContainsKey(date.AddDays(1));
    }

    private static int BlockLengthAt(IReadOnlyDictionary<DateOnly, int> occupiedDays, DateOnly date)
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
    private static Dictionary<DateOnly, int> BuildKindByDay(IReadOnlyList<CoreToken> agentTokens)
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
    /// let the pass split a block across the period boundary.
    /// </summary>
    private static Dictionary<DateOnly, int> BuildOccupiedDays(
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

    private static bool IsEarlier(CoreToken candidate, CoreToken incumbent)
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
