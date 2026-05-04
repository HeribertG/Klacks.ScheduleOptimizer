// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Multi-day block swap between two rows. Both blocks must have the same length.
/// Cells [RowA, StartDay..StartDay+Length-1] are swapped with cells [RowB, StartDay..StartDay+Length-1].
/// Unlike <see cref="ReplaceMove"/> (single day), block swaps preserve intra-block homogeneity
/// while opening shift-type rotation between agents that the same-day swap cannot achieve.
/// </summary>
/// <param name="RowA">Row index of the primary block</param>
/// <param name="RowB">Row index of the partner block</param>
/// <param name="StartDay">First day index of the block (inclusive)</param>
/// <param name="Length">Number of consecutive days in the block (must be >= 1)</param>
public sealed record BlockSwapMove(int RowA, int RowB, int StartDay, int Length);
