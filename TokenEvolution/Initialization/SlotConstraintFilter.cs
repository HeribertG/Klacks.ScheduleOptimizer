// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

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
    /// <paramref name="relaxation"/> selects the rung of the coverage escalation:
    /// <see cref="SlotRelaxation.All"/> lets the MaxWorkDays block ideal step aside — the last resort
    /// of the escalation, because coverage is the highest rule of the specification and the block
    /// ideal is not. No rung touches a hard rule: the MaxConsecutiveDays cap, collisions, bans,
    /// keywords, the shift blacklist, breaks, hour caps, the minimum pause, the restricted windows, the package rest
    /// (MinRestDays as hours, owner ruling 2026-08-12) AND the weekly rest days of the schedule check
    /// (<see cref="WeeklyRestDayGuard"/>) veto on every rung —
    /// <see cref="SlotRelaxation.RestDaysOnly"/> is a historic no-op rung since that ruling.
    /// </summary>
    public static bool IsValidAssignment(
        CoreAgent agent,
        DateOnly date,
        int shiftTypeIndex,
        Guid shiftRefId,
        decimal slotHours,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> alreadyAssigned,
        DateTime? slotStartUtc = null,
        DateTime? slotEndUtc = null,
        SlotRelaxation relaxation = SlotRelaxation.None)
    {
        if (ViolatesAbsoluteVeto(agent, date, shiftTypeIndex, shiftRefId, context, alreadyAssigned, slotStartUtc, slotEndUtc))
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

        if (SlotPackageRules.ExceedsBlockLength(agent, date, context, alreadyAssigned, relaxation != SlotRelaxation.All))
        {
            return false;
        }

        if (SlotPackageRules.ViolatesMinRestDays(agent, date, alreadyAssigned, context, slotStartUtc, slotEndUtc))
        {
            return false;
        }

        return !(slotStartUtc.HasValue && slotEndUtc.HasValue
            && ViolatesMinPauseHours(agent, slotStartUtc.Value, slotEndUtc.Value, alreadyAssigned, context));
    }

    /// <summary>
    /// The vetoes no coverage pressure may lift: qualification, contract day or weekday, the shift-work flag,
    /// breaks, the day's schedule commands, the shift blacklist, the restricted time windows, a physical
    /// collision and the weekly rest days of the schedule check. The forced-coverage path of the greedy seeder
    /// relaxes everything else (hour caps, block length, package rest, minimum pause) but never these: a slot
    /// it cannot fill under them stays open. Without slot times the window and collision checks are skipped.
    /// <see cref="IsValidAssignment"/> calls this first and adds the relaxable rules on top, so the veto list
    /// exists only here and both paths cannot drift apart.
    /// </summary>
    /// <param name="agent">Agent the slot would be assigned to.</param>
    /// <param name="date">Calendar day the slot starts on.</param>
    /// <param name="shiftTypeIndex">Shift kind of the slot (see ShiftTypeInference).</param>
    /// <param name="shiftRefId">Shift id of the slot, Guid.Empty when unknown.</param>
    /// <param name="context">Wizard context holding the blockers, commands and preferences.</param>
    /// <param name="alreadyAssigned">Tokens placed so far, including locked ones.</param>
    /// <param name="slotStartUtc">Slot start, null when unknown.</param>
    /// <param name="slotEndUtc">Slot end, null when unknown.</param>
    public static bool ViolatesAbsoluteVeto(
        CoreAgent agent,
        DateOnly date,
        int shiftTypeIndex,
        Guid shiftRefId,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> alreadyAssigned,
        DateTime? slotStartUtc,
        DateTime? slotEndUtc)
    {
        if (ViolatesDayVeto(agent, date, shiftTypeIndex, shiftRefId, context))
        {
            return true;
        }

        if (slotStartUtc.HasValue && slotEndUtc.HasValue
            && (IsBlockedByRestrictedWindow(shiftRefId, slotStartUtc.Value, slotEndUtc.Value, context.RestrictedTimeWindows)
                || HasHardTemporalCollision(agent.Id, slotStartUtc.Value, slotEndUtc.Value, context, alreadyAssigned)))
        {
            return true;
        }

        return WeeklyRestDayGuard.Violates(agent, date, alreadyAssigned, context, slotStartUtc, slotEndUtc);
    }

    private static bool ViolatesDayVeto(
        CoreAgent agent, DateOnly date, int shiftTypeIndex, Guid shiftRefId, CoreWizardContext context)
    {
        // Qualification gating is an O(1) lookup, so it runs first: an agent lacking a mandatory
        // qualification of the shift may never receive it (empty set = no-op).
        if (shiftRefId != Guid.Empty && !context.IsEligible(agent.Id, shiftRefId, date))
        {
            return true;
        }

        // Per-date contract availability wins over the static weekday flags: a contract starting
        // or ending mid-period makes individual days non-workable regardless of the weekday.
        var worksOnDate = context.WorksOnDate(agent.Id, date);
        if (worksOnDate.HasValue ? !worksOnDate.Value : !RespectsWeekday(agent, date.DayOfWeek))
        {
            return true;
        }

        if (!agent.PerformsShiftWork && shiftTypeIndex != ShiftTypeInference.EarlyIndex)
        {
            return true;
        }

        return IsBlockedByBreak(agent.Id, date, context.BreakBlockers)
            || !RespectsKeyword(agent.Id, date, shiftTypeIndex, context.ScheduleCommands)
            || (shiftRefId != Guid.Empty && IsBlacklistedShift(agent.Id, shiftRefId, context.ShiftPreferences));
    }

    // K16 seasonal daily forbidden-time window. Always a hard veto (like a break blocker), independent of
    // the compliance enforcement mode, so the GA never seeds a restricted shift into a banned window and
    // instead lays split shifts around it. Empty window set = no-op.
    private static bool IsBlockedByRestrictedWindow(
        Guid shiftRefId, DateTime slotStart, DateTime slotEnd, IReadOnlyList<CoreRestrictedTimeWindow> windows)
    {
        foreach (var window in windows)
        {
            if (window.Blocks(slotStart, slotEnd, shiftRefId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Physical double booking: the slot overlaps another assignment of the same agent, an existing work
    /// inside or next to the period, or a locked work on the boundary days. Bundled so the forced
    /// coverage path can veto collisions even where the soft rules are deliberately skipped - a plan may
    /// leave a slot open, but it may never put one agent in two places at once. Boundary locked works
    /// used to be checked for the rest gap only, and a rest-gap check reports no violation on a real
    /// overlap, so an overnight boundary shift passed even the fully validated path.
    /// </summary>
    /// <param name="agentId">Agent the slot would be assigned to.</param>
    /// <param name="slotStart">Start of the slot in UTC.</param>
    /// <param name="slotEnd">End of the slot in UTC.</param>
    /// <param name="context">Wizard context holding the external blockers.</param>
    /// <param name="alreadyAssigned">Tokens placed so far, including locked ones.</param>
    public static bool HasHardTemporalCollision(
        string agentId,
        DateTime slotStart,
        DateTime slotEnd,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> alreadyAssigned)
    {
        if (HasOverlappingShift(agentId, slotStart, slotEnd, alreadyAssigned))
        {
            return true;
        }

        if (HasOverlappingExistingWork(agentId, slotStart, slotEnd, context.ExistingWorkBlockers))
        {
            return true;
        }

        if (HasOverlappingExistingWork(agentId, slotStart, slotEnd, context.BoundaryExistingWorkBlockers))
        {
            return true;
        }

        foreach (var locked in context.BoundaryLockedWorks)
        {
            if (locked.AgentId == agentId && locked.StartAt < slotEnd && slotStart < locked.EndAt)
            {
                return true;
            }
        }

        return false;
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

        foreach (var locked in context.BoundaryLockedWorks)
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

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
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

    internal static bool IsBlacklistedShift(string agentId, Guid shiftRefId, IReadOnlyList<CoreShiftPreference> preferences)
    {
        foreach (var preference in preferences)
        {
            if (preference.AgentId == agentId
                && preference.ShiftRefId == shiftRefId
                && preference.Kind == ShiftPreferenceKind.Blacklist)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool RespectsKeyword(
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
