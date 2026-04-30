// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction;
using Klacks.ScheduleOptimizer.TokenEvolution.Constraints;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;
using Klacks.ScheduleOptimizer.TokenEvolution.Operators;

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Genetic algorithm loop for token-based scheduling. Maintains a population, selects via tournament,
/// applies crossover + weighted mutation, preserves elites and reports progress per generation.
/// Runs a coverage-sweep on the best scenario after every generation to drive UnderSupply to zero
/// as long as it is theoretically reachable.
/// </summary>
public sealed class TokenEvolutionLoop
{
    private readonly TokenPopulationBuilder _populationBuilder;
    private readonly BlockCrossover _crossover;
    private readonly TokenSwapMutation _swap;
    private readonly BlockSplitMutation _split;
    private readonly BlockMergeMutation _merge;
    private readonly ReassignMutation _reassign;
    private readonly TokenRepair _repair;

    public TokenEvolutionLoop(
        TokenPopulationBuilder populationBuilder,
        BlockCrossover crossover,
        TokenSwapMutation swap,
        BlockSplitMutation split,
        BlockMergeMutation merge,
        ReassignMutation reassign,
        TokenRepair repair)
    {
        _populationBuilder = populationBuilder;
        _crossover = crossover;
        _swap = swap;
        _split = split;
        _merge = merge;
        _reassign = reassign;
        _repair = repair;
    }

    public static TokenEvolutionLoop Create(TokenConstraintChecker? checker = null)
    {
        var realChecker = checker ?? new TokenConstraintChecker();
        var builder = new TokenPopulationBuilder(
            new AuctionTokenStrategy(),
            new CoverageFirstTokenStrategy(),
            new GreedyTokenStrategy(),
            new RandomTokenStrategy());
        return new TokenEvolutionLoop(
            builder,
            new BlockCrossover(),
            new TokenSwapMutation(),
            new BlockSplitMutation(),
            new BlockMergeMutation(),
            new ReassignMutation(),
            new TokenRepair(realChecker));
    }

    public CoreScenario Run(
        CoreWizardContext context,
        TokenEvolutionConfig config,
        IProgress<TokenEvolutionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Action<string>? trace = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        trace?.Invoke($"Run: enter (population={config.PopulationSize}, maxGen={config.MaxGenerations}, agents={context.Agents.Count}, shifts={context.Shifts.Count}, lockedWorks={context.LockedWorks.Count})");

        var rng = new Random(config.RandomSeed);
        var evaluator = TokenFitnessEvaluator.Create(context, config);

        var t0 = sw.ElapsedMilliseconds;
        var population = _populationBuilder
            .BuildPopulation(context, config.PopulationSize, rng, cancellationToken, trace, config.InitAuctionRatio)
            .ToList();
        trace?.Invoke($"Run: BuildPopulation done in {sw.ElapsedMilliseconds - t0}ms ({population.Count} scenarios)");
        cancellationToken.ThrowIfCancellationRequested();

        t0 = sw.ElapsedMilliseconds;
        var idx = 0;
        foreach (var scenario in population)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluator.Evaluate(scenario, context);
            idx++;
            if (idx % 10 == 0)
            {
                trace?.Invoke($"Run: initial Evaluate {idx}/{population.Count} at {sw.ElapsedMilliseconds - t0}ms");
            }
        }
        trace?.Invoke($"Run: initial Evaluate done in {sw.ElapsedMilliseconds - t0}ms");

        var best = SelectBest(population, evaluator);
        var generationsNoImprovement = 0;

        for (var generation = 1; generation <= config.MaxGenerations; generation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var genStart = sw.ElapsedMilliseconds;

            var next = population
                .OrderBy(s => s, evaluator)
                .Take(config.ElitismCount)
                .ToList();

            while (next.Count < config.PopulationSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var p1 = TournamentSelect(population, config.TournamentK, evaluator, rng);
                var p2 = TournamentSelect(population, config.TournamentK, evaluator, rng);

                var child = rng.NextDouble() < config.CrossoverRate
                    ? _crossover.Apply(new TokenOperatorContext(p1, p2, context, rng))
                    : CloneScenario(p1);

                if (rng.NextDouble() < config.MutationRate)
                {
                    child = ApplyWeightedMutation(child, context, rng, config);
                }

                evaluator.Evaluate(child, context);
                next.Add(child);
            }

            population = next;
            var currentBest = SelectBest(population, evaluator);

            var sweepStart = sw.ElapsedMilliseconds;
            currentBest = RunCoverageSweep(currentBest, context, rng, evaluator, cancellationToken, trace);
            var sweepMs = sw.ElapsedMilliseconds - sweepStart;

            if (evaluator.Compare(currentBest, best) < 0)
            {
                best = currentBest;
                generationsNoImprovement = 0;
            }
            else
            {
                generationsNoImprovement++;
            }

            var willStop = generationsNoImprovement >= config.EarlyStopNoImprovementGenerations
                           || generation == config.MaxGenerations;
            progress?.Report(new TokenEvolutionProgress(
                Generation: generation,
                MaxGenerations: config.MaxGenerations,
                BestHardViolations: best.FitnessStage0,
                BestStage1Completion: best.FitnessStage1,
                BestStage2Score: best.FitnessStage2,
                EarlyStopping: willStop));

            var genMs = sw.ElapsedMilliseconds - genStart;
            if (generation <= 3 || generation % 10 == 0 || willStop)
            {
                trace?.Invoke($"Run: gen={generation}/{config.MaxGenerations} took {genMs}ms (sweep={sweepMs}ms) tokens={best.Tokens.Count} stage1={best.FitnessStage1 * 100:F1}%");
            }

            if (generationsNoImprovement >= config.EarlyStopNoImprovementGenerations)
            {
                break;
            }
        }

        trace?.Invoke($"Run: total {sw.ElapsedMilliseconds}ms");
        return best;
    }

    private CoreScenario ApplyWeightedMutation(
        CoreScenario child, CoreWizardContext context, Random rng, TokenEvolutionConfig config)
    {
        var total = config.MutationWeightSwap + config.MutationWeightSplit + config.MutationWeightMerge
                    + config.MutationWeightReassign + config.MutationWeightRepair;
        if (total <= 0)
        {
            return child;
        }

        var pick = rng.NextDouble() * total;
        var cumulative = 0.0;

        cumulative += config.MutationWeightSwap;
        if (pick < cumulative)
        {
            return _swap.Apply(new TokenOperatorContext(child, null, context, rng));
        }

        cumulative += config.MutationWeightSplit;
        if (pick < cumulative)
        {
            return _split.Apply(new TokenOperatorContext(child, null, context, rng));
        }

        cumulative += config.MutationWeightMerge;
        if (pick < cumulative)
        {
            return _merge.Apply(new TokenOperatorContext(child, null, context, rng));
        }

        cumulative += config.MutationWeightReassign;
        if (pick < cumulative)
        {
            return _reassign.Apply(new TokenOperatorContext(child, null, context, rng));
        }

        return _repair.Apply(new TokenOperatorContext(child, null, context, rng));
    }

    private static CoreScenario TournamentSelect(
        IReadOnlyList<CoreScenario> population,
        int k,
        IComparer<CoreScenario> comparer,
        Random rng)
    {
        if (population.Count == 0)
        {
            throw new InvalidOperationException("Population must not be empty.");
        }

        var picks = Math.Min(k, population.Count);
        var best = population[rng.Next(population.Count)];
        for (var i = 1; i < picks; i++)
        {
            var candidate = population[rng.Next(population.Count)];
            if (comparer.Compare(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static CoreScenario SelectBest(IReadOnlyList<CoreScenario> population, IComparer<CoreScenario> comparer)
    {
        return population.OrderBy(s => s, comparer).First();
    }

    private static CoreScenario CloneScenario(CoreScenario source)
    {
        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = source.Tokens.ToList(),
            FitnessStage0 = source.FitnessStage0,
            FitnessStage1 = source.FitnessStage1,
            FitnessStage2 = source.FitnessStage2,
            FitnessStage3 = source.FitnessStage3,
            FitnessStage4 = source.FitnessStage4,
            Fitness = source.Fitness,
            HardViolations = source.HardViolations,
        };
    }

    private CoreScenario RunCoverageSweep(
        CoreScenario scenario,
        CoreWizardContext context,
        Random rng,
        TokenFitnessEvaluator evaluator,
        CancellationToken cancellationToken = default,
        Action<string>? trace = null)
    {
        var filled = _repair.FillAllUnderSupply(scenario, context, rng, cancellationToken, trace);
        if (filled.Tokens.Count != scenario.Tokens.Count)
        {
            evaluator.Evaluate(filled, context);
        }

        return filled;
    }
}
