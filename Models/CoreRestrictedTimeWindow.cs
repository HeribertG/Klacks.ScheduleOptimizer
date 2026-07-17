// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Models;

/// <summary>
/// A seasonal daily forbidden-time-window (K16) projected onto the GA world: work on any shift in
/// <see cref="RestrictedShiftIds"/> is vetoed whenever the slot overlaps the daily window
/// [<see cref="DailyStartMinutes"/>, <see cref="DailyEndMinutes"/>) on a calendar day inside the season
/// (<see cref="FromMonth"/>/<see cref="FromDay"/> .. <see cref="ToMonth"/>/<see cref="ToDay"/>).
/// The season is a month/day tuple comparison so a year-boundary wrap (15 Nov .. 15 Feb) is handled the
/// same way in both directions. RestrictedShiftIds already carries the fully resolved set of shift ids in
/// scope for this rule (an empty group tag resolves to ALL period shift ids), so the GA filter only needs
/// an O(1) membership test. This logic is deliberately duplicated in the API-side
/// RestrictedTimeWindowEvaluator (the optimizer must not reference Klacks.Api); the two copies are pinned
/// against a single shared vector table in the unit tests so they cannot drift.
/// </summary>
/// <param name="FromMonth">Season start month (1-12), inclusive</param>
/// <param name="FromDay">Season start day of month (1-31), inclusive</param>
/// <param name="ToMonth">Season end month (1-12), inclusive</param>
/// <param name="ToDay">Season end day of month (1-31), inclusive</param>
/// <param name="DailyStartMinutes">Daily window start as minutes since midnight (0-1439)</param>
/// <param name="DailyEndMinutes">Daily window end as minutes since midnight (0-1439); a value not greater than the start wraps past midnight into the next day</param>
/// <param name="RestrictedShiftIds">Fully resolved set of shift ids the rule governs (empty tag = all period shifts)</param>
public sealed record CoreRestrictedTimeWindow(
    int FromMonth,
    int FromDay,
    int ToMonth,
    int ToDay,
    int DailyStartMinutes,
    int DailyEndMinutes,
    IReadOnlySet<Guid> RestrictedShiftIds)
{
    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// True when a slot [<paramref name="slotStart"/>, <paramref name="slotEnd"/>) on shift
    /// <paramref name="shiftRefId"/> falls inside the forbidden window on any day it touches.
    /// Membership in <see cref="RestrictedShiftIds"/> gates the check first (O(1)).
    /// </summary>
    public bool Blocks(DateTime slotStart, DateTime slotEnd, Guid shiftRefId)
    {
        return RestrictedShiftIds.Contains(shiftRefId)
            && WouldBlock(FromMonth, FromDay, ToMonth, ToDay, DailyStartMinutes, DailyEndMinutes, slotStart, slotEnd);
    }

    /// <summary>
    /// Pure season + daily-window overlap test, independent of any shift scope. Public and static so the
    /// unit tests can drive the SAME vectors through this and the API-side RestrictedTimeWindowEvaluator
    /// mirror and assert both agree - the sole guard against the two duplicated copies drifting apart.
    /// The slot spans at most one midnight (a work shift &lt; 24 h), and a daily window may itself wrap
    /// midnight, so three anchor days (previous, current, next) are inspected; the season is evaluated on
    /// the window's start day. Boundary touches are exclusive: a slot ending exactly at the window start,
    /// or starting exactly at the window end, does not overlap.
    /// </summary>
    public static bool WouldBlock(
        int fromMonth,
        int fromDay,
        int toMonth,
        int toDay,
        int dailyStartMinutes,
        int dailyEndMinutes,
        DateTime slotStart,
        DateTime slotEnd)
    {
        if (slotEnd <= slotStart)
        {
            return false;
        }

        var startDay = DateOnly.FromDateTime(slotStart);
        for (var offset = -1; offset <= 1; offset++)
        {
            var anchor = startDay.AddDays(offset);
            if (!SeasonContains(anchor.Month, anchor.Day, fromMonth, fromDay, toMonth, toDay))
            {
                continue;
            }

            var midnight = anchor.ToDateTime(TimeOnly.MinValue);
            var windowStart = midnight.AddMinutes(dailyStartMinutes);
            var windowEnd = dailyEndMinutes > dailyStartMinutes
                ? midnight.AddMinutes(dailyEndMinutes)
                : midnight.AddMinutes(MinutesPerDay + dailyEndMinutes);

            if (slotStart < windowEnd && windowStart < slotEnd)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when (month, day) falls inside the inclusive season, handling the year-boundary wrap: when the
    /// start tuple is after the end tuple (e.g. 11-15 .. 02-15) the season spans New Year.
    /// </summary>
    public static bool SeasonContains(int month, int day, int fromMonth, int fromDay, int toMonth, int toDay)
    {
        var current = (month * 100) + day;
        var from = (fromMonth * 100) + fromDay;
        var to = (toMonth * 100) + toDay;

        return from <= to
            ? current >= from && current <= to
            : current >= from || current <= to;
    }
}
