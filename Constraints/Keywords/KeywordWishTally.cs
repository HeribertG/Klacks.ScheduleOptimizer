// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Counts of missed wishes of a plan, per tier, plus the unfulfilled positive wishes.
/// </summary>
/// <param name="BrokenFree">Worked days marked FREE.</param>
/// <param name="BrokenOnly">Worked days against an EARLY / LATE / NIGHT.</param>
/// <param name="BrokenNo">Worked days against a -EARLY / -LATE / -NIGHT.</param>
/// <param name="Unfulfilled">Positive wishes without a matching shift.</param>
public readonly record struct KeywordWishTally(int BrokenFree, int BrokenOnly, int BrokenNo, int Unfulfilled)
{
    public int Broken => BrokenFree + BrokenOnly + BrokenNo;

    /// <summary>The tally with one more day's verdict.</summary>
    /// <param name="verdict">Verdict of the day.</param>
    public KeywordWishTally Add(KeywordDayVerdict verdict) => new(
        BrokenFree + (verdict.Broken == KeywordWishOutcome.BrokenFree ? 1 : 0),
        BrokenOnly + (verdict.Broken == KeywordWishOutcome.BrokenOnly ? 1 : 0),
        BrokenNo + (verdict.Broken == KeywordWishOutcome.BrokenNo ? 1 : 0),
        Unfulfilled + (verdict.Unfulfilled ? 1 : 0));

    /// <summary>Weighted penalty of the tally (lower is better).</summary>
    /// <param name="weights">Tier weights.</param>
    public double Penalty(KeywordWishWeights weights)
        => (BrokenFree * weights.Free) + (BrokenOnly * weights.Only) + (BrokenNo * weights.No) + (Unfulfilled * weights.Unfulfilled);
}
