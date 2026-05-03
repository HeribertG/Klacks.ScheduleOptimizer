// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <summary>
/// Ordinal classification of a bitmap cell. The numeric ordering is load-bearing for the
/// Frühdienst → Spätdienst → Nachtdienst transition rule used by the harmony scorer.
/// </summary>
public enum CellSymbol : byte
{
    Free = 0,
    Early = 1,
    Late = 2,
    Night = 3,
    Other = 4,
}
