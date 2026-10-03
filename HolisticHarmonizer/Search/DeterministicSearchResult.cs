// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search;

/// <param name="AppliedBatches">Evaluations of the batches that were applied, in order (never WouldDegrade or Rejected).</param>
/// <param name="FitnessBefore">Fitness of the input bitmap.</param>
/// <param name="FitnessAfter">Fitness of the bitmap after the search; never below <paramref name="FitnessBefore"/>.</param>
/// <param name="StopReason">Why the winning pass ended, or WallClockBudget when the budget cut any pass.</param>
/// <param name="IterationsRun">Iterations of the winning pass (including the last one that found nothing).</param>
/// <param name="Evaluations">Batch evaluations performed over all passes (singles + pairs).</param>
/// <param name="MaxCandidatesPerIteration">Largest single-move neighbourhood seen.</param>
/// <param name="ElapsedMs">Wall-clock duration of the search.</param>
public sealed record DeterministicSearchResult(
    IReadOnlyList<BatchEvaluation> AppliedBatches,
    double FitnessBefore,
    double FitnessAfter,
    DeterministicSearchStopReason StopReason,
    int IterationsRun,
    long Evaluations,
    int MaxCandidatesPerIteration,
    long ElapsedMs)
{
    public int RestartsRun { get; init; } = 1;

    public int BestRestart { get; init; }
}
