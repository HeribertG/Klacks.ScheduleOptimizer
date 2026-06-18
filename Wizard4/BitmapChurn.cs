// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Wizard4;

/// <summary>
/// Edit-distance ratio between two same-shaped bitmaps over their working assignments. Used as the
/// candidate's ChurnRatio (how much W4 moved relative to the snapshot) — the in-engine bitmap proxy
/// for plan stability. Two cells differ when their working content (symbol + shift) differs; Free and
/// Break are not churn. Returns 0 for identical plans, up to 1 for a full rewrite.
/// </summary>
public static class BitmapChurn
{
    public static double Ratio(HarmonyBitmap before, HarmonyBitmap after)
    {
        if (before.RowCount != after.RowCount || before.DayCount != after.DayCount)
        {
            return 1.0;
        }

        var changed = 0;
        var baseline = 0;
        for (var r = 0; r < before.RowCount; r++)
        {
            for (var d = 0; d < before.DayCount; d++)
            {
                var b = before.GetCell(r, d);
                var a = after.GetCell(r, d);
                var bWorking = IsWorking(b);
                var aWorking = IsWorking(a);
                if (bWorking)
                {
                    baseline++;
                }

                if (bWorking != aWorking || (bWorking && aWorking && b.ShiftRefId != a.ShiftRefId))
                {
                    changed++;
                }
            }
        }

        return baseline == 0 ? (changed > 0 ? 1.0 : 0.0) : Math.Min(1.0, (double)changed / baseline);
    }

    private static bool IsWorking(Cell cell)
        => cell.Symbol is not CellSymbol.Free and not CellSymbol.Break;
}
