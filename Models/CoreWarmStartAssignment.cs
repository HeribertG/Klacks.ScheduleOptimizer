// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// A single assignment from the last accepted plan of the previous period, already mapped onto the new
/// period's date axis. Seeds the warm-start population strategy. Never lifted as a locked token.
/// </summary>
/// <param name="AgentId">The agent this assignment belongs to</param>
/// <param name="Date">Calendar date already mapped onto the new (target) period</param>
/// <param name="ShiftRefId">Reference to the original Shift definition</param>
/// <param name="StartAt">Start of the work span</param>
/// <param name="EndAt">End of the work span</param>
/// <param name="TotalHours">Total paid hours of the assignment</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreWarmStartAssignment(
    string AgentId,
    DateOnly Date,
    Guid ShiftRefId,
    DateTime StartAt,
    DateTime EndAt,
    decimal TotalHours);
