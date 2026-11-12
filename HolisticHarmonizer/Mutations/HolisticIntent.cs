// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

/// <summary>
/// Catalog of the stage-3 move intents. Each intent represents a distinct optimization angle with
/// its own candidate generator; the deterministic search collects the candidates of all intents
/// per iteration and labels each evaluated batch with the intent it came from.
/// </summary>
public static class HolisticIntent
{
    /// <summary>Merge fragmented work blocks of one employee into a longer contiguous run.</summary>
    public const string ConsolidateBlock = "consolidate_block";

    /// <summary>Extend the rest period between work blocks of one employee — adjacent free days widen.</summary>
    public const string EnlargePause = "enlarge_pause";

    /// <summary>Move work from over-worked employees to under-worked ones — equalize target-hours deviation.</summary>
    public const string RedistributeLoad = "redistribute_load";

    /// <summary>All known intents, in candidate collection order.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        ConsolidateBlock,
        EnlargePause,
        RedistributeLoad,
    };

    /// <summary>Returns true when the supplied intent label is one of the known catalog entries.</summary>
    public static bool IsKnown(string intent) => All.Contains(intent);
}
