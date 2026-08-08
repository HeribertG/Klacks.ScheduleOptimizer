// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Operators;

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
    private readonly TopDownHandover _handover = new();

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
        AddDeterministicStrategy(_auction, auctionCount, result, context, rng, cancellationToken);
        trace?.Invoke($"BuildPopulation: auction 1 base + {Math.Max(0, auctionCount - 1)} perturbed clones in {sw.ElapsedMilliseconds - t0}ms");

        t0 = sw.ElapsedMilliseconds;
        AddDeterministicStrategy(_coverageFirst, coverageCount, result, context, rng, cancellationToken);
        trace?.Invoke($"BuildPopulation: coverageFirst 1 base + {Math.Max(0, coverageCount - 1)} perturbed clones in {sw.ElapsedMilliseconds - t0}ms");

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

        t0 = sw.ElapsedMilliseconds;
        ServeGuaranteedHoursTopDown(result, context, cancellationToken);
        trace?.Invoke($"BuildPopulation: top-down handover over {result.Count} scenarios in {sw.ElapsedMilliseconds - t0}ms");

        return result;
    }

    /// <summary>
    /// Serves the guaranteed hours top-down (rule 5) in every seeded plan. None of the five strategies
    /// builds that distribution: the auction awards by bid score, coverage-first by the largest
    /// remaining target — both of which balance the load instead of filling the roster from the top —
    /// and the one strategy that does fill top-down, greedy, force-assigns its leftover slots and is
    /// eliminated on the hard stage. Rewriting the owners afterwards keeps each strategy's own block
    /// geometry, which is what carries the 5/2 structure, and makes the whole start population a
    /// top-down one instead of leaving the rule to a single elite.
    /// </summary>
    /// <param name="population">Scenarios built by the strategies; entries are replaced in place</param>
    /// <param name="context">Wizard context supplying the roster order and the rules</param>
    /// <param name="cancellationToken">Cancellation of the surrounding build</param>
    private void ServeGuaranteedHoursTopDown(
        List<CoreScenario> population, CoreWizardContext context, CancellationToken cancellationToken)
    {
        for (var i = 0; i < population.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            population[i] = _handover.Apply(population[i], context);
        }
    }

    private static void AddDeterministicStrategy(
        ITokenPopulationStrategy strategy,
        int count,
        List<CoreScenario> result,
        CoreWizardContext context,
        Random rng,
        CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var basis = strategy.BuildScenario(context, rng);
        result.Add(basis);

        for (var i = 1; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(ScenarioPerturbator.Perturb(basis, rng));
        }
    }
}
