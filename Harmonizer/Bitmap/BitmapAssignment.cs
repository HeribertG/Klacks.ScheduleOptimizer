// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <param name="AgentId">Owner of the assignment</param>
/// <param name="Date">Calendar date</param>
/// <param name="Symbol">Ordinal shift category</param>
/// <param name="ShiftRefId">Reference to the original Shift definition</param>
/// <param name="WorkIds">DB Work entries that materialise this assignment</param>
/// <param name="IsLocked">True for Locked Works that the harmonizer must not mutate</param>
/// <param name="StartAt">Start instant of the work for pause/weekly-hours checks</param>
/// <param name="EndAt">End instant of the work for pause/weekly-hours checks</param>
/// <param name="Hours">Paid hours of the work</param>
public sealed record BitmapAssignment(
    string AgentId,
    DateOnly Date,
    CellSymbol Symbol,
    Guid ShiftRefId,
    IReadOnlyList<Guid> WorkIds,
    bool IsLocked,
    DateTime StartAt = default,
    DateTime EndAt = default,
    decimal Hours = 0m);
