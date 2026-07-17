// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

/// <summary>
/// Hard-constraint layer for Holistic Harmonizer. Wraps the Wizard 2 <see cref="DomainAwareReplaceValidator"/>
/// for same-day swaps and applies cheap pre-checks (bounds, locks, no-op) before delegating.
/// Cross-day swaps (DayA != DayB) are admitted when coverage-neutral — i.e. both cells share the
/// same work-or-free state so the daily head-count on each affected day stays unchanged. The
/// per-row constraint check (max-consec, min-pause) for cross-day is delegated to the
/// constraint-agent committee plus score-greedy because <c>DomainAwareReplaceValidator</c> is
/// scoped to single-day swaps; this is an explicit trade-off documented in the spec.
/// </summary>
public sealed class PlanMutationValidator
{
    private readonly DomainAwareReplaceValidator _domainValidator;
    private readonly IReadOnlyList<CoreRestrictedTimeWindow> _restrictedTimeWindows;

    public PlanMutationValidator(
        DomainAwareReplaceValidator domainValidator,
        IReadOnlyList<CoreRestrictedTimeWindow>? restrictedTimeWindows = null)
    {
        _domainValidator = domainValidator;
        _restrictedTimeWindows = restrictedTimeWindows ?? [];
    }

    public PlanMutationRejection? Validate(HarmonyBitmap bitmap, PlanCellSwap swap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(swap);

        if (!IsInBounds(bitmap, swap))
        {
            return new PlanMutationRejection(swap, PlanMutationRejectionReason.OutOfBounds, FormatCoords(swap, bitmap));
        }

        if (swap.RowA == swap.RowB && swap.DayA == swap.DayB)
        {
            return new PlanMutationRejection(swap, PlanMutationRejectionReason.NoEffect, "RowA equals RowB and DayA equals DayB.");
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

        var crossDay = swap.DayA != swap.DayB;
        if (crossDay)
        {
            if (IsWork(cellA.Symbol) != IsWork(cellB.Symbol))
            {
                return new PlanMutationRejection(
                    swap,
                    PlanMutationRejectionReason.HardConstraintViolation,
                    "Cross-day swap would change daily work coverage: one cell is work, the other is free. Only swap two cells with the same work-or-free state across different days.");
            }

            // K16 RestrictedTimeWindow hard veto. A cross-day swap relocates each cell to a different
            // calendar day; the apply path re-anchors the persisted work to that new date
            // (HarmonizerApplyService.RepointClonedWorksAsync / BuildBulkItems set CurrentDate to
            // bitmap.Days[day] while preserving the time-of-day), so the seasonal window classification can
            // flip and a compliant slot can land inside a forbidden window. Both moved sides are checked at
            // their TARGET day (cellB lands on dayA, cellA lands on dayB). Always hard, independent of any
            // enforcement mode - identical doctrine to Wizard 1's Stage0HardConstraintChecker. The same-day
            // path never changes a cell's day and so needs no such check.
            var windowBlockB = DiagnoseRestrictedWindow(bitmap, cellB, swap.DayA);
            if (windowBlockB is not null)
            {
                return new PlanMutationRejection(swap, PlanMutationRejectionReason.HardConstraintViolation, windowBlockB);
            }

            var windowBlockA = DiagnoseRestrictedWindow(bitmap, cellA, swap.DayB);
            if (windowBlockA is not null)
            {
                return new PlanMutationRejection(swap, PlanMutationRejectionReason.HardConstraintViolation, windowBlockA);
            }

            // Cross-day swaps do not delegate to the same-day domain validator, so the qualification
            // gate is applied here directly for both receiving sides (rowA gets cellB on dayA,
            // rowB gets cellA on dayB).
            var crossDayAgentA = bitmap.Rows[swap.RowA];
            var crossDayAgentB = bitmap.Rows[swap.RowB];
            var eligibilityA = _domainValidator.DiagnoseEligibility(crossDayAgentA.Id, crossDayAgentA.DisplayName, cellB, bitmap.Days[swap.DayA], "rowA");
            if (eligibilityA is not null)
            {
                return new PlanMutationRejection(swap, PlanMutationRejectionReason.HardConstraintViolation, eligibilityA);
            }

            var eligibilityB = _domainValidator.DiagnoseEligibility(crossDayAgentB.Id, crossDayAgentB.DisplayName, cellA, bitmap.Days[swap.DayB], "rowB");
            if (eligibilityB is not null)
            {
                return new PlanMutationRejection(swap, PlanMutationRejectionReason.HardConstraintViolation, eligibilityB);
            }
        }

        if (!crossDay)
        {
            var move = new ReplaceMove(swap.RowA, swap.RowB, swap.DayA);
            var diagnosis = _domainValidator.Diagnose(bitmap, move);
            if (diagnosis is not null)
            {
                return new PlanMutationRejection(
                    swap,
                    PlanMutationRejectionReason.HardConstraintViolation,
                    diagnosis);
            }
        }

        return null;
    }

    private static bool IsWork(CellSymbol symbol)
        => symbol == CellSymbol.Early || symbol == CellSymbol.Late || symbol == CellSymbol.Night || symbol == CellSymbol.Other;

    /// <summary>
    /// Returns a rejection reason if moving <paramref name="movedCell"/> onto <paramref name="toDay"/> lands it
    /// inside a K16 forbidden window, otherwise null. The cell's interval is re-anchored onto the target day at
    /// its original time-of-day, preserving its duration (exactly as the persist path re-anchors the work). The
    /// cell's own <see cref="Cell.StartAt"/> date is never trusted as the source day, so a cell relocated more
    /// than once in the same run is still evaluated against its true target day. The re-anchored slot is then
    /// tested against every window via <see cref="CoreRestrictedTimeWindow.Blocks"/>. Free/Break and shiftless
    /// cells never block.
    /// </summary>
    private string? DiagnoseRestrictedWindow(HarmonyBitmap bitmap, Cell movedCell, int toDay)
    {
        if (_restrictedTimeWindows.Count == 0
            || movedCell.ShiftRefId is not Guid shiftRefId
            || shiftRefId == Guid.Empty
            || movedCell.StartAt == default
            || movedCell.EndAt == default)
        {
            return null;
        }

        var duration = movedCell.EndAt - movedCell.StartAt;
        var slotStart = bitmap.Days[toDay].ToDateTime(TimeOnly.FromDateTime(movedCell.StartAt));
        var slotEnd = slotStart.Add(duration);

        foreach (var window in _restrictedTimeWindows)
        {
            if (window.Blocks(slotStart, slotEnd, shiftRefId))
            {
                return $"Cross-day swap would place shift {movedCell.Symbol} into a RestrictedTimeWindow (K16) on {bitmap.Days[toDay]:yyyy-MM-dd}.";
            }
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
