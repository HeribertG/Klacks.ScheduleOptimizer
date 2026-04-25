// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M2: Splits one block at a random point into two blocks with new BlockIds.
/// Useful to relieve MaxConsecutiveDays pressure. Locked-only blocks are skipped.
/// </summary>
public sealed class BlockSplitMutation : ITokenOperator
{
    public CoreScenario Apply(TokenOperatorContext context)
    {
        var tokens = context.Primary.Tokens.ToList();
        var blocks = tokens
            .GroupBy(t => t.BlockId)
            .Where(g => g.Any(t => !t.IsLocked) && g.Count() >= 2)
            .ToList();

        if (blocks.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var chosenBlock = blocks[context.Rng.Next(blocks.Count)];
        var ordered = chosenBlock.OrderBy(t => t.StartAt).ToList();
        var splitIndex = context.Rng.Next(1, ordered.Count);
        var newBlockId = Guid.NewGuid();

        var updated = new List<CoreToken>(tokens.Count);
        foreach (var token in tokens)
        {
            if (token.BlockId != chosenBlock.Key)
            {
                updated.Add(token);
                continue;
            }

            var positionInBlock = ordered.IndexOf(token);
            if (positionInBlock >= splitIndex)
            {
                updated.Add(token with
                {
                    BlockId = newBlockId,
                    PositionInBlock = positionInBlock - splitIndex,
                });
            }
            else
            {
                updated.Add(token with { PositionInBlock = positionInBlock });
            }
        }

        return TokenSwapMutation.CloneScenario(context.Primary, updated);
    }
}
