// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.RestDays;
using Klacks.ScheduleOptimizer.Common.WeeklyHours;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Wizard-1 hard veto for the maximum weekly hours, under the one week definition of <see cref="CalendarWeekHours"/>:
/// a slot is refused when the paid hours starting in its ISO calendar week would exceed the agent's cap (contract value,
/// otherwise the scheduling default; zero or less = unconstrained). Counted are the agent's tokens (locked tokens are part
/// of the genome, so <see cref="CoreWizardContext.LockedWorks"/> is not added again), the unlocked existing works in the
/// period and the locked and existing works on the boundary days. Breaks and absences never count.
/// </summary>
public static class WeeklyHoursGuard
{
    /// <summary>Effective weekly cap of the agent: its contract value, otherwise the scheduling default.</summary>
    /// <param name="agent">Agent whose cap is resolved</param>
    /// <param name="context">Wizard context holding the scheduling default</param>
    public static double CapOf(CoreAgent agent, CoreWizardContext context)
        => agent.MaxWeeklyHours > 0 ? agent.MaxWeeklyHours : context.SchedulingMaxWeeklyHours;

    /// <summary>True when placing the slot would push the agent's calendar week over its weekly cap.</summary>
    /// <param name="agent">Agent the slot would be assigned to</param>
    /// <param name="date">Calendar day the slot starts on</param>
    /// <param name="slotHours">Paid hours of the slot</param>
    /// <param name="assigned">Tokens placed so far (locked tokens included), the candidate slot excluded</param>
    /// <param name="context">Wizard context holding the existing and boundary works</param>
    public static bool Violates(
        CoreAgent agent,
        DateOnly date,
        decimal slotHours,
        IReadOnlyList<CoreToken> assigned,
        CoreWizardContext context)
    {
        var cap = CapOf(agent, context);
        if (cap <= 0)
        {
            return false;
        }

        var weekStart = CalendarWeekRestDays.WeekStartOf(date);
        var hours = slotHours;
        foreach (var token in assigned)
        {
            if (token.AgentId == agent.Id && CalendarWeekHours.IsInWeek(token.Date, weekStart))
            {
                hours += token.TotalHours;
            }
        }

        foreach (var work in WorksOutsidePlan(context.ExistingWorkBlockers, context))
        {
            if (work.AgentId == agent.Id && CalendarWeekHours.IsInWeek(work.Date, weekStart))
            {
                hours += work.Hours;
            }
        }

        return CalendarWeekHours.Exceeds(hours, (decimal)cap);
    }

    /// <summary>
    /// The works outside the plan that count toward the weekly hours: the given in-period existing works and the locked
    /// and existing works on the boundary days. One list for the slot veto and the plan-level check.
    /// </summary>
    /// <param name="inPeriodExisting">Unlocked existing works inside the period that are not plan assignments</param>
    /// <param name="context">Wizard context holding the boundary works</param>
    internal static IEnumerable<(string AgentId, DateOnly Date, decimal Hours)> WorksOutsidePlan(
        IReadOnlyList<CoreExistingWorkBlocker> inPeriodExisting, CoreWizardContext context)
    {
        foreach (var work in inPeriodExisting)
        {
            yield return (work.AgentId, work.Date, work.Hours);
        }

        foreach (var work in context.BoundaryLockedWorks)
        {
            yield return (work.AgentId, work.Date, work.TotalHours);
        }

        foreach (var work in context.BoundaryExistingWorkBlockers)
        {
            yield return (work.AgentId, work.Date, work.Hours);
        }
    }
}
