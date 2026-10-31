// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

/// <summary>
/// Limits of the region rebuild that runs after the deterministic local search. All limits are deterministic except the
/// wall-clock safety net; with the same input and seed the phase produces the same plan unless that net is hit.
/// </summary>
/// <param name="Rounds">Upper bound of rebuild rounds (iterated local search: each round starts from the best plan so far).</param>
/// <param name="RegionsPerRound">Regions proposed and rebuilt per round; their results are combined by the synthesis.</param>
/// <param name="RowsPerRegion">Rows of one region (the cells of a day are rearranged among these rows).</param>
/// <param name="MinDays">Shortest day window of a region.</param>
/// <param name="MaxDays">Longest day window of a region.</param>
/// <param name="BeamWidth">States kept per day while rebuilding one region.</param>
/// <param name="MaxStaleRounds">Consecutive rounds without a harmony gain before the phase stops.</param>
/// <param name="Seed">Seed of the region proposer.</param>
/// <param name="WallClockBudgetSeconds">Safety-net time budget of the whole phase.</param>
/// <param name="ScreenedRegions">
/// Coarse-to-fine region choice: regions added per round on top of the plain <paramref name="RegionsPerRound"/>. The
/// proposer draws <c>ScreenedRegions x ScreeningFactor</c> further regions, scores each on the coarse level only
/// (<see cref="RegionProposer.CoarseGain"/>: how many cells could be brought onto the canon by rearranging the day's
/// cells among the region rows, rules ignored) and adds the best ones to the expensive beam rebuild. 0 keeps the plain
/// proposer exactly. Default 8 (with factor 16): measured on scenarios 4 and 5 and on the region-rebuild fixture.
/// </param>
/// <param name="ScreeningFactor">Pool size per screened region (see <paramref name="ScreenedRegions"/>).</param>
public sealed record RegionRebuildOptions(
    int Rounds = RegionRebuildOptions.DefaultRounds,
    int RegionsPerRound = RegionRebuildOptions.DefaultRegionsPerRound,
    int RowsPerRegion = RegionRebuildOptions.DefaultRowsPerRegion,
    int MinDays = RegionRebuildOptions.DefaultMinDays,
    int MaxDays = RegionRebuildOptions.DefaultMaxDays,
    int BeamWidth = RegionRebuildOptions.DefaultBeamWidth,
    int MaxStaleRounds = RegionRebuildOptions.DefaultMaxStaleRounds,
    int Seed = RegionRebuildOptions.DefaultSeed,
    int WallClockBudgetSeconds = RegionRebuildOptions.DefaultWallClockBudgetSeconds,
    int ScreenedRegions = RegionRebuildOptions.DefaultScreenedRegions,
    int ScreeningFactor = RegionRebuildOptions.DefaultScreeningFactor)
{
    public const int DefaultRounds = 6;
    public const int DefaultRegionsPerRound = 8;
    public const int DefaultRowsPerRegion = 3;
    public const int DefaultMinDays = 4;
    public const int DefaultMaxDays = 7;
    public const int DefaultBeamWidth = 24;
    public const int DefaultMaxStaleRounds = 2;
    public const int DefaultSeed = 42;
    public const int DefaultWallClockBudgetSeconds = 30;
    public const int DefaultScreenedRegions = 8;
    public const int DefaultScreeningFactor = 16;

    public static RegionRebuildOptions Default { get; } = new();
}
