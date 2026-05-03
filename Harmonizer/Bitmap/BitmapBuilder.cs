// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <summary>
/// Constructs a HarmonyBitmap from a flat list of persisted assignments. Multiple assignments
/// on the same (agent, day) collapse to a single cell whose symbol represents the dominant shift
/// of that day; locked status propagates if any contributing assignment is locked.
/// </summary>
public static class BitmapBuilder
{
    public static HarmonyBitmap Build(BitmapInput input)
    {
        if (input.EndDate < input.StartDate)
        {
            throw new ArgumentException("EndDate is before StartDate.");
        }

        var days = BuildDays(input.StartDate, input.EndDate);
        var dayIndex = BuildDayIndex(days);
        var rowIndex = BuildRowIndex(input.Agents);

        var cells = InitFreeGrid(input.Agents.Count, days.Count);
        ApplyAssignments(cells, input.Assignments, rowIndex, dayIndex);

        return new HarmonyBitmap(input.Agents, days, cells);
    }

    private static IReadOnlyList<DateOnly> BuildDays(DateOnly start, DateOnly end)
    {
        var count = end.DayNumber - start.DayNumber + 1;
        var list = new List<DateOnly>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(start.AddDays(i));
        }
        return list;
    }

    private static IReadOnlyDictionary<DateOnly, int> BuildDayIndex(IReadOnlyList<DateOnly> days)
    {
        var map = new Dictionary<DateOnly, int>(days.Count);
        for (var i = 0; i < days.Count; i++)
        {
            map[days[i]] = i;
        }
        return map;
    }

    private static IReadOnlyDictionary<string, int> BuildRowIndex(IReadOnlyList<BitmapAgent> agents)
    {
        var map = new Dictionary<string, int>(agents.Count, StringComparer.Ordinal);
        for (var i = 0; i < agents.Count; i++)
        {
            map[agents[i].Id] = i;
        }
        return map;
    }

    private static Cell[,] InitFreeGrid(int rows, int cols)
    {
        var grid = new Cell[rows, cols];
        var freeCell = Cell.Free();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                grid[r, c] = freeCell;
            }
        }
        return grid;
    }

    private static void ApplyAssignments(
        Cell[,] cells,
        IReadOnlyList<BitmapAssignment> assignments,
        IReadOnlyDictionary<string, int> rowIndex,
        IReadOnlyDictionary<DateOnly, int> dayIndex)
    {
        foreach (var a in assignments)
        {
            if (!rowIndex.TryGetValue(a.AgentId, out var row))
            {
                continue;
            }
            if (!dayIndex.TryGetValue(a.Date, out var day))
            {
                continue;
            }
            var existing = cells[row, day];
            cells[row, day] = Merge(existing, a);
        }
    }

    private static Cell Merge(Cell existing, BitmapAssignment incoming)
    {
        if (existing.Symbol == CellSymbol.Free)
        {
            return new Cell(
                incoming.Symbol,
                incoming.ShiftRefId,
                incoming.WorkIds,
                incoming.IsLocked,
                incoming.StartAt,
                incoming.EndAt,
                incoming.Hours);
        }

        var mergedWorkIds = new List<Guid>(existing.WorkIds.Count + incoming.WorkIds.Count);
        mergedWorkIds.AddRange(existing.WorkIds);
        mergedWorkIds.AddRange(incoming.WorkIds);

        var dominant = (byte)existing.Symbol >= (byte)incoming.Symbol ? existing.Symbol : incoming.Symbol;
        var locked = existing.IsLocked || incoming.IsLocked;
        var earliestStart = existing.StartAt == default ? incoming.StartAt
            : incoming.StartAt == default ? existing.StartAt
            : existing.StartAt < incoming.StartAt ? existing.StartAt : incoming.StartAt;
        var latestEnd = existing.EndAt > incoming.EndAt ? existing.EndAt : incoming.EndAt;
        var totalHours = existing.Hours + incoming.Hours;
        return new Cell(
            dominant,
            existing.ShiftRefId ?? incoming.ShiftRefId,
            mergedWorkIds,
            locked,
            earliestStart,
            latestEnd,
            totalHours);
    }
}
