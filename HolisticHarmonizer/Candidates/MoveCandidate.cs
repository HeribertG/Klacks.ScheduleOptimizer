// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;

/// <summary>
/// A pre-validated swap candidate for the stage-3 search. The host pre-computes a pool of
/// structurally-promising swaps per intent and runs each through the hard-constraint validator;
/// the deterministic search evaluates them singly or in pairs. Candidates still pass the full
/// downstream pipeline (committee + score-greedy) when evaluated as a batch.
/// </summary>
/// <param name="RowA">Zero-based row index of the first cell.</param>
/// <param name="DayA">Zero-based day index of the first cell.</param>
/// <param name="RowB">Zero-based row index of the second cell.</param>
/// <param name="DayB">Zero-based day index of the second cell.</param>
/// <param name="Hint">Short, prompt-ready description of why the host considers the swap
/// promising (e.g. "extends r02 block 4->6 days"), used for diagnostics.</param>
/// <param name="ExpectedBenefit">Heuristic ranking score used to sort and top-K-cap the
/// candidate pool. Larger is better. Not a fitness delta — only the host scorer can say that.</param>
public sealed record MoveCandidate(
    int RowA,
    int DayA,
    int RowB,
    int DayB,
    string Hint,
    double ExpectedBenefit);
