// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Evolution algorithm configuration with tunable parameters.
/// </summary>
/// <param name="PopulationSize">Number of solutions per generation</param>
/// <param name="MaxGenerations">Maximum evolution iterations</param>
/// <param name="EliteCount">Number of best solutions kept unchanged</param>
/// <param name="MutationRate">Probability of mutation (0-1)</param>
/// <param name="CrossoverRate">Probability of crossover (0-1)</param>
/// <param name="ConvergenceThreshold">Min improvement to continue</param>
/// <param name="StagnationLimit">Generations without improvement before stop</param>
/// <param name="TargetFitness">Stop when this fitness is reached</param>
/// <param name="TimeLimitMs">Max runtime in milliseconds</param>
/// <param name="WarmStartRatio">Ratio of greedy vs random initial solutions</param>
/// <param name="RandomSeed">Optional seed for deterministic runs</param>

using System.Text.Json.Serialization;

namespace Klacks.ScheduleOptimizer.Models;

public class CoreConfig
{
    [JsonPropertyName("populationSize")]
    public int PopulationSize { get; set; } = 50;

    [JsonPropertyName("maxGenerations")]
    public int MaxGenerations { get; set; } = 200;

    [JsonPropertyName("eliteCount")]
    public int EliteCount { get; set; } = 2;

    [JsonPropertyName("mutationRate")]
    public double MutationRate { get; set; } = 0.8;

    [JsonPropertyName("crossoverRate")]
    public double CrossoverRate { get; set; } = 0.7;

    [JsonPropertyName("convergenceThreshold")]
    public double ConvergenceThreshold { get; set; } = 0.001;

    [JsonPropertyName("stagnationLimit")]
    public int StagnationLimit { get; set; } = 40;

    [JsonPropertyName("targetFitness")]
    public double TargetFitness { get; set; } = 1.0;

    [JsonPropertyName("timeLimitMs")]
    public int TimeLimitMs { get; set; } = 15000;

    [JsonPropertyName("warmStartRatio")]
    public double WarmStartRatio { get; set; } = 0.7;

    [JsonPropertyName("randomSeed")]
    public int? RandomSeed { get; set; }
}
