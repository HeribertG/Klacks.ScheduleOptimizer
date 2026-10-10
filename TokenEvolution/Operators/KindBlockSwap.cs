// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Kind-block building blocks shared by the balancers that exchange work between two agents on the same calendar
/// days: the swappable kind-pure runs of an agent, the shared-day range of two runs and the filter-gated swap itself.
/// </summary>
internal static class KindBlockSwap
{
    /// <summary>
    /// The two token runs the swap would exchange, or null when the blocks share no day, hold the same
    /// kind, or — for a partial overlap — pair a day whose two tokens differ in hours. Both blocks are
    /// gap-free runs of one token per day, so their shared days are the contiguous range between the
    /// later start and the earlier end and both sides hold equally many tokens.
    /// </summary>
    /// <param name="blockA">Kind-pure run of the first agent</param>
    /// <param name="blockB">Kind-pure run of the second agent</param>
    public static SharedDayRange? SharedDays(KindBlock blockA, KindBlock blockB)
    {
        if (blockA.Kind == blockB.Kind)
        {
            return null;
        }

        var first = blockA.FirstDay > blockB.FirstDay ? blockA.FirstDay : blockB.FirstDay;
        var last = blockA.LastDay < blockB.LastDay ? blockA.LastDay : blockB.LastDay;
        var length = last.DayNumber - first.DayNumber + 1;
        if (length <= 0)
        {
            return null;
        }

        var coversBoth = length == blockA.Tokens.Count && length == blockB.Tokens.Count;
        var offsetA = first.DayNumber - blockA.FirstDay.DayNumber;
        var offsetB = first.DayNumber - blockB.FirstDay.DayNumber;
        var fromA = new List<CoreToken>(length);
        var fromB = new List<CoreToken>(length);
        for (var i = 0; i < length; i++)
        {
            var tokenA = blockA.Tokens[offsetA + i];
            var tokenB = blockB.Tokens[offsetB + i];
            if (!coversBoth && tokenA.TotalHours != tokenB.TotalHours)
            {
                return null;
            }

            fromA.Add(tokenA);
            fromB.Add(tokenB);
        }

        return new SharedDayRange(fromA, fromB, coversBoth);
    }

    /// <summary>
    /// Builds the swap result when every exchanged token passes the assignment filter for its new owner,
    /// otherwise null. The filter runs against the plan with both runs removed plus the tokens already
    /// re-owned, so rest-hour checks see the growing swapped state.
    /// </summary>
    public static CoreScenario? TrySwap(
        CoreScenario scenario,
        CoreWizardContext context,
        CoreAgent agentA,
        List<CoreToken> fromA,
        CoreAgent agentB,
        List<CoreToken> fromB)
    {
        var removed = new HashSet<CoreToken>(fromA);
        removed.UnionWith(fromB);
        var working = scenario.Tokens.Where(t => !removed.Contains(t)).ToList();

        foreach (var (token, receiver) in fromB.Select(t => (t, agentA))
                     .Concat(fromA.Select(t => (t, agentB)))
                     .OrderBy(p => p.t.Date)
                     .ThenBy(p => p.t.StartAt))
        {
            if (!SlotConstraintFilter.IsValidAssignment(
                    receiver, token.Date, token.ShiftTypeIndex, token.ShiftRefId,
                    token.TotalHours, context, working, token.StartAt, token.EndAt))
            {
                return null;
            }

            working.Add(token with
            {
                AgentId = receiver.Id,
                Surcharges = SurchargeEstimator.Estimate(
                    token.TotalHours, token.ShiftTypeIndex, token.Date, receiver),
            });
        }

        return TokenSwapMutation.CloneScenario(scenario, working);
    }

    /// <summary>
    /// Consecutive-day runs of one agent that are pure in kind, hold exactly one shift per day,
    /// contain no locked token and do not continue fixed work from before the period — a carried-in
    /// package must keep its kind, so a run touching the agent's boundary work is never swapped.
    /// </summary>
    public static List<KindBlock> BuildKindBlocks(
        IReadOnlyList<CoreToken> tokens, string agentId, CoreWizardContext context)
    {
        var boundaryDays = new HashSet<DateOnly>();
        foreach (var locked in context.BoundaryLockedWorks)
        {
            if (string.Equals(locked.AgentId, agentId, StringComparison.Ordinal))
            {
                boundaryDays.Add(locked.Date);
            }
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            if (string.Equals(blocker.AgentId, agentId, StringComparison.Ordinal))
            {
                boundaryDays.Add(blocker.Date);
            }
        }

        var own = tokens
            .Where(t => string.Equals(t.AgentId, agentId, StringComparison.Ordinal))
            .OrderBy(t => t.Date)
            .ThenBy(t => t.StartAt)
            .ToList();

        var blocks = new List<KindBlock>();
        var run = new List<CoreToken>();

        void CloseRun()
        {
            if (run.Count > 0
                && run.All(t => !t.IsLocked)
                && !boundaryDays.Contains(run[0].Date.AddDays(-1)))
            {
                blocks.Add(new KindBlock(run[0].ShiftTypeIndex, run[0].Date, run[^1].Date, run.ToList()));
            }

            run.Clear();
        }

        for (var i = 0; i < own.Count; i++)
        {
            var token = own[i];
            var continues = run.Count > 0
                && token.Date == run[^1].Date.AddDays(1)
                && token.ShiftTypeIndex == run[0].ShiftTypeIndex;
            var sameDay = run.Count > 0 && token.Date == run[^1].Date;

            if (sameDay)
            {
                run.Clear();
                while (i + 1 < own.Count && own[i + 1].Date == token.Date)
                {
                    i++;
                }

                continue;
            }

            if (!continues)
            {
                CloseRun();
            }

            run.Add(token);
        }

        CloseRun();
        return blocks;
    }
}
