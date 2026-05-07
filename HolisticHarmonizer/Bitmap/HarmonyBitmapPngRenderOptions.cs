// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Bitmap;

/// <summary>
/// Configuration for <see cref="HarmonyBitmapPngRenderer"/>. All values are pixel sizes
/// or boolean toggles; a sensible <see cref="Default"/> instance is provided so callers
/// rarely need to set anything explicitly.
/// </summary>
/// <param name="CellSize">Width and height of a single schedule cell in pixels</param>
/// <param name="HeaderLeft">Width in pixels of the row header that shows agent initials</param>
/// <param name="HeaderTop">Height in pixels of the column header that shows day number and weekday letter</param>
/// <param name="LockedBorderThickness">Stroke width in pixels for the thick black border drawn around locked or Break cells</param>
/// <param name="TintWeekends">When true, Saturday and Sunday columns receive a light tint background</param>
public sealed record HarmonyBitmapPngRenderOptions(
    int CellSize = 24,
    int HeaderLeft = 32,
    int HeaderTop = 32,
    int LockedBorderThickness = 2,
    bool TintWeekends = true)
{
    public static HarmonyBitmapPngRenderOptions Default { get; } = new();
}
