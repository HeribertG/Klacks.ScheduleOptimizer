// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Produces deep copies of a HarmonyBitmap. Cells are immutable records so the cell array
/// is the only mutable state that needs cloning; agent and day collections are reused.
/// </summary>
public static class BitmapCloner
{
    public static HarmonyBitmap Clone(HarmonyBitmap source)
    {
        var copy = new Cell[source.RowCount, source.DayCount];
        for (var r = 0; r < source.RowCount; r++)
        {
            for (var d = 0; d < source.DayCount; d++)
            {
                copy[r, d] = source.GetCell(r, d);
            }
        }
        return new HarmonyBitmap(source.Rows, source.Days, copy);
    }
}
