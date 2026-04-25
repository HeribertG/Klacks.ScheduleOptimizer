// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// A per-day contract snapshot used by ConstraintChecker when contracts change mid-period.
/// Built once before the GA run from ClientContractDataProvider.
/// </summary>
/// <param name="AgentId">The agent</param>
/// <param name="Date">The calendar date</param>
/// <param name="WorksOnDay">True if the contract allows work on this weekday (WorkOnMonday..Sunday)</param>
/// <param name="PerformsShiftWork">Contract-level shift-work flag valid on this date</param>
/// <param name="FullTimeShare">Pro-rated full-time share for this date (used for Stage 2 weighting)</param>
/// <param name="MaximumHoursPerDay">Hard upper bound on hours this day (typically equals SchedulingMaxDailyHours, but can be contract-specific)</param>
/// <param name="ContractId">Reference to the source Contract entity</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreContractDay(
    string AgentId,
    DateOnly Date,
    bool WorksOnDay,
    bool PerformsShiftWork,
    double FullTimeShare,
    double MaximumHoursPerDay,
    Guid ContractId);
