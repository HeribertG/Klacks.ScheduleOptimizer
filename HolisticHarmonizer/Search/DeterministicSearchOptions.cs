// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search;

/// <summary>
/// Limits and knobs of the deterministic stage-3 search. The iteration, no-improvement and evaluation caps are
/// deterministic; the wall-clock budget is only a safety net, and a run stopped by it is no longer reproducible.
/// </summary>
/// <param name="MaxIterations">Upper bound of applied moves (one move per iteration).</param>
/// <param name="MaxNoImprovementIterations">Consecutive iterations that only applied an equal-score (sideways) move before the search stops.</param>
/// <param name="PairPoolCap">Pairs are formed among the best PairPoolCap single moves (ranked by real end score); 0 disables pairs.</param>
/// <param name="TabuTenure">Iterations a just-applied swap stays forbidden (prevents undoing sideways moves).</param>
/// <param name="Seed">Seed for tie-breaking between equally good moves and for batch ids.</param>
/// <param name="WallClockBudget">Safety-net time budget for the whole search.</param>
/// <param name="MaxEvaluations">Deterministic upper bound of batch evaluations per pass.</param>
/// <param name="Restarts">Independent passes from the same start plan with different tie-breaking (pass 0 = first
/// best in candidate order, pass k = Random(Seed + k)); the best pass wins. MaxEvaluations applies per pass.</param>
/// <param name="IncludeAllSameDaySwaps">Adds every hard-valid same-day swap to the candidate pool neighbourhood
/// in every pass (much larger and slower; off by default).</param>
/// <param name="AlternateNeighbourhoods">Even passes search the pool neighbourhood, odd passes the pool plus every
/// hard-valid same-day swap; the best pass wins (never below the first in-order pass of either neighbourhood).
/// On by default: in the stage-3 benchmark each neighbourhood alone
/// lost on some scenarios (pool-only 0.4731 vs 0.6205 on the 16x37 live size, the full one 0.7238 vs 0.7491 on a
/// 5x7 week).</param>
/// <param name="MaxTotalEvaluations">Deterministic upper bound of batch evaluations over all passes; later passes
/// get what is left. Sized so that it, not the wall clock, ends the search: the benchmark measured about 40 000
/// evaluations per second (Release, developer machine), so 600 000 take about 15 s there and the wall-clock
/// budget only binds on a machine more than six times slower.</param>
/// <param name="RegionRebuild">Region rebuild after the local search (second instance: global harmony); null switches
/// it off. On in <see cref="Harmonized"/>, off in <see cref="Default"/> and in explicitly constructed options.</param>
/// <param name="HarmonyTieBreak">When two passes end on the same per-row fitness, the pass with the higher global
/// harmony wins instead of the earlier one.</param>
/// <param name="HarmonyGuard">"Nothing gets worse" for the global harmony: the passes alternate in pairs between the free
/// local search and a guarded one that rejects every move taking the harmony below the pass start; a free pass may
/// only win when it ends at or above the start harmony. So a per-row gain is never bought by breaking the rhythm
/// between the rows, and the guard costs no fitness where the free search keeps the harmony anyway. On in
/// <see cref="Harmonized"/>.</param>
public sealed record DeterministicSearchOptions(
    int MaxIterations,
    int MaxNoImprovementIterations,
    int PairPoolCap,
    int TabuTenure,
    int Seed,
    TimeSpan WallClockBudget,
    long MaxEvaluations,
    int Restarts = DeterministicSearchOptions.DefaultRestarts,
    bool IncludeAllSameDaySwaps = false,
    bool AlternateNeighbourhoods = DeterministicSearchOptions.DefaultAlternateNeighbourhoods,
    long MaxTotalEvaluations = DeterministicSearchOptions.DefaultMaxTotalEvaluations,
    RegionRebuildOptions? RegionRebuild = null,
    bool HarmonyTieBreak = DeterministicSearchOptions.DefaultHarmonyTieBreak,
    bool HarmonyGuard = false)
{
    public const bool DefaultHarmonyTieBreak = true;

    public const int DefaultMaxIterations = 200;
    public const int DefaultMaxNoImprovementIterations = 5;
    public const int DefaultPairPoolCap = 100;
    public const int DefaultTabuTenure = 7;
    public const int DefaultSeed = 42;
    public const int DefaultWallClockBudgetSeconds = 90;
    public const long DefaultMaxEvaluations = 150_000;
    public const long DefaultMaxTotalEvaluations = 600_000;
    public const int DefaultRestarts = 8;
    public const bool DefaultAlternateNeighbourhoods = true;

    public static DeterministicSearchOptions Default { get; } = new(
        DefaultMaxIterations,
        DefaultMaxNoImprovementIterations,
        DefaultPairPoolCap,
        DefaultTabuTenure,
        DefaultSeed,
        TimeSpan.FromSeconds(DefaultWallClockBudgetSeconds),
        DefaultMaxEvaluations,
        DefaultRestarts);

    /// <summary>
    /// The production setting of Wizard 3: <see cref="Default"/> plus the second instance - the harmony guard keeps the
    /// local search from lowering the global harmony, and the region rebuild raises it afterwards without lowering the
    /// per-row fitness. <see cref="Default"/> stays the plain local search the search contracts are written against.
    /// </summary>
    public static DeterministicSearchOptions Harmonized { get; } = Default with
    {
        RegionRebuild = RegionRebuildOptions.Default,
        HarmonyGuard = true,
    };
}
