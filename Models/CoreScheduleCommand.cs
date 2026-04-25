// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Per-day planning command from the schedule cell (FREE / -FREE / EARLY / -EARLY / LATE / -LATE / NIGHT / -NIGHT).
/// All values are hard-constraints (Stage 0 of fitness).
/// </summary>
/// <param name="AgentId">The agent the command applies to</param>
/// <param name="Date">The calendar date the command applies to</param>
/// <param name="Keyword">The command keyword</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreScheduleCommand(
    string AgentId,
    DateOnly Date,
    ScheduleCommandKeyword Keyword);
