// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Hybrid validator that combines bitmap-local checks (locks, indices, self-swap) with
/// hard domain constraints lifted from Wizard 1: contract WorksOnDay, FREE-Keyword,
/// BreakBlocker, ClientShiftPreference Blacklist, MaxConsecutiveDays, MaxWeeklyHours and
/// MinPauseHours. Operates on the bitmap directly without converting to tokens.
/// </summary>
/// <param name="availability">Per (agent, date) availability map, supplied by the context builder</param>
/// <param name="boundaryAssignments">
/// Optional list of works/breaks on the days adjacent to the bitmap (BitmapInput.BoundaryAssignments).
/// Used by MaxConsecutiveDays and MinPauseHours checks so runs and rest gaps that cross the bitmap
/// edges (period start / period end) are detected. Cells in the bitmap itself are never affected;
/// the engine never reads or mutates these entries.
/// </param>
public sealed class DomainAwareReplaceValidator : IReplaceValidator
{
    private static readonly Calendar IsoCalendar = CultureInfo.InvariantCulture.Calendar;
    private const CalendarWeekRule WeekRule = CalendarWeekRule.FirstFourDayWeek;
    private const DayOfWeek FirstDayOfWeek = DayOfWeek.Monday;

    private readonly IReadOnlyDictionary<(string AgentId, DateOnly Date), DayAvailability> _availability;
    private readonly Dictionary<(string AgentId, DateOnly Date), BitmapAssignment> _boundaryByKey;

    public DomainAwareReplaceValidator(
        IReadOnlyDictionary<(string AgentId, DateOnly Date), DayAvailability>? availability,
        IReadOnlyList<BitmapAssignment>? boundaryAssignments = null)
    {
        _availability = availability ?? new Dictionary<(string, DateOnly), DayAvailability>();
        _boundaryByKey = new Dictionary<(string, DateOnly), BitmapAssignment>();
        if (boundaryAssignments is not null)
        {
            foreach (var assignment in boundaryAssignments)
            {
                _boundaryByKey[(assignment.AgentId, assignment.Date)] = assignment;
            }
        }
    }

    public bool IsValid(HarmonyBitmap bitmap, ReplaceMove move) => Diagnose(bitmap, move) is null;

    /// <summary>
    /// Returns null if the move is valid, otherwise a short human-readable explanation
    /// naming the violated constraint and the specific values involved (run length,
    /// pause hours, weekly hours, etc.). Used by Wizard 3 to feed structured rejections
    /// back to the LLM through <c>RejectMemory</c>.
    /// </summary>
    public string? Diagnose(HarmonyBitmap bitmap, ReplaceMove move)
    {
        var local = DiagnoseBitmapLocal(bitmap, move);
        if (local is not null)
        {
            return local;
        }

        var cellA = bitmap.GetCell(move.RowA, move.Day);
        var cellB = bitmap.GetCell(move.RowB, move.Day);
        var date = bitmap.Days[move.Day];
        var agentA = bitmap.Rows[move.RowA];
        var agentB = bitmap.Rows[move.RowB];

        var sideA = DiagnoseReceivingSide(bitmap, move.RowA, agentA, date, cellB, "rowA");
        if (sideA is not null)
        {
            return sideA;
        }
        var sideB = DiagnoseReceivingSide(bitmap, move.RowB, agentB, date, cellA, "rowB");
        if (sideB is not null)
        {
            return sideB;
        }

        return null;
    }

    private static string? DiagnoseBitmapLocal(HarmonyBitmap bitmap, ReplaceMove move)
    {
        if (move.RowA == move.RowB)
        {
            return "rowA equals rowB";
        }
        if (move.RowA < 0 || move.RowA >= bitmap.RowCount)
        {
            return $"rowA {move.RowA} out of bounds (rowCount {bitmap.RowCount})";
        }
        if (move.RowB < 0 || move.RowB >= bitmap.RowCount)
        {
            return $"rowB {move.RowB} out of bounds (rowCount {bitmap.RowCount})";
        }
        if (move.Day < 0 || move.Day >= bitmap.DayCount)
        {
            return $"day {move.Day} out of bounds (dayCount {bitmap.DayCount})";
        }
        if (bitmap.GetCell(move.RowA, move.Day).IsLocked)
        {
            return $"cell at rowA={move.RowA} day={move.Day} is locked";
        }
        if (bitmap.GetCell(move.RowB, move.Day).IsLocked)
        {
            return $"cell at rowB={move.RowB} day={move.Day} is locked";
        }
        return null;
    }

    private string? DiagnoseReceivingSide(
        HarmonyBitmap bitmap,
        int receivingRow,
        BitmapAgent receivingAgent,
        DateOnly date,
        Cell incomingCell,
        string roleLabel)
    {
        if (incomingCell.Symbol == CellSymbol.Free)
        {
            return null;
        }

        if (!IsAvailableOnDate(receivingAgent.Id, date))
        {
            return $"{roleLabel} {receivingAgent.DisplayName} not available on {date:yyyy-MM-dd}";
        }

        var keywordIssue = DiagnoseKeywordRestriction(receivingAgent.Id, date, incomingCell.Symbol);
        if (keywordIssue is not null)
        {
            return $"{roleLabel} {receivingAgent.DisplayName}: {keywordIssue}";
        }

        if (incomingCell.ShiftRefId is Guid shiftId
            && receivingAgent.BlacklistedShiftIds is not null
            && receivingAgent.BlacklistedShiftIds.Contains(shiftId))
        {
            return $"{roleLabel} {receivingAgent.DisplayName} blacklisted for shift {incomingCell.Symbol}";
        }

        var dayIndex = IndexOfDate(bitmap.Days, date);
        if (dayIndex < 0)
        {
            return null;
        }

        var pauseIssue = DiagnoseMinPause(bitmap, receivingRow, receivingAgent, dayIndex, incomingCell);
        if (pauseIssue is not null)
        {
            return $"{roleLabel} {receivingAgent.DisplayName}: {pauseIssue}";
        }

        var consecIssue = DiagnoseMaxConsecutiveDays(bitmap, receivingRow, receivingAgent, dayIndex, incomingCell);
        if (consecIssue is not null)
        {
            return $"{roleLabel} {receivingAgent.DisplayName}: {consecIssue}";
        }

        var weeklyIssue = DiagnoseMaxWeeklyHours(bitmap, receivingRow, receivingAgent, date, dayIndex, incomingCell);
        if (weeklyIssue is not null)
        {
            return $"{roleLabel} {receivingAgent.DisplayName}: {weeklyIssue}";
        }

        return null;
    }

    private static int IndexOfDate(IReadOnlyList<DateOnly> days, DateOnly date)
    {
        if (days.Count == 0)
        {
            return -1;
        }
        var index = date.DayNumber - days[0].DayNumber;
        if (index < 0 || index >= days.Count || days[index] != date)
        {
            return -1;
        }
        return index;
    }

    private bool IsAvailableOnDate(string agentId, DateOnly date)
    {
        if (_availability.TryGetValue((agentId, date), out var availability))
        {
            return availability.IsAvailable;
        }
        return true;
    }

    private string? DiagnoseKeywordRestriction(string agentId, DateOnly date, CellSymbol incomingSymbol)
    {
        if (!_availability.TryGetValue((agentId, date), out var availability))
        {
            return null;
        }

        if (availability.RequiredSymbol is { } required && incomingSymbol != required)
        {
            return $"day requires {required} but incoming cell is {incomingSymbol}";
        }

        if (availability.ForbiddenSymbol is { } forbidden && incomingSymbol == forbidden)
        {
            return $"day forbids {forbidden}";
        }

        return null;
    }

    private string? DiagnoseMinPause(
        HarmonyBitmap bitmap,
        int receivingRow,
        BitmapAgent receivingAgent,
        int dayIndex,
        Cell incomingCell)
    {
        if (receivingAgent.MinPauseHours <= 0 || incomingCell.StartAt == default || incomingCell.EndAt == default)
        {
            return null;
        }

        if (dayIndex - 1 >= 0)
        {
            var previous = bitmap.GetCell(receivingRow, dayIndex - 1);
            if (IsWorkingDayCell(previous.Symbol) && previous.EndAt != default)
            {
                var pause = (decimal)(incomingCell.StartAt - previous.EndAt).TotalHours;
                if (pause < receivingAgent.MinPauseHours)
                {
                    return $"MinPauseHours violated: previous shift ends {previous.EndAt:HH:mm}, new shift starts {incomingCell.StartAt:HH:mm} (pause {pause:F1}h, min {receivingAgent.MinPauseHours}h)";
                }
            }
        }
        else if (TryGetBoundaryAssignment(receivingAgent.Id, bitmap.Days[0].AddDays(-1), out var prevBoundary)
                 && IsWorkingAssignment(prevBoundary)
                 && prevBoundary.EndAt != default)
        {
            // Boundary context: previous day lies outside the bitmap (dayIndex == 0). Use the boundary
            // assignment so a late shift on the last day of the previous period correctly constrains the
            // first day of this period.
            var pause = (decimal)(incomingCell.StartAt - prevBoundary.EndAt).TotalHours;
            if (pause < receivingAgent.MinPauseHours)
            {
                return $"MinPauseHours violated: previous shift (boundary) ends {prevBoundary.EndAt:HH:mm}, new shift starts {incomingCell.StartAt:HH:mm} (pause {pause:F1}h, min {receivingAgent.MinPauseHours}h)";
            }
        }

        if (dayIndex + 1 < bitmap.DayCount)
        {
            var next = bitmap.GetCell(receivingRow, dayIndex + 1);
            if (IsWorkingDayCell(next.Symbol) && next.StartAt != default)
            {
                var pause = (decimal)(next.StartAt - incomingCell.EndAt).TotalHours;
                if (pause < receivingAgent.MinPauseHours)
                {
                    return $"MinPauseHours violated: new shift ends {incomingCell.EndAt:HH:mm}, next shift starts {next.StartAt:HH:mm} (pause {pause:F1}h, min {receivingAgent.MinPauseHours}h)";
                }
            }
        }
        else if (TryGetBoundaryAssignment(receivingAgent.Id, bitmap.Days[^1].AddDays(1), out var nextBoundary)
                 && IsWorkingAssignment(nextBoundary)
                 && nextBoundary.StartAt != default)
        {
            // Boundary context: next day lies outside the bitmap (dayIndex == DayCount - 1).
            var pause = (decimal)(nextBoundary.StartAt - incomingCell.EndAt).TotalHours;
            if (pause < receivingAgent.MinPauseHours)
            {
                return $"MinPauseHours violated: new shift ends {incomingCell.EndAt:HH:mm}, next shift (boundary) starts {nextBoundary.StartAt:HH:mm} (pause {pause:F1}h, min {receivingAgent.MinPauseHours}h)";
            }
        }

        return null;
    }

    private bool TryGetBoundaryAssignment(string agentId, DateOnly date, out BitmapAssignment assignment)
    {
        return _boundaryByKey.TryGetValue((agentId, date), out assignment!);
    }

    private static bool IsWorkingAssignment(BitmapAssignment assignment)
        => IsWorkingDayCell(assignment.Symbol);

    private string? DiagnoseMaxConsecutiveDays(
        HarmonyBitmap bitmap,
        int receivingRow,
        BitmapAgent receivingAgent,
        int dayIndex,
        Cell incomingCell)
    {
        if (receivingAgent.MaxConsecutiveDays <= 0 || !IsWorkingDayCell(incomingCell.Symbol))
        {
            return null;
        }

        var run = 1;
        var reachedStart = true;
        for (var d = dayIndex - 1; d >= 0; d--)
        {
            if (!IsWorkingDayCell(bitmap.GetCell(receivingRow, d).Symbol))
            {
                reachedStart = false;
                break;
            }
            run++;
        }
        if (reachedStart && _boundaryByKey.Count > 0)
        {
            // Bitmap walk reached the start without a non-working cell — extend the run into the
            // adjacent boundary days. A break or missing entry stops the walk (matches in-bitmap logic).
            var probe = bitmap.Days[0].AddDays(-1);
            while (TryGetBoundaryAssignment(receivingAgent.Id, probe, out var b) && IsWorkingAssignment(b))
            {
                run++;
                probe = probe.AddDays(-1);
            }
        }

        var reachedEnd = true;
        for (var d = dayIndex + 1; d < bitmap.DayCount; d++)
        {
            if (!IsWorkingDayCell(bitmap.GetCell(receivingRow, d).Symbol))
            {
                reachedEnd = false;
                break;
            }
            run++;
        }
        if (reachedEnd && _boundaryByKey.Count > 0)
        {
            var probe = bitmap.Days[^1].AddDays(1);
            while (TryGetBoundaryAssignment(receivingAgent.Id, probe, out var b) && IsWorkingAssignment(b))
            {
                run++;
                probe = probe.AddDays(1);
            }
        }

        if (run > receivingAgent.MaxConsecutiveDays)
        {
            return $"MaxConsecutiveDays exceeded: run would be {run} days, cap is {receivingAgent.MaxConsecutiveDays}";
        }
        return null;
    }

    private static bool IsWorkingDayCell(CellSymbol symbol)
    {
        return symbol != CellSymbol.Free && symbol != CellSymbol.Break;
    }

    private static string? DiagnoseMaxWeeklyHours(
        HarmonyBitmap bitmap,
        int receivingRow,
        BitmapAgent receivingAgent,
        DateOnly date,
        int dayIndex,
        Cell incomingCell)
    {
        if (receivingAgent.MaxWeeklyHours <= 0 || incomingCell.Hours <= 0)
        {
            return null;
        }

        var targetWeek = WeekOf(date);
        var totalHours = incomingCell.Hours;

        for (var d = 0; d < bitmap.DayCount; d++)
        {
            if (d == dayIndex)
            {
                continue;
            }
            if (WeekOf(bitmap.Days[d]) != targetWeek)
            {
                continue;
            }
            var cell = bitmap.GetCell(receivingRow, d);
            if (IsWorkingDayCell(cell.Symbol))
            {
                totalHours += cell.Hours;
            }
        }

        if (totalHours > receivingAgent.MaxWeeklyHours)
        {
            return $"MaxWeeklyHours exceeded: week total would be {totalHours:F1}h, cap is {receivingAgent.MaxWeeklyHours}h";
        }
        return null;
    }

    private static (int Year, int Week) WeekOf(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var week = IsoCalendar.GetWeekOfYear(dt, WeekRule, FirstDayOfWeek);
        return (date.Year, week);
    }
}
