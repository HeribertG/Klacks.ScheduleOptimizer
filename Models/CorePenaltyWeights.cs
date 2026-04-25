// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Penalty/bonus weights for fitness calculation.
/// </summary>
/// <param name="HardViolation">Penalty per hard constraint violation (negative)</param>
/// <param name="SoftViolation">Penalty per soft constraint violation (negative)</param>
/// <param name="CoverageBonus">Bonus per covered shift</param>
/// <param name="MotivationBonus">Bonus for high motivation assignments</param>
/// <param name="FairnessBonus">Bonus for fair hour distribution</param>

using System.Text.Json.Serialization;

namespace Klacks.ScheduleOptimizer.Models;

public class CorePenaltyWeights
{
    [JsonPropertyName("hardViolation")]
    public double HardViolation { get; set; } = -100;

    [JsonPropertyName("softViolation")]
    public double SoftViolation { get; set; } = -5;

    [JsonPropertyName("coverageBonus")]
    public double CoverageBonus { get; set; } = 5;

    [JsonPropertyName("motivationBonus")]
    public double MotivationBonus { get; set; } = 2;

    [JsonPropertyName("fairnessBonus")]
    public double FairnessBonus { get; set; } = 1;

    [JsonPropertyName("uncoveredPenalty")]
    public double UncoveredPenalty { get; set; } = -30;
}
