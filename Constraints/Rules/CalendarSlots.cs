// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// The calendar periods (week, month or year) that intersect the planning period, as contiguous ranges of
/// plan day indices, plus the slot of every plan day. Built once per check so the hot paths only index arrays.
/// </summary>
/// <param name="context">Evaluation context providing the period</param>
/// <param name="period">Calendar period the slots are anchored to</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal sealed class CalendarSlots
{
    public const int NoSlot = -1;

    private readonly int[] _slotOfDay;
    private readonly List<int> _firstDay = [];
    private readonly List<int> _lastDay = [];
    private readonly List<DateOnly> _periodStart = [];

    public CalendarSlots(RuleEvaluationContext context, RuleCalendarPeriod period)
    {
        Period = period;
        _slotOfDay = new int[context.DayCount];
        for (var day = 0; day < context.DayCount; day++)
        {
            var start = RuleCalendar.PeriodStart(period, context.DateAt(day));
            if (_periodStart.Count == 0 || _periodStart[^1] != start)
            {
                _periodStart.Add(start);
                _firstDay.Add(day);
                _lastDay.Add(day);
            }

            _lastDay[^1] = day;
            _slotOfDay[day] = _periodStart.Count - 1;
        }
    }

    public RuleCalendarPeriod Period { get; }

    public int Count => _periodStart.Count;

    public int SlotOfDay(int dayIndex) => _slotOfDay[dayIndex];

    public int FirstDay(int slot) => _firstDay[slot];

    public int LastDay(int slot) => _lastDay[slot];

    /// <summary>Slot whose calendar period contains the date, or NoSlot when the period does not touch the plan.</summary>
    public int SlotOfDate(DateOnly date)
    {
        var start = RuleCalendar.PeriodStart(Period, date);
        for (var slot = 0; slot < _periodStart.Count; slot++)
        {
            if (_periodStart[slot] == start)
            {
                return slot;
            }
        }

        return NoSlot;
    }
}
