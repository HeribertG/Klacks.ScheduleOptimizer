// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// A token represents a Work-Unit (1..n DB Works that form a single semantic unit) assigned to an agent on a date.
/// Tokens are the atomic genome units the GA mutates and crosses over.
/// </summary>
/// <param name="WorkIds">The DB Works that form this Work-Unit (length 1..n)</param>
/// <param name="ShiftTypeIndex">Shift category: 0=Frühdienst, 1=Spätdienst, 2=Nachtdienst — drives Block-Ordering</param>
/// <param name="Date">Calendar date this token belongs to</param>
/// <param name="TotalHours">Total paid hours (excluding internal gaps)</param>
/// <param name="StartAt">Start of the unit span (incl. internal gaps)</param>
/// <param name="EndAt">End of the unit span (incl. internal gaps)</param>
/// <param name="BlockId">Identifier of the work-block this token belongs to</param>
/// <param name="PositionInBlock">Zero-based ordinal within the block</param>
/// <param name="IsLocked">True if this token represents an Existing Work that the GA must not mutate</param>
/// <param name="LocationContext">Optional location identifier for location-continuity penalty</param>
/// <param name="ShiftRefId">Reference to the original Shift definition</param>
/// <param name="AgentId">The agent this token is assigned to</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreToken(
    IReadOnlyList<string> WorkIds,
    int ShiftTypeIndex,
    DateOnly Date,
    decimal TotalHours,
    DateTime StartAt,
    DateTime EndAt,
    Guid BlockId,
    int PositionInBlock,
    bool IsLocked,
    string? LocationContext,
    Guid ShiftRefId,
    string AgentId);
