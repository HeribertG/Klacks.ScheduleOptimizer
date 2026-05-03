// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Seeds population diversity by applying a small number of validator-approved random
/// same-day swaps. Acceptance is stochastic (no harmony-delta check) to give the genetic
/// algorithm room to explore basins the deterministic conductor would never visit.
/// </summary>
public sealed class StochasticBitmapMutation
{
    private readonly IReplaceValidator _validator;

    public StochasticBitmapMutation(IReplaceValidator validator)
    {
        _validator = validator;
    }

    public int Apply(HarmonyBitmap bitmap, int desiredSwaps, Random random)
    {
        if (bitmap.RowCount < 2 || bitmap.DayCount == 0)
        {
            return 0;
        }

        var maxAttempts = desiredSwaps * 8;
        var applied = 0;
        for (var attempt = 0; attempt < maxAttempts && applied < desiredSwaps; attempt++)
        {
            var rowA = random.Next(bitmap.RowCount);
            var rowB = random.Next(bitmap.RowCount);
            var day = random.Next(bitmap.DayCount);
            var move = new ReplaceMove(rowA, rowB, day);
            if (!_validator.IsValid(bitmap, move))
            {
                continue;
            }

            var cellA = bitmap.GetCell(move.RowA, move.Day);
            var cellB = bitmap.GetCell(move.RowB, move.Day);
            bitmap.SetCell(move.RowA, move.Day, cellB);
            bitmap.SetCell(move.RowB, move.Day, cellA);
            applied++;
        }
        return applied;
    }
}
