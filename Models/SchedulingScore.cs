// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Composite scheduling score for autoresearch optimization.
/// Combines coverage, fairness, constraint compliance and speed.
/// </summary>
/// <param name="Coverage">Shift coverage ratio (0-1), weight 0.5</param>
/// <param name="Fairness">Hour distribution fairness (0-1), weight 0.2</param>
/// <param name="Compliance">Constraint compliance ratio (0-1), weight 0.2</param>
/// <param name="Speed">Time budget compliance (0-1), weight 0.1</param>

using System.Text.Json.Serialization;

namespace Klacks.ScheduleOptimizer.Models;

public class SchedulingScore
{
    public const double COVERAGE_WEIGHT = 0.5;
    public const double FAIRNESS_WEIGHT = 0.2;
    public const double COMPLIANCE_WEIGHT = 0.2;
    public const double SPEED_WEIGHT = 0.1;

    [JsonPropertyName("coverage")]
    public double Coverage { get; set; }

    [JsonPropertyName("fairness")]
    public double Fairness { get; set; }

    [JsonPropertyName("compliance")]
    public double Compliance { get; set; }

    [JsonPropertyName("speed")]
    public double Speed { get; set; }

    [JsonPropertyName("composite")]
    public double Composite => Coverage * COVERAGE_WEIGHT
        + Fairness * FAIRNESS_WEIGHT
        + Compliance * COMPLIANCE_WEIGHT
        + Speed * SPEED_WEIGHT;

    public static string Rating(double score) => score switch
    {
        >= 0.90 => "Exzellent",
        >= 0.75 => "Sehr gut",
        >= 0.60 => "Gut",
        >= 0.40 => "Verbesserungswuerdig",
        _ => "Schlecht"
    };
}
