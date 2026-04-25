// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// An existing Work entity that the GA must respect as locked. Becomes a Token with IsLocked=true in the genome.
/// </summary>
/// <param name="WorkId">The DB work identifier</param>
/// <param name="AgentId">The agent the work is assigned to</param>
/// <param name="Date">The calendar date</param>
/// <param name="ShiftTypeIndex">Shift category (0=FD, 1=SD, 2=ND)</param>
/// <param name="TotalHours">Paid hours (already counted against the agent's CurrentHours)</param>
/// <param name="StartAt">Start of the work span</param>
/// <param name="EndAt">End of the work span</param>
/// <param name="ShiftRefId">Reference to the shift definition</param>
/// <param name="LocationContext">Optional location identifier</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreLockedWork(
    string WorkId,
    string AgentId,
    DateOnly Date,
    int ShiftTypeIndex,
    decimal TotalHours,
    DateTime StartAt,
    DateTime EndAt,
    Guid ShiftRefId,
    string? LocationContext);
