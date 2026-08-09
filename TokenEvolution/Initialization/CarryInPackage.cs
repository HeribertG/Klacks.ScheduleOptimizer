// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// A work package of the previous period that is still open on the first plannable day: it ran without
/// a gap up to the day before, every one of its days carried the same shift kind, and it has not yet
/// reached the agent's package length.
/// </summary>
/// <param name="AgentId">Employee the package belongs to</param>
/// <param name="ShiftRefId">Shift definition the last package day was served on</param>
/// <param name="LocationContext">Location identifier of the last package day, null when unknown</param>
/// <param name="ShiftTypeIndex">Shift kind of every package day (0=early, 1=late, 2=night)</param>
/// <param name="LastDay">Last day of the package before the first plannable day</param>
/// <param name="ServedDays">Days of the package that already lie before the first plannable day</param>
/// <param name="RemainingDays">Days the package still owes; always greater than zero</param>

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

public sealed record CarryInPackage(
    string AgentId,
    Guid ShiftRefId,
    string? LocationContext,
    int ShiftTypeIndex,
    DateOnly LastDay,
    int ServedDays,
    int RemainingDays);
