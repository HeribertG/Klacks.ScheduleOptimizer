// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Same-day swap between two rows. RowA's cell at Day swaps with RowB's cell at Day.
/// One side may be Free; in that case the move effectively reassigns the existing work
/// to the other agent. Cross-day moves are not modelled in Phase 4 because they would
/// change Wizard 1's already-validated day-coverage decision.
/// </summary>
/// <param name="RowA">Index of the row being optimised (the "primary" row in asymmetric acceptance)</param>
/// <param name="RowB">Index of the swap partner ("secondary" row)</param>
/// <param name="Day">Calendar day index in the bitmap</param>
public sealed record ReplaceMove(int RowA, int RowB, int Day);
