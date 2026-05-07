// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

/// <summary>
/// Hard-constraint layer for Holistic Harmonizer. Wraps the Wizard 2 <see cref="DomainAwareReplaceValidator"/>
/// for same-day swaps and applies cheap pre-checks (bounds, locks, no-op) before delegating.
/// MVP scope: cross-day swaps are rejected — only same-day (DayA == DayB) moves are admitted.
/// </summary>
public sealed class PlanMutationValidator
{
    private readonly DomainAwareReplaceValidator _domainValidator;

    public PlanMutationValidator(DomainAwareReplaceValidator domainValidator)
    {
        _domainValidator = domainValidator;
    }

    public PlanMutationRejection? Validate(HarmonyBitmap bitmap, PlanCellSwap swap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(swap);

        if (!IsInBounds(bitmap, swap))
        {
            return new PlanMutationRejection(swap, PlanMutationRejectionReason.OutOfBounds, FormatCoords(swap, bitmap));
        }

        if (swap.DayA != swap.DayB)
        {
            return new PlanMutationRejection(
                swap,
                PlanMutationRejectionReason.HardConstraintViolation,
                "Cross-day swaps are not supported in Holistic Harmonizer MVP — DayA must equal DayB.");
        }

        if (swap.RowA == swap.RowB)
        {
            return new PlanMutationRejection(swap, PlanMutationRejectionReason.NoEffect, "RowA equals RowB.");
        }

        var cellA = bitmap.GetCell(swap.RowA, swap.DayA);
        var cellB = bitmap.GetCell(swap.RowB, swap.DayB);

        if (cellA.IsLocked || cellB.IsLocked)
        {
            return new PlanMutationRejection(
                swap,
                PlanMutationRejectionReason.LockedCell,
                BuildLockDetail(swap, cellA, cellB));
        }

        if (cellA.Symbol == cellB.Symbol && cellA.ShiftRefId == cellB.ShiftRefId)
        {
            return new PlanMutationRejection(swap, PlanMutationRejectionReason.NoEffect, "Cells already share the same symbol.");
        }

        var move = new ReplaceMove(swap.RowA, swap.RowB, swap.DayA);
        var diagnosis = _domainValidator.Diagnose(bitmap, move);
        if (diagnosis is not null)
        {
            return new PlanMutationRejection(
                swap,
                PlanMutationRejectionReason.HardConstraintViolation,
                diagnosis);
        }

        return null;
    }

    public static void Apply(HarmonyBitmap bitmap, PlanCellSwap swap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(swap);

        var cellA = bitmap.GetCell(swap.RowA, swap.DayA);
        var cellB = bitmap.GetCell(swap.RowB, swap.DayB);
        bitmap.SetCell(swap.RowA, swap.DayA, cellB);
        bitmap.SetCell(swap.RowB, swap.DayB, cellA);
    }

    private static bool IsInBounds(HarmonyBitmap bitmap, PlanCellSwap swap)
        => swap.RowA >= 0 && swap.RowA < bitmap.RowCount
        && swap.RowB >= 0 && swap.RowB < bitmap.RowCount
        && swap.DayA >= 0 && swap.DayA < bitmap.DayCount
        && swap.DayB >= 0 && swap.DayB < bitmap.DayCount;

    private static string FormatCoords(PlanCellSwap swap, HarmonyBitmap bitmap)
        => $"Coordinates out of bounds (rowA={swap.RowA}, dayA={swap.DayA}, rowB={swap.RowB}, dayB={swap.DayB}; rowCount={bitmap.RowCount}, dayCount={bitmap.DayCount}).";

    private static string BuildLockDetail(PlanCellSwap swap, Cell cellA, Cell cellB)
    {
        if (cellA.IsLocked && cellB.IsLocked)
        {
            return "Both source and target cells are locked.";
        }
        return cellA.IsLocked
            ? $"Cell A (row {swap.RowA}, day {swap.DayA}) is locked."
            : $"Cell B (row {swap.RowB}, day {swap.DayB}) is locked.";
    }
}
