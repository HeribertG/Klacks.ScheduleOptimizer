// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

/// <summary>
/// Result of a controller check. Stage 0 = absolute taboo, Stage 1 = soft (escalatable),
/// Stage 2 = hint only. RuleName is a short identifier for logging and UI display.
/// </summary>
/// <param name="Stage">0/1/2 escalation level</param>
/// <param name="RuleName">Short identifier of the violated rule</param>
/// <param name="Hint">Human-readable explanation</param>
public sealed record VetoVerdict(int Stage, string RuleName, string Hint);
