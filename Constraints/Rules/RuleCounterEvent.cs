// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>Events a period-count rule counts; mirrors the API CounterEventType one to one.</summary>
public enum RuleCounterEvent
{
    NightShift,
    WorkedDayInWeek,
    ShiftExceedingHours,
}
