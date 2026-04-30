// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Evaluates scheduling stories against the token-based evolution loop and calculates composite scores.
/// Score-mapping (token-engine outputs → autoresearch metrics):
///   - Coverage = assignedTokens / requiredAssignmentsTotal
///   - Fairness = 1 - sigma(perAgentHourRatios) reproducing the legacy fairness measure
///   - Compliance = 1 - clamp(stage0HardViolations / max(1, shifts + agents))
///   - Speed = 1 - elapsedMs / story.MaxTimeMs
/// </summary>
/// <param name="config">Token-evolution config to use for all runs</param>

using System.Diagnostics;
using System.Text.Json;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution;

namespace Klacks.ScheduleOptimizer.Evaluators;

public class SchedulingEvaluator
{
    private static readonly TokenEvolutionLoop SharedLoop = TokenEvolutionLoop.Create();

    public static List<StoryResult> EvaluateAll(
        List<Story> stories,
        TokenEvolutionConfig config,
        Action<string>? onProgress = null)
    {
        var results = new List<StoryResult>();

        foreach (var story in stories)
        {
            onProgress?.Invoke($"Running story: {story.Id}");
            var result = EvaluateStory(story, config);
            results.Add(result);
        }

        return results;
    }

    public static StoryResult EvaluateStory(Story story, TokenEvolutionConfig config)
    {
        var context = ScenarioGenerator.Generate(story, seed: 42);

        var sw = Stopwatch.StartNew();
        var scenario = SharedLoop.Run(context, config);
        sw.Stop();

        var requiredAssignments = Math.Max(1, context.Shifts.Sum(s => s.RequiredAssignments));
        var coverage = Math.Min(1.0, scenario.Tokens.Count / (double)requiredAssignments);
        var fairness = ComputeFairness(scenario, context);

        var hardViolations = scenario.FitnessStage0;
        var maxExpectedViolations = Math.Max(1, context.Shifts.Count + context.Agents.Count);
        var compliance = Math.Max(0, 1.0 - hardViolations / (double)maxExpectedViolations);

        var speed = story.Expectations.MaxTimeMs > 0
            ? Math.Max(0, 1.0 - sw.ElapsedMilliseconds / (double)story.Expectations.MaxTimeMs)
            : 1.0;

        var score = new SchedulingScore
        {
            Coverage = coverage,
            Fairness = fairness,
            Compliance = compliance,
            Speed = speed
        };

        var passed = coverage >= story.Expectations.MinCoverage
            && hardViolations <= story.Expectations.MaxHardViolations
            && sw.ElapsedMilliseconds <= story.Expectations.MaxTimeMs;

        return new StoryResult
        {
            StoryId = story.Id,
            Passed = passed,
            Score = score,
            Coverage = coverage,
            HardViolations = hardViolations,
            SoftViolations = 0,
            Fairness = fairness,
            FinalGeneration = 0,
            StopReason = string.Empty,
            TimeElapsedMs = sw.ElapsedMilliseconds,
            ViolationBreakdown = []
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

    private static double ComputeFairness(CoreScenario scenario, CoreWizardContext context)
    {
        if (context.Agents.Count == 0)
        {
            return 1.0;
        }

        var hoursByAgent = scenario.Tokens
            .GroupBy(t => t.AgentId)
            .ToDictionary(g => g.Key, g => g.Sum(t => (double)t.TotalHours));

        var ratios = new List<double>();
        foreach (var agent in context.Agents)
        {
            var target = agent.GuaranteedHours > 0 ? agent.GuaranteedHours : agent.MaxWeeklyHours * 4;
            if (target <= 0)
            {
                continue;
            }
            var hours = hoursByAgent.GetValueOrDefault(agent.Id, 0);
            ratios.Add(Math.Min(1, hours / target));
        }

        if (ratios.Count == 0)
        {
            return 1.0;
        }

        var mean = ratios.Average();
        var variance = ratios.Sum(r => Math.Pow(r - mean, 2)) / ratios.Count;
        return 1.0 - Math.Min(1.0, Math.Sqrt(variance));
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
