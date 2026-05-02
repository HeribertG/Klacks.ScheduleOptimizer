// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Represents a proposed parameter change from an optimization iteration.
/// </summary>
/// <param name="Parameter">Name of the changed parameter</param>
/// <param name="Before">Value before change</param>
/// <param name="After">Value after change</param>

using System.Text.Json.Serialization;

namespace Klacks.ScheduleOptimizer.Models;

public class ProposedChange
{
    [JsonPropertyName("parameter")]
    public string Parameter { get; set; } = string.Empty;

    [JsonPropertyName("before")]
    public double Before { get; set; }

    [JsonPropertyName("after")]
    public double After { get; set; }

    [JsonPropertyName("scoreBefore")]
    public double ScoreBefore { get; set; }

    [JsonPropertyName("scoreAfter")]
    public double ScoreAfter { get; set; }

    [JsonPropertyName("improvement")]
    public double Improvement => ScoreAfter - ScoreBefore;

    [JsonPropertyName("kept")]
    public bool Kept { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public class OptimizationReport
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("baselineScore")]
    public double BaselineScore { get; set; }

    [JsonPropertyName("finalScore")]
    public double FinalScore { get; set; }

    [JsonPropertyName("improvement")]
    public double Improvement => FinalScore - BaselineScore;

    [JsonPropertyName("iterations")]
    public int Iterations { get; set; }

    [JsonPropertyName("changes")]
    public List<ProposedChange> Changes { get; set; } = [];
}
