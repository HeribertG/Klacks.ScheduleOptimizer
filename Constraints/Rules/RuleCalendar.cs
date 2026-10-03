// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Calendar-anchored period resolution, identical to the API CounterRuleEvaluator: ISO week Monday to
/// Sunday, calendar month, calendar year. Never rolling.
/// </summary>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public static class RuleCalendar
{
    public static DateOnly PeriodStart(RuleCalendarPeriod period, DateOnly date) => period switch
    {
        RuleCalendarPeriod.Week => date.AddDays(-(((int)date.DayOfWeek + RuleTimeConstants.DaysPerWeek - 1) % RuleTimeConstants.DaysPerWeek)),
        RuleCalendarPeriod.Month => new DateOnly(date.Year, date.Month, 1),
        RuleCalendarPeriod.Year => new DateOnly(date.Year, 1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown calendar period."),
    };

    public static DateOnly PeriodEnd(RuleCalendarPeriod period, DateOnly date)
    {
        var start = PeriodStart(period, date);
        return period switch
        {
            RuleCalendarPeriod.Week => start.AddDays(RuleTimeConstants.DaysPerWeek - 1),
            RuleCalendarPeriod.Month => start.AddMonths(1).AddDays(-1),
            _ => start.AddYears(1).AddDays(-1),
        };
    }

    public static RuleCalendarPeriod ToCalendarPeriod(FairnessWindow window) => window switch
    {
        FairnessWindow.Week => RuleCalendarPeriod.Week,
        FairnessWindow.Month => RuleCalendarPeriod.Month,
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "Window is not calendar-anchored."),
    };
}
