// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>Calendar-anchored counting period (ISO week starting Monday, calendar month, calendar year).</summary>
public enum RuleCalendarPeriod
{
    Week,
    Month,
    Year,
}
