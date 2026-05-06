// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Wizard3.Mutations;

/// <summary>
/// A single mutation proposed by the LLM: swap the cell contents of two (row, day) coordinates.
/// Coordinates reference the post-RowSorter bitmap.
/// </summary>
/// <param name="RowA">Zero-based row index of the first cell (agent row).</param>
/// <param name="DayA">Zero-based day index of the first cell (calendar column).</param>
/// <param name="RowB">Zero-based row index of the second cell.</param>
/// <param name="DayB">Zero-based day index of the second cell.</param>
/// <param name="Reason">Free-text justification provided by the LLM for diagnostics and UI.</param>
public sealed record PlanCellSwap(
    int RowA,
    int DayA,
    int RowB,
    int DayB,
    string Reason);
