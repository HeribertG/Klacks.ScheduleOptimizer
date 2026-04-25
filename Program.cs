// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// CLI entry point for the Schedule Optimizer.
/// Supports two verbs: evaluate (measure baseline) and optimize (autoresearch loop).
/// </summary>
/// <param name="args">CLI arguments: evaluate or optimize with options</param>

using CommandLine;
using Spectre.Console;
using Klacks.ScheduleOptimizer.Config;
using Klacks.ScheduleOptimizer.Engine;
using Klacks.ScheduleOptimizer.Evaluators;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.Optimizers;
using System.Text.Json;

namespace Klacks.ScheduleOptimizer;

[Verb("evaluate", HelpText = "Evaluate scheduling algorithm against test stories")]
public class EvaluateOptions
{
    [Option('s', "stories", Default = null, HelpText = "Path to stories JSON file")]
    public string? StoriesPath { get; set; }

    [Option('t', "tag", Default = null, HelpText = "Filter stories by tag")]
    public string? Tag { get; set; }
}

[Verb("optimize", HelpText = "Run autoresearch optimization loop")]
public class OptimizeOptions
{
    [Option('s', "stories", Default = null, HelpText = "Path to stories JSON file")]
    public string? StoriesPath { get; set; }

    [Option('i', "iterations", Default = 20, HelpText = "Max optimization iterations")]
    public int MaxIterations { get; set; }

    [Option('t', "tag", Default = null, HelpText = "Filter stories by tag")]
    public string? Tag { get; set; }
}

public class Program
{
    public static int Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("ScheduleOptimizer").Color(Color.Blue));
        AnsiConsole.MarkupLine("[grey]Autoresearch loop for shift scheduling algorithm[/]");
        AnsiConsole.WriteLine();

        return Parser.Default.ParseArguments<EvaluateOptions, OptimizeOptions>(args)
            .MapResult(
                (EvaluateOptions opts) => RunEvaluate(opts),
                (OptimizeOptions opts) => RunOptimize(opts),
                _ => 1);
    }

    private static int RunEvaluate(EvaluateOptions opts)
    {
        var stories = LoadStories(opts.StoriesPath, opts.Tag);
        if (stories.Count == 0) { AnsiConsole.MarkupLine("[red]No stories found.[/]"); return 1; }

        var config = new CoreConfig { RandomSeed = 42 };
        var weights = new CorePenaltyWeights();

        AnsiConsole.MarkupLine($"[blue]Evaluating {stories.Count} stories with default parameters...[/]");
        AnsiConsole.WriteLine();

        var results = SchedulingEvaluator.EvaluateAll(stories, config, weights,
            msg => AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(msg)}[/]"));

        PrintResultsTable(results);
        PrintViolationBreakdown(results);

        var aggregate = SchedulingEvaluator.CalculateAggregateScore(results);
        PrintAggregateScore(aggregate);

        return 0;
    }

    private static int RunOptimize(OptimizeOptions opts)
    {
        var stories = LoadStories(opts.StoriesPath, opts.Tag);
        if (stories.Count == 0) { AnsiConsole.MarkupLine("[red]No stories found.[/]"); return 1; }

        var settings = OptimizerSettings.Load();
        var maxIterations = opts.MaxIterations > 0 ? opts.MaxIterations : settings.MaxIterations;

        AnsiConsole.MarkupLine($"[blue]Starting autoresearch loop ({maxIterations} iterations, {stories.Count} stories)...[/]");
        AnsiConsole.WriteLine();

        var optimizer = new ParameterOptimizer();
        var report = optimizer.Optimize(stories, maxIterations, settings.RegressionThreshold,
            msg => AnsiConsole.MarkupLine($"  {Markup.Escape(msg)}"));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Optimization complete![/]");
        var impStr = report.Improvement >= 0 ? $"+{report.Improvement:F4}" : $"{report.Improvement:F4}";
        AnsiConsole.MarkupLine($"  Baseline: [yellow]{report.BaselineScore:F4}[/]  Final: [green]{report.FinalScore:F4}[/]  Improvement: [green]{impStr}[/]");
        AnsiConsole.MarkupLine($"  Iterations: {report.Iterations}  Kept changes: {report.Changes.Count(c => c.Kept)}");

        PrintChangesTable(report.Changes);
        SaveReport(report);

        return 0;
    }

    private static List<Story> LoadStories(string? storiesPath, string? tag)
    {
        var path = storiesPath ?? FindStoriesPath();
        if (path is null || !File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]Stories file not found: {storiesPath ?? "Datasets/stories-scheduling.json"}[/]");
            return [];
        }

        var stories = SchedulingEvaluator.LoadStories(path);
        if (!string.IsNullOrEmpty(tag))
            stories = stories.Where(s => s.Tags.Contains(tag)).ToList();

        return stories;
    }

    private static string? FindStoriesPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Datasets", "stories-scheduling.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Datasets", "stories-scheduling.json"),
            "Datasets/stories-scheduling.json"
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void PrintResultsTable(List<StoryResult> results)
    {
        var table = new Table()
            .AddColumn("Story")
            .AddColumn("Pass")
            .AddColumn("Coverage")
            .AddColumn("Fairness")
            .AddColumn("Hard")
            .AddColumn("Soft")
            .AddColumn("Gens")
            .AddColumn("Time")
            .AddColumn("Score")
            .AddColumn("Stop");

        foreach (var r in results)
        {
            var pass = r.Passed ? "[green]PASS[/]" : "[red]FAIL[/]";
            table.AddRow(
                Markup.Escape(r.StoryId),
                pass,
                $"{r.Coverage:P0}",
                $"{r.Fairness:F2}",
                r.HardViolations.ToString(),
                r.SoftViolations.ToString(),
                r.FinalGeneration.ToString(),
                $"{r.TimeElapsedMs}ms",
                $"{r.Score.Composite:F4}",
                Markup.Escape(r.StopReason));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var passed = results.Count(r => r.Passed);
        var color = passed == results.Count ? "green" : "yellow";
        AnsiConsole.MarkupLine($"[{color}]{passed}/{results.Count} stories passed[/]");
        AnsiConsole.WriteLine();
    }

    private static void PrintViolationBreakdown(List<StoryResult> results)
    {
        var hasViolations = results.Any(r => r.ViolationBreakdown.Count > 0);
        if (!hasViolations) return;

        AnsiConsole.MarkupLine("[bold]Hard Violation Breakdown:[/]");
        foreach (var r in results.Where(r => r.ViolationBreakdown.Count > 0))
        {
            var parts = r.ViolationBreakdown.Select(kv => $"{kv.Key}={kv.Value}");
            AnsiConsole.MarkupLine($"  {Markup.Escape(r.StoryId)}: {string.Join(", ", parts)}");
        }
        AnsiConsole.WriteLine();
    }

    private static void PrintAggregateScore(SchedulingScore score)
    {
        var table = new Table()
            .AddColumn("Metric")
            .AddColumn("Value")
            .AddColumn("Weight");

        table.AddRow("Coverage", $"{score.Coverage:F4}", $"{SchedulingScore.COVERAGE_WEIGHT}");
        table.AddRow("Fairness", $"{score.Fairness:F4}", $"{SchedulingScore.FAIRNESS_WEIGHT}");
        table.AddRow("Compliance", $"{score.Compliance:F4}", $"{SchedulingScore.COMPLIANCE_WEIGHT}");
        table.AddRow("Speed", $"{score.Speed:F4}", $"{SchedulingScore.SPEED_WEIGHT}");
        table.AddRow("[bold]SCHEDULING_SCORE[/]", $"[bold]{score.Composite:F4}[/]", "[bold]1.0[/]");

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"Rating: [bold]{SchedulingScore.Rating(score.Composite)}[/]");
    }

    private static void PrintChangesTable(List<ProposedChange> changes)
    {
        if (changes.Count == 0) return;

        AnsiConsole.WriteLine();
        var table = new Table()
            .AddColumn("Parameter")
            .AddColumn("Before")
            .AddColumn("After")
            .AddColumn("Score +/-")
            .AddColumn("Kept");

        foreach (var c in changes)
        {
            var kept = c.Kept ? "[green]YES[/]" : "[red]NO[/]";
            var diff = c.Improvement >= 0 ? $"[green]+{c.Improvement:F4}[/]" : $"[red]{c.Improvement:F4}[/]";
            table.AddRow(
                Markup.Escape(c.Parameter),
                $"{c.Before:F4}",
                $"{c.After:F4}",
                diff,
                kept);
        }

        AnsiConsole.Write(table);
    }

    private static void SaveReport(OptimizationReport report)
    {
        var reportsDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Reports");
        if (!Directory.Exists(reportsDir))
            reportsDir = Path.Combine(Directory.GetCurrentDirectory(), "Reports");
        Directory.CreateDirectory(reportsDir);

        var filePath = Path.Combine(reportsDir, "optimization-report.json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
        AnsiConsole.MarkupLine($"[grey]Report saved to {Markup.Escape(filePath)}[/]");
    }
}
