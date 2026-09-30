// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Numerics;

namespace Klacks.ScheduleOptimizer.Common.RestDays;

/// <summary>
/// The single definition of a rest day, shared by the schedule check (ClientTimeline /
/// ScheduleValidationBuilder and the Sunday rotation in Klacks.Api) and every wizard that places or
/// moves work, so a plan a wizard accepts can never be flagged by the check for the same rule. Callers
/// pass company-local wall-clock intervals, never UTC; weeks run Monday to Sunday of that calendar.
/// A day is a work day when a work starts on it or covers it completely. A day touched only by the
/// morning end of a work that started the day before (owner rule 2026-09-30) is a rest day only when
/// the following calendar day is untouched by any work AND the free block from the end of that
/// spillover to the next work start reaches the minimum free block (<see cref="MinimumFreeBlock"/>,
/// the package rest of MinRestDays times 24 hours that Wizard 1 already enforces between packages);
/// an unknown next start (end of the known data) satisfies the free block. Every other day is a rest
/// day, including a day that holds only an absence. Week occupancy is a seven-bit mask (bit 0 = Monday).
/// </summary>
public static class CalendarWeekRestDays
{
    public const int DaysPerWeek = 7;

    /// <summary>Owner ruling 2026-08-12: one configured rest day of a free block equals 24 hours.</summary>
    public const int HoursPerRestDay = 24;

    private const int FullWeekMask = (1 << DaysPerWeek) - 1;

    /// <summary>Monday of the calendar week that contains <paramref name="date"/>.</summary>
    /// <param name="date">Any day of the week</param>
    public static DateOnly WeekStartOf(DateOnly date)
    {
        var offsetFromMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + DaysPerWeek) % DaysPerWeek;
        return date.AddDays(-offsetFromMonday);
    }

    /// <summary>
    /// Minimum free block between two work packages: MinRestDays rounded up to whole days, times
    /// <see cref="HoursPerRestDay"/>. The same threshold the Wizard-1 package-rest rule vetoes on.
    /// </summary>
    /// <param name="minimumRestDays">Configured MinRestDays (may be fractional)</param>
    public static TimeSpan MinimumFreeBlock(decimal minimumRestDays)
        => minimumRestDays <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromHours((double)Math.Ceiling(minimumRestDays) * HoursPerRestDay);

    /// <summary>True when the work interval overlaps any moment of the calendar day.</summary>
    /// <param name="start">Inclusive start of the work</param>
    /// <param name="end">Exclusive end of the work</param>
    /// <param name="date">Calendar day to test</param>
    public static bool Touches(DateTime start, DateTime end, DateOnly date)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        return start < dayStart.AddDays(1) && end > dayStart;
    }

    /// <summary>Last calendar day the interval touches (the end is exclusive).</summary>
    /// <param name="work">Work interval</param>
    public static DateOnly LastTouchedDay(WorkInterval work)
        => DateOnly.FromDateTime(work.End > work.Start ? work.End.AddTicks(-1) : work.Start);

    /// <summary>
    /// The interval of a work, or the whole calendar day when the work carries no usable times - the one
    /// fallback every caller uses, so a work without times always counts as a work starting that day.
    /// </summary>
    /// <param name="start">Start of the work, default when unknown</param>
    /// <param name="end">End of the work, default when unknown</param>
    /// <param name="day">Calendar day the work belongs to</param>
    public static WorkInterval IntervalOrWholeDay(DateTime start, DateTime end, DateOnly day)
        => start == default || end <= start
            ? new WorkInterval(day.ToDateTime(TimeOnly.MinValue), day.AddDays(1).ToDateTime(TimeOnly.MinValue))
            : new WorkInterval(start, end);

    /// <summary>True when <paramref name="date"/> is a work day under the rule described on the class.</summary>
    /// <param name="date">Calendar day to judge</param>
    /// <param name="works">All known work intervals of one person, also outside the week</param>
    /// <param name="minimumFreeBlock">Minimum free block (<see cref="MinimumFreeBlock"/>)</param>
    public static bool IsWorkDay(DateOnly date, IReadOnlyList<WorkInterval> works, TimeSpan minimumFreeBlock)
        => IsWorkDay(date, works, null, minimumFreeBlock);

    /// <summary>Work days of the week starting at <paramref name="weekStart"/> as a seven-bit mask.</summary>
    /// <param name="weekStart">Monday of the week</param>
    /// <param name="works">All known work intervals of one person, also outside the week</param>
    /// <param name="minimumFreeBlock">Minimum free block</param>
    public static int WorkDayMask(DateOnly weekStart, IReadOnlyList<WorkInterval> works, TimeSpan minimumFreeBlock)
        => WorkDayMask(weekStart, works, null, minimumFreeBlock);

    /// <summary>Number of rest days a week with the given work-day mask keeps.</summary>
    /// <param name="workDayMask">Seven-bit work-day mask of the week</param>
    public static int RestDaysOf(int workDayMask)
        => DaysPerWeek - BitOperations.PopCount((uint)(workDayMask & FullWeekMask));

    /// <summary>Number of rest days in the week starting at <paramref name="weekStart"/>.</summary>
    /// <param name="weekStart">Monday of the week</param>
    /// <param name="works">All known work intervals of one person; absences must not be passed</param>
    /// <param name="minimumRestDays">Configured MinRestDays; sets the minimum free block</param>
    public static int Count(DateOnly weekStart, IReadOnlyList<WorkInterval> works, decimal minimumRestDays)
        => RestDaysOf(WorkDayMask(weekStart, works, MinimumFreeBlock(minimumRestDays)));

    /// <summary>
    /// True when the week keeps at least the required rest days. The requirement is decimal because
    /// some jurisdictions set a fractional weekly minimum; whole rest days are compared against it as is.
    /// </summary>
    /// <param name="restDays">Rest days the week keeps</param>
    /// <param name="minimumRestDays">Required rest days per week</param>
    public static bool MeetsMinimum(int restDays, decimal minimumRestDays) => restDays >= minimumRestDays;

    /// <summary>
    /// First calendar day an added work can influence: its own start day and the spillover days before
    /// it whose free block it can shorten.
    /// </summary>
    /// <param name="added">The added work interval</param>
    /// <param name="minimumRestDays">Configured MinRestDays; sets the minimum free block</param>
    public static DateOnly FirstAffectedDay(WorkInterval added, decimal minimumRestDays)
        => DateOnly.FromDateTime(added.Start - MinimumFreeBlock(minimumRestDays)).AddDays(-1);

    /// <summary>
    /// Last calendar day whose judgement needs works: a spillover day at the end of the affected range
    /// looks at the following day and at the next start within the minimum free block.
    /// </summary>
    /// <param name="lastAffectedDay">Last calendar day that is judged</param>
    /// <param name="minimumRestDays">Configured MinRestDays; sets the minimum free block</param>
    public static DateOnly LastRelevantDay(DateOnly lastAffectedDay, decimal minimumRestDays)
        => lastAffectedDay.AddDays(DaysPerWeek + (int)Math.Ceiling(MinimumFreeBlock(minimumRestDays).TotalDays) + 1);

    /// <summary>
    /// True when adding <paramref name="added"/> to <paramref name="existing"/> turns a rest day into a
    /// work day in a week that then keeps fewer than the required rest days. Allocation-free variant for
    /// the hot placement paths of Wizard 1.
    /// </summary>
    /// <param name="existing">Work intervals of the person before the addition</param>
    /// <param name="added">The work interval the placement adds</param>
    /// <param name="minimumRestDays">Required rest days per week; also sets the minimum free block</param>
    public static bool AdditionBreaksMinimum(IReadOnlyList<WorkInterval> existing, WorkInterval added, decimal minimumRestDays)
    {
        if (minimumRestDays <= 0)
        {
            return false;
        }

        var minimumFreeBlock = MinimumFreeBlock(minimumRestDays);
        var lastWeek = WeekStartOf(LastTouchedDay(added));
        for (var weekStart = WeekStartOf(FirstAffectedDay(added, minimumRestDays)); weekStart <= lastWeek; weekStart = weekStart.AddDays(DaysPerWeek))
        {
            var workDaysBefore = WorkDayMask(weekStart, existing, null, minimumFreeBlock);
            var workDaysAfter = WorkDayMask(weekStart, existing, added, minimumFreeBlock);
            if (BreaksWeek(workDaysBefore, workDaysAfter, minimumRestDays))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when a change of one person's works (<paramref name="before"/> to <paramref name="after"/>)
    /// turns a rest day into a work day in a week between <paramref name="firstAffectedDay"/> and
    /// <paramref name="lastAffectedDay"/> that then keeps fewer than the required rest days. A change
    /// that adds no work day never breaks the rule, so a week already short because of fixed works does
    /// not freeze every other change in it.
    /// </summary>
    /// <param name="before">Work intervals before the change</param>
    /// <param name="after">Work intervals after the change</param>
    /// <param name="firstAffectedDay">First calendar day the change can influence</param>
    /// <param name="lastAffectedDay">Last calendar day the change can influence</param>
    /// <param name="minimumRestDays">Required rest days per week; also sets the minimum free block</param>
    public static bool ChangeBreaksMinimum(
        IReadOnlyList<WorkInterval> before,
        IReadOnlyList<WorkInterval> after,
        DateOnly firstAffectedDay,
        DateOnly lastAffectedDay,
        decimal minimumRestDays)
    {
        if (minimumRestDays <= 0)
        {
            return false;
        }

        var minimumFreeBlock = MinimumFreeBlock(minimumRestDays);
        var lastWeek = WeekStartOf(lastAffectedDay);
        for (var weekStart = WeekStartOf(firstAffectedDay); weekStart <= lastWeek; weekStart = weekStart.AddDays(DaysPerWeek))
        {
            var workDaysBefore = WorkDayMask(weekStart, before, null, minimumFreeBlock);
            var workDaysAfter = WorkDayMask(weekStart, after, null, minimumFreeBlock);
            if (BreaksWeek(workDaysBefore, workDaysAfter, minimumRestDays))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BreaksWeek(int workDaysBefore, int workDaysAfter, decimal minimumRestDays)
        => (workDaysAfter & ~workDaysBefore) != 0 && !MeetsMinimum(RestDaysOf(workDaysAfter), minimumRestDays);

    private static int WorkDayMask(DateOnly weekStart, IReadOnlyList<WorkInterval> works, WorkInterval? extra, TimeSpan minimumFreeBlock)
    {
        var mask = 0;
        for (var offset = 0; offset < DaysPerWeek; offset++)
        {
            if (IsWorkDay(weekStart.AddDays(offset), works, extra, minimumFreeBlock))
            {
                mask |= 1 << offset;
            }
        }

        return mask;
    }

    private static bool IsWorkDay(DateOnly date, IReadOnlyList<WorkInterval> works, WorkInterval? extra, TimeSpan minimumFreeBlock)
    {
        var day = new DayProbe(date);
        foreach (var work in works)
        {
            if (day.Observe(work))
            {
                return true;
            }
        }

        if (extra.HasValue && day.Observe(extra.Value))
        {
            return true;
        }

        return day.SpilloverIsWork(minimumFreeBlock);
    }

    /// <summary>Accumulates what one calendar day sees of a person's works.</summary>
    private struct DayProbe
    {
        private readonly DateTime _dayStart;
        private readonly DateTime _dayEnd;
        private DateTime? _spilloverEnd;
        private DateTime? _nextStart;

        public DayProbe(DateOnly date)
        {
            _dayStart = date.ToDateTime(TimeOnly.MinValue);
            _dayEnd = _dayStart.AddDays(1);
            _spilloverEnd = null;
            _nextStart = null;
        }

        public bool Observe(WorkInterval work)
        {
            if (work.Start >= _dayEnd)
            {
                if (!_nextStart.HasValue || work.Start < _nextStart.Value)
                {
                    _nextStart = work.Start;
                }

                return false;
            }

            if (work.End <= _dayStart)
            {
                return false;
            }

            if (work.Start >= _dayStart || work.End >= _dayEnd)
            {
                return true;
            }

            if (!_spilloverEnd.HasValue || work.End > _spilloverEnd.Value)
            {
                _spilloverEnd = work.End;
            }

            return false;
        }

        public readonly bool SpilloverIsWork(TimeSpan minimumFreeBlock)
        {
            if (!_spilloverEnd.HasValue || !_nextStart.HasValue)
            {
                return false;
            }

            var nextDayTouched = _nextStart.Value < _dayEnd.AddDays(1);
            return nextDayTouched || _nextStart.Value - _spilloverEnd.Value < minimumFreeBlock;
        }
    }
}