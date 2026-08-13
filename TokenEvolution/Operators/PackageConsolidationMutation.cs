// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;
using Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M6: dissolves one short calendar package (rule 6) through a targeted equal-hours trade. A day of
/// the short package moves to another agent, and in exchange its owner takes over a same-length
/// token on a date that EXTENDS one of the owner's other packages. Both genome-wide hour accounts
/// stay unchanged, so the child ties the lexicographic comparison up to stage 2 and lets the
/// stage-3 package-compactness term decide — the generating counterpart to that term, which alone
/// could not move the byte-stable attractors because the random operators never produced a more
/// compact candidate at equal hours (RESULTS-MESSKORREKTUR-E4RECUT-2026-08-13.md § 3). Partners
/// that extend their own package with the received day and give away a package-edge day rank
/// first. Both traded tokens are re-checked against stage 0 and the slot filter exactly like
/// TokenSwapMutation; when no candidate trade is valid the parent is returned unmodified.
/// </summary>
/// <param name="stage0">Hard-constraint checker used to veto trades that introduce violations.</param>
public sealed class PackageConsolidationMutation : ITokenOperator
{
    private readonly Stage0HardConstraintChecker _stage0;

    public PackageConsolidationMutation()
        : this(new Stage0HardConstraintChecker())
    {
    }

    public PackageConsolidationMutation(Stage0HardConstraintChecker stage0)
    {
        _stage0 = stage0;
    }

    public CoreScenario Apply(TokenOperatorContext context)
    {
        var tokens = context.Primary.Tokens.ToList();
        var datesByAgent = ShortPackageTrace.DatesByAgent(context.Primary, context.Wizard);

        var shortRuns = new List<(string AgentId, DateOnly Start, DateOnly End)>();
        foreach (var (agentId, dates) in datesByAgent.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var run in ShortPackageTrace.Runs(dates))
            {
                if (run.End.DayNumber - run.Start.DayNumber + 1 <= ShortPackageTrace.ShortPackageMaxDays)
                {
                    shortRuns.Add((agentId, run.Start, run.End));
                }
            }
        }

        if (shortRuns.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var agentsById = new Dictionary<string, CoreAgent>(StringComparer.Ordinal);
        foreach (var agent in context.Wizard.Agents)
        {
            agentsById[agent.Id] = agent;
        }

        // One random entry point, then a deterministic cyclic sweep: the draw stays cheap while a
        // child still finds a valid trade whenever any short package offers one.
        var offset = context.Rng.Next(shortRuns.Count);
        for (var i = 0; i < shortRuns.Count; i++)
        {
            var run = shortRuns[(offset + i) % shortRuns.Count];
            if (TryConsolidate(run, tokens, datesByAgent, agentsById, context, out var traded))
            {
                return TokenSwapMutation.CloneScenario(context.Primary, traded);
            }
        }

        return TokenSwapMutation.CloneScenario(context.Primary, tokens);
    }

    private bool TryConsolidate(
        (string AgentId, DateOnly Start, DateOnly End) run,
        List<CoreToken> tokens,
        Dictionary<string, SortedSet<DateOnly>> datesByAgent,
        IReadOnlyDictionary<string, CoreAgent> agentsById,
        TokenOperatorContext context,
        out List<CoreToken> traded)
    {
        traded = tokens;
        var ownDates = datesByAgent[run.AgentId];

        var runDates = new List<DateOnly>();
        for (var d = run.Start; d <= run.End; d = d.AddDays(1))
        {
            runDates.Add(d);
        }

        // Target dates that extend one of the owner's OTHER packages: free for the owner, adjacent
        // to an occupied day outside the short run itself.
        var targetDates = new SortedSet<DateOnly>();
        foreach (var occupied in ownDates)
        {
            if (occupied >= run.Start && occupied <= run.End)
            {
                continue;
            }

            foreach (var neighbour in new[] { occupied.AddDays(-1), occupied.AddDays(1) })
            {
                if (!ownDates.Contains(neighbour)
                    && neighbour >= context.Wizard.PeriodFrom
                    && neighbour <= context.Wizard.PeriodUntil)
                {
                    targetDates.Add(neighbour);
                }
            }
        }

        if (targetDates.Count == 0)
        {
            return false;
        }

        foreach (var runDate in runDates)
        {
            var giveIdx = tokens.FindIndex(t =>
                t.AgentId == run.AgentId && t.Date == runDate && !t.IsLocked);
            if (giveIdx < 0)
            {
                continue;
            }

            var give = tokens[giveIdx];
            foreach (var (takeIdx, take) in RankedPartners(give, targetDates, tokens, datesByAgent))
            {
                var (tradedGive, tradedTake) = TokenTradeGuard.TradeAgents(give, take, agentsById);
                var candidate = new List<CoreToken>(tokens)
                {
                    [giveIdx] = tradedGive,
                    [takeIdx] = tradedTake,
                };

                if (!TokenTradeGuard.RejectsTrade(
                        _stage0, tradedGive, tradedTake, candidate, agentsById, context.Wizard))
                {
                    traded = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Partner tokens on the target dates, best trades first. The strongest signal is the shift
    /// kind: a received token whose kind matches the package it extends keeps that package
    /// homogeneous (rule 7). Measured 2026-08-13: on the reference seeds this preference picks the
    /// same trades as the rank without it (plans byte-identical), and the mixed-count movements of
    /// those runs trace to the shifted random draw sequence, not to the trades — the preference
    /// stays because it is the right tie-breaker wherever kinds do differ. Below it a partner
    /// whose own package the received day would extend ranks above one that merely gives away a
    /// package-edge day; ties break by date and agent so a fixed seed replays the same trade.
    /// </summary>
    private static IEnumerable<(int Index, CoreToken Token)> RankedPartners(
        CoreToken give,
        SortedSet<DateOnly> targetDates,
        List<CoreToken> tokens,
        Dictionary<string, SortedSet<DateOnly>> datesByAgent)
    {
        var ownKindsByDate = new Dictionary<DateOnly, HashSet<int>>();
        foreach (var t in tokens)
        {
            if (t.AgentId != give.AgentId)
            {
                continue;
            }

            if (!ownKindsByDate.TryGetValue(t.Date, out var kinds))
            {
                kinds = [];
                ownKindsByDate[t.Date] = kinds;
            }

            kinds.Add(t.ShiftTypeIndex);
        }

        var candidates = new List<(int Score, DateOnly Date, string AgentId, int Index, CoreToken Token)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.IsLocked
                || t.AgentId == give.AgentId
                || t.TotalHours != give.TotalHours
                || !targetDates.Contains(t.Date)
                || !datesByAgent.TryGetValue(t.AgentId, out var partnerDates))
            {
                continue;
            }

            var kindMatchesExtendedPackage =
                (ownKindsByDate.TryGetValue(t.Date.AddDays(-1), out var before) && before.Contains(t.ShiftTypeIndex))
                || (ownKindsByDate.TryGetValue(t.Date.AddDays(1), out var after) && after.Contains(t.ShiftTypeIndex));
            var receivedDayExtendsPartner =
                partnerDates.Contains(give.Date.AddDays(-1)) || partnerDates.Contains(give.Date.AddDays(1));
            var givenDayIsPackageEdge =
                partnerDates.Contains(t.Date.AddDays(-1)) != partnerDates.Contains(t.Date.AddDays(1));

            var score = (kindMatchesExtendedPackage ? 4 : 0)
                + (receivedDayExtendsPartner ? 2 : 0)
                + (givenDayIsPackageEdge ? 1 : 0);
            candidates.Add((score, t.Date, t.AgentId, i, t));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Date)
            .ThenBy(c => c.AgentId, StringComparer.Ordinal)
            .Select(c => (c.Index, c.Token));
    }
}
