// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Scorer;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Computes a single global fitness value for a bitmap by combining per-row harmony scores
/// with linearly decreasing weights so upper rows dominate the result. The weighted average
/// stays in [0,1] regardless of row count.
/// </summary>
public sealed class HarmonyFitnessEvaluator
{
    private readonly HarmonyScorer _scorer;

    public HarmonyFitnessEvaluator(HarmonyScorer scorer)
    {
        _scorer = scorer;
    }

    public FitnessResult Evaluate(HarmonyBitmap bitmap)
    {
        if (bitmap.RowCount == 0)
        {
            return new FitnessResult(1.0, []);
        }

        var rowScores = new double[bitmap.RowCount];
        var weightedSum = 0.0;
        var weightTotal = 0.0;
        var rowCount = bitmap.RowCount;

        for (var r = 0; r < rowCount; r++)
        {
            var rowScore = _scorer.Score(bitmap, r).Score;
            rowScores[r] = rowScore;
            var weight = (double)(rowCount - r);
            weightedSum += weight * rowScore;
            weightTotal += weight;
        }

        var fitness = weightTotal > 0 ? weightedSum / weightTotal : 0.0;
        return new FitnessResult(fitness, rowScores);
    }
}

/// <param name="Fitness">Weighted average of per-row scores, in [0,1]; higher is better</param>
/// <param name="RowScores">Individual harmony scores per row, in input order</param>
public sealed record FitnessResult(double Fitness, IReadOnlyList<double> RowScores);
