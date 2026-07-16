// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

/// <summary>
/// Full fitness decomposition for a single scenario: the five lexicographic stage aggregates plus the
/// Stage-3 and Stage-4 component breakdowns. Produced once per run for the winning individual by
/// <see cref="TokenFitnessEvaluator.EvaluateDetailed"/>; the run-end capture consumes it, no per-generation cost.
/// </summary>
/// <param name="Stage0">Hard-constraint violation count (0 is best)</param>
/// <param name="Stage1">GuaranteedHours coverage top-down, in [0..1]</param>
/// <param name="Stage2">FullTime / MaxPossible coverage top-down, in [0..1]</param>
/// <param name="Stage3">Aggregate soft-constraint score, in [0..1]</param>
/// <param name="Stage4">Aggregate cosmetic score, in [0..1]</param>
/// <param name="Stage3Components">Weighted contributors that recompose into <paramref name="Stage3"/></param>
/// <param name="Stage4Components">Unweighted contributors whose mean is <paramref name="Stage4"/></param>
public sealed record DetailedFitnessResult(
    int Stage0,
    double Stage1,
    double Stage2,
    double Stage3,
    double Stage4,
    Stage3Components Stage3Components,
    Stage4Components Stage4Components);
