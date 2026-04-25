// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Tuning parameters for the token-based evolution loop.
/// All values are autoresearch-trainable within the documented ranges.
/// </summary>
public sealed record TokenEvolutionConfig
{
    public int PopulationSize { get; init; } = 40;

    public int MaxGenerations { get; init; } = 200;

    public int TournamentK { get; init; } = 3;

    public double MutationRate { get; init; } = 0.25;

    public double CrossoverRate { get; init; } = 0.7;

    public int ElitismCount { get; init; } = 2;

    public double MutationWeightSwap { get; init; } = 0.30;

    public double MutationWeightSplit { get; init; } = 0.20;

    public double MutationWeightMerge { get; init; } = 0.15;

    public double MutationWeightReassign { get; init; } = 0.10;

    public double MutationWeightRepair { get; init; } = 0.25;

    public int EarlyStopNoImprovementGenerations { get; init; } = 30;

    public int RandomSeed { get; init; } = 0;
}
