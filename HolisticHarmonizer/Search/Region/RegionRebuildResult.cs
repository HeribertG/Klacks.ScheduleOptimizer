// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Global;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

/// <summary>Outcome of the region rebuild phase.</summary>
/// <param name="AppliedBatches">One accepted batch (intent region_rebuild) per kept region rebuild, in order.</param>
/// <param name="HarmonyBefore">Global harmony when the phase started.</param>
/// <param name="HarmonyAfter">Global harmony when the phase ended; never below <paramref name="HarmonyBefore"/>.</param>
/// <param name="RoundsRun">Rounds actually run.</param>
/// <param name="CandidatesFound">Admitted region rebuilds over all rounds (kept or not).</param>
/// <param name="WallClockBudgetHit">True when the safety-net time budget ended the phase.</param>
public sealed record RegionRebuildResult(
    IReadOnlyList<BatchEvaluation> AppliedBatches,
    GlobalHarmonyScore HarmonyBefore,
    GlobalHarmonyScore HarmonyAfter,
    int RoundsRun,
    int CandidatesFound,
    bool WallClockBudgetHit);
