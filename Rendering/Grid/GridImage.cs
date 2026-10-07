// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Rendering.Grid;

/// <summary>
/// Domain-free description of a labelled grid image (rows x columns of coloured, lettered cells). Any feature can
/// map its data into this model and render it with <see cref="GridImageRenderer"/>; the schedule bitmap is one such
/// adapter.
/// </summary>
/// <param name="RowLabels">Text of the left header per row</param>
/// <param name="Columns">Header and tint per column</param>
/// <param name="Cells">Cells indexed [row, column]; dimensions must match the row and column counts</param>
/// <param name="Markers">Cells to highlight with a numbered badge; empty for none</param>
public sealed record GridImage(
    IReadOnlyList<string> RowLabels,
    IReadOnlyList<GridImageColumn> Columns,
    GridImageCell[,] Cells,
    IReadOnlyList<GridImageMarker> Markers);
