// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Global;
using Klacks.ScheduleOptimizer.Harmonizer.Rules;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

/// <summary>
/// Outcome of a single Holistic Harmonizer run of the deterministic search. Carries the applied batches so the UI
/// can show what was accepted and how the score progressed.
/// </summary>
/// <param name="OriginalBitmap">The bitmap as loaded from the schedule (post-RowSorter).</param>
/// <param name="FinalBitmap">The bitmap after all accepted batches were applied.</param>
/// <param name="Iterations">The applied batches in evaluation order, with full acceptance metadata
/// (score delta, applied steps, rejections, intent).</param>
/// <param name="FitnessBefore">Global harmony fitness of OriginalBitmap (0..1).</param>
/// <param name="FitnessAfter">Global harmony fitness of FinalBitmap (0..1).</param>
/// <param name="LlmModelId">Label of the engine that produced the result (historic name, kept for the wire contract).</param>
public sealed record HolisticHarmonizerRunResult(
    HarmonyBitmap OriginalBitmap,
    HarmonyBitmap FinalBitmap,
    IReadOnlyList<BatchEvaluation> Iterations,
    double FitnessBefore,
    double FitnessAfter,
    string LlmModelId)
{
    /// <summary>
    /// Approved hard planning constraints that were skipped because their stored parameters are invalid; the
    /// caller surfaces them as a warning (the run itself honoured every valid rule).
    /// </summary>
    public IReadOnlyList<Guid> InvalidPlanningRuleIds { get; init; } = [];

    /// <summary>
    /// Hard planning-rule findings before and after the run; null when the run carried no planning rule.
    /// </summary>
    public PlanningRuleHardCounts? PlanningRuleHardCounts { get; init; }
    /// <summary>
    /// Global harmony (rhythm and fairness between the rows) of the input plan; null when the engine did not measure it.
    /// </summary>
    public GlobalHarmonyScore? HarmonyBefore { get; init; }
    /// <summary>
    /// Global harmony of the final plan; null when the engine did not measure it.
    /// </summary>
    public GlobalHarmonyScore? HarmonyAfter { get; init; }
}
