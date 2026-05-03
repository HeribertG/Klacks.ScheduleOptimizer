// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <param name="WorksOnDay">True if the agent's contract permits work on this day-of-week</param>
/// <param name="HasFreeCommand">True if a ScheduleCommand with FREE keyword blocks this date</param>
/// <param name="HasBreakBlocker">True if a Break or Absence overlaps this date</param>
public sealed record DayAvailability(bool WorksOnDay, bool HasFreeCommand, bool HasBreakBlocker)
{
    public static readonly DayAvailability AlwaysAvailable = new(true, false, false);

    public bool IsAvailable => WorksOnDay && !HasFreeCommand && !HasBreakBlocker;
}
