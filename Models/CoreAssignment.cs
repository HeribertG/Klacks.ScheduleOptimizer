// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Assignment of a shift to an agent with motivation score.
/// </summary>
/// <param name="ShiftId">The assigned shift</param>
/// <param name="AgentId">The assigned agent</param>
/// <param name="MotivationScore">Motivation score at time of assignment</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreAssignment(string ShiftId, string AgentId, double MotivationScore);
