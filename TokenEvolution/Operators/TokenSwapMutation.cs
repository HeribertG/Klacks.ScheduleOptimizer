// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M1: Swaps the agent assignment of two non-locked tokens in the scenario.
/// Locked tokens are never mutated. Returns an unmodified copy when no valid swap is possible.
/// </summary>
public sealed class TokenSwapMutation : ITokenOperator
{
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
        tokens[firstIdx] = a with { AgentId = b.AgentId };
        tokens[secondIdx] = b with { AgentId = a.AgentId };

        return CloneScenario(context.Primary, tokens);
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
