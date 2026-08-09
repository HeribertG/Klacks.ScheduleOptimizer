// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Counts, per (calendar date, shift reference), how many assignments a slot already carries, so a
/// seeding strategy can leave an occupied slot alone instead of staffing it a second time. Lookup and
/// update are O(1); an empty occupancy answers "not satisfied" for every slot, which is exactly the
/// behaviour of a context without pre-placed work.
/// </summary>
public sealed class SlotOccupancy
{
    /// <summary>A slot always needs at least one assignment, even when the demand field says otherwise.</summary>
    private const int MinimumRequiredAssignments = 1;

    private readonly Dictionary<(DateOnly Date, Guid ShiftRefId), int> _counts;

    private SlotOccupancy(Dictionary<(DateOnly Date, Guid ShiftRefId), int> counts) => _counts = counts;

    /// <summary>Counts the assignments the given tokens place on each slot.</summary>
    /// <param name="tokens">Tokens already present in the genome, locked and seeded alike</param>
    public static SlotOccupancy Of(IReadOnlyList<CoreToken> tokens)
    {
        var counts = new Dictionary<(DateOnly, Guid), int>(tokens.Count);
        foreach (var token in tokens)
        {
            Increment(counts, token.Date, token.ShiftRefId);
        }

        return new SlotOccupancy(counts);
    }

    /// <summary>Counts the assignments the given fixed works place on each slot.</summary>
    /// <param name="lockedWorks">Works the run must keep unchanged</param>
    public static SlotOccupancy Of(IReadOnlyList<CoreLockedWork> lockedWorks)
    {
        var counts = new Dictionary<(DateOnly, Guid), int>(lockedWorks.Count);
        foreach (var work in lockedWorks)
        {
            Increment(counts, work.Date, work.ShiftRefId);
        }

        return new SlotOccupancy(counts);
    }

    /// <summary>True when the slot already carries as many assignments as it demands.</summary>
    /// <param name="slot">Slot to test; an unparseable date or shift id is never satisfied</param>
    public bool IsSatisfied(CoreShift slot)
    {
        if (!Guid.TryParse(slot.Id, out var shiftRefId)
            || !DateOnly.TryParse(slot.Date, out var date))
        {
            return false;
        }

        return IsSatisfied(date, shiftRefId, slot.RequiredAssignments);
    }

    /// <summary>True when the slot already carries as many assignments as it demands.</summary>
    /// <param name="date">Calendar date of the slot</param>
    /// <param name="shiftRefId">Shift definition the slot belongs to</param>
    /// <param name="requiredAssignments">Demand of the slot; values below one are read as one</param>
    public bool IsSatisfied(DateOnly date, Guid shiftRefId, int requiredAssignments)
    {
        if (shiftRefId == Guid.Empty)
        {
            return false;
        }

        return _counts.GetValueOrDefault((date, shiftRefId), 0)
            >= Math.Max(MinimumRequiredAssignments, requiredAssignments);
    }

    /// <summary>Records one further assignment on the slot.</summary>
    /// <param name="date">Calendar date of the slot</param>
    /// <param name="shiftRefId">Shift definition the slot belongs to</param>
    public void Add(DateOnly date, Guid shiftRefId) => Increment(_counts, date, shiftRefId);

    private static void Increment(
        Dictionary<(DateOnly Date, Guid ShiftRefId), int> counts, DateOnly date, Guid shiftRefId)
    {
        if (shiftRefId == Guid.Empty)
        {
            return;
        }

        var key = (date, shiftRefId);
        counts[key] = counts.GetValueOrDefault(key, 0) + 1;
    }
}
