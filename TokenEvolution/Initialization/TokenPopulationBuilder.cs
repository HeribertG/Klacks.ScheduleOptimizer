// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Composes the initial population by mixing five strategies: warm-start (seeded from the last accepted
/// previous-period plan), auction (3-layer Dirigent/Agent/Controller), coverage-first, greedy and random.
/// Warm-start biases the start towards familiar prior patterns, auction drives fuzzy-aware seeding,
/// coverage-first ensures slot coverage, greedy drives contract-fill efficiency, random injects diversity.
/// Only the auction and warm-start ratios are autoresearch-trainable (wired through TokenEvolutionConfig);
/// the coverage-first and greedy ratios are fixed init properties. The remainder is filled with random.
/// </summary>
/// <param name="auction">Auction strategy (Phase 1: hunger-only, Phase 2: fuzzy-driven)</param>
/// <param name="coverageFirst">Coverage-first strategy that fills every valid slot deterministically</param>
/// <param name="greedy">Greedy strategy for contract-driven assignments</param>
/// <param name="random">Random strategy for diversity</param>
/// <param name="warmStart">Warm-start strategy that seeds from the previous period's accepted plan</param>
public sealed class TokenPopulationBuilder
{
    /// <summary>Upper cap on the warm-start share so auction/random stay substantial and the GA cannot converge prematurely onto the old pattern.</summary>
    private const double MaxWarmStartRatio = 0.4;

    private readonly ITokenPopulationStrategy _auction;
    private readonly ITokenPopulationStrategy _coverageFirst;
    private readonly ITokenPopulationStrategy _greedy;
    private readonly ITokenPopulationStrategy _random;
    private readonly ITokenPopulationStrategy _warmStart;

    public TokenPopulationBuilder(
        ITokenPopulationStrategy auction,
        ITokenPopulationStrategy coverageFirst,
        ITokenPopulationStrategy greedy,
        ITokenPopulationStrategy random,
        ITokenPopulationStrategy warmStart)
    {
        _auction = auction;
        _coverageFirst = coverageFirst;
        _greedy = greedy;
        _random = random;
        _warmStart = warmStart;
    }

    /// <summary>Share of auction scenarios in the initial population (0..1). Default 0.5 in Phase 1 live-test.</summary>
    public double InitAuctionRatio { get; init; } = 0.5;

    /// <summary>Share of coverage-first scenarios in the initial population (0..1). Default 0.3.</summary>
    public double InitCoverageFirstRatio { get; init; } = 0.3;

    /// <summary>Share of greedy scenarios in the initial population (0..1). Default 0.1.</summary>
    public double InitGreedyRatio { get; init; } = 0.1;

    /// <summary>Share of warm-start scenarios in the initial population (0..0.4; clamped). Default 0.2.</summary>
    public double InitWarmStartRatio { get; init; } = 0.2;

    public IReadOnlyList<CoreScenario> BuildPopulation(
        CoreWizardContext context,
        int populationSize,
        Random rng,
        CancellationToken cancellationToken = default,
        Action<string>? trace = null,
        double? auctionRatioOverride = null,
        double? warmStartRatioOverride = null)
    {
        var effectiveWarmStartRatio = context.WarmStartAssignments.Count == 0
            ? 0d
            : Math.Clamp(warmStartRatioOverride ?? InitWarmStartRatio, 0d, MaxWarmStartRatio);
        var effectiveAuctionRatio = auctionRatioOverride is { } v
            ? Math.Clamp(v, 0d, 1d)
            : InitAuctionRatio;
        effectiveAuctionRatio = Math.Max(0d, effectiveAuctionRatio - effectiveWarmStartRatio);

        var warmStartCount = Math.Min(populationSize, (int)Math.Round(populationSize * effectiveWarmStartRatio));
        var auctionCount = Math.Min(populationSize - warmStartCount, (int)Math.Round(populationSize * effectiveAuctionRatio));
        var coverageCount = Math.Min(populationSize - warmStartCount - auctionCount, (int)Math.Round(populationSize * InitCoverageFirstRatio));
        var greedyCount = Math.Min(populationSize - warmStartCount - auctionCount - coverageCount, (int)Math.Round(populationSize * InitGreedyRatio));
        var randomCount = populationSize - warmStartCount - auctionCount - coverageCount - greedyCount;
        var result = new List<CoreScenario>(populationSize);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        trace?.Invoke($"BuildPopulation: warmStart={warmStartCount} auction={auctionCount} coverageFirst={coverageCount} greedy={greedyCount} random={randomCount}");

        var tw = sw.ElapsedMilliseconds;
        for (var i = 0; i < warmStartCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(_warmStart.BuildScenario(context, rng));
        }
        trace?.Invoke($"BuildPopulation: warmStart {warmStartCount} scenarios in {sw.ElapsedMilliseconds - tw}ms");

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
