// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using SkiaSharp;

namespace Klacks.ScheduleOptimizer.Rendering.Grid;

/// <summary>
/// One cell of a <see cref="GridImage"/>.
/// </summary>
/// <param name="Fill">Solid fill colour; null leaves the column background (white or tinted) visible</param>
/// <param name="HatchColor">When set, the cell is drawn white with diagonal stripes of this colour instead of a solid fill</param>
/// <param name="Label">Bold centred label, typically one letter; null for none</param>
/// <param name="LightLabel">True draws a white label with black outline (for dark fills), false a black label with white outline</param>
/// <param name="ThickBorder">True draws the thick black border (e.g. locked cells), false the thin grey grid line</param>
public sealed record GridImageCell(
    SKColor? Fill,
    SKColor? HatchColor,
    string? Label,
    bool LightLabel,
    bool ThickBorder)
{
    public static GridImageCell Empty { get; } = new(null, null, null, false, false);
}
