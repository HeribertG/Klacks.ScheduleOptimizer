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
    private readonly IReadOnlySet<(string AgentId, Guid ShiftId, DateOnly Date)> _ineligibleAssignments;

    public DomainAwareReplaceValidator(
        IReadOnlyDictionary<(string AgentId, DateOnly Date), DayAvailability>? availability,
        IReadOnlyList<BitmapAssignment>? boundaryAssignments = null,
        IReadOnlySet<(string AgentId, Guid ShiftId, DateOnly Date)>? ineligibleAssignments = null)
    {
        _availability = availability ?? new Dictionary<(string, DateOnly), DayAvailability>();
        _ineligibleAssignments = ineligibleAssignments ?? new HashSet<(string, Guid, DateOnly)>();
        _boundaryByKey = new Dictionary<(string, DateOnly), BitmapAssignment>();
        if (boundaryAssignments is not null)
        {
            foreach (var assignment in boundaryAssignments)
            {
                _boundaryByKey[(assignment.AgentId, assignment.Date)] = assignment;
            }
        }
    }

    /// <summary>
    /// Returns null if the agent may receive the incoming cell's shift on the date, otherwise a short
    /// reason. A missing mandatory qualification is a hard blocker. Public so Wizard 3's
    /// PlanMutationValidator can apply the same check on its cross-day branch (which does not delegate
    /// to <see cref="DiagnoseReceivingSide"/>). With an empty ineligible set this is always null.
    /// </summary>
    public string? DiagnoseEligibility(string agentId, string displayName, Cell incomingCell, DateOnly date, string roleLabel)
    {
        if (_ineligibleAssignments.Count == 0 || incomingCell.ShiftRefId is not Guid shiftId || shiftId == Guid.Empty)
        {
            return null;
        }

        if (_ineligibleAssignments.Contains((agentId, shiftId, date)))
        {
            return $"{roleLabel} {displayName} not qualified for shift {incomingCell.Symbol} on {date:yyyy-MM-dd}";
        }

        return null;
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
        // K16 RestrictedTimeWindow invariant: no seasonal forbidden-window veto is mirrored here,
        // and none is needed. This validator only ever receives a same-day ReplaceMove, whose swap
        // moves whole Cell objects between two rows on ONE day column, preserving each cell's
        // (calendar day, shift, time-of-day). The K16 veto (CoreRestrictedTimeWindow.Blocks) is a
        // pure function of exactly those three and is agent-independent, so a same-day swap can only
        // change which agent owns an existing slot, never relocate a compliant slot into the window;
        // it cannot introduce a new K16 violation. Cross-day moves DO change a cell's day; they never
        // reach this same-day method (they are routed through Wizard 3's PlanMutationValidator cross-day
        // branch, which mirrors this veto for both relocated cells at their target day - that path closes
        // the seasonal-window gap that only cross-day relocation can open).
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

        var eligibilityIssue = DiagnoseEligibility(receivingAgent.Id, receivingAgent.DisplayName, incomingCell, date, roleLabel);
        if (eligibilityIssue is not null)
        {
            return eligibilityIssue;
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

        var agentId = bitmap.Rows[receivingRow].Id;
        var run = 1;
        var reachedStart = true;
        for (var d = dayIndex - 1; d >= 0; d--)
        {
            if (!IsCellOccupied(bitmap, receivingRow, d, agentId))
            {
                reachedStart = false;
                break;
            }
            run++;
        }
        if (reachedStart)
        {
            // Bitmap walk reached the start without a non-working cell — extend the run into the
            // adjacent boundary days. A break or missing entry stops the walk (matches in-bitmap logic).
            var probe = bitmap.Days[0].AddDays(-1);
            while (IsBoundaryDateOccupied(agentId, probe))
            {
                run++;
                probe = probe.AddDays(-1);
            }
        }

        var reachedEnd = true;
        for (var d = dayIndex + 1; d < bitmap.DayCount; d++)
        {
            if (!IsCellOccupied(bitmap, receivingRow, d, agentId))
            {
                reachedEnd = false;
                break;
            }
            run++;
        }
        if (reachedEnd)
        {
            var probe = bitmap.Days[^1].AddDays(1);
            while (IsBoundaryDateOccupied(agentId, probe))
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

    /// <summary>
    /// Returns true if the bitmap cell at (row, day) is a working symbol OR if the previous day's cell
    /// holds a cross-midnight shift whose interval extends into this day. This catches the case where
    /// e.g. a Night cell on day d-1 ends at 07:00 of day d — the morning of day d is occupied even
    /// though the bitmap symbol on day d may be Free.
    /// </summary>
    private bool IsCellOccupied(HarmonyBitmap bitmap, int row, int day, string agentId)
    {
        if (IsWorkingDayCell(bitmap.GetCell(row, day).Symbol)) return true;
        if (day > 0)
        {
            var prev = bitmap.GetCell(row, day - 1);
            if (CellCrossesMidnight(prev)) return true;
        }
        else
        {
            // Day 0 — check boundary entry on the previous calendar day for a cross-midnight shift.
            var prevDate = bitmap.Days[0].AddDays(-1);
            if (TryGetBoundaryAssignment(agentId, prevDate, out var b)
                && IsWorkingAssignment(b)
                && AssignmentCrossesMidnight(b))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns true if a boundary entry occupies <paramref name="date"/> — either by direct anchor
    /// (entry's date == target) or by cross-midnight extension from the previous day.
    /// </summary>
    private bool IsBoundaryDateOccupied(string agentId, DateOnly date)
    {
        if (TryGetBoundaryAssignment(agentId, date, out var anchor) && IsWorkingAssignment(anchor))
        {
            return true;
        }
        if (TryGetBoundaryAssignment(agentId, date.AddDays(-1), out var prev)
            && IsWorkingAssignment(prev)
            && AssignmentCrossesMidnight(prev))
        {
            return true;
        }
        return false;
    }

    private static bool CellCrossesMidnight(Cell cell)
        => cell.StartAt != default && cell.EndAt != default && cell.EndAt.Date > cell.StartAt.Date;

    private static bool AssignmentCrossesMidnight(BitmapAssignment a)
        => a.StartAt != default && a.EndAt != default && a.EndAt.Date > a.StartAt.Date;

    private static bool IsWorkingDayCell(CellSymbol symbol)
    {
        return symbol != CellSymbol.Free && symbol != CellSymbol.Break;
    }

    private string? DiagnoseMaxWeeklyHours(
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

        // Boundary context: hours already worked in the SAME ISO week but on days outside the bitmap
        // (e.g. a period that starts/ends mid-week, with the rest of that week in the adjacent period).
        // Without this, a swap could push the real calendar week over MaxWeeklyHours undetected — the
        // leak this check closes. Break assignments are excluded (IsWorkingAssignment is false for
        // Break), so Break hours never count toward the weekly cap, matching the in-bitmap rule.
        totalHours += BoundaryHoursInWeek(receivingAgent.Id, targetWeek, bitmap.Days[0], bitmap.Days[^1]);

        if (totalHours > receivingAgent.MaxWeeklyHours)
        {
            return $"MaxWeeklyHours exceeded: week total would be {totalHours:F1}h, cap is {receivingAgent.MaxWeeklyHours}h";
        }
        return null;
    }

    /// <summary>
    /// Sums the working hours of boundary assignments that fall in <paramref name="targetWeek"/> but
    /// on days strictly outside the bitmap range. Mirrors the boundary awareness of the MinPause and
    /// MaxConsecutiveDays checks. A full week (7 days) on each side is scanned, which the
    /// ContextDaysBefore/After window (>= 14 days) always covers; <see cref="WeekOf"/> filters to the
    /// exact ISO week so it stays consistent with the in-bitmap summation above.
    /// </summary>
    private decimal BoundaryHoursInWeek(string agentId, (int Year, int Week) targetWeek, DateOnly firstDay, DateOnly lastDay)
    {
        var hours = 0m;
        for (var i = 1; i <= 6; i++)
        {
            var before = firstDay.AddDays(-i);
            if (WeekOf(before) == targetWeek
                && TryGetBoundaryAssignment(agentId, before, out var b)
                && IsWorkingAssignment(b))
            {
                hours += b.Hours;
            }

            var after = lastDay.AddDays(i);
            if (WeekOf(after) == targetWeek
                && TryGetBoundaryAssignment(agentId, after, out var a)
                && IsWorkingAssignment(a))
            {
                hours += a.Hours;
            }
        }
        return hours;
    }

    private static (int Year, int Week) WeekOf(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var week = IsoCalendar.GetWeekOfYear(dt, WeekRule, FirstDayOfWeek);
        return (date.Year, week);
    }
}
