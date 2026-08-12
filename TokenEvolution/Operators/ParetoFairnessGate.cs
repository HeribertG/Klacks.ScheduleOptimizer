// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Second acceptance path for shift-kind fairness (rule 9). The lexicographic <c>Compare</c> refuses a
/// swap as soon as ANY earlier stage falls, and Stage 3 mixes the numbered rotation rules with helper
/// terms that carry no rule number at all — order loyalty (rule 10, which stands BELOW fairness), the
/// intra-day gap, and the Stage-4 cosmetics. A swap that raises the fairness and pays for it only with
/// one of those helpers is therefore rejected today although the priority order permits it.
/// <para>
/// This gate accepts that case unconditionally: the fairness must rise strictly and no rule between 1
/// and 8 may move the wrong way. Everything the priority order places below rule 9 is free to
/// fluctuate. The comparison always runs against the CURRENT intermediate plan, so over a chain of
/// swaps every protected component is monotonically non-decreasing on this path.
/// </para>
/// <para>
/// Owner ruling 2026-08-12 (SPEC.md decision 12c, "fairness is fuzzy, not sharp"): fairness may
/// additionally buy a SMALL, bounded regression of the soft rules 7 and 8. The trade path opens only
/// once the fairness gain against the plan the balancer STARTED from reaches
/// <see cref="MinFairnessGainForTrade"/>; it may then spend at most <see cref="FairnessTradeRate"/>
/// times that gain as block-order loss and at most <see cref="MaxMixedPackagesTradeIncrease"/> extra
/// mixed package, both measured cumulatively against that origin snapshot, so a chain of swaps can
/// never drift further than the single-swap allowance. The hard rules — legality, hour coverage, the
/// blacklist share and the overlong count of rule 6 — are never tradable.
/// </para>
/// <para>
/// Rule 6 has no fitness representation, so the overlong-package count enters the snapshot explicitly
/// rather than through a stage. Rule 7 has one, but only inside an average: the block-ordering component
/// mixes the package constancy of rule 7 with the rotation of rule 8, so a move that breaks a package
/// and rotates better can leave it standing still. Rule 7 outranks rule 8, so the count of packages
/// holding two kinds enters the snapshot on its own as well.
/// </para>
/// </summary>
public static class ParetoFairnessGate
{
    /// <summary>
    /// Smallest cumulative fairness gain (origin to candidate, on the [0..1] stddev-based fairness
    /// scale) that opens the rule-7/8 trade path; below it the gate stays strictly Pareto.
    /// </summary>
    public const double MinFairnessGainForTrade = 0.01;

    /// <summary>
    /// Fraction of the cumulative fairness gain that may be spent as block-order loss (both terms
    /// live on a [0..1] scale). Below 1 the trade always favours the higher-ranked rules 7/8.
    /// </summary>
    public const double FairnessTradeRate = 0.5;

    /// <summary>Most extra mixed packages one balancer run may accumulate through trades.</summary>
    public const int MaxMixedPackagesTradeIncrease = 1;

    /// <summary>
    /// Reads the numbered-rule components of one plan. Runs the full evaluation, so the plan carries
    /// its stage fitness afterwards and can be handed to <c>TokenFitnessEvaluator.Compare</c>.
    /// </summary>
    /// <param name="plan">Plan to measure; its fitness fields are written</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the ban list</param>
    /// <param name="evaluator">Evaluator that owns the fitness definition</param>
    public static ParetoGateSnapshot SnapshotOf(
        CoreScenario plan, CoreWizardContext context, TokenFitnessEvaluator evaluator)
    {
        var detailed = evaluator.EvaluateDetailed(plan, context);

        return new ParetoGateSnapshot(
            Legality: plan.FitnessStage0Legality,
            Stage0: detailed.Stage0,
            Stage1: detailed.Stage1,
            Stage2: detailed.Stage2,
            BlockOrder: detailed.Stage3Components.BlockOrder,
            Blacklist: detailed.Stage3Components.Blacklist,
            ShiftKindFairness: detailed.Stage4Components.ShiftKindFairness,
            OverlongPackages: OverlongBlockTrace.Overlong(plan, context).Count,
            MixedPackages: MixedKindPackageTrace.Count(plan));
    }

    /// <summary>
    /// True when the candidate raises the shift-kind fairness without moving any numbered rule from 1
    /// to 8 the wrong way. Kept as the strict single-step form for callers without an origin plan;
    /// equivalent to the three-snapshot overload with the current plan as its own origin.
    /// </summary>
    /// <param name="current">Snapshot of the plan the swap starts from</param>
    /// <param name="candidate">Snapshot of the plan the swap would produce</param>
    public static bool Accepts(ParetoGateSnapshot current, ParetoGateSnapshot candidate)
        => Accepts(current, current, candidate);

    /// <summary>
    /// True when the candidate raises the shift-kind fairness strictly over the current plan and either
    /// no rule between 1 and 8 moves the wrong way, or the cumulative fairness gain over the origin
    /// plan is large enough to buy the candidate's cumulative rule-7/8 regression within the trade
    /// bounds. Hard components always compare against the CURRENT plan and stay monotonic; the two
    /// tradable ones compare against the ORIGIN plan so the allowance cannot compound over a chain.
    /// </summary>
    /// <param name="origin">Snapshot of the plan the balancer run started from; owns the trade budget</param>
    /// <param name="current">Snapshot of the plan the swap starts from</param>
    /// <param name="candidate">Snapshot of the plan the swap would produce</param>
    public static bool Accepts(
        ParetoGateSnapshot origin, ParetoGateSnapshot current, ParetoGateSnapshot candidate)
    {
        var hardRulesHold = candidate.ShiftKindFairness > current.ShiftKindFairness
            && candidate.Legality <= current.Legality
            && candidate.Stage0 <= current.Stage0
            && candidate.Stage1 >= current.Stage1
            && candidate.Stage2 >= current.Stage2
            && candidate.Blacklist >= current.Blacklist
            && candidate.OverlongPackages <= current.OverlongPackages;
        if (!hardRulesHold)
        {
            return false;
        }

        if (candidate.BlockOrder >= current.BlockOrder && candidate.MixedPackages <= current.MixedPackages)
        {
            return true;
        }

        var cumulativeFairnessGain = candidate.ShiftKindFairness - origin.ShiftKindFairness;
        if (cumulativeFairnessGain < MinFairnessGainForTrade)
        {
            return false;
        }

        var cumulativeBlockOrderLoss = origin.BlockOrder - candidate.BlockOrder;
        var cumulativeMixedIncrease = candidate.MixedPackages - origin.MixedPackages;

        return cumulativeBlockOrderLoss <= FairnessTradeRate * cumulativeFairnessGain
            && cumulativeMixedIncrease <= MaxMixedPackagesTradeIncrease;
    }
}
