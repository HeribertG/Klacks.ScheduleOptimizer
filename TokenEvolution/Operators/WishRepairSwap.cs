// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Klacks.ScheduleOptimizer.Constraints.Keywords;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Deterministic repair of unfulfilled positive keyword wishes (-FREE, EARLY / LATE / NIGHT). For an employee A whose wish
/// on day d is open, it looks for a shift of a wanted kind on d that another employee B holds and gives it to A; in
/// exchange A hands one of her own shifts (on any day, the same day included) to B, so both keep their hours. The plain
/// hand-over without a counterpart is tried as well, for the case where B can spare the hours.
/// <para>
/// The random operators rarely hit this two-employee, cross-day exchange, and the tier order (unfulfilled wishes after
/// Stage 1) forbids the one-sided move whenever it costs B the guaranteed hours. The exchange keeps Stage 1 intact, so
/// the wish decides.
/// </para>
/// <para>
/// Both receiving sides pass <see cref="SlotConstraintFilter.IsValidAssignment"/> without relaxation: qualification and
/// eligibility (availability included), contract days, breaks, the employee's own keyword restrictions, block length,
/// rest days, minimum pause, weekly and daily hours. A move is accepted only when it lowers the unfulfilled wishes and
/// leaves legality, coverage, the broken tiers, Stage 1 and Stage 2 no worse (<see cref="IsRepair"/>), so the
/// lexicographic fitness strictly improves, and when no employee who reached the guaranteed hours falls short of them
/// (<see cref="IsAcceptedRepair"/>); locked shifts and the shifts of an open carried-in package never move. Like
/// the hand-over passes it only rewrites token owners, so coverage cannot change. No random source: wishes in roster
/// and date order, candidates in a fixed order; at most <see cref="MaxEvaluations"/> candidates are evaluated per call.
/// </para>
/// </summary>
public sealed class WishRepairSwap
{
    /// <summary>Upper bound on accepted moves per invocation; each one lowers the unfulfilled count, so it is a safety net only.</summary>
    private const int MaxRepairs = 64;

    /// <summary>
    /// Upper bound on full fitness evaluations per invocation, so a plan with many wishes no exchange can fulfil costs a
    /// bounded amount per generation. The pass stops with the plan reached so far when it is used up.
    /// </summary>
    private const int MaxEvaluations = 400;

    /// <summary>Stages 1 and 2 may differ by floating-point noise of a different summation order without counting as worse.</summary>
    private const double StageTolerance = 1e-9;

    /// <summary>An exchange needs a wisher and at least one other employee.</summary>
    internal const int MinAgentsForExchange = 2;

    /// <summary>
    /// Returns a plan with fewer unfulfilled positive wishes, or the unchanged input when no exchange improves the fitness.
    /// </summary>
    /// <param name="scenario">Plan to repair; its tokens are never modified, its fitness fields are (re)evaluated</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the schedule commands</param>
    /// <param name="evaluator">Fitness evaluator used as the per-move acceptance gate</param>
    public CoreScenario Apply(CoreScenario scenario, CoreWizardContext context, TokenFitnessEvaluator evaluator)
        => Repair(scenario, context, evaluator).Scenario;

    /// <summary>
    /// <see cref="Apply"/> together with the number of exchanges it accepted, for the run's trace.
    /// </summary>
    /// <param name="scenario">Plan to repair; its tokens are never modified, its fitness fields are (re)evaluated</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the schedule commands</param>
    /// <param name="evaluator">Fitness evaluator used as the per-move acceptance gate</param>
    public WishRepairOutcome Repair(CoreScenario scenario, CoreWizardContext context, TokenFitnessEvaluator evaluator)
    {
        if (context.Agents.Count < MinAgentsForExchange || scenario.Tokens.Count == 0 || context.ScheduleCommands.Count == 0)
        {
            return new WishRepairOutcome(scenario, 0);
        }

        var wishes = WizardKeywordWishes.For(context);
        if (wishes.Days.Count == 0)
        {
            return new WishRepairOutcome(scenario, 0);
        }

        evaluator.Evaluate(scenario, context);
        var agents = context.Agents.ToDictionary(agent => agent.Id, StringComparer.Ordinal);
        var rosterIndex = context.Agents.Select((agent, index) => (agent.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);
        var continuation = HandoverGeometry.BuildContinuationDays(context);

        var evaluationsLeft = new StrongBox<int>(MaxEvaluations);
        var current = scenario;
        var acceptedMoves = 0;
        for (var move = 0; move < MaxRepairs; move++)
        {
            var accepted = RepairOne(current, context, evaluator, wishes, agents, rosterIndex, continuation, evaluationsLeft);
            if (accepted is null)
            {
                break;
            }

            current = accepted;
            acceptedMoves++;
        }

        return new WishRepairOutcome(current, acceptedMoves);
    }

    private static CoreScenario? RepairOne(
        CoreScenario current,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        WizardKeywordWishes wishes,
        IReadOnlyDictionary<string, CoreAgent> agents,
        IReadOnlyDictionary<string, int> rosterIndex,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation,
        StrongBox<int> evaluationsLeft)
    {
        var open = wishes.Evaluate(current.Tokens, collectItems: true).Items
            .Where(item => item.Outcome == KeywordWishOutcome.Unfulfilled && agents.ContainsKey(item.AgentId))
            .OrderBy(item => rosterIndex[item.AgentId])
            .ThenBy(item => item.Date)
            .ToList();

        foreach (var wish in open)
        {
            var wanted = wishes.Days[(wish.AgentId, wish.Date)].WantedKinds;
            var accepted = RepairWish(
                current, context, evaluator, agents[wish.AgentId], wish.Date, wanted, agents, continuation, evaluationsLeft);
            if (accepted is not null)
            {
                return accepted;
            }
        }

        return null;
    }

    private static CoreScenario? RepairWish(
        CoreScenario current,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        CoreAgent wisher,
        DateOnly date,
        int wantedKinds,
        IReadOnlyDictionary<string, CoreAgent> agents,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation,
        StrongBox<int> evaluationsLeft)
    {
        var tokens = current.Tokens;
        for (var wantedIndex = 0; wantedIndex < tokens.Count; wantedIndex++)
        {
            var wantedToken = tokens[wantedIndex];
            if (wantedToken.Date != date
                || (KeywordShiftKinds.FromShiftTypeIndex(wantedToken.ShiftTypeIndex) & wantedKinds) == KeywordShiftKinds.None
                || !IsMovable(wantedToken, continuation)
                || string.Equals(wantedToken.AgentId, wisher.Id, StringComparison.Ordinal)
                || !agents.TryGetValue(wantedToken.AgentId, out var holder))
            {
                continue;
            }

            foreach (var counterIndex in CounterpartCandidates(tokens, wisher.Id, wantedToken, continuation))
            {
                if (evaluationsLeft.Value <= 0)
                {
                    return null;
                }

                var candidate = TryExchange(current, context, evaluator, wisher, holder, wantedIndex, counterIndex, evaluationsLeft);
                if (candidate is not null)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The wisher's shifts the holder could take in exchange, closest in hours first, then by date; the plain hand-over
    /// without a counterpart (null) comes first.
    /// </summary>
    private static IEnumerable<int?> CounterpartCandidates(
        IReadOnlyList<CoreToken> tokens,
        string wisherId,
        CoreToken wantedToken,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation)
    {
        yield return null;

        var counterparts = new List<(int Index, decimal HoursGap, CoreToken Token)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.Equals(token.AgentId, wisherId, StringComparison.Ordinal) && IsMovable(token, continuation))
            {
                counterparts.Add((i, Math.Abs(token.TotalHours - wantedToken.TotalHours), token));
            }
        }

        foreach (var counterpart in counterparts
            .OrderBy(c => c.HoursGap)
            .ThenBy(c => c.Token.Date)
            .ThenBy(c => c.Token.StartAt)
            .ThenBy(c => c.Token.ShiftRefId))
        {
            yield return counterpart.Index;
        }
    }

    private static CoreScenario? TryExchange(
        CoreScenario current,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        CoreAgent wisher,
        CoreAgent holder,
        int wantedIndex,
        int? counterIndex,
        StrongBox<int> evaluationsLeft)
    {
        var tokens = current.Tokens;
        var wantedToken = tokens[wantedIndex];
        var counterToken = counterIndex is int index ? tokens[index] : null;

        var wisherKeeps = TokensOf(tokens, wisher.Id, exceptIndex: counterIndex);
        if (!AcceptsToken(wisher, wantedToken, wisherKeeps, context))
        {
            return null;
        }

        if (counterToken is not null && !AcceptsToken(holder, counterToken, TokensOf(tokens, holder.Id, exceptIndex: wantedIndex), context))
        {
            return null;
        }

        var candidateTokens = new List<CoreToken>(tokens) { [wantedIndex] = Reowned(wantedToken, wisher) };
        if (counterIndex is int counter)
        {
            candidateTokens[counter] = Reowned(counterToken!, holder);
        }

        var candidate = TokenSwapMutation.CloneScenario(current, candidateTokens);
        evaluationsLeft.Value--;
        evaluator.Evaluate(candidate, context);
        return IsAcceptedRepair(candidate, current, evaluator, context) ? candidate : null;
    }

    /// <summary>
    /// True when the candidate fulfils more positive wishes and is not worse anywhere else: legality, coverage (Stage 0),
    /// broken wish tiers, guaranteed hours (Stage 1) and target hours (Stage 2). A move that only improves the hours is
    /// left to the hand-over passes, so this pass never trades a wish for anything.
    /// </summary>
    public static bool IsRepair(CoreScenario candidate, CoreScenario current)
        => candidate.FitnessKeywordWishes.Unfulfilled < current.FitnessKeywordWishes.Unfulfilled
            && candidate.FitnessStage0Legality <= current.FitnessStage0Legality
            && candidate.FitnessStage0 <= current.FitnessStage0
            && candidate.FitnessKeywordWishes.CompareBrokenTo(current.FitnessKeywordWishes) <= 0
            && candidate.FitnessStage1 >= current.FitnessStage1 - StageTolerance
            && candidate.FitnessStage2 >= current.FitnessStage2 - StageTolerance;

    /// <summary>
    /// The acceptance gate of both wish repairs: <see cref="IsRepair"/>, and no employee who reaches the guaranteed hours
    /// before the move falls short of them after it (<see cref="TokenFitnessEvaluator.KeepsEveryGuarantee"/>).
    /// </summary>
    internal static bool IsAcceptedRepair(
        CoreScenario candidate, CoreScenario current, TokenFitnessEvaluator evaluator, CoreWizardContext context)
        => IsRepair(candidate, current) && evaluator.KeepsEveryGuarantee(candidate, current, context);

    internal static bool AcceptsToken(CoreAgent receiver, CoreToken token, IReadOnlyList<CoreToken> receiverTokens, CoreWizardContext context)
        => SlotConstraintFilter.IsValidAssignment(
            receiver,
            token.Date,
            token.ShiftTypeIndex,
            token.ShiftRefId,
            token.TotalHours,
            context,
            receiverTokens,
            token.StartAt,
            token.EndAt);

    internal static CoreToken Reowned(CoreToken token, CoreAgent receiver)
        => token with
        {
            AgentId = receiver.Id,
            Surcharges = SurchargeEstimator.Estimate(token.TotalHours, token.ShiftTypeIndex, token.Date, receiver),
        };

    internal static bool IsMovable(CoreToken token, IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation)
        => !token.IsLocked && !continuation.Contains((token.AgentId, token.Date, token.ShiftRefId));

    private static List<CoreToken> TokensOf(IReadOnlyList<CoreToken> tokens, string agentId, int? exceptIndex)
    {
        var owned = new List<CoreToken>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i != exceptIndex && string.Equals(tokens[i].AgentId, agentId, StringComparison.Ordinal))
            {
                owned.Add(tokens[i]);
            }
        }

        return owned;
    }
}
