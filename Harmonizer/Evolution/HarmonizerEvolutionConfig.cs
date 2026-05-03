// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <param name="PopulationSize">Number of individual bitmaps held in each generation</param>
/// <param name="MaxGenerations">Hard upper bound on generations</param>
/// <param name="EliteCount">Number of top individuals carried over unchanged</param>
/// <param name="TournamentSize">Number of contestants drawn for each tournament selection</param>
/// <param name="StochasticMutationsPerOffspring">Random swap count applied before deterministic optimisation</param>
/// <param name="StagnationGenerations">Generations without improvement before early stop</param>
/// <param name="Seed">Deterministic seed for the random source; null for non-deterministic runs</param>
public sealed record HarmonizerEvolutionConfig(
    int PopulationSize = 16,
    int MaxGenerations = 40,
    int EliteCount = 2,
    int TournamentSize = 3,
    int StochasticMutationsPerOffspring = 4,
    int StagnationGenerations = 8,
    int? Seed = null);
