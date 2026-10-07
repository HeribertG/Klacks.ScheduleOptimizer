// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Rendering.Grid;

/// <summary>
/// Highlights one cell of a <see cref="GridImage"/> with a coloured ring and a numbered badge, so a vision model can
/// refer to the cell by its label instead of having to locate it through row and column headers.
/// </summary>
/// <param name="Row">Zero-based row index</param>
/// <param name="Column">Zero-based column index</param>
/// <param name="Label">Short badge text, e.g. "1" or "C07"</param>
public sealed record GridImageMarker(int Row, int Column, string Label);
