// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Rules;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

/// <summary>
/// Outcome of a single Holistic Harmonizer inner-loop run. Carries the full iteration history so the UI
/// can show what the LLM tried, what was accepted, what was rejected, and how the score
/// trajectory progressed.
/// </summary>
/// <param name="OriginalBitmap">The bitmap as loaded from the schedule (post-RowSorter).</param>
/// <param name="FinalBitmap">The bitmap after all accepted batches were applied.</param>
/// <param name="Iterations">Every batch the LLM proposed across the inner loop, in evaluation
/// order, with full acceptance metadata (score delta, applied steps, rejections, intent).</param>
/// <param name="FitnessBefore">Global harmony fitness of OriginalBitmap (0..1).</param>
/// <param name="FitnessAfter">Global harmony fitness of FinalBitmap (0..1). Score-Greedy
/// guarantees this is at least <see cref="FitnessBefore"/>.</param>
/// <param name="LlmModelId">The LLM model id that produced the proposals.</param>
/// <param name="LlmParsingError">Non-null when the last LLM response could not be parsed;
/// UI should surface this so the operator can pick a different model.</param>
/// <param name="LlmRawResponsePreview">First ~600 chars of the last raw LLM response when
/// parsing failed; null otherwise.</param>
/// <param name="AbortedOnUnusableResponses">Set when the inner loop stopped because the model produced
/// several consecutive unusable responses, or because the pre-flight capability check failed.</param>
public sealed record HolisticHarmonizerRunResult(
    HarmonyBitmap OriginalBitmap,
    HarmonyBitmap FinalBitmap,
    IReadOnlyList<BatchEvaluation> Iterations,
    double FitnessBefore,
    double FitnessAfter,
    string LlmModelId,
    string? LlmParsingError,
    string? LlmRawResponsePreview,
    bool AbortedOnUnusableResponses = false)
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
}
