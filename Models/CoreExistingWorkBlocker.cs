// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// An existing Work entity that is NOT lifted into the GA genome (no LockLevel) but still
/// blocks any auction slot that overlaps with its time span. Without this veto, the Wizard
/// could award an early shift to an agent that already has a late shift on the same day in
/// the database.
/// </summary>
/// <param name="AgentId">Client identifier the work belongs to</param>
/// <param name="Date">Calendar date of the work</param>
/// <param name="StartAt">Inclusive start of the work span</param>
/// <param name="EndAt">Exclusive end of the work span (after midnight if EndAt &lt; StartAt)</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreExistingWorkBlocker(
    string AgentId,
    DateOnly Date,
    DateTime StartAt,
    DateTime EndAt);
