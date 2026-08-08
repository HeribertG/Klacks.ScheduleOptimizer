// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M1: Swaps the agent assignment of two non-locked tokens in the scenario.
/// Locked tokens are never mutated. After the swap, both modified tokens are re-checked
/// against Stage 0 AND against <see cref="SlotConstraintFilter"/> — if either would violate a
/// hard constraint, the mutation is rolled back and the parent scenario is returned unmodified.
/// Stage 0 alone only knows the MaxConsecutiveDays hard cap, so a swap could grow a work block
/// past the MaxWorkDays block length and below the MinRestDays free block that seeding, repair
/// and reassign already enforce through the filter.
/// </summary>
/// <param name="stage0">Hard-constraint checker used to veto swaps that introduce violations.</param>
public sealed class TokenSwapMutation : ITokenOperator
{
    private readonly Stage0HardConstraintChecker _stage0;

    public TokenSwapMutation()
        : this(new Stage0HardConstraintChecker())
    {
    }

    public TokenSwapMutation(Stage0HardConstraintChecker stage0)
    {
        _stage0 = stage0;
    }

    public CoreScenario Apply(TokenOperatorContext context)
    {
        var tokens = context.Primary.Tokens.ToList();
        var swappable = tokens
            .Select((t, idx) => (Token: t, Index: idx))
            .Where(x => !x.Token.IsLocked)
            .ToList();

        if (swappable.Count < 2)
        {
            return CloneScenario(context.Primary, tokens);
        }

        var firstIdx = swappable[context.Rng.Next(swappable.Count)].Index;
        int secondIdx;
        var attempts = 0;
        do
        {
            secondIdx = swappable[context.Rng.Next(swappable.Count)].Index;
            attempts++;
        }
        while (secondIdx == firstIdx && attempts < 5);

        if (secondIdx == firstIdx)
        {
            return CloneScenario(context.Primary, tokens);
        }

        var a = tokens[firstIdx];
        var b = tokens[secondIdx];

        // Each half moves to a different agent, so both need the surcharges of their NEW holder -
        // copying them along with the assignment falsifies the Stage-1 hours account.
        var agentsById = new Dictionary<string, CoreAgent>(StringComparer.Ordinal);
        foreach (var agent in context.Wizard.Agents)
        {
            agentsById[agent.Id] = agent;
        }

        var swappedA = a with
        {
            AgentId = b.AgentId,
            Surcharges = SurchargeEstimator.Estimate(
                a.TotalHours, a.ShiftTypeIndex, a.Date, agentsById.GetValueOrDefault(b.AgentId)),
        };
        var swappedB = b with
        {
            AgentId = a.AgentId,
            Surcharges = SurchargeEstimator.Estimate(
                b.TotalHours, b.ShiftTypeIndex, b.Date, agentsById.GetValueOrDefault(a.AgentId)),
        };
        tokens[firstIdx] = swappedA;
        tokens[secondIdx] = swappedB;

        if (ViolatesHardConstraints(swappedA, swappedB, tokens, agentsById, context.Wizard))
        {
            return CloneScenario(context.Primary, context.Primary.Tokens.ToList());
        }

        return CloneScenario(context.Primary, tokens);
    }

    private bool ViolatesHardConstraints(
        CoreToken first,
        CoreToken second,
        IReadOnlyList<CoreToken> allTokens,
        IReadOnlyDictionary<string, CoreAgent> agentsById,
        CoreWizardContext wizard)
    {
        var othersForFirst = new List<CoreToken>(allTokens.Count - 1);
        var othersForSecond = new List<CoreToken>(allTokens.Count - 1);
        for (var i = 0; i < allTokens.Count; i++)
        {
            var t = allTokens[i];
            if (!ReferenceEquals(t, first))
            {
                othersForFirst.Add(t);
            }
            if (!ReferenceEquals(t, second))
            {
                othersForSecond.Add(t);
            }
        }

        return IsRejected(first, othersForFirst, agentsById, wizard)
            || IsRejected(second, othersForSecond, agentsById, wizard);
    }

    private bool IsRejected(
        CoreToken token,
        IReadOnlyList<CoreToken> others,
        IReadOnlyDictionary<string, CoreAgent> agentsById,
        CoreWizardContext wizard)
    {
        if (_stage0.ValidateToken(token, others, wizard) != null)
        {
            return true;
        }

        if (!agentsById.TryGetValue(token.AgentId, out var agent))
        {
            return true;
        }

        return !SlotConstraintFilter.IsValidAssignment(
            agent,
            token.Date,
            token.ShiftTypeIndex,
            token.ShiftRefId,
            token.TotalHours,
            wizard,
            others,
            token.StartAt,
            token.EndAt);
    }

    internal static CoreScenario CloneScenario(CoreScenario source, List<CoreToken> tokens)
    {
        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = tokens,
        };
    }
}
