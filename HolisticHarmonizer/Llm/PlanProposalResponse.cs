// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Llm;

/// <summary>
/// Output of one inner-loop LLM call. Holistic Holistic Harmonizer returns a list of
/// <see cref="MutationBatch"/> rather than flat swaps so the LLM can group multi-step
/// transformations whose intermediate steps would temporarily worsen the score.
/// </summary>
/// <param name="Batches">Parsed batches in the order returned by the LLM.</param>
/// <param name="RawResponse">Raw LLM response text for diagnostics on parse failure.</param>
/// <param name="ParsingError">Null on success; otherwise a short reason why parsing failed.</param>
/// <param name="LlmSignaledSatisfied">True only when the response carried an explicitly empty
/// "batches": [] array - the sole valid convergence signal. Batches that were present but all
/// discarded (empty steps, wrong field names) are a dud iteration, not convergence.</param>
public sealed record PlanProposalResponse(
    IReadOnlyList<MutationBatch> Batches,
    string RawResponse,
    string? ParsingError,
    bool LlmSignaledSatisfied = false);
