// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Lightweight hard-constraint filter used during initial population and repair.
/// Checks the "cheap" constraints inline; MinPauseHours and MaxConsecutiveDays are deferred to
/// the GA repair operator. MaxDailyHours is enforced here to prevent the same agent from being
/// double-booked on one day during population construction.
/// </summary>
public static class SlotConstraintFilter
{
    /// <summary>
    /// True if the given agent may receive a token for the slot (date + shift-type) given the context.
    /// Considers: weekday (WorkOnXxx), shift-work flag, break blockers, per-day keywords,
    /// per-agent MaximumHours, per-day MaxDailyHours (contract override or scheduling default).
    /// </summary>
    public static bool IsValidAssignment(
        CoreAgent agent,
        DateOnly date,
        int shiftTypeIndex,
        decimal slotHours,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> alreadyAssigned)
    {
        if (!RespectsWeekday(agent, date.DayOfWeek))
        {
            return false;
        }

        if (!agent.PerformsShiftWork && shiftTypeIndex != 0)
        {
            return false;
        }

        if (IsBlockedByBreak(agent.Id, date, context.BreakBlockers))
        {
            return false;
        }

        if (!RespectsKeyword(agent.Id, date, shiftTypeIndex, context.ScheduleCommands))
        {
            return false;
        }

        if (agent.MaximumHours > 0 && ExceedsMaxHours(agent, slotHours, alreadyAssigned))
        {
            return false;
        }

        if (ExceedsDailyHours(agent, date, slotHours, context, alreadyAssigned))
        {
            return false;
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

    private static bool IsBlockedByBreak(string agentId, DateOnly date, IReadOnlyList<CoreBreakBlocker> blockers)
    {
        foreach (var blocker in blockers)
        {
            if (blocker.AgentId == agentId && date >= blocker.FromInclusive && date <= blocker.UntilInclusive)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RespectsKeyword(
        string agentId, DateOnly date, int shiftTypeIndex, IReadOnlyList<CoreScheduleCommand> commands)
    {
        foreach (var cmd in commands)
        {
            if (cmd.AgentId != agentId || cmd.Date != date)
            {
                continue;
            }

            switch (cmd.Keyword)
            {
                case ScheduleCommandKeyword.Free:
                    return false;
                case ScheduleCommandKeyword.OnlyEarly when shiftTypeIndex != 0:
                case ScheduleCommandKeyword.NoEarly when shiftTypeIndex == 0:
                case ScheduleCommandKeyword.OnlyLate when shiftTypeIndex != 1:
                case ScheduleCommandKeyword.NoLate when shiftTypeIndex == 1:
                case ScheduleCommandKeyword.OnlyNight when shiftTypeIndex != 2:
                case ScheduleCommandKeyword.NoNight when shiftTypeIndex == 2:
                    return false;
            }
        }

        return true;
    }

    private static bool ExceedsMaxHours(CoreAgent agent, decimal slotHours, IReadOnlyList<CoreToken> assigned)
    {
        decimal sumAssigned = 0;
        foreach (var t in assigned)
        {
            if (t.AgentId == agent.Id)
            {
                sumAssigned += t.TotalHours;
            }
        }

        return (double)(sumAssigned + slotHours) + agent.CurrentHours > agent.MaximumHours;
    }

    private static bool ExceedsDailyHours(
        CoreAgent agent,
        DateOnly date,
        decimal slotHours,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> assigned)
    {
        double cap = context.SchedulingMaxDailyHours;
        foreach (var day in context.ContractDays)
        {
            if (day.AgentId == agent.Id && day.Date == date && day.MaximumHoursPerDay > 0)
            {
                cap = day.MaximumHoursPerDay;
                break;
            }
        }

        if (cap <= 0)
        {
            return false;
        }

        decimal sumDay = 0;
        foreach (var t in assigned)
        {
            if (t.AgentId == agent.Id && t.Date == date)
            {
                sumDay += t.TotalHours;
            }
        }

        return (double)(sumDay + slotHours) > cap;
    }
}
