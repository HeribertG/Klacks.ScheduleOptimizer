// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

/// <summary>
/// A coordinated group of cell swaps the search evaluates as one atomic transformation.
/// Holistic Harmonizer evaluates the batch as a unit (apply all, score the end state, revert if worse)
/// rather than per-step, so holistic moves are possible where intermediate steps
/// would temporarily worsen the score but the final state improves it.
/// </summary>
/// <param name="BatchId">Stable identifier for logging and correlation.</param>
/// <param name="Intent">Label of the candidate generator the batch came from (e.g. "consolidate_block"),
/// see <see cref="HolisticIntent"/>.</param>
/// <param name="Iteration">Search iteration the batch was generated in (zero-based).</param>
/// <param name="Steps">Ordered list of swaps. Order matters — evaluation applies them in sequence
/// and may keep a valid prefix if a later step fails.</param>
public sealed record MutationBatch(
    Guid BatchId,
    string Intent,
    int Iteration,
    IReadOnlyList<PlanCellSwap> Steps);
