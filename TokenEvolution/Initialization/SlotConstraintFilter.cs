// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Lightweight hard-constraint filter used during initial population and repair.
/// Enforces every Stage-0 hard rule including MinPauseHours so that the greedy population
/// builder cannot seed the GA with infeasible scenarios. Mirrors Stage0HardConstraintChecker.
/// </summary>
public static class SlotConstraintFilter
{
    /// <summary>
    /// True if the given agent may receive a token for the slot (date + shift-type) given the context.
    /// Considers: weekday (WorkOnXxx), shift-work flag, break blockers, per-day keywords,
    /// per-agent MaximumHours, per-day MaxDailyHours (contract override or per-agent cap),
    /// per-agent MinPauseHours (incl. cross-day overnight gaps), block-length and rest-day rules.
    /// The optional slot interval enables the MinPauseHours check; pass null for keyword-only seeds.
    /// </summary>
    public static bool IsValidAssignment(
        CoreAgent agent,
        DateOnly date,
        int shiftTypeIndex,
        decimal slotHours,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> alreadyAssigned,
        DateTime? slotStartUtc = null,
        DateTime? slotEndUtc = null)
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

        if (ExceedsBlockLength(agent, date, context, alreadyAssigned))
        {
            return false;
        }

        if (ViolatesMinRestDays(agent, date, alreadyAssigned))
        {
            return false;
        }

        if (slotStartUtc.HasValue && slotEndUtc.HasValue)
        {
            if (HasOverlappingShift(agent.Id, slotStartUtc.Value, slotEndUtc.Value, alreadyAssigned))
            {
                return false;
            }

            if (HasOverlappingExistingWork(agent.Id, slotStartUtc.Value, slotEndUtc.Value, context.ExistingWorkBlockers))
            {
                return false;
            }

            if (ViolatesMinPauseHours(agent, slotStartUtc.Value, slotEndUtc.Value, alreadyAssigned, context))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasOverlappingShift(
        string agentId, DateTime slotStart, DateTime slotEnd, IReadOnlyList<CoreToken> assigned)
    {
        foreach (var t in assigned)
        {
            if (t.AgentId != agentId)
            {
                continue;
            }
            if (t.StartAt < slotEnd && slotStart < t.EndAt)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasOverlappingExistingWork(
        string agentId, DateTime slotStart, DateTime slotEnd, IReadOnlyList<CoreExistingWorkBlocker> blockers)
    {
        foreach (var b in blockers)
        {
            if (b.AgentId != agentId)
            {
                continue;
            }
            if (b.StartAt < slotEnd && slotStart < b.EndAt)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ViolatesMinPauseHours(
        CoreAgent agent,
        DateTime slotStart,
        DateTime slotEnd,
        IReadOnlyList<CoreToken> assigned,
        CoreWizardContext context)
    {
        var minRest = agent.MinRestHours > 0 ? agent.MinRestHours : context.SchedulingMinPauseHours;
        if (minRest <= 0)
        {
            return false;
        }

        foreach (var t in assigned)
        {
            if (t.AgentId != agent.Id)
            {
                continue;
            }
            if (GapHoursBelow(slotStart, slotEnd, t.StartAt, t.EndAt, minRest))
            {
                return true;
            }
        }

        foreach (var locked in context.LockedWorks)
        {
            if (locked.AgentId != agent.Id)
            {
                continue;
            }
            if (GapHoursBelow(slotStart, slotEnd, locked.StartAt, locked.EndAt, minRest))
            {
                return true;
            }
        }

        foreach (var blocker in context.ExistingWorkBlockers)
        {
            if (blocker.AgentId != agent.Id)
            {
                continue;
            }
            if (GapHoursBelow(slotStart, slotEnd, blocker.StartAt, blocker.EndAt, minRest))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GapHoursBelow(
        DateTime slotStart, DateTime slotEnd,
        DateTime otherStart, DateTime otherEnd,
        double minRestHours)
    {
        if (slotStart < otherEnd && otherStart < slotEnd)
        {
            return false;
        }

        var gapHours = slotStart >= otherEnd
            ? (slotStart - otherEnd).TotalHours
            : (otherStart - slotEnd).TotalHours;

        return gapHours >= 0 && gapHours < minRestHours;
    }

    private static bool ExceedsBlockLength(
        CoreAgent agent,
        DateOnly date,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> assigned)
    {
        var softCap = agent.MaxWorkDays > 0 ? agent.MaxWorkDays : 0;
        var hardCap = agent.MaxConsecutiveDays > 0
            ? agent.MaxConsecutiveDays
            : context.SchedulingMaxConsecutiveDays;

        var before = CountConsecutive(agent.Id, date, assigned, step: -1);
        var after = CountConsecutive(agent.Id, date, assigned, step: +1);
        var runLength = before + 1 + after;

        if (softCap > 0 && runLength > softCap)
        {
            return true;
        }

        if (hardCap > 0 && runLength > hardCap)
        {
            return true;
        }

        return false;
    }

    private static bool ViolatesMinRestDays(
        CoreAgent agent,
        DateOnly date,
        IReadOnlyList<CoreToken> assigned)
    {
        if (agent.MinRestDays <= 0)
        {
            return false;
        }

        var hasPrev = HasAssignmentOnDate(agent.Id, date.AddDays(-1), assigned);
        var hasNext = HasAssignmentOnDate(agent.Id, date.AddDays(+1), assigned);

        if (!hasPrev)
        {
            var lastBefore = FindNearestAssignedDate(agent.Id, date, assigned, step: -1);
            if (lastBefore.HasValue)
            {
                var gap = (date.DayNumber - lastBefore.Value.DayNumber) - 1;
                if (gap < agent.MinRestDays)
                {
                    return true;
                }
            }
        }

        if (!hasNext)
        {
            var firstAfter = FindNearestAssignedDate(agent.Id, date, assigned, step: +1);
            if (firstAfter.HasValue)
            {
                var gap = (firstAfter.Value.DayNumber - date.DayNumber) - 1;
                if (gap < agent.MinRestDays)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static DateOnly? FindNearestAssignedDate(
        string agentId,
        DateOnly anchor,
        IReadOnlyList<CoreToken> assigned,
        int step)
    {
        DateOnly? best = null;
        foreach (var token in assigned)
        {
            if (token.AgentId != agentId)
            {
                continue;
            }

            if (step < 0 && token.Date < anchor)
            {
                if (!best.HasValue || token.Date > best.Value)
                {
                    best = token.Date;
                }
            }
            else if (step > 0 && token.Date > anchor)
            {
                if (!best.HasValue || token.Date < best.Value)
                {
                    best = token.Date;
                }
            }
        }

        return best;
    }

    private static int CountConsecutive(
        string agentId,
        DateOnly anchor,
        IReadOnlyList<CoreToken> assigned,
        int step)
    {
        var count = 0;
        var probe = anchor.AddDays(step);
        while (HasAssignmentOnDate(agentId, probe, assigned))
        {
            count++;
            probe = probe.AddDays(step);
        }

        return count;
    }

    private static bool HasAssignmentOnDate(
        string agentId,
        DateOnly date,
        IReadOnlyList<CoreToken> assigned)
    {
        foreach (var token in assigned)
        {
            if (token.AgentId == agentId && token.Date == date)
            {
                return true;
            }
        }

        return false;
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
        var cap = ResolveDailyCap(agent, date, context);
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

    private static double ResolveDailyCap(CoreAgent agent, DateOnly date, CoreWizardContext context)
    {
        foreach (var day in context.ContractDays)
        {
            if (day.AgentId == agent.Id && day.Date == date && day.MaximumHoursPerDay > 0)
            {
                return day.MaximumHoursPerDay;
            }
        }

        return agent.MaxDailyHours > 0 ? agent.MaxDailyHours : context.SchedulingMaxDailyHours;
    }
}
