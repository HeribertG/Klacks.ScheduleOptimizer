// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

/// <summary>
/// A block of the plan the region rebuild may rearrange: on every day of the window the cells of these rows are
/// permuted among the rows. The cells of a day stay the same multiset, so the daily coverage never changes; a row's
/// sequence may change both vertically (taking another row's block) and horizontally (another row compensating).
/// </summary>
/// <param name="Rows">Distinct row indices, ascending.</param>
/// <param name="DayFrom">First day of the window (inclusive).</param>
/// <param name="DayTo">Last day of the window (inclusive).</param>
public sealed record HarmonyRegion(IReadOnlyList<int> Rows, int DayFrom, int DayTo)
{
    /// <summary>True when the two regions share a row on overlapping or adjacent days (adjacent days share pauses).</summary>
    public bool Touches(HarmonyRegion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var daysTouch = DayFrom <= other.DayTo + 1 && other.DayFrom <= DayTo + 1;
        return daysTouch && Rows.Intersect(other.Rows).Any();
    }
}
