// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <summary>
/// Ordinal classification of a bitmap cell. The numeric ordering 0..4 is load-bearing for the
/// Frühdienst → Spätdienst → Nachtdienst transition rule used by the harmony scorer.
/// Break is appended outside that ordinal range: it represents an absence (vacation, sick,
/// leave) and is always paired with Cell.IsLocked = true. Break.Hours contribute to target
/// hours but not to the weekly-max cap.
/// </summary>
public enum CellSymbol : byte
{
    Free = 0,
    Early = 1,
    Late = 2,
    Night = 3,
    Other = 4,
    Break = 5,
}
