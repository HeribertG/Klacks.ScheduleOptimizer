// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;

/// <summary>
/// Strategy that emits a ranked list of swap suggestions for one specific intent. The
/// implementation is purely structural — it must NOT call the fitness evaluator or the
/// committee. Aggregation, deduplication, hard-validation and Top-K capping are done by
/// <see cref="MoveCandidatePool"/>.
/// </summary>
public interface IMoveCandidateGenerator
{
    /// <summary>The intent label this generator targets (one of <c>HolisticIntent.*</c>).</summary>
    string Intent { get; }

    /// <summary>
    /// Emits raw candidate suggestions for the given bitmap. Implementations may emit more
    /// than the final cap — the pool de-duplicates, hard-validates and trims.
    /// </summary>
    IEnumerable<MoveCandidate> Generate(HarmonyBitmap bitmap);
}
