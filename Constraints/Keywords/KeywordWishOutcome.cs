// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// How a planned day misses an employee's schedule command: a broken restriction in one of three tiers
/// (FREE, OnlyX, NoX) or an unfulfilled positive wish (-FREE or OnlyX without a matching shift).
/// </summary>
public enum KeywordWishOutcome
{
    BrokenFree,
    BrokenOnly,
    BrokenNo,
    Unfulfilled,
}
