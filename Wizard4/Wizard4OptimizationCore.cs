// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.Harmonizer.Scorer;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.Objective;

namespace Klacks.ScheduleOptimizer.Wizard4;

/// <summary>
/// Pure engine core of the Wizard-4 background anytime-optimiser, isolated from the hosting concerns
/// (BackgroundService / presence / resource gating, which live in Klacks.Api). It is the Harmonizer
/// GA composition with EXACTLY one substitution: the outer fitness is the composite "Gesamtzustand"
/// (via <see cref="CompositeBitmapFitnessEvaluator"/>) instead of the row-position-weighted harmony
/// fitness. The inner conductor keeps the HarmonyScorer, so move proposals are harmony-biased (a
/// documented v1 reachability limit, not a correctness break — see the implementation plan). The loop's
/// elitism carries the immutable snapshot (the seed, always admissible against itself), so the best
/// arrangement is never worse than the snapshot. Deterministic for a seeded config.
/// </summary>
public sealed class Wizard4OptimizationCore : IWizard4OptimizationCore
{
    /// <summary>Emergency-unlock score fraction of the row median, matching the harmonizer runner's default.</summary>
    private const double EmergencyUnlockThreshold = 0.5;

    private readonly CompositeObjective _objective = new();

    /// <summary>
    /// Runs the anytime optimisation against the composite objective and returns the best arrangement
    /// plus the baseline/best composite results (for the candidate diff/rationale).
    /// </summary>
    /// <param name="seed">The snapshot bitmap to improve (the accepted/real state, cloned into a W4 token)</param>
    /// <param name="objectiveContext">Static context for the composite objective over the same snapshot</param>
    /// <param name="validator">Domain validator (locks/breaks/eligibility/availability) shared by the conductor and stochastic mutation</param>
    /// <param name="config">Evolution budget/termination knobs (set <c>Seed</c> for determinism)</param>
    /// <param name="hints">Optional softening hints that enlarge the conductor's per-row budget</param>
    /// <param name="progress">Optional per-generation progress callback</param>
    /// <param name="enforceCoverageFloor">When true (default, Spitex) UnderSupply may not regress vs the snapshot</param>
    /// <param name="ct">Cancellation (the loop honours it at generation boundaries)</param>
    public Wizard4OptimizationResult Optimize(
        HarmonyBitmap seed,
        CoreWizardContext objectiveContext,
        DomainAwareReplaceValidator validator,
        HarmonizerEvolutionConfig config,
        IReadOnlyList<SofteningHint>? hints = null,
        IProgress<EvolutionGenerationProgress>? progress = null,
        bool enforceCoverageFloor = true,
        CancellationToken ct = default)
    {
        var sorted = RowSorter.Sort(seed);
        var scorer = new HarmonyScorer();
        var baseline = _objective.Evaluate(ObjectiveInputBuilder.FromBitmap(sorted, objectiveContext));
        var fitness = new CompositeBitmapFitnessEvaluator(_objective, objectiveContext, baseline, enforceCoverageFloor);
        var stochasticMutation = new StochasticBitmapMutation(validator);

        HarmonizerConductor BuildConductor(int rowCount)
        {
            var emergencyState = new EmergencyUnlockState(rowCount);
            var emergency = new EmergencyUnlockManager(emergencyState, EmergencyUnlockThreshold);
            var mutation = new ReplaceMutation(scorer, validator);
            var blockSwap = new BlockSwapMutation(scorer, validator);
            return new HarmonizerConductor(scorer, mutation, emergency, hints: hints, blockSwapMutation: blockSwap);
        }

        var loop = new HarmonizerEvolutionLoop(fitness, stochasticMutation, BuildConductor, config);
        var result = loop.Run(sorted, progress, ct);
        var best = _objective.Evaluate(ObjectiveInputBuilder.FromBitmap(result.Best.Bitmap, objectiveContext));

        return new Wizard4OptimizationResult(result.Best.Bitmap, baseline, best, baseline.Scalar, result.Best.Fitness);
    }
}
