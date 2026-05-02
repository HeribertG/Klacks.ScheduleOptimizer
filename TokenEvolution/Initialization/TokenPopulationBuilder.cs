// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Composes the initial population by mixing four strategies: auction (3-layer Dirigent/Agent/Controller),
/// coverage-first, greedy and random. Auction drives the new fuzzy-aware seeding, coverage-first ensures
/// slot coverage, greedy drives contract-fill efficiency, random injects diversity. Ratios are
/// autoresearch-trainable; the remainder after auction/coverage/greedy is filled with random.
/// </summary>
/// <param name="auction">Auction strategy (Phase 1: hunger-only, Phase 2: fuzzy-driven)</param>
/// <param name="coverageFirst">Coverage-first strategy that fills every valid slot deterministically</param>
/// <param name="greedy">Greedy strategy for contract-driven assignments</param>
/// <param name="random">Random strategy for diversity</param>
public sealed class TokenPopulationBuilder
{
    private readonly ITokenPopulationStrategy _auction;
    private readonly ITokenPopulationStrategy _coverageFirst;
    private readonly ITokenPopulationStrategy _greedy;
    private readonly ITokenPopulationStrategy _random;

    public TokenPopulationBuilder(
        ITokenPopulationStrategy auction,
        ITokenPopulationStrategy coverageFirst,
        ITokenPopulationStrategy greedy,
        ITokenPopulationStrategy random)
    {
        _auction = auction;
        _coverageFirst = coverageFirst;
        _greedy = greedy;
        _random = random;
    }

    /// <summary>Share of auction scenarios in the initial population (0..1). Default 0.5 in Phase 1 live-test.</summary>
    public double InitAuctionRatio { get; init; } = 0.5;

    /// <summary>Share of coverage-first scenarios in the initial population (0..1). Default 0.3.</summary>
    public double InitCoverageFirstRatio { get; init; } = 0.3;

    /// <summary>Share of greedy scenarios in the initial population (0..1). Default 0.1.</summary>
    public double InitGreedyRatio { get; init; } = 0.1;

    public IReadOnlyList<CoreScenario> BuildPopulation(
        CoreWizardContext context,
        int populationSize,
        Random rng,
        CancellationToken cancellationToken = default,
        Action<string>? trace = null,
        double? auctionRatioOverride = null)
    {
        var effectiveAuctionRatio = auctionRatioOverride is { } v
            ? Math.Clamp(v, 0d, 1d)
            : InitAuctionRatio;
        var auctionCount = Math.Min(populationSize, (int)Math.Round(populationSize * effectiveAuctionRatio));
        var coverageCount = Math.Min(populationSize - auctionCount, (int)Math.Round(populationSize * InitCoverageFirstRatio));
        var greedyCount = Math.Min(populationSize - auctionCount - coverageCount, (int)Math.Round(populationSize * InitGreedyRatio));
        var randomCount = populationSize - auctionCount - coverageCount - greedyCount;
        var result = new List<CoreScenario>(populationSize);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        trace?.Invoke($"BuildPopulation: auction={auctionCount} coverageFirst={coverageCount} greedy={greedyCount} random={randomCount}");

        var t0 = sw.ElapsedMilliseconds;
        for (var i = 0; i < auctionCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(_auction.BuildScenario(context, rng));
        }
        trace?.Invoke($"BuildPopulation: auction {auctionCount} scenarios in {sw.ElapsedMilliseconds - t0}ms");

        t0 = sw.ElapsedMilliseconds;
        for (var i = 0; i < coverageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(_coverageFirst.BuildScenario(context, rng));
        }
        trace?.Invoke($"BuildPopulation: coverageFirst {coverageCount} scenarios in {sw.ElapsedMilliseconds - t0}ms");

        t0 = sw.ElapsedMilliseconds;
        for (var i = 0; i < greedyCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(_greedy.BuildScenario(context, rng));
        }
        trace?.Invoke($"BuildPopulation: greedy {greedyCount} scenarios in {sw.ElapsedMilliseconds - t0}ms");

        t0 = sw.ElapsedMilliseconds;
        for (var i = 0; i < randomCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(_random.BuildScenario(context, rng));
        }
        trace?.Invoke($"BuildPopulation: random {randomCount} scenarios in {sw.ElapsedMilliseconds - t0}ms");

        return result;
    }
}
