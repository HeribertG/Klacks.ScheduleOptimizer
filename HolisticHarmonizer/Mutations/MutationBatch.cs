// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

/// <summary>
/// A coordinated group of cell swaps the LLM proposes as one atomic transformation.
/// Holistic Harmonizer evaluates the batch as a unit (apply all, score the end state, revert if worse)
/// rather than per-step, so the LLM can suggest holistic moves where intermediate steps
/// would temporarily worsen the score but the final state improves it.
/// </summary>
/// <param name="BatchId">Stable identifier for logging and reject-memory correlation.</param>
/// <param name="Intent">Short label the LLM attaches to the batch (e.g. "consolidate_block")
/// so we can track which intents work and feed adaptive operator selection later.</param>
/// <param name="LlmIteration">Inner-loop iteration the batch was generated in (zero-based).</param>
/// <param name="Steps">Ordered list of swaps. Order matters — evaluation applies them in sequence
/// and may keep a valid prefix if a later step fails.</param>
public sealed record MutationBatch(
    Guid BatchId,
    string Intent,
    int LlmIteration,
    IReadOnlyList<PlanCellSwap> Steps);
