// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

/// <summary>
/// The first instance for a region rebuild: decides whether a rebuilt plan is ALLOWED, never whether it is better.
/// A rebuilt plan is admitted only when (1) no locked cell moved, (2) every rebuilt row passes the end-state rule check
/// (<see cref="PlanMutationValidator.DiagnoseRowRebuild"/>: day restrictions, eligibility, pauses, consecutive days,
/// weekly hours, weekly rest days, hard planning rules), (3) no stand-in receives a work a real person legally held
/// (<see cref="StandInWork"/>), (4) the touched rows move no further from
/// their target hours (<see cref="TargetHoursDeviationGuard"/>) and (5) the per-row harmony fitness does not fall below
/// the floor.
/// The constraint-agent committee is not consulted: its votes are soft hints, and (5) already rejects every rebuild
/// the per-row score sees as worse.
/// </summary>
/// <param name="validator">Hard rule layer shared with the rest of Wizard 3.</param>
/// <param name="fitness">Per-row harmony fitness used by the acceptance stack.</param>
/// <param name="targetHoursGuard">Guard against moving rows away from their target hours.</param>
public sealed class RegionAdmission
{
    private const double Epsilon = 1e-9;

    private readonly PlanMutationValidator _validator;
    private readonly IBitmapFitnessEvaluator _fitness;
    private readonly TargetHoursDeviationGuard _targetHoursGuard;

    public RegionAdmission(PlanMutationValidator validator, IBitmapFitnessEvaluator fitness, TargetHoursDeviationGuard targetHoursGuard)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _fitness = fitness ?? throw new ArgumentNullException(nameof(fitness));
        _targetHoursGuard = targetHoursGuard ?? throw new ArgumentNullException(nameof(targetHoursGuard));
    }

    /// <summary>Per-row harmony fitness of a plan (the score the floor refers to).</summary>
    public double Fitness(HarmonyBitmap bitmap) => _fitness.Evaluate(bitmap).Fitness;

    /// <summary>Domain check of one cell in place; used to prune the beam early.</summary>
    public bool CellValid(HarmonyBitmap bitmap, int row, int day) => _validator.DiagnoseCellInPlace(bitmap, row, day) is null;

    /// <summary>
    /// True when the cell at (<paramref name="row"/>, <paramref name="day"/>) is invalid in <paramref name="rebuilt"/>
    /// and that invalidity is new: the cell was changed, or it is the same cell as in <paramref name="original"/> but
    /// was valid there (a changed neighbour broke it). An unchanged cell that was already invalid in the original plan
    /// (e.g. a locked late duty of an agent without shift work) is a pre-existing violation and returns false.
    /// </summary>
    /// <param name="original">The plan before the rebuild.</param>
    /// <param name="rebuilt">The plan after (part of) the rebuild.</param>
    /// <param name="row">Row of the cell.</param>
    /// <param name="day">Day of the cell.</param>
    public bool CreatesNewViolation(HarmonyBitmap original, HarmonyBitmap rebuilt, int row, int day)
    {
        if (CellValid(rebuilt, row, day))
        {
            return false;
        }

        var unchanged = ReferenceEquals(rebuilt.GetCell(row, day), original.GetCell(row, day));
        return !unchanged || CellValid(original, row, day);
    }

    /// <summary>
    /// Returns the fitness of <paramref name="rebuilt"/> when it is admitted against <paramref name="original"/>,
    /// otherwise null.
    /// </summary>
    /// <param name="original">The plan before the rebuild.</param>
    /// <param name="rebuilt">The plan after the rebuild.</param>
    /// <param name="rows">Rows the rebuild may have changed.</param>
    /// <param name="steps">The swaps of the rebuild (used to capture the touched rows' target-hour deviation).</param>
    /// <param name="fitnessFloor">Lowest admissible per-row fitness.</param>
    public double? Admit(HarmonyBitmap original, HarmonyBitmap rebuilt, IEnumerable<int> rows, IReadOnlyList<PlanCellSwap> steps, double fitnessFloor)
    {
        if (steps.Count == 0)
        {
            return null;
        }

        foreach (var row in rows)
        {
            var changedDays = new List<int>();
            for (var d = 0; d < original.DayCount; d++)
            {
                var before = original.GetCell(row, d);
                if (ReferenceEquals(before, rebuilt.GetCell(row, d)))
                {
                    continue;
                }

                if (before.IsLocked)
                {
                    return null;
                }

                changedDays.Add(d);
            }

            if (changedDays.Count > 0 && _validator.DiagnoseRowRebuild(original, rebuilt, row, changedDays) is not null)
            {
                return null;
            }
        }

        if (HandsLegalRealWorkToStandIn(original, rebuilt))
        {
            return null;
        }

        var deviationBefore = _targetHoursGuard.Capture(original, steps);
        if (_targetHoursGuard.Diagnose(rebuilt, deviationBefore) is not null)
        {
            return null;
        }

        var fitness = Fitness(rebuilt);
        return fitness >= fitnessFloor - Epsilon ? fitness : null;
    }

    /// <summary>
    /// True when a stand-in holds a work in <paramref name="rebuilt"/> that a real person legally held in
    /// <paramref name="original"/>. A rebuild permutes the cells of a day among the rows and keeps their identity, so the
    /// origin of a stand-in's new work is the row that held the same cell on that day. A work that broke a rule where it
    /// stood may go to the stand-in.
    /// </summary>
    private bool HandsLegalRealWorkToStandIn(HarmonyBitmap original, HarmonyBitmap rebuilt)
    {
        for (var row = 0; row < rebuilt.RowCount; row++)
        {
            if (!rebuilt.Rows[row].IsStandIn)
            {
                continue;
            }

            for (var day = 0; day < rebuilt.DayCount; day++)
            {
                var cell = rebuilt.GetCell(row, day);
                if (!StandInWork.IsWork(cell.Symbol) || ReferenceEquals(cell, original.GetCell(row, day)))
                {
                    continue;
                }

                for (var origin = 0; origin < original.RowCount; origin++)
                {
                    if (ReferenceEquals(original.GetCell(origin, day), cell))
                    {
                        if (!original.Rows[origin].IsStandIn && CellValid(original, origin, day))
                        {
                            return true;
                        }

                        break;
                    }
                }
            }
        }

        return false;
    }
}
