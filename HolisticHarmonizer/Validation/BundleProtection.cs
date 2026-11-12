// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

/// <summary>
/// Protection of stage 3, judged on the end state of a whole batch or region rebuild: on the touched rows no employee
/// loses hours to an employee ranked below, no row gets more packages or more short packages (a package is not cut up),
/// no row gets more packages longer than the 5/2 ideal, no row gets more package pairs with a short rest between them,
/// and the number of mixed packages (packages with more than one shift symbol) summed over the touched rows does not
/// rise. Rows are in rank order, so a lower row index is a higher rank. Package lengths include the days the package ran
/// before the period (<see cref="PeriodCarryIn"/>).
/// </summary>
/// <param name="carryIn">What each agent brings in from the days before the period, keyed by agent id; null for none</param>
public sealed class BundleProtection(IReadOnlyDictionary<string, PeriodCarryIn>? carryIn = null)
{
    private const string HoursFlowDownFormat = "Row {0} loses hours while the lower-ranked row {1} gains hours.";
    private const string ShapeFormat = "Row {0}: {1} would rise from {2} to {3}.";
    private const string PackagesLabel = "the number of packages";
    private const string ShortPackagesLabel = "the number of short packages";
    private const string LongPackagesLabel = "the number of packages beyond the ideal length";
    private const string ShortRestsLabel = "the number of short rests between packages";
    private const string MixedPackagesFormat = "The touched rows: the number of mixed packages would rise from {0} to {1}.";
    private const int NoRow = -1;

    private readonly IReadOnlyDictionary<string, PeriodCarryIn> _carryIn =
        carryIn ?? new Dictionary<string, PeriodCarryIn>(StringComparer.Ordinal);

    /// <summary>Measures the rows the steps touch, before the steps are applied.</summary>
    /// <param name="bitmap">Plan before the steps</param>
    /// <param name="steps">All steps of the batch or rebuild; their rows are the only ones that can change</param>
    public PlanProtectionSnapshot Capture(HarmonyBitmap bitmap, IReadOnlyList<PlanCellSwap> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        return Capture(bitmap, steps.SelectMany(step => new[] { step.RowA, step.RowB }));
    }

    /// <summary>Measures the given rows, before they are changed.</summary>
    /// <param name="bitmap">Plan before the change</param>
    /// <param name="rows">Rows that can change; rows outside the plan are ignored</param>
    public PlanProtectionSnapshot Capture(HarmonyBitmap bitmap, IEnumerable<int> rows)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(rows);

        var ordered = rows.Where(row => row >= 0 && row < bitmap.RowCount).Distinct().Order().ToArray();
        return new PlanProtectionSnapshot(ordered, RowShape.Measure(bitmap, ordered, _carryIn));
    }

    /// <summary>Returns the reason the plan is refused against the captured rows, or null when it is allowed.</summary>
    /// <param name="bitmap">Plan after the change</param>
    /// <param name="before">Measurement taken by <see cref="Capture(HarmonyBitmap, IEnumerable{int})"/></param>
    public string? Diagnose(HarmonyBitmap bitmap, PlanProtectionSnapshot before)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(before);

        var after = RowShape.Measure(bitmap, before.Rows, _carryIn);
        var highestLoser = NoRow;
        var lowestGainer = NoRow;
        var mixedBefore = 0;
        var mixedAfter = 0;
        for (var i = 0; i < before.Rows.Length; i++)
        {
            var was = before.Shapes[i];
            var now = after[i];
            var row = before.Rows[i];
            var worse = Rise(row, PackagesLabel, was.Packages, now.Packages)
                ?? Rise(row, ShortPackagesLabel, was.ShortPackages, now.ShortPackages)
                ?? Rise(row, LongPackagesLabel, was.LongPackages, now.LongPackages)
                ?? Rise(row, ShortRestsLabel, was.ShortRests, now.ShortRests);
            if (worse is not null)
            {
                return worse;
            }

            mixedBefore += was.MixedPackages;
            mixedAfter += now.MixedPackages;

            if (now.Hours < was.Hours && highestLoser == NoRow)
            {
                highestLoser = row;
            }

            if (now.Hours > was.Hours)
            {
                lowestGainer = row;
            }
        }

        if (mixedAfter > mixedBefore)
        {
            return string.Format(CultureInfo.InvariantCulture, MixedPackagesFormat, mixedBefore, mixedAfter);
        }

        return highestLoser != NoRow && lowestGainer > highestLoser
            ? string.Format(CultureInfo.InvariantCulture, HoursFlowDownFormat, highestLoser, lowestGainer)
            : null;
    }

    private static string? Rise(int row, string label, int was, int now)
        => now > was ? string.Format(CultureInfo.InvariantCulture, ShapeFormat, row, label, was, now) : null;
}
