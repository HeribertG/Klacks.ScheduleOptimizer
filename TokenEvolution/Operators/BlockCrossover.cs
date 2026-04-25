// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// C1: 1-point block crossover. Takes the first k blocks (ordered by AgentId, FirstDate) from parent A
/// and appends the blocks from parent B that do not collide on the same (agent, date, shift) triple.
/// Locked tokens appear exactly once regardless of source.
/// </summary>
public sealed class BlockCrossover : ITokenOperator
{
    public CoreScenario Apply(TokenOperatorContext context)
    {
        var parentA = context.Primary;
        var parentB = context.Secondary ?? context.Primary;

        var blocksA = parentA.Tokens
            .GroupBy(t => t.BlockId)
            .Select(g => new { Id = g.Key, Tokens = g.ToList(), AgentId = g.First().AgentId, FirstDate = g.Min(t => t.Date) })
            .OrderBy(b => b.AgentId, StringComparer.Ordinal)
            .ThenBy(b => b.FirstDate)
            .ToList();

        if (blocksA.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(parentA, parentB.Tokens.ToList());
        }

        var cutPoint = context.Rng.Next(1, blocksA.Count + 1);
        var result = new List<CoreToken>();
        var usedLockedWorkIds = new HashSet<string>();
        var usedKeys = new HashSet<(string, DateOnly, Guid)>();

        foreach (var block in blocksA.Take(cutPoint))
        {
            foreach (var token in block.Tokens)
            {
                result.Add(token);
                TrackToken(token, usedKeys, usedLockedWorkIds);
            }
        }

        var parentBBlocks = parentB.Tokens
            .GroupBy(t => t.BlockId)
            .Select(g => new { Id = g.Key, Tokens = g.ToList() })
            .ToList();

        foreach (var block in parentBBlocks)
        {
            if (block.Tokens.Any(t => ConflictsWith(t, usedKeys, usedLockedWorkIds)))
            {
                continue;
            }

            foreach (var token in block.Tokens)
            {
                result.Add(token);
                TrackToken(token, usedKeys, usedLockedWorkIds);
            }
        }

        return TokenSwapMutation.CloneScenario(parentA, result);
    }

    private static bool ConflictsWith(
        CoreToken token,
        HashSet<(string, DateOnly, Guid)> usedKeys,
        HashSet<string> usedLockedWorkIds)
    {
        foreach (var workId in token.WorkIds)
        {
            if (usedLockedWorkIds.Contains(workId))
            {
                return true;
            }
        }

        return usedKeys.Contains((token.AgentId, token.Date, token.ShiftRefId));
    }

    private static void TrackToken(
        CoreToken token,
        HashSet<(string, DateOnly, Guid)> usedKeys,
        HashSet<string> usedLockedWorkIds)
    {
        usedKeys.Add((token.AgentId, token.Date, token.ShiftRefId));
        foreach (var workId in token.WorkIds)
        {
            usedLockedWorkIds.Add(workId);
        }
    }
}
