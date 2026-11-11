// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Loop;

/// <summary>
/// Canonical key for a swap coordinate pair (rowA, dayA, rowB, dayB), normalized so the
/// pair (a, b) and (b, a) hash to the same key. Day must match (same-day swap invariant)
/// so it is stored once. Used by the tabu list, the deterministic optimizer and the
/// candidate-pool exclusion to recognise swaps that must not be offered again.
/// </summary>
/// <param name="RowSmaller">The smaller of the two row indices.</param>
/// <param name="RowLarger">The larger of the two row indices.</param>
/// <param name="Day">Day index of the swap (DayA of the source swap).</param>
public readonly record struct ForbiddenSwapKey(int RowSmaller, int RowLarger, int Day)
{
    public static ForbiddenSwapKey From(PlanCellSwap swap)
    {
        ArgumentNullException.ThrowIfNull(swap);
        var (smaller, larger) = swap.RowA <= swap.RowB ? (swap.RowA, swap.RowB) : (swap.RowB, swap.RowA);
        return new ForbiddenSwapKey(smaller, larger, swap.DayA);
    }
}
