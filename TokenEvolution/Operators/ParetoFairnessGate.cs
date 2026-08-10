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
/// This gate accepts exactly that case: the fairness must rise strictly and no rule between 1 and 8 may
/// move the wrong way. Everything the priority order places below rule 9 is free to fluctuate. The
/// comparison always runs against the CURRENT intermediate plan, so over a chain of swaps every
/// protected component is monotonically non-decreasing.
/// </para>
/// <para>
/// Rule 6 has no fitness representation, so the overlong-package count enters the snapshot explicitly
/// rather than through a stage.
/// </para>
/// </summary>
public static class ParetoFairnessGate
{
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
            OverlongPackages: OverlongBlockTrace.Overlong(plan, context).Count);
    }

    /// <summary>
    /// True when the candidate raises the shift-kind fairness without moving any numbered rule from 1
    /// to 8 the wrong way.
    /// </summary>
    /// <param name="current">Snapshot of the plan the swap starts from</param>
    /// <param name="candidate">Snapshot of the plan the swap would produce</param>
    public static bool Accepts(ParetoGateSnapshot current, ParetoGateSnapshot candidate)
        => candidate.ShiftKindFairness > current.ShiftKindFairness
            && candidate.Legality <= current.Legality
            && candidate.Stage0 <= current.Stage0
            && candidate.Stage1 >= current.Stage1
            && candidate.Stage2 >= current.Stage2
            && candidate.BlockOrder >= current.BlockOrder
            && candidate.Blacklist >= current.Blacklist
            && candidate.OverlongPackages <= current.OverlongPackages;
}
