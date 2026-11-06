// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Counts of missed wishes of a plan, per tier, plus the unfulfilled positive wishes. The tiers are strict: one broken
/// FREE outweighs any number of broken OnlyX, one broken OnlyX any number of broken NoX, so tallies are compared
/// lexicographically on the counts (<see cref="CompareBrokenTo"/>), never through a weighted sum. The unfulfilled positive
/// wishes are a separate, lowest tier that the callers rank on their own.
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

    /// <summary>
    /// Lexicographic comparison of the broken restrictions (FREE, then OnlyX, then NoX); negative when this tally breaks
    /// less. The unfulfilled positive wishes are not part of it.
    /// </summary>
    /// <param name="other">The tally to compare with.</param>
    public int CompareBrokenTo(KeywordWishTally other)
    {
        var free = BrokenFree.CompareTo(other.BrokenFree);
        if (free != 0)
        {
            return free;
        }

        var only = BrokenOnly.CompareTo(other.BrokenOnly);
        return only != 0 ? only : BrokenNo.CompareTo(other.BrokenNo);
    }

    /// <summary>True when any single tier (the three broken tiers or the unfulfilled wishes) counts more than in the other tally.</summary>
    /// <param name="other">The tally to compare with, typically the state before a move.</param>
    public bool RisesInAnyTierAbove(KeywordWishTally other)
        => BrokenFree > other.BrokenFree
            || BrokenOnly > other.BrokenOnly
            || BrokenNo > other.BrokenNo
            || Unfulfilled > other.Unfulfilled;
}
