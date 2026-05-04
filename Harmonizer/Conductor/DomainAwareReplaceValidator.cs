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
public sealed class DomainAwareReplaceValidator : IReplaceValidator
{
    private static readonly Calendar IsoCalendar = CultureInfo.InvariantCulture.Calendar;
    private const CalendarWeekRule WeekRule = CalendarWeekRule.FirstFourDayWeek;
    private const DayOfWeek FirstDayOfWeek = DayOfWeek.Monday;

    private readonly IReadOnlyDictionary<(string AgentId, DateOnly Date), DayAvailability> _availability;

    public DomainAwareReplaceValidator(IReadOnlyDictionary<(string AgentId, DateOnly Date), DayAvailability>? availability)
    {
        _availability = availability ?? new Dictionary<(string, DateOnly), DayAvailability>();
    }

    public bool IsValid(HarmonyBitmap bitmap, ReplaceMove move)
    {
        if (!IsBitmapLocallyValid(bitmap, move))
        {
            return false;
        }

        var cellA = bitmap.GetCell(move.RowA, move.Day);
        var cellB = bitmap.GetCell(move.RowB, move.Day);
        var date = bitmap.Days[move.Day];
        var agentA = bitmap.Rows[move.RowA];
        var agentB = bitmap.Rows[move.RowB];

        if (!IsReceivingSideAdmissible(bitmap, move.RowA, agentA, date, cellB))
        {
            return false;
        }
        if (!IsReceivingSideAdmissible(bitmap, move.RowB, agentB, date, cellA))
        {
            return false;
        }

        return true;
    }

    private static bool IsBitmapLocallyValid(HarmonyBitmap bitmap, ReplaceMove move)
    {
        if (move.RowA == move.RowB)
        {
            return false;
        }
        if (move.RowA < 0 || move.RowA >= bitmap.RowCount)
        {
            return false;
        }
        if (move.RowB < 0 || move.RowB >= bitmap.RowCount)
        {
            return false;
        }
        if (move.Day < 0 || move.Day >= bitmap.DayCount)
        {
            return false;
        }
        return !bitmap.GetCell(move.RowA, move.Day).IsLocked
            && !bitmap.GetCell(move.RowB, move.Day).IsLocked;
    }

    private bool IsReceivingSideAdmissible(
        HarmonyBitmap bitmap,
        int receivingRow,
        BitmapAgent receivingAgent,
        DateOnly date,
        Cell incomingCell)
    {
        if (incomingCell.Symbol == CellSymbol.Free)
        {
            return true;
        }

        if (!IsAvailableOnDate(receivingAgent.Id, date))
        {
            return false;
        }

        if (!RespectsKeywordRestriction(receivingAgent.Id, date, incomingCell.Symbol))
        {
            return false;
        }

        if (incomingCell.ShiftRefId is Guid shiftId
            && receivingAgent.BlacklistedShiftIds is not null
            && receivingAgent.BlacklistedShiftIds.Contains(shiftId))
        {
            return false;
        }

        var dayIndex = IndexOfDate(bitmap.Days, date);
        if (dayIndex < 0)
        {
            return true;
        }

        if (!RespectsMinPause(bitmap, receivingRow, receivingAgent, dayIndex, incomingCell))
        {
            return false;
        }

        if (!RespectsMaxConsecutiveDays(bitmap, receivingRow, receivingAgent, dayIndex, incomingCell))
        {
            return false;
        }

        if (!RespectsMaxWeeklyHours(bitmap, receivingRow, receivingAgent, date, dayIndex, incomingCell))
        {
            return false;
        }

        return true;
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

    private bool RespectsKeywordRestriction(string agentId, DateOnly date, CellSymbol incomingSymbol)
    {
        if (!_availability.TryGetValue((agentId, date), out var availability))
        {
            return true;
        }

        if (availability.RequiredSymbol is { } required && incomingSymbol != required)
        {
            return false;
        }

        if (availability.ForbiddenSymbol is { } forbidden && incomingSymbol == forbidden)
        {
            return false;
        }

        return true;
    }

    private static bool RespectsMinPause(
        HarmonyBitmap bitmap,
        int receivingRow,
        BitmapAgent receivingAgent,
        int dayIndex,
        Cell incomingCell)
    {
        if (receivingAgent.MinPauseHours <= 0 || incomingCell.StartAt == default || incomingCell.EndAt == default)
        {
            return true;
        }

        if (dayIndex - 1 >= 0)
        {
            var previous = bitmap.GetCell(receivingRow, dayIndex - 1);
            if (previous.Symbol != CellSymbol.Free && previous.EndAt != default)
            {
                var pause = (decimal)(incomingCell.StartAt - previous.EndAt).TotalHours;
                if (pause < receivingAgent.MinPauseHours)
                {
                    return false;
                }
            }
        }

        if (dayIndex + 1 < bitmap.DayCount)
        {
            var next = bitmap.GetCell(receivingRow, dayIndex + 1);
            if (next.Symbol != CellSymbol.Free && next.StartAt != default)
            {
                var pause = (decimal)(next.StartAt - incomingCell.EndAt).TotalHours;
                if (pause < receivingAgent.MinPauseHours)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool RespectsMaxConsecutiveDays(
        HarmonyBitmap bitmap,
        int receivingRow,
        BitmapAgent receivingAgent,
        int dayIndex,
        Cell incomingCell)
    {
        if (receivingAgent.MaxConsecutiveDays <= 0 || incomingCell.Symbol == CellSymbol.Free)
        {
            return true;
        }

        var run = 1;
        for (var d = dayIndex - 1; d >= 0; d--)
        {
            if (bitmap.GetCell(receivingRow, d).Symbol == CellSymbol.Free)
            {
                break;
            }
            run++;
        }
        for (var d = dayIndex + 1; d < bitmap.DayCount; d++)
        {
            if (bitmap.GetCell(receivingRow, d).Symbol == CellSymbol.Free)
            {
                break;
            }
            run++;
        }
        return run <= receivingAgent.MaxConsecutiveDays;
    }

    private static bool RespectsMaxWeeklyHours(
        HarmonyBitmap bitmap,
        int receivingRow,
        BitmapAgent receivingAgent,
        DateOnly date,
        int dayIndex,
        Cell incomingCell)
    {
        if (receivingAgent.MaxWeeklyHours <= 0 || incomingCell.Hours <= 0)
        {
            return true;
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
            if (cell.Symbol != CellSymbol.Free)
            {
                totalHours += cell.Hours;
            }
        }

        return totalHours <= receivingAgent.MaxWeeklyHours;
    }

    private static (int Year, int Week) WeekOf(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var week = IsoCalendar.GetWeekOfYear(dt, WeekRule, FirstDayOfWeek);
        return (date.Year, week);
    }
}
