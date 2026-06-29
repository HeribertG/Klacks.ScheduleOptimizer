// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Scorer;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Research fitness that augments the per-row fuzzy harmony with GLOBAL schedule-quality terms the
/// per-row scorer is structurally blind to: block consolidation (fewer, longer blocks) and
/// consecutive-day legality. Cross-row FAIRNESS is deliberately EXCLUDED so it can serve as a
/// held-out generalisation probe in the alignment study. Higher fitness is better; the value stays
/// in [0,1]. Not wired into production — a mechanism study against <see cref="HarmonyFitnessEvaluator"/>.
/// </summary>
/// <param name="scorer">The live per-row Mamdani harmony scorer reused for the harmony base term</param>
public sealed class AlignedHarmonyFitnessEvaluator : IBitmapFitnessEvaluator
{
    private const double HarmonyWeight = 0.40;
    private const double BlockLengthWeight = 0.25;
    private const double FragmentationWeight = 0.25;
    private const double LegalityWeight = 0.10;
    private const double IdealBlockLength = 5.0;
    private const int DefaultMaxConsecutiveDays = 6;

    private readonly HarmonyScorer _scorer;

    public AlignedHarmonyFitnessEvaluator(HarmonyScorer scorer)
    {
        _scorer = scorer;
    }

    public FitnessResult Evaluate(HarmonyBitmap bitmap)
    {
        if (bitmap.RowCount == 0)
        {
            return new FitnessResult(1.0, []);
        }

        var rowCount = bitmap.RowCount;
        var rowScores = new double[rowCount];
        var harmonyWeightedSum = 0.0;
        var harmonyWeightTotal = 0.0;

        var totalBlocks = 0;
        var totalWorkDays = 0;
        var totalViolations = 0;

        for (var r = 0; r < rowCount; r++)
        {
            rowScores[r] = _scorer.Score(bitmap, r).Score;
            var weight = (double)(rowCount - r);
            harmonyWeightedSum += weight * rowScores[r];
            harmonyWeightTotal += weight;

            var rowStats = ComputeRowStats(bitmap, r);
            totalBlocks += rowStats.Blocks;
            totalWorkDays += rowStats.WorkDays;
            totalViolations += rowStats.Violations;
        }

        var harmony = harmonyWeightTotal > 0 ? harmonyWeightedSum / harmonyWeightTotal : 0.0;
        var avgBlockLength = totalBlocks > 0 ? (double)totalWorkDays / totalBlocks : 0.0;
        var blockLengthScore = Math.Min(1.0, avgBlockLength / IdealBlockLength);
        var fragmentationScore = totalWorkDays > 0
            ? 1.0 - Math.Min(1.0, (double)totalBlocks / totalWorkDays)
            : 1.0;
        var legalityScore = totalViolations == 0 ? 1.0 : 1.0 / (1.0 + totalViolations);

        var fitness = (HarmonyWeight * harmony)
                    + (BlockLengthWeight * blockLengthScore)
                    + (FragmentationWeight * fragmentationScore)
                    + (LegalityWeight * legalityScore);

        return new FitnessResult(fitness, rowScores);
    }

    private static RowStats ComputeRowStats(HarmonyBitmap bitmap, int rowIndex)
    {
        var agent = bitmap.Rows[rowIndex];
        var maxAllowed = agent.MaxConsecutiveDays > 0 ? agent.MaxConsecutiveDays : DefaultMaxConsecutiveDays;
        var blocks = 0;
        var workDays = 0;
        var violations = 0;
        var run = 0;

        for (var d = 0; d < bitmap.DayCount; d++)
        {
            if (bitmap.GetCell(rowIndex, d).Symbol == CellSymbol.Free)
            {
                run = 0;
                continue;
            }

            if (run == 0)
            {
                blocks++;
            }
            run++;
            workDays++;
            if (run > maxAllowed)
            {
                violations++;
            }
        }

        return new RowStats(blocks, workDays, violations);
    }

    private readonly record struct RowStats(int Blocks, int WorkDays, int Violations);
}
