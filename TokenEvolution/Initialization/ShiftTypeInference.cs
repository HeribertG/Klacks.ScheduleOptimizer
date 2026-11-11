// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// The one place that decides whether a shift is an early, a late or a night shift (index 0/1/2).
/// Every engine reads it from here - a second opinion anywhere else silently produces plans that
/// disagree about who may work what.
/// <para>
/// The bands are Early 06:00-17:00, Late 17:01-22:59, Night 23:00-05:59 (owner decision 2026-11-11; until then
/// Early 06:00-14:59, Late 15:00-22:59). The owner's reason: these bands match what people feel is early, late
/// and night, the former three times eight hours were too mathematical. The late band begins with the minute
/// 17:01.
/// </para>
/// <para>
/// A shift is classified by its whole SPAN, not by its start alone. The worked minutes are counted per band;
/// the stronger band wins (Night beats Late beats Early), but a stay in a stronger band is tolerated when it
/// lasts at most 60 minutes AND is less than half of the shift (owner decision 2026-11-11: a few minutes into
/// another band should not flip the kind; the bands follow what people feel). The tolerance applies to the
/// night band as well. The bands are an internal planning classification: legal limits live in the settings and
/// act there as hard limits on the real clock times (Klacks is distributed internationally), so surcharges, rest
/// times and maximum hours are not affected by this classification. The end is exclusive, so the
/// last worked minute is the minute before the end. Examples: 08:00-16:00 and 07:00-15:00 are early, 09:00-18:00
/// is early (59 minutes late are tolerated), 09:00-18:02 is late (61 minutes), 13:00-19:00, 14:00-22:00 and
/// 15:00-23:00 are late, 05:00-13:00 is early (exactly 60 night minutes are tolerated), 04:59-13:00 is night,
/// 14:00-00:00 is late (60 night minutes), 15:00-00:30 is night (90 night minutes), 22:30-00:00 is night
/// (60 night minutes, but not less than half of the shift), and a 24 hour duty is night.
/// </para>
/// </summary>
public static class ShiftTypeInference
{
    /// <summary>Early shift.</summary>
    public const int EarlyIndex = 0;

    /// <summary>Late shift.</summary>
    public const int LateIndex = 1;

    /// <summary>Night shift.</summary>
    public const int NightIndex = 2;

    private const int MinutesPerDay = 24 * 60;
    private const int EarlyBandStart = 6 * 60;
    private const int LateBandStart = (17 * 60) + 1;
    private const int NightBandStart = 23 * 60;
    private const int BandToleranceMinutes = 60;
    private const int HalfDivisor = 2;

    /// <summary>
    /// Classifies a shift by its span: the worked minutes are counted per band, and the stronger band
    /// (Night, then Late) wins unless its share is tolerated - at most 60 minutes and less than half of the
    /// shift (for Late against Early: less than half of the non-night minutes). The end is exclusive, so a
    /// shift ending at 18:01 has exactly 60 late minutes (17:01 to 18:00) and stays early, one ending at 18:02
    /// has 61 and is late.
    /// An end that is not after the start means the shift runs past midnight, equal bounds included:
    /// those span a full 24 hours and therefore count as night.
    /// </summary>
    /// <param name="start">Start time of day.</param>
    /// <param name="end">End time of day, exclusive.</param>
    public static int FromSpan(TimeOnly start, TimeOnly end)
    {
        var startMinute = ToMinute(start);
        var endMinute = ToMinute(end);

        var spans = endMinute > startMinute
            ? new[] { (startMinute, endMinute), (0, 0) }
            : [(startMinute, MinutesPerDay), (0, endMinute)];

        var nightMinutes = Overlap(spans, NightBandStart, MinutesPerDay) + Overlap(spans, 0, EarlyBandStart);
        var lateMinutes = Overlap(spans, LateBandStart, NightBandStart);
        var earlyMinutes = Overlap(spans, EarlyBandStart, LateBandStart);
        var totalMinutes = nightMinutes + lateMinutes + earlyMinutes;

        if (!IsTolerated(nightMinutes, totalMinutes))
        {
            return NightIndex;
        }

        return IsTolerated(lateMinutes, lateMinutes + earlyMinutes) ? EarlyIndex : LateIndex;
    }

    /// <summary>
    /// Classifies the start instant alone, on the same bands as <see cref="FromSpan"/>: 17:00 is early,
    /// 17:01 is late, 23:00 to 05:59 is night. A bare start instant cannot know a tolerance, which needs the span.
    /// Only for callers that genuinely have no end time; a caller that does must use <see cref="FromSpan"/>,
    /// otherwise the two disagree about the same shift.
    /// </summary>
    /// <param name="start">Start time of day.</param>
    public static int FromStartTime(TimeOnly start)
    {
        var minute = ToMinute(start);

        if (minute >= NightBandStart || minute < EarlyBandStart)
        {
            return NightIndex;
        }

        return minute < LateBandStart ? EarlyIndex : LateIndex;
    }

    /// <summary>Same as <see cref="FromSpan"/> for the string times a CoreShift carries.</summary>
    /// <param name="startTime">Start time, parseable as a time of day.</param>
    /// <param name="endTime">End time, parseable as a time of day.</param>
    public static int FromSpanString(string startTime, string endTime)
    {
        if (!TimeOnly.TryParse(startTime, out var start))
        {
            return EarlyIndex;
        }

        return TimeOnly.TryParse(endTime, out var end) ? FromSpan(start, end) : FromStartTime(start);
    }

    /// <summary>Classifies the start instant of a string time; prefer <see cref="FromSpanString"/>.</summary>
    /// <param name="startTime">Start time, parseable as a time of day.</param>
    public static int FromStartTimeString(string startTime)
        => TimeOnly.TryParse(startTime, out var time) ? FromStartTime(time) : EarlyIndex;

    private static bool IsTolerated(int minutes, int wholeMinutes)
        => minutes <= BandToleranceMinutes && minutes * HalfDivisor < wholeMinutes;

    private static int Overlap((int Start, int End)[] spans, int bandStart, int bandEnd)
    {
        var minutes = 0;

        foreach (var (start, end) in spans)
        {
            if (start < end)
            {
                minutes += Math.Max(0, Math.Min(end, bandEnd) - Math.Max(start, bandStart));
            }
        }

        return minutes;
    }

    private static int ToMinute(TimeOnly time) => (time.Hour * 60) + time.Minute;
}
