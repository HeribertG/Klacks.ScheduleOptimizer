// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Gives surplus hours back down the roster (rule 5, the way back). The top-down handover only moves
/// shifts upwards and stops as soon as a receiver reaches its guarantee, so an agent that ended up
/// above its guarantee keeps the surplus while a worse rank stays below its own — a state the rank
/// rule does not ask for. This pass returns exactly that surplus: an agent at least one full shift
/// above its guaranteed hours hands a single shift to a worse-ranked agent that is still below its
/// own, and only while the donor itself stays at or above its guarantee.
/// <para>
/// That donor condition is not a precaution, it is what the fitness demands: stage 1 is a per-agent
/// flag "reached the guaranteed hours", so a move that costs a better rank its flag can never win the
/// lexicographic comparison. With the flag kept, stage 2 improves on both sides — the donor moves
/// towards its target from above, the receiver from below.
/// </para>
/// <para>
/// Structurally a no-op wherever supply is short: without an agent a full shift above its guarantee
/// there is no donor at all, the first scan of the roster ends the pass, and the plan instance is
/// returned unchanged. Like the handover the pass never adds or drops a token, so coverage cannot
/// change; it only rewrites the owner of a token.
/// </para>
/// <para>
/// Deterministic by construction: no random source, receivers in roster order from the top, donors in
/// roster order from the top, the shared candidate tie-break of
/// <see cref="HandoverGeometry"/>, and a fixed upper bound on the number of moves.
/// </para>
/// </summary>
public sealed class SurplusHoursReturn
{
    /// <summary>
    /// Upper bound on shifts returned per invocation; a safety net against a non-terminating loop, not
    /// a tuning knob. Every move strictly lowers the surplus of one donor, so the pass terminates on
    /// its own long before this.
    /// </summary>
    private const int MaxReturns = 64;

    /// <summary>
    /// Returns a scenario in which the surplus hours are handed back down the roster, or the unchanged
    /// input when no legal return exists.
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
        var hours = HandoverGeometry.BuildHours(tokens, context);
        var continuation = HandoverGeometry.BuildContinuationDays(context);
        var changed = false;

        for (var move = 0; move < MaxReturns; move++)
        {
            if (!ReturnOneShift(tokens, hours, context, continuation))
            {
                break;
            }

            changed = true;
        }

        return changed ? TokenSwapMutation.CloneScenario(scenario, tokens) : scenario;
    }

    /// <summary>
    /// Moves a single shift from the best-ranked agent with a returnable surplus to the best-ranked
    /// agent below it that is still short of its guarantee. Returns false when no such move exists,
    /// which is the pass's termination condition and its no-op proof in a short-supply run.
    /// </summary>
    /// <param name="tokens">Working copy of the plan; owners are rewritten in place</param>
    /// <param name="hours">Hours per agent including surcharges; kept in sync with the moves</param>
    /// <param name="context">Wizard context supplying the roster order and the rules</param>
    /// <param name="continuation">Days that belong to an open carried-in package and may not be released</param>
    private static bool ReturnOneShift(
        List<CoreToken> tokens,
        Dictionary<string, double> hours,
        CoreWizardContext context,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation)
    {
        for (var receiverIndex = 1; receiverIndex < context.Agents.Count; receiverIndex++)
        {
            var receiver = context.Agents[receiverIndex];
            if (receiver.GuaranteedHours <= 0
                || hours.GetValueOrDefault(receiver.Id, 0) >= receiver.GuaranteedHours)
            {
                continue;
            }

            var receiverTokens = TokensOf(tokens, receiver.Id);
            var receiverDays = HandoverGeometry.BuildKindByDay(receiverTokens);

            for (var donorIndex = 0; donorIndex < receiverIndex; donorIndex++)
            {
                var donor = context.Agents[donorIndex];
                var donorHours = hours.GetValueOrDefault(donor.Id, 0);
                if (donor.GuaranteedHours <= 0 || donorHours <= donor.GuaranteedHours)
                {
                    continue;
                }

                var index = FindReturn(
                    donor, donorHours, receiver, receiverTokens, receiverDays, tokens, context, continuation);
                if (index < 0)
                {
                    continue;
                }

                Handover(tokens, hours, index, receiver);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Index of the token the donor should hand back, or -1 when it holds no shift the receiver may
    /// legally take while the donor stays at or above its own guarantee. Candidate ranking is the one
    /// the top-down handover uses: least damage to the receiver's package first, then the donor's
    /// shortest block, then the earliest shift.
    /// </summary>
    private static int FindReturn(
        CoreAgent donor,
        double donorHours,
        CoreAgent receiver,
        IReadOnlyList<CoreToken> receiverTokens,
        IReadOnlyDictionary<DateOnly, int> receiverDays,
        IReadOnlyList<CoreToken> tokens,
        CoreWizardContext context,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation)
    {
        var donorDays = HandoverGeometry.BuildOccupiedDays(tokens, donor.Id, context);

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

            if (donorHours - (double)(token.TotalHours + token.Surcharges) < donor.GuaranteedHours)
            {
                continue;
            }

            if (continuation.Contains((donor.Id, token.Date, token.ShiftRefId))
                || !HandoverGeometry.MayRelease(donorDays, token.Date))
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

            var penalty = HandoverGeometry.ReceiverPenalty(receiverDays, token);
            var blockLength = HandoverGeometry.BlockLengthAt(donorDays, token.Date);

            if (bestIndex < 0
                || penalty < bestPenalty
                || (penalty == bestPenalty && blockLength < bestBlockLength)
                || (penalty == bestPenalty && blockLength == bestBlockLength
                    && HandoverGeometry.IsEarlier(token, bestToken)))
            {
                bestIndex = i;
                bestPenalty = penalty;
                bestBlockLength = blockLength;
                bestToken = token;
            }
        }

        return bestIndex;
    }

    private static void Handover(
        List<CoreToken> tokens, Dictionary<string, double> hours, int index, CoreAgent receiver)
    {
        var token = tokens[index];
        var surcharges = SurchargeEstimator.Estimate(
            token.TotalHours, token.ShiftTypeIndex, token.Date, receiver);

        hours[token.AgentId] = hours.GetValueOrDefault(token.AgentId, 0)
            - (double)(token.TotalHours + token.Surcharges);
        hours[receiver.Id] = hours.GetValueOrDefault(receiver.Id, 0)
            + (double)(token.TotalHours + surcharges);

        tokens[index] = token with { AgentId = receiver.Id, Surcharges = surcharges };
    }

    private static List<CoreToken> TokensOf(IReadOnlyList<CoreToken> tokens, string agentId)
    {
        var owned = new List<CoreToken>();
        foreach (var token in tokens)
        {
            if (string.Equals(token.AgentId, agentId, StringComparison.Ordinal))
            {
                owned.Add(token);
            }
        }

        return owned;
    }
}
