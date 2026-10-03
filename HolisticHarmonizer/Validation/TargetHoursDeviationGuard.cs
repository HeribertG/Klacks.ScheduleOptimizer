// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

/// <summary>
/// Stage 3 must never move the plan away from the contracted hours. A batch only touches the rows of its
/// swaps, so the guard compares those rows before and after the batch: the sum of |worked - target|, the
/// sum of squared deviations (the row RMS of the whole plan rises exactly when this sum rises) and the
/// largest single-row deviation must all stay the same or shrink. Rows without a target (TargetHours &lt;= 0)
/// are ignored, as in the harmony scorer. Worked hours are the cell hours of the row (Break.WorkTime
/// included), the same quantity the scorer and the benchmark use.
/// </summary>
public sealed class TargetHoursDeviationGuard
{
    private const string DetailFormat =
        "Target-hour deviation would worsen on the touched rows: sum {0:0.##} h -> {1:0.##} h, largest row {2:0.##} h -> {3:0.##} h.";

    /// <summary>Snapshot of the rows a batch touches, taken before the batch is applied.</summary>
    /// <param name="bitmap">Plan before the batch.</param>
    /// <param name="steps">All steps of the batch; their rows are the only ones that can change.</param>
    public DeviationSnapshot Capture(HarmonyBitmap bitmap, IReadOnlyList<PlanCellSwap> steps)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(steps);

        var rows = new SortedSet<int>();
        foreach (var step in steps)
        {
            AddRow(bitmap, rows, step.RowA);
            AddRow(bitmap, rows, step.RowB);
        }
        return Measure(bitmap, rows.ToArray());
    }

    /// <summary>
    /// Returns a human-readable reason when the current plan is worse than <paramref name="before"/> on the
    /// captured rows; null when the batch keeps or improves the target-hour fit.
    /// </summary>
    public string? Diagnose(HarmonyBitmap bitmap, DeviationSnapshot before)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(before);

        var after = Measure(bitmap, before.Rows);
        var worse = after.AbsoluteSum > before.AbsoluteSum
                    || after.SquaredSum > before.SquaredSum
                    || after.MaxRow > before.MaxRow;
        return worse
            ? string.Format(CultureInfo.InvariantCulture, DetailFormat, before.AbsoluteSum, after.AbsoluteSum, before.MaxRow, after.MaxRow)
            : null;
    }

    private static void AddRow(HarmonyBitmap bitmap, SortedSet<int> rows, int row)
    {
        if (row >= 0 && row < bitmap.RowCount && bitmap.Rows[row].TargetHours > 0)
        {
            rows.Add(row);
        }
    }

    private static DeviationSnapshot Measure(HarmonyBitmap bitmap, int[] rows)
    {
        var absoluteSum = 0m;
        var squaredSum = 0m;
        var maxRow = 0m;
        foreach (var row in rows)
        {
            var worked = 0m;
            for (var day = 0; day < bitmap.DayCount; day++)
            {
                worked += bitmap.GetCell(row, day).Hours;
            }
            var deviation = Math.Abs(worked - bitmap.Rows[row].TargetHours);
            absoluteSum += deviation;
            squaredSum += deviation * deviation;
            maxRow = Math.Max(maxRow, deviation);
        }
        return new DeviationSnapshot(rows, absoluteSum, squaredSum, maxRow);
    }
}
