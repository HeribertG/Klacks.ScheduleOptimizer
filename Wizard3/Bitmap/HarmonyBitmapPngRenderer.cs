// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using SkiaSharp;

namespace Klacks.ScheduleOptimizer.Wizard3.Bitmap;

/// <summary>
/// Renders a <see cref="HarmonyBitmap"/> as a PNG image for vision-capable LLMs in Wizard 3.
/// Cells are colored by <see cref="CellSymbol"/>; locked cells get a thick black border;
/// Break cells use a red/white diagonal hatch pattern. Weekend columns receive a light tint
/// so the LLM can locate Saturday/Sunday at a glance.
/// </summary>
/// <param name="options">Layout and color options. <see cref="HarmonyBitmapPngRenderOptions.Default"/> covers most callers.</param>
public sealed class HarmonyBitmapPngRenderer
{
    private const int PngQuality = 100;
    private const int HeaderFontSize = 11;
    private const int CellFontSize = 10;
    private const int CellLabelFontSize = 14;
    private const float ThinBorderThickness = 1f;
    private const float HatchStripeSpacing = 4f;
    private const float HatchStrokeWidth = 2f;

    private static readonly SKColor BackgroundColor = SKColors.White;
    private static readonly SKColor HeaderBackgroundColor = new(0xF0, 0xF0, 0xF0);
    private static readonly SKColor HeaderTextColor = SKColors.Black;
    private static readonly SKColor ThinBorderColor = new(0xCC, 0xCC, 0xCC);
    private static readonly SKColor LockedBorderColor = SKColors.Black;
    private static readonly SKColor WeekendTintColor = new(0xF5, 0xF5, 0xDC);

    private static readonly SKColor FreeFillColor = SKColors.White;
    private static readonly SKColor EarlyFillColor = new(0xFF, 0xD7, 0x00);
    private static readonly SKColor LateFillColor = new(0xFF, 0x8C, 0x00);
    private static readonly SKColor NightFillColor = new(0x1E, 0x3A, 0x8A);
    private static readonly SKColor OtherFillColor = new(0x80, 0x80, 0x80);
    private static readonly SKColor BreakStripeColor = new(0xDC, 0x26, 0x26);
    private static readonly SKColor BreakBackgroundColor = SKColors.White;

    private readonly HarmonyBitmapPngRenderOptions _options;

    public HarmonyBitmapPngRenderer()
        : this(HarmonyBitmapPngRenderOptions.Default)
    {
    }

    public HarmonyBitmapPngRenderer(HarmonyBitmapPngRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public byte[] Render(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var width = _options.HeaderLeft + (bitmap.DayCount * _options.CellSize);
        var height = _options.HeaderTop + (bitmap.RowCount * _options.CellSize);
        if (width <= 0)
        {
            width = _options.HeaderLeft;
        }
        if (height <= 0)
        {
            height = _options.HeaderTop;
        }

        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(BackgroundColor);

        DrawWeekendTints(canvas, bitmap);
        DrawCells(canvas, bitmap);
        DrawColumnHeader(canvas, bitmap);
        DrawRowHeader(canvas, bitmap);
        DrawHeaderCorner(canvas);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, PngQuality);
        return data.ToArray();
    }

    private void DrawWeekendTints(SKCanvas canvas, HarmonyBitmap bitmap)
    {
        if (!_options.TintWeekends)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = WeekendTintColor,
            IsAntialias = false,
        };

        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var dayOfWeek = bitmap.Days[d].DayOfWeek;
            if (dayOfWeek != DayOfWeek.Saturday && dayOfWeek != DayOfWeek.Sunday)
            {
                continue;
            }

            var x = _options.HeaderLeft + (d * _options.CellSize);
            var rect = new SKRect(x, 0, x + _options.CellSize, _options.HeaderTop + (bitmap.RowCount * _options.CellSize));
            canvas.DrawRect(rect, paint);
        }
    }

    private void DrawCells(SKCanvas canvas, HarmonyBitmap bitmap)
    {
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };
        using var thinBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ThinBorderColor,
            StrokeWidth = ThinBorderThickness,
            IsAntialias = false,
        };
        using var thickBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = LockedBorderColor,
            StrokeWidth = _options.LockedBorderThickness,
            IsAntialias = false,
        };
        using var symbolFont = new SKFont { Size = CellLabelFontSize, Embolden = true };
        using var darkSymbolPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var lightSymbolPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

        for (var r = 0; r < bitmap.RowCount; r++)
        {
            for (var d = 0; d < bitmap.DayCount; d++)
            {
                var cell = bitmap.GetCell(r, d);
                var x = _options.HeaderLeft + (d * _options.CellSize);
                var y = _options.HeaderTop + (r * _options.CellSize);
                var rect = new SKRect(x, y, x + _options.CellSize, y + _options.CellSize);

                if (cell.Symbol == CellSymbol.Break)
                {
                    DrawBreakCell(canvas, rect);
                }
                else
                {
                    fillPaint.Color = ResolveFillColor(cell.Symbol);
                    if (cell.Symbol != CellSymbol.Free || !IsWeekendColumn(bitmap, d))
                    {
                        canvas.DrawRect(rect, fillPaint);
                    }
                }

                var borderPaint = cell.IsLocked ? thickBorderPaint : thinBorderPaint;
                var inset = borderPaint.StrokeWidth / 2f;
                var borderRect = new SKRect(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
                canvas.DrawRect(borderRect, borderPaint);

                var symbolLetter = ResolveSymbolLetter(cell.Symbol);
                if (symbolLetter is not null)
                {
                    var paint = NeedsLightSymbol(cell.Symbol) ? lightSymbolPaint : darkSymbolPaint;
                    var centerX = rect.MidX;
                    var baselineY = rect.MidY + (CellLabelFontSize / 2f) - 1;
                    DrawCenteredText(canvas, symbolFont, paint, symbolLetter, centerX, baselineY);
                }
            }
        }
    }

    private static string? ResolveSymbolLetter(CellSymbol symbol) => symbol switch
    {
        CellSymbol.Early => "E",
        CellSymbol.Late => "L",
        CellSymbol.Night => "N",
        CellSymbol.Other => "O",
        CellSymbol.Break => "B",
        _ => null,
    };

    private static bool NeedsLightSymbol(CellSymbol symbol) =>
        symbol == CellSymbol.Night || symbol == CellSymbol.Other;

    private bool IsWeekendColumn(HarmonyBitmap bitmap, int dayIndex)
    {
        if (!_options.TintWeekends)
        {
            return false;
        }
        var dow = bitmap.Days[dayIndex].DayOfWeek;
        return dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;
    }

    private static SKColor ResolveFillColor(CellSymbol symbol) => symbol switch
    {
        CellSymbol.Free => FreeFillColor,
        CellSymbol.Early => EarlyFillColor,
        CellSymbol.Late => LateFillColor,
        CellSymbol.Night => NightFillColor,
        CellSymbol.Other => OtherFillColor,
        CellSymbol.Break => BreakStripeColor,
        _ => FreeFillColor,
    };

    private static void DrawBreakCell(SKCanvas canvas, SKRect rect)
    {
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = BreakBackgroundColor,
            IsAntialias = false,
        };
        canvas.DrawRect(rect, bgPaint);

        using var stripePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = BreakStripeColor,
            StrokeWidth = HatchStrokeWidth,
            IsAntialias = true,
        };

        var save = canvas.Save();
        canvas.ClipRect(rect);
        var diag = rect.Width + rect.Height;
        for (var offset = -diag; offset <= diag; offset += HatchStripeSpacing)
        {
            var x0 = rect.Left + offset;
            var y0 = rect.Top;
            var x1 = rect.Left + offset + rect.Height;
            var y1 = rect.Bottom;
            canvas.DrawLine(x0, y0, x1, y1, stripePaint);
        }
        canvas.RestoreToCount(save);
    }

    private void DrawColumnHeader(SKCanvas canvas, HarmonyBitmap bitmap)
    {
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = HeaderBackgroundColor, IsAntialias = false };
        canvas.DrawRect(new SKRect(_options.HeaderLeft, 0, _options.HeaderLeft + (bitmap.DayCount * _options.CellSize), _options.HeaderTop), bgPaint);

        using var font = new SKFont { Size = HeaderFontSize };
        using var textPaint = new SKPaint
        {
            Color = HeaderTextColor,
            IsAntialias = true,
        };

        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var day = bitmap.Days[d];
            var dayNumber = day.Day.ToString(CultureInfo.InvariantCulture);
            var weekdayLetter = WeekdayLetter(day.DayOfWeek);
            var x = _options.HeaderLeft + (d * _options.CellSize);
            var centerX = x + (_options.CellSize / 2f);

            var halfHeader = _options.HeaderTop / 2f;
            DrawCenteredText(canvas, font, textPaint, dayNumber, centerX, halfHeader - 1);
            DrawCenteredText(canvas, font, textPaint, weekdayLetter, centerX, _options.HeaderTop - 2);
        }
    }

    private void DrawRowHeader(SKCanvas canvas, HarmonyBitmap bitmap)
    {
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = HeaderBackgroundColor, IsAntialias = false };
        canvas.DrawRect(new SKRect(0, _options.HeaderTop, _options.HeaderLeft, _options.HeaderTop + (bitmap.RowCount * _options.CellSize)), bgPaint);

        using var font = new SKFont { Size = CellFontSize };
        using var textPaint = new SKPaint
        {
            Color = HeaderTextColor,
            IsAntialias = true,
        };

        for (var r = 0; r < bitmap.RowCount; r++)
        {
            var initials = BuildInitials(bitmap.Rows[r].DisplayName);
            var y = _options.HeaderTop + (r * _options.CellSize);
            var centerX = _options.HeaderLeft / 2f;
            var centerY = y + (_options.CellSize / 2f) + (CellFontSize / 2f) - 1;
            DrawCenteredText(canvas, font, textPaint, initials, centerX, centerY);
        }
    }

    private void DrawHeaderCorner(SKCanvas canvas)
    {
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = HeaderBackgroundColor, IsAntialias = false };
        canvas.DrawRect(new SKRect(0, 0, _options.HeaderLeft, _options.HeaderTop), bgPaint);
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
