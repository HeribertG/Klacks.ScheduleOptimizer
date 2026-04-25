// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Composes the initial population by mixing three strategies: coverage-first, greedy and random.
/// Coverage-first drives full slot coverage, greedy drives contract-fill efficiency, random injects
/// diversity. Ratios are autoresearch-trainable via <see cref="InitCoverageFirstRatio"/> and
/// <see cref="InitGreedyRatio"/>; the remainder is filled with the random strategy.
/// </summary>
/// <param name="coverageFirst">Coverage-first strategy that fills every valid slot deterministically</param>
/// <param name="greedy">Greedy strategy for contract-driven assignments</param>
/// <param name="random">Random strategy for diversity</param>
public sealed class TokenPopulationBuilder
{
    private readonly ITokenPopulationStrategy _coverageFirst;
    private readonly ITokenPopulationStrategy _greedy;
    private readonly ITokenPopulationStrategy _random;

    public TokenPopulationBuilder(
        ITokenPopulationStrategy coverageFirst,
        ITokenPopulationStrategy greedy,
        ITokenPopulationStrategy random)
    {
        _coverageFirst = coverageFirst;
        _greedy = greedy;
        _random = random;
    }

    /// <summary>Share of coverage-first scenarios in the initial population (0..1). Default 0.5.</summary>
    public double InitCoverageFirstRatio { get; init; } = 0.5;

    /// <summary>Share of greedy scenarios in the initial population (0..1). Default 0.2.</summary>
    public double InitGreedyRatio { get; init; } = 0.2;

    public IReadOnlyList<CoreScenario> BuildPopulation(
        CoreWizardContext context, int populationSize, Random rng)
    {
        var coverageCount = Math.Min(populationSize, (int)Math.Round(populationSize * InitCoverageFirstRatio));
        var greedyCount = Math.Min(populationSize - coverageCount, (int)Math.Round(populationSize * InitGreedyRatio));
        var randomCount = populationSize - coverageCount - greedyCount;
        var result = new List<CoreScenario>(populationSize);

        for (var i = 0; i < coverageCount; i++)
        {
            result.Add(_coverageFirst.BuildScenario(context, rng));
        }

        for (var i = 0; i < greedyCount; i++)
        {
            result.Add(_greedy.BuildScenario(context, rng));
        }

        for (var i = 0; i < randomCount; i++)
        {
            result.Add(_random.BuildScenario(context, rng));
        }

        return result;
    }
}
