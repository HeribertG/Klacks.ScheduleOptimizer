// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Rough surcharge estimate for the wizard planning fitness: applies the agent's contract rates
/// (Night/Sa/So) by shift type and weekday. Each rate carries its own mode: a Multiplier is a fraction
/// of the worked hours (0.10 = 10%), FixedPerHour is an absolute amount per worked hour — both are
/// hours x rate, only the unit differs — while FixedPerShift is a flat amount charged once, independent
/// of the shift length. The modes are interpreted exactly as WorkMacroService.AdjustAmount does; the
/// figure stays an estimate because each rate is applied to the whole assignment rather than to the
/// hours actually falling inside the surcharge window. Holiday rate is intentionally skipped because the
/// wizard does not load calendar selections — the actual surcharge is computed by WorkMacroService at
/// apply time.
/// Shared by every place that creates a token or moves one to a different agent: a token that keeps the
/// previous agent's surcharges silently falsifies the Stage-1 hours account.
/// </summary>
public static class SurchargeEstimator
{
    private const int NightShiftTypeIndex = 2;

    /// <summary>
    /// Estimated surcharge for one assignment, in the unit the configured rate modes imply.
    /// </summary>
    /// <param name="totalHours">Paid hours of the assignment.</param>
    /// <param name="shiftTypeIndex">Shift type; index 2 is the night shift.</param>
    /// <param name="date">Day of the assignment, for the weekend rates.</param>
    /// <param name="agent">Agent holding the assignment; null yields zero.</param>
    public static decimal Estimate(decimal totalHours, int shiftTypeIndex, DateOnly date, CoreAgent? agent)
    {
        if (agent is null || totalHours <= 0)
        {
            return 0m;
        }

        var amount = 0m;
        if (shiftTypeIndex == NightShiftTypeIndex)
        {
            amount += AmountFor(agent.NightRate, agent.NightRateMode, totalHours);
        }

        if (date.DayOfWeek == DayOfWeek.Saturday)
        {
            amount += AmountFor(agent.WE1Rate, agent.WE1RateMode, totalHours);
        }

        if (date.DayOfWeek == DayOfWeek.Sunday)
        {
            amount += AmountFor(agent.WE2Rate, agent.WE2RateMode, totalHours);
        }

        return amount <= 0 ? 0m : amount;
    }

    private static decimal AmountFor(decimal rate, CoreSurchargeRateMode mode, decimal totalHours) =>
        mode == CoreSurchargeRateMode.FixedPerShift ? rate : totalHours * rate;
}
