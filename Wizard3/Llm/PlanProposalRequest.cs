// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Wizard3.Loop;

namespace Klacks.ScheduleOptimizer.Wizard3.Llm;

/// <summary>
/// Input for one inner-loop LLM call. The engine assembles a fresh request per iteration
/// containing the current bitmap rendering, the adaptive step cap, and a digest of recently
/// rejected batches so the LLM can avoid repeating discarded ideas.
/// </summary>
/// <param name="ModelId">LLM model identifier (matches the assistant API model ids).</param>
/// <param name="PlanText">The bitmap rendered as plain text. Vision input is added by a future
/// task; until then we ship the same text rendering as the MVP.</param>
/// <param name="AgentSummary">Per-row summary of agent constraints (target hours, max weekly,
/// max consecutive, min pause, preferred symbols).</param>
/// <param name="MaxStepsPerBatch">Adaptive cap on the number of swaps the LLM may put into
/// a single batch. Grows on accepted batches, shrinks on rejects.</param>
/// <param name="Language">UI language for the LLM's intent label and reason field.</param>
/// <param name="IterationIndex">Zero-based inner-loop iteration. Surfaces in logs and is
/// stamped into the resulting <see cref="MutationBatch.LlmIteration"/>.</param>
/// <param name="PriorRejections">Compact digest of recently rejected batches used to steer
/// the LLM away from repeating them.</param>
public sealed record PlanProposalRequest(
    string ModelId,
    string PlanText,
    string AgentSummary,
    int MaxStepsPerBatch,
    string Language,
    int IterationIndex,
    IReadOnlyList<RejectMemoryEntry> PriorRejections);
