// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Result of a single evolution run including best solution and metadata.
/// </summary>
/// <param name="Assignments">Best solution assignments</param>
/// <param name="Fitness">Best fitness score (0-1)</param>
/// <param name="Coverage">Shift coverage ratio (0-1)</param>

namespace Klacks.ScheduleOptimizer.Models;

public class EvolutionResult
{
    public List<CoreAssignment> Assignments { get; set; } = [];
    public double Fitness { get; set; }
    public double Coverage { get; set; }
    public double PenaltyScore { get; set; }
    public int HardViolations { get; set; }
    public int FinalGeneration { get; set; }
    public string StopReason { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public long TimeElapsedMs { get; set; }
}
