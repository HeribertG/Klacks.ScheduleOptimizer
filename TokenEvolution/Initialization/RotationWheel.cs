// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Rank-aware staircase ("wheel") for one start plan of Wizard 1: one cycle of stages of seven days, one stage per
/// demanded shift kind in the order early, late, night, each stage w days of its kind and 7 - w free days, with
/// w = min(MaxWorkDays, 7 - MinRestDays) taken from the settings (upper median of the team), capped at
/// SchedulingMaxConsecutiveDays - 1 when nights are demanded. The seats on the wheel go to the
/// top of the roster: their number is the bottleneck floor(min daily demand per kind x cycle length / w), so the
/// phases chosen at the start never staff a kind twice where it is demanded once (a re-entry after absence is not
/// checked against the other rows); demand above the bottleneck and everybody below the seats is left
/// to the fillers and the other start plans.
/// <para>
/// A seat holder with worked days in the known previous days (at most <see cref="KnownHistoryDays"/> before the first
/// plannable day: the default context window of the wizard request, which the context itself does not carry) continues its own phase; days outside that window are unknown
/// and never count as free. Without any history the seats share evenly spaced phases, the most work days to the top
/// rank (<see cref="RotationWheelPlacement"/>). Absence
/// days and days on which the kind is closed to the person are holes; after at least
/// <see cref="ShiftRotation.LongPauseFreeDays"/> consecutive absence days the row restarts with a stage of the ideal
/// re-entry kind (early, else late, else night).
/// </para>
/// <para>
/// No wheel (null) when: the period is shorter than one cycle, fewer than two seats, a seat holder cannot work every demanded shift kind (one group only; the
/// persons below the seats are fillers whatever they can work), or a seat row would land more than one daily working time above the person's contract hours or above the
/// maximum hours (a part-time contract cannot carry a 5/2 row; the gate counts the row's shifts at the mean shift hours
/// of their kind, without current hours, carry-in or surcharges). The wheel is only a preference; the strategy checks every
/// assignment with the same filter as every other start plan.
/// </para>
/// </summary>
public sealed class RotationWheel
{
    /// <summary>Days before the first plannable day read as known history; equals the default context window of the wizard request.</summary>
    public const int KnownHistoryDays = 14;

    /// <summary>Length of one stage of the wheel in days.</summary>
    public const int StageDays = 7;

    private const int DefaultWorkDays = 5;
    private const int MinSeats = 2;

    private readonly Dictionary<string, int[]> _rows;
    private readonly DateOnly _periodFrom;

    private RotationWheel(Dictionary<string, int[]> rows, DateOnly periodFrom, int[] pattern)
    {
        _rows = rows;
        _periodFrom = periodFrom;
        Pattern = pattern;
    }

    /// <summary>The cyclic pattern: shift type index or <see cref="RotationTemplate.FreeDay"/> per position.</summary>
    public IReadOnlyList<int> Pattern { get; }

    /// <summary>Ids of the seat holders, in roster order.</summary>
    public IReadOnlyCollection<string> Members => _rows.Keys;

    /// <summary>
    /// Shift type the seat holder's row expects on the date, <see cref="RotationTemplate.FreeDay"/> for a free day or a
    /// hole, or null for a filler (no row) and for dates outside the period.
    /// </summary>
    public int? Expected(string agentId, DateOnly date)
    {
        if (!_rows.TryGetValue(agentId, out var row))
        {
            return null;
        }

        var day = date.DayNumber - _periodFrom.DayNumber;
        return day >= 0 && day < row.Length ? row[day] : null;
    }

    /// <summary>Builds the wheel, or returns null when it does not apply to the context.</summary>
    public static RotationWheel? Build(CoreWizardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var periodDays = context.PeriodUntil.DayNumber - context.PeriodFrom.DayNumber + 1;
        if (periodDays <= 0)
        {
            return null;
        }

        var days = Enumerable.Range(0, periodDays).Select(d => context.PeriodFrom.AddDays(d)).ToList();
        var demand = RotationWheelDemand.Count(context);
        var kinds = demand.Kinds;
        var people = context.Agents.Where(a => !a.IsStandIn && (a.GuaranteedHours > 0 || a.FullTime > 0)).ToList();
        if (kinds.Count == 0 || people.Count < MinSeats)
        {
            return null;
        }

        var workDays = WorkDaysPerStage(context, people, kinds);
        var pattern = BuildPattern(kinds, workDays);
        if (days.Count < pattern.Length)
        {
            return null;
        }

        var seats = Math.Min(people.Count, kinds.Min(k => demand.MinDaily(k, days)) * pattern.Length / workDays);
        var holders = people.Take(seats).ToList();
        var rotation = RotationContext.For(context);
        if (seats < MinSeats || holders.Any(p => kinds.Any(k => !rotation.IsAllowedOnAnyDay(p.Id, k, days))))
        {
            return null;
        }

        var placement = new RotationWheelPlacement(context, pattern, days, demand);
        var offsets = ChooseOffsets(placement, holders);
        var rows = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var holder in holders.Where(h => offsets.ContainsKey(h.Id)))
        {
            var row = placement.Row(holder, offsets[holder.Id]);
            if (!FitsContract(holder, row, demand))
            {
                return null;
            }

            rows[holder.Id] = row;
        }

        return rows.Count < MinSeats ? null : new RotationWheel(rows, context.PeriodFrom, pattern);
    }

    /// <summary>
    /// Phases of the seat holders: those with known previous days continue their own phase first, in roster order; the
    /// others share evenly spaced phases when nobody has history, else take the best free phase one after another. A
    /// holder without a collision-free phase gets no row.
    /// </summary>
    private static Dictionary<string, int> ChooseOffsets(RotationWheelPlacement start, IReadOnlyList<CoreAgent> holders)
    {
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var holder in holders.Where(h => start.HasHistory(h.Id)))
        {
            if (start.HistoryOffset(holder.Id) is { } offset)
            {
                start.Take(offset);
                offsets[holder.Id] = offset;
            }
        }

        var fresh = holders.Where(h => !start.HasHistory(h.Id)).ToList();
        var even = offsets.Count == 0 ? start.EvenOffsets(fresh.Count) : [];
        for (var i = 0; i < fresh.Count; i++)
        {
            var offset = even.Count > 0 ? even[i] : start.FreeOffset();
            if (offset is null)
            {
                continue;
            }

            start.Take(offset.Value);
            offsets[fresh[i].Id] = offset.Value;
        }

        return offsets;
    }

    private static int WorkDaysPerStage(CoreWizardContext context, List<CoreAgent> people, IReadOnlyList<int> kinds)
    {
        var maxWork = Median(people.Select(p => p.MaxWorkDays).Where(v => v > 0), DefaultWorkDays);
        var minRest = Median(people.Select(p => p.MinRestDays).Where(v => v > 0), 1);
        var workDays = Math.Min(maxWork, StageDays - minRest);
        if (kinds.Contains(ShiftTypeInference.NightIndex))
        {
            workDays = Math.Min(workDays, context.SchedulingMaxConsecutiveDays - 1);
        }

        return Math.Clamp(workDays, 1, StageDays - 1);
    }

    private static int[] BuildPattern(IReadOnlyList<int> kinds, int workDays)
    {
        var pattern = new List<int>(kinds.Count * StageDays);
        foreach (var kind in kinds)
        {
            pattern.AddRange(Enumerable.Repeat(kind, workDays));
            pattern.AddRange(Enumerable.Repeat(RotationTemplate.FreeDay, StageDays - workDays));
        }

        return pattern.ToArray();
    }

    private static bool FitsContract(CoreAgent person, int[] row, RotationWheelDemand demand)
    {
        var hours = row.Where(kind => kind != RotationTemplate.FreeDay).Sum(demand.HoursOf);
        var target = Math.Max(person.GuaranteedHours, person.FullTime);
        return hours <= target + person.MaxDailyHours && (person.MaximumHours <= 0 || hours <= person.MaximumHours);
    }

    private static int Median(IEnumerable<int> values, int fallback)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? fallback : sorted[sorted.Length / 2];
    }
}
