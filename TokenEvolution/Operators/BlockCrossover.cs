// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// C1: 1-point block crossover. Takes the first k blocks (ordered by AgentId, FirstDate) from parent A
/// and appends the blocks from parent B that do not collide on the same (agent, date, shift) triple,
/// fill an already-taken (date, shift) slot, or trigger a Stage-0 hard-constraint veto when added.
/// Locked tokens appear exactly once regardless of source.
/// </summary>
/// <param name="stage0">Hard-constraint checker used to filter parent-B blocks that would
/// introduce hard violations (MinPauseHours, MaxDailyHours, MaxConsecutiveDays, cross-day overlap).</param>
public sealed class BlockCrossover : ITokenOperator
{
    private readonly Stage0HardConstraintChecker _stage0;

    public BlockCrossover()
        : this(new Stage0HardConstraintChecker())
    {
    }

    public BlockCrossover(Stage0HardConstraintChecker stage0)
    {
        _stage0 = stage0;
    }

    public CoreScenario Apply(TokenOperatorContext context)
    {
        var parentA = context.Primary;
        var parentB = context.Secondary ?? context.Primary;

        var slotCapacity = BuildSlotCapacity(context.Wizard);

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
        var usedAgentKeys = new HashSet<(string, DateOnly, Guid)>();
        var slotFill = new Dictionary<(DateOnly, Guid), int>();

        foreach (var block in blocksA.Take(cutPoint))
        {
            foreach (var token in block.Tokens)
            {
                result.Add(token);
                TrackToken(token, usedAgentKeys, usedLockedWorkIds, slotFill);
            }
        }

        var parentBBlocks = parentB.Tokens
            .GroupBy(t => t.BlockId)
            .Select(g => new { Id = g.Key, Tokens = g.ToList() })
            .ToList();

        foreach (var block in parentBBlocks)
        {
            if (block.Tokens.Any(t => ConflictsWith(t, usedAgentKeys, usedLockedWorkIds, slotFill, slotCapacity)))
            {
                continue;
            }

            if (BlockViolatesStage0(block.Tokens, result, context.Wizard))
            {
                continue;
            }

            foreach (var token in block.Tokens)
            {
                result.Add(token);
                TrackToken(token, usedAgentKeys, usedLockedWorkIds, slotFill);
            }
        }

        return TokenSwapMutation.CloneScenario(parentA, result);
    }

    private bool BlockViolatesStage0(
        IReadOnlyList<CoreToken> blockTokens,
        IReadOnlyList<CoreToken> resultSoFar,
        CoreWizardContext wizard)
    {
        var combined = new List<CoreToken>(resultSoFar.Count + blockTokens.Count);
        combined.AddRange(resultSoFar);
        combined.AddRange(blockTokens);

        for (var i = 0; i < blockTokens.Count; i++)
        {
            var token = blockTokens[i];
            if (token.IsLocked)
            {
                continue;
            }

            var others = new List<CoreToken>(combined.Count - 1);
            for (var j = 0; j < combined.Count; j++)
            {
                if (!ReferenceEquals(combined[j], token))
                {
                    others.Add(combined[j]);
                }
            }

            if (_stage0.ValidateToken(token, others, wizard) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<(DateOnly, Guid), int> BuildSlotCapacity(CoreWizardContext context)
    {
        var capacity = new Dictionary<(DateOnly, Guid), int>();
        foreach (var shift in context.Shifts)
        {
            if (!Guid.TryParse(shift.Id, out var shiftRefId))
            {
                continue;
            }

            if (!DateOnly.TryParse(shift.Date, out var date))
            {
                continue;
            }

            var key = (date, shiftRefId);
            capacity.TryGetValue(key, out var current);
            capacity[key] = current + Math.Max(1, shift.RequiredAssignments);
        }

        return capacity;
    }

    private static bool ConflictsWith(
        CoreToken token,
        HashSet<(string, DateOnly, Guid)> usedAgentKeys,
        HashSet<string> usedLockedWorkIds,
        IReadOnlyDictionary<(DateOnly, Guid), int> slotFill,
        IReadOnlyDictionary<(DateOnly, Guid), int> slotCapacity)
    {
        foreach (var workId in token.WorkIds)
        {
            if (usedLockedWorkIds.Contains(workId))
            {
                return true;
            }
        }

        if (usedAgentKeys.Contains((token.AgentId, token.Date, token.ShiftRefId)))
        {
            return true;
        }

        if (token.ShiftRefId != Guid.Empty)
        {
            var slotKey = (token.Date, token.ShiftRefId);
            slotFill.TryGetValue(slotKey, out var assigned);
            slotCapacity.TryGetValue(slotKey, out var capacity);
            if (capacity > 0 && assigned >= capacity)
            {
                return true;
            }
        }

        return false;
    }

    private static void TrackToken(
        CoreToken token,
        HashSet<(string, DateOnly, Guid)> usedAgentKeys,
        HashSet<string> usedLockedWorkIds,
        Dictionary<(DateOnly, Guid), int> slotFill)
    {
        usedAgentKeys.Add((token.AgentId, token.Date, token.ShiftRefId));
        foreach (var workId in token.WorkIds)
        {
            usedLockedWorkIds.Add(workId);
        }

        if (token.ShiftRefId != Guid.Empty)
        {
            var slotKey = (token.Date, token.ShiftRefId);
            slotFill.TryGetValue(slotKey, out var current);
            slotFill[slotKey] = current + 1;
        }
    }
}
