// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Reusable probe for the row memos: Fill writes a row into one shared cell array, so a cache hit allocates nothing.
/// The probe key must never be stored - a memo inserts BitmapRowKey.ToOwned() on a miss. Not thread-safe; one
/// instance per memo.
/// </summary>
internal sealed class BitmapRowKeyBuffer
{
    private Cell[] _cells = [];

    public BitmapRowKey Fill(HarmonyBitmap bitmap, int row)
    {
        if (_cells.Length != bitmap.DayCount)
        {
            _cells = new Cell[bitmap.DayCount];
        }

        for (var d = 0; d < _cells.Length; d++)
        {
            _cells[d] = bitmap.GetCell(row, d);
        }

        return new BitmapRowKey(bitmap.Rows[row], _cells);
    }
}
