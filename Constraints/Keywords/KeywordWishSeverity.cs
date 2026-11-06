// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Rank of one broken restriction for choosing between placement candidates: no broken wish ranks best, then a broken
/// NoX, then a broken OnlyX, then a broken FREE. The numeric order of <see cref="KeywordWishOutcome"/> runs the other way
/// and must never be compared directly.
/// </summary>
public static class KeywordWishSeverity
{
    public const int NoneBroken = 0;
    public const int BrokenNo = 1;
    public const int BrokenOnly = 2;
    public const int BrokenFree = 3;

    /// <summary>Severity of a broken restriction; <see cref="NoneBroken"/> for null or an unfulfilled wish.</summary>
    /// <param name="broken">The broken restriction of a placement, null when it breaks none.</param>
    public static int Of(KeywordWishOutcome? broken) => broken switch
    {
        KeywordWishOutcome.BrokenFree => BrokenFree,
        KeywordWishOutcome.BrokenOnly => BrokenOnly,
        KeywordWishOutcome.BrokenNo => BrokenNo,
        _ => NoneBroken,
    };
}
