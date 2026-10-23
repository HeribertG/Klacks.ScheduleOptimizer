// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Global;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

/// <summary>
/// Deterministic region proposer: starts each region at a row that fits the plan's canon worst (lowest
/// <see cref="RhythmMatrix.RowFit"/>), adds other rows and a day window drawn from the seeded random source. It is the
/// "where to look" step; any other proposer (for example a vision model) can feed the same rebuild.
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
        var regions = new List<HarmonyRegion>(options.RegionsPerRound);
        for (var k = 0; k < options.RegionsPerRound; k++)
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
