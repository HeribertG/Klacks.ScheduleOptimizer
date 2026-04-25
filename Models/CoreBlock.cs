// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// A block is a maximal sequence of consecutive calendar days where an agent has at least one Work-Unit per day,
/// hard-capped by SCHEDULING_MAX_CONSECUTIVE_DAYS. A free day terminates the block.
/// </summary>
/// <param name="Id">Unique block identifier (links Tokens via Token.BlockId)</param>
/// <param name="AgentId">The agent this block belongs to</param>
/// <param name="FirstDate">First date of the block (inclusive)</param>
/// <param name="LastDate">Last date of the block (inclusive)</param>
/// <param name="DayCount">Number of distinct work days in this block</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreBlock(
    Guid Id,
    string AgentId,
    DateOnly FirstDate,
    DateOnly LastDate,
    int DayCount);
