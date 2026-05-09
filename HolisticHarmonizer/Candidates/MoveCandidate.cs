// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;

/// <summary>
/// A pre-validated swap candidate the host suggests to the LLM as inspiration. The host
/// pre-computes a small pool of structurally-promising swaps per intent and runs each
/// through the hard-constraint validator before exposing them in the prompt; the LLM may
/// pick one or more of them, combine them into a batch, or propose its own coordinates.
/// Candidates are advisory only — they pass the same downstream pipeline (committee +
/// score-greedy) when the LLM emits them.
/// </summary>
/// <param name="RowA">Zero-based row index of the first cell.</param>
/// <param name="DayA">Zero-based day index of the first cell.</param>
/// <param name="RowB">Zero-based row index of the second cell.</param>
/// <param name="DayB">Zero-based day index of the second cell.</param>
/// <param name="Hint">Short, prompt-ready description of why the host considers the swap
/// promising (e.g. "extends r02 block 4->6 days"). Renders into the LLM prompt verbatim.</param>
/// <param name="ExpectedBenefit">Heuristic ranking score used to sort and top-K-cap the
/// candidate pool. Larger is better. Not a fitness delta — only the host scorer can say that.</param>
public sealed record MoveCandidate(
    int RowA,
    int DayA,
    int RowB,
    int DayB,
    string Hint,
    double ExpectedBenefit);
