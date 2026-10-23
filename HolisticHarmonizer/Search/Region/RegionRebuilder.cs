// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.Harmonizer.Global;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

/// <summary>
/// Rebuilds one region by a beam search over its days: on each day the unlocked cells of the region rows are permuted
/// among those rows (locked cells stay where they are), so every day keeps exactly its cells and the coverage is
/// untouched. States are ranked by per-row fitness plus global harmony; each finished state then has to pass
/// <see cref="RegionAdmission"/> (first instance) and raise the global harmony (second instance). The best admitted
/// state is returned as a <see cref="RegionCandidate"/>, or null when no rebuild is admitted.
/// </summary>
/// <param name="admission">First instance (rules, target hours, per-row fitness floor).</param>
/// <param name="harmony">Second instance (global harmony).</param>
/// <param name="beamWidth">States kept per day.</param>
public sealed class RegionRebuilder
{
    public const string Intent = "region_rebuild";
    private const double Epsilon = 1e-9;
    private const double HarmonyRankWeight = 0.5;

    private readonly RegionAdmission _admission;
    private readonly GlobalHarmonyEvaluator _harmony;
    private readonly int _beamWidth;

    public RegionRebuilder(RegionAdmission admission, GlobalHarmonyEvaluator harmony, int beamWidth)
    {
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _harmony = harmony ?? throw new ArgumentNullException(nameof(harmony));
        _beamWidth = Math.Max(1, beamWidth);
    }

    public RegionCandidate? Rebuild(
        HarmonyBitmap baseBitmap,
        RhythmMatrix baseRhythm,
        double fitnessFloor,
        GlobalHarmonyScore baseHarmony,
        HarmonyRegion region,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseBitmap);
        ArgumentNullException.ThrowIfNull(baseRhythm);
        ArgumentNullException.ThrowIfNull(region);

        var rows = region.Rows;
        var beam = new List<BeamState> { new(BitmapCloner.Clone(baseBitmap), baseRhythm.Clone(), 0d) };
        for (var day = region.DayFrom; day <= region.DayTo; day++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = new List<BeamState>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var unlocked = rows.Where(r => !baseBitmap.GetCell(r, day).IsLocked).ToArray();
            var cells = unlocked.Select(r => baseBitmap.GetCell(r, day)).ToArray();
            foreach (var state in beam)
            {
                foreach (var order in DistinctOrders(cells))
                {
                    var bitmap = BitmapCloner.Clone(state.Bitmap);
                    for (var i = 0; i < unlocked.Length; i++)
                    {
                        bitmap.SetCell(unlocked[i], day, cells[order[i]]);
                    }

                    if (!DaysValid(bitmap, rows, region, day))
                    {
                        continue;
                    }

                    var key = RegionKey(bitmap, rows, region.DayFrom, day);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    var rhythm = state.Rhythm.Clone();
                    rhythm.Refresh(bitmap, unlocked);
                    var rank = _admission.Fitness(bitmap) + HarmonyRankWeight * _harmony.Evaluate(bitmap, rhythm).Total;
                    next.Add(new BeamState(bitmap, rhythm, rank));
                }
            }

            if (next.Count == 0)
            {
                return null;
            }

            beam = next.OrderByDescending(s => s.Rank).Take(_beamWidth).ToList();
        }

        RegionCandidate? best = null;
        foreach (var state in beam)
        {
            var steps = BuildSteps(baseBitmap, state.Bitmap, region);
            var fitness = _admission.Admit(baseBitmap, state.Bitmap, rows, steps, fitnessFloor);
            if (fitness is null)
            {
                continue;
            }

            var harmony = _harmony.Evaluate(state.Bitmap, state.Rhythm);
            if (harmony.Total > baseHarmony.Total + Epsilon && (best is null || harmony.Total > best.Harmony.Total + Epsilon))
            {
                best = new RegionCandidate(region, steps, state.Bitmap, state.Rhythm, fitness.Value, harmony);
            }
        }

        return best;
    }

    /// <summary>
    /// Same-day swaps that rearrange the base plan into the target plan, day by day (selection sort over the region
    /// rows, comparing cell identity). Applying them in order to <paramref name="baseBitmap"/> yields the target.
    /// </summary>
    public static IReadOnlyList<PlanCellSwap> BuildSteps(HarmonyBitmap baseBitmap, HarmonyBitmap target, HarmonyRegion region)
    {
        var steps = new List<PlanCellSwap>();
        var rows = region.Rows;
        for (var day = region.DayFrom; day <= region.DayTo; day++)
        {
            var current = rows.Select(r => baseBitmap.GetCell(r, day)).ToArray();
            for (var i = 0; i < rows.Count; i++)
            {
                var wanted = target.GetCell(rows[i], day);
                if (ReferenceEquals(current[i], wanted))
                {
                    continue;
                }

                for (var j = i + 1; j < rows.Count; j++)
                {
                    if (ReferenceEquals(current[j], wanted))
                    {
                        (current[i], current[j]) = (current[j], current[i]);
                        steps.Add(new PlanCellSwap(rows[i], day, rows[j], day, Intent));
                        break;
                    }
                }
            }
        }

        return steps;
    }

    private bool DaysValid(HarmonyBitmap bitmap, IReadOnlyList<int> rows, HarmonyRegion region, int day)
    {
        // A cell is final once both neighbours are placed: the previous day now, the last day at the end.
        if (day > region.DayFrom && rows.Any(r => !_admission.CellValid(bitmap, r, day - 1)))
        {
            return false;
        }

        return day != region.DayTo || rows.All(r => _admission.CellValid(bitmap, r, day));
    }

    private static IEnumerable<int[]> DistinctOrders(Cell[] cells)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in Permutations(Enumerable.Range(0, cells.Length).ToArray(), 0))
        {
            var signature = string.Join('|', order.Select(i => Signature(cells[i])));
            if (seen.Add(signature))
            {
                yield return (int[])order.Clone();
            }
        }
    }

    private static IEnumerable<int[]> Permutations(int[] items, int start)
    {
        if (start >= items.Length - 1)
        {
            yield return items;
            yield break;
        }

        for (var i = start; i < items.Length; i++)
        {
            (items[start], items[i]) = (items[i], items[start]);
            foreach (var permutation in Permutations(items, start + 1))
            {
                yield return permutation;
            }

            (items[start], items[i]) = (items[i], items[start]);
        }
    }

    private static string Signature(Cell cell)
        => $"{(int)cell.Symbol}:{cell.ShiftRefId}:{cell.StartAt.Ticks}:{cell.EndAt.Ticks}:{cell.Hours}";

    private static string RegionKey(HarmonyBitmap bitmap, IReadOnlyList<int> rows, int dayFrom, int dayTo)
    {
        var parts = new List<string>();
        foreach (var r in rows)
        {
            for (var d = dayFrom; d <= dayTo; d++)
            {
                parts.Add(Signature(bitmap.GetCell(r, d)));
            }
        }

        return string.Join(';', parts);
    }

    private sealed record BeamState(HarmonyBitmap Bitmap, RhythmMatrix Rhythm, double Rank);
}
