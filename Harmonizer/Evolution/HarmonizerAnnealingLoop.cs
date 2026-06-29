// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Diagnostics;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Single-chain Simulated Annealing optimiser over the same bitmap state, move operator and
/// fitness seam as <see cref="HarmonizerEvolutionLoop"/>. Each step proposes a mutated
/// (optionally conductor-optimised) candidate and accepts it by the Metropolis criterion while
/// the temperature decays geometrically. It exists as a head-to-head research alternative to the
/// population-based GA, not as a production path. It returns the same <see cref="EvolutionResult"/>
/// so the research benchmark can compare it drop-in; note that the result's GenerationFitness here
/// carries the best fitness per STEP, not per generation.
/// </summary>
public sealed class HarmonizerAnnealingLoop
{
    private const int CalibrationSampleCount = 40;
    private const double MinimumTemperature = 1e-9;
    private const int CalibrationSeedSalt = 0x5A5A;

    private static readonly ConductorResult EmptyConductorResult = new([], [], []);

    private readonly IBitmapFitnessEvaluator _fitness;
    private readonly StochasticBitmapMutation _mutation;
    private readonly Func<int, HarmonizerConductor> _conductorFactory;
    private readonly HarmonizerAnnealingConfig _config;

    public HarmonizerAnnealingLoop(
        IBitmapFitnessEvaluator fitness,
        StochasticBitmapMutation mutation,
        Func<int, HarmonizerConductor> conductorFactory,
        HarmonizerAnnealingConfig config)
    {
        _fitness = fitness;
        _mutation = mutation;
        _conductorFactory = conductorFactory;
        _config = config;
    }

    public EvolutionResult Run(
        HarmonyBitmap seed,
        IProgress<EvolutionGenerationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var random = _config.Seed.HasValue ? new Random(_config.Seed.Value) : new Random();

        var current = EvaluateInitial(BitmapCloner.Clone(seed), ct);
        var best = current;
        var stepFitness = new List<double>(_config.MaxIterations + 1) { best.Fitness };
        progress?.Report(new EvolutionGenerationProgress(0, _config.MaxIterations, best.Fitness, false));

        var temperature = CalibrateInitialTemperature(current, stopwatch, ct);

        for (var step = 0; step < _config.MaxIterations; step++)
        {
            ct.ThrowIfCancellationRequested();
            if (BudgetExceeded(stopwatch))
            {
                break;
            }

            var candidate = ProposeCandidate(current.Bitmap, random, ct);
            var delta = candidate.Fitness - current.Fitness;

            if (delta >= 0 || random.NextDouble() < Math.Exp(delta / temperature))
            {
                current = candidate;
                if (current.Fitness > best.Fitness)
                {
                    best = current;
                }
            }

            temperature = Math.Max(MinimumTemperature, temperature * _config.CoolingRate);
            stepFitness.Add(best.Fitness);
            progress?.Report(new EvolutionGenerationProgress(step + 1, _config.MaxIterations, best.Fitness, false));
        }

        return new EvolutionResult(best, stepFitness);
    }

    private Individual EvaluateInitial(HarmonyBitmap bitmap, CancellationToken ct)
    {
        var trace = MaybeRunConductor(bitmap, ct);
        var fitness = _fitness.Evaluate(bitmap);
        return new Individual(bitmap, fitness.Fitness, fitness.RowScores, trace);
    }

    private Individual ProposeCandidate(HarmonyBitmap source, Random random, CancellationToken ct)
    {
        var candidate = BitmapCloner.Clone(source);
        _mutation.Apply(candidate, _config.MovesPerStep, random);
        var trace = MaybeRunConductor(candidate, ct);
        var fitness = _fitness.Evaluate(candidate);
        return new Individual(candidate, fitness.Fitness, fitness.RowScores, trace);
    }

    private ConductorResult MaybeRunConductor(HarmonyBitmap bitmap, CancellationToken ct)
    {
        if (!_config.RunConductorPerStep)
        {
            return EmptyConductorResult;
        }

        var conductor = _conductorFactory(bitmap.RowCount);
        return conductor.Run(bitmap, ct);
    }

    /// <summary>
    /// Picks the initial temperature so that worsening moves are accepted with probability
    /// TargetInitialAcceptance. The probe samples the actual step operator (mutation plus the
    /// optional conductor pass), because the conductor compresses the fitness delta and a
    /// temperature scaled to raw mutation would be far too cold.
    /// </summary>
    private double CalibrateInitialTemperature(Individual baseline, Stopwatch stopwatch, CancellationToken ct)
    {
        if (_config.TargetInitialAcceptance is not { } target || target <= 0 || target >= 1 || BudgetExceeded(stopwatch))
        {
            return _config.InitialTemperature;
        }

        var probeRandom = _config.Seed.HasValue
            ? new Random(unchecked(_config.Seed.Value ^ CalibrationSeedSalt))
            : new Random();

        var worseningMagnitudeSum = 0.0;
        var worseningCount = 0;
        for (var i = 0; i < CalibrationSampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (BudgetExceeded(stopwatch))
            {
                break;
            }

            var probe = ProposeCandidate(baseline.Bitmap, probeRandom, ct);
            var delta = probe.Fitness - baseline.Fitness;
            if (delta < 0)
            {
                worseningMagnitudeSum += -delta;
                worseningCount++;
            }
        }

        if (worseningCount == 0)
        {
            return _config.InitialTemperature;
        }

        var meanMagnitude = worseningMagnitudeSum / worseningCount;
        return meanMagnitude <= 0
            ? _config.InitialTemperature
            : -meanMagnitude / Math.Log(target);
    }

    private bool BudgetExceeded(Stopwatch stopwatch) =>
        _config.MaxRuntime is { } maxRuntime && stopwatch.Elapsed >= maxRuntime;
}
