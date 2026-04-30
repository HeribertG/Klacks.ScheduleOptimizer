// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Autoresearch loop: measure → change one parameter → measure → keep only if improved.
/// Iterates through TokenEvolutionConfig parameters, applying small perturbations
/// and keeping improvements via immutable record with-expressions.
/// </summary>
/// <param name="stories">Test stories to evaluate against</param>
/// <param name="maxIterations">Maximum optimization iterations</param>
/// <param name="regressionThreshold">Min improvement required to keep a change</param>

using Klacks.ScheduleOptimizer.Evaluators;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution;

namespace Klacks.ScheduleOptimizer.Optimizers;

public class ParameterOptimizer
{
    private static readonly List<ParameterDefinition> TunableParameters =
    [
        new("populationSize",    10,   200, 10,   c => c.PopulationSize,                    (c, v) => c with { PopulationSize = (int)v }),
        new("maxGenerations",    50,   500, 50,   c => c.MaxGenerations,                    (c, v) => c with { MaxGenerations = (int)v }),
        new("elitismCount",       1,    10,  1,   c => c.ElitismCount,                      (c, v) => c with { ElitismCount = (int)v }),
        new("tournamentK",        2,     7,  1,   c => c.TournamentK,                       (c, v) => c with { TournamentK = (int)v }),
        new("mutationRate",     0.1,  0.95, 0.1,  c => c.MutationRate,                      (c, v) => c with { MutationRate = v }),
        new("crossoverRate",    0.3,  0.95, 0.1,  c => c.CrossoverRate,                     (c, v) => c with { CrossoverRate = v }),
        new("earlyStop",         10,   100, 10,   c => c.EarlyStopNoImprovementGenerations, (c, v) => c with { EarlyStopNoImprovementGenerations = (int)v }),
        new("initAuctionRatio", 0.1,   0.9, 0.1,  c => c.InitAuctionRatio,                  (c, v) => c with { InitAuctionRatio = v }),
        new("mutSwap",          0.05,  0.5, 0.05, c => c.MutationWeightSwap,                (c, v) => c with { MutationWeightSwap = v }),
        new("mutSplit",         0.05,  0.5, 0.05, c => c.MutationWeightSplit,               (c, v) => c with { MutationWeightSplit = v }),
        new("mutMerge",         0.05,  0.5, 0.05, c => c.MutationWeightMerge,               (c, v) => c with { MutationWeightMerge = v }),
        new("mutReassign",      0.05,  0.5, 0.05, c => c.MutationWeightReassign,            (c, v) => c with { MutationWeightReassign = v }),
        new("mutRepair",        0.05,  0.5, 0.05, c => c.MutationWeightRepair,              (c, v) => c with { MutationWeightRepair = v }),
    ];

    public OptimizationReport Optimize(
        List<Story> stories,
        int maxIterations,
        double regressionThreshold,
        Action<string>? onProgress = null)
    {
        var config = new TokenEvolutionConfig { RandomSeed = 42 };
        var report = new OptimizationReport();

        var baselineResults = SchedulingEvaluator.EvaluateAll(stories, config);
        var baselineScore = SchedulingEvaluator.CalculateAggregateScore(baselineResults).Composite;
        report.BaselineScore = baselineScore;

        onProgress?.Invoke($"Baseline SCHEDULING_SCORE: {baselineScore:F4} ({SchedulingScore.Rating(baselineScore)})");

        var currentBestScore = baselineScore;
        var iteration = 0;

        for (var round = 0; round < 3 && iteration < maxIterations; round++)
        {
            foreach (var param in TunableParameters)
            {
                if (iteration >= maxIterations) break;
                iteration++;

                var currentValue = param.Get(config);
                var directions = new[] { param.Step, -param.Step };

                foreach (var delta in directions)
                {
                    var newValue = Math.Clamp(currentValue + delta, param.Min, param.Max);
                    if (Math.Abs(newValue - currentValue) < 0.0001) continue;

                    var candidate = param.Set(config, newValue);
                    var results = SchedulingEvaluator.EvaluateAll(stories, candidate);
                    var newScore = SchedulingEvaluator.CalculateAggregateScore(results).Composite;

                    var change = new ProposedChange
                    {
                        Parameter = param.Name,
                        Before = currentValue,
                        After = newValue,
                        ScoreBefore = currentBestScore,
                        ScoreAfter = newScore,
                        Reason = $"Iteration {iteration}, round {round + 1}"
                    };

                    if (newScore > currentBestScore + regressionThreshold)
                    {
                        change.Kept = true;
                        currentBestScore = newScore;
                        currentValue = newValue;
                        config = candidate;
                        onProgress?.Invoke($"[{iteration}/{maxIterations}] {param.Name}: {change.Before:F2} -> {change.After:F2} | Score: {newScore:F4} [KEPT +{change.Improvement:F4}]");
                        report.Changes.Add(change);
                        break;
                    }

                    change.Kept = false;
                    onProgress?.Invoke($"[{iteration}/{maxIterations}] {param.Name}: {change.Before:F2} -> {change.After:F2} | Score: {newScore:F4} [REVERTED]");
                    report.Changes.Add(change);
                }
            }
        }

        report.FinalScore = currentBestScore;
        report.Iterations = iteration;
        return report;
    }
}

public record ParameterDefinition(
    string Name,
    double Min,
    double Max,
    double Step,
    Func<TokenEvolutionConfig, double> Get,
    Func<TokenEvolutionConfig, double, TokenEvolutionConfig> Set);
