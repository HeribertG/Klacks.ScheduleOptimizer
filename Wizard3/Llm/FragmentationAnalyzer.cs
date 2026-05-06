// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using System.Text;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Wizard3.Llm;

/// <summary>
/// Computes a deterministic fragmentation summary that the LLM can read as a pre-digested
/// signal next to the raw schedule grid. Empirically (2026-05-06) the text-rendered
/// bitmap alone is too dense for current LLMs (llama-3.3-70b, claude-sonnet-4.6,
/// claude-opus-4.6) to scan reliably for fragmentation; pre-computing block structure
/// and gap days lifts the perceptual load off the model.
/// </summary>
/// <param name="bitmap">Current working bitmap.</param>
public static class FragmentationAnalyzer
{
    private const int MaxTargetRowsListed = 5;

    public static string Render(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var fragmented = new List<RowFragmentation>();
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            var rowFrag = AnalyzeRow(bitmap, r);
            if (rowFrag.Blocks.Count >= 2 && rowFrag.GapDays.Count > 0)
            {
                fragmented.Add(rowFrag);
            }
        }

        if (fragmented.Count == 0)
        {
            return "FRAGMENTATION ANALYSIS: no row has 2+ work blocks separated by free gap days. Reply with {\"batches\":[]}.";
        }

        fragmented.Sort((a, b) => b.Blocks.Count.CompareTo(a.Blocks.Count));

        var sb = new StringBuilder();
        sb.AppendLine("FRAGMENTATION ANALYSIS (deterministic, pre-computed by the host):");
        foreach (var row in fragmented)
        {
            var lengths = string.Join(",", row.Blocks.Select(b => b.Length.ToString(CultureInfo.InvariantCulture)));
            var gaps = string.Join(",", row.GapDays.Select(d => d.ToString(CultureInfo.InvariantCulture)));
            sb.Append("- r");
            sb.Append(row.RowIndex.ToString("D2", CultureInfo.InvariantCulture));
            sb.Append(": ");
            sb.Append(row.Blocks.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(" work blocks (lengths ");
            sb.Append(lengths);
            sb.Append("), free gap days [");
            sb.Append(gaps);
            sb.AppendLine("]");
        }

        var topRows = string.Join(
            ", ",
            fragmented
                .Take(MaxTargetRowsListed)
                .Select(r => "r" + r.RowIndex.ToString("D2", CultureInfo.InvariantCulture)));
        sb.Append("TARGET ROWS for consolidate_block (most fragmented first): ");
        sb.AppendLine(topRows);
        sb.AppendLine("Use these rows as the primary candidates: propose batches that fill their gap days by swapping with neighbouring rows that work on those days (DayA == DayB).");
        return sb.ToString();
    }

    private static RowFragmentation AnalyzeRow(HarmonyBitmap bitmap, int rowIndex)
    {
        var blocks = new List<WorkBlock>();
        int? blockStart = null;
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var isWork = IsWork(bitmap.GetCell(rowIndex, d).Symbol);
            if (isWork && blockStart is null)
            {
                blockStart = d;
            }
            else if (!isWork && blockStart is not null)
            {
                blocks.Add(new WorkBlock(blockStart.Value, d - 1));
                blockStart = null;
            }
        }
        if (blockStart is not null)
        {
            blocks.Add(new WorkBlock(blockStart.Value, bitmap.DayCount - 1));
        }

        var gapDays = new List<int>();
        for (var i = 0; i < blocks.Count - 1; i++)
        {
            for (var d = blocks[i].EndDay + 1; d < blocks[i + 1].StartDay; d++)
            {
                if (bitmap.GetCell(rowIndex, d).Symbol == CellSymbol.Free)
                {
                    gapDays.Add(d);
                }
            }
        }

        return new RowFragmentation(rowIndex, blocks, gapDays);
    }

    private static bool IsWork(CellSymbol symbol) =>
        symbol is CellSymbol.Early or CellSymbol.Late or CellSymbol.Night or CellSymbol.Other;

    private sealed record WorkBlock(int StartDay, int EndDay)
    {
        public int Length => EndDay - StartDay + 1;
    }

    private sealed record RowFragmentation(int RowIndex, IReadOnlyList<WorkBlock> Blocks, IReadOnlyList<int> GapDays);
}
