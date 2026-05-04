// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <param name="WorksOnDay">True if the agent's contract permits work on this day-of-week</param>
/// <param name="HasFreeCommand">True if a ScheduleCommand with FREE keyword blocks this date</param>
/// <param name="HasBreakBlocker">True if a Break or Absence overlaps this date</param>
/// <param name="RequiredSymbol">When set, the cell symbol on this date must equal this symbol (OnlyEarly/OnlyLate/OnlyNight keywords)</param>
/// <param name="ForbiddenSymbol">When set, the cell symbol on this date must not equal this symbol (NoEarly/NoLate/NoNight keywords)</param>
public sealed record DayAvailability(
    bool WorksOnDay,
    bool HasFreeCommand,
    bool HasBreakBlocker,
    CellSymbol? RequiredSymbol = null,
    CellSymbol? ForbiddenSymbol = null)
{
    public static readonly DayAvailability AlwaysAvailable = new(true, false, false);

    public bool IsAvailable => WorksOnDay && !HasFreeCommand && !HasBreakBlocker;
}
