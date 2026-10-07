// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Rendering.Grid;

/// <summary>
/// Layout of a <see cref="GridImageRenderer"/> image. All sizes are pixels at <see cref="Scale"/> 1 and are multiplied
/// by <see cref="Scale"/>, so a larger scale keeps the proportions but gives a vision model more pixels per cell.
/// </summary>
/// <param name="CellSize">Width and height of one cell</param>
/// <param name="HeaderLeft">Width of the row header</param>
/// <param name="HeaderTop">Height of the column header</param>
/// <param name="ThickBorderWidth">Stroke width of the thick cell border</param>
/// <param name="Scale">Factor applied to every size, font and stroke</param>
public sealed record GridImageRenderOptions(
    int CellSize = 24,
    int HeaderLeft = 32,
    int HeaderTop = 32,
    int ThickBorderWidth = 2,
    float Scale = 1f);
