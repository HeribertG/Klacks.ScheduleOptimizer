// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Missed wishes of a plan: the counts and, when requested, the single items.
/// </summary>
/// <param name="Tally">Counts per tier and the unfulfilled positive wishes.</param>
/// <param name="Items">The missed wishes in agent and date order; empty when items were not requested.</param>
public sealed record KeywordWishResult(KeywordWishTally Tally, IReadOnlyList<KeywordWishItem> Items)
{
    public static KeywordWishResult Empty { get; } = new(default, []);
}
