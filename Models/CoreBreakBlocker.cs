// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// A read-only capacity blocker representing an existing Break (vacation, sick, lock).
/// No tokens may be assigned to the agent during this date range.
/// </summary>
/// <param name="AgentId">The agent</param>
/// <param name="FromInclusive">Start date (inclusive)</param>
/// <param name="UntilInclusive">End date (inclusive)</param>
/// <param name="Reason">Human-readable reason (for diagnostics)</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreBreakBlocker(
    string AgentId,
    DateOnly FromInclusive,
    DateOnly UntilInclusive,
    string Reason);
