// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M3: Merges two same-agent adjacent blocks (gap ≤ 1 day) into one block.
/// After the merge the resulting scenario is re-checked against Stage 0; if a hard veto
/// fires (e.g. MaxConsecutiveDays exceeded), the mutation is rolled back to the parent.
/// </summary>
/// <param name="stage0">Hard-constraint checker used to veto merges that introduce violations.</param>
public sealed class BlockMergeMutation : ITokenOperator
{
    private readonly Stage0HardConstraintChecker _stage0;

    public BlockMergeMutation()
        : this(new Stage0HardConstraintChecker())
    {
    }

    public BlockMergeMutation(Stage0HardConstraintChecker stage0)
    {
        _stage0 = stage0;
    }

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

        if (_stage0.ValidateScenario(mergedTokens, context.Wizard) != null)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        return TokenSwapMutation.CloneScenario(context.Primary, mergedTokens);
    }
}
