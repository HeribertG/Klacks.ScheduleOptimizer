// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Scorer;

/// <summary>
/// Extracts harmony features from a single bitmap row. A "work block" is a contiguous run of
/// working cells (Early/Late/Night/Other); Free and Break cells both interrupt a block and
/// count as rest. Break.Hours still contributes to TargetHoursDeviation (the absence is paid),
/// but Break is never part of a "block" or a preferred shift count.
/// </summary>
public static class RowFeatureExtractor
{
    public static RowFeatures Extract(HarmonyBitmap bitmap, int rowIndex)
    {
        var blocks = ScanBlocks(bitmap, rowIndex);
        var targetHoursDeviation = ComputeTargetHoursDeviation(bitmap, rowIndex);
        if (blocks.Count == 0)
        {
            return new RowFeatures(1.0, 1.0, 1.0, 1.0, 1.0, 1.0, targetHoursDeviation, 0);
        }

        var blockSizeUniformity = Uniformity(blocks.ConvertAll(b => (double)b.Length));
        var restPeriods = InterBlockRestLengths(blocks);
        var restUniformity = restPeriods.Count == 0 ? 1.0 : Uniformity(restPeriods);
        var rotationDays = BitmapRotation.DaysOf(bitmap, rowIndex);
        var blockHomogeneity = ComputeBlockHomogeneity(rotationDays);
        var transitionCompliance = ComputeTransitionCompliance(bitmap, rotationDays);
        var shiftTypeRotation = ComputeShiftTypeRotation(bitmap, rowIndex, blocks);
        var preferredShiftFraction = ComputePreferredShiftFraction(bitmap, rowIndex);

        return new RowFeatures(
            blockSizeUniformity,
            restUniformity,
            blockHomogeneity,
            transitionCompliance,
            shiftTypeRotation,
            preferredShiftFraction,
            targetHoursDeviation,
            blocks.Count);
    }

    private const double TargetHoursDeviationScale = 3.0;
    private const int CellSymbolCount = (int)CellSymbol.Break + 1;

    private static double ComputeTargetHoursDeviation(HarmonyBitmap bitmap, int rowIndex)
    {
        var target = (double)bitmap.Rows[rowIndex].TargetHours;
        if (target <= 0)
        {
            return 0.0;
        }

        var actual = 0.0;
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            actual += (double)bitmap.GetCell(rowIndex, d).Hours;
        }

        var rawDeviation = Math.Abs(actual - target) / target;
        return Math.Clamp(rawDeviation * TargetHoursDeviationScale, 0.0, 1.0);
    }

    private static double ComputeShiftTypeRotation(HarmonyBitmap bitmap, int rowIndex, List<Block> blocks)
    {
        if (blocks.Count < 2)
        {
            return 1.0;
        }

        Span<int> counts = stackalloc int[3];
        var totalScorable = 0;
        foreach (var block in blocks)
        {
            var dominant = DominantSymbol(bitmap, rowIndex, block);
            if (dominant == CellSymbol.Early) { counts[0]++; totalScorable++; }
            else if (dominant == CellSymbol.Late) { counts[1]++; totalScorable++; }
            else if (dominant == CellSymbol.Night) { counts[2]++; totalScorable++; }
        }

        if (totalScorable < 2)
        {
            return 1.0;
        }

        var distinctClasses = 0;
        var presentValues = new List<double>(3);
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] > 0)
            {
                distinctClasses++;
                presentValues.Add(counts[i]);
            }
        }

        if (distinctClasses == 1)
        {
            return 0.0;
        }
        return Uniformity(presentValues);
    }

    private static double ComputePreferredShiftFraction(HarmonyBitmap bitmap, int rowIndex)
    {
        var preferred = bitmap.Rows[rowIndex].PreferredShiftSymbols;
        if (preferred is null || preferred.Count == 0)
        {
            return 0.5;
        }

        var workCells = 0;
        var preferredCells = 0;
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var symbol = bitmap.GetCell(rowIndex, d).Symbol;
            if (!IsWorkSymbol(symbol))
            {
                continue;
            }
            workCells++;
            if (preferred.Contains(symbol))
            {
                preferredCells++;
            }
        }

        if (workCells == 0)
        {
            return 1.0;
        }
        return (double)preferredCells / workCells;
    }

    private static List<Block> ScanBlocks(HarmonyBitmap bitmap, int rowIndex)
    {
        var blocks = new List<Block>();
        var blockStart = -1;
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var symbol = bitmap.GetCell(rowIndex, d).Symbol;
            if (IsWorkSymbol(symbol))
            {
                if (blockStart < 0)
                {
                    blockStart = d;
                }
                continue;
            }

            if (blockStart >= 0)
            {
                blocks.Add(new Block(blockStart, d - 1));
                blockStart = -1;
            }
        }

        if (blockStart >= 0)
        {
            blocks.Add(new Block(blockStart, bitmap.DayCount - 1));
        }
        return blocks;
    }

    private static bool IsWorkSymbol(CellSymbol symbol)
    {
        return symbol != CellSymbol.Free && symbol != CellSymbol.Break;
    }

    private static List<double> InterBlockRestLengths(List<Block> blocks)
    {
        if (blocks.Count <= 1)
        {
            return [];
        }

        var rests = new List<double>(blocks.Count - 1);
        for (var i = 1; i < blocks.Count; i++)
        {
            var rest = blocks[i].StartDay - blocks[i - 1].EndDay - 1;
            rests.Add(rest);
        }
        return rests;
    }

    /// <summary>
    /// Share of pure blocks under the rotation rule's block definition (<see cref="ShiftRotation"/>: a block ends only
    /// after 48 h of rest), so one free day between two kinds no longer passes as two pure blocks.
    /// </summary>
    private static double ComputeBlockHomogeneity(IReadOnlyList<RotationDay> rotationDays)
    {
        var rotationBlocks = ShiftRotation.BuildBlocks(rotationDays);
        if (rotationBlocks.Count == 0)
        {
            return 1.0;
        }

        var homogeneous = rotationBlocks.Count(block => block.All(day =>
            day.FirstKindIndex == block[0].FirstKindIndex && day.LastKindIndex == block[0].FirstKindIndex));
        return (double)homogeneous / rotationBlocks.Count;
    }

    /// <summary>
    /// Share of block changes that go to the ideal successor of SPEC-ROTATION-2026-10-08 (early, late, night, early;
    /// restart at early after a long pause), read through <see cref="BitmapRotation"/> and its known gaps.
    /// </summary>
    private static double ComputeTransitionCompliance(HarmonyBitmap bitmap, IReadOnlyList<RotationDay> rotationDays)
    {
        var assessment = BitmapRotation.Assess(bitmap, rotationDays);
        return assessment.Transitions == 0
            ? 1.0
            : 1.0 - ((double)assessment.NonIdealTransitions / assessment.Transitions);
    }

    private static CellSymbol DominantSymbol(HarmonyBitmap bitmap, int rowIndex, Block block)
    {
        Span<int> counts = stackalloc int[CellSymbolCount];
        for (var d = block.StartDay; d <= block.EndDay; d++)
        {
            counts[(int)bitmap.GetCell(rowIndex, d).Symbol]++;
        }

        var bestIndex = 0;
        var bestCount = -1;
        for (var i = 1; i < counts.Length; i++)
        {
            if (counts[i] > bestCount)
            {
                bestCount = counts[i];
                bestIndex = i;
            }
        }
        return (CellSymbol)bestIndex;
    }

    private static double Uniformity(List<double> values)
    {
        if (values.Count <= 1)
        {
            return 1.0;
        }

        var mean = 0.0;
        foreach (var v in values)
        {
            mean += v;
        }
        mean /= values.Count;
        if (mean <= 0)
        {
            return 1.0;
        }

        var sumSq = 0.0;
        foreach (var v in values)
        {
            var d = v - mean;
            sumSq += d * d;
        }
        var stddev = Math.Sqrt(sumSq / values.Count);
        var cv = stddev / mean;
        return Math.Clamp(1.0 - cv, 0.0, 1.0);
    }

    private readonly record struct Block(int StartDay, int EndDay)
    {
        public int Length => EndDay - StartDay + 1;
    }
}
