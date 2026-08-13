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
    /// <paramref name="relaxation"/> selects the rung of the coverage escalation:
    /// <see cref="SlotRelaxation.All"/> lets the MaxWorkDays block ideal step aside — the last resort
    /// of the escalation, because coverage is the highest rule of the specification and the block
    /// ideal is not. No rung touches a hard rule: the MaxConsecutiveDays cap, collisions, bans,
    /// keywords, breaks, hour caps, the minimum pause, the restricted windows AND the package rest
    /// (MinRestDays as hours, owner ruling 2026-08-12) veto on every rung —
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
        // Qualification gating is a hard prerequisite and an O(1) lookup, so it runs first: an agent
        // lacking a mandatory qualification of the shift may never receive it (empty set = no-op).
        if (shiftRefId != Guid.Empty && !context.IsEligible(agent.Id, shiftRefId, date))
        {
            return false;
        }

        // Per-date contract availability wins over the static weekday flags: a contract starting
        // or ending mid-period makes individual days non-workable regardless of the weekday.
        var worksOnDate = context.WorksOnDate(agent.Id, date);
        if (worksOnDate.HasValue)
        {
            if (!worksOnDate.Value)
            {
                return false;
            }
        }
        else if (!RespectsWeekday(agent, date.DayOfWeek))
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

        if (ExceedsBlockLength(agent, date, context, alreadyAssigned, relaxation != SlotRelaxation.All))
        {
            return false;
        }

        if (ViolatesMinRestDays(agent, date, alreadyAssigned, context, slotStartUtc, slotEndUtc))
        {
            return false;
        }

        if (slotStartUtc.HasValue && slotEndUtc.HasValue)
        {
            if (HasHardTemporalCollision(agent.Id, slotStartUtc.Value, slotEndUtc.Value, context, alreadyAssigned))
            {
                return false;
            }

            if (IsBlockedByRestrictedWindow(shiftRefId, slotStartUtc.Value, slotEndUtc.Value, context.RestrictedTimeWindows))
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

    private static bool ExceedsBlockLength(
        CoreAgent agent,
        DateOnly date,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> assigned,
        bool applySoftCap)
    {
        var softCap = applySoftCap && agent.MaxWorkDays > 0 ? agent.MaxWorkDays : 0;
        var hardCap = agent.MaxConsecutiveDays > 0
            ? agent.MaxConsecutiveDays
            : context.SchedulingMaxConsecutiveDays;

        var before = CountConsecutive(agent.Id, date, assigned, context, step: -1);
        var after = CountConsecutive(agent.Id, date, assigned, context, step: +1);
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

    /// <summary>Owner ruling 2026-08-12: one configured rest day between packages equals 24 hours.</summary>
    private const int HoursPerRestDay = 24;

    /// <summary>
    /// Rest between two packages, measured in HOURS: the configured MinRestDays times 24, from the end
    /// of the last shift of one package to the start of the first shift of the next (owner ruling
    /// 2026-08-12, SPEC.md decision 12d — "2 days, computed as hours, so 48h", not two calendar days).
    /// A day adjacent to an occupied day extends that package and is exempt, exactly as before. When
    /// the caller supplies no slot times the old calendar-day arithmetic remains as the fallback.
    /// Since the same ruling the check holds on EVERY escalation rung — the repair ladder may no
    /// longer trade package rest for coverage.
    /// </summary>
    private static bool ViolatesMinRestDays(
        CoreAgent agent,
        DateOnly date,
        IReadOnlyList<CoreToken> assigned,
        CoreWizardContext context,
        DateTime? slotStart,
        DateTime? slotEnd)
    {
        if (agent.MinRestDays <= 0)
        {
            return false;
        }

        // Package membership follows the day a shift STARTS on, exactly as the package builders read
        // it. The overlap reading of HasAssignmentOnDate would let the morning end of a midnight
        // crosser mark the next day as occupied, and a slot one day later would then pass as a
        // package extension although it opens a NEW package after far too little rest.
        var hasPrev = StartsOnDate(agent.Id, date.AddDays(-1), assigned, context);
        var hasNext = StartsOnDate(agent.Id, date.AddDays(+1), assigned, context);
        var requiredRestHours = agent.MinRestDays * (double)HoursPerRestDay;

        if (!hasPrev)
        {
            var lastBefore = FindNearestOccupiedDate(agent.Id, date, assigned, context, step: -1);
            if (lastBefore.HasValue)
            {
                var latestEnd = slotStart.HasValue
                    ? LatestEndOnDate(agent.Id, lastBefore.Value, assigned, context)
                    : null;
                if (latestEnd.HasValue)
                {
                    if ((slotStart!.Value - latestEnd.Value).TotalHours < requiredRestHours)
                    {
                        return true;
                    }
                }
                else if ((date.DayNumber - lastBefore.Value.DayNumber) - 1 < agent.MinRestDays)
                {
                    return true;
                }
            }
        }

        if (!hasNext)
        {
            var firstAfter = FindNearestOccupiedDate(agent.Id, date, assigned, context, step: +1);
            if (firstAfter.HasValue)
            {
                var earliestStart = slotEnd.HasValue
                    ? EarliestStartOnDate(agent.Id, firstAfter.Value, assigned, context)
                    : null;
                if (earliestStart.HasValue)
                {
                    if ((earliestStart.Value - slotEnd!.Value).TotalHours < requiredRestHours)
                    {
                        return true;
                    }
                }
                else if ((firstAfter.Value.DayNumber - date.DayNumber) - 1 < agent.MinRestDays)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// True when a shift of the agent STARTS on the day — the package reading of occupancy, blind to
    /// the morning end of a midnight crosser on purpose. Internal because the package-aware repair
    /// (SPEC.md decision 13) asks the same question when it prefers a fill that extends a package.
    /// </summary>
    internal static bool StartsOnDate(
        string agentId, DateOnly date, IReadOnlyList<CoreToken> assigned, CoreWizardContext context)
    {
        foreach (var token in assigned)
        {
            if (token.AgentId == agentId && token.Date == date)
            {
                return true;
            }
        }

        foreach (var locked in context.BoundaryLockedWorks)
        {
            if (locked.AgentId == agentId && locked.Date == date)
            {
                return true;
            }
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            if (blocker.AgentId == agentId && blocker.Date == date)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Latest shift end on one occupied day, read from the same sources as
    /// <see cref="FindNearestOccupiedDate"/>; an entry counts for the day it starts on and — for a
    /// midnight crosser — also for the day it ends on.
    /// </summary>
    private static DateTime? LatestEndOnDate(
        string agentId, DateOnly day, IReadOnlyList<CoreToken> assigned, CoreWizardContext context)
    {
        DateTime? latest = null;

        void Consider(string entryAgentId, DateOnly startDay, DateTime startAt, DateTime endAt)
        {
            if (entryAgentId != agentId)
            {
                return;
            }

            if ((startDay == day || DateOnly.FromDateTime(endAt) == day)
                && (!latest.HasValue || endAt > latest.Value))
            {
                latest = endAt;
            }
        }

        foreach (var token in assigned)
        {
            Consider(token.AgentId, token.Date, token.StartAt, token.EndAt);
        }

        foreach (var locked in context.BoundaryLockedWorks)
        {
            Consider(locked.AgentId, locked.Date, locked.StartAt, locked.EndAt);
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            Consider(blocker.AgentId, blocker.Date, blocker.StartAt, blocker.EndAt);
        }

        return latest;
    }

    /// <summary>
    /// Earliest shift start on one occupied day, from the same sources as
    /// <see cref="LatestEndOnDate"/> and with the same midnight-crosser day matching.
    /// </summary>
    private static DateTime? EarliestStartOnDate(
        string agentId, DateOnly day, IReadOnlyList<CoreToken> assigned, CoreWizardContext context)
    {
        DateTime? earliest = null;

        void Consider(string entryAgentId, DateOnly startDay, DateTime startAt, DateTime endAt)
        {
            if (entryAgentId != agentId)
            {
                return;
            }

            if ((startDay == day || DateOnly.FromDateTime(endAt) == day)
                && (!earliest.HasValue || startAt < earliest.Value))
            {
                earliest = startAt;
            }
        }

        foreach (var token in assigned)
        {
            Consider(token.AgentId, token.Date, token.StartAt, token.EndAt);
        }

        foreach (var locked in context.BoundaryLockedWorks)
        {
            Consider(locked.AgentId, locked.Date, locked.StartAt, locked.EndAt);
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            Consider(blocker.AgentId, blocker.Date, blocker.StartAt, blocker.EndAt);
        }

        return earliest;
    }

    private static DateOnly? FindNearestOccupiedDate(
        string agentId,
        DateOnly anchor,
        IReadOnlyList<CoreToken> assigned,
        CoreWizardContext context,
        int step)
    {
        DateOnly? best = null;
        foreach (var token in assigned)
        {
            if (token.AgentId != agentId) continue;
            ConsiderDate(token.Date, anchor, step, ref best);
            if (CrossesMidnight(token))
            {
                ConsiderDate(DateOnly.FromDateTime(token.EndAt), anchor, step, ref best);
            }
        }
        foreach (var locked in context.BoundaryLockedWorks)
        {
            if (locked.AgentId != agentId) continue;
            ConsiderDate(locked.Date, anchor, step, ref best);
            if (locked.EndAt.Date > locked.StartAt.Date)
            {
                ConsiderDate(DateOnly.FromDateTime(locked.EndAt), anchor, step, ref best);
            }
        }
        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            if (blocker.AgentId != agentId) continue;
            ConsiderDate(blocker.Date, anchor, step, ref best);
            if (blocker.EndAt.Date > blocker.StartAt.Date)
            {
                ConsiderDate(DateOnly.FromDateTime(blocker.EndAt), anchor, step, ref best);
            }
        }
        return best;
    }

    private static void ConsiderDate(DateOnly candidate, DateOnly anchor, int step, ref DateOnly? best)
    {
        if (step < 0 && candidate < anchor)
        {
            if (!best.HasValue || candidate > best.Value) best = candidate;
        }
        else if (step > 0 && candidate > anchor)
        {
            if (!best.HasValue || candidate < best.Value) best = candidate;
        }
    }

    private static bool CrossesMidnight(CoreToken token) =>
        token.EndAt.Date > token.StartAt.Date;

    private static int CountConsecutive(
        string agentId,
        DateOnly anchor,
        IReadOnlyList<CoreToken> assigned,
        CoreWizardContext context,
        int step)
    {
        var count = 0;
        var probe = anchor.AddDays(step);
        while (HasAssignmentOnDate(agentId, probe, assigned, context))
        {
            count++;
            probe = probe.AddDays(step);
        }

        return count;
    }

    private static bool HasAssignmentOnDate(
        string agentId,
        DateOnly date,
        IReadOnlyList<CoreToken> assigned,
        CoreWizardContext context)
    {
        foreach (var token in assigned)
        {
            if (token.AgentId == agentId && OccupiesDate(token.StartAt, token.EndAt, date))
            {
                return true;
            }
        }

        foreach (var locked in context.BoundaryLockedWorks)
        {
            if (locked.AgentId == agentId && OccupiesDate(locked.StartAt, locked.EndAt, date))
            {
                return true;
            }
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            if (blocker.AgentId == agentId && OccupiesDate(blocker.StartAt, blocker.EndAt, date))
            {
                return true;
            }
        }

        return false;
    }

    private static bool OccupiesDate(DateTime startAt, DateTime endAt, DateOnly target)
    {
        var dayStart = target.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        return startAt < dayEnd && endAt > dayStart;
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
