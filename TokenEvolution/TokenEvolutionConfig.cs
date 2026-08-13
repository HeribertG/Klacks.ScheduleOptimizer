// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Tuning parameters for the token-based evolution loop.
/// All values are autoresearch-trainable within the documented ranges.
/// </summary>
public sealed record TokenEvolutionConfig
{
    private const double DefaultInitWarmStartRatio = 0.2;

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

    /// <summary>Weight of the package-consolidation trade (rule 6): dissolves a short package through an equal-hours swap onto a date that extends another package of the same agent. Additive to the other five weights, so their mutual ratios stay untouched; 0 removes the operator and reproduces the pre-operator draw sequence.</summary>
    public double MutationWeightConsolidate { get; init; } = 0.15;

    /// <summary>Weight of the ruin-and-recreate macro move: one contiguous RuinWindowMinDays..RuinWindowMaxDays calendar window loses every non-locked token and the package-aware coverage sweep rebuilds it; only the end state is compared. Additive; 0 removes the move and replays the prior draw sequence byte-identically.</summary>
    public double MutationWeightRuinRecreate { get; init; } = 0.10;

    /// <summary>Smallest ruined window in calendar days. Deliberately several days: destroying too little cannot escape a basin in tightly coupled rosters (Legrain, INRC-II).</summary>
    public int RuinWindowMinDays { get; init; } = 5;

    /// <summary>Largest ruined window in calendar days.</summary>
    public int RuinWindowMaxDays { get; init; } = 10;

    /// <summary>Weight of the play-sequence transaction: a child plays PlaySequenceMinSteps..PlaySequenceMaxSteps draws from the single-operator pool in a row and ONLY the end state enters the lexicographic comparison — the literature-standard transactional move (SSHH acceptance at sequence end, memetic child polishing) against single-move greedy attractors. Additive; 0 removes the transaction and replays the single-draw sequence byte-identically.</summary>
    public double MutationWeightPlaySequence { get; init; } = 0.15;

    /// <summary>Smallest number of single-operator draws inside one play-sequence transaction.</summary>
    public int PlaySequenceMinSteps { get; init; } = 2;

    /// <summary>Largest number of single-operator draws inside one play-sequence transaction.</summary>
    public int PlaySequenceMaxSteps { get; init; } = 4;

    public int EarlyStopNoImprovementGenerations { get; init; } = 30;

    public int RandomSeed { get; init; } = 0;

    /// <summary>
    /// Degree of parallelism for scoring a generation. 0 uses <see cref="Environment.ProcessorCount"/>,
    /// 1 evaluates sequentially (reference mode for determinism proofs), any other value pins the degree.
    /// The result never depends on this: breeding stays sequential and evaluation draws no randomness.
    /// </summary>
    public int EvaluationParallelism { get; init; } = 0;

    /// <summary>Share of auction-built scenarios in the initial population (0..1). Default 0.5.</summary>
    public double InitAuctionRatio { get; init; } = 0.5;

    /// <summary>Share of warm-start scenarios (seeded from the last accepted previous-period plan) in the initial population (0..0.4; clamped). Default 0.2.</summary>
    public double InitWarmStartRatio { get; init; } = DefaultInitWarmStartRatio;

    /// <summary>Stage-1 exponential decay factor per roster rank (0..1). Weights WHO reaches the guaranteed hours: 1.0 = every agent counts equally (index-blind), lower = satisfying top-roster agents dominates. Implements the top-down roster rule.</summary>
    public double FitnessStage1RankDecay { get; init; } = 0.85;

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

    /// <summary>Stage-3 weight for holding the carried-in packages that are still open at the period start. Ignored, weight included, when the context has no open package.</summary>
    public double FitnessStage3CarryIn { get; init; } = 0.2;

    /// <summary>Stage-3 weight for package compactness (rule 6): the share of calendar packages longer than the short-package bound. Guards the compact auction seeds against splintering by swap and crossover offspring. Doubling to 0.8 was measured on 2026-08-13 and REJECTED: it bought only scenario-2 polish, erased the whole scenario-1b sequence win (0.14 back to 0.27) and pushed fairness/attribution edges out of the selection (Sz3 spread and A24 rips) — the stage-3 balance tips before the compactness defence pays.</summary>
    public double FitnessStage3PackageLength { get; init; } = 0.4;

    /// <summary>Optional soft wall-clock budget. When exceeded the loop stops at the next generation boundary and returns the best solution found so far. Null = no time limit.</summary>
    public TimeSpan? MaxRuntime { get; init; }
}
