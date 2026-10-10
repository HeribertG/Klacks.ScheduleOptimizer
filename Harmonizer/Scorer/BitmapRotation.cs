// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Scorer;

/// <summary>
/// Reads a bitmap row as the shared rotation rule sees it (<see cref="ShiftRotation"/>, SPEC-ROTATION-2026-10-08), for
/// Wizard 2's scorer and Wizard 3's rotation agent. Early, late and night cells are rotation days; other (non-shift),
/// free and break cells are not. A cell without times gets the typical span of its kind. Known gap: the row carries
/// neither the shifts before the period nor the kinds the employee may not work, so the first block has no predecessor
/// and no kind is skipped.
/// </summary>
public static class BitmapRotation
{
    /// <summary>Weight of a non-ideal block change against a kind change inside a block: rotation before purity.</summary>
    public const int NonIdealTransitionWeight = 2;

    private const double TypicalShiftHours = 8;

    private static readonly TimeOnly[] TypicalStarts = [new(6, 0), new(14, 0), new(22, 0)];

    /// <summary>The row's rotation days in date order.</summary>
    /// <param name="bitmap">Plan bitmap</param>
    /// <param name="rowIndex">Row of the employee</param>
    /// <param name="replaced">Optional cells to read instead of the bitmap's cells, by day index (a trial swap)</param>
    public static List<RotationDay> DaysOf(HarmonyBitmap bitmap, int rowIndex, IReadOnlyDictionary<int, Cell>? replaced = null)
    {
        var days = new List<RotationDay>();
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var cell = replaced is not null && replaced.TryGetValue(d, out var trial) ? trial : bitmap.GetCell(rowIndex, d);
            var kind = KindIndexOf(cell.Symbol);
            if (kind is null)
            {
                continue;
            }

            var date = bitmap.Days[d];
            var startAt = cell.StartAt != default ? cell.StartAt : date.ToDateTime(TypicalStarts[kind.Value]);
            var endAt = cell.EndAt != default ? cell.EndAt : startAt.AddHours(TypicalShiftHours);
            days.Add(new RotationDay(date, kind.Value, kind.Value, startAt, endAt));
        }

        return days;
    }

    /// <summary>Assessment of the row's days; every kind counts as allowed.</summary>
    public static RotationAssessment Assess(HarmonyBitmap bitmap, IReadOnlyList<RotationDay> days)
        => days.Count == 0 || bitmap.DayCount == 0
            ? default
            : ShiftRotation.Assess(days, bitmap.Days[0], (_, _) => true);

    /// <summary>Kind changes inside blocks plus <see cref="NonIdealTransitionWeight"/> per non-ideal block change.</summary>
    public static int Cost(HarmonyBitmap bitmap, IReadOnlyList<RotationDay> days)
    {
        var assessment = Assess(bitmap, days);
        return assessment.InBlockChanges + (assessment.NonIdealTransitions * NonIdealTransitionWeight);
    }

    private static int? KindIndexOf(CellSymbol symbol) => symbol switch
    {
        CellSymbol.Early => 0,
        CellSymbol.Late => 1,
        CellSymbol.Night => 2,
        _ => null,
    };
}
