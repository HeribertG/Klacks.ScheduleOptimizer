// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Constraints;

/// <summary>
/// Taxonomy of hard-constraint violations detected by the TokenConstraintChecker.
/// Stage 0 of the fitness function counts the number of violations; target is 0.
/// </summary>
public enum ViolationKind
{
    MaxConsecutiveDays,
    MinPauseHours,
    MaxDailyHours,
    WorkOnDayViolation,
    PerformsShiftWorkViolation,
    PerDayKeywordViolation,
    BreakBlockerViolation,
    MaximumHoursExceeded,
    UnderSupply,
    OverSupply,
}
