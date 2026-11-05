// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Constraints.Keywords;

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <param name="WorksOnDay">True if the agent's contract permits work on this day-of-week</param>
/// <param name="HasFreeCommand">True if a ScheduleCommand with FREE keyword blocks this date</param>
/// <param name="HasBreakBlocker">True if a Break or Absence overlaps this date</param>
/// <param name="RequiredSymbol">When set, the cell symbol on this date must equal this symbol (OnlyEarly/OnlyLate/OnlyNight keywords)</param>
/// <param name="ForbiddenSymbol">When set, the cell symbol on this date must not equal this symbol (NoEarly/NoLate/NoNight keywords)</param>
/// <param name="Keywords">The day's schedule commands as a keyword-wish day (restriction tier and positive wish); null when the
/// day has none or the caller does not supply them. Read by the keyword-wish move guard of the replace validator.</param>
public sealed record DayAvailability(
    bool WorksOnDay,
    bool HasFreeCommand,
    bool HasBreakBlocker,
    CellSymbol? RequiredSymbol = null,
    CellSymbol? ForbiddenSymbol = null,
    KeywordDay? Keywords = null)
{
    public static readonly DayAvailability AlwaysAvailable = new(true, false, false);

    public bool IsAvailable => WorksOnDay && !HasFreeCommand && !HasBreakBlocker;

    /// <summary>Availability without the FREE command, used in Soft keyword mode where FREE is a wish.</summary>
    public bool IsAvailableIgnoringKeywords => WorksOnDay && !HasBreakBlocker;
}
