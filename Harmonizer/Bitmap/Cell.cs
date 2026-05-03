// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <param name="Symbol">Ordinal shift category used for harmony scoring</param>
/// <param name="ShiftRefId">Reference to the original Shift definition (null on Free cells)</param>
/// <param name="WorkIds">DB Work entries that materialise this cell (empty on Free cells)</param>
/// <param name="IsLocked">True if the cell originates from a Work with LockLevel != None and must not be mutated</param>
/// <param name="StartAt">Earliest start of any contributing Work on this day, used by domain-aware validation</param>
/// <param name="EndAt">Latest end of any contributing Work on this day, used by domain-aware validation</param>
/// <param name="Hours">Sum of paid hours of all contributing Works on this day</param>
public sealed record Cell(
    CellSymbol Symbol,
    Guid? ShiftRefId,
    IReadOnlyList<Guid> WorkIds,
    bool IsLocked,
    DateTime StartAt = default,
    DateTime EndAt = default,
    decimal Hours = 0m)
{
    public static Cell Free() => new(CellSymbol.Free, null, [], false);
}
