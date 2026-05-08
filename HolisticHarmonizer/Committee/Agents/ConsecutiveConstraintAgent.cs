// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee.Agents;

/// <summary>
/// Vetoes swaps that push either row to its <c>MaxConsecutiveDays</c> contractual cap. The
/// hard-validator already rejects runs that exceed the cap; this agent goes one step further
/// and discourages reaching the cap exactly, so the LLM aims for shorter, sustainable streaks.
/// Approves when the swap shortens an existing long run (length above <c>MaxConsecutiveDays - 1</c>).
/// Abstains when neither row has a max-consec contract value (0 = unconstrained) or when the
/// swap is outside any work run.
/// </summary>
public sealed class ConsecutiveConstraintAgent : IConstraintAgent
{
    public string Name => "Consecutive";

    public ConstraintAgentVerdict Evaluate(HarmonyBitmap before, PlanCellSwap swap)
    {
        var rowAMax = before.Rows[swap.RowA].MaxConsecutiveDays;
        var rowBMax = before.Rows[swap.RowB].MaxConsecutiveDays;
        if (rowAMax <= 0 && rowBMax <= 0)
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Abstain, "no max-consecutive cap defined for either row");
        }

        var symbolAfterRowA = before.GetCell(swap.RowB, swap.DayB).Symbol;
        var symbolAfterRowB = before.GetCell(swap.RowA, swap.DayA).Symbol;

        var rowABeforeRun = RunLength(before, swap.RowA, swap.DayA, before.GetCell(swap.RowA, swap.DayA).Symbol);
        var rowAAfterRun = RunLength(before, swap.RowA, swap.DayA, symbolAfterRowA);
        var rowBBeforeRun = RunLength(before, swap.RowB, swap.DayB, before.GetCell(swap.RowB, swap.DayB).Symbol);
        var rowBAfterRun = RunLength(before, swap.RowB, swap.DayB, symbolAfterRowB);

        var rowAHitsCap = rowAMax > 0 && rowAAfterRun >= rowAMax && rowABeforeRun < rowAMax;
        var rowBHitsCap = rowBMax > 0 && rowBAfterRun >= rowBMax && rowBBeforeRun < rowBMax;
        if (rowAHitsCap || rowBHitsCap)
        {
            var who = rowAHitsCap && rowBHitsCap
                ? "both rows"
                : rowAHitsCap ? before.Rows[swap.RowA].DisplayName : before.Rows[swap.RowB].DisplayName;
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Veto, string.Format(
                CultureInfo.InvariantCulture,
                "swap pushes {0} to its max-consecutive cap (run after = {1})",
                who,
                rowAHitsCap ? rowAAfterRun : rowBAfterRun));
        }

        var rowAShortens = rowAMax > 0 && rowABeforeRun >= rowAMax - 1 && rowAAfterRun < rowABeforeRun;
        var rowBShortens = rowBMax > 0 && rowBBeforeRun >= rowBMax - 1 && rowBAfterRun < rowBBeforeRun;
        if (rowAShortens || rowBShortens)
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Approve, "shortens an at-cap consecutive run");
        }

        return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Abstain, "swap does not stress consecutive caps");
    }

    private static int RunLength(HarmonyBitmap bitmap, int rowIndex, int dayIndex, CellSymbol symbolOnDay)
    {
        if (!IsWork(symbolOnDay)) return 0;
        var run = 1;
        for (var d = dayIndex - 1; d >= 0; d--)
        {
            if (!IsWork(bitmap.GetCell(rowIndex, d).Symbol)) break;
            run++;
        }
        for (var d = dayIndex + 1; d < bitmap.DayCount; d++)
        {
            if (!IsWork(bitmap.GetCell(rowIndex, d).Symbol)) break;
            run++;
        }
        return run;
    }

    private static bool IsWork(CellSymbol symbol)
        => symbol == CellSymbol.Early || symbol == CellSymbol.Late || symbol == CellSymbol.Night || symbol == CellSymbol.Other;
}
