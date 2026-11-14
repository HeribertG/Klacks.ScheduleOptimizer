// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

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
        var currentSnapshot = origin;
        for (var i = 0; i < MaxSwaps; i++)
        {
            var swapped = FindImprovingSwap(origin, current, currentSnapshot, context, evaluator, stats);
            if (swapped is null)
            {
                break;
            }

            (current, currentSnapshot) = swapped.Value;
        }

        stats?.Report(diagnostics!);
        return current;
    }

    /// <summary>
    /// Rejection census of one balancer invocation; only built when a sink listens. The counters follow the check order
    /// of <see cref="FindImprovingSwap"/>: a pair is counted at the first check that drops it, so a pair without fairness
    /// gain is never offered to the slot filter, and a partial swap refused on its best case carries no full-gate reason.
    /// </summary>
    private sealed class BalanceDiagnostics
    {
        private const int MaxDetailLines = 12;

        public int OverlappingPairs;
        public int FilteredBySlotGuard;
        public int NoFairnessGain;
        public int RejectedByBestCase;
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
                + $"noFairnessGain={NoFairnessGain} rejectedByBestCase={RejectedByBestCase} rejected={Rejected} "
                + $"accepted={Accepted}");
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

    /// <summary>
    /// First improving swap in roster and block order, with the gate snapshot of the plan it produces; null when no
    /// candidate qualifies. The checks run cheapest first: fairness from the kind counts, then the slot filter on the
    /// built plan, then for a partial swap the gate on its best case. Each of them only skips a candidate the full
    /// gate refuses as well, so the first accepted candidate is the one a full check of every pair would accept.
    /// </summary>
    private static (CoreScenario Plan, ParetoGateSnapshot Snapshot)? FindImprovingSwap(
        ParetoGateSnapshot origin,
        CoreScenario scenario,
        ParetoGateSnapshot current,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        BalanceDiagnostics? stats)
    {
        var kindCounts = TokenFitnessEvaluator.CountShiftKinds(scenario.Tokens);
        var blocksByAgent = new Dictionary<string, List<KindBlock>>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            blocksByAgent[agent.Id] = KindBlockSwap.BuildKindBlocks(scenario.Tokens, agent.Id, context);
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
                        var overlap = KindBlockSwap.SharedDays(blockA, blockB);
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

                        var fairness = FairnessAfterExchange(
                            kindCounts, agentA.Id, blockA.Kind, agentB.Id, blockB.Kind, fromA.Count, context, evaluator);
                        if (fairness <= current.ShiftKindFairness)
                        {
                            if (stats is not null)
                            {
                                stats.NoFairnessGain++;
                            }
                            continue;
                        }

                        var candidate = KindBlockSwap.TrySwap(
                            scenario, context, agentA, fromA, agentB, fromB);
                        if (candidate is null)
                        {
                            if (stats is not null)
                            {
                                stats.FilteredBySlotGuard++;
                            }
                            continue;
                        }

                        if (!coversBoth && !BestCasePassesGate(origin, current, candidate, fairness, context, evaluator))
                        {
                            if (stats is not null)
                            {
                                stats.RejectedByBestCase++;
                                stats.Detail(
                                    $"{agentA.Id}[{blockA.Kind}]x{agentB.Id}[{blockB.Kind}] "
                                    + $"days={fromA.Count} coversBoth={coversBoth} refusedOnBestCase");
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
                            return (candidate, proposed);
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
    /// Shift-kind fairness of the plan after the two agents exchange <paramref name="days"/> tokens of their two
    /// kinds, read from the kind counts of the current plan. The swap re-owns the tokens and keeps their kinds, so
    /// this is the fairness of the plan <see cref="KindBlockSwap.TrySwap"/> would build. The counts are shifted for
    /// the reading and restored before returning.
    /// </summary>
    internal static double FairnessAfterExchange(
        Dictionary<(int Kind, string AgentId), int> kindCounts,
        string agentA,
        int kindA,
        string agentB,
        int kindB,
        int days,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator)
    {
        Shift(kindCounts, kindA, agentA, -days);
        Shift(kindCounts, kindB, agentA, days);
        Shift(kindCounts, kindB, agentB, -days);
        Shift(kindCounts, kindA, agentB, days);
        var fairness = evaluator.ComputeShiftKindFairnessScore(kindCounts, context);
        Shift(kindCounts, kindA, agentA, days);
        Shift(kindCounts, kindB, agentA, -days);
        Shift(kindCounts, kindB, agentB, days);
        Shift(kindCounts, kindA, agentB, -days);
        return fairness;

        static void Shift(Dictionary<(int Kind, string AgentId), int> counts, int kind, string agentId, int delta)
            => counts[(kind, agentId)] = counts.GetValueOrDefault((kind, agentId), 0) + delta;
    }

    /// <summary>
    /// False when the gate refuses the partial swap even with every unmeasured component at its best value, which
    /// settles the refusal without the full evaluation. The mixed-package count is read first, the block order only
    /// when the count alone does not refuse.
    /// </summary>
    private static bool BestCasePassesGate(
        ParetoGateSnapshot origin,
        ParetoGateSnapshot current,
        CoreScenario candidate,
        double fairness,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator)
    {
        var mixedPackages = MixedKindPackageTrace.Count(candidate);
        var withoutBlockOrder = ParetoFairnessGate.BestCaseOf(current, fairness, double.PositiveInfinity, mixedPackages);
        if (!ParetoFairnessGate.Accepts(origin, current, withoutBlockOrder))
        {
            return false;
        }

        var blockOrder = evaluator.ComputeBlockOrderingScore(candidate, context);
        return ParetoFairnessGate.Accepts(
            origin, current, ParetoFairnessGate.BestCaseOf(current, fairness, blockOrder, mixedPackages));
    }
}
