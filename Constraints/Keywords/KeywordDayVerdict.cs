// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Result of one (agent, day) against its schedule commands. A day can be both broken and unfulfilled, e.g. NIGHT with
/// only an early shift.
/// </summary>
/// <param name="Broken">Tier of the broken restriction; null when the day's restriction holds.</param>
/// <param name="Unfulfilled">True when the day's positive wish is not met.</param>
public readonly record struct KeywordDayVerdict(KeywordWishOutcome? Broken, bool Unfulfilled)
{
    public static KeywordDayVerdict Clean { get; } = new(null, false);

    public bool IsClean => Broken is null && !Unfulfilled;
}
