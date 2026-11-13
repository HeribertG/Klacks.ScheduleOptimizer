// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Chooses the phases (offsets) of the seat holders of a <see cref="RotationWheel"/>. A phase collides when it would
/// put more rows on a kind than the day demands. A person with worked days in the known previous days
/// (<see cref="RotationWheel.KnownHistoryDays"/> before the first plannable day) gets the collision-free phase that best
/// reproduces them, recent days weighing more. Without any history the seats get evenly spaced phases chosen together;
/// otherwise a person without history gets the collision-free phase with the most work days. The row of a placed person has holes on absence days and on days its kind is closed to the
/// person, and restarts after <see cref="ShiftRotation.LongPauseFreeDays"/> or more consecutive absence days with a
/// stage of the ideal re-entry kind.
/// </summary>
/// <param name="context">Wizard context</param>
/// <param name="pattern">Cyclic wheel pattern</param>
/// <param name="days">Days of the period</param>
/// <param name="demand">Seats per day and kind</param>
public sealed class RotationWheelPlacement(
    CoreWizardContext context, int[] pattern, IReadOnlyList<DateOnly> days, RotationWheelDemand demand)
{
    private readonly int[,] _staffed = new int[days.Count, ShiftRotation.KindCount];
    private readonly List<int> _taken = [];
    private readonly DateOnly _anchor = CarryInContinuation.FirstPlannableDay(context);
    private readonly IReadOnlyDictionary<string, Dictionary<DateOnly, int>> _history = CarryInContinuation.FixedShiftKinds(context);
    private readonly RotationContext _rotation = RotationContext.For(context);

    /// <summary>
    /// The phase that best reproduces the person's known previous days among the collision-free phases, or null when
    /// the person has no worked day in the known window or no phase is collision-free.
    /// </summary>
    public int? HistoryOffset(string agentId)
    {
        var known = KnownWorkedDays(agentId);
        if (known.Count == 0)
        {
            return null;
        }

        var free = FreeOffsets();
        return free.Count == 0
            ? null
            : free.OrderByDescending(offset => HistoryScore(offset, known)).ThenByDescending(WorkDays).ThenBy(offset => offset).First();
    }

    /// <summary>True when the person worked on at least one of the known previous days.</summary>
    public bool HasHistory(string agentId) => KnownWorkedDays(agentId).Count > 0;

    /// <summary>
    /// Phases for <paramref name="count"/> seats chosen together: evenly spaced (step cycle length / seats) from every
    /// base, the collision-free set with the most work days in the period wins; the phases are returned most work days
    /// first, so the upper ranks get the most hours. Empty when no evenly spaced set is collision-free.
    /// </summary>
    public IReadOnlyList<int> EvenOffsets(int count)
    {
        IReadOnlyList<int> best = [];
        var bestWork = -1;
        for (var start = 0; start < pattern.Length; start++)
        {
            var set = Enumerable.Range(0, count).Select(i => Mod(start + (i * pattern.Length / count))).ToList();
            if (set.Distinct().Count() != count || !IsCollisionFree(set))
            {
                continue;
            }

            var work = set.Sum(WorkDays);
            if (work > bestWork)
            {
                best = set.OrderByDescending(WorkDays).ThenBy(offset => offset).ToList();
                bestWork = work;
            }
        }

        return best;
    }

    /// <summary>The collision-free phase with the most work days, then the one farthest from the phases taken; null when none is free.</summary>
    public int? FreeOffset()
    {
        var free = FreeOffsets();
        return free.Count == 0
            ? null
            : free.OrderByDescending(WorkDays).ThenByDescending(MinDistance).ThenBy(offset => offset).First();
    }

    /// <summary>Books the phase's pattern cells as staffed.</summary>
    public void Take(int offset)
    {
        _taken.Add(offset);
        for (var day = 0; day < days.Count; day++)
        {
            var kind = At(offset, day);
            if (kind != RotationTemplate.FreeDay)
            {
                _staffed[day, kind]++;
            }
        }
    }

    /// <summary>The person's expected kind per period day, with holes and re-entry applied.</summary>
    public int[] Row(CoreAgent person, int offset)
    {
        var absent = AbsenceDays(person.Id);
        var row = new int[days.Count];
        var absentRun = AbsentRunBefore(absent);
        for (var day = 0; day < days.Count; day++)
        {
            var date = days[day];
            if (absent.Contains(date))
            {
                absentRun++;
                row[day] = RotationTemplate.FreeDay;
                continue;
            }

            if (absentRun >= ShiftRotation.LongPauseFreeDays)
            {
                offset = ReentryOffset(person.Id, date, day) ?? offset;
            }

            absentRun = 0;
            var kind = At(offset, day);
            row[day] = kind != RotationTemplate.FreeDay && _rotation.IsClosed(person.Id, kind, date) ? RotationTemplate.FreeDay : kind;
        }

        return row;
    }

    private int? ReentryOffset(string agentId, DateOnly date, int day)
    {
        var kind = ShiftRotation.IdealSuccessor(
            ShiftRotation.EarlyKindIndex, longPause: true, k => demand.Kinds.Contains(k) && !_rotation.IsClosed(agentId, k, date));
        if (kind is null)
        {
            return null;
        }

        var stageStart = demand.Kinds.ToList().IndexOf(kind.Value) * RotationWheel.StageDays;
        return Mod(day - stageStart);
    }

    private int At(int offset, int day) => pattern[Mod(day - offset)];

    private int Mod(int value) => ((value % pattern.Length) + pattern.Length) % pattern.Length;

    private int Collisions(int offset)
    {
        var collisions = 0;
        for (var day = 0; day < days.Count; day++)
        {
            var kind = At(offset, day);
            if (kind != RotationTemplate.FreeDay && _staffed[day, kind] + 1 > demand.Seats(days[day], kind))
            {
                collisions++;
            }
        }

        return collisions;
    }

    private List<int> FreeOffsets() => Enumerable.Range(0, pattern.Length).Where(offset => Collisions(offset) == 0).ToList();

    private bool IsCollisionFree(IReadOnlyList<int> offsets)
    {
        for (var day = 0; day < days.Count; day++)
        {
            foreach (var group in offsets.Select(o => At(o, day)).Where(k => k != RotationTemplate.FreeDay).GroupBy(k => k))
            {
                if (_staffed[day, group.Key] + group.Count() > demand.Seats(days[day], group.Key))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private int WorkDays(int offset) => Enumerable.Range(0, days.Count).Count(day => At(offset, day) != RotationTemplate.FreeDay);

    private int MinDistance(int offset)
        => _taken.Count == 0
            ? 0
            : _taken.Min(t => Math.Min(Mod(offset - t), Mod(t - offset)));

    /// <summary>Known previous days of the person: worked kind, or free when nothing was worked; unknown days are absent.</summary>
    private Dictionary<DateOnly, int> KnownWorkedDays(string agentId)
    {
        if (!_history.TryGetValue(agentId, out var kinds))
        {
            return [];
        }

        var window = Enumerable.Range(1, RotationWheel.KnownHistoryDays).Select(d => _anchor.AddDays(-d)).ToList();
        if (!window.Any(kinds.ContainsKey))
        {
            return [];
        }

        return window.ToDictionary(d => d, d => kinds.TryGetValue(d, out var kind) ? kind : RotationTemplate.FreeDay);
    }

    private double HistoryScore(int offset, Dictionary<DateOnly, int> known)
    {
        var score = 0d;
        foreach (var (date, actual) in known)
        {
            var day = date.DayNumber - context.PeriodFrom.DayNumber;
            if (At(offset, day) == actual)
            {
                score += RotationWheel.KnownHistoryDays - (_anchor.DayNumber - date.DayNumber) + 1;
            }
        }

        return score;
    }

    private HashSet<DateOnly> AbsenceDays(string agentId)
    {
        var result = new HashSet<DateOnly>();
        foreach (var blocker in context.BreakBlockers.Concat(context.BoundaryBreakBlockers).Where(b => b.AgentId == agentId))
        {
            for (var date = blocker.FromInclusive; date <= blocker.UntilInclusive; date = date.AddDays(1))
            {
                result.Add(date);
            }
        }

        return result;
    }

    private int AbsentRunBefore(HashSet<DateOnly> absent)
    {
        var run = 0;
        for (var date = context.PeriodFrom.AddDays(-1); run < RotationWheel.KnownHistoryDays && absent.Contains(date); date = date.AddDays(-1))
        {
            run++;
        }

        return run;
    }
}
