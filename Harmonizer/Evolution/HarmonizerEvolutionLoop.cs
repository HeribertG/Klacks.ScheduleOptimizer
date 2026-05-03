// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Genetic optimiser around the deterministic HarmonizerConductor. Each individual is a
/// bitmap variant; offspring are produced by stochastic mutation followed by a conductor
/// pass. Selection uses tournaments; the top elites carry over unchanged. Stops on
/// stagnation or when the configured maximum generation count is reached.
/// </summary>
public sealed class HarmonizerEvolutionLoop
{
    private readonly HarmonyFitnessEvaluator _fitness;
    private readonly StochasticBitmapMutation _stochasticMutation;
    private readonly Func<int, HarmonizerConductor> _conductorFactory;
    private readonly HarmonizerEvolutionConfig _config;

    public HarmonizerEvolutionLoop(
        HarmonyFitnessEvaluator fitness,
        StochasticBitmapMutation stochasticMutation,
        Func<int, HarmonizerConductor> conductorFactory,
        HarmonizerEvolutionConfig config)
    {
        _fitness = fitness;
        _stochasticMutation = stochasticMutation;
        _conductorFactory = conductorFactory;
        _config = config;
    }

    public EvolutionResult Run(
        HarmonyBitmap seed,
        IProgress<EvolutionGenerationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var random = _config.Seed.HasValue ? new Random(_config.Seed.Value) : new Random();
        var population = InitialisePopulation(seed, random, ct);
        var generationFitness = new List<double>(_config.MaxGenerations + 1);
        var bestSoFar = population[0];
        generationFitness.Add(bestSoFar.Fitness);
        progress?.Report(new EvolutionGenerationProgress(0, _config.MaxGenerations, bestSoFar.Fitness, false));
        var stagnation = 0;

        for (var generation = 0; generation < _config.MaxGenerations; generation++)
        {
            ct.ThrowIfCancellationRequested();
            var nextGen = new List<Individual>(_config.PopulationSize);
            for (var e = 0; e < _config.EliteCount && e < population.Count; e++)
            {
                nextGen.Add(population[e]);
            }

            while (nextGen.Count < _config.PopulationSize)
            {
                ct.ThrowIfCancellationRequested();
                var parent = SelectByTournament(population, random);
                var child = ProduceChild(parent, random, ct);
                nextGen.Add(child);
            }

            nextGen.Sort(static (a, b) => b.Fitness.CompareTo(a.Fitness));
            population = nextGen;
            generationFitness.Add(population[0].Fitness);

            var earlyStopping = false;
            if (population[0].Fitness > bestSoFar.Fitness)
            {
                bestSoFar = population[0];
                stagnation = 0;
            }
            else
            {
                stagnation++;
                earlyStopping = stagnation >= _config.StagnationGenerations;
            }

            progress?.Report(new EvolutionGenerationProgress(generation + 1, _config.MaxGenerations, population[0].Fitness, earlyStopping));

            if (earlyStopping)
            {
                break;
            }
        }

        return new EvolutionResult(bestSoFar, generationFitness);
    }

    private List<Individual> InitialisePopulation(HarmonyBitmap seed, Random random, CancellationToken ct)
    {
        var population = new List<Individual>(_config.PopulationSize)
        {
            EvaluateConductorPass(BitmapCloner.Clone(seed), random, ct),
        };

        while (population.Count < _config.PopulationSize)
        {
            ct.ThrowIfCancellationRequested();
            var clone = BitmapCloner.Clone(seed);
            _stochasticMutation.Apply(clone, _config.StochasticMutationsPerOffspring, random);
            population.Add(EvaluateConductorPass(clone, random, ct));
        }

        population.Sort(static (a, b) => b.Fitness.CompareTo(a.Fitness));
        return population;
    }

    private Individual SelectByTournament(IReadOnlyList<Individual> population, Random random)
    {
        Individual? best = null;
        for (var i = 0; i < _config.TournamentSize; i++)
        {
            var contender = population[random.Next(population.Count)];
            if (best is null || contender.Fitness > best.Fitness)
            {
                best = contender;
            }
        }
        return best!;
    }

    private Individual ProduceChild(Individual parent, Random random, CancellationToken ct)
    {
        var child = BitmapCloner.Clone(parent.Bitmap);
        _stochasticMutation.Apply(child, _config.StochasticMutationsPerOffspring, random);
        return EvaluateConductorPass(child, random, ct);
    }

    private Individual EvaluateConductorPass(HarmonyBitmap bitmap, Random random, CancellationToken ct)
    {
        var conductor = _conductorFactory(bitmap.RowCount);
        var conductorResult = conductor.Run(bitmap, ct);
        var fitness = _fitness.Evaluate(bitmap);
        return new Individual(bitmap, fitness.Fitness, fitness.RowScores, conductorResult);
    }
}

/// <param name="Generation">Current generation index (1-based; 0 means initial population)</param>
/// <param name="MaxGenerations">Configured maximum</param>
/// <param name="BestFitness">Best fitness in the current generation</param>
/// <param name="EarlyStopping">True when stagnation triggered the loop to terminate after this generation</param>
public sealed record EvolutionGenerationProgress(int Generation, int MaxGenerations, double BestFitness, bool EarlyStopping);

/// <param name="Best">The best individual found across all generations</param>
/// <param name="GenerationFitness">Best fitness per generation, indexed from generation 0 (initial population)</param>
public sealed record EvolutionResult(Individual Best, IReadOnlyList<double> GenerationFitness);
