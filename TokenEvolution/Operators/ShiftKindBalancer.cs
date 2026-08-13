// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Hour-neutral shift-kind rebalancing: swaps work of different kinds between two agents on the
/// calendar days their two blocks have in common. Both agents keep every worked day, so ONE swap leaves
/// the package structure — package count, package lengths, the overlong and the short-package share —
/// exactly as it was and moves only the KIND distribution, which is what the shift-kind fairness rule
/// asks for. That invariance holds per swap, not for the finished plan: the balanced elite re-enters the
/// population, so the search takes a different path afterwards and the end plan's package lengths do
/// move. Every candidate is gated by the full assignment filter on both sides (bans, keywords, rest
/// hours at the block edges, daily caps) and then by an acceptance path that depends on how much of the
/// two blocks the swap covers.
/// <para>
/// A FULL swap — the two blocks cover exactly the same days — replaces a whole kind block by one of the
/// same days and can therefore never leave a package holding two kinds. It keeps both historic
/// acceptance paths: the lexicographic fitness improves strictly, or <see cref="ParetoFairnessGate"/>
/// confirms that the fairness rises while the rules from 1 to 8 either hold or — within the gate's
/// bounded trade allowance against the run's origin plan — are paid for by the fairness gain. A PARTIAL
/// swap reaches inside a package and can break its kind constancy, which the gate's own rule-7 counter
/// prices. It is accepted through the Pareto gate alone, and it additionally requires the two exchanged
/// tokens of a day to carry the same hours, so no agent's daily or weekly hours move. The lexicographic
/// path is denied to partial swaps on purpose: it aggregates the block ordering with helper terms that
/// carry no rule number and would let a location gain pay for a broken package, against rule 7.
/// </para>
/// <para>
/// A cross-day WHOLE-BLOCK trade (M10, 2026-08-13) was built and measured after the rejection
/// census showed 92 percent of the same-day candidates dying in the slot filter on compact plans —
/// and REVERTED the same day: it never fired on the night hoard it was built for (its own unit
/// fixture stayed unbalanced, scenario 3 stayed byte-identical), while its side effects moved the
/// search paths of the other scenarios and broke four edges including an hour-monotonicity rip.
/// The remaining route to night fairness on compact plans is a fairness-aware rebuild inside the
/// ruin-recreate sweep, not a balancer move; the census sink below stays for that work.
/// </para>
/// <para>
/// Deterministic by construction: no random source, candidate pairs are walked in roster order and
/// block start order, first improvement wins, bounded by a fixed swap budget.
/// </para>
/// </summary>
public sealed class ShiftKindBalancer
{
    /// <summary>Upper bound on accepted swaps per invocation; a safety net, not a tuning knob.</summary>
    private const int MaxSwaps = 24;

    /// <summary>
    /// Returns a scenario with strictly better fitness produced by kind swaps on shared calendar days,
    /// or the unchanged input when no improving swap exists.
    /// </summary>
    /// <param name="scenario">Plan to rebalance; never modified</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the ban list</param>
    /// <param name="evaluator">Fitness evaluator used as the acceptance gate</param>
    /// <param name="diagnostics">
    /// Optional sink for the M9 rejection census: how many candidate pairs each acceptance stage
    /// consumed and why the gate refused, with detail lines for the first refusals. Null keeps the
    /// production path free of any counting.
    /// </param>
    public CoreScenario Apply(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        Action<string>? diagnostics = null)
    {
        if (context.Agents.Count < 2 || scenario.Tokens.Count == 0)
        {
            return scenario;
        }

        var stats = diagnostics is null ? null : new BalanceDiagnostics();
        var origin = ParetoFairnessGate.SnapshotOf(scenario, context, evaluator);
        var current = scenario;
        for (var i = 0; i < MaxSwaps; i++)
        {
            var swapped = FindImprovingSwap(origin, current, context, evaluator, stats);
            if (swapped is null)
            {
                break;
            }

            current = swapped;
        }

        stats?.Report(diagnostics!);
        return current;
    }

    /// <summary>Rejection census of one balancer invocation; only built when a sink listens.</summary>
    private sealed class BalanceDiagnostics
    {
        private const int MaxDetailLines = 12;

        public int OverlappingPairs;
        public int FilteredBySlotGuard;
        public int NoFairnessGain;
        public int Rejected;
        public int Accepted;
        public readonly List<string> Details = [];

        public void Detail(string line)
        {
            if (Details.Count < MaxDetailLines)
            {
                Details.Add(line);
            }
        }

        public void Report(Action<string> sink)
        {
            sink(
                $"BALANCE pairs={OverlappingPairs} slotFiltered={FilteredBySlotGuard} "
                + $"noFairnessGain={NoFairnessGain} rejected={Rejected} accepted={Accepted}");
            foreach (var line in Details)
            {
                sink($"BALANCE reject {line}");
            }
        }
    }

    /// <summary>
    /// Names the first acceptance condition the candidate failed — a diagnosis-only re-computation
    /// of the gate's checks on the three snapshots the balancer already holds.
    /// </summary>
    private static string DescribeRejection(
        ParetoGateSnapshot origin, ParetoGateSnapshot current, ParetoGateSnapshot proposed, bool coversBoth)
    {
        if (proposed.ShiftKindFairness <= current.ShiftKindFairness)
        {
            return $"fairnessNotStrictlyBetter {proposed.ShiftKindFairness:0.####}<={current.ShiftKindFairness:0.####}";
        }
        if (proposed.Legality > current.Legality) { return "legality"; }
        if (proposed.Stage0 > current.Stage0) { return "stage0"; }
        if (proposed.Stage1 < current.Stage1) { return "stage1Hours"; }
        if (proposed.Stage2 < current.Stage2) { return "stage2Hours"; }
        if (proposed.Blacklist < current.Blacklist) { return "blacklist"; }
        if (proposed.OverlongPackages > current.OverlongPackages) { return "overlong"; }

        var gain = proposed.ShiftKindFairness - origin.ShiftKindFairness;
        if (gain < MinFairnessGainForTrade)
        {
            return $"gainBelowTradeThreshold gain={gain:0.####} coversBoth={coversBoth}";
        }

        var blockOrderLoss = origin.BlockOrder - proposed.BlockOrder;
        if (blockOrderLoss > FairnessTradeRate * gain)
        {
            return $"blockOrderRate loss={blockOrderLoss:0.####} allowance={FairnessTradeRate * gain:0.####}";
        }
        if (proposed.MixedPackages - origin.MixedPackages > MaxMixedPackagesTradeIncrease)
        {
            return $"mixedCap {proposed.MixedPackages}>{origin.MixedPackages}+{MaxMixedPackagesTradeIncrease}";
        }
        if (proposed.ShortPackages - origin.ShortPackages > MaxShortPackagesTradeIncrease)
        {
            return $"shortCap {proposed.ShortPackages}>{origin.ShortPackages}+{MaxShortPackagesTradeIncrease}";
        }

        return "acceptedByGateButNotByCompare";
    }

    private const double MinFairnessGainForTrade = ParetoFairnessGate.MinFairnessGainForTrade;
    private const double FairnessTradeRate = ParetoFairnessGate.FairnessTradeRate;
    private const int MaxMixedPackagesTradeIncrease = ParetoFairnessGate.MaxMixedPackagesTradeIncrease;
    private const int MaxShortPackagesTradeIncrease = ParetoFairnessGate.MaxShortPackagesTradeIncrease;

    private static CoreScenario? FindImprovingSwap(
        ParetoGateSnapshot origin,
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        BalanceDiagnostics? stats)
    {
        var current = ParetoFairnessGate.SnapshotOf(scenario, context, evaluator);
        var currentKindFairness = current.ShiftKindFairness;
        var blocksByAgent = new Dictionary<string, List<KindBlock>>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            blocksByAgent[agent.Id] = BuildKindBlocks(scenario.Tokens, agent.Id, context);
        }

        for (var a = 0; a < context.Agents.Count; a++)
        {
            var agentA = context.Agents[a];
            foreach (var blockA in blocksByAgent[agentA.Id])
            {
                for (var b = a + 1; b < context.Agents.Count; b++)
                {
                    var agentB = context.Agents[b];
                    foreach (var blockB in blocksByAgent[agentB.Id])
                    {
                        var overlap = SharedDays(blockA, blockB);
                        if (overlap is null)
                        {
                            continue;
                        }

                        var fromA = overlap.FromA;
                        var fromB = overlap.FromB;
                        var coversBoth = overlap.CoversBothBlocks;

                        if (stats is not null)
                        {
                            stats.OverlappingPairs++;
                        }

                        var candidate = TrySwap(
                            scenario, context, agentA, fromA, agentB, fromB);
                        if (candidate is null)
                        {
                            if (stats is not null)
                            {
                                stats.FilteredBySlotGuard++;
                            }
                            continue;
                        }

                        if (evaluator.ComputeShiftKindFairnessScore(candidate, context) <= currentKindFairness)
                        {
                            if (stats is not null)
                            {
                                stats.NoFairnessGain++;
                            }
                            continue;
                        }

                        var proposed = ParetoFairnessGate.SnapshotOf(candidate, context, evaluator);
                        var accepted = coversBoth
                            ? evaluator.Compare(candidate, scenario) < 0
                                || ParetoFairnessGate.Accepts(origin, current, proposed)
                            : ParetoFairnessGate.Accepts(origin, current, proposed);
                        if (accepted)
                        {
                            if (stats is not null)
                            {
                                stats.Accepted++;
                            }
                            return candidate;
                        }

                        if (stats is not null)
                        {
                            stats.Rejected++;
                            stats.Detail(
                                $"{agentA.Id}[{blockA.Kind}]x{agentB.Id}[{blockB.Kind}] "
                                + $"days={fromA.Count} coversBoth={coversBoth} "
                                + DescribeRejection(origin, current, proposed, coversBoth));
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The two token runs the swap would exchange, or null when the blocks share no day, hold the same
    /// kind, or — for a partial overlap — pair a day whose two tokens differ in hours. Both blocks are
    /// gap-free runs of one token per day, so their shared days are the contiguous range between the
    /// later start and the earlier end and both sides hold equally many tokens.
    /// </summary>
    /// <param name="blockA">Kind-pure run of the first agent</param>
    /// <param name="blockB">Kind-pure run of the second agent</param>
    private static SharedDayRange? SharedDays(KindBlock blockA, KindBlock blockB)
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
    private static CoreScenario? TrySwap(
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
    private static List<KindBlock> BuildKindBlocks(
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

    private sealed record KindBlock(int Kind, DateOnly FirstDay, DateOnly LastDay, List<CoreToken> Tokens);

    private sealed record SharedDayRange(List<CoreToken> FromA, List<CoreToken> FromB, bool CoversBothBlocks);
}
