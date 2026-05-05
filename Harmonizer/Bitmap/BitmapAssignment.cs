// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <param name="AgentId">Owner of the assignment</param>
/// <param name="Date">Calendar date</param>
/// <param name="Symbol">Ordinal shift category, or Break for absences</param>
/// <param name="ShiftRefId">Reference to the original Shift definition (Guid.Empty for Break)</param>
/// <param name="WorkIds">DB Work or Break entry ids that materialise this assignment</param>
/// <param name="IsLocked">True for Locked Works (LockLevel != None) and ALL Break assignments — neither may be mutated</param>
/// <param name="StartAt">Start instant of the work for pause/weekly-hours checks (default for Break)</param>
/// <param name="EndAt">End instant of the work for pause/weekly-hours checks (default for Break)</param>
/// <param name="Hours">Paid hours: Work.WorkTime for shifts, Break.WorkTime for absences. Break hours count toward target hours but not toward MaxWeeklyHours.</param>
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
