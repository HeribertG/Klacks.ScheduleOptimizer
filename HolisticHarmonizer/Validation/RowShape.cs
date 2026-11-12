// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

/// <summary>
/// What one row of the plan looks like to the stage-3 protection: its hours and its work packages (runs of consecutive
/// worked days; an absence ends a package like a free day). A package that runs into the period from the days before it
/// counts with its full length (<see cref="PeriodCarryIn"/>), and the rest before the first package of the period is
/// measured from the last worked day before it. A package is mixed when its worked days do not all carry the same shift
/// symbol.
/// </summary>
/// <param name="Hours">Sum of the cell hours of the row inside the period</param>
/// <param name="Packages">Number of work packages</param>
/// <param name="ShortPackages">Packages of at most <see cref="ShortPackageMaxDays"/> days</param>
/// <param name="LongPackages">Packages of more than <see cref="IdealPackageMaxDays"/> days</param>
/// <param name="ShortRests">Neighbouring packages with less than <see cref="PackageRestHours"/> hours between them</param>
/// <param name="MixedPackages">Packages whose worked days carry more than one shift symbol</param>
public sealed record RowShape(
    decimal Hours,
    int Packages,
    int ShortPackages,
    int LongPackages,
    int ShortRests,
    int MixedPackages)
{
    /// <summary>Longest package that counts as a short one.</summary>
    public const int ShortPackageMaxDays = 2;

    /// <summary>Longest package the 5/2 ideal allows.</summary>
    public const int IdealPackageMaxDays = 5;

    /// <summary>Rest the specification asks for between two packages.</summary>
    public const int PackageRestHours = 48;

    /// <summary>Measures the given rows.</summary>
    /// <param name="bitmap">Plan to measure</param>
    /// <param name="rows">Row indices to measure</param>
    /// <param name="carryIn">What each agent brings in from the days before the period, keyed by agent id</param>
    public static RowShape[] Measure(
        HarmonyBitmap bitmap, int[] rows, IReadOnlyDictionary<string, PeriodCarryIn> carryIn)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(carryIn);

        var shapes = new RowShape[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            shapes[i] = Of(bitmap, rows[i], carryIn.GetValueOrDefault(bitmap.Rows[rows[i]].Id));
        }

        return shapes;
    }

    private static RowShape Of(HarmonyBitmap bitmap, int row, PeriodCarryIn? carryIn)
    {
        var hours = 0m;
        var shortPackages = 0;
        var longPackages = 0;
        var shortRests = 0;
        var mixedPackages = 0;
        var runLength = carryIn?.Days ?? 0;
        var runSymbol = carryIn?.LastSymbol ?? CellSymbol.Free;
        var runIsMixed = carryIn?.IsMixed ?? false;
        var lastWorkEnd = carryIn?.LastWorkEnd ?? default;
        var packages = runLength > 0 ? 1 : 0;

        for (var day = 0; day < bitmap.DayCount; day++)
        {
            var cell = bitmap.GetCell(row, day);
            hours += cell.Hours;

            if (StandInWork.IsWork(cell.Symbol))
            {
                if (runLength == 0)
                {
                    packages++;
                    runSymbol = cell.Symbol;
                    runIsMixed = false;
                    if (RestIsShort(lastWorkEnd, cell))
                    {
                        shortRests++;
                    }
                }

                runIsMixed |= cell.Symbol != runSymbol;
                runLength++;
                lastWorkEnd = cell.EndAt;
                continue;
            }

            if (runLength > 0)
            {
                Close(runLength, runIsMixed, ref shortPackages, ref longPackages, ref mixedPackages);
                runLength = 0;
            }
        }

        if (runLength > 0)
        {
            Close(runLength, runIsMixed, ref shortPackages, ref longPackages, ref mixedPackages);
        }

        return new RowShape(hours, packages, shortPackages, longPackages, shortRests, mixedPackages);
    }

    private static void Close(
        int runLength, bool runIsMixed, ref int shortPackages, ref int longPackages, ref int mixedPackages)
    {
        if (runIsMixed)
        {
            mixedPackages++;
        }

        if (runLength <= ShortPackageMaxDays)
        {
            shortPackages++;
        }

        if (runLength > IdealPackageMaxDays)
        {
            longPackages++;
        }
    }

    private static bool RestIsShort(DateTime lastWorkEnd, Cell firstOfRun)
        => lastWorkEnd != default
            && firstOfRun.StartAt != default
            && (firstOfRun.StartAt - lastWorkEnd).TotalHours < PackageRestHours;
}
