// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// A test story defining a scheduling scenario with expected outcomes.
/// </summary>
/// <param name="Id">Unique story identifier</param>
/// <param name="Description">Human-readable description of the scenario</param>
/// <param name="AgentCount">Number of agents to generate</param>
/// <param name="ShiftCount">Number of shifts to generate</param>

using System.Text.Json.Serialization;

namespace Klacks.ScheduleOptimizer.Models;

public class Story
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("agentCount")]
    public int AgentCount { get; set; }

    [JsonPropertyName("shiftCount")]
    public int ShiftCount { get; set; }

    [JsonPropertyName("shiftTypes")]
    public List<ShiftTypeConfig> ShiftTypes { get; set; } = [];

    [JsonPropertyName("agentConfig")]
    public AgentConfig AgentConfig { get; set; } = new();

    [JsonPropertyName("expectations")]
    public StoryExpectations Expectations { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";
}

public class ShiftTypeConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = "07:00";

    [JsonPropertyName("endTime")]
    public string EndTime { get; set; } = "15:00";

    [JsonPropertyName("hours")]
    public double Hours { get; set; } = 8;

    [JsonPropertyName("ratio")]
    public double Ratio { get; set; } = 1.0;

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1;
}

public class AgentConfig
{
    [JsonPropertyName("guaranteedHours")]
    public double GuaranteedHours { get; set; } = 170;

    [JsonPropertyName("maxDailyHours")]
    public double MaxDailyHours { get; set; } = 10;

    [JsonPropertyName("maxWeeklyHours")]
    public double MaxWeeklyHours { get; set; } = 50;

    [JsonPropertyName("maxConsecutiveDays")]
    public int MaxConsecutiveDays { get; set; } = 5;

    [JsonPropertyName("minRestHours")]
    public double MinRestHours { get; set; } = 12;

    [JsonPropertyName("maxOptimalGap")]
    public double MaxOptimalGap { get; set; } = 2;

    /// <summary>Fraction of agents that are FD-only (PerformsShiftWork=false, shiftTypeIndex must be 0).</summary>
    [JsonPropertyName("fdOnlyRatio")]
    public double FdOnlyRatio { get; set; } = 0.0;

    /// <summary>Fraction of agents that have MaximumHours set to GuaranteedHours * 1.2 (period cap).</summary>
    [JsonPropertyName("maxHoursCapRatio")]
    public double MaxHoursCapRatio { get; set; } = 0.0;

    /// <summary>Multiplier range for GuaranteedHours variation. Default (0.8..1.2). Range (low..high).</summary>
    [JsonPropertyName("guaranteedHoursSpread")]
    public double GuaranteedHoursSpread { get; set; } = 0.4;

    /// <summary>Fraction of agents that receive a break blocker (vacation/sick) for the first breakBlockerDays days.</summary>
    [JsonPropertyName("breakBlockerRatio")]
    public double BreakBlockerRatio { get; set; } = 0.0;

    /// <summary>Number of consecutive days blocked at the start of the period for each break-blocked agent.</summary>
    [JsonPropertyName("breakBlockerDays")]
    public int BreakBlockerDays { get; set; } = 7;

    /// <summary>Fraction of generated shifts that are pre-locked (already assigned, must not be mutated).</summary>
    [JsonPropertyName("lockedWorkRatio")]
    public double LockedWorkRatio { get; set; } = 0.0;
}

public class StoryExpectations
{
    [JsonPropertyName("minCoverage")]
    public double MinCoverage { get; set; } = 0.80;

    [JsonPropertyName("maxHardViolations")]
    public int MaxHardViolations { get; set; }

    [JsonPropertyName("maxSoftViolations")]
    public int MaxSoftViolations { get; set; } = 10;

    [JsonPropertyName("maxTimeMs")]
    public long MaxTimeMs { get; set; } = 15000;
}
