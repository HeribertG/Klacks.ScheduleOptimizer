// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Constructs the continuation of the packages an employee carries into the period instead of leaving
/// it to the auction, which values a shift-kind switch higher than the repetition the package needs.
/// The pre-pass runs day by day over all employees at once — never employee by employee, or the first
/// employee would empty the later days before the next one is asked — and every placement passes the
/// full hard-rule filter. A package that finds no legal slot on a day is closed for good: a gap in the
/// middle of a package is worse than a package that ends early, and legality outranks the ideal.
/// Deterministic by construction: roster order, then calendar order, then ordinal shift id.
/// </summary>
public static class CarryInContinuationSeeder
{
    /// <summary>
    /// Appends the continuation tokens to the given genome and returns what was placed together with
    /// the slot occupancy the calling strategy must respect. Without open packages nothing is placed
    /// and the occupancy only reflects the tokens that were already there.
    /// </summary>
    /// <param name="context">Wizard context supplying roster, slots, rules and fixed works</param>
    /// <param name="tokens">Genome under construction; continuation tokens are appended</param>
    public static CarryInSeedResult Seed(CoreWizardContext context, List<CoreToken> tokens)
    {
        var occupancy = SlotOccupancy.Of(tokens);
        var anchor = CarryInContinuation.FirstPlannableDay(context);
        var packages = CarryInContinuation.Detect(context, anchor);
        if (packages.Count == 0)
        {
            return new CarryInSeedResult(occupancy, [], anchor, packages);
        }

        var agents = new Dictionary<string, CoreAgent>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            agents[agent.Id] = agent;
        }

        var lockedDates = new HashSet<DateOnly>();
        foreach (var work in context.LockedWorks)
        {
            lockedDates.Add(work.Date);
        }

        var slotsByDate = BuildSlotsByDate(context);
        var closed = new HashSet<string>(StringComparer.Ordinal);
        var placed = new List<CoreToken>();
        var longestPackage = 0;
        foreach (var package in packages)
        {
            longestPackage = Math.Max(longestPackage, package.RemainingDays);
        }

        for (var offset = 0; offset < longestPackage; offset++)
        {
            var day = anchor.AddDays(offset);
            if (day > context.PeriodUntil)
            {
                break;
            }

            slotsByDate.TryGetValue(day, out var slots);
            var plannable = !lockedDates.Contains(day) && slots is not null;

            foreach (var package in packages)
            {
                if (offset >= package.RemainingDays || closed.Contains(package.AgentId))
                {
                    continue;
                }

                if (!plannable
                    || !agents.TryGetValue(package.AgentId, out var agent)
                    || !TryPlace(context, agent, package, day, slots!, tokens, placed, occupancy))
                {
                    closed.Add(package.AgentId);
                }
            }
        }

        return new CarryInSeedResult(occupancy, placed, anchor, packages);
    }

    private static bool TryPlace(
        CoreWizardContext context,
        CoreAgent agent,
        CarryInPackage package,
        DateOnly day,
        IReadOnlyList<CoreShift> slots,
        List<CoreToken> tokens,
        List<CoreToken> placed,
        SlotOccupancy occupancy)
    {
        CoreShift? sameOrder = null;
        CoreShift? sameKind = null;

        foreach (var slot in slots)
        {
            if (!Guid.TryParse(slot.Id, out var shiftRefId)
                || occupancy.IsSatisfied(day, shiftRefId, slot.RequiredAssignments)
                || ShiftTypeInference.FromSpanString(slot.StartTime, slot.EndTime) != package.ShiftTypeIndex)
            {
                continue;
            }

            if (shiftRefId == package.ShiftRefId)
            {
                sameOrder = slot;
                break;
            }

            sameKind ??= slot;
        }

        var chosen = sameOrder ?? sameKind;
        if (chosen is null)
        {
            return false;
        }

        return TryAppend(context, agent, chosen, day, tokens, placed, occupancy);
    }

    private static bool TryAppend(
        CoreWizardContext context,
        CoreAgent agent,
        CoreShift slot,
        DateOnly day,
        List<CoreToken> tokens,
        List<CoreToken> placed,
        SlotOccupancy occupancy)
    {
        var start = TimeOnly.TryParse(slot.StartTime, out var parsedStart) ? parsedStart : DefaultStart;
        var end = TimeOnly.TryParse(slot.EndTime, out var parsedEnd) ? parsedEnd : start.AddHours(DefaultShiftHours);
        var shiftTypeIndex = ShiftTypeInference.FromSpan(start, end);
        var slotHours = (decimal)slot.Hours;
        var shiftRefId = Guid.Parse(slot.Id);
        var startAt = day.ToDateTime(start);
        var endAt = end <= start ? day.AddDays(1).ToDateTime(end) : day.ToDateTime(end);

        if (!SlotConstraintFilter.IsValidAssignment(
                agent, day, shiftTypeIndex, shiftRefId, slotHours, context, tokens, startAt, endAt))
        {
            return false;
        }

        var token = new CoreToken(
            WorkIds: [],
            ShiftTypeIndex: shiftTypeIndex,
            Date: day,
            TotalHours: slotHours,
            StartAt: startAt,
            EndAt: endAt,
            BlockId: Guid.NewGuid(),
            PositionInBlock: 0,
            IsLocked: false,
            LocationContext: null,
            ShiftRefId: shiftRefId,
            AgentId: agent.Id)
        {
            Surcharges = SurchargeEstimator.Estimate(slotHours, shiftTypeIndex, day, agent),
        };

        tokens.Add(token);
        placed.Add(token);
        occupancy.Add(day, shiftRefId);
        return true;
    }

    private static Dictionary<DateOnly, List<CoreShift>> BuildSlotsByDate(CoreWizardContext context)
    {
        var slotsByDate = new Dictionary<DateOnly, List<CoreShift>>();
        foreach (var slot in context.Shifts.OrderBy(s => s.Id, StringComparer.Ordinal))
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

    /// <summary>Fallback start time for a slot whose start cannot be parsed; mirrors the auction.</summary>
    private static readonly TimeOnly DefaultStart = new(8, 0);

    /// <summary>Fallback shift length in hours for a slot whose end cannot be parsed; mirrors the auction.</summary>
    private const int DefaultShiftHours = 8;
}
