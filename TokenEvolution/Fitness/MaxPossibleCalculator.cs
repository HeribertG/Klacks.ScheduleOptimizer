// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

/// <summary>
/// Computes the maximum possible hours each agent could realistically cover in the period,
/// respecting weekday flags, shift-work flag, break blockers, per-day keywords and MaximumHours cap.
/// Result is constant for a GA run and used to normalise Stage-1/Stage-2 fitness.
/// </summary>
public sealed class MaxPossibleCalculator
{
    public IReadOnlyDictionary<string, double> ComputeForAll(CoreWizardContext context)
    {
        var result = new Dictionary<string, double>();

        foreach (var agent in context.Agents)
        {
            double total = 0;

            foreach (var shift in context.Shifts)
            {
                if (!IsShiftFeasibleForAgent(agent, shift, context))
                {
                    continue;
                }

                total += shift.Hours;
            }

            if (agent.MaximumHours > 0)
            {
                total = Math.Min(total, agent.MaximumHours - agent.CurrentHours);
                total = Math.Max(0, total);
            }

            result[agent.Id] = total;
        }

        return result;
    }

    private static bool IsShiftFeasibleForAgent(CoreAgent agent, CoreShift shift, CoreWizardContext context)
    {
        if (!DateOnly.TryParse(shift.Date, out var date))
        {
            return false;
        }

        if (date < context.PeriodFrom || date > context.PeriodUntil)
        {
            return false;
        }

        if (!RespectsWeekday(agent, date.DayOfWeek))
        {
            return false;
        }

        var shiftTypeIndex = TimeOnly.TryParse(shift.StartTime, out var start)
            ? ShiftTypeInference.FromStartTime(start)
            : 0;

        if (!agent.PerformsShiftWork && shiftTypeIndex != 0)
        {
            return false;
        }

        foreach (var blocker in context.BreakBlockers)
        {
            if (blocker.AgentId == agent.Id
                && date >= blocker.FromInclusive
                && date <= blocker.UntilInclusive)
            {
                return false;
            }
        }

        foreach (var cmd in context.ScheduleCommands)
        {
            if (cmd.AgentId == agent.Id && cmd.Date == date && ViolatesKeyword(cmd.Keyword, shiftTypeIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RespectsWeekday(CoreAgent agent, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => agent.WorkOnMonday,
        DayOfWeek.Tuesday => agent.WorkOnTuesday,
        DayOfWeek.Wednesday => agent.WorkOnWednesday,
        DayOfWeek.Thursday => agent.WorkOnThursday,
        DayOfWeek.Friday => agent.WorkOnFriday,
        DayOfWeek.Saturday => agent.WorkOnSaturday,
        DayOfWeek.Sunday => agent.WorkOnSunday,
        _ => false,
    };

    private static bool ViolatesKeyword(ScheduleCommandKeyword keyword, int shiftTypeIndex) => keyword switch
    {
        ScheduleCommandKeyword.Free => true,
        ScheduleCommandKeyword.OnlyEarly => shiftTypeIndex != 0,
        ScheduleCommandKeyword.NoEarly => shiftTypeIndex == 0,
        ScheduleCommandKeyword.OnlyLate => shiftTypeIndex != 1,
        ScheduleCommandKeyword.NoLate => shiftTypeIndex == 1,
        ScheduleCommandKeyword.OnlyNight => shiftTypeIndex != 2,
        ScheduleCommandKeyword.NoNight => shiftTypeIndex == 2,
        _ => false,
    };
}
