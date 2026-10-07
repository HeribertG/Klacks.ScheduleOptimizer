// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Rendering.Grid;
using SkiaSharp;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Bitmap;

/// <summary>
/// Renders a <see cref="HarmonyBitmap"/> as a PNG image for vision-capable LLMs in Holistic Harmonizer. Schedule
/// adapter of the generic <see cref="GridImageRenderer"/>: cells are coloured and lettered by <see cref="CellSymbol"/>,
/// locked cells get a thick black border, Break cells a red/white diagonal hatch, weekend columns a light tint,
/// rows the agent initials and columns day number plus weekday letter.
/// </summary>
/// <param name="options">Layout and color options. <see cref="HarmonyBitmapPngRenderOptions.Default"/> covers most callers.</param>
public sealed class HarmonyBitmapPngRenderer
{
    private static readonly SKColor EarlyFillColor = new(0xFF, 0xD7, 0x00);
    private static readonly SKColor LateFillColor = new(0xFF, 0x8C, 0x00);
    private static readonly SKColor NightFillColor = new(0x1E, 0x3A, 0x8A);
    private static readonly SKColor OtherFillColor = new(0x4B, 0x55, 0x63);
    private static readonly SKColor BreakStripeColor = new(0xDC, 0x26, 0x26);

    private readonly HarmonyBitmapPngRenderOptions _options;
    private readonly GridImageRenderer _gridRenderer;

    public HarmonyBitmapPngRenderer()
        : this(HarmonyBitmapPngRenderOptions.Default)
    {
    }

    public HarmonyBitmapPngRenderer(HarmonyBitmapPngRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _gridRenderer = new GridImageRenderer(new GridImageRenderOptions(
            options.CellSize, options.HeaderLeft, options.HeaderTop, options.LockedBorderThickness, options.Scale));
    }

    public byte[] Render(HarmonyBitmap bitmap) => Render(bitmap, []);

    public byte[] Render(HarmonyBitmap bitmap, IReadOnlyList<GridImageMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(markers);
        return _gridRenderer.Render(ToGridImage(bitmap, markers));
    }

    private GridImage ToGridImage(HarmonyBitmap bitmap, IReadOnlyList<GridImageMarker> markers)
    {
        var rowLabels = bitmap.Rows.Select(r => BuildInitials(r.DisplayName)).ToList();
        var columns = bitmap.Days
            .Select(d => new GridImageColumn(
                d.Day.ToString(CultureInfo.InvariantCulture),
                WeekdayLetter(d.DayOfWeek),
                _options.TintWeekends && IsWeekend(d.DayOfWeek)))
            .ToList();

        var cells = new GridImageCell[bitmap.RowCount, bitmap.DayCount];
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            for (var d = 0; d < bitmap.DayCount; d++)
            {
                cells[r, d] = ToGridCell(bitmap.GetCell(r, d));
            }
        }

        return new GridImage(rowLabels, columns, cells, markers);
    }

    private static GridImageCell ToGridCell(Cell cell) => cell.Symbol switch
    {
        CellSymbol.Early => new GridImageCell(EarlyFillColor, null, "E", false, cell.IsLocked),
        CellSymbol.Late => new GridImageCell(LateFillColor, null, "L", false, cell.IsLocked),
        CellSymbol.Night => new GridImageCell(NightFillColor, null, "N", true, cell.IsLocked),
        CellSymbol.Other => new GridImageCell(OtherFillColor, null, "O", true, cell.IsLocked),
        CellSymbol.Break => new GridImageCell(null, BreakStripeColor, "B", false, cell.IsLocked),
        _ => new GridImageCell(null, null, null, false, cell.IsLocked),
    };

    private static bool IsWeekend(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday;

    private static string BuildInitials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }
        var parts = displayName.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }
        if (parts.Length == 1)
        {
            return parts[0].Length >= 2
                ? parts[0][..2].ToUpperInvariant()
                : parts[0].ToUpperInvariant();
        }
        Span<char> initials = stackalloc char[2];
        initials[0] = char.ToUpperInvariant(parts[0][0]);
        initials[1] = char.ToUpperInvariant(parts[^1][0]);
        return new string(initials);
    }

    private static string WeekdayLetter(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "M",
        DayOfWeek.Tuesday => "T",
        DayOfWeek.Wednesday => "W",
        DayOfWeek.Thursday => "T",
        DayOfWeek.Friday => "F",
        DayOfWeek.Saturday => "S",
        DayOfWeek.Sunday => "S",
        _ => "?",
    };
}
