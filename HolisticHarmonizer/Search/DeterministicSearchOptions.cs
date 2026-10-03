// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

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
/// <param name="MaxEvaluations">Deterministic upper bound of batch evaluations per pass; sized to bind
/// before the wall-clock budget on slow machines.</param>
/// <param name="Restarts">Independent passes from the same start plan with different tie-breaking (pass 0 = first
/// best in candidate order, pass k = Random(Seed + k)); the best pass wins. MaxEvaluations applies per pass.</param>
/// <param name="IncludeAllSameDaySwaps">Adds every hard-valid same-day swap to the candidate pool neighbourhood
/// in every pass (much larger and slower; off by default).</param>
/// <param name="AlternateNeighbourhoods">Even passes search the pool neighbourhood, odd passes the pool plus every
/// hard-valid same-day swap; the best pass wins (never below the first in-order pass of either neighbourhood).
/// On by default: in the stage-3 benchmark each neighbourhood alone
/// lost on some scenarios (pool-only 0.4731 vs 0.6205 on the 16x37 live size, the full one 0.7238 vs 0.7491 on a
/// 5x7 week).</param>
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
    bool AlternateNeighbourhoods = DeterministicSearchOptions.DefaultAlternateNeighbourhoods)
{
    public const int DefaultMaxIterations = 200;
    public const int DefaultMaxNoImprovementIterations = 5;
    public const int DefaultPairPoolCap = 100;
    public const int DefaultTabuTenure = 7;
    public const int DefaultSeed = 42;
    public const int DefaultWallClockBudgetSeconds = 75;
    public const long DefaultMaxEvaluations = 150_000;
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
}
