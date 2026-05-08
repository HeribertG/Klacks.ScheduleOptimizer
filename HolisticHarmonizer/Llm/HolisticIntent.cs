// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Llm;

/// <summary>
/// Catalog of intents the LLM may attach to a proposed mutation batch. Each intent represents
/// a distinct optimization angle — different goal text and different success heuristics. The
/// engine picks one intent per iteration via <c>RouletteIntentSelector</c> and tells the LLM
/// to focus on that intent for the next batch proposal. The LLM is still allowed to emit
/// other intents, but the selected one steers the goal section of the prompt.
/// </summary>
public static class HolisticIntent
{
    /// <summary>Merge fragmented work blocks of one employee into a longer contiguous run.</summary>
    public const string ConsolidateBlock = "consolidate_block";

    /// <summary>Extend the rest period between work blocks of one employee — adjacent free days widen.</summary>
    public const string EnlargePause = "enlarge_pause";

    /// <summary>Move work from over-worked employees to under-worked ones — equalize target-hours deviation.</summary>
    public const string RedistributeLoad = "redistribute_load";

    /// <summary>All known intents. Order is the default committee/prompt order.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        ConsolidateBlock,
        EnlargePause,
        RedistributeLoad,
    };

    /// <summary>Returns true when the supplied intent label is one of the known catalog entries.</summary>
    public static bool IsKnown(string intent) => All.Contains(intent);
}
