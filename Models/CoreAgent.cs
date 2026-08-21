// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Represents a worker agent with scheduling constraints, contract data and current state.
/// Init-only properties hold contract-level fields that do not change during a single GA run.
/// </summary>
/// <param name="Id">Unique agent identifier</param>
/// <param name="CurrentHours">Hours already worked in current period</param>
/// <param name="GuaranteedHours">Contractual guaranteed hours per period (Stage 1 of fitness)</param>
/// <param name="MaxConsecutiveDays">Max consecutive work days allowed</param>
/// <param name="MinRestHours">Min rest hours between shifts</param>
/// <param name="Motivation">Current motivation score (0-1)</param>
/// <param name="MaxDailyHours">Max hours per day</param>
/// <param name="MaxWeeklyHours">Max hours per week</param>
/// <param name="MaxOptimalGap">Max optimal gap between shifts same day (hours)</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreAgent(
    string Id,
    double CurrentHours,
    double GuaranteedHours,
    int MaxConsecutiveDays,
    double MinRestHours,
    double Motivation,
    double MaxDailyHours,
    double MaxWeeklyHours,
    double MaxOptimalGap)
{
    /// <summary>Full-time hours (basis for top-down ranking — Stage 2).</summary>
    public double FullTime { get; init; } = 0;

    /// <summary>Soft maximum length of a single work block in days (e.g. 5). Hard cap stays MaxConsecutiveDays.</summary>
    public int MaxWorkDays { get; init; } = 0;

    /// <summary>Required free days between two work blocks (e.g. 2).</summary>
    public int MinRestDays { get; init; } = 0;

    /// <summary>Hard upper bound on hours (Stage 0 violation if exceeded).</summary>
    public double MaximumHours { get; init; } = 0;

    /// <summary>Soft target on minimum hours (Stage 4).</summary>
    public double MinimumHours { get; init; } = 0;

    /// <summary>True if the agent participates in shift rotation. False = FD-only, no rotation.</summary>
    public bool PerformsShiftWork { get; init; } = true;

    public bool WorkOnMonday { get; init; } = true;

    public bool WorkOnTuesday { get; init; } = true;

    public bool WorkOnWednesday { get; init; } = true;

    public bool WorkOnThursday { get; init; } = true;

    public bool WorkOnFriday { get; init; } = true;

    public bool WorkOnSaturday { get; init; } = false;

    public bool WorkOnSunday { get; init; } = false;

    /// <summary>Surcharge rate for night shifts, read through NightRateMode (Multiplier: fraction, e.g. 0.10 = 10%; otherwise an absolute amount). Used by the wizard for rough surcharge estimation toward GuaranteedHours.</summary>
    public decimal NightRate { get; init; } = 0;

    /// <summary>Surcharge rate for holiday shifts, read through HolidayRateMode.</summary>
    public decimal HolidayRate { get; init; } = 0;

    /// <summary>Surcharge rate for the 1st configured weekend day (country-neutral weekend slot), read through WE1RateMode.</summary>
    public decimal WE1Rate { get; init; } = 0;

    /// <summary>Surcharge rate for the 2nd configured weekend day (country-neutral weekend slot), read through WE2RateMode.</summary>
    public decimal WE2Rate { get; init; } = 0;

    /// <summary>Surcharge rate for the 3rd configured weekend day (country-neutral weekend slot), read through WE3RateMode.</summary>
    public decimal WE3Rate { get; init; } = 0;

    /// <summary>How NightRate is to be interpreted. Defaults to Multiplier, matching the contract default.</summary>
    public CoreSurchargeRateMode NightRateMode { get; init; } = CoreSurchargeRateMode.Multiplier;

    /// <summary>How HolidayRate is to be interpreted.</summary>
    public CoreSurchargeRateMode HolidayRateMode { get; init; } = CoreSurchargeRateMode.Multiplier;

    /// <summary>How WE1Rate is to be interpreted.</summary>
    public CoreSurchargeRateMode WE1RateMode { get; init; } = CoreSurchargeRateMode.Multiplier;

    /// <summary>How WE2Rate is to be interpreted.</summary>
    public CoreSurchargeRateMode WE2RateMode { get; init; } = CoreSurchargeRateMode.Multiplier;

    /// <summary>How WE3Rate is to be interpreted.</summary>
    public CoreSurchargeRateMode WE3RateMode { get; init; } = CoreSurchargeRateMode.Multiplier;
}
