// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee.Agents;

/// <summary>
/// Vetoes swaps that introduce a "rough transition" pattern: a Night cell directly followed
/// by an Early/Late/Other day-shift, or a day-shift placed directly after a Night cell. The
/// hard-validator only enforces the contractual <c>MinPauseHours</c>; this agent acts as a
/// soft preference layer that discourages back-to-back day-shift-after-night even when the
/// minimum rest is technically met.
/// </summary>
public sealed class PauseConstraintAgent : IConstraintAgent
{
    public string Name => "Pause";

    public ConstraintAgentVerdict Evaluate(HarmonyBitmap before, PlanCellSwap swap)
    {
        var rowAFlagBefore = HasRoughTransition(before, swap.RowA, swap.DayA, before.GetCell(swap.RowA, swap.DayA).Symbol);
        var rowBFlagBefore = HasRoughTransition(before, swap.RowB, swap.DayB, before.GetCell(swap.RowB, swap.DayB).Symbol);

        var symbolAfterRowA = before.GetCell(swap.RowB, swap.DayB).Symbol;
        var symbolAfterRowB = before.GetCell(swap.RowA, swap.DayA).Symbol;
        var rowAFlagAfter = HasRoughTransition(before, swap.RowA, swap.DayA, symbolAfterRowA);
        var rowBFlagAfter = HasRoughTransition(before, swap.RowB, swap.DayB, symbolAfterRowB);

        var introducesRowA = rowAFlagAfter && !rowAFlagBefore;
        var introducesRowB = rowBFlagAfter && !rowBFlagBefore;
        var removesRowA = !rowAFlagAfter && rowAFlagBefore;
        var removesRowB = !rowBFlagAfter && rowBFlagBefore;

        if (introducesRowA || introducesRowB)
        {
            var who = introducesRowA && introducesRowB ? "both rows" : introducesRowA ? before.Rows[swap.RowA].DisplayName : before.Rows[swap.RowB].DisplayName;
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Veto, $"introduces a night-to-day-shift transition for {who}");
        }

        if (removesRowA || removesRowB)
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Approve, "removes a night-to-day-shift transition");
        }

        return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Abstain, "no transition pattern affected");
    }

    private static bool HasRoughTransition(HarmonyBitmap bitmap, int rowIndex, int dayIndex, CellSymbol symbol)
    {
        if (dayIndex > 0)
        {
            var prev = bitmap.GetCell(rowIndex, dayIndex - 1).Symbol;
            if (IsNight(prev) && IsDayShift(symbol)) return true;
        }
        if (dayIndex < bitmap.DayCount - 1)
        {
            var next = bitmap.GetCell(rowIndex, dayIndex + 1).Symbol;
            if (IsNight(symbol) && IsDayShift(next)) return true;
        }
        return false;
    }

    private static bool IsNight(CellSymbol symbol) => symbol == CellSymbol.Night;
    private static bool IsDayShift(CellSymbol symbol) => symbol == CellSymbol.Early || symbol == CellSymbol.Late || symbol == CellSymbol.Other;
}
