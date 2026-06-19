// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Constraints;

/// <summary>
/// Engine-neutral taxonomy of hard-constraint violations. Produced by the plan-level
/// <see cref="PlanConstraintChecker"/> and consumed by both the Wizard-1 token engine
/// (via TokenConstraintChecker) and the composite objective gate.
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
    QualificationMissing,
    Overlap,
}
