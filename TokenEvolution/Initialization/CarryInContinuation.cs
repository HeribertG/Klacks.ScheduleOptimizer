// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Recognises which employees enter the planning period in the middle of a work package: the fixed
/// work of the previous period and the frozen head of a replanning run are read with the same rules,
/// so the wizard start and the replan seam answer the same question. Pure and deterministic — no
/// random source, no clock, roster order throughout.
/// </summary>
public static class CarryInContinuation
{
    /// <summary>
    /// First day of the period the run may still staff: days whose slots are all covered by fixed work
    /// are the frozen head of a replanning run and are skipped. A day without demand cannot be planned
    /// either and is skipped as well, which keeps the anchor from ever landing inside a frozen head.
    /// </summary>
    /// <param name="context">Wizard context supplying the period, the slots and the fixed works</param>
    public static DateOnly FirstPlannableDay(CoreWizardContext context)
    {
        if (context.LockedWorks.Count == 0)
        {
            return context.PeriodFrom;
        }

        var occupancy = SlotOccupancy.Of(context.LockedWorks);
        var slotsByDate = BuildSlotsByDate(context);

        for (var day = context.PeriodFrom; day <= context.PeriodUntil; day = day.AddDays(1))
        {
            if (!slotsByDate.TryGetValue(day, out var slots))
            {
                continue;
            }

            foreach (var slot in slots)
            {
                if (!occupancy.IsSatisfied(slot))
                {
                    return day;
                }
            }
        }

        return context.PeriodUntil.AddDays(1);
    }

    /// <summary>
    /// The open packages at the first plannable day of the context.
    /// </summary>
    /// <param name="context">Wizard context supplying the roster, the period and the fixed works</param>
    public static IReadOnlyList<CarryInPackage> Detect(CoreWizardContext context)
        => Detect(context, FirstPlannableDay(context));

    /// <summary>
    /// The open packages at a given anchor day, in roster order. A package counts as open when the
    /// employee worked the day before the anchor, the run of fixed work days ending there carries a
    /// single shift kind, and the run is shorter than the employee's package length. An employee
    /// without a package length has no package ideal and never appears here, so a context that does
    /// not use the rule behaves exactly as before.
    /// </summary>
    /// <param name="context">Wizard context supplying the roster and the fixed works</param>
    /// <param name="anchor">First day the run may staff</param>
    public static IReadOnlyList<CarryInPackage> Detect(CoreWizardContext context, DateOnly anchor)
    {
        var packages = new List<CarryInPackage>();
        if (anchor > context.PeriodUntil || context.Agents.Count == 0)
        {
            return packages;
        }

        var occupiedDays = BuildOccupiedDays(context);
        var fixedWork = BuildFixedWork(context);
        var lastDay = anchor.AddDays(-1);

        foreach (var agent in context.Agents)
        {
            if (agent.MaxWorkDays <= 0
                || !occupiedDays.TryGetValue(agent.Id, out var days)
                || !days.Contains(lastDay))
            {
                continue;
            }

            var servedDays = 0;
            var probe = lastDay;
            while (days.Contains(probe))
            {
                servedDays++;
                probe = probe.AddDays(-1);
            }

            var remainingDays = agent.MaxWorkDays - servedDays;
            if (remainingDays <= 0)
            {
                continue;
            }

            var package = BuildPackage(agent.Id, fixedWork, probe.AddDays(1), lastDay, servedDays, remainingDays);
            if (package is not null)
            {
                packages.Add(package);
            }
        }

        return packages;
    }

    private static CarryInPackage? BuildPackage(
        string agentId,
        IReadOnlyDictionary<(string AgentId, DateOnly Date), FixedWorkDay> fixedWork,
        DateOnly firstDay,
        DateOnly lastDay,
        int servedDays,
        int remainingDays)
    {
        int? shiftTypeIndex = null;
        var shiftRefId = Guid.Empty;
        string? locationContext = null;

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (!fixedWork.TryGetValue((agentId, day), out var work))
            {
                continue;
            }

            if (shiftTypeIndex is null)
            {
                shiftTypeIndex = work.ShiftTypeIndex;
            }
            else if (shiftTypeIndex.Value != work.ShiftTypeIndex)
            {
                return null;
            }

            shiftRefId = work.ShiftRefId;
            locationContext = work.LocationContext;
        }

        if (shiftTypeIndex is null || shiftRefId == Guid.Empty)
        {
            return null;
        }

        return new CarryInPackage(
            agentId, shiftRefId, locationContext, shiftTypeIndex.Value, lastDay, servedDays, remainingDays);
    }

    /// <summary>
    /// Days on which an employee is occupied by fixed work, using the same cross-midnight semantics as
    /// the hard-constraint checker: a night shift occupies the day it starts and the day it ends.
    /// </summary>
    private static Dictionary<string, HashSet<DateOnly>> BuildOccupiedDays(CoreWizardContext context)
    {
        var occupied = new Dictionary<string, HashSet<DateOnly>>(StringComparer.Ordinal);

        foreach (var work in context.BoundaryLockedWorks)
        {
            AddSpan(occupied, work.AgentId, work.StartAt, work.EndAt);
        }

        foreach (var work in context.LockedWorks)
        {
            AddSpan(occupied, work.AgentId, work.StartAt, work.EndAt);
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            AddSpan(occupied, blocker.AgentId, blocker.StartAt, blocker.EndAt);
        }

        return occupied;
    }

    /// <summary>
    /// Shift kind, shift reference and location of the fixed work an employee starts on a day. Only
    /// works carrying a shift reference qualify — a package without an order cannot be continued on one.
    /// </summary>
    private static Dictionary<(string AgentId, DateOnly Date), FixedWorkDay> BuildFixedWork(
        CoreWizardContext context)
    {
        var fixedWork = new Dictionary<(string, DateOnly), FixedWorkDay>();

        foreach (var work in context.BoundaryLockedWorks)
        {
            fixedWork[(work.AgentId, work.Date)] =
                new FixedWorkDay(work.ShiftTypeIndex, work.ShiftRefId, work.LocationContext);
        }

        foreach (var work in context.LockedWorks)
        {
            fixedWork[(work.AgentId, work.Date)] =
                new FixedWorkDay(work.ShiftTypeIndex, work.ShiftRefId, work.LocationContext);
        }

        return fixedWork;
    }

    private static Dictionary<DateOnly, List<CoreShift>> BuildSlotsByDate(CoreWizardContext context)
    {
        var slotsByDate = new Dictionary<DateOnly, List<CoreShift>>();
        foreach (var slot in context.Shifts)
        {
            if (!DateOnly.TryParse(slot.Date, out var date))
            {
                continue;
            }

            if (!slotsByDate.TryGetValue(date, out var slots))
            {
                slots = [];
                slotsByDate[date] = slots;
            }

            slots.Add(slot);
        }

        return slotsByDate;
    }

    private static void AddSpan(
        Dictionary<string, HashSet<DateOnly>> occupied, string agentId, DateTime startAt, DateTime endAt)
    {
        if (!occupied.TryGetValue(agentId, out var days))
        {
            days = [];
            occupied[agentId] = days;
        }

        var first = DateOnly.FromDateTime(startAt);
        var last = DateOnly.FromDateTime(endAt);
        if (endAt.TimeOfDay == TimeSpan.Zero && last > first)
        {
            last = last.AddDays(-1);
        }

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            days.Add(day);
        }
    }

    private readonly record struct FixedWorkDay(int ShiftTypeIndex, Guid ShiftRefId, string? LocationContext);
}
