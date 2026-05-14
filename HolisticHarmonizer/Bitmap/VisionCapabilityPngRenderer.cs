// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using SkiaSharp;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Bitmap;

/// <summary>
/// Renders a tiny PNG containing a high-contrast secret token, used to verify that an LLM is
/// genuinely vision-capable (Holistic Harmonizer / Wizard 3 needs bitmap support). A model that
/// silently drops the image cannot read the token back and therefore fails the check.
/// </summary>
/// <param name="token">Short alphanumeric string painted prominently in the centre of the bitmap.</param>
public static class VisionCapabilityPngRenderer
{
    private const int CanvasWidth = 220;
    private const int CanvasHeight = 130;
    private const float TokenFontSize = 72f;
    private const float BorderWidth = 4f;
    private const int PngQuality = 100;

    private static readonly SKColor BackgroundColor = SKColors.White;
    private static readonly SKColor BoxFillColor = new(0xFF, 0xD7, 0x00);
    private static readonly SKColor BorderColor = SKColors.Black;
    private static readonly SKColor TextColor = SKColors.Black;

    public static byte[] Render(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var imageInfo = new SKImageInfo(CanvasWidth, CanvasHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(BackgroundColor);

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = BoxFillColor,
            IsAntialias = true,
        };
        canvas.DrawRect(new SKRect(0, 0, CanvasWidth, CanvasHeight), fillPaint);

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = BorderColor,
            StrokeWidth = BorderWidth,
            IsAntialias = true,
        };
        var inset = BorderWidth / 2f;
        canvas.DrawRect(new SKRect(inset, inset, CanvasWidth - inset, CanvasHeight - inset), borderPaint);

        using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), TokenFontSize);
        using var textPaint = new SKPaint
        {
            Color = TextColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        var textWidth = font.MeasureText(token);
        var metrics = font.Metrics;
        var centerX = (CanvasWidth - textWidth) / 2f;
        var baselineY = (CanvasHeight / 2f) - ((metrics.Ascent + metrics.Descent) / 2f);
        canvas.DrawText(token, centerX, baselineY, SKTextAlign.Left, font, textPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, PngQuality);
        return data.ToArray();
    }
}
