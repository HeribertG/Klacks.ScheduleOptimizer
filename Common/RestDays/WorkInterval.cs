// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Common.RestDays;

/// <summary>
/// One span of work in company-local wall-clock time, as read by the calendar-week rest-day arithmetic.
/// </summary>
/// <param name="Start">Inclusive start of the work</param>
/// <param name="End">Exclusive end of the work; after midnight for an overnight shift</param>
public readonly record struct WorkInterval(DateTime Start, DateTime End);
