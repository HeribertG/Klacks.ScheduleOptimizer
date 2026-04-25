// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M3: Merges two same-agent adjacent blocks (gap ≤ 1 day) into one block.
/// Can create MaxConsecutiveDays violations — the fitness evaluator filters via Stage 0.
/// </summary>
public sealed class BlockMergeMutation : ITokenOperator
{
    public CoreScenario Apply(TokenOperatorContext context)
    {
        var tokens = context.Primary.Tokens.ToList();

        var perAgent = tokens
            .Where(t => !t.IsLocked)
            .GroupBy(t => t.AgentId)
            .Select(g => (AgentId: g.Key, Blocks: g.GroupBy(t => t.BlockId)
                .Select(b => (BlockId: b.Key, FirstDate: b.Min(t => t.Date), LastDate: b.Max(t => t.Date)))
                .OrderBy(b => b.FirstDate)
                .ToList()))
            .Where(x => x.Blocks.Count >= 2)
            .ToList();

        if (perAgent.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var chosenAgent = perAgent[context.Rng.Next(perAgent.Count)];
        (Guid BlockA, Guid BlockB)? merge = null;
        for (var i = 0; i < chosenAgent.Blocks.Count - 1; i++)
        {
            var gap = (chosenAgent.Blocks[i + 1].FirstDate.DayNumber - chosenAgent.Blocks[i].LastDate.DayNumber);
            if (gap == 1)
            {
                merge = (chosenAgent.Blocks[i].BlockId, chosenAgent.Blocks[i + 1].BlockId);
                break;
            }
        }

        if (merge is null)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var mergedTokens = tokens
            .Select(t => t.BlockId == merge.Value.BlockB ? t with { BlockId = merge.Value.BlockA } : t)
            .ToList();

        return TokenSwapMutation.CloneScenario(context.Primary, mergedTokens);
    }
}
