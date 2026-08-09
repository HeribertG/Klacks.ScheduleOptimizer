// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Represents a shift slot that needs to be assigned to an agent.
/// </summary>
/// <param name="Id">Unique shift identifier</param>
/// <param name="Name">Display name of the shift (e.g. "Frühdienst")</param>
/// <param name="Date">ISO date string (YYYY-MM-DD)</param>
/// <param name="StartTime">Start time (HH:mm)</param>
/// <param name="EndTime">End time (HH:mm)</param>
/// <param name="Hours">Duration in hours</param>
/// <param name="RequiredAssignments">Number of agents needed for this shift</param>
/// <param name="Priority">Scheduling priority (higher = scheduled first)</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreShift(
    string Id,
    string Name,
    string Date,
    string StartTime,
    string EndTime,
    double Hours,
    int RequiredAssignments,
    int Priority)
{
    /// <summary>
    /// Identity of the order (customer object) this slot belongs to; null when the caller does not
    /// resolve orders. Deliberately an init-only property and not a positional parameter: every
    /// existing construction site keeps compiling, and a context that leaves it null scores exactly
    /// as before, because a uniform location context produces a continuity score of 1.
    /// </summary>
    public string? LocationContext { get; init; }
}
