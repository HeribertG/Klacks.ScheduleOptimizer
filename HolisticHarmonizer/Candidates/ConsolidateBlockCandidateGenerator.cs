// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Llm;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;

/// <summary>
/// Suggests same-day swaps that fill a free gap day inside a fragmented row by moving a
/// neighbour's work shift onto that gap. Each candidate pairs (rowA = fragmented row, dayA
/// = gap day, blank cell) with (rowB = a colleague who works on the same day, swap target).
/// The expected benefit ranks gaps that connect two existing blocks — the bigger the
/// resulting contiguous run, the higher the rank.
/// </summary>
public sealed class ConsolidateBlockCandidateGenerator : IMoveCandidateGenerator
{
    public string Intent => HolisticIntent.ConsolidateBlock;

    public IEnumerable<MoveCandidate> Generate(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        for (var row = 0; row < bitmap.RowCount; row++)
        {
            var gaps = FindGapDays(bitmap, row);
            if (gaps.Count == 0)
            {
                continue;
            }

            foreach (var gap in gaps)
            {
                if (bitmap.GetCell(row, gap.Day).IsLocked)
                {
                    continue;
                }

                for (var partner = 0; partner < bitmap.RowCount; partner++)
                {
                    if (partner == row)
                    {
                        continue;
                    }
                    var partnerCell = bitmap.GetCell(partner, gap.Day);
                    if (partnerCell.IsLocked || !IsWork(partnerCell.Symbol))
                    {
                        continue;
                    }

                    var hint = string.Format(
                        CultureInfo.InvariantCulture,
                        "fills r{0:D2} gap on day {1} (extends block to ~{2} days)",
                        row, gap.Day, gap.MergedLength);

                    yield return new MoveCandidate(
                        RowA: row,
                        DayA: gap.Day,
                        RowB: partner,
                        DayB: gap.Day,
                        Hint: hint,
                        ExpectedBenefit: gap.MergedLength);
                }
            }
        }
    }

    private static List<GapDay> FindGapDays(HarmonyBitmap bitmap, int row)
    {
        var blocks = new List<(int Start, int End)>();
        int? blockStart = null;
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var isWork = IsWork(bitmap.GetCell(row, d).Symbol);
            if (isWork && blockStart is null)
            {
                blockStart = d;
            }
            else if (!isWork && blockStart is not null)
            {
                blocks.Add((blockStart.Value, d - 1));
                blockStart = null;
            }
        }
        if (blockStart is not null)
        {
            blocks.Add((blockStart.Value, bitmap.DayCount - 1));
        }

        var gaps = new List<GapDay>();
        for (var i = 0; i < blocks.Count - 1; i++)
        {
            var leftLen = blocks[i].End - blocks[i].Start + 1;
            var rightLen = blocks[i + 1].End - blocks[i + 1].Start + 1;
            for (var d = blocks[i].End + 1; d < blocks[i + 1].Start; d++)
            {
                if (bitmap.GetCell(row, d).Symbol != CellSymbol.Free)
                {
                    continue;
                }
                gaps.Add(new GapDay(d, leftLen + rightLen + 1));
            }
        }
        return gaps;
    }

    private static bool IsWork(CellSymbol symbol)
        => symbol == CellSymbol.Early
        || symbol == CellSymbol.Late
        || symbol == CellSymbol.Night
        || symbol == CellSymbol.Other;

    private readonly record struct GapDay(int Day, int MergedLength);
}
