// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// A complete scheduling solution (token-based genome).
/// Fitness is broken down into 5 lexicographically ordered stages.
/// </summary>

namespace Klacks.ScheduleOptimizer.Models;

public class CoreScenario
{
    public string Id { get; set; } = string.Empty;

    public List<CoreToken> Tokens { get; set; } = [];

    /// <summary>Aggregate fitness (stage-weighted sum, used for telemetry).</summary>
    public double Fitness { get; set; }

    public int HardViolations { get; set; }

    /// <summary>Stage 0: hard-constraint violations (count, must minimize to 0).</summary>
    public int FitnessStage0 { get; set; }

    /// <summary>Stage 1: GuaranteedHours coverage top-down (0..1, higher is better).</summary>
    public double FitnessStage1 { get; set; }

    /// <summary>Stage 2: FullTime / MaxPossible coverage top-down (0..1).</summary>
    public double FitnessStage2 { get; set; }

    /// <summary>Stage 3: soft constraints (block ordering, preferences, location). 0..1.</summary>
    public double FitnessStage3 { get; set; }

    /// <summary>Stage 4: cosmetic optimizations (fairness, MinimumHours). 0..1.</summary>
    public double FitnessStage4 { get; set; }
}
