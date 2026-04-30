// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Tuning parameters for the token-based evolution loop.
/// All values are autoresearch-trainable within the documented ranges.
/// </summary>
public sealed record TokenEvolutionConfig
{
    public int PopulationSize { get; init; } = 50;

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

    /// <summary>Share of auction-built scenarios in the initial population (0..1). Default 0.5.</summary>
    public double InitAuctionRatio { get; init; } = 0.5;

    /// <summary>Stage-2 exponential decay factor per rank position (0..1). Lower = steeper priority towards top-ranked agents.</summary>
    public double FitnessStage2Decay { get; init; } = 0.7;

    /// <summary>Stage-3 weight for block temporal ordering (later shifts should follow earlier).</summary>
    public double FitnessStage3BlockOrder { get; init; } = 0.4;

    /// <summary>Stage-3 weight for avoiding blacklisted shift preferences.</summary>
    public double FitnessStage3Blacklist { get; init; } = 0.3;

    /// <summary>Stage-3 weight for location continuity across consecutive tokens.</summary>
    public double FitnessStage3Location { get; init; } = 0.2;

    /// <summary>Stage-3 weight for staying within the optimal intra-day gap between tokens.</summary>
    public double FitnessStage3MaxGap { get; init; } = 0.1;
}
