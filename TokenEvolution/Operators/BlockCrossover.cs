// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// C1: 1-point block crossover over CALENDAR packages. Takes the first k packages (ordered by
/// AgentId, FirstDate) from parent A and appends the packages from parent B that do not collide on
/// the same (agent, date, shift) triple, fill an already-taken (date, shift) slot, or trigger a
/// Stage-0 or <see cref="SlotConstraintFilter"/> hard-constraint veto when added.
/// <para>
/// The exchange unit is a maximal run of consecutive worked calendar dates of one agent — NOT the
/// genome BlockId, which the split/merge mutations fragment far below the calendar packages. The
/// short-package attribution of SPEC.md decision 13 measured the BlockId-based exchange as the
/// dominant splinter source of the whole evolution (+449 to +7741 net short packages per run):
/// transferring a genome fragment whose calendar neighbours collide away leaves one-day and two-day
/// rests in the child. A calendar package transfers whole or not at all, so an omission leaves a
/// coverage HOLE for the package-aware sweep to refill instead of a splinter.
/// </para>
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
        var agentsById = new Dictionary<string, CoreAgent>(StringComparer.Ordinal);
        foreach (var agent in context.Wizard.Agents)
        {
            agentsById[agent.Id] = agent;
        }

        var blocksA = CalendarPackages(parentA.Tokens);

        if (blocksA.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(parentA, parentB.Tokens.ToList());
        }

        // M11e (2026-08-13): a package holding a continuation day of an open carried-in package is
        // never part of the cut lottery — it transfers from the primary parent unconditionally.
        // Without this the crossover could shorten a continuation the seeder had built completely
        // (measured: one of three continuation nights lost in the evolution while every auction
        // seed was correct), against rules A10/A11. M11f refinement (same day): the pin only
        // covers packages within the owner's MaxWorkDays — an overgrown version must re-enter the
        // lottery, or the pin CONSERVES its growth across generations (measured as two six-day
        // packages under the unconditional pin).
        var pinned = new List<CalendarPackage>();
        var lottery = new List<CalendarPackage>();
        foreach (var block in blocksA)
        {
            var touchesContinuation = block.Tokens.Any(t =>
                string.Equals(
                    TokenRepair.ContinuationOwnerIdOf(context.Wizard, t.Date, t.ShiftRefId),
                    block.AgentId,
                    StringComparison.Ordinal));
            var withinWorkDays = !agentsById.TryGetValue(block.AgentId, out var owner)
                || owner.MaxWorkDays <= 0
                || block.Tokens.Select(t => t.Date).Distinct().Count() <= owner.MaxWorkDays;
            (touchesContinuation && withinWorkDays ? pinned : lottery).Add(block);
        }

        var cutPoint = lottery.Count > 0 ? context.Rng.Next(1, lottery.Count + 1) : 0;
        var result = new List<CoreToken>();
        var usedLockedWorkIds = new HashSet<string>();
        var usedAgentKeys = new HashSet<(string, DateOnly, Guid)>();
        var slotFill = new Dictionary<(DateOnly, Guid), int>();

        foreach (var block in pinned.Concat(lottery.Take(cutPoint)))
        {
            foreach (var token in block.Tokens)
            {
                result.Add(token);
                TrackToken(token, usedAgentKeys, usedLockedWorkIds, slotFill);
            }
        }

        var parentBBlocks = CalendarPackages(parentB.Tokens);

        foreach (var block in parentBBlocks)
        {
            if (block.Tokens.Any(t => ConflictsWith(t, usedAgentKeys, usedLockedWorkIds, slotFill, slotCapacity)))
            {
                continue;
            }

            if (BlockViolatesHardConstraints(block.Tokens, result, agentsById, context.Wizard))
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

    private bool BlockViolatesHardConstraints(
        IReadOnlyList<CoreToken> blockTokens,
        IReadOnlyList<CoreToken> resultSoFar,
        IReadOnlyDictionary<string, CoreAgent> agentsById,
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

            if (!agentsById.TryGetValue(token.AgentId, out var agent))
            {
                return true;
            }

            if (!SlotConstraintFilter.IsValidAssignment(
                agent,
                token.Date,
                token.ShiftTypeIndex,
                token.ShiftRefId,
                token.TotalHours,
                wizard,
                others,
                token.StartAt,
                token.EndAt))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The exchange units: maximal runs of consecutive worked calendar dates per agent, ordered by
    /// AgentId and first date. Several shifts on one day (split duty) belong to the same run; the
    /// genome BlockIds of the tokens are carried along untouched.
    /// </summary>
    /// <param name="tokens">Tokens of one parent plan</param>
    private static List<CalendarPackage> CalendarPackages(IReadOnlyList<CoreToken> tokens)
    {
        var packages = new List<CalendarPackage>();
        foreach (var perAgent in tokens
                     .GroupBy(t => t.AgentId, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            List<CoreToken>? run = null;
            DateOnly runStart = default;
            DateOnly previous = default;

            foreach (var token in perAgent.OrderBy(t => t.Date).ThenBy(t => t.StartAt))
            {
                if (run is not null && token.Date.DayNumber - previous.DayNumber <= 1)
                {
                    run.Add(token);
                    previous = token.Date;
                    continue;
                }

                if (run is not null)
                {
                    packages.Add(new CalendarPackage(perAgent.Key, runStart, run));
                }

                run = [token];
                runStart = token.Date;
                previous = token.Date;
            }

            if (run is not null)
            {
                packages.Add(new CalendarPackage(perAgent.Key, runStart, run));
            }
        }

        return packages;
    }

    private sealed record CalendarPackage(string AgentId, DateOnly FirstDate, List<CoreToken> Tokens);

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
