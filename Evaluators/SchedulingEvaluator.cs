// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Evaluates scheduling stories against the evolution algorithm and calculates composite scores.
/// Analogous to Tier1Evaluator in SkillOptimizer — measures coverage, fairness, compliance, speed.
/// </summary>
/// <param name="config">Evolution config to use for all runs</param>
/// <param name="penaltyWeights">Penalty weights for fitness calculation</param>

using System.Text.Json;
using Klacks.ScheduleOptimizer.Engine;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Evaluators;

public class SchedulingEvaluator
{
    public static List<StoryResult> EvaluateAll(
        List<Story> stories,
        CoreConfig config,
        CorePenaltyWeights penaltyWeights,
        Action<string>? onProgress = null)
    {
        var results = new List<StoryResult>();

        foreach (var story in stories)
        {
            onProgress?.Invoke($"Running story: {story.Id}");
            var result = EvaluateStory(story, config, penaltyWeights);
            results.Add(result);
        }

        return results;
    }

    public static StoryResult EvaluateStory(
        Story story,
        CoreConfig config,
        CorePenaltyWeights penaltyWeights)
    {
        var (shifts, agents) = ScenarioGenerator.Generate(story, seed: 42);

        var evolutionResult = EvolutionCore.RunEvolution(shifts, agents, config, penaltyWeights);

        var hardViolations = ConstraintEngine.EvaluateHardViolations(evolutionResult.Assignments, shifts, agents);
        var softViolations = ConstraintEngine.EvaluateSoftViolations(evolutionResult.Assignments, shifts, agents);

        var fairness = EvolutionCore.CalculateFairness(
            new CoreScenario { Assignments = evolutionResult.Assignments },
            agents);

        var totalViolations = hardViolations.Count + softViolations.Count;
        var maxExpectedViolations = Math.Max(1, shifts.Count + agents.Count);
        var compliance = Math.Max(0, 1.0 - (double)totalViolations / maxExpectedViolations);

        var speed = story.Expectations.MaxTimeMs > 0
            ? Math.Max(0, 1.0 - (double)evolutionResult.TimeElapsedMs / story.Expectations.MaxTimeMs)
            : 1.0;

        var score = new SchedulingScore
        {
            Coverage = evolutionResult.Coverage,
            Fairness = fairness,
            Compliance = compliance,
            Speed = Math.Max(0, speed)
        };

        var passed = evolutionResult.Coverage >= story.Expectations.MinCoverage
            && hardViolations.Count <= story.Expectations.MaxHardViolations
            && evolutionResult.TimeElapsedMs <= story.Expectations.MaxTimeMs;

        var violationBreakdown = new Dictionary<string, int>();
        foreach (var v in hardViolations)
        {
            var key = ExtractViolationType(v.Description);
            violationBreakdown[key] = violationBreakdown.GetValueOrDefault(key) + 1;
        }

        return new StoryResult
        {
            StoryId = story.Id,
            Passed = passed,
            Score = score,
            Coverage = evolutionResult.Coverage,
            HardViolations = hardViolations.Count,
            SoftViolations = softViolations.Count,
            Fairness = fairness,
            FinalGeneration = evolutionResult.FinalGeneration,
            StopReason = evolutionResult.StopReason,
            TimeElapsedMs = evolutionResult.TimeElapsedMs,
            ViolationBreakdown = violationBreakdown
        };
    }

    public static SchedulingScore CalculateAggregateScore(List<StoryResult> results)
    {
        if (results.Count == 0)
            return new SchedulingScore();

        return new SchedulingScore
        {
            Coverage = results.Average(r => r.Score.Coverage),
            Fairness = results.Average(r => r.Score.Fairness),
            Compliance = results.Average(r => r.Score.Compliance),
            Speed = results.Average(r => r.Score.Speed)
        };
    }

    private static string ExtractViolationType(string description)
    {
        if (description.Contains("exceeds") && description.Contains("h on")) return "DailyHours";
        if (description.Contains("exceeds") && description.Contains("week")) return "WeeklyHours";
        if (description.Contains("consecutive")) return "ConsecutiveDays";
        if (description.Contains("double-booked")) return "TimeOverlap";
        if (description.Contains("rest period")) return "RestPeriod";
        return "Unknown";
    }

    public static List<Story> LoadStories(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<Story>>(json) ?? [];
    }
}

public class StoryResult
{
    public string StoryId { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public SchedulingScore Score { get; set; } = new();
    public double Coverage { get; set; }
    public int HardViolations { get; set; }
    public int SoftViolations { get; set; }
    public double Fairness { get; set; }
    public int FinalGeneration { get; set; }
    public string StopReason { get; set; } = string.Empty;
    public long TimeElapsedMs { get; set; }
    public Dictionary<string, int> ViolationBreakdown { get; set; } = [];
}
