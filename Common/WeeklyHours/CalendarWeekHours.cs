// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Klacks.ScheduleOptimizer.Common.RestDays;

namespace Klacks.ScheduleOptimizer.Common.WeeklyHours;

/// <summary>
/// The single definition of the maximum weekly hours, shared by every wizard stage: a week is the ISO calendar week
/// (Monday to Sunday, identical to the week of <see cref="CalendarWeekRestDays"/>), a work counts with its paid hours
/// on the day it starts, and break or absence hours never count. A cap of zero or less means unconstrained.
/// </summary>
public static class CalendarWeekHours
{
    /// <summary>ISO year and ISO week number of the calendar week that contains <paramref name="date"/>.</summary>
    /// <param name="date">Any day of the week</param>
    public static (int Year, int Week) WeekOf(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(dateTime), ISOWeek.GetWeekOfYear(dateTime));
    }

    /// <summary>True when <paramref name="date"/> lies in the week that starts on <paramref name="weekStart"/>.</summary>
    /// <param name="date">Start day of the work</param>
    /// <param name="weekStart">Monday of the week (<see cref="CalendarWeekRestDays.WeekStartOf"/>)</param>
    public static bool IsInWeek(DateOnly date, DateOnly weekStart)
        => date >= weekStart && date.DayNumber - weekStart.DayNumber < CalendarWeekRestDays.DaysPerWeek;

    /// <summary>True when the week holds more hours than the cap allows; a cap of zero or less never vetoes.</summary>
    /// <param name="weekHours">Paid hours of the week</param>
    /// <param name="cap">Maximum weekly hours</param>
    public static bool Exceeds(decimal weekHours, decimal cap) => cap > 0 && weekHours > cap;
}
