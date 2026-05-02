// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M1: Swaps the agent assignment of two non-locked tokens in the scenario.
/// Locked tokens are never mutated. After the swap, both modified tokens are re-checked
/// against Stage 0 — if either would violate a hard constraint, the mutation is rolled back
/// and the parent scenario is returned unmodified.
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
        var swappedA = a with { AgentId = b.AgentId };
        var swappedB = b with { AgentId = a.AgentId };
        tokens[firstIdx] = swappedA;
        tokens[secondIdx] = swappedB;

        if (ViolatesStage0(swappedA, swappedB, tokens, context.Wizard))
        {
            return CloneScenario(context.Primary, context.Primary.Tokens.ToList());
        }

        return CloneScenario(context.Primary, tokens);
    }

    private bool ViolatesStage0(
        CoreToken first,
        CoreToken second,
        IReadOnlyList<CoreToken> allTokens,
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

        if (_stage0.ValidateToken(first, othersForFirst, wizard) != null)
        {
            return true;
        }

        if (_stage0.ValidateToken(second, othersForSecond, wizard) != null)
        {
            return true;
        }

        return false;
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
