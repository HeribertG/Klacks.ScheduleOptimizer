// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Global;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

/// <summary>
/// Deterministic region proposer: starts each region at a row that fits the plan's canon worst (lowest
/// <see cref="RhythmMatrix.RowFit"/>), adds other rows and a day window drawn from the seeded random source. It is the
/// "where to look" step; any other proposer (for example a vision model) can feed the same rebuild.
/// <para>
/// With <see cref="RegionRebuildOptions.ScreenedRegions"/> above 0 the choice also runs coarse to fine: a larger pool
/// is drawn, each region is scored by <see cref="CoarseGain"/> (cheap, symbol level, rules ignored) and the most
/// promising ones are added to the plain regions for the expensive beam rebuild, which still checks every rule. The
/// coarse score never admits anything; it only decides where the fine search spends its extra budget.
/// </para>
/// </summary>
public static class RegionProposer
{
    public static IReadOnlyList<HarmonyRegion> Propose(
        HarmonyBitmap bitmap,
        RhythmMatrix rhythm,
        RegionRebuildOptions options,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(rhythm);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rng);

        var plain = Draw(bitmap, rhythm, options, options.RegionsPerRound, rng);
        if (options.ScreenedRegions <= 0 || options.ScreeningFactor <= 0 || plain.Count == 0)
        {
            return plain;
        }

        // Coarse-to-fine: draw a larger pool, score it on the coarse level only and add the most promising regions
        // to the plain ones, so every round still rebuilds everything the plain proposer would.
        var pool = Draw(bitmap, rhythm, options, options.ScreenedRegions * options.ScreeningFactor, rng);
        var expected = Enumerable.Range(0, bitmap.RowCount).Select(rhythm.ExpectedRow).ToArray();
        var ranked = pool
            .Select((region, index) => (Region: region, Index: index, Gain: CoarseGain(bitmap, expected, region),
                Misfit: region.Rows.Sum(r => 1d - rhythm.RowFit(r))))
            .Where(x => x.Gain > 0)
            .OrderByDescending(x => x.Gain)
            .ThenByDescending(x => x.Misfit)
            .ThenBy(x => x.Index)
            .ToList();

        // Prefer screened regions that do not touch each other: the synthesis can only combine non-touching rebuilds.
        var screened = new List<HarmonyRegion>(options.ScreenedRegions);
        foreach (var candidate in ranked)
        {
            if (screened.Count < options.ScreenedRegions && !screened.Any(c => c.Touches(candidate.Region)))
            {
                screened.Add(candidate.Region);
            }
        }

        foreach (var candidate in ranked)
        {
            if (screened.Count < options.ScreenedRegions && !screened.Contains(candidate.Region))
            {
                screened.Add(candidate.Region);
            }
        }

        return [.. plain, .. screened];
    }

    /// <summary>
    /// Coarse level of a region: per day, how many more of the region's unlocked cells could sit where the canon
    /// expects their symbol if the day's cells were rearranged among the region rows (a bipartite count per symbol:
    /// min(cells of the symbol, rows expecting it)), minus how many already do. Rules, rest times and hours are ignored;
    /// the beam rebuild checks them. 0 means the canon cannot be served better inside this region.
    /// </summary>
    /// <param name="bitmap">Current plan</param>
    /// <param name="expected">Per row the canon's expected symbol per day (<see cref="RhythmMatrix.ExpectedRow"/>)</param>
    /// <param name="region">Region to score</param>
    public static int CoarseGain(HarmonyBitmap bitmap, IReadOnlyList<byte[]?> expected, HarmonyRegion region)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(region);

        var gain = 0;
        var cells = new int[byte.MaxValue + 1];
        var wants = new int[byte.MaxValue + 1];
        for (var d = region.DayFrom; d <= region.DayTo; d++)
        {
            Array.Clear(cells);
            Array.Clear(wants);
            var current = 0;
            foreach (var r in region.Rows)
            {
                var cell = bitmap.GetCell(r, d);
                var want = expected[r]?[d];
                if (cell.IsLocked || want is null or RhythmMatrix.Wildcard)
                {
                    continue;
                }

                var symbol = (byte)cell.Symbol;
                cells[symbol]++;
                wants[want.Value]++;
                current += symbol == want.Value ? 1 : 0;
            }

            var best = 0;
            for (var s = 0; s < cells.Length; s++)
            {
                best += Math.Min(cells[s], wants[s]);
            }

            gain += best - current;
        }

        return gain;
    }

    private static List<HarmonyRegion> Draw(
        HarmonyBitmap bitmap,
        RhythmMatrix rhythm,
        RegionRebuildOptions options,
        int count,
        Random rng)
    {

        var rowsPerRegion = Math.Min(options.RowsPerRegion, bitmap.RowCount);
        var minDays = Math.Min(options.MinDays, bitmap.DayCount);
        var maxDays = Math.Min(Math.Max(options.MaxDays, minDays), bitmap.DayCount);
        if (rowsPerRegion < 2 || minDays < 1)
        {
            return [];
        }


        var byFit = Enumerable.Range(0, bitmap.RowCount)
            .OrderBy(rhythm.RowFit)
            .ThenBy(r => r)
            .ToArray();
        var regions = new List<HarmonyRegion>(count);
        for (var k = 0; k < count; k++)
        {
            var anchor = byFit[k % byFit.Length];
            var partners = Enumerable.Range(0, bitmap.RowCount)
                .Where(r => r != anchor)
                .Select(r => (Row: r, Key: rng.Next()))
                .OrderBy(x => x.Key)
                .Take(rowsPerRegion - 1)
                .Select(x => x.Row);
            var rows = partners.Append(anchor).OrderBy(r => r).ToArray();
            var length = rng.Next(minDays, maxDays + 1);
            var start = rng.Next(0, bitmap.DayCount - length + 1);
            regions.Add(new HarmonyRegion(rows, start, start + length - 1));
        }

        return regions;
    }
}
