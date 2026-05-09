// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;

/// <summary>
/// Strict-Candidate-Mode helper. When the host supplies a non-empty pre-validated candidate list,
/// any LLM-emitted step whose coordinates are not in the list is treated as a hallucination and
/// silently dropped before reaching the BatchEvaluator. Steps that match a candidate (in either
/// direction — rowA/rowB and dayA/dayB are swappable) are kept. With an empty candidate list
/// the helper is a no-op pass-through and the LLM keeps its full freedom.
/// </summary>
public static class CandidateStepFilter
{
    /// <summary>Counts how many of the batch's steps appear in the candidate list (either orientation).</summary>
    public static int CountStepsInCandidates(MutationBatch batch, IReadOnlyList<MoveCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return 0;
        }
        var hits = 0;
        foreach (var step in batch.Steps)
        {
            if (IsStepInCandidates(step, candidates))
            {
                hits++;
            }
        }
        return hits;
    }

    /// <summary>
    /// Returns the input batch if all steps are in the candidate list (or the list is empty);
    /// otherwise returns a new batch with only the matching steps preserved (same id and intent).
    /// </summary>
    public static MutationBatch FilterToCandidates(MutationBatch batch, IReadOnlyList<MoveCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return batch;
        }
        var keptSteps = new List<PlanCellSwap>(batch.Steps.Count);
        foreach (var step in batch.Steps)
        {
            if (IsStepInCandidates(step, candidates))
            {
                keptSteps.Add(step);
            }
        }
        if (keptSteps.Count == batch.Steps.Count)
        {
            return batch;
        }
        return new MutationBatch(batch.BatchId, batch.Intent, batch.LlmIteration, keptSteps);
    }

    /// <summary>
    /// True when <paramref name="step"/> matches any candidate in either orientation
    /// (direct: rowA/dayA == c.RowA/c.DayA AND rowB/dayB == c.RowB/c.DayB; mirrored: swap A and B).
    /// </summary>
    public static bool IsStepInCandidates(PlanCellSwap step, IReadOnlyList<MoveCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(candidates);
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var direct = c.RowA == step.RowA && c.DayA == step.DayA && c.RowB == step.RowB && c.DayB == step.DayB;
            var mirrored = c.RowA == step.RowB && c.DayA == step.DayB && c.RowB == step.RowA && c.DayB == step.DayA;
            if (direct || mirrored)
            {
                return true;
            }
        }
        return false;
    }
}
