// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

/// <summary>
/// Result of evaluating a single <see cref="MutationBatch"/> against the working bitmap.
/// Carries everything the inner loop needs: which steps survived, why others did not, the
/// score delta, and where the batch broke off — used to build reject-memory feedback for
/// the next LLM call.
/// </summary>
/// <param name="BatchId">The evaluated batch's id (matches <see cref="MutationBatch.BatchId"/>).</param>
/// <param name="Intent">Intent label copied from the batch for logging convenience.</param>
/// <param name="Result">Acceptance category — see <see cref="BatchAcceptance"/>.</param>
/// <param name="AppliedSteps">Steps that were ultimately kept on the bitmap. Empty when Rejected
/// or WouldDegrade. Equal to the full Steps list when Accepted; a prefix when PartiallyAccepted.</param>
/// <param name="Rejections">Per-step rejection records for steps that failed hard constraints.</param>
/// <param name="StoppedAtStep">Zero-based index of the first failing step, or null if all steps
/// passed hard constraints (the batch may still have been reverted via Score-Greedy).</param>
/// <param name="ScoreBefore">Fitness of the working bitmap before applying the batch.</param>
/// <param name="ScoreAfter">Fitness after the batch: the committed end-state when Accepted or
/// PartiallyAccepted, the real (lower) end-state score of the reverted prefix when WouldDegrade, and the
/// unchanged start score when Rejected (nothing was applied).</param>
/// <remarks>RevertedSteps: steps that passed hard validation and the committee, were applied and then
/// rolled back by Score-Greedy. Non-empty only for WouldDegrade; never part of the committed plan.</remarks>
public sealed record BatchEvaluation(
    Guid BatchId,
    string Intent,
    BatchAcceptance Result,
    IReadOnlyList<PlanCellSwap> AppliedSteps,
    IReadOnlyList<PlanMutationRejection> Rejections,
    int? StoppedAtStep,
    double ScoreBefore,
    double ScoreAfter)
{
    public IReadOnlyList<PlanCellSwap> RevertedSteps { get; init; } = [];
}
