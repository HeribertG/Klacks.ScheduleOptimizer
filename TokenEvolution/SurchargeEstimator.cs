// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Rough surcharge estimate for the wizard planning fitness: applies the agent's contract rates
/// (Night/Sa/So) by shift type and weekday. Rates are stored as multipliers (0.10 = 10%), so the
/// estimate is simply hours x rate. Holiday rate is intentionally skipped because the wizard does not
/// load calendar selections — the actual surcharge is computed precisely by WorkMacroService at apply
/// time. Shared by every place that creates a token or moves one to a different agent: a token that
/// keeps the previous agent's surcharges silently falsifies the Stage-1 hours account.
/// </summary>
public static class SurchargeEstimator
{
    private const int NightShiftTypeIndex = 2;

    /// <summary>
    /// Estimated surcharge hours for one assignment.
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

        var rate = 0m;
        if (shiftTypeIndex == NightShiftTypeIndex)
        {
            rate += agent.NightRate;
        }

        if (date.DayOfWeek == DayOfWeek.Saturday)
        {
            rate += agent.WE1Rate;
        }

        if (date.DayOfWeek == DayOfWeek.Sunday)
        {
            rate += agent.WE2Rate;
        }

        return rate <= 0 ? 0m : totalHours * rate;
    }
}
