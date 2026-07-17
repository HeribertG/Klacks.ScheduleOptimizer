// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

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
/// <param name="IneligibleAssignments">
/// Optional (agent, shift, date) triples the agent must not receive because it lacks a mandatory shift
/// qualification. Empty/null = no qualification gating. Consumed by the replace validator.
/// </param>
/// <param name="RestrictedTimeWindows">
/// Optional K16 seasonal daily forbidden-time windows resolved for the period's shifts. Empty/null = no
/// restricted windows. Consumed by Wizard 3's cross-day veto: a cross-day swap re-anchors a cell to a new
/// calendar day at persist time, so a compliant slot can be relocated into a forbidden window; the same-day
/// path never changes a cell's day and so does not read this.
/// </param>
public sealed record BitmapInput(
    IReadOnlyList<BitmapAgent> Agents,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<BitmapAssignment> Assignments,
    IReadOnlyList<SofteningHint>? SofteningHints = null,
    IReadOnlyDictionary<(string AgentId, DateOnly Date), DayAvailability>? Availability = null,
    IReadOnlyList<BitmapAssignment>? BoundaryAssignments = null,
    IReadOnlySet<(string AgentId, Guid ShiftId, DateOnly Date)>? IneligibleAssignments = null,
    IReadOnlyList<CoreRestrictedTimeWindow>? RestrictedTimeWindows = null);
