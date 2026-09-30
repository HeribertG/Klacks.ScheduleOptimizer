// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.RestDays;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Weekly rest-day veto of the bitmap wizards (Wizard 2 Harmonizer, Holistic Harmonizer, Wizard 4),
/// the same rule the schedule check and Wizard 1 apply (<see cref="CalendarWeekRestDays"/>). A row whose
/// cells change must not turn a rest day of a Monday-to-Sunday week into a work day when that week then
/// keeps fewer than the agent's MinRestDays. Every cell is re-anchored onto its bitmap day at its time
/// of day, because a cross-day swap moves a cell without rewriting its times; a work cell without times
/// counts as the whole day, like everywhere else. Only the days that can influence the judged weeks
/// are read.
/// </summary>
/// <param name="boundaryAssignments">Works and breaks on the days around the bitmap, for weeks crossing its edges</param>
public sealed class BitmapWeeklyRestDayGuard
{
    private readonly Dictionary<string, List<BitmapAssignment>> _boundaryByAgent = new(StringComparer.Ordinal);

    public BitmapWeeklyRestDayGuard(IReadOnlyList<BitmapAssignment>? boundaryAssignments)
    {
        if (boundaryAssignments is null)
        {
            return;
        }

        foreach (var assignment in boundaryAssignments)
        {
            if (!_boundaryByAgent.TryGetValue(assignment.AgentId, out var list))
            {
                list = [];
                _boundaryByAgent[assignment.AgentId] = list;
            }

            list.Add(assignment);
        }
    }

    /// <summary>
    /// Null when the row may receive <paramref name="incomingCell"/> on <paramref name="dayIndex"/>,
    /// otherwise a short reason naming the violated weekly minimum.
    /// </summary>
    /// <param name="bitmap">Current plan</param>
    /// <param name="row">Receiving row</param>
    /// <param name="agent">Agent of the receiving row</param>
    /// <param name="dayIndex">Bitmap day the cell lands on</param>
    /// <param name="incomingCell">Cell the row receives</param>
    public string? Diagnose(HarmonyBitmap bitmap, int row, BitmapAgent agent, int dayIndex, Cell incomingCell)
        => Diagnose(bitmap, row, agent, [(dayIndex, incomingCell)]);

    /// <summary>
    /// Null when the row may take all <paramref name="replacements"/> at once (e.g. a cross-day swap of
    /// two cells within the same row), otherwise a short reason naming the violated weekly minimum.
    /// </summary>
    /// <param name="bitmap">Current plan</param>
    /// <param name="row">Row whose cells change</param>
    /// <param name="agent">Agent of the row</param>
    /// <param name="replacements">Bitmap day and new cell of every changed day of the row</param>
    public string? Diagnose(HarmonyBitmap bitmap, int row, BitmapAgent agent, IReadOnlyList<(int Day, Cell Cell)> replacements)
    {
        if (agent.MinRestDays <= 0 || !replacements.Any(replacement => IsWork(replacement.Cell.Symbol)))
        {
            return null;
        }

        var firstAffected = DateOnly.MaxValue;
        var lastAffected = DateOnly.MinValue;
        foreach (var (day, cell) in replacements)
        {
            if (!IsWork(cell.Symbol))
            {
                continue;
            }

            var incoming = Anchor(cell.StartAt, cell.EndAt, bitmap.Days[day]);
            var first = CalendarWeekRestDays.FirstAffectedDay(incoming, agent.MinRestDays);
            var last = CalendarWeekRestDays.LastTouchedDay(incoming);
            firstAffected = first < firstAffected ? first : firstAffected;
            lastAffected = last > lastAffected ? last : lastAffected;
        }

        var firstRelevant = firstAffected.AddDays(-CalendarWeekRestDays.DaysPerWeek);
        var lastRelevant = CalendarWeekRestDays.LastRelevantDay(lastAffected, agent.MinRestDays);
        var before = new List<WorkInterval>();
        var after = new List<WorkInterval>();
        for (var day = 0; day < bitmap.DayCount; day++)
        {
            var date = bitmap.Days[day];
            if (date < firstRelevant || date > lastRelevant)
            {
                continue;
            }

            var existing = bitmap.GetCell(row, day);
            if (IsWork(existing.Symbol))
            {
                before.Add(Anchor(existing.StartAt, existing.EndAt, date));
            }

            var current = ReplacementFor(replacements, day) ?? existing;
            if (IsWork(current.Symbol))
            {
                after.Add(Anchor(current.StartAt, current.EndAt, date));
            }
        }

        if (_boundaryByAgent.TryGetValue(agent.Id, out var boundary))
        {
            foreach (var assignment in boundary)
            {
                if (IsWork(assignment.Symbol) && assignment.Date >= firstRelevant && assignment.Date <= lastRelevant)
                {
                    var interval = Anchor(assignment.StartAt, assignment.EndAt, assignment.Date);
                    before.Add(interval);
                    after.Add(interval);
                }
            }
        }

        var breaks = CalendarWeekRestDays.ChangeBreaksMinimum(before, after, firstAffected, lastAffected, agent.MinRestDays);
        return breaks
            ? $"MinRestDays violated: a calendar week between {firstAffected:yyyy-MM-dd} and {lastAffected:yyyy-MM-dd} would keep fewer than {agent.MinRestDays} rest day(s)"
            : null;
    }

    private static Cell? ReplacementFor(IReadOnlyList<(int Day, Cell Cell)> replacements, int day)
    {
        foreach (var replacement in replacements)
        {
            if (replacement.Day == day)
            {
                return replacement.Cell;
            }
        }

        return null;
    }

    private static WorkInterval Anchor(DateTime startAt, DateTime endAt, DateOnly day)
    {
        if (startAt == default || endAt <= startAt)
        {
            return CalendarWeekRestDays.IntervalOrWholeDay(default, default, day);
        }

        var start = day.ToDateTime(TimeOnly.FromDateTime(startAt));
        return new WorkInterval(start, start + (endAt - startAt));
    }

    private static bool IsWork(CellSymbol symbol) => symbol != CellSymbol.Free && symbol != CellSymbol.Break;
}
