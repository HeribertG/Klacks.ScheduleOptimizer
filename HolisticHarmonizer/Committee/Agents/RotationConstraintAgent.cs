// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee.Agents;

/// <summary>
/// Vetoes swaps that create three identical work-shift symbols in a row for either agent — a
/// monotonous block that hurts shift rotation diversity. Approves swaps that break an existing
/// 3-in-a-row pattern. Abstains otherwise. Free and Break cells are ignored: the rotation
/// concept only applies to actively worked shift symbols.
/// </summary>
public sealed class RotationConstraintAgent : IConstraintAgent
{
    public string Name => "Rotation";

    public ConstraintAgentVerdict Evaluate(HarmonyBitmap before, PlanCellSwap swap)
    {
        var rowABeforeMonotone = HasMonotoneBlock(before, swap.RowA, swap.DayA, before.GetCell(swap.RowA, swap.DayA).Symbol);
        var rowBBeforeMonotone = HasMonotoneBlock(before, swap.RowB, swap.DayB, before.GetCell(swap.RowB, swap.DayB).Symbol);

        var symbolAfterRowA = before.GetCell(swap.RowB, swap.DayB).Symbol;
        var symbolAfterRowB = before.GetCell(swap.RowA, swap.DayA).Symbol;
        var rowAAfterMonotone = HasMonotoneBlock(before, swap.RowA, swap.DayA, symbolAfterRowA);
        var rowBAfterMonotone = HasMonotoneBlock(before, swap.RowB, swap.DayB, symbolAfterRowB);

        var introducesRowA = rowAAfterMonotone && !rowABeforeMonotone;
        var introducesRowB = rowBAfterMonotone && !rowBBeforeMonotone;
        var removesRowA = !rowAAfterMonotone && rowABeforeMonotone;
        var removesRowB = !rowBAfterMonotone && rowBBeforeMonotone;

        if (introducesRowA || introducesRowB)
        {
            var who = introducesRowA && introducesRowB ? "both rows" : introducesRowA ? before.Rows[swap.RowA].DisplayName : before.Rows[swap.RowB].DisplayName;
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Veto, $"creates 3+ identical shifts in a row for {who}");
        }

        if (removesRowA || removesRowB)
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Approve, "breaks an existing monotone shift block");
        }

        return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Abstain, "swap does not affect rotation diversity");
    }

    private static bool HasMonotoneBlock(HarmonyBitmap bitmap, int rowIndex, int dayIndex, CellSymbol symbol)
    {
        if (!IsWork(symbol)) return false;
        var run = 1;
        for (var d = dayIndex - 1; d >= 0 && bitmap.GetCell(rowIndex, d).Symbol == symbol; d--)
        {
            run++;
        }
        for (var d = dayIndex + 1; d < bitmap.DayCount && bitmap.GetCell(rowIndex, d).Symbol == symbol; d++)
        {
            run++;
        }
        return run >= 3;
    }

    private static bool IsWork(CellSymbol symbol)
        => symbol == CellSymbol.Early || symbol == CellSymbol.Late || symbol == CellSymbol.Night || symbol == CellSymbol.Other;
}
