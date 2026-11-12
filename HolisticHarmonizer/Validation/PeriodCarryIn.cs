// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

/// <summary>
/// What a row brings into the planned period from the days before it (BitmapInput.BoundaryAssignments): the work package
/// that runs up to the day before the period, so <see cref="RowShape"/> measures the first package of the period with
/// its full length, and the end of the last worked day before the period, for the rest before the first package.
/// </summary>
/// <param name="Days">Worked days in a row that end on the day before the period; 0 when that day is not worked</param>
/// <param name="IsMixed">True when those days carry more than one shift symbol, also on the same day</param>
/// <param name="LastSymbol">Shift symbol of the day before the period (the lowest one when that day carries several);
/// Free when <paramref name="Days"/> is 0</param>
/// <param name="LastWorkEnd">End of the last worked day before the period; default when unknown</param>
public sealed record PeriodCarryIn(int Days, bool IsMixed, CellSymbol LastSymbol, DateTime LastWorkEnd)
{
    /// <summary>Carry-in of every agent with a worked day before the period, keyed by agent id.</summary>
    /// <param name="boundaryAssignments">Works and absences on the days around the period; null when there are none</param>
    /// <param name="periodStart">First day of the planned period</param>
    public static IReadOnlyDictionary<string, PeriodCarryIn> Of(
        IReadOnlyList<BitmapAssignment>? boundaryAssignments, DateOnly periodStart)
    {
        var result = new Dictionary<string, PeriodCarryIn>(StringComparer.Ordinal);
        if (boundaryAssignments is null)
        {
            return result;
        }

        var worksBefore = boundaryAssignments
            .Where(a => a.Date < periodStart && StandInWork.IsWork(a.Symbol))
            .GroupBy(a => a.AgentId, StringComparer.Ordinal);
        foreach (var works in worksBefore)
        {
            var symbolByDate = new Dictionary<DateOnly, CellSymbol>();
            var mixedDates = new HashSet<DateOnly>();
            var lastWorkEnd = default(DateTime);
            foreach (var work in works)
            {
                if (symbolByDate.TryGetValue(work.Date, out var known) && known != work.Symbol)
                {
                    mixedDates.Add(work.Date);
                    symbolByDate[work.Date] = known < work.Symbol ? known : work.Symbol;
                }
                else
                {
                    symbolByDate[work.Date] = work.Symbol;
                }

                if (work.EndAt > lastWorkEnd)
                {
                    lastWorkEnd = work.EndAt;
                }
            }

            var dayBefore = periodStart.AddDays(-1);
            var lastSymbol = symbolByDate.GetValueOrDefault(dayBefore, CellSymbol.Free);
            var days = 0;
            var isMixed = false;
            for (var date = dayBefore; symbolByDate.TryGetValue(date, out var symbol); date = date.AddDays(-1))
            {
                days++;
                isMixed |= symbol != lastSymbol || mixedDates.Contains(date);
            }

            result[works.Key] = new PeriodCarryIn(days, isMixed, lastSymbol, lastWorkEnd);
        }

        return result;
    }
}
