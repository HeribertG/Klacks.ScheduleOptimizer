// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <summary>
/// Two-dimensional schedule bitmap consumed by the harmonizer. Rows are agents (sorted
/// top-down per the conductor's processing order); columns are calendar days. Cells are
/// mutable so replace operations can update the grid in-place.
/// </summary>
public sealed class HarmonyBitmap
{
    private readonly Cell[,] _cells;

    public HarmonyBitmap(IReadOnlyList<BitmapAgent> rows, IReadOnlyList<DateOnly> days, Cell[,] cells)
    {
        if (cells.GetLength(0) != rows.Count)
        {
            throw new ArgumentException("Cell row dimension does not match agents count.");
        }
        if (cells.GetLength(1) != days.Count)
        {
            throw new ArgumentException("Cell day dimension does not match days count.");
        }
        Rows = rows;
        Days = days;
        _cells = cells;
    }

    public IReadOnlyList<BitmapAgent> Rows { get; }
    public IReadOnlyList<DateOnly> Days { get; }
    public int RowCount => Rows.Count;
    public int DayCount => Days.Count;

    public Cell GetCell(int rowIndex, int dayIndex) => _cells[rowIndex, dayIndex];

    public void SetCell(int rowIndex, int dayIndex, Cell cell) => _cells[rowIndex, dayIndex] = cell;

    public IEnumerable<Cell> Row(int rowIndex)
    {
        for (var d = 0; d < DayCount; d++)
        {
            yield return _cells[rowIndex, d];
        }
    }

    public HarmonyBitmap WithRowOrder(IReadOnlyList<int> permutation)
    {
        if (permutation.Count != RowCount)
        {
            throw new ArgumentException("Permutation length does not match row count.");
        }

        var reordered = new Cell[RowCount, DayCount];
        var newRows = new BitmapAgent[RowCount];
        for (var newIdx = 0; newIdx < RowCount; newIdx++)
        {
            var oldIdx = permutation[newIdx];
            newRows[newIdx] = Rows[oldIdx];
            for (var d = 0; d < DayCount; d++)
            {
                reordered[newIdx, d] = _cells[oldIdx, d];
            }
        }
        return new HarmonyBitmap(newRows, Days, reordered);
    }
}
