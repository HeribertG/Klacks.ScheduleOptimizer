// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Progress snapshot emitted by <see cref="TokenEvolutionLoop"/> after each generation.
/// </summary>
/// <param name="Generation">Zero-based generation counter</param>
/// <param name="MaxGenerations">Configured upper bound</param>
/// <param name="BestHardViolations">Stage-0 value of the current best scenario</param>
/// <param name="BestStage1Completion">Stage-1 completion rate of the current best</param>
/// <param name="BestStage2Score">Stage-2 score of the current best</param>
/// <param name="EarlyStopping">True if the loop is about to terminate due to no-improvement plateau</param>
public sealed record TokenEvolutionProgress(
    int Generation,
    int MaxGenerations,
    int BestHardViolations,
    double BestStage1Completion,
    double BestStage2Score,
    bool EarlyStopping);
