// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Scorer;

/// <summary>
/// Extracts harmony features from a single bitmap row. A "work block" is a contiguous run of
/// non-Free cells; a "rest period" is a contiguous run of Free cells *between* two blocks
/// (leading and trailing free time is excluded from the variance calculation).
/// </summary>
public static class RowFeatureExtractor
{
    public static RowFeatures Extract(HarmonyBitmap bitmap, int rowIndex)
    {
        var blocks = ScanBlocks(bitmap, rowIndex);
        if (blocks.Count == 0)
        {
            return new RowFeatures(1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0);
        }

        var blockSizeUniformity = Uniformity(blocks.ConvertAll(b => (double)b.Length));
        var restPeriods = InterBlockRestLengths(blocks);
        var restUniformity = restPeriods.Count == 0 ? 1.0 : Uniformity(restPeriods);
        var blockHomogeneity = ComputeBlockHomogeneity(bitmap, rowIndex, blocks);
        var transitionCompliance = ComputeTransitionCompliance(bitmap, rowIndex, blocks);
        var shiftTypeRotation = ComputeShiftTypeRotation(bitmap, rowIndex, blocks);
        var preferredShiftFraction = ComputePreferredShiftFraction(bitmap, rowIndex);

        return new RowFeatures(
            blockSizeUniformity,
            restUniformity,
            blockHomogeneity,
            transitionCompliance,
            shiftTypeRotation,
            preferredShiftFraction,
            blocks.Count);
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
            return 1.0;
        }

        var workCells = 0;
        var preferredCells = 0;
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var symbol = bitmap.GetCell(rowIndex, d).Symbol;
            if (symbol == CellSymbol.Free)
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
            if (symbol != CellSymbol.Free)
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

    private static double ComputeBlockHomogeneity(HarmonyBitmap bitmap, int rowIndex, List<Block> blocks)
    {
        var homogeneous = 0;
        foreach (var block in blocks)
        {
            if (IsBlockHomogeneous(bitmap, rowIndex, block))
            {
                homogeneous++;
            }
        }
        return (double)homogeneous / blocks.Count;
    }

    private static bool IsBlockHomogeneous(HarmonyBitmap bitmap, int rowIndex, Block block)
    {
        var first = bitmap.GetCell(rowIndex, block.StartDay).Symbol;
        for (var d = block.StartDay + 1; d <= block.EndDay; d++)
        {
            if (bitmap.GetCell(rowIndex, d).Symbol != first)
            {
                return false;
            }
        }
        return true;
    }

    private static double ComputeTransitionCompliance(HarmonyBitmap bitmap, int rowIndex, List<Block> blocks)
    {
        if (blocks.Count <= 1)
        {
            return 1.0;
        }

        var totalChanges = 0;
        var compliant = 0;
        for (var i = 1; i < blocks.Count; i++)
        {
            var fromSymbol = DominantSymbol(bitmap, rowIndex, blocks[i - 1]);
            var toSymbol = DominantSymbol(bitmap, rowIndex, blocks[i]);
            if (fromSymbol == toSymbol)
            {
                continue;
            }
            if (!IsScorableSymbol(fromSymbol) || !IsScorableSymbol(toSymbol))
            {
                continue;
            }

            totalChanges++;
            if ((byte)toSymbol > (byte)fromSymbol)
            {
                compliant++;
            }
        }

        if (totalChanges == 0)
        {
            return 1.0;
        }
        return (double)compliant / totalChanges;
    }

    private static bool IsScorableSymbol(CellSymbol symbol)
    {
        return symbol == CellSymbol.Early || symbol == CellSymbol.Late || symbol == CellSymbol.Night;
    }

    private static CellSymbol DominantSymbol(HarmonyBitmap bitmap, int rowIndex, Block block)
    {
        Span<int> counts = stackalloc int[5];
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
