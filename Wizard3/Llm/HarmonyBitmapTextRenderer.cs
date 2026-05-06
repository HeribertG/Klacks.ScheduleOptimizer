// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using System.Text;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Wizard3.Llm;

/// <summary>
/// Renders a <see cref="HarmonyBitmap"/> as a plain-text grid for text-only LLMs in the Wizard 3
/// MVP. Each row is one agent, each column is one calendar day. Symbols are single-letter codes
/// (E/L/N/O/_) with an asterisk suffix marking locked cells and a B for break/absence.
/// </summary>
public static class HarmonyBitmapTextRenderer
{
    private const int RowLabelWidth = 16;
    private const int CellWidth = 5;

    public static string Render(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var sb = new StringBuilder();
        AppendHeader(sb, bitmap);
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            AppendRow(sb, bitmap, r);
        }
        AppendLegend(sb);
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, HarmonyBitmap bitmap)
    {
        sb.Append(' ', RowLabelWidth);
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var date = bitmap.Days[d];
            var label = $"{date.Day:D2}{DayCode(date.DayOfWeek)}";
            sb.Append(label.PadRight(CellWidth));
        }
        sb.AppendLine();
    }

    private static void AppendRow(StringBuilder sb, HarmonyBitmap bitmap, int rowIndex)
    {
        var agent = bitmap.Rows[rowIndex];
        var label = $"r{rowIndex:D2} {Truncate(agent.DisplayName, RowLabelWidth - 5)}";
        sb.Append(label.PadRight(RowLabelWidth));
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            sb.Append(FormatCell(bitmap.GetCell(rowIndex, d)).PadRight(CellWidth));
        }
        sb.AppendLine();
    }

    private static string FormatCell(Cell cell)
    {
        var symbol = cell.Symbol switch
        {
            CellSymbol.Free => "_",
            CellSymbol.Early => "E",
            CellSymbol.Late => "L",
            CellSymbol.Night => "N",
            CellSymbol.Other => "O",
            CellSymbol.Break => "B",
            _ => "?",
        };
        return cell.IsLocked ? symbol + "*" : symbol;
    }

    private static string DayCode(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Mo",
        DayOfWeek.Tuesday => "Tu",
        DayOfWeek.Wednesday => "We",
        DayOfWeek.Thursday => "Th",
        DayOfWeek.Friday => "Fr",
        DayOfWeek.Saturday => "Sa",
        DayOfWeek.Sunday => "Su",
        _ => "??",
    };

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || max <= 0)
        {
            return string.Empty;
        }
        return s.Length <= max ? s : s[..max];
    }

    private static void AppendLegend(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("Legend: E=Early L=Late N=Night O=Other _=Free B=Break");
        sb.AppendLine("Asterisk (*) marks LOCKED cells (Work.LockLevel != None or Break) — must NOT be swapped.");
        sb.AppendLine("Row labels: r## prefix is the row index; coordinates in proposals use this index.");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Column header: DD<weekday>; coordinates in proposals use 0-based day index (first column = day 0)."));
    }
}
