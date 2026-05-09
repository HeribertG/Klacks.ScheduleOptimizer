// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <param name="Agents">Agents that participate in the harmonisation (order = input order from caller)</param>
/// <param name="StartDate">Inclusive start of the bitmap date range</param>
/// <param name="EndDate">Inclusive end of the bitmap date range</param>
/// <param name="Assignments">All persisted Work-derived assignments inside the date range</param>
/// <param name="SofteningHints">Optional hints from Wizard 1 marking slots where soft constraints were relaxed</param>
/// <param name="Availability">Optional per (agent, date) availability map; missing keys default to "always available"</param>
/// <param name="BoundaryAssignments">
/// Works/Breaks on the days adjacent to the bitmap (within ContextDaysBefore/After of the request).
/// Provided for boundary-constraint validators (MaxConsecutiveDays/MinRestHours) to detect runs that cross the
/// bitmap edges. The bitmap itself stays sized to [StartDate, EndDate]; these entries are never rendered into cells
/// and never mutated.
/// </param>
public sealed record BitmapInput(
    IReadOnlyList<BitmapAgent> Agents,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<BitmapAssignment> Assignments,
    IReadOnlyList<SofteningHint>? SofteningHints = null,
    IReadOnlyDictionary<(string AgentId, DateOnly Date), DayAvailability>? Availability = null,
    IReadOnlyList<BitmapAssignment>? BoundaryAssignments = null);
