// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.RestDays;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// The package rules of <see cref="SlotConstraintFilter"/> that read the occupancy around a slot: the block length
/// (MaxWorkDays ideal and MaxConsecutiveDays cap) and the rest between two packages (MinRestDays as hours).
/// Occupancy comes from the tokens placed so far plus the locked works and existing works on the period boundary.
/// Split off the filter so it stays readable; the filter remains the single entry point of the slot checks.
/// </summary>
internal static class SlotPackageRules
{
    internal static bool ExceedsBlockLength(
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

    /// <summary>
    /// Rest between two packages, measured in HOURS: the configured MinRestDays times 24, from the end
    /// of the last shift of one package to the start of the first shift of the next (owner ruling
    /// 2026-08-12, SPEC.md decision 12d — "2 days, computed as hours, so 48h", not two calendar days).
    /// A day adjacent to an occupied day extends that package and is exempt, exactly as before. When
    /// the caller supplies no slot times the old calendar-day arithmetic remains as the fallback.
    /// Since the same ruling the check holds on EVERY escalation rung — the repair ladder may no
    /// longer trade package rest for coverage.
    /// </summary>
    internal static bool ViolatesMinRestDays(
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
        var requiredRestHours = CalendarWeekRestDays.MinimumFreeBlock(agent.MinRestDays).TotalHours;

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
}
