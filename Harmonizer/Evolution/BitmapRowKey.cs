// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Memo key of one bitmap row: the agent reference plus the sequence of cell references. Cells are immutable
/// records that swaps move by reference, so equal keys mean an identical row; compared by reference only (record
/// value equality would be both slower and wrong for distinct works with equal fields).
/// </summary>
/// <param name="Agent">Agent of the row</param>
/// <param name="Cells">Cells of the row in day order</param>
internal readonly record struct BitmapRowKey(BitmapAgent Agent, Cell[] Cells)
{
    public static IEqualityComparer<BitmapRowKey> Comparer { get; } = new ReferenceComparer();

    public static BitmapRowKey Of(HarmonyBitmap bitmap, int row)
    {
        var cells = new Cell[bitmap.DayCount];
        for (var d = 0; d < cells.Length; d++)
        {
            cells[d] = bitmap.GetCell(row, d);
        }

        return new BitmapRowKey(bitmap.Rows[row], cells);
    }

    private sealed class ReferenceComparer : IEqualityComparer<BitmapRowKey>
    {
        public bool Equals(BitmapRowKey x, BitmapRowKey y)
        {
            if (!ReferenceEquals(x.Agent, y.Agent) || x.Cells.Length != y.Cells.Length)
            {
                return false;
            }

            for (var i = 0; i < x.Cells.Length; i++)
            {
                if (!ReferenceEquals(x.Cells[i], y.Cells[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(BitmapRowKey key)
        {
            var hash = new HashCode();
            hash.Add(RuntimeHelpers.GetHashCode(key.Agent));
            foreach (var cell in key.Cells)
            {
                hash.Add(RuntimeHelpers.GetHashCode(cell));
            }

            return hash.ToHashCode();
        }
    }
}
