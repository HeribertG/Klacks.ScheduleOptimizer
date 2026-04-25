// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Autoresearch loop: measure → change one parameter → measure → keep only if improved.
/// Iterates through tunable evolution parameters and penalty weights,
/// applying small perturbations and keeping improvements.
/// </summary>
/// <param name="stories">Test stories to evaluate against</param>
/// <param name="maxIterations">Maximum optimization iterations</param>
/// <param name="regressionThreshold">Max allowed score regression before rollback</param>

using Klacks.ScheduleOptimizer.Evaluators;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Optimizers;

public class ParameterOptimizer
{
    private static readonly List<ParameterDefinition> TunableParameters =
    [
        new("populationSize", 10, 100, 10, p => p.Config.PopulationSize, (p, v) => p.Config.PopulationSize = (int)v),
        new("maxGenerations", 50, 500, 50, p => p.Config.MaxGenerations, (p, v) => p.Config.MaxGenerations = (int)v),
        new("eliteCount", 1, 10, 1, p => p.Config.EliteCount, (p, v) => p.Config.EliteCount = (int)v),
        new("mutationRate", 0.1, 0.95, 0.1, p => p.Config.MutationRate, (p, v) => p.Config.MutationRate = v),
        new("crossoverRate", 0.3, 0.95, 0.1, p => p.Config.CrossoverRate, (p, v) => p.Config.CrossoverRate = v),
        new("warmStartRatio", 0.1, 0.9, 0.1, p => p.Config.WarmStartRatio, (p, v) => p.Config.WarmStartRatio = v),
        new("stagnationLimit", 5, 50, 5, p => p.Config.StagnationLimit, (p, v) => p.Config.StagnationLimit = (int)v),
        new("convergenceThreshold", 0.0001, 0.01, 0.001, p => p.Config.ConvergenceThreshold, (p, v) => p.Config.ConvergenceThreshold = v),
        new("hardViolation", -1000, -10, 50, p => p.Weights.HardViolation, (p, v) => p.Weights.HardViolation = v),
        new("softViolation", -100, -1, 5, p => p.Weights.SoftViolation, (p, v) => p.Weights.SoftViolation = v),
        new("coverageBonus", 1, 100, 5, p => p.Weights.CoverageBonus, (p, v) => p.Weights.CoverageBonus = v),
        new("motivationBonus", 0.5, 50, 2, p => p.Weights.MotivationBonus, (p, v) => p.Weights.MotivationBonus = v),
        new("fairnessBonus", 0.5, 20, 1, p => p.Weights.FairnessBonus, (p, v) => p.Weights.FairnessBonus = v),
        new("uncoveredPenalty", -100, -1, 10, p => p.Weights.UncoveredPenalty, (p, v) => p.Weights.UncoveredPenalty = v),
    ];

    public OptimizationReport Optimize(
        List<Story> stories,
        int maxIterations,
        double regressionThreshold,
        Action<string>? onProgress = null)
    {
        var paramSet = new ParameterSet();
        var report = new OptimizationReport();

        var baselineResults = SchedulingEvaluator.EvaluateAll(stories, paramSet.Config, paramSet.Weights);
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

                var currentValue = param.Get(paramSet);
                var directions = new[] { param.Step, -param.Step };

                foreach (var delta in directions)
                {
                    var newValue = Math.Clamp(currentValue + delta, param.Min, param.Max);
                    if (Math.Abs(newValue - currentValue) < 0.0001) continue;

                    param.Set(paramSet, newValue);

                    var results = SchedulingEvaluator.EvaluateAll(stories, paramSet.Config, paramSet.Weights);
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
                        onProgress?.Invoke($"[{iteration}/{maxIterations}] {param.Name}: {change.Before:F2} -> {change.After:F2} | Score: {newScore:F4} [KEPT +{change.Improvement:F4}]");
                        report.Changes.Add(change);
                        break;
                    }

                    change.Kept = false;
                    param.Set(paramSet, currentValue);
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

public class ParameterSet
{
    public CoreConfig Config { get; set; } = new() { RandomSeed = 42 };
    public CorePenaltyWeights Weights { get; set; } = new();
}

public record ParameterDefinition(
    string Name,
    double Min,
    double Max,
    double Step,
    Func<ParameterSet, double> Get,
    Action<ParameterSet, double> Set);
