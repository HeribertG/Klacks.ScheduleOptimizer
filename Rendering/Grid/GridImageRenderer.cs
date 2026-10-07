// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using SkiaSharp;

namespace Klacks.ScheduleOptimizer.Rendering.Grid;

/// <summary>
/// Renders a <see cref="GridImage"/> as PNG with SkiaSharp: tinted columns, filled or hatched cells with outlined
/// bold labels, thin or thick cell borders, a column header with two lines, a row header and optional numbered
/// cell markers. Knows nothing about schedules; callers map their data into the model.
/// </summary>
/// <param name="options">Layout and scale; <see cref="GridImageRenderOptions"/> defaults match the Wizard 3 plan image</param>
public sealed class GridImageRenderer
{
    private const int PngQuality = 100;
    private const float HeaderFontSize = 11f;
    private const float RowLabelFontSize = 10f;
    private const float CellLabelFontSize = 18f;
    private const float ThinBorderWidth = 1f;
    private const float HatchStripeSpacing = 4f;
    private const float HatchStrokeWidth = 2f;
    private const float LabelOutlineWidth = 3f;
    private const float ColumnTopBaselineOffset = 1f;
    private const float ColumnBottomBaselineOffset = 2f;
    private const float RowLabelBaselineOffset = 1f;
    private const float CellLabelBaselineOffset = 2f;
    private const float MarkerRingWidth = 3f;
    private const float MarkerBadgeRadius = 7f;
    private const float MarkerFontSize = 10f;
    private const float MarkerBaselineOffset = 3.5f;

    private static readonly SKColor BackgroundColor = SKColors.White;
    private static readonly SKColor HeaderBackgroundColor = new(0xF0, 0xF0, 0xF0);
    private static readonly SKColor HeaderTextColor = SKColors.Black;
    private static readonly SKColor ThinBorderColor = new(0xCC, 0xCC, 0xCC);
    private static readonly SKColor ThickBorderColor = SKColors.Black;
    private static readonly SKColor TintColor = new(0xF5, 0xF5, 0xDC);
    private static readonly SKColor HatchBackgroundColor = SKColors.White;
    private static readonly SKColor MarkerColor = new(0xE0, 0x00, 0xE0);
    private static readonly SKColor MarkerTextColor = SKColors.White;

    private readonly GridImageRenderOptions _options;

    public GridImageRenderer()
        : this(new GridImageRenderOptions())
    {
    }

    public GridImageRenderer(GridImageRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    private int CellSize => Scaled(_options.CellSize);

    private int HeaderLeft => Scaled(_options.HeaderLeft);

    private int HeaderTop => Scaled(_options.HeaderTop);

    public byte[] Render(GridImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var rowCount = image.RowLabels.Count;
        var columnCount = image.Columns.Count;
        if (image.Cells.GetLength(0) != rowCount || image.Cells.GetLength(1) != columnCount)
        {
            throw new ArgumentException("Cell dimensions must match the row and column counts.", nameof(image));
        }

        var width = HeaderLeft + (columnCount * CellSize);
        var height = HeaderTop + (rowCount * CellSize);
        if (width <= 0)
        {
            width = HeaderLeft;
        }
        if (height <= 0)
        {
            height = HeaderTop;
        }

        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(BackgroundColor);

        DrawColumnTints(canvas, image);
        DrawCells(canvas, image);
        DrawColumnHeader(canvas, image);
        DrawRowHeader(canvas, image);
        DrawHeaderCorner(canvas);
        DrawMarkers(canvas, image);

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, PngQuality);
        return data.ToArray();
    }

    private int Scaled(int value) => (int)MathF.Round(value * _options.Scale);

    private float Scaled(float value) => value * _options.Scale;

    private SKRect CellRect(int row, int column)
    {
        var x = HeaderLeft + (column * CellSize);
        var y = HeaderTop + (row * CellSize);
        return new SKRect(x, y, x + CellSize, y + CellSize);
    }

    private void DrawColumnTints(SKCanvas canvas, GridImage image)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, Color = TintColor, IsAntialias = false };
        var bottom = HeaderTop + (image.RowLabels.Count * CellSize);
        for (var c = 0; c < image.Columns.Count; c++)
        {
            if (!image.Columns[c].Tinted)
            {
                continue;
            }

            var x = HeaderLeft + (c * CellSize);
            canvas.DrawRect(new SKRect(x, 0, x + CellSize, bottom), paint);
        }
    }

    private void DrawCells(SKCanvas canvas, GridImage image)
    {
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };
        using var thinBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ThinBorderColor,
            StrokeWidth = Scaled(ThinBorderWidth),
            IsAntialias = false,
        };
        using var thickBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ThickBorderColor,
            StrokeWidth = Scaled(_options.ThickBorderWidth),
            IsAntialias = false,
        };
        using var labelFont = new SKFont { Size = Scaled(CellLabelFontSize), Embolden = true };
        using var darkFillPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var lightFillPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var darkOutlinePaint = OutlinePaint(SKColors.White);
        using var lightOutlinePaint = OutlinePaint(SKColors.Black);

        for (var r = 0; r < image.RowLabels.Count; r++)
        {
            for (var c = 0; c < image.Columns.Count; c++)
            {
                var cell = image.Cells[r, c];
                var rect = CellRect(r, c);

                if (cell.HatchColor is { } hatchColor)
                {
                    DrawHatchedCell(canvas, rect, hatchColor);
                }
                else if (cell.Fill is { } fill)
                {
                    fillPaint.Color = fill;
                    canvas.DrawRect(rect, fillPaint);
                }

                var borderPaint = cell.ThickBorder ? thickBorderPaint : thinBorderPaint;
                var inset = borderPaint.StrokeWidth / 2f;
                canvas.DrawRect(new SKRect(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset), borderPaint);

                if (!string.IsNullOrEmpty(cell.Label))
                {
                    var baselineY = rect.MidY + (labelFont.Size / 2f) - Scaled(CellLabelBaselineOffset);
                    DrawCenteredText(canvas, labelFont, cell.LightLabel ? lightOutlinePaint : darkOutlinePaint, cell.Label, rect.MidX, baselineY);
                    DrawCenteredText(canvas, labelFont, cell.LightLabel ? lightFillPaint : darkFillPaint, cell.Label, rect.MidX, baselineY);
                }
            }
        }
    }

    private SKPaint OutlinePaint(SKColor color) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = Scaled(LabelOutlineWidth),
        StrokeJoin = SKStrokeJoin.Round,
    };

    private void DrawHatchedCell(SKCanvas canvas, SKRect rect, SKColor hatchColor)
    {
        using var backgroundPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = HatchBackgroundColor, IsAntialias = false };
        canvas.DrawRect(rect, backgroundPaint);

        using var stripePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = hatchColor,
            StrokeWidth = Scaled(HatchStrokeWidth),
            IsAntialias = true,
        };

        var save = canvas.Save();
        canvas.ClipRect(rect);
        var diagonal = rect.Width + rect.Height;
        var spacing = Scaled(HatchStripeSpacing);
        for (var offset = -diagonal; offset <= diagonal; offset += spacing)
        {
            canvas.DrawLine(rect.Left + offset, rect.Top, rect.Left + offset + rect.Height, rect.Bottom, stripePaint);
        }
        canvas.RestoreToCount(save);
    }

    private void DrawColumnHeader(SKCanvas canvas, GridImage image)
    {
        using var backgroundPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = HeaderBackgroundColor, IsAntialias = false };
        canvas.DrawRect(new SKRect(HeaderLeft, 0, HeaderLeft + (image.Columns.Count * CellSize), HeaderTop), backgroundPaint);

        using var font = new SKFont { Size = Scaled(HeaderFontSize) };
        using var textPaint = new SKPaint { Color = HeaderTextColor, IsAntialias = true };

        for (var c = 0; c < image.Columns.Count; c++)
        {
            var centerX = HeaderLeft + (c * CellSize) + (CellSize / 2f);
            DrawCenteredText(canvas, font, textPaint, image.Columns[c].TopLabel, centerX, (HeaderTop / 2f) - Scaled(ColumnTopBaselineOffset));
            DrawCenteredText(canvas, font, textPaint, image.Columns[c].BottomLabel, centerX, HeaderTop - Scaled(ColumnBottomBaselineOffset));
        }
    }

    private void DrawRowHeader(SKCanvas canvas, GridImage image)
    {
        using var backgroundPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = HeaderBackgroundColor, IsAntialias = false };
        canvas.DrawRect(new SKRect(0, HeaderTop, HeaderLeft, HeaderTop + (image.RowLabels.Count * CellSize)), backgroundPaint);

        using var font = new SKFont { Size = Scaled(RowLabelFontSize) };
        using var textPaint = new SKPaint { Color = HeaderTextColor, IsAntialias = true };

        for (var r = 0; r < image.RowLabels.Count; r++)
        {
            var y = HeaderTop + (r * CellSize);
            var centerY = y + (CellSize / 2f) + (font.Size / 2f) - Scaled(RowLabelBaselineOffset);
            DrawCenteredText(canvas, font, textPaint, image.RowLabels[r], HeaderLeft / 2f, centerY);
        }
    }

    private void DrawHeaderCorner(SKCanvas canvas)
    {
        using var backgroundPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = HeaderBackgroundColor, IsAntialias = false };
        canvas.DrawRect(new SKRect(0, 0, HeaderLeft, HeaderTop), backgroundPaint);
    }

    private void DrawMarkers(SKCanvas canvas, GridImage image)
    {
        if (image.Markers.Count == 0)
        {
            return;
        }

        using var ringPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = MarkerColor, StrokeWidth = Scaled(MarkerRingWidth), IsAntialias = true };
        using var badgePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = MarkerColor, IsAntialias = true };
        using var textPaint = new SKPaint { Color = MarkerTextColor, IsAntialias = true };
        using var font = new SKFont { Size = Scaled(MarkerFontSize), Embolden = true };
        var radius = Scaled(MarkerBadgeRadius);

        foreach (var marker in image.Markers)
        {
            var rect = CellRect(marker.Row, marker.Column);
            var inset = ringPaint.StrokeWidth / 2f;
            canvas.DrawRect(new SKRect(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset), ringPaint);
            canvas.DrawCircle(rect.Right, rect.Top, radius, badgePaint);
            DrawCenteredText(canvas, font, textPaint, marker.Label, rect.Right, rect.Top + Scaled(MarkerBaselineOffset));
        }
    }

    private static void DrawCenteredText(SKCanvas canvas, SKFont font, SKPaint paint, string text, float centerX, float baselineY)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var width = font.MeasureText(text);
        canvas.DrawText(text, centerX - (width / 2f), baselineY, SKTextAlign.Left, font, paint);
    }
}
