// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.RestDays;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Wizard-1 hard veto for the weekly rest days exactly as the schedule check counts them
/// (<see cref="CalendarWeekRestDays"/>): a slot is refused when it turns a rest day of a
/// Monday-to-Sunday week into a work day and that week then keeps fewer than the agent's MinRestDays.
/// Occupancy comes from every work the wizard knows for the agent: the plan, locked works, unlocked
/// existing works and the boundary works around the period, so weeks crossing the period edges are
/// judged as well; days beyond the boundary data count as free. Complements the package-rest gap rule (owner ruling 2026-08-12), it does not replace it.
/// </summary>
public static class WeeklyRestDayGuard
{
    /// <summary>True when placing the slot would break the weekly rest-day minimum of the agent.</summary>
    /// <param name="agent">Agent the slot would be assigned to</param>
    /// <param name="date">Calendar day the slot belongs to; the slot span when no times are given</param>
    /// <param name="assigned">Tokens placed so far, the candidate slot excluded</param>
    /// <param name="context">Wizard context holding locked, existing and boundary works</param>
    /// <param name="slotStart">Start of the slot, or null to treat the slot as the whole day</param>
    /// <param name="slotEnd">End of the slot, or null to treat the slot as the whole day</param>
    public static bool Violates(
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

        var slot = slotStart.HasValue && slotEnd.HasValue
            ? CalendarWeekRestDays.IntervalOrWholeDay(slotStart.Value, slotEnd.Value, date)
            : CalendarWeekRestDays.IntervalOrWholeDay(default, default, date);

        var firstRelevantDay = CalendarWeekRestDays.FirstAffectedDay(slot, agent.MinRestDays).AddDays(-CalendarWeekRestDays.DaysPerWeek);
        var lastRelevantDay = CalendarWeekRestDays.LastRelevantDay(CalendarWeekRestDays.LastTouchedDay(slot), agent.MinRestDays);
        var existing = WorksOf(agent.Id, assigned, context, firstRelevantDay, lastRelevantDay);

        return CalendarWeekRestDays.AdditionBreaksMinimum(existing, slot, agent.MinRestDays);
    }

    private static List<WorkInterval> WorksOf(
        string agentId, IReadOnlyList<CoreToken> assigned, CoreWizardContext context, DateOnly firstDay, DateOnly lastDay)
    {
        var works = new List<WorkInterval>();
        var range = (First: firstDay, Last: lastDay);
        Collect(works, agentId, range, assigned, token => (token.AgentId, token.Date, token.StartAt, token.EndAt));
        Collect(works, agentId, range, context.LockedWorks, work => (work.AgentId, work.Date, work.StartAt, work.EndAt));
        Collect(works, agentId, range, context.BoundaryLockedWorks, work => (work.AgentId, work.Date, work.StartAt, work.EndAt));
        Collect(works, agentId, range, context.ExistingWorkBlockers, work => (work.AgentId, work.Date, work.StartAt, work.EndAt));
        Collect(works, agentId, range, context.BoundaryExistingWorkBlockers, work => (work.AgentId, work.Date, work.StartAt, work.EndAt));
        return works;
    }

    private static void Collect<T>(
        List<WorkInterval> target,
        string agentId,
        (DateOnly First, DateOnly Last) range,
        IReadOnlyList<T> source,
        Func<T, (string AgentId, DateOnly Date, DateTime Start, DateTime End)> selector)
    {
        foreach (var item in source)
        {
            var (itemAgentId, day, start, end) = selector(item);
            if (itemAgentId == agentId && day >= range.First && day <= range.Last)
            {
                target.Add(CalendarWeekRestDays.IntervalOrWholeDay(start, end, day));
            }
        }
    }
}