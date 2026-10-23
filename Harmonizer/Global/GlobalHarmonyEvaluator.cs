// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Global;

/// <summary>
/// Second instance of the harmonizer: scores the harmony BETWEEN the rows of a plan (<see cref="GlobalHarmonyScore"/>).
/// It never decides whether a plan is allowed - the rules and the per-row score do that - it only ranks plans the rules
/// already admit. Rhythm comes from <see cref="RhythmMatrix"/>; fairness is the evenness (1 - mean coefficient of
/// variation) of each working row's share of early, late, night and weekend days among its own worked days.
/// </summary>
public sealed class GlobalHarmonyEvaluator
{
    public const double RhythmWeight = 0.6;
    public const double FairnessWeight = 0.4;

    public GlobalHarmonyScore Evaluate(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return Evaluate(bitmap, new RhythmMatrix(bitmap));
    }

    /// <summary>Scores with a rhythm matrix the caller keeps in step with <paramref name="bitmap"/>.</summary>
    public GlobalHarmonyScore Evaluate(HarmonyBitmap bitmap, RhythmMatrix rhythm)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(rhythm);
        var rhythmValue = rhythm.Rhythm;
        var fairness = Fairness(bitmap);
        return new GlobalHarmonyScore(RhythmWeight * rhythmValue + FairnessWeight * fairness, rhythmValue, fairness);
    }

    public static double Fairness(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var early = new List<double>();
        var late = new List<double>();
        var night = new List<double>();
        var weekend = new List<double>();
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            int worked = 0, e = 0, l = 0, n = 0, w = 0;
            for (var d = 0; d < bitmap.DayCount; d++)
            {
                var symbol = bitmap.GetCell(r, d).Symbol;
                if (symbol is not (CellSymbol.Early or CellSymbol.Late or CellSymbol.Night or CellSymbol.Other))
                {
                    continue;
                }

                worked++;
                e += symbol == CellSymbol.Early ? 1 : 0;
                l += symbol == CellSymbol.Late ? 1 : 0;
                n += symbol == CellSymbol.Night ? 1 : 0;
                w += bitmap.Days[d].DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1 : 0;
            }

            if (worked == 0)
            {
                continue;
            }

            early.Add((double)e / worked);
            late.Add((double)l / worked);
            night.Add((double)n / worked);
            weekend.Add((double)w / worked);
        }

        if (early.Count < 2)
        {
            return 1d;
        }

        var meanCv = (Cv(early) + Cv(late) + Cv(night) + Cv(weekend)) / 4d;
        return Math.Clamp(1d - meanCv, 0d, 1d);
    }

    private static double Cv(List<double> values)
    {
        var mean = values.Average();
        if (mean <= 0d)
        {
            return 0d;
        }

        var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        return Math.Sqrt(variance) / mean;
    }
}
