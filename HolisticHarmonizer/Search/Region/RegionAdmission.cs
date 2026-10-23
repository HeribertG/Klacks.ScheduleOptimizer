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
/// weekly hours, weekly rest days, hard planning rules), (3) the touched rows move no further from their target hours
/// (<see cref="TargetHoursDeviationGuard"/>) and (4) the per-row harmony fitness does not fall below the floor.
/// The constraint-agent committee is not consulted: its votes are soft hints, and (4) already rejects every rebuild
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

        var deviationBefore = _targetHoursGuard.Capture(original, steps);
        if (_targetHoursGuard.Diagnose(rebuilt, deviationBefore) is not null)
        {
            return null;
        }

        var fitness = Fitness(rebuilt);
        return fitness >= fitnessFloor - Epsilon ? fitness : null;
    }
}
