// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;
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
/// Every single move carries its own acceptance gate. Offering the whole bundle of moves to the
/// fitness at once makes one rejected move discard all the legal ones with it — the pass then reads
/// as a no-op although most of its moves were improvements. Each move is therefore built, evaluated
/// and compared on its own against the state the previous accepted move produced, exactly as the
/// order-continuity balancer does, and a rejected candidate only ends its own candidacy: the scan
/// continues with the next shift, the next donor and the next receiver.
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
    /// input when no legal return improves the fitness.
    /// </summary>
    /// <param name="scenario">Plan to rebalance; never modified</param>
    /// <param name="context">Wizard context supplying the roster order, the rules and the fixed boundary work</param>
    /// <param name="evaluator">Fitness evaluator used as the per-move acceptance gate</param>
    public CoreScenario Apply(
        CoreScenario scenario, CoreWizardContext context, TokenFitnessEvaluator evaluator)
    {
        if (context.Agents.Count < 2 || scenario.Tokens.Count == 0)
        {
            return scenario;
        }

        var tokens = scenario.Tokens.ToList();
        var hours = HandoverGeometry.BuildHours(tokens, context);
        var continuation = HandoverGeometry.BuildContinuationDays(context);

        // The gate compares against the incoming plan, so that plan needs its own stage values first.
        // Evaluating is idempotent, and a caller that hands in an unscored plan would otherwise have
        // every candidate compared against zeros and accepted.
        evaluator.Evaluate(scenario, context);

        var current = scenario;
        for (var move = 0; move < MaxReturns; move++)
        {
            var accepted = ReturnOneShift(current, tokens, hours, context, evaluator, continuation);
            if (accepted is null)
            {
                break;
            }

            current = accepted;
        }

        return current;
    }

    /// <summary>
    /// Hands a single shift from the best-ranked agent with a returnable surplus to the best-ranked
    /// agent below it that is still short of its guarantee, and returns the resulting plan once the
    /// fitness accepts it. Returns null when no such move exists or none of them wins its comparison —
    /// that is the pass's termination condition and its no-op proof in a short-supply run.
    /// </summary>
    /// <param name="current">Plan every candidate is compared against; the state of the accepted moves so far</param>
    /// <param name="tokens">Working copy of the plan; owners are rewritten once a move is accepted</param>
    /// <param name="hours">Hours per agent including surcharges; kept in sync with the accepted moves</param>
    /// <param name="context">Wizard context supplying the roster order and the rules</param>
    /// <param name="evaluator">Fitness evaluator used as the per-move acceptance gate</param>
    /// <param name="continuation">Days that belong to an open carried-in package and may not be released</param>
    private static CoreScenario? ReturnOneShift(
        CoreScenario current,
        List<CoreToken> tokens,
        Dictionary<string, double> hours,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
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

                foreach (var index in RankedReturns(
                    donor, donorHours, receiver, receiverTokens, receiverDays, tokens, context, continuation))
                {
                    var accepted = TryReturn(current, tokens, hours, context, evaluator, index, receiver);
                    if (accepted is not null)
                    {
                        return accepted;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the plan in which the given shift belongs to the receiver and returns it when the fitness
    /// prefers it over the current one, otherwise null. Only an accepted move is written back into the
    /// working state, so a rejected candidate leaves nothing behind.
    /// </summary>
    /// <param name="current">Plan the candidate is compared against</param>
    /// <param name="tokens">Working copy of the plan; only touched once the candidate is accepted</param>
    /// <param name="hours">Hours per agent including surcharges; only touched once the candidate is accepted</param>
    /// <param name="context">Wizard context supplying the roster order and the rules</param>
    /// <param name="evaluator">Fitness evaluator used as the acceptance gate</param>
    /// <param name="index">Position of the shift inside the working copy</param>
    /// <param name="receiver">Agent that would take the shift over</param>
    private static CoreScenario? TryReturn(
        CoreScenario current,
        List<CoreToken> tokens,
        Dictionary<string, double> hours,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        int index,
        CoreAgent receiver)
    {
        var token = tokens[index];
        var surcharges = SurchargeEstimator.Estimate(
            token.TotalHours, token.ShiftTypeIndex, token.Date, receiver);
        var moved = token with { AgentId = receiver.Id, Surcharges = surcharges };

        var candidateTokens = new List<CoreToken>(tokens) { [index] = moved };
        var candidate = TokenSwapMutation.CloneScenario(current, candidateTokens);

        evaluator.Evaluate(candidate, context);
        if (evaluator.Compare(candidate, current) >= 0)
        {
            return null;
        }

        hours[token.AgentId] = hours.GetValueOrDefault(token.AgentId, 0)
            - (double)(token.TotalHours + token.Surcharges);
        hours[receiver.Id] = hours.GetValueOrDefault(receiver.Id, 0)
            + (double)(token.TotalHours + surcharges);
        tokens[index] = moved;

        return candidate;
    }

    /// <summary>
    /// Positions of the shifts the donor may hand to this receiver, best candidate first, or an empty
    /// sequence when it holds none that the receiver may legally take while the donor stays at or above
    /// its own guarantee. The ranking is the one the top-down handover uses: least damage to the
    /// receiver's package first, then the donor's shortest block, then the earliest shift.
    /// </summary>
    private static IEnumerable<int> RankedReturns(
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
        var candidates = new List<(int Index, int Penalty, int BlockLength, CoreToken Token)>();

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

            candidates.Add((
                i,
                HandoverGeometry.ReceiverPenalty(receiverDays, token),
                HandoverGeometry.BlockLengthAt(donorDays, token.Date),
                token));
        }

        return candidates
            .OrderBy(c => c.Penalty)
            .ThenBy(c => c.BlockLength)
            .ThenBy(c => c.Token.Date)
            .ThenBy(c => c.Token.StartAt)
            .ThenBy(c => c.Token.ShiftRefId)
            .Select(c => c.Index)
            .ToList();
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
