// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// A read-only capacity blocker representing an existing Break (vacation, sick, lock).
/// No tokens may be assigned to the agent during this date range.
/// Break hours count toward the agent's target hours (TargetHoursDeviation), but NOT
/// toward MaxWeeklyHours — the employee is absent, paid, but not actually working.
/// </summary>
/// <param name="AgentId">The agent</param>
/// <param name="FromInclusive">Start date (inclusive)</param>
/// <param name="UntilInclusive">End date (inclusive)</param>
/// <param name="Reason">Human-readable reason (for diagnostics)</param>
/// <param name="Hours">Paid hours per blocker day (Break.WorkTime). Used for target-hours coverage; 0 for diagnostics-only blockers.</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreBreakBlocker(
    string AgentId,
    DateOnly FromInclusive,
    DateOnly UntilInclusive,
    string Reason,
    decimal Hours = 0m);
