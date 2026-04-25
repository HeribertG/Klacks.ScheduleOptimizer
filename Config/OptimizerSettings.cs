// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Configuration settings for the schedule optimization process.
/// </summary>
/// <param name="TimeLimitMs">Time limit per evolution run in milliseconds</param>
/// <param name="MaxIterations">Maximum autoresearch iterations</param>
/// <param name="RegressionThreshold">Max allowed score regression before rollback</param>

using System.Text.Json.Serialization;

namespace Klacks.ScheduleOptimizer.Config;

public class OptimizerSettings
{
    [JsonPropertyName("timeLimitMs")]
    public int TimeLimitMs { get; set; } = 15000;

    [JsonPropertyName("maxIterations")]
    public int MaxIterations { get; set; } = 20;

    [JsonPropertyName("regressionThreshold")]
    public double RegressionThreshold { get; set; } = 0.02;

    public static OptimizerSettings Load(string? path = null)
    {
        var configPath = path ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Config", "optimizer-settings.json");
        if (!File.Exists(configPath))
            configPath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "optimizer-settings.json");

        if (!File.Exists(configPath))
            return new OptimizerSettings();

        var json = File.ReadAllText(configPath);
        return System.Text.Json.JsonSerializer.Deserialize<OptimizerSettings>(json) ?? new OptimizerSettings();
    }
}
