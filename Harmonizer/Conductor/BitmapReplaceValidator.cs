// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Bitmap-local validator. Rejects out-of-range moves, self-swaps, and moves that touch a
/// locked cell. Does not enforce domain-level constraints (coverage, qualification, hours);
/// those are layered on top in Phase 6 via a tokenising validator.
/// </summary>
public sealed class BitmapReplaceValidator : IReplaceValidator
{
    public bool IsValid(HarmonyBitmap bitmap, ReplaceMove move)
    {
        if (move.RowA == move.RowB)
        {
            return false;
        }
        if (move.RowA < 0 || move.RowA >= bitmap.RowCount)
        {
            return false;
        }
        if (move.RowB < 0 || move.RowB >= bitmap.RowCount)
        {
            return false;
        }
        if (move.Day < 0 || move.Day >= bitmap.DayCount)
        {
            return false;
        }

        var cellA = bitmap.GetCell(move.RowA, move.Day);
        var cellB = bitmap.GetCell(move.RowB, move.Day);
        if (cellA.IsLocked || cellB.IsLocked)
        {
            return false;
        }

        return true;
    }
}
